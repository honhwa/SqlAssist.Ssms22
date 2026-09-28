using System;
using System.Collections.Generic;
using SqlAssist.Core.Parsing;

namespace SqlAssist.Core.Rewriting;

/// <summary>把 <c>TOP 10</c> 這種沒加括號的數量補成 <c>TOP (10)</c>。</summary>
/// <remarks>
/// T-SQL 只允許「非負整數字面值」省略括號——<c>TOP @n</c> 雖然在文法上也通，
/// 但它與 <c>TOP (@n + 1)</c> 寫在同一個位置，混著用時看不出哪一種是刻意的。
/// 因此這裡只處理數字：動的東西越少，使用者越有把握這次按下去只是風格統一。
///
/// 判斷條件刻意窄到只剩「<c>TOP</c> 後面緊接著一個數字」。<c>TOP</c> 也可以是別的名稱
/// （資料行、別名），但那時候它後面不會是一個數字——<c>SELECT 1 AS top, 2</c> 的
/// <c>top</c> 後面是逗號。條件放寬成「TOP 後面是運算式」就會開始亂補。
/// </remarks>
public static class SqlTopClauseParenthesis
{
    /// <summary>補上這份文字裡每一處 <c>TOP</c> 的括號。</summary>
    /// <param name="sql">要處理的整段文字。</param>
    /// <param name="caretPosition">游標在 <paramref name="sql"/> 裡的位置；負值代表不處理游標。</param>
    /// <returns>換完的文字與動到的處數。</returns>
    public static SqlTextRewriteResult Parenthesize(string sql, int caretPosition = -1)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var tokens = SqlTokenizer.Tokenize(sql);
        var edits = new List<SqlTextEdit>();

        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if (!tokens[index].IsKeyword("TOP"))
            {
                continue;
            }

            var value = tokens[index + 1];

            if (value.Kind != SqlTokenKind.Number)
            {
                continue;
            }

            // 數字後面還接著名稱字元時，掃出來的這個數字其實是更長的東西的一部分；
            // 括號加在中間會把原本還算能編的東西弄成語法錯誤。
            if (value.End < sql.Length && IsNameCharacter(sql[value.End]))
            {
                continue;
            }

            edits.Add(new SqlTextEdit(value.Start, value.Length, "(" + value.Text + ")"));
        }

        return SqlTextRewrite.Apply(edits, sql, caretPosition);
    }

    private static bool IsNameCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_' || value == '.' || value == '#';
    }
}
