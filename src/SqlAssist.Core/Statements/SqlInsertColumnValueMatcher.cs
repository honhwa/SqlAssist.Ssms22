using System;
using System.Collections.Generic;
using SqlAssist.Core.Parsing;

namespace SqlAssist.Core.Statements;

/// <summary>
/// 從既有的 <c>INSERT</c> 文字找出欄位清單與第一組 <c>VALUES</c> 的同一序位對應。
/// </summary>
/// <remarks>
/// 只讀詞法串流，不建語法樹：游標所處的語句幾乎總是還沒打完，而欄位與值的
/// 位置在詞法層就決定好了。清單切分一律走 <see cref="SqlTokenNavigator.FindListItemEnd"/>，
/// 才不會把 <c>dbo.f(1, 2)</c> 或 <c>N'a,b'</c> 裡的逗號當成項目的分隔。
/// 只認第一組 <c>VALUES</c>：骨架展開只產生一組，後面的組別沒有對應的欄位。
/// </remarks>
public sealed class SqlInsertColumnValueMatcher
{
    private readonly IReadOnlyList<SqlInsertColumnValuePair> _pairs;

    public SqlInsertColumnValueMatcher(string sql)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        _pairs = Analyze(SqlTokenizer.Tokenize(sql));
    }

    public IReadOnlyList<SqlInsertColumnValuePair> Pairs => _pairs;

    /// <summary>游標落在欄位或值上時，回傳另一端的位置；其餘位置回 <c>null</c>。</summary>
    public SqlTextSpan? FindCounterpart(int position)
    {
        foreach (var pair in _pairs)
        {
            if (pair.Column.Contains(position))
            {
                return pair.Value;
            }

            if (pair.Value.Contains(position))
            {
                return pair.Column;
            }
        }

        return null;
    }

    private static IReadOnlyList<SqlInsertColumnValuePair> Analyze(IReadOnlyList<SqlToken> tokens)
    {
        var pairs = new List<SqlInsertColumnValuePair>();

        for (var index = 0; index < tokens.Count; index++)
        {
            if (!tokens[index].IsKeyword("INSERT"))
            {
                continue;
            }

            if (!TryFindLists(tokens, index, out var columnOpen, out var columnClose,
                    out var valueOpen, out var valueClose))
            {
                continue;
            }

            var columns = SplitList(tokens, columnOpen + 1, columnClose);
            var values = SplitList(tokens, valueOpen + 1, valueClose);
            var count = Math.Min(columns.Count, values.Count);

            for (var item = 0; item < count; item++)
            {
                pairs.Add(new SqlInsertColumnValuePair(columns[item], values[item]));
            }
        }

        return pairs;
    }

    private static bool TryFindLists(
        IReadOnlyList<SqlToken> tokens,
        int insert,
        out int columnOpen,
        out int columnClose,
        out int valueOpen,
        out int valueClose)
    {
        columnOpen = -1;
        columnClose = -1;
        valueOpen = -1;
        valueClose = -1;

        // INSERT [TOP (n)] [INTO] 名稱 (欄位…) [OUTPUT …] VALUES (值…)
        var index = insert + 1;

        if (index < tokens.Count && tokens[index].IsKeyword("TOP"))
        {
            index++;
            if (index < tokens.Count && tokens[index].IsPunctuation("("))
            {
                var topEnd = SqlTokenNavigator.FindClosingParenthesis(tokens, index, tokens.Count);
                if (topEnd < 0) return false;
                index = topEnd + 1;
            }
        }

        if (index < tokens.Count && tokens[index].IsKeyword("INTO"))
        {
            index++;
        }

        // 跳過目標資料表的限定名稱；它後面必須緊接欄位清單的左括號。
        // OUTPUT 子句的 INTO 在欄位括號之後，不會在這裡被當成目標。
        if (index >= tokens.Count || !SqlTokenNavigator.IsNamePart(tokens[index]))
        {
            return false;
        }

        while (index + 2 < tokens.Count && tokens[index + 1].IsPunctuation(".") &&
            SqlTokenNavigator.IsNamePart(tokens[index + 2]))
        {
            index += 2;
        }

        index++;
        if (index >= tokens.Count || !tokens[index].IsPunctuation("("))
        {
            return false;
        }

        columnOpen = index;
        columnClose = SqlTokenNavigator.FindClosingParenthesis(tokens, columnOpen, tokens.Count);
        if (columnClose < 0) return false;

        var values = columnClose + 1;
        while (values < tokens.Count && !tokens[values].IsKeyword("VALUES") &&
            !tokens[values].IsKeyword("INSERT") && !tokens[values].IsPunctuation(";"))
        {
            values++;
        }

        if (values >= tokens.Count || !tokens[values].IsKeyword("VALUES") || values + 1 >= tokens.Count ||
            !tokens[values + 1].IsPunctuation("("))
        {
            return false;
        }

        valueOpen = values + 1;
        valueClose = SqlTokenNavigator.FindClosingParenthesis(tokens, valueOpen, tokens.Count);
        if (valueClose < 0) return false;

        return true;
    }

    private static IReadOnlyList<SqlTextSpan> SplitList(IReadOnlyList<SqlToken> tokens, int start, int end)
    {
        var spans = new List<SqlTextSpan>();
        var item = start;

        while (item < end)
        {
            var itemEnd = SqlTokenNavigator.FindListItemEnd(tokens, item, end);
            if (itemEnd > item)
            {
                spans.Add(SqlTextSpan.FromBounds(tokens[item].Start, tokens[itemEnd - 1].End));
            }

            item = itemEnd + 1;
        }

        return spans;
    }
}
