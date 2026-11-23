using System;
using SqlAssist.Core.Statements;
using Xunit;

namespace SqlAssist.Core.Tests.Statements;

/// <summary>
/// 從既有 INSERT 文字反查「游標所在的欄位或值」與它的另一端。
/// </summary>
public sealed class SqlInsertColumnValueMatcherTests
{
    [Fact]
    public void 游標在值上回傳同序的欄位()
    {
        var sql = SqlWithCaret.Parse("INSERT INTO dbo.Lib_Reader (ReaderId, Name) VALUES (1, |N'Ann')");
        var matcher = new SqlInsertColumnValueMatcher(sql.Text);

        var counterpart = matcher.FindCounterpart(sql.Caret);

        Assert.Equal("Name", Slice(sql.Text, counterpart));
    }

    [Fact]
    public void 游標在欄位上回傳同序的值()
    {
        var sql = SqlWithCaret.Parse("INSERT INTO dbo.Lib_Reader (ReaderId, |Name) VALUES (1, N'Ann')");
        var matcher = new SqlInsertColumnValueMatcher(sql.Text);

        var counterpart = matcher.FindCounterpart(sql.Caret);

        Assert.Equal("N'Ann'", Slice(sql.Text, counterpart));
    }

    [Theory]
    [InlineData("INSERT INTO t (A, B) VALUES (1, 2)")]
    [InlineData("INSERT t (A, B) VALUES (1, 2)")]
    [InlineData("INSERT INTO t (A, B) OUTPUT INSERTED.A VALUES (1, 2)")]
    public void 各欄位與值一一對應(string text)
    {
        var matcher = new SqlInsertColumnValueMatcher(text);

        Assert.Equal(2, matcher.Pairs.Count);
        Assert.Equal("A", Slice(text, matcher.Pairs[0].Column));
        Assert.Equal("1", Slice(text, matcher.Pairs[0].Value));
        Assert.Equal("B", Slice(text, matcher.Pairs[1].Column));
        Assert.Equal("2", Slice(text, matcher.Pairs[1].Value));
    }

    [Fact]
    public void 限定名稱與方括號欄位也對應()
    {
        var text = "INSERT INTO [dbo].[Lib Reader] ([Copy No], Name) VALUES (1, N'Ann')";
        var matcher = new SqlInsertColumnValueMatcher(text);

        Assert.Equal(2, matcher.Pairs.Count);
        Assert.Equal("[Copy No]", Slice(text, matcher.Pairs[0].Column));
        Assert.Equal("Name", Slice(text, matcher.Pairs[1].Column));
    }

    [Fact]
    public void 值的欄位清單內逗號不當成分隔()
    {
        var text = "INSERT INTO t (A, B) VALUES (dbo.f(1, 2), N'a,b')";
        var matcher = new SqlInsertColumnValueMatcher(text);

        Assert.Equal(2, matcher.Pairs.Count);
        Assert.Equal("dbo.f(1, 2)", Slice(text, matcher.Pairs[0].Value));
        Assert.Equal("N'a,b'", Slice(text, matcher.Pairs[1].Value));
    }

    [Fact]
    public void 值比欄位多時多出來的值不配對()
    {
        var text = "INSERT INTO t (A, B) VALUES (1, 2, 3)";
        var matcher = new SqlInsertColumnValueMatcher(text);

        Assert.Equal(2, matcher.Pairs.Count);
    }

    [Fact]
    public void 只有一組值時第二組不認()
    {
        var sql = SqlWithCaret.Parse("INSERT INTO t (A, B) VALUES (1, 2), (3, |4)");
        var matcher = new SqlInsertColumnValueMatcher(sql.Text);

        Assert.Null(matcher.FindCounterpart(sql.Caret));
        Assert.Equal(2, matcher.Pairs.Count);
    }

    [Fact]
    public void 游標在逗號括號或空白上不回傳()
    {
        var text = "INSERT INTO t (A, B) VALUES (1, 2)";
        var matcher = new SqlInsertColumnValueMatcher(text);

        foreach (var token in new[] { "(A", "A,", ", B", "(1", ", 2" })
        {
            var tokenStart = text.IndexOf(token, StringComparison.Ordinal);
            Assert.True(tokenStart >= 0, token);
            var position = token.EndsWith(",", StringComparison.Ordinal) ? tokenStart + 1 : tokenStart;
            Assert.Null(matcher.FindCounterpart(position));
        }
    }

    [Fact]
    public void 沒有欄位清單的INSERT不配對()
    {
        var matcher = new SqlInsertColumnValueMatcher("INSERT INTO t VALUES (1, 2)");

        Assert.Empty(matcher.Pairs);
    }

    [Fact]
    public void 註解裡的假INSERT不配對()
    {
        var text = "/* INSERT INTO t (A) VALUES (1) */ SELECT 1;";
        var matcher = new SqlInsertColumnValueMatcher(text);

        Assert.Empty(matcher.Pairs);
    }

    [Fact]
    public void 多句各自成對()
    {
        var sql = SqlWithCaret.Parse("INSERT INTO t (A, B) VALUES (1, 2); INSERT INTO u (X, Y) VALUES (3, |4)");
        var matcher = new SqlInsertColumnValueMatcher(sql.Text);

        Assert.Equal(4, matcher.Pairs.Count);
        Assert.Equal("Y", Slice(sql.Text, matcher.FindCounterpart(sql.Caret)));
    }

    [Fact]
    public void 游標停在逗號後的值起點算在值上()
    {
        var text = "INSERT INTO t (A, B) VALUES (1, 2)";
        var position = text.IndexOf(", 2", StringComparison.Ordinal) + 2;
        var matcher = new SqlInsertColumnValueMatcher(text);

        Assert.Equal("B", Slice(text, matcher.FindCounterpart(position)));
    }

    private static string Slice(string text, SqlAssist.Core.Parsing.SqlTextSpan? span) =>
        span is { } value ? text.Substring(value.Start, value.Length) : string.Empty;
}
