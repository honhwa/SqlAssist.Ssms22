using System;
using System.Collections.Generic;
using System.Text;
using SqlAssist.Core.Parsing;

namespace SqlAssist.Core.Pairing;

/// <summary>
/// 選取一段文字之後打 <c>BEGIN</c>，要不要把它包成一組區塊，以及包起來長什麼樣。
/// </summary>
/// <remarks>
/// 這一條與單字元配對（<see cref="SqlAutoPairAnalyzer"/>）分開，因為觸發時機不同：
/// 單字元配對在按鍵<b>當下</b>就有答案——按的是 <c>'</c> 就是要一對引號；
/// 區塊要等 <c>BEGIN</c> 的每個字元都進了緩衝區之後才問得出來，
/// 那時才分得出 <c>BEGIN</c> 與 <c>BEGIN TRY</c>，也才認得出 <c>BEGIN TRAN</c>
/// 這種不該包夾的開頭。因此<see cref="CloserFor"/> 問的是<b>已經寫進緩衝區</b>的游標前文字。
///
/// 版面也歸這裡管：開頭那一行留在使用者原本的位置，內容整段往右縮一層，
/// 結尾那幾行另起新行。縮排的單位由呼叫端依編輯器設定給定，所以同一個骨架
/// 在兩個空白與四個空白、空白與定位字元之下都排得對。
/// </remarks>
public static class SqlBlockPairAnalyzer
{
    /// <summary>認得的骨架，長的排在前面。</summary>
    /// <remarks>
    /// 順序有意義：<c>BEGIN TRY</c> 的尾巴也是 <c>BEGIN</c>，
    /// 短的先比中就會把整個 TRY 骨架降級成普通的區塊。
    /// </remarks>
    private static readonly SqlBlockCloser[] Closers = { SqlBlockCloser.TryCatch, SqlBlockCloser.Block };

    /// <summary>
    /// 游標前那幾個字元是不是一組區塊的開頭。
    /// </summary>
    /// <param name="sql">目前緩衝區的文字，關鍵字<b>已經</b>寫進去了。</param>
    /// <param name="position">游標位置，落在關鍵字最後一個字元之後。</param>
    /// <returns>要用的骨架與它的起點；不該包夾時為 <c>null</c>。</returns>
    /// <remarks>
    /// 只認得兩種開頭，其餘一律不包：<c>BEGIN TRAN</c>、<c>BEGIN DISTRIBUTED
    /// TRANSACTION</c>、<c>BEGIN DIALOG</c> 都不是區塊，它們不需要 <c>END</c>。
    /// 猜錯的代價是使用者得把補上的三行刪掉，而猜對只省下一次換行與兩個字。
    ///
    /// 關鍵字前面必須是邊界：<c>dbo.BEGIN</c> 或 <c>@BEGIN</c> 這種識別字的尾巴不算。
    /// </remarks>
    public static SqlBlockMatch? MatchEndingAt(ISqlTextSource sql, int position)
    {
        Validate(sql, position, nameof(position));

        if (position < "BEGIN".Length)
        {
            return null;
        }

        // 先試長的：BEGIN TRY 的尾巴也是 BEGIN，順序反過來永遠只會拿到 Block。
        foreach (var closer in Closers)
        {
            if (FindKeywordStart(sql, position, closer.Keyword) is not int start)
            {
                continue;
            }

            // 字串、註解與方括號識別字裡不包夾：那裡的 BEGIN 是內容而不是語法。
            return IsKeywordBoundary(sql, start) && SqlLexicalContext.IsCode(sql, start)
                ? new SqlBlockMatch(closer, start)
                : null;
        }

        return null;
    }

