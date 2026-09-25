using System;
using System.Collections.Generic;
using SqlAssist.Core.Parsing;

namespace SqlAssist.Core.Statements;

/// <summary>
/// 從模組定義找出哪些參數寫了預設值，因而呼叫時可以整個省略。
/// </summary>
/// <remarks>
/// 這一份只能從定義本文讀出來。<c>sys.parameters.has_default_value</c> 對 T-SQL 模組
/// <b>永遠是 0</b>——那一欄只對 CLR 模組有效，而中繼資料層拿得到的就只有那一欄。
/// 少了這一步，展開出來的 EXEC 會把七個參數全部列出來，而使用者根本分不出哪三個
/// 本來就不必傳。
///
/// 定義是第三層資料，本來不在按鍵路徑上；但提交建議也不在按鍵路徑上，而且
/// <c>GetDetailAsync</c> 的同一次呼叫本來就會把欄位、參數與定義一起帶回來，
/// 所以這裡不多付任何一次往返。
///
/// 讀不出來就回傳空集合：少標幾個「選擇性」只是少一點資訊，猜錯卻會讓使用者
/// 刪掉一個其實必填的參數。
///
/// <see cref="Resolve"/> 是同一趟掃描的另一個出口，多帶回<b>預設值本身</b>；
/// 展開成 <c>DECLARE</c> 時要拿它當初始值，見
/// <see cref="SqlProcedureCallText"/>。
/// </remarks>
public static class SqlModuleParameterDefaults
{
    private static readonly ISet<string> None =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> NoneValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>回傳有預設值的參數名稱（含 <c>@</c> 前綴）。</summary>
    /// <param name="definition">模組定義本文；加密模組為 null。</param>
    public static ISet<string> Find(string? definition)
    {
        var values = Resolve(definition);

        if (values.Count == 0)
        {
            return None;
        }

        return new HashSet<string>(values.Keys, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 回傳「參數名稱 → 預設值字面值」，鍵含 <c>@</c> 前綴。
    /// </summary>
    /// <param name="definition">模組定義本文；加密模組為 null。</param>
    /// <remarks>
    /// 只收<b>可以原樣嵌進 DECLARE 的字面值</b>：數字、字串、<c>NULL</c>、
    /// 二進位常值與不會被求值成運算式的簡單識別字（<c>GETDATE</c>、<c>ON</c>…）。
    ///
    /// 帶運算子的預設值（<c>@b INT = 1 + 2</c>）刻意<b>不收</b>。收了的話，
    /// <c>DECLARE @b INT = 1</c> 會少掉後半段——那是一個語法正確卻算錯的值，
    /// 比留給使用者自己填糟糕得多。少收一個只是退回依型別的預留值。
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Resolve(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return NoneValues;
        }

        var tokens = SqlTokenizer.Tokenize(definition!);
        var start = FindParameterListStart(tokens);

        if (start < 0)
        {
            return NoneValues;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var depth = 0;

        for (var index = start; index < tokens.Count; index++)
        {
            var token = tokens[index];

            if (token.IsPunctuation("("))
            {
                depth++;
                continue;
            }

            if (token.IsPunctuation(")"))
            {
                // 函式的參數清單自己就包在一對括號裡，收掉那一對就代表清單結束了。
                if (--depth < 0)
                {
                    break;
                }

                continue;
            }

            // 程序的參數清單以 AS 收尾，函式以 RETURNS。兩者都不可能出現在型別中間。
            if (depth == 0 && (token.IsKeyword("AS") || token.IsKeyword("RETURNS")))
            {
                break;
            }

            if (token.Kind != SqlTokenKind.Variable)
            {
                continue;
            }

            if (FindDefaultValue(tokens, index, depth) is { } value)
            {
                values[token.Value] = value;
            }
        }

        return values.Count == 0 ? NoneValues : values;
    }

    /// <summary>
    /// 參數清單從模組名稱之後開始。
    /// </summary>
    /// <remarks>
    /// 認的是 <c>PROCEDURE</c>／<c>PROC</c>／<c>FUNCTION</c> 這個字本身，前面是
    /// <c>CREATE</c> 還是 <c>CREATE OR ALTER</c> 都不必看——那三個字在定義開頭
    /// 只會出現一次，而它後面接的必定是名稱。
    /// </remarks>
    private static int FindParameterListStart(IReadOnlyList<SqlToken> tokens)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].IsKeyword("PROCEDURE") ||
                tokens[index].IsKeyword("PROC") ||
                tokens[index].IsKeyword("FUNCTION"))
            {
                return index + 1;
            }
        }

        return -1;
    }

    /// <summary>
    /// 取出預設值的字面值；沒有預設值或前面接的不是預設值時回傳 null。
    /// </summary>
    /// <remarks>
    /// 先往後找到<b>同一層</b>的第一個 <c>=</c> 或 <c>,</c> 才有答案：型別本身寫不出
    /// 這兩個符號，而 <c>decimal(18,2)</c> 的逗號在括號裡，深度不同所以不會誤判。
    /// 找到等號之後繼續往下走完那一個值，走完必須停在<b>同一層</b>的逗號、
    /// <c>AS</c> 或清單結尾，否則代表這個值是由好幾個詞元拼起來的運算式，寧可整個放棄。
    /// </remarks>
    private static string? FindDefaultValue(IReadOnlyList<SqlToken> tokens, int start, int depth)
    {
        var current = depth;
        var equals = -1;

        for (var index = start + 1; index < tokens.Count; index++)
        {
            var token = tokens[index];

            if (token.IsPunctuation("("))
            {
                current++;
                continue;
            }

            if (token.IsPunctuation(")"))
            {
                if (--current < depth)
                {
                    return null;
                }

                continue;
            }

            if (current != depth)
            {
                continue;
            }

            if (token.IsPunctuation(","))
            {
                return null;
            }

            if (token.Kind == SqlTokenKind.Operator && string.Equals(token.Value, "=", StringComparison.Ordinal))
            {
                equals = index;
                break;
            }

            if (token.IsKeyword("AS") || token.IsKeyword("RETURNS"))
            {
                return null;
            }
        }

        if (equals < 0 || equals + 1 >= tokens.Count)
        {
            return null;
        }

        var value = tokens[equals + 1];

        // 預設值開頭是運算子、標點或註解：那是運算式，不是字面值。
        if (value.Kind != SqlTokenKind.Number &&
            value.Kind != SqlTokenKind.String &&
            value.Kind != SqlTokenKind.Variable &&
            value.Kind != SqlTokenKind.Identifier)
        {
            return null;
        }

        // 數字、字串與變數一定只有自己一個詞元——後面再接東西就是運算式
        // （1 + 2、'x' + 'y'），收了會少掉後半段，變成一句語法正確卻算錯的宣告。
        //
        // 識別字多一種合法長相：帶一對空括號的函式呼叫（GETDATE()），
        // 而 Token.Text 只有名稱本身，少了那對括號 DECLARE @d DATETIME = GETDATE
        // 是語法錯誤，所以要連括號一起帶回去。
        var following = equals + 2;

        if (value.Kind == SqlTokenKind.Identifier &&
            following + 1 < tokens.Count &&
            tokens[following].IsPunctuation("(") &&
            tokens[following + 1].IsPunctuation(")"))
        {
            // GETDATE() + 1 是運算式，收成 GETDATE() 會少掉後半段。
            var afterCall = following + 2;

            if (afterCall >= tokens.Count)
            {
                return ValueWithParentheses(tokens, equals + 1);
            }

            var trailing = tokens[afterCall];

            return trailing.IsPunctuation(",") ||
                   trailing.IsPunctuation(")") ||
                   trailing.IsKeyword("AS") ||
                   trailing.IsKeyword("RETURNS")
                ? ValueWithParentheses(tokens, equals + 1)
                : null;
        }

        if (following >= tokens.Count)
        {
            return value.Text;
        }

        // NULL 在詞法上是識別字而不是關鍵字，因此與 GETDATE 走同一條。
        var next = tokens[following];

        return next.IsPunctuation(",") ||
               next.IsPunctuation(")") ||
               next.IsKeyword("AS") ||
               next.IsKeyword("RETURNS")
            ? value.Text
            : null;
    }

    /// <summary>
    /// 從原始文字切出「識別字加一對空括號」，例如 <c>GETDATE()</c>。
    /// </summary>
    /// <remarks>
    /// 用 <c>Start</c> 與 <c>End</c> 切原文而不是自己接兩個括號：原文裡可能有空白
    /// （<c>GETDATE ()</c>），照抄才不會改掉使用者寫的樣子。
    /// </remarks>
    private static string ValueWithParentheses(IReadOnlyList<SqlToken> tokens, int start)
    {
        var end = tokens[start + 2];
        return tokens[start].Text + tokens[start + 1].Text + end.Text;
    }
}
