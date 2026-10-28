using System;
using System.Collections.Generic;
using SqlAssist.Core.Keywords;
using SqlAssist.Core.Parsing;

namespace SqlAssist.Core.Rewriting;

/// <summary>
/// 掃出「這個位置上的名稱是一個物件名稱」的地方。
/// </summary>
/// <remarks>
/// T-SQL 沒有標記「這個名稱是表、是檢視、還是預存程序」，一件事只能由前面的字決定：
/// <c>FROM</c>／<c>JOIN</c> 後面是資料來源，<c>EXEC</c> 後面是模組，
/// <c>CREATE TABLE</c> 後面是正要建立的那張表。這些字寫成第二份清單的症狀是
/// 兩份各自增補，而補得比較少的那一份看起來只像「漏了幾處」，使用者分不出
/// 那是設計還是壞掉。
///
/// 這裡只回報<b>位置</b>，不回報要補什麼：<c>dbo.</c> 補在哪裡由名稱本來怎麼寫
/// 決定（見 <see cref="SqlSchemaQualification"/>）。
///
/// 掃描不設深度限制，巢狀子查詢的 <c>FROM</c> 一樣會走到——整份指令碼一起補
/// 才是使用者的預期，只補外層會留下半份。
/// </remarks>
public static class SqlNameSlotScanner
{
    /// <summary>這個名稱是從哪一種文法位置來的。</summary>
    /// <remarks>
    /// 分種類不是為了報告好看：<c>sp_</c>／<c>xp_</c> 的例外只適用於模組，
    /// DML 目標才需要問別名名冊，其餘位置問了只是白花時間。
    /// </remarks>
    public enum SlotOrigin
    {
        /// <summary>資料來源：<c>FROM</c>／<c>JOIN</c>／<c>USING</c> 這一族。</summary>
        Source,

        /// <summary>模組：<c>EXEC</c>／<c>EXECUTE</c> 後面那一個。</summary>
        Module,

        /// <summary>DML 的目標：敘述開頭的 <c>UPDATE</c>／<c>DELETE</c>／<c>MERGE</c> 後面那一個。</summary>
        Target,

        /// <summary>DDL 的物件名稱：<c>TABLE</c>／<c>VIEW</c>／<c>PROC</c>／<c>FUNCTION</c> 後面那一個。</summary>
        Object
    }

    /// <summary>一個名稱位置。</summary>
    public readonly struct Slot
    {
        public Slot(int tokenIndex, SlotOrigin origin)
        {
            TokenIndex = tokenIndex;
            Origin = origin;
        }

        /// <summary>名稱<b>最後一段</b>的詞法單元索引。</summary>
        /// <remarks>
        /// 是<b>那一段</b>的索引，中間每一段都在 <see cref="SqlObjectPath"/> 那一層處理。
        /// </remarks>
        public int TokenIndex { get; }

        public SlotOrigin Origin { get; }
    }

    /// <summary>後面接資料來源，而且可以再接一個（逗號清單）的關鍵字。</summary>
    private static readonly HashSet<string> ListSourceKeywords =
        new(StringComparer.OrdinalIgnoreCase) { "FROM" };

