using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlAssist.Core.Parsing;

namespace SqlAssist.Core.Rewriting;

/// <summary>游標所在的那個區域變數，以及它在同一個批次裡可以一起改掉的所有出現位置。</summary>
/// <remarks>
/// 位置一律是<b>原文</b>的字元座標。改名的過程中文字會變長或變短，之後的座標全部跟著
/// 位移；呼叫端要保留的是範圍而不是座標（編輯器的追蹤範圍會自己跟著文字跑），
/// 這裡只負責把「一開始是哪些位置」講清楚。
/// </remarks>
public sealed class SqlVariableRenameTarget
{
    internal SqlVariableRenameTarget(
        string name,
        IReadOnlyList<int> occurrences,
        int cursorIndex,
        int batchStart,
        int batchEnd)
    {
        Name = name;
        Occurrences = occurrences;
        CursorIndex = cursorIndex;
        BatchStart = batchStart;
        BatchEnd = batchEnd;
    }

    /// <summary>原文裡的名稱，含開頭的 <c>@</c>。</summary>
    public string Name { get; }

    /// <summary>可以一起改名的出現位置起點，遞增排列；長度一律是 <see cref="Name"/> 的長度。</summary>
    public IReadOnlyList<int> Occurrences { get; }

    /// <summary>游標那一處在 <see cref="Occurrences"/> 裡的索引。</summary>
    public int CursorIndex { get; }

    /// <summary>游標那一處的起點。</summary>
    public int CursorOffset => Occurrences[CursorIndex];

    /// <summary>所在批次的起點（前一個 <c>GO</c> 之後）。</summary>
    public int BatchStart { get; }

    /// <summary>所在批次的終點（下一個 <c>GO</c> 之前，不含）。</summary>
    public int BatchEnd { get; }
}

/// <summary>就地改名一個區域變數時，該改哪幾處、以及新的名稱能不能用。</summary>
/// <remarks>
/// 全部只看文字，因此放在 Core：這一段的每一條判定（批次界線、哪些 <c>@變數</c> 算同一組、
/// 哪些是別人的參數名、新名稱合不合法）都可以用字串直接驗，不必開 SSMS。
///
/// 範圍是<b>一個批次</b>而不是整份文件。變數是批次層級的物件，跨過 <c>GO</c> 之後
/// 同名的 <c>@變數</c> 是另一個變數；一起改掉的話，第二個批次會憑空多出一個沒有
/// <c>DECLARE</c> 的名稱，而錯誤要等到執行那一段才會出現。
///
/// <b>批次的界線取自 ScriptDom 判定的 <c>Go</c> 權杖，不是找 <c>GO</c> 這個字。</b>
/// 用字串比對會把字串常值（<c>'GO'</c>）、註解（<c>-- GO</c>）與資料行名稱
/// （<c>[GO]</c>）一起當成分隔，切出一個不存在的批次；症狀是只改到一半的出現位置，
/// 而使用者看得到的地方每一處都對，得捲到很後面才發現有幾處沒改到。
/// </remarks>
public static class SqlVariableRename
{
    /// <summary>游標處那個變數能不能改名；可以時連同所有出現位置一起回傳。</summary>
    /// <param name="sql">整份文字。</param>
    /// <param name="caretPosition">游標位置。</param>
    /// <param name="target">可以改名時的目標；否則為 <c>null</c>。</param>
    /// <param name="message">不能改名時要讓使用者看到的原因；可以時是空字串。</param>
    /// <returns>可以改名時為 <c>true</c>。</returns>
    /// <remarks>
    /// 失敗一律回一句原因而不是安靜地回 <c>false</c>：入口是一顆使用者按下去的命令，
    /// 沒有反應在使用者眼裡與壞掉一樣，而「游標不在變數上」與「這是系統函式」
    /// 該做的事完全不同。
    /// </remarks>
    public static bool TryLocate(
        string sql,
        int caretPosition,
        out SqlVariableRenameTarget? target,
        out string message)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        target = null;

        if (caretPosition < 0 || caretPosition > sql.Length)
        {
            message = RewritingText.VariableCaretOutOfRange;
            return false;
        }

        var tokens = ReadTokens(sql);
        var cursor = FindTokenAt(tokens, caretPosition);

