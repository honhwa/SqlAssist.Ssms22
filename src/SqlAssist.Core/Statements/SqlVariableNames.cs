using System;
using System.Collections.Generic;
using SqlAssist.Core.Parsing;

namespace SqlAssist.Core.Statements;

/// <summary>
/// 想知道「這個名字在這個批次裡被用掉了嗎」，以及「換到哪個名字才不會撞」。
/// </summary>
/// <remarks>
/// 用在 <c>EXEC</c> 展開：展開出來的是 <c>DECLARE @x AS …</c>，而 <c>@x</c> 可能
/// 早就被同一批次的其他地方宣告過了。兩份 <c>DECLARE</c> 同名不會有編譯錯誤，
/// 第二句只是把第一句的值覆蓋掉——症狀是展開之後那一句跑得動，卻把使用者原本的
/// 變數值改掉了，而畫面上看不出來。
///
/// 範圍是<b>一個批次</b>而不是整份文件，界線取自 <c>GO</c>：變數是批次層級的物件，
/// 跨過 <c>GO</c> 之後同名的 <c>@變數</c> 是另一個變數，把它算成衝突只會讓展開
/// 出來的名字多一個沒必要的後綴。判準與[就地改名](../Rewriting/SqlVariableRename.cs)
/// 同一條，只是那一邊看的是 ScriptDom 權杖、這一邊看的是自家的詞法單元。
///
/// <b><c>GO</c> 的判定是逐字比對，不是問詞法單元是不是識別字。</b>
/// 行首的 <c>GO</c> 才會被詞法分析器讀成識別字；<c>SELECT 1 GO</c> 那種同一行的寫法
/// 裡，<c>GO</c> 之後還有東西，ScriptDom 根本不把它當分隔。因此還要要求它<b>獨占一行</b>
/// ——前後只有空白與註解。寬鬆一點（看到 <c>GO</c> 就切）的症狀是把批次切在一個
/// 不存在的界線上，展開出來的名字因此少算了幾個本來該避開的變數。
/// </remarks>
public static class SqlVariableNames
{
    /// <summary>一個批次裡已經被用到的所有變數名稱。</summary>
    /// <param name="sql">整份文字。</param>
    /// <param name="position">批次裡的任一個位置，用來決定要看哪一個批次。</param>
    /// <remarks>
    /// 以 <see cref="StringComparer.OrdinalIgnoreCase"/> 比對：T-SQL 的變數名稱不分大小寫，
    /// <c>@LoanId</c> 與 <c>@loanid</c> 是同一個變數。
    /// </remarks>
    public static IReadOnlyCollection<string> Collect(string sql, int position)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in SqlTokenizer.Tokenize(sql))
        {
            if (token.Kind != SqlTokenKind.Variable)
            {
                continue;
            }

            if (!InBatch(sql, token.Start, position))
            {
                continue;
            }

            names.Add(token.Text);
        }

        return names;
    }

    /// <summary>
    /// 把 <paramref name="name"/> 換成一個同批次裡還沒有人用過的名字。
    /// </summary>
    /// <param name="name">原本想要的名字，含開頭的 <c>@</c>。</param>
    /// <param name="taken">同批次裡已經被用掉的名字，比對不分大小寫。</param>
    /// <returns>可以用的名字；沒有衝突時就是 <paramref name="name"/> 本身。</returns>
    /// <remarks>
    /// 有衝突時在尾巴接上從 <c>1</c> 開始的流水號：<c>@LoanId</c> → <c>@LoanId1</c> →
    /// <c>@LoanId2</c>，直到不衝突為止。接數字而不是接固定字尾（<c>@LoanId_Exec</c>）：
    /// 固定字尾在連續展開第二次時會再撞一次，而流水號天生保證找得到落點。
    ///
    /// 型別那段可能帶長度（<c>@name nvarchar(50)</c> 只會出現在參數定義裡，這裡拿到的是
    /// 純名稱），因此不必考慮字尾插入位置。
    /// </remarks>
    public static string Resolve(string name, IReadOnlyCollection<string> taken)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (taken is null)
        {
            throw new ArgumentNullException(nameof(taken));
        }

        var names = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);

        if (!names.Contains(name))
        {
            return name;
        }

        for (var suffix = 1; ; suffix++)
        {
            var candidate = name + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// 把要展開的參數逐一套上一個同批次沒人用過的變數名。
    /// </summary>
    /// <param name="parameters">模組的參數，順序即輸出順序。</param>
    /// <param name="sql">整份文字。</param>
    /// <param name="position">展開位置，用來決定看哪一個批次。</param>
    /// <returns>變數名調整過的新清單；沒有撞名時回傳原本的內容。</returns>
    /// <remarks>
    /// <b>只換 <see cref="SqlStatementParameter.VariableName"/>，不動
    /// <see cref="SqlStatementParameter.Name"/>。</b>後者是模組簽章裡的名字，呼叫那一行
    /// <c>EXEC dbo.usp_P @LoanId = …</c> 左邊那個 <c>@LoanId</c> 一定要是模組定義裡寫的
    /// 那一個，跟著改名那一句就找不到對應的參數（錯誤 8145）——而畫面上只看得出
    /// 名字多了一個數字。宣告、傳值（等號右邊）與 <c>SELECT</c> 才是這個區域變數。
    ///
    /// 挑好的名字累加進同一份集合，否則兩個參數會搶到同一個落點：<c>@x</c> 撞名換成
    /// <c>@x1</c>，而另一個本來就叫 <c>@x1</c> 的參數沒人告訴它。
    /// </remarks>
    public static IReadOnlyList<SqlStatementParameter> Avoid(
        IReadOnlyList<SqlStatementParameter> parameters,
        string sql,
        int position)
    {
        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var taken = new HashSet<string>(Collect(sql, position), StringComparer.OrdinalIgnoreCase);
        var resolved = new SqlStatementParameter[parameters.Count];
        var changed = false;

        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            var variable = Resolve(parameter.VariableName, taken);

            taken.Add(variable);

            if (variable == parameter.VariableName)
            {
                resolved[index] = parameter;
                continue;
            }

            changed = true;
            resolved[index] = new SqlStatementParameter(
                parameter.Name,
                parameter.DataType,
                parameter.IsOutput,
                parameter.IsOptional,
                parameter.DefaultValue,
                variable);
        }

        return changed ? resolved : parameters;
    }

    /// <summary>兩個位置是不是落在同一個批次裡。</summary>
    private static bool InBatch(string sql, int left, int right)
    {
        // 兩個位置之間只要有任一個真正的批次分隔，就不算同一批次；
        // 用「有沒有跨過去」而不是「各在哪一段」來問，是因為這一支只答是或不是。
        var from = Math.Min(left, right);
        var to = Math.Max(left, right);

        foreach (var offset in GoOffsets(sql))
        {
            if (offset > from && offset <= to)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>所有真正的批次分隔（行首的 <c>GO</c>）的起點。</summary>
    private static IEnumerable<int> GoOffsets(string sql)
    {
        foreach (var token in SqlTokenizer.Tokenize(sql))
        {
            if (!token.IsKeyword("GO") || !OwnsLine(sql, token))
            {
                continue;
            }

            yield return token.Start;
        }
    }

    /// <summary>這個權杖是不是自己占了一整行。</summary>
    /// <remarks>
    /// <c>GO 3</c>（重複次數）也算：次數寫在 <c>GO</c> 後面，而它仍然是批次分隔。
    /// 前後看的是同一行裡有沒有別的東西，註解不算——<c>GO -- 換一批</c> 是常見寫法。
    /// </remarks>
    private static bool OwnsLine(string sql, SqlToken token)
    {
        var lineStart = token.Start;

        while (lineStart > 0 && sql[lineStart - 1] != '\n' && sql[lineStart - 1] != '\r')
        {
            lineStart--;
        }

        if (!IsBlank(sql, lineStart, token.Start))
        {
            return false;
        }

        var lineEnd = token.End;

        while (lineEnd < sql.Length && sql[lineEnd] != '\n' && sql[lineEnd] != '\r')
        {
            lineEnd++;
        }

        return IsGoLineTrailing(sql, token.End, lineEnd);
    }

    /// <summary>
    /// <c>GO</c> 後面那一段只有空白、註解或重複次數時才算數。
    /// </summary>
    /// <remarks>
    /// 只要遇到不是這三種的字元就整行不算——<c>SELECT 1 GO</c> 是敘述的一部分，
    /// <c>GO x</c> 的 <c>x</c> 也不是次數。寬鬆判定會多切出一個批次，
    /// 而那個假界線讓展開漏掉界線之後的變數。
    /// </remarks>
    private static bool IsGoLineTrailing(string sql, int start, int end)
    {
        var index = start;

        while (index < end)
        {
            var current = sql[index];

            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (SqlTrivia.StartsLineComment(sql, index, end))
            {
                return true;
            }

            if (SqlTrivia.StartsBlockComment(sql, index, end))
            {
                index = SqlTrivia.SkipBlockComment(sql, index, end);
                continue;
            }

            if (!char.IsDigit(current))
            {
                return false;
            }

            while (index < end && char.IsDigit(sql[index]))
            {
                index++;
            }
        }

        return true;
    }

    private static bool IsBlank(string sql, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (!char.IsWhiteSpace(sql[index]))
            {
                return false;
            }
        }

        return true;
    }
}
