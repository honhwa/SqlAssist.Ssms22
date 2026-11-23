using System.Linq;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Parsing;
using Xunit;

namespace SqlAssist.Core.Tests.Completion;

/// <summary>
/// 打完一條條件之後認不認得出「兩欄相比」：形狀、位置與那幾個不能補的情形。
/// </summary>
/// <remarks>
/// 這一層只回答「游標前面是不是 <c>限定字.欄位 = 限定字.欄位</c>」；
/// 定序有沒有不同、要不要補，由 <see cref="SqlJoinKey.CollationToApply"/> 回答。
/// </remarks>
public sealed class SqlJoinCollateAnalyzerTests
{
    private static SqlColumnComparison? Find(string sqlWithCaret, char separator = ' ')
    {
        var input = SqlWithCaret.Parse(sqlWithCaret);

        return SqlJoinCollateAnalyzer.TryFindBefore(new SqlStringText(input.Text), input.Caret, separator);
    }

    /// <summary>把結果攤成 <c>左限定字.左欄=右限定字.右欄</c>，一次比對整份。</summary>
    private static string? Describe(string sqlWithCaret, char separator = ' ')
    {
        return Find(sqlWithCaret, separator) is { } comparison
            ? $"{comparison.LeftQualifier}.{comparison.LeftName}=" +
              $"{comparison.RightQualifier}.{comparison.RightName}"
            : null;
    }