        if (cursor is null || cursor.TokenType != TSqlTokenType.Variable)
        {
            message = RewritingText.VariableCaretNotOnVariable;
            return false;
        }

        if (cursor.Text.StartsWith("@@", StringComparison.Ordinal))
        {
            message = RewritingText.VariableIsSystemFunction(cursor.Text);
            return false;
        }

        var (batchStart, batchEnd) = FindBatch(tokens, sql.Length, caretPosition);
        var arguments = FindExecArgumentOffsets(tokens, batchStart, batchEnd);

        if (arguments.Contains(cursor.Offset))
        {
            message = RewritingText.VariableIsProcedureArgument(cursor.Text);
            return false;
        }

        var occurrences = new List<int>();
        var cursorIndex = -1;

        foreach (var token in tokens)
        {
            if (token.Offset < batchStart || token.Offset >= batchEnd)
            {
                continue;
            }

            if (token.TokenType != TSqlTokenType.Variable
                || !string.Equals(token.Text, cursor.Text, StringComparison.OrdinalIgnoreCase)
                || arguments.Contains(token.Offset))
            {
                continue;
            }

            if (token.Offset == cursor.Offset)
            {
                cursorIndex = occurrences.Count;
            }

            occurrences.Add(token.Offset);
        }

        if (cursorIndex < 0)
        {
            message = RewritingText.VariableOnlyAsProcedureArgument(cursor.Text);
            return false;
        }

