using System;
using System.Collections.Generic;
using SqlAssist.Core.Keywords;
using SqlAssist.Core.Parsing;

namespace SqlAssist.Core.Rewriting;

/// <summary>
/// 把沒帶結構描述的物件名稱補成 <c>dbo.</c>，並把 <c>db..obj</c> 這種空的中間段正規化。
/// </summary>
/// <remarks>
/// 補的是 <c>dbo.</c> 而不是「目前使用者的預設結構描述」：只看文字判斷不出預設結構描述
/// 是什麼，而中繼資料要連線才有。這條命令的用法是「把自己寫的指令碼整理成統一的寫法」，
/// 那些指令碼的物件本來就掛在 <c>dbo</c>；不是 <c>dbo</c> 的物件寫在別人的結構描述下時
/// 本來就會帶限定字，不會落到這條路上。
///
/// <b>刻意不做成存檔時自動整理。</b>判斷錯了就是改壞使用者的 SQL，而存檔是使用者
/// 最不希望被打斷的動作；做成右鍵命令之後，改壞的責任回到按下它的人身上，
/// 而且改完的結果就在眼前。
/// </remarks>
public static class SqlSchemaQualification
{
    /// <summary>系統程序的保留前置字。</summary>
    /// <remarks>
    /// <c>sp_</c> 與 <c>xp_</c> 兩族住在 <c>master</c> 的 <c>sys</c> 結構描述，
    /// 補成 <c>dbo.sp_help</c> 會直接找不到——那比不補更糟。
    /// </remarks>
    private static readonly string[] SystemModulePrefixes = { "sp_", "xp_" };

    /// <summary>資料來源函式：它們不是使用者自訂的資料表值函式。</summary>
    /// <remarks>
    /// 與 <c>SqlScopeAnalyzer.RowsetFunctions</c> 刻意各留一份：那一份回答的是
    /// 「欄位建議要去哪裡問資料行」，因此不含 <c>OPENXML</c>（它在文法上自成一條）；
    /// 這裡漏掉它就會補出一個不存在的 <c>dbo.OPENXML</c>。
    ///
    /// 清單以外的內建函式（<c>OPENJSON</c>、<c>STRING_SPLIT</c> 之類）由
    /// <see cref="SqlFunctionCatalog"/> 回答。
    /// </remarks>
    private static readonly HashSet<string> DataSourceFunctions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "OPENROWSET", "OPENQUERY", "OPENDATASOURCE", "OPENXML"
        };

    /// <summary>補齊這份文字裡的結構描述。</summary>
    /// <param name="sql">要處理的整段文字。</param>
    /// <param name="caretPosition">游標在 <paramref name="sql"/> 裡的位置；負值代表不處理游標。</param>
    /// <returns>換完的文字與動到的處數。</returns>
    public static SqlTextRewriteResult Qualify(string sql, int caretPosition = -1)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var tokens = SqlTokenizer.Tokenize(sql);

        if (tokens.Count == 0)
        {
            return new SqlTextRewriteResult(sql, 0, caretPosition);
        }

        var aliases = SqlNameSlotScanner.CollectAliases(tokens);

        // CTE 名稱只存在於這份文字裡，補上 dbo. 會讓 FROM cte 找不到東西。
        // 名冊與欄位建議共用，不在這裡再掃一次。
        var cteNames = new HashSet<string>(
            new SqlColumnSourceResolver(sql, tokens).CommonTableExpressionNames,
            StringComparer.OrdinalIgnoreCase);

        var edits = new List<SqlTextEdit>();

        foreach (var slot in SqlNameSlotScanner.Find(tokens))
        {
            var name = tokens[slot.TokenIndex];

            if (name.Value.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (cteNames.Contains(name.Value))
            {
                continue;
            }

            var dots = CountDotsBefore(sql, name.Start);

            if (dots >= 2)
            {
                // db..obj 與 srv...obj：把名稱前面那一段點號換成結構描述。
                var runStart = name.Start - dots;
                edits.Add(new SqlTextEdit(
                    runStart,
                    dots,
                    HasSegmentBefore(sql, runStart) ? ".dbo." : "dbo."));

                continue;
            }

            if (dots == 1)
            {
                continue;
            }

            // 名稱前面有點號，只是中間夾了空白（dbo . T）。形狀認不出來就別動它。
            if (slot.TokenIndex > 0 && tokens[slot.TokenIndex - 1].IsPunctuation("."))
            {
                continue;
            }

            if (slot.Origin == SqlNameSlotScanner.SlotOrigin.Module && IsSystemModule(name.Value))
            {
                continue;
            }

            if (IsAlias(slot, name.Value, aliases)
                || IsDataSourceFunction(tokens, slot.TokenIndex, name))
            {
                continue;
            }

            edits.Add(new SqlTextEdit(name.Start, 0, "dbo."));
        }

        return SqlTextRewrite.Apply(edits, sql, caretPosition);
    }

    /// <summary>被當成別名用過的名稱不補。</summary>
    /// <remarks>
    /// 只有可能是別名的那兩種位置才問：DDL 的物件名稱與 <c>EXEC</c> 的模組名稱都不會是
    /// 別名，問了只會讓「有人剛好在別處用了同名的別名」變成少補一處的藉口。
    /// </remarks>
    private static bool IsAlias(
        SqlNameSlotScanner.Slot slot,
        string name,
        HashSet<string> aliases)
    {
        if (aliases.Count == 0)
        {
            return false;
        }

        return slot.Origin != SqlNameSlotScanner.SlotOrigin.Object
            && slot.Origin != SqlNameSlotScanner.SlotOrigin.Module
            && aliases.Contains(name);
    }

    private static bool IsSystemModule(string name)
    {
        foreach (var prefix in SystemModulePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>後面接左括號的內建函式不是使用者自訂的資料表值函式。</summary>
    private static bool IsDataSourceFunction(IReadOnlyList<SqlToken> tokens, int index, SqlToken name)
    {
        if (name.IsQuoted
            || index + 1 >= tokens.Count
            || !tokens[index + 1].IsPunctuation("("))
        {
            return false;
        }

        return DataSourceFunctions.Contains(name.Value)
            || SqlFunctionCatalog.TryGetSignature(name.Value, out _);
    }

    /// <summary>名稱前面緊接著幾個點號。</summary>
    private static int CountDotsBefore(string sql, int start)
    {
        var dots = 0;

        while (start - dots - 1 >= 0 && sql[start - dots - 1] == '.')
        {
            dots++;
        }

        return dots;
    }

    /// <summary>這一段點號前面還有一段名稱（<c>db..obj</c> 的 <c>db</c>）。</summary>
    private static bool HasSegmentBefore(string sql, int dotRunStart)
    {
        return dotRunStart > 0 && IsNameCharacter(sql[dotRunStart - 1]);
    }

    private static bool IsNameCharacter(char value)
    {
        return char.IsLetterOrDigit(value)
            || value == '_'
            || value == '#'
            || value == '$'
            || value == '@'
            || value == ']'
            || value == '"';
    }
}
