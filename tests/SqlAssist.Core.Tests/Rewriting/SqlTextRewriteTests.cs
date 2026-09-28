using System;
using System.Collections.Generic;
using SqlAssist.Core.Rewriting;
using Xunit;

namespace SqlAssist.Core.Tests.Rewriting;

public sealed class SqlTextRewriteTests
{
    /// <remarks>
    /// 編輯的順序不拘——位置是原文座標，先套用哪一個都不影響結果。這一條讓分析器
    /// 可以邊掃邊加，不必先排序；少了它，分析器就得自己保證順序，而那個保證
    /// 只在「掃描一定是遞增」成立時才對。
    /// </remarks>
    [Fact]
    public void 編輯順序不影響結果()
    {
        var forwards = new List<SqlTextEdit>
        {
            new(0, 0, "dbo."),
            new(10, 0, "dbo.")
        };

        var backwards = new List<SqlTextEdit>
        {
            new(10, 0, "dbo."),
            new(0, 0, "dbo.")
        };

        Assert.Equal("dbo.ABCDEFGHIJdbo.K", SqlTextRewrite.Apply(forwards, "ABCDEFGHIJK", -1).Text);
        Assert.Equal("dbo.ABCDEFGHIJdbo.K", SqlTextRewrite.Apply(backwards, "ABCDEFGHIJK", -1).Text);
    }

    /// <remarks>替換（不只是插入）的長度差要算對，否則後面的位置整批偏掉。</remarks>
    [Fact]
    public void 取代比插入的長度還短時位置不會偏()
    {
        var result = SqlTextRewrite.Apply(
            new List<SqlTextEdit> { new(3, 2, ".") },
            "ABC..DEF",
            -1);

        Assert.Equal("ABC.DEF", result.Text);
        Assert.Equal(1, result.AffectedCount);
    }

    /// <remarks>
    /// 游標在編輯之後就跟著位移，在編輯起點則留在原地（插入的文字接在它後面）。
    /// </remarks>
    [Fact]
    public void 游標依編輯位置換算()
    {
        IReadOnlyList<SqlTextEdit> edits = new List<SqlTextEdit>
        {
            new(0, 0, "dbo."),
            new(6, 0, "dbo.")
        };

        const string text = "AB CD EF";

        // 游標在第一處的起點：插入的文字接在它後面，只有後面的那一處會推走它。
        Assert.Equal(0, SqlTextRewrite.Apply(edits, text, 0).CaretPosition);

        // 游標在第二處的起點：第一處在它前面，仍然要位移。
        Assert.Equal(6 + 4, SqlTextRewrite.Apply(edits, text, 6).CaretPosition);

        // 游標在兩處之間與兩處之後。
        Assert.Equal(2 + 4, SqlTextRewrite.Apply(edits, text, 2).CaretPosition);
        Assert.Equal(text.Length + 8, SqlTextRewrite.Apply(edits, text, text.Length).CaretPosition);
        Assert.Equal(-1, SqlTextRewrite.Apply(edits, text, -1).CaretPosition);
    }

    /// <remarks>
    /// 沒有編輯時原樣回傳，游標也照傳進去的那個值回去。
    /// </remarks>
    [Fact]
    public void 沒有編輯時原樣回傳()
    {
        var result = SqlTextRewrite.Apply(Array.Empty<SqlTextEdit>(), "SELECT 1", 3);

        Assert.Equal("SELECT 1", result.Text);
        Assert.Equal(0, result.AffectedCount);
        Assert.Equal(3, result.CaretPosition);
    }

    /// <remarks>
    /// 重疊直接視為程式錯誤。安靜地挑一個套用的話，補出來的 SQL 少一段，
    /// 而畫面上看不出來——那正是這個型別存在的理由（位置一律以原文為準）。
    /// </remarks>
    [Fact]
    public void 重疊的編輯直接丟例外()
    {
        var edits = new List<SqlTextEdit>
        {
            new(0, 4, "x"),
            new(2, 0, "y")
        };

        Assert.Throws<InvalidOperationException>(() => SqlTextRewrite.Apply(edits, "ABCDEFG", -1));
    }

    /// <remarks>超出原文長度同樣是程式錯誤，不該安靜地截掉。</remarks>
    [Fact]
    public void 超出原文長度的編輯直接丟例外()
    {
        var edits = new List<SqlTextEdit> { new(6, 4, "x") };

        Assert.Throws<InvalidOperationException>(() => SqlTextRewrite.Apply(edits, "ABCDEFG", -1));
    }
}
