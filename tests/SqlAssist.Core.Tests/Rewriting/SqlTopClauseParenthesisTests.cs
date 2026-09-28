using SqlAssist.Core.Rewriting;
using Xunit;

namespace SqlAssist.Core.Tests.Rewriting;

public sealed class SqlTopClauseParenthesisTests
{
    private static string Parenthesize(string sql) => SqlTopClauseParenthesis.Parenthesize(sql).Text;

    private static int Count(string sql) => SqlTopClauseParenthesis.Parenthesize(sql).AffectedCount;

    /// <remarks>
    /// 只處理數字：<c>TOP @n</c> 是合法語法，改它只是風格統一，而使用者按這條命令時
    /// 期待的是「把 T-SQL 要求括號的地方補上」。條件放寬成「TOP 後面是運算式」之後，
    /// 判斷就開始靠猜，猜錯時是把能執行的 SQL 改成語法錯誤。
    /// </remarks>
    [Theory]
    [InlineData("SELECT TOP 10 * FROM dbo.Loan", "SELECT TOP (10) * FROM dbo.Loan")]
    [InlineData("SELECT TOP 010 * FROM dbo.Loan", "SELECT TOP (010) * FROM dbo.Loan")]
    [InlineData("SELECT TOP 10 PERCENT * FROM dbo.Loan", "SELECT TOP (10) PERCENT * FROM dbo.Loan")]
    [InlineData("SELECT TOP 10 WITH TIES * FROM dbo.Loan ORDER BY CopyNo", "SELECT TOP (10) WITH TIES * FROM dbo.Loan ORDER BY CopyNo")]
    [InlineData("UPDATE TOP 10 dbo.Loan SET CopyNo = 1", "UPDATE TOP (10) dbo.Loan SET CopyNo = 1")]
    [InlineData("DELETE TOP 10 FROM dbo.Loan", "DELETE TOP (10) FROM dbo.Loan")]
    [InlineData("select top 5 * from dbo.Loan", "select top (5) * from dbo.Loan")]
    public void TOP的數字補上括號(string sql, string expected)
    {
        Assert.Equal(expected, Parenthesize(sql));
    }

    /// <remarks>
    /// 已經有括號的一律不動，包含 <c>TOP (@n + 1)</c> 這種運算式。重複加一層括號
    /// 雖然還編得過，但每按一次就多一層，而使用者看不出是哪一次造成的。
    /// </remarks>
    [Theory]
    [InlineData("SELECT TOP (10) * FROM dbo.Loan")]
    [InlineData("SELECT TOP (@n) * FROM dbo.Loan")]
    [InlineData("SELECT TOP (@n + 1) * FROM dbo.Loan")]
    [InlineData("SELECT TOP @n * FROM dbo.Loan")]
    public void 已有括號或不是數字時不動(string sql)
    {
        Assert.Equal(sql, Parenthesize(sql));
        Assert.Equal(0, Count(sql));
    }

    /// <remarks>
    /// <c>TOP</c> 也可以是資料行或別名。那時候它後面不會是一個數字——條件窄到
    /// 「後面緊接著數字」就是為了這個；只看關鍵字的話，<c>SELECT 1 AS top, 2</c>
    /// 這種寫法會被當成 TOP 子句。
    /// </remarks>
    [Theory]
    [InlineData("SELECT 1 AS top, 2 AS CopyNo")]
    [InlineData("SELECT CopyNo AS top FROM dbo.Loan")]
    [InlineData("SELECT * FROM dbo.Loan WHERE top = 10")]
    public void TOP當成名稱時不動(string sql)
    {
        Assert.Equal(sql, Parenthesize(sql));
    }

    /// <remarks>字串與註解裡的 <c>TOP 10</c> 不是子句。</remarks>
    [Theory]
    [InlineData("SELECT 'TOP 10' AS x")]
    [InlineData("SELECT 1 AS x -- TOP 10")]
    [InlineData("SELECT 1 AS x /* TOP 10 */")]
    public void 字串與註解不動(string sql)
    {
        Assert.Equal(sql, Parenthesize(sql));
    }

    /// <remarks>
    /// 多處一起改時位置全部以原文為準，補上的兩個字元不會把後面的位置推走。
    /// </remarks>
    [Fact]
    public void 多處一次改寫()
    {
        var result = SqlTopClauseParenthesis.Parenthesize(
            "SELECT TOP 10 * FROM dbo.Loan;\r\nSELECT TOP 5 * FROM dbo.Copy");

        Assert.Equal(
            "SELECT TOP (10) * FROM dbo.Loan;\r\nSELECT TOP (5) * FROM dbo.Copy",
            result.Text);
        Assert.Equal(2, result.AffectedCount);
    }

    /// <remarks>
    /// 游標在數字之後（含結尾）時要跟著往後移兩個字元，否則在 <c>TOP 10|</c>
    /// 按完右鍵，游標會留在左括號之前。
    /// </remarks>
    [Fact]
    public void 游標跟著往後移()
    {
        const string sql = "SELECT TOP 10 *";

        Assert.Equal(sql.Length + 2, SqlTopClauseParenthesis.Parenthesize(sql, sql.Length).CaretPosition);
        Assert.Equal(11, SqlTopClauseParenthesis.Parenthesize(sql, 11).CaretPosition);
        Assert.Equal(-1, SqlTopClauseParenthesis.Parenthesize(sql).CaretPosition);
    }

    /// <remarks>沒有可補的 TOP 時回「沒有改動」，呼叫端才不會白寫一次緩衝區。</remarks>
    [Fact]
    public void 沒有可補的TOP時不改動()
    {
        const string sql = "SELECT TOP (10) * FROM dbo.Loan";

        var result = SqlTopClauseParenthesis.Parenthesize(sql);

        Assert.False(result.HasChanges);
        Assert.Equal(sql, result.Text);
    }

    /// <remarks>空文字不該丟例外。</remarks>
    [Fact]
    public void 空文字不丟例外()
    {
        var result = SqlTopClauseParenthesis.Parenthesize(string.Empty);

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(0, result.AffectedCount);
    }
}