        target = new SqlVariableRenameTarget(cursor.Text, occurrences, cursorIndex, batchStart, batchEnd);
        message = string.Empty;
        return true;
    }

    /// <summary>把目標的每一處換成新的名稱。</summary>
    /// <param name="sql">原文。</param>
    /// <param name="target">要改的目標，必須是從 <paramref name="sql"/> 找出來的。</param>
    /// <param name="newName">新的名稱，含開頭的 <c>@</c>。</param>
    /// <returns>換完的文字與動到的處數。</returns>
    /// <exception cref="ArgumentNullException">參數為 <c>null</c>。</exception>
    /// <exception cref="ArgumentException"><paramref name="newName"/> 不是合法的區域變數名稱。</exception>
    // 這兩句是程式錯誤的例外訊息（呼叫端自己算錯座標、自己傳了不合法的名稱），
    // 只進診斷紀錄、不給使用者看，所以固定繁中不進 resjson。
    [Localizable(false)]
    public static SqlTextRewriteResult Rename(string sql, SqlVariableRenameTarget target, string newName)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (!IsValidName(newName))
        {
            throw new ArgumentException($"不是合法的區域變數名稱：{newName}", nameof(newName));
        }

        var edits = new List<SqlTextEdit>(target.Occurrences.Count);

        foreach (var offset in target.Occurrences)
        {
            if (offset + target.Name.Length > sql.Length)
            {
                throw new ArgumentException("目標的位置不在這份文字上。", nameof(target));
            }

            edits.Add(new SqlTextEdit(offset, target.Name.Length, newName));
        }

        return SqlTextRewrite.Apply(edits, sql, target.CursorOffset);
    }

    /// <summary>這個名稱可不可以用來命名一個區域變數。</summary>
    /// <remarks>
    /// <c>@@</c> 開頭的一律不准：那一族是系統函式（<c>@@ROWCOUNT</c>、<c>@@ERROR</c>），
    /// 它們不是宣告出來的東西，改了不會有新的變數冒出來，只會讓原本那一段全部失效。
    ///
    /// 組成規則直接問既有的詞法分析器，不另寫一份：與掃描那一邊分歧的下場是
    /// 「名稱通過了檢查，但每一個出現位置都不再被認成同一個變數」，而那一刻已經改完了。
    /// </remarks>
    public static bool IsValidName(string? name)
    {
        // netstandard2.0 的參考組件沒有 IsNullOrEmpty 的 NotNullWhen 標註，
        // 先自己擋掉 null 才不會在下一個判斷被判成可能為 null。
        if (name is null || name.Length < 2 || name[0] != '@')
        {
            return false;
        }

        if (name[1] == '@' || char.IsDigit(name[1]))
        {
            return false;
        }

        var tokens = SqlTokenizer.Tokenize(name);

        return tokens.Count == 1
            && tokens[0].Kind == SqlTokenKind.Variable
            && tokens[0].Length == name.Length;
    }

    /// <summary>新的名稱在同一批次裡是不是已經被別的變數用掉了。</summary>
    /// <param name="sql">目前的文字。</param>
    /// <param name="positionInBatch">批次裡的任一個位置，用來決定要看哪一個批次。</param>
    /// <param name="name">新的名稱，含開頭的 <c>@</c>。</param>
    /// <param name="ignoredOffsets">這一輪正在改名的那幾處，不算衝突。</param>
    /// <returns>已經被別的變數用掉時為 <c>true</c>。</returns>
    /// <remarks>
    /// 判定用<b>改名後</b>的文字而不是原文：改名的過程中每一處都已經換成新名稱，
    /// 拿原文來問的話，正在改名的那幾處會把自己報成衝突。
    /// </remarks>
    public static bool IsNameTaken(
        string sql,
        int positionInBatch,
        string name,
        IReadOnlyCollection<int>? ignoredOffsets = null)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        var tokens = ReadTokens(sql);
        var (batchStart, batchEnd) = FindBatch(tokens, sql.Length, positionInBatch);

        foreach (var token in tokens)
        {
            if (token.Offset < batchStart || token.Offset >= batchEnd)
            {
                continue;
            }

            if (token.TokenType != TSqlTokenType.Variable
                || !string.Equals(token.Text, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ignoredOffsets is not null && ignoredOffsets.Contains(token.Offset))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    // ── 詞法掃描 ────────────────────────────────────────────────────────

    /// <remarks>
    /// 空白與註解不留：這一支的每一個判斷都只看語意單元，而留著它們的話，
    /// 每一個「下一個權杖」的查詢都要再跳一次。
    /// </remarks>
    private static IReadOnlyList<TSqlParserToken> ReadTokens(string sql)
    {
        using var reader = new StringReader(sql);
        var stream = new TSql160Parser(true).GetTokenStream(reader, out _);
        var tokens = new List<TSqlParserToken>(stream.Count);

        foreach (var token in stream)
        {
            if (token.TokenType is TSqlTokenType.WhiteSpace
                or TSqlTokenType.SingleLineComment
                or TSqlTokenType.MultilineComment
                or TSqlTokenType.EndOfFile)
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>游標所在的權杖。</summary>
    /// <remarks>
    /// 游標正好貼在某個權杖的右邊界時算在它身上：使用者剛打完名稱就按右鍵，
    /// 而游標停在最後一個字之後是那個當下最自然的位置。
    /// </remarks>
    private static TSqlParserToken? FindTokenAt(IReadOnlyList<TSqlParserToken> tokens, int position)
    {
        TSqlParserToken? previous = null;

        foreach (var token in tokens)
        {
            if (token.Offset > position)
            {
                break;
            }

            if (position < token.Offset + token.Text.Length)
            {
                return token;
            }

            previous = token;
        }

        return previous is { } candidate && candidate.Offset + candidate.Text.Length == position
            ? candidate
            : null;
    }

    /// <summary>包含指定位置的批次範圍。</summary>
    private static (int Start, int End) FindBatch(
        IReadOnlyList<TSqlParserToken> tokens,
        int length,
        int position)
    {
        var start = 0;
        var end = length;

        foreach (var token in tokens)
        {
            if (token.TokenType != TSqlTokenType.Go)
            {
                continue;
            }

            if (token.Offset + token.Text.Length <= position)
            {
                start = token.Offset + token.Text.Length;
            }
            else if (token.Offset > position)
            {
                end = token.Offset;
                break;
            }
        }

        return (start, end);
    }

    // ── 被呼叫程序的參數名 ──────────────────────────────────────────────

    /// <summary>所有「屬於被呼叫程序的簽章、不該跟著改名」的變數權杖位置。</summary>
    /// <remarks>
    /// <c>EXEC dbo.Proc @p = 1</c> 裡的 <c>@p</c> 名字由程序的宣告決定，不是呼叫端的
    /// 區域變數；跟著改掉的話，那一次呼叫會找不到對應的參數。
    ///
    /// <c>EXEC @rc = dbo.Proc @p = 1</c> 的 <c>@rc</c> 則相反——等號右邊才開始是程序的
    /// 參數，它自己是接回傳值的區域變數，必須留下來。兩者長得一模一樣（都是
    /// <c>@名稱 =</c>），差別只在它出現在程序名稱的哪一側，所以掃描要分成兩段。
    /// </remarks>
    private static HashSet<int> FindExecArgumentOffsets(
        IReadOnlyList<TSqlParserToken> tokens,
        int batchStart,
        int batchEnd)
    {
        var offsets = new HashSet<int>();

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];

            // EXEC 與 EXECUTE 是兩個不同的權杖型別。只認前者時，用全寫的那一種呼叫
            // 會被當成一般的敘述，參數名就整批跟著被改掉——而那一種寫法在產生出來的
            // 指令碼裡很常見。
            if (token.TokenType is not (TSqlTokenType.Exec or TSqlTokenType.Execute)
                || token.Offset < batchStart
                || token.Offset >= batchEnd)
            {
                continue;
            }

            CollectExecArguments(tokens, index, batchEnd, offsets);
        }

        return offsets;
    }

    /// <param name="execIndex">掃到的那一個 <c>EXEC</c> 權杖。</param>
    /// <remarks>
    /// 程序名稱之後的掃描停在「下一道敘述的起頭」：參數清單沒有結束標記，
    /// 而 <c>@名稱 =</c> 這種形狀在 <c>SET</c>、<c>SELECT</c>、<c>DECLARE</c> 裡
    /// 到處都是，不停下來就會把它們一起算成別人的參數名。
    /// </remarks>
    private static void CollectExecArguments(
        IReadOnlyList<TSqlParserToken> tokens,
        int execIndex,
        int batchEnd,
        HashSet<int> offsets)
    {
        var afterName = false;

        for (var index = execIndex + 1; index < tokens.Count; index++)
        {
            var token = tokens[index];

            if (token.Offset >= batchEnd || token.TokenType == TSqlTokenType.Go)
            {
                return;
            }

            if (afterName && IsStatementStart(token))
            {
                return;
            }

            switch (token.TokenType)
            {
                // EXEC ('…') 與 EXEC (@sql)：沒有具名參數。
                case TSqlTokenType.LeftParenthesis when !afterName:
                    return;

                // 等號左邊的變數：接回傳值的那一個，不是參數名。
                case TSqlTokenType.Variable when !afterName:
                    if (!IsFollowedByEquals(tokens, index))
                    {
                        return;
                    }

                    index++;
                    continue;

                case TSqlTokenType.Variable:
                    if (IsFollowedByEquals(tokens, index))
                    {
                        offsets.Add(token.Offset);
                    }

                    continue;

                // 程序名稱（可能帶資料庫與結構描述）。名稱之後才是參數清單。
                case TSqlTokenType.Identifier:
                case TSqlTokenType.QuotedIdentifier:
                    afterName = true;
                    continue;

                // 分號結束這一道敘述；再往下掃就會掃到下一道敘述的 @名稱 =。
                case TSqlTokenType.Semicolon:
                    return;
            }
        }
    }

    /// <summary>這一道權杖是不是另一道敘述的起頭。</summary>
    private static bool IsStatementStart(TSqlParserToken token) => token.TokenType is
        TSqlTokenType.Select or TSqlTokenType.Insert or TSqlTokenType.Update
        or TSqlTokenType.Delete or TSqlTokenType.Merge or TSqlTokenType.With
        or TSqlTokenType.Declare or TSqlTokenType.Set or TSqlTokenType.If
        or TSqlTokenType.Else or TSqlTokenType.While or TSqlTokenType.Begin
        or TSqlTokenType.End or TSqlTokenType.Return or TSqlTokenType.Create
        or TSqlTokenType.Alter or TSqlTokenType.Drop or TSqlTokenType.Truncate
        or TSqlTokenType.Commit or TSqlTokenType.Rollback or TSqlTokenType.Print
        or TSqlTokenType.Raiserror or TSqlTokenType.GoTo
        or TSqlTokenType.Exec or TSqlTokenType.Execute;

    private static bool IsFollowedByEquals(IReadOnlyList<TSqlParserToken> tokens, int index) =>
        index + 1 < tokens.Count && tokens[index + 1].TokenType == TSqlTokenType.EqualsSign;
}