    /// <summary>後面接一個資料來源的關鍵字。</summary>
    /// <remarks>
    /// 與 <c>SqlScopeAnalyzer.SourceKeywords</c> 刻意各留一份：那一份回答的是
    /// 「欄位建議要去哪裡找資料來源」，因此沒有 <c>REFERENCES</c> 與 DDL 的物件關鍵字；
    /// 這裡少一個就是少補一處，不是少一份建議。
    /// </remarks>
    private static readonly HashSet<string> SourceKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "FROM", "JOIN", "APPLY", "USING", "INTO", "REFERENCES"
        };

    /// <summary>DML 的目標關鍵字；只有敘述開頭那一個才接物件名稱。</summary>
    /// <remarks>
    /// 不加這一條限制時，<c>ON DELETE CASCADE</c> 與 <c>ON UPDATE NO ACTION</c> 的
    /// <c>CASCADE</c> 會被讀成一個名稱而補成 <c>dbo.CASCADE</c>——外鍵的 DDL 幾乎都寫成
    /// 一行，所以那個錯字看起來完全合理。
    /// </remarks>
    private static readonly HashSet<string> TargetKeywords =
        new(StringComparer.OrdinalIgnoreCase) { "UPDATE", "DELETE", "MERGE" };

    /// <summary>DDL 的物件關鍵字；後面直接接物件名稱。</summary>
    /// <remarks>
    /// <c>DECLARE @t TABLE (…)</c> 不必另外排除：後面接的是左括號，形狀自己就擋掉了。
    /// </remarks>
    private static readonly HashSet<string> ObjectKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "TABLE", "VIEW", "PROCEDURE", "PROC", "FUNCTION"
        };

    /// <summary>掃出這份文字裡所有可以補結構描述的名稱位置。</summary>
    /// <remarks>同一個位置只回報一次；順序是它在文字裡出現的順序。</remarks>
    public static IReadOnlyList<Slot> Find(IReadOnlyList<SqlToken> tokens)
    {
        if (tokens is null)
        {
            throw new ArgumentNullException(nameof(tokens));
        }

        var slots = new List<Slot>();
        Walk(tokens, slots, aliases: null);
        return slots;
    }

    /// <summary>掃出這份文字裡所有被當成別名用過的名稱。</summary>
    /// <remarks>
    /// 用途是擋掉「其實是別名」的假物件名稱：<c>UPDATE a SET … FROM dbo.Loan a</c> 的
    /// <c>a</c> 不是一張叫 <c>a</c> 的表，補成 <c>dbo.a</c> 會讓那句 UPDATE 指到別的東西。
    ///
    /// 與 <c>SqlScopeAnalyzer.RemoveAliasReferences</c> 的判斷條件相同（單段裸名，
    /// 而且同一段文字裡有人拿這個名字當別名），但輸入不同：那一份吃的是已經解析好的
    /// 資料來源，這一份只有詞法單元——整份指令碼一起補時沒有「目前的敘述」可用。
    /// 名稱重複不會出錯，因此寧可多收：多收一個名字只是少補一處。
    /// </remarks>
    public static HashSet<string> CollectAliases(IReadOnlyList<SqlToken> tokens)
    {
        if (tokens is null)
        {
            throw new ArgumentNullException(nameof(tokens));
        }

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(tokens, slots: null, aliases);
        return aliases;
    }

    /// <remarks>
    /// 主迴圈<b>不改動索引</b>：改了的話，<c>FROM (SELECT … FROM x) s</c> 那種寫法
    /// 會在讀第一個來源時把整個子查詢跳過去，裡面的 <c>FROM</c> 就再也走不到。
    /// </remarks>
    private static void Walk(
        IReadOnlyList<SqlToken> tokens,
        List<Slot>? slots,
        HashSet<string>? aliases)
    {
        var seen = slots is null ? null : new HashSet<int>();

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];

            if (token.Kind != SqlTokenKind.Identifier || token.IsQuoted)
            {
                continue;
            }

            if (ListSourceKeywords.Contains(token.Value))
            {
                ReadSourceRun(tokens, index + 1, slots, seen, aliases);
                continue;
            }

            if (SourceKeywords.Contains(token.Value))
            {
                ReadSource(tokens, index + 1, slots, seen, aliases);
                continue;
            }

            if (ObjectKeywords.Contains(token.Value))
            {
                AddName(tokens, index + 1, SlotOrigin.Object, slots, seen);
                continue;
            }

            // ON 絕大多數時候是 JOIN 條件，只有 CREATE INDEX／TRIGGER／ALTER INDEX
            // 那一族後面接的是資料表。判斷沿用既有那一份，不在這裡重寫一次。
            if (token.IsKeyword("ON") && SqlDdlTarget.IsDataSourceOn(tokens, index))
            {
                AddName(tokens, index + 1, SlotOrigin.Source, slots, seen);
                continue;
            }

            if (token.IsKeyword("EXEC") || token.IsKeyword("EXECUTE"))
            {
                AddModule(tokens, index + 1, slots, seen);
                continue;
            }

            if (TargetKeywords.Contains(token.Value) && StartsStatement(tokens, index))
            {
                var target = SkipTopClause(tokens, index + 1);

                // MERGE INTO t 與 MERGE t 是同一個意思。
                if (target < tokens.Count && tokens[target].IsKeyword("INTO"))
                {
                    target++;
                }

                AddName(tokens, target, SlotOrigin.Target, slots, seen);
            }
        }
    }

    /// <summary>讀一串逗號分隔的資料來源。</summary>
    private static void ReadSourceRun(
        IReadOnlyList<SqlToken> tokens,
        int index,
        List<Slot>? slots,
        HashSet<int>? seen,
        HashSet<string>? aliases)
    {
        while (index < tokens.Count)
        {
            var after = ReadSource(tokens, index, slots, seen, aliases);

            // 讀不到來源時停在原地，才不會把後面的字一個個當成下一個來源吃掉。
            if (after <= index)
            {
                return;
            }

            if (after >= tokens.Count || !tokens[after].IsPunctuation(","))
            {
                return;
            }

            index = after + 1;
        }
    }

    /// <summary>讀一個資料來源（含別名），回傳它之後的位置。</summary>
    /// <remarks>
    /// 衍生資料表（<c>FROM (SELECT …) s</c>）整組跳過：它沒有名稱可補，
    /// 但它後面的別名要收，否則同一個名字出現在別處時會被當成物件名稱。
    /// </remarks>
    private static int ReadSource(
        IReadOnlyList<SqlToken> tokens,
        int index,
        List<Slot>? slots,
        HashSet<int>? seen,
        HashSet<string>? aliases)
    {
        if (index >= tokens.Count)
        {
            return index;
        }

        int after;

        if (tokens[index].IsPunctuation("("))
        {
            after = SqlTokenNavigator.SkipParenthesised(tokens, index, tokens.Count);
        }
        else
        {
            // 開頭連續點號也算（FROM ..Loan）——那一段點號本身就是「結構描述從缺」，
            // 而它在這裡若被當成「不是來源」跳過，那個名稱就永遠補不到。
            if (tokens[index].Kind != SqlTokenKind.Identifier && !tokens[index].IsPunctuation("."))
            {
                return index;
            }

            var last = LastSegmentIndex(tokens, index);
            Add(tokens, last, SlotOrigin.Source, slots, seen);
            after = last + 1;
        }

        var alias = after;

        if (alias < tokens.Count && tokens[alias].IsKeyword("AS"))
        {
            alias++;
        }

        if (alias < tokens.Count && IsAliasToken(tokens[alias]))
        {
            aliases?.Add(tokens[alias].Value);
            return alias + 1;
        }

        return after;
    }

    /// <summary>讀 <c>EXEC</c> 後面那一個模組名稱。</summary>
    /// <remarks>
    /// <c>EXEC @rc = dbo.Proc</c> 的傳回值寫法要一起跳過，否則那個名稱永遠補不到。
    /// </remarks>
    private static void AddModule(
        IReadOnlyList<SqlToken> tokens,
        int index,
        List<Slot>? slots,
        HashSet<int>? seen)
    {
        if (index + 1 < tokens.Count
            && tokens[index].Kind == SqlTokenKind.Variable
            && tokens[index + 1].Value == "=")
        {
            index += 2;
        }

        AddName(tokens, index, SlotOrigin.Module, slots, seen);
    }

    /// <summary>從名稱鏈的開頭讀到最後一段，把最後那一段記成一個位置。</summary>
    private static void AddName(
        IReadOnlyList<SqlToken> tokens,
        int index,
        SlotOrigin origin,
        List<Slot>? slots,
        HashSet<int>? seen)
    {
        if (index >= tokens.Count)
        {
            return;
        }

        Add(tokens, LastSegmentIndex(tokens, index), origin, slots, seen);
    }

    /// <remarks>
    /// 關鍵字與型別名一律不算名稱：沒加引號的 <c>FROM</c>、<c>TABLE</c> 這些字之所以
    /// 會走到這裡，是因為它跟在另一個關鍵字後面（<c>DELETE FROM</c>、<c>TRUNCATE TABLE</c>），
    /// 而不是那裡真的有一個叫這個名字的物件。
    ///
    /// 不必另外檢查名稱鏈的開頭是不是識別字：鏈是照「點號後面接識別字」走的，
    /// 開頭不是識別字時最後一段只會是開頭自己或一個標點，兩種都在這裡被擋掉。
    /// </remarks>
    private static void Add(
        IReadOnlyList<SqlToken> tokens,
        int last,
        SlotOrigin origin,
        List<Slot>? slots,
        HashSet<int>? seen)
    {
        if (last >= tokens.Count || slots is null)
        {
            return;
        }

        var name = tokens[last];

        if (name.Kind != SqlTokenKind.Identifier
            || (!name.IsQuoted && SqlKeywordCatalog.IsKeywordOrDataType(name.Value)))
        {
            return;
        }

        if (seen is not null && !seen.Add(last))
        {
            return;
        }

        slots.Add(new Slot(last, origin));
    }

    /// <summary>
    /// 名稱鏈的最後一段（物件名稱本身）。
    /// </summary>
    /// <remarks>
    /// 中間的空段要跳過（<c>LibArchive..Loan</c>），否則最後一段會停在
    /// <c>LibArchive</c>，補出來的東西就成了 <c>dbo.LibArchive..Loan</c>。
    ///
    /// 只認「點號後面」的下一段，不認緊接著的識別字：<c>FROM t a</c> 的 <c>a</c>
    /// 是別名而不是第四段。
    /// </remarks>
    private static int LastSegmentIndex(IReadOnlyList<SqlToken> tokens, int index)
    {
        var last = index;
        var cursor = index + 1;

        while (cursor < tokens.Count && tokens[cursor].IsPunctuation("."))
        {
            while (cursor < tokens.Count && tokens[cursor].IsPunctuation("."))
            {
                cursor++;
            }

            if (cursor >= tokens.Count || tokens[cursor].Kind != SqlTokenKind.Identifier)
            {
                break;
            }

            last = cursor;
            cursor++;
        }

        return last;
    }

    /// <summary>這個詞法單元可不可以當別名。</summary>
    private static bool IsAliasToken(SqlToken token)
    {
        return token.Kind == SqlTokenKind.Identifier
            && (token.IsQuoted || !SqlKeywordCatalog.IsKeywordOrDataType(token.Value));
    }

    /// <summary>
    /// 這個關鍵字是不是某一個敘述的第一個字。
    /// </summary>
    /// <remarks>
    /// 只為 DML 目標而問。<c>ON DELETE CASCADE</c> 的 <c>DELETE</c> 前面是 <c>ON</c>，
    /// 在這裡就擋掉了；而真正開頭的 <c>UPDATE</c> 前面只會是分號、批次分隔、
    /// 右括號（<c>IF EXISTS (…) UPDATE …</c>）或 <c>BEGIN</c>／<c>ELSE</c> 這一族。
    ///
    /// 認不出來時回 false（不補）：漏掉一處只會被當成沒補到，補錯一處會讓那句 SQL
    /// 指到別的物件。
    /// </remarks>
    private static bool StartsStatement(IReadOnlyList<SqlToken> tokens, int index)
    {
        for (var cursor = index - 1; cursor >= 0; cursor--)
        {
            var token = tokens[cursor];

            if (token.Kind == SqlTokenKind.Comment)
            {
                continue;
            }

            return token.IsPunctuation(";")
                || token.IsPunctuation(")")
                || token.IsKeyword("GO")
                || token.IsKeyword("BEGIN")
                || token.IsKeyword("ELSE")
                || token.IsKeyword("END");
        }

        return true;
    }

    /// <summary>跳過 <c>TOP n</c>／<c>TOP (n)</c>／<c>TOP n PERCENT</c> 這一段。</summary>
    private static int SkipTopClause(IReadOnlyList<SqlToken> tokens, int index)
    {
        if (index >= tokens.Count || !tokens[index].IsKeyword("TOP"))
        {
            return index;
        }

        index++;

        if (index < tokens.Count && tokens[index].IsPunctuation("("))
        {
            index = SqlTokenNavigator.SkipParenthesised(tokens, index, tokens.Count);
        }
        else if (index < tokens.Count
            && (tokens[index].Kind == SqlTokenKind.Number || tokens[index].Kind == SqlTokenKind.Variable))
        {
            index++;
        }

        if (index < tokens.Count && tokens[index].IsKeyword("PERCENT"))
        {
            index++;
        }

        return index;
    }
}
