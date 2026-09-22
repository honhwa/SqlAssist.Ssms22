using System;
using System.Collections.Generic;
using SqlAssist.Core.Parsing;

namespace SqlAssist.Core.Statements;

/// <summary>
/// 從模組定義找出參數的預設值，以及哪些參數寫了預設值因而呼叫時可以整個省略。
/// </summary>
/// <remarks>
/// 這一份只能從定義本文讀出來。<c>sys.parameters.has_default_value</c> 對 T-SQL 模組
/// <b>永遠是 0</b>——那一欄只對 CLR 模組有效，而中繼資料層拿得到的就只有那一欄。
/// 少了這一步，展開出來的 EXEC 會把七個參數全部列出來，而使用者根本分不出哪三個
/// 本來就不必傳，也看不到模組原本替它們準備了什麼值。
///
/// 定義是第三層資料，本來不在按鍵路徑上；但提交建議也不在按鍵路徑上，而且
/// <c>GetDetailAsync</c> 的同一次呼叫本來就會把欄位、參數與定義一起帶回來，
/// 所以這裡不多付任何一次往返。
///
/// 讀不出來就回傳空的字典：少填一個預設值只是少一點方便，猜錯卻會讓使用者
/// 在一句看不出哪裡有問題的呼叫上追半天。
/// </remarks>
public static class SqlModuleParameterDefaults
{
    private static readonly IDictionary<string, string> None =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>回傳有預設值的參數名稱（含 <c>@</c> 前綴）。</summary>
    /// <param name="definition">模組定義本文；加密模組為 null。</param>
    public static ISet<string> Find(string? definition)
    {
        var defaults = FindValues(definition);

        if (defaults.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(defaults.Keys, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 回傳每個有預設值的參數其預設值原文，例如
    /// <c>@Days</c> → <c>7</c>、<c>@Note</c> → <c>NULL</c>、<c>@At</c> → <c>GETDATE()</c>。
    /// </summary>
    /// <remarks>
    /// 值取的是<b>原文</b>而不是型別對應的預留值：模組開發者寫進去的東西比型別本身
    /// 精確得多，<c>@Days INT = 7</c> 的 7 不可能從 <c>INT</c> 推出來。
    ///
    /// 值的結束位置由下一個同層的逗號或清單結尾決定，取的是<b>原文切片</b>而不是
    /// 詞法單元重組：重組會把 <c>'a''b'</c> 或 <c>DEFAULT</c> 這類寫法改得面目全非，
    /// 而這一段是要原樣填進使用者正在寫的那句話裡的。
    /// </remarks>
    public static IDictionary<string, string> FindValues(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return None;
        }

        var tokens = SqlTokenizer.Tokenize(definition!);
        var start = FindParameterListStart(tokens);

        if (start < 0)
        {
            return None;
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

            if (token.Kind == SqlTokenKind.Variable && TryReadDefault(definition!, tokens, index, out var value))
            {
                values[token.Value] = value;
            }
        }

        return values.Count == 0 ? None : values;
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
    /// 這個參數名稱後面接的是不是預設值，是的話把值切出來。
    /// </summary>
    /// <remarks>
    /// 往後找到<b>同一層</b>的第一個 <c>=</c> 或 <c>,</c> 就有答案：型別本身寫不出
    /// 這兩個符號，而 <c>decimal(18,2)</c> 的逗號在括號裡，深度不同所以不會誤判。
    ///
    /// 有 <c>=</c> 時，值從那個等號的下一個詞法單元起算，到同層的下一個 <c>,</c>
    /// 之前結束。取原文切片而不重組詞法單元：重組會把 <c>N'a b'</c> 的空白、
    /// <c>'a''b'</c> 的跳脫字元與 <c>-1</c> 的負號寫成別的樣子。
    /// </remarks>
    private static bool TryReadDefault(
        string definition,
        IReadOnlyList<SqlToken> tokens,
        int start,
        out string value)
    {
        value = string.Empty;
        var depth = DepthAt(tokens, start);
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
                    break;
                }

                continue;
            }

            if (current != depth)
            {
                continue;
            }

            if (token.IsPunctuation(","))
            {
                break;
            }

            if (token.Kind == SqlTokenKind.Operator && string.Equals(token.Value, "=", StringComparison.Ordinal))
            {
                equals = index;
                continue;
            }

            if (token.IsKeyword("AS") || token.IsKeyword("RETURNS"))
            {
                break;
            }
        }

        if (equals < 0 || equals + 1 >= tokens.Count)
        {
            return false;
        }

        // 值可以再包一層括號（(0.05)），也可能自己就是用逗號分隔的函式呼叫
        // （CONVERT(varchar(10), GETDATE(), 120)）——那兩層深度不同，所以往後掃的
        // 深度必須從「值的第一個詞法單元」重新起算，而不是沿用參數那一層。
        //
        // 收尾的 ')' 要算進值裡：`= GETDATE()` 與 `= (0.05)` 的最後一個字元都是
        // 右括號，把它排除掉會切出 `GETDATE` 與 `(0.05` 這種當場語法錯誤的值——
        // 而那個錯誤會出現在使用者展開之後的宣告行上，看起來像展開壞了。
        current = depth;
        var end = -1;

        for (var index = equals + 1; index < tokens.Count; index++)
        {
            var token = tokens[index];

            if (token.IsPunctuation("("))
            {
                current++;
                end = index;
                continue;
            }

            if (token.IsPunctuation(")"))
            {
                // 收掉剛好回到參數那一層時，這個右括號仍然屬於值（函式呼叫的尾巴）；
                // 再少一層才代表值的括號已經超出參數清單，值到此為止。
                if (current == depth)
                {
                    end = index;
                    break;
                }

                current--;
                end = index;
                continue;
            }

            if (current == depth && token.IsPunctuation(","))
            {
                break;
            }

            if (current == depth && (token.IsKeyword("AS") || token.IsKeyword("RETURNS")))
            {
                break;
            }

            end = index;
        }

        if (end < equals + 1)
        {
            return false;
        }

        var from = tokens[equals + 1].Start;
        var to = tokens[end].End;
        value = definition.Substring(from, to - from).Trim();
        return value.Length > 0;
    }

    /// <summary>這個詞法單元所在的括號深度。</summary>
    private static int DepthAt(IReadOnlyList<SqlToken> tokens, int position)
    {
        var depth = 0;

        for (var index = 0; index < position; index++)
        {
            if (tokens[index].IsPunctuation("("))
            {
                depth++;
            }
            else if (tokens[index].IsPunctuation(")"))
            {
                depth--;
            }
        }

        return depth;
    }
}