    /// <summary>
    /// 述詞子句裡剛寫完的等值比較：兩側都是帶限定字的欄位。
    /// </summary>
    /// <remarks>
    /// <c>AND</c> 之後也算——一條 <c>ON</c> 常常有第二、第三個條件，
    /// 只認 <c>ON</c> 的話後面那幾條永遠等不到。
    /// </remarks>
    [Theory]
    [InlineData("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo = c.CopyNo|", "l.CopyNo=c.CopyNo")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.CopyNo = c.CopyNo|", "l.CopyNo=c.CopyNo")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.CopyNo = c.CopyNo AND c.Code = l.Code|", "c.Code=l.Code")]
    [InlineData("SELECT l.Code FROM dbo.Loan l GROUP BY l.Code HAVING l.Code = c.Code|", "l.Code=c.Code")]
    public void 述詞子句的等值比較認得出來(string sqlWithCaret, string expected)
    {
        Assert.Equal(expected, Describe(sqlWithCaret));
    }

    /// <summary>條件寫完了、游標停在 <c>AND</c> 後面時，前面那一條已經補過了。</summary>
    [Fact]
    public void 游標停在AND後面時不重複判斷()
    {
        Assert.Null(Describe("SELECT * FROM dbo.Loan l WHERE l.CopyNo = c.CopyNo AND |"));
    }

    /// <summary>
    /// 空白、換行、右括號、逗號與分號都代表「這一條寫完了」。
    /// </summary>
    [Theory]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('\n')]
    [InlineData(')')]
    [InlineData(',')]
    [InlineData(';')]
    public void 分隔字元結束了這一條(char separator)
    {
        Assert.Equal(
            "l.CopyNo=c.CopyNo",
            Describe("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo = c.CopyNo|", separator));
    }

    /// <summary>
    /// 運算子與左括號不是結束：那裡條件還沒寫完，補上去會插在運算式中間。
    /// </summary>
    [Theory]
    [InlineData('=')]
    [InlineData('(')]
    [InlineData('+')]
    [InlineData('<')]
    [InlineData('.')]
    public void 運算子和左括號不算分隔(char separator)
    {
        Assert.Null(Describe("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo = c.CopyNo|", separator));
    }

    /// <summary>
    /// 指定不是比較。
    /// </summary>
    /// <remarks>
    /// <c>SET a.X = b.Y</c> 與 <c>SELECT a.X = b.Y</c> 的形狀與條件一模一樣，
    /// 補上 <c>COLLATE</c> 之後那一句是語法錯誤——這也是為什麼前面那個字要認。
    /// </remarks>
    [Theory]
    [InlineData("UPDATE dbo.Loan SET l.CopyNo = c.CopyNo|")]
    [InlineData("SET l.CopyNo = c.CopyNo|")]
    [InlineData("SELECT l.CopyNo = c.CopyNo|")]
    [InlineData("DECLARE @v int = l.CopyNo|")]
    public void 指定不補(string sqlWithCaret)
    {
        Assert.Null(Describe(sqlWithCaret));
    }

    /// <summary>變數不是欄位。</summary>
    [Theory]
    [InlineData("SELECT * FROM dbo.Loan l WHERE @x = c.CopyNo|")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.CopyNo = @x|")]
    [InlineData("EXEC dbo.usp_Renew @readerId = c.CopyNo|")]
    public void 變數不算欄位(string sqlWithCaret)
    {
        Assert.Null(Describe(sqlWithCaret));
    }

    /// <summary>兩邊同一個限定字不是跨來源的比較，裸名分不出是哪一邊。</summary>
    [Theory]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.CopyNo = l.Code|")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE CopyNo = c.CopyNo|")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.CopyNo = CopyNo|")]
    public void 分不出兩邊時不補(string sqlWithCaret)
    {
        Assert.Null(Describe(sqlWithCaret));
    }

    /// <summary>只有等值比較會撞上定序衝突。</summary>
    [Theory]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.CopyNo != c.CopyNo|")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.CopyNo <> c.CopyNo|")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.CopyNo <= c.CopyNo|")]
    [InlineData("SELECT * FROM dbo.Loan l WHERE l.CopyNo >= c.CopyNo|")]
    public void 不是等值比較就不補(string sqlWithCaret)
    {
        Assert.Null(Describe(sqlWithCaret));
    }

    /// <summary>字串與註解裡的同一個形狀不是條件。</summary>
    [Theory]
    [InlineData("SELECT 'ON l.CopyNo = c.CopyNo|")]
    [InlineData("-- ON l.CopyNo = c.CopyNo|")]
    [InlineData("/* ON l.CopyNo = c.CopyNo|")]
    public void 字串與註解裡不算(string sqlWithCaret)
    {
        Assert.Null(Describe(sqlWithCaret));
    }

    /// <summary>方括號包起來的名稱一樣認得，名稱要去掉方括號再比。</summary>
    [Fact]
    public void 方括號名稱認得出來()
    {
        Assert.Equal(
            "l.CopyNo=c.CopyNo",
            Describe("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON [l].[CopyNo] = [c].[CopyNo]|"));
    }

    /// <summary>
    /// 四段式名稱吃得下，限定字取緊鄰欄位的那一段。
    /// </summary>
    /// <remarks>
    /// 敘述裡沒有別名時 <c>SqlColumnSource.Qualifier</c> 就是資料表名稱，
    /// 整串 <c>LibArchive.dbo.Copy</c> 拿去比會一個來源都對不上。
    /// </remarks>
    [Fact]
    public void 四段式名稱取緊鄰欄位的那一段()
    {
        Assert.Equal(
            "Copy.CopyNo=l.CopyNo",
            Describe("SELECT * FROM LibArchive.dbo.Copy JOIN dbo.Loan l ON LibArchive.dbo.Copy.CopyNo = l.CopyNo|"));
    }

    /// <summary>空白多打幾個、條件換行寫，都還是同一條。</summary>
    [Theory]
    [InlineData("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo   =   c.CopyNo|")]
    [InlineData("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON\n    l.CopyNo = c.CopyNo|")]
    [InlineData("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo =\n    c.CopyNo|")]
    public void 空白不影響判斷(string sqlWithCaret)
    {
        Assert.Equal("l.CopyNo=c.CopyNo", Describe(sqlWithCaret));
    }

    /// <summary>條件整條包在括號裡時，前面那個字是左括號。</summary>
    [Fact]
    public void 左括號之後的條件也認得()
    {
        Assert.Equal(
            "l.CopyNo=c.CopyNo",
            Describe("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON (l.CopyNo = c.CopyNo|", ')'));
    }

    /// <summary>游標在文字最前面、或位置超出長度時什麼都不做。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    public void 位置不合理時不判斷(int position)
    {
        var text = new SqlStringText("SELECT 1");

        Assert.Null(SqlJoinCollateAnalyzer.TryFindBefore(text, position, ' '));
    }

    /// <summary>
    /// 條件尾端仍說得出兩個來源——補 COLLATE 那一端要靠這一份才查得到定序。
    /// </summary>
    /// <remarks>
    /// 條件寫完、游標停在欄位尾端時，上下文分析回的位置是「判不出來」，
    /// 但來源清單仍然要有值；空了的話整個功能會安靜地不作用。
    /// </remarks>
    [Fact]
    public void 條件尾端仍說得出敘述的來源()
    {
        const string Sql = "SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo = c.CopyNo";

        var context = SqlCompletionContextAnalyzer.Analyze(Sql, Sql.Length);

        Assert.Equal(
            new[] { "l", "c" },
            context.ScopeSources.Select(source => source.Qualifier));
    }
}
