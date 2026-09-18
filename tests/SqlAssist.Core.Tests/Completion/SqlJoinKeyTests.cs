using SqlAssist.Core.Completion;
using SqlAssist.Core.Keywords;
using SqlAssist.Core.Settings;
using Xunit;

namespace SqlAssist.Core.Tests.Completion;

/// <summary>
/// 配對鍵寫進編輯器的樣子，以及它在哪些位置成立。
/// </summary>
public sealed class SqlJoinKeyTests
{
    /// <summary>預設設定：不加方括號。</summary>
    private static readonly SqlAssistSettings Plain = new();

    private static SqlJoinKey Key(
        string column = "CopyNo",
        string? qualifier = "b",
        string counterpart = "CopyNo",
        string? counterpartQualifier = "a")
    {
        return new SqlJoinKey(column, qualifier, counterpart, counterpartQualifier);
    }

    /// <summary>一整條條件一次寫完，游標留在後面接 <c>AND</c>。</summary>
    [Fact]
    public void 插入文字是整條聯結條件()
    {
        Assert.Equal("b.CopyNo = a.CopyNo", Key().ComposeInsertionText(Plain));
    }

    /// <remarks>
    /// 左邊那一半就是顯示文字本身，再寫一次只是雜訊；說明欄要說的是它對到誰。
    /// </remarks>
    [Fact]
    public void 說明欄只寫對面那一側()
    {
        Assert.Equal("= a.CopyNo", Key().ComposeSuffix(Plain));
    }

    /// <summary>
    /// 兩側一定都冠限定字。
    /// </summary>
    /// <remarks>
    /// 一般欄位只有在相異限定字兩個以上時才補名字，這裡不能沿用那一條：配得出對，
    /// 正是因為敘述裡有兩個來源，裸名在敘述裡是模稜兩可的。
    /// </remarks>
    [Fact]
    public void 兩側都冠上限定字()
    {
        var key = Key("CopyNo", "x", "CopyNo", "y");

        Assert.Equal("x.CopyNo = y.CopyNo", key.ComposeInsertionText(Plain));
        Assert.Equal("= y.CopyNo", key.ComposeSuffix(Plain));
    }

    [Fact]
    public void 開啟方括號時兩側都加()
    {
        var settings = new SqlAssistSettings { UseSquareBrackets = true };

        Assert.Equal("[b].[CopyNo] = [a].[CopyNo]", Key().ComposeInsertionText(settings));
        Assert.Equal("= [a].[CopyNo]", Key().ComposeSuffix(settings));
    }

    /// <remarks>關掉「一律加方括號」不是要產生無效語法。</remarks>
    [Fact]
    public void 需要括號的名稱即使沒開偏好也要加()
    {
        Assert.Equal(
            "b.[Copy No] = a.[Copy No]",
            Key("Copy No", "b", "Copy No", "a").ComposeInsertionText(Plain));
    }

    [Fact]
    public void 讀不出限定字時只寫名稱()
    {
        var key = new SqlJoinKey("CopyNo", null, "CopyNo", null);

        Assert.Equal("CopyNo = CopyNo", key.ComposeInsertionText(Plain));
        Assert.Equal("= CopyNo", key.ComposeSuffix(Plain));
    }

    /// <remarks>
    /// 指令碼自己宣告的名稱不在方括號偏好的管轄內：<c>[#tmp]</c> 合法卻不是任何人
    /// 會手寫的樣子。欄位名稱不在這一條裡，它仍照一般名稱走同一份括號規則。
    /// </remarks>
    [Fact]
    public void 指令碼宣告的名稱不套用方括號偏好()
    {
        var settings = new SqlAssistSettings { UseSquareBrackets = true };

        Assert.Equal(
            "#tmp.[CopyNo] = [a].[CopyNo]",
            Key("CopyNo", "#tmp", "CopyNo", "a").ComposeInsertionText(settings));
    }

    private static bool WantsJoinKeys(string sqlWithCaret)
    {
        var input = SqlWithCaret.Parse(sqlWithCaret);

        return SqlJoinKeyMatcher.WantsJoinKeys(
            SqlCompletionContextAnalyzer.Analyze(input.Text, input.Caret));
    }

    /// <summary>
    /// 述詞的起點才配對：那裡使用者的下一句就是那兩個同名欄位的條件。
    /// </summary>
    [Theory]
    [InlineData("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON |")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE |")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.Qty > 0 AND |")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.Qty > 0 OR |")]
    [InlineData("SELECT l.Code, COUNT(*) FROM dbo.Loan l GROUP BY l.Code HAVING |")]
    [InlineData("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.Code = c.Code AND |")]
    public void 述詞起點要配對(string sqlWithCaret)
    {
        Assert.True(WantsJoinKeys(sqlWithCaret));
    }

    /// <summary>
    /// 條件的左邊寫完之後就不是述詞起點了。
    /// </summary>
    /// <remarks>
    /// 那裡要的是右邊那一個欄位；插入整條條件會寫出 <c>= a.CopyNo = b.CopyNo</c>。
    /// 三個案例的詞元剛好都落在 <see cref="SqlKeywordPosition.Any"/>——運算元結束
    /// 之後分析器判不出上下文——而那個成員含著述詞這一個位元，所以這一條守的正是
    /// 「確定是述詞」與「判不出來」的分別。
    /// </remarks>
    [Theory]
    [InlineData("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo = |")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.Qty > |")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.Code LIKE |")]
    public void 條件的左邊寫完之後不配對(string sqlWithCaret)
    {
        Assert.False(WantsJoinKeys(sqlWithCaret));
    }

    /// <summary>
    /// <c>SELECT</c> 與 <c>FROM</c> 之後不配對：那裡要的是欄位或資料表本身。
    /// </summary>
    [Theory]
    [InlineData("SELECT |")]
    [InlineData("SELECT | FROM dbo.Loan l")]
    [InlineData("SELECT * FROM |")]
    [InlineData("SELECT * FROM dbo.Loan l JOIN |")]
    [InlineData("SELECT * FROM dbo.Loan l ORDER BY |")]
    [InlineData("SELECT * FROM dbo.Loan l GROUP BY |")]
    public void 清單起點不配對(string sqlWithCaret)
    {
        Assert.False(WantsJoinKeys(sqlWithCaret));
    }
}