    /// <summary>
    /// 把骨架與被包住的內容排成一段文字。
    /// </summary>
    /// <param name="closer">要用的骨架。</param>
    /// <param name="content">被包住的內容，可能含多行，續行不含前導縮排。</param>
    /// <param name="indent">骨架每一行的前導空白（外層那一行的縮排）。</param>
    /// <param name="unit">縮排一層的單位。</param>
    /// <param name="newLine">換行序列。</param>
    /// <returns>整段要取代選取範圍的文字，也就是從 <c>BEGIN</c> 到 <c>END</c> 為止。</returns>
    /// <remarks>
    /// 內容的續行一律貼齊同一欄，不保留它原本的相對縮排：呼叫端交進來的
    /// <paramref name="content"/> 已經去掉了每一行原本的前導縮排，
    /// 這裡再疊一次就只會愈縮愈深。
    ///
    /// 內容自己的換行原樣保留，縮排補在<b>整串</b>換行之後：CRLF 要補在 <c>\n</c>
    /// 後面而不是夾在 <c>\r</c> 與 <c>\n</c> 之間——夾在中間會把一個換行拆成兩個。
    /// </remarks>
    public static string Wrap(
        SqlBlockCloser closer,
        string content,
        string indent,
        string unit,
        string newLine)
    {
        if (closer is null)
        {
            throw new ArgumentNullException(nameof(closer));
        }

        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        if (indent is null)
        {
            throw new ArgumentNullException(nameof(indent));
        }

        if (unit is null)
        {
            throw new ArgumentNullException(nameof(unit));
        }

        if (newLine is null)
        {
            throw new ArgumentNullException(nameof(newLine));
        }

        var builder = new StringBuilder(content.Length + (newLine.Length * 4) + 32);
        AppendLines(builder, closer.Opening, indent, newLine);
        builder.Append(newLine);

        var inner = indent + unit;
        builder.Append(inner);

        for (var index = 0; index < content.Length; index++)
        {
            builder.Append(content[index]);

            // 換行原樣帶過去再補縮排；CRLF 算一次，不補兩次。
            if (content[index] == '\n' ||
                (content[index] == '\r' && (index + 1 == content.Length || content[index + 1] != '\n')))
            {
                builder.Append(inner);
            }
        }

        builder.Append(newLine);
        AppendLines(builder, closer.Closing, indent, newLine);

        return builder.ToString();
    }

    /// <summary>骨架每一行都在同一欄，因此只有第一行以後才要加換行。</summary>
    private static void AppendLines(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        string indent,
        string newLine)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(newLine);
            }

            builder.Append(indent).Append(lines[index]);
        }
    }

    /// <summary>
    /// 游標前那一段是不是正好以這個關鍵字結尾，是的話回傳它的起點。
    /// </summary>
    /// <remarks>
    /// 由後往前逐字比對，遇到關鍵字裡的空白時改成吃掉<b>一到多個</b>空白：
    /// <c>BEGIN   TRY</c> 與 <c>BEGIN TRY</c> 是同一件事，而兩個字的間距有幾格
    /// 取決於使用者怎麼排。硬要比對同一個長度，就只認得剛好一個空白的寫法。
    ///
    /// 回傳起點而不是布林值，是因為呼叫端接著要問「這個位置的語彙狀態」——
    /// 那必須是關鍵字的<b>第一個</b>字元，而空白有幾格不一定。
    /// </remarks>
    private static int? FindKeywordStart(ISqlTextSource sql, int position, string keyword)
    {
        var cursor = position - 1;

        for (var index = keyword.Length - 1; index >= 0; index--)
        {
            if (keyword[index] == ' ')
            {
                if (cursor < 0 || !char.IsWhiteSpace(sql[cursor]))
                {
                    return null;
                }

                while (cursor >= 0 && char.IsWhiteSpace(sql[cursor]))
                {
                    cursor--;
                }

                continue;
            }

            if (cursor < 0 || char.ToUpperInvariant(sql[cursor]) != keyword[index])
            {
                return null;
            }

            cursor--;
        }

        return cursor + 1;
    }

    /// <summary>關鍵字前面是邊界：空白、行首、標點，或本身就是另一個關鍵字。</summary>
    /// <remarks>
    /// 只擋識別字的尾巴（<c>@BEGIN</c>、<c>xBEGIN</c>）。一般標點放行——
    /// <c>IF x = 1 BEGIN</c> 也算數，雖然那樣寫的人不多。
    /// </remarks>
    private static bool IsKeywordBoundary(ISqlTextSource sql, int start)
    {
        if (start <= 0)
        {
            return true;
        }

        var previous = sql[start - 1];

        return !char.IsLetterOrDigit(previous) && previous != '_' && previous != '@' && previous != '#';
    }

    private static void Validate(ISqlTextSource sql, int position, string parameterName)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        if (position < 0 || position > sql.Length)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
