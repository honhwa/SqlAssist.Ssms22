using SqlAssist.Core.Pairing;
using SqlAssist.Core.Parsing;
using Xunit;

namespace SqlAssist.Core.Tests.Pairing;

/// <summary>
/// 選取一段文字之後打 <c>BEGIN</c>，要不要把它包成一組區塊骨架。
/// </summary>
/// <remarks>
/// 這一組與 <see cref="SqlAutoPairAnalyzerTests"/> 分開的理由是<b>時機</b>：
/// 單字元配對在按鍵當下就有答案，而區塊要等關鍵字整串進了緩衝區之後才問得出來。
/// 因此測試輸入標的是「<b>游標在關鍵字後面</b>」，而不是「即將輸入某個字元」。
///
/// 反方向照樣比正方向重要：多補的每一行使用者都得手動刪掉，
/// 而 <c>BEGIN</c> 開頭的敘述在 T-SQL 裡有一大票不是區塊。
/// </remarks>
public sealed class SqlBlockPairAnalyzerTests
{
    [Theory]
    [InlineData("BEGIN|", "BEGIN")]
    [InlineData("begin|", "BEGIN")]
    [InlineData("    BEGIN|", "BEGIN")]
    [InlineData("BEGIN TRY|", "BEGIN TRY")]
    [InlineData("begin try|", "BEGIN TRY")]
    [InlineData("BEGIN   TRY|", "BEGIN TRY")]
    [InlineData("IF @@ROWCOUNT > 0 BEGIN|", "BEGIN")]
    public void 認得區塊的開頭(string sqlWithCaret, string expected)
    {
        var input = SqlWithCaret.Parse(sqlWithCaret);

        Assert.Equal(
            expected,
            SqlBlockPairAnalyzer.MatchEndingAt(new SqlStringText(input.Text), input.Caret)?.Closer.Keyword);
    }

    /// <remarks>
    /// 起點要一起回傳，因為關鍵字裡的空白幾個都算，長度推不出起點。
    /// 呼叫端拿它去問語彙狀態、也拿它決定取代範圍從哪一行起算。
    /// </remarks>
    [Theory]
    [InlineData("BEGIN|", 0)]
    [InlineData("    BEGIN|", 4)]
    [InlineData("IF 1 = 1 BEGIN|", 9)]
    [InlineData("BEGIN   TRY|", 0)]
    [InlineData("    BEGIN   TRY|", 4)]
    public void 一起回報關鍵字從哪裡開始(string sqlWithCaret, int expected)
    {
        var input = SqlWithCaret.Parse(sqlWithCaret);

        Assert.Equal(expected, SqlBlockPairAnalyzer.MatchEndingAt(new SqlStringText(input.Text), input.Caret)?.Start);
    }

    /// <remarks>
    /// <c>BEGIN TRAN</c> 與 <c>BEGIN DIALOG</c> 都不需要 <c>END</c>。
    /// 猜錯的代價是使用者得把補上的三行刪掉，而猜對只省下一次換行。
    /// </remarks>
    [Theory]
    [InlineData("BEGIN TRAN|")]
    [InlineData("BEGIN DISTRIBUTED TRANSACTION|")]
    [InlineData("BEGIN DIALOG|")]
    [InlineData("BEGIN CONVERSATION TIMER|")]
    [InlineData("BEGIN TRYING|")]
    [InlineData("BEGI|")]
    public void 不是區塊的開頭不認(string sqlWithCaret)
    {
        var input = SqlWithCaret.Parse(sqlWithCaret);

        Assert.Null(SqlBlockPairAnalyzer.MatchEndingAt(new SqlStringText(input.Text), input.Caret));
    }

    /// <remarks>
    /// 識別字的尾巴不算：<c>@BEGIN</c> 是變數、<c>xBEGIN</c> 是欄位名稱。
    /// </remarks>
    [Theory]
    [InlineData("@BEGIN|")]
    [InlineData("xBEGIN|")]
    [InlineData("#BEGIN|")]
    [InlineData("_BEGIN|")]
    public void 識別字尾巴不認(string sqlWithCaret)
    {
        var input = SqlWithCaret.Parse(sqlWithCaret);

        Assert.Null(SqlBlockPairAnalyzer.MatchEndingAt(new SqlStringText(input.Text), input.Caret));
    }

    /// <remarks>
    /// 那裡的 <c>BEGIN</c> 是內容而不是語法，包起來等於竄改使用者的字面值。
    /// </remarks>
    [Theory]
    [InlineData("SELECT 'BEGIN|")]
    [InlineData("SELECT 'BEGIN'|")]
    [InlineData("-- BEGIN|")]
    [InlineData("SELECT [BEGIN|")]
    public void 字串註解與方括號識別字裡不認(string sqlWithCaret)
    {
        var input = SqlWithCaret.Parse(sqlWithCaret);

        Assert.Null(SqlBlockPairAnalyzer.MatchEndingAt(new SqlStringText(input.Text), input.Caret));
    }

    /// <remarks>
    /// 開頭那一行留在使用者原本的位置，內容整段往右縮一層，<c>END</c> 另起一行。
    /// 縮排單位跟著外層那一行，所以同一個骨架在任何深度都排得對。
    /// </remarks>
    [Fact]
    public void 把內容包成一對BEGIN與END()
    {
        var text = SqlBlockPairAnalyzer.Wrap(
            SqlBlockCloser.Block,
            "UPDATE dbo.Loan\r\nSET CopyNo = 1;",
            "    ",
            "    ",
            "\r\n");

        Assert.Equal(
            "    BEGIN\r\n" +
            "        UPDATE dbo.Loan\r\n" +
            "        SET CopyNo = 1;\r\n" +
            "    END",
            text);
    }

    /// <remarks>
    /// 四行一次到齊。分兩次補的話，中間那段時間緩衝區裡是一句語法錯誤——
    /// <c>BEGIN TRY</c> 少了 <c>END TRY</c>，查詢視窗會把整段標紅。
    /// </remarks>
    [Fact]
    public void TRY與CATCH一次補齊()
    {
        var text = SqlBlockPairAnalyzer.Wrap(
            SqlBlockCloser.TryCatch,
            "UPDATE dbo.Loan\r\nSET CopyNo = 1;",
            "    ",
            "    ",
            "\r\n");

        Assert.Equal(
            "    BEGIN TRY\r\n" +
            "        UPDATE dbo.Loan\r\n" +
            "        SET CopyNo = 1;\r\n" +
            "    END TRY\r\n" +
            "    BEGIN CATCH\r\n" +
            "    END CATCH",
            text);
    }

    /// <remarks>
    /// 外層縮排是幾格就跟幾格，不必再有一份「這裡要縮多少」的設定。
    /// </remarks>
    [Fact]
    public void 縮排跟著外層那一行()
    {
        var text = SqlBlockPairAnalyzer.Wrap(
            SqlBlockCloser.Block,
            "SELECT 1;",
            "\t\t",
            "\t",
            "\r\n");

        Assert.Equal("\t\tBEGIN\r\n\t\t\tSELECT 1;\r\n\t\tEND", text);
    }

    /// <remarks>
    /// 行首本來就沒有縮排時，內容仍然要縮一層——否則區塊的內容看起來與 <c>BEGIN</c>
    /// 同一層，那正是這個功能要避免的。
    /// </remarks>
    [Fact]
    public void 沒有外層縮排時內容仍縮一層()
    {
        var text = SqlBlockPairAnalyzer.Wrap(
            SqlBlockCloser.Block,
            "SELECT 1;",
            string.Empty,
            "    ",
            "\n");

        Assert.Equal("BEGIN\n    SELECT 1;\nEND", text);
    }

    /// <remarks>
    /// 多行內容的續行全部貼齊同一欄，不保留它原本的相對縮排——
    /// 呼叫端交進來之前已經去掉原本的縮排了。
    /// </remarks>
    [Fact]
    public void 多行內容的續行貼齊同一欄()
    {
        var text = SqlBlockPairAnalyzer.Wrap(
            SqlBlockCloser.Block,
            "SELECT a\r\nFROM b\r\nWHERE c = 1",
            string.Empty,
            "  ",
            "\r\n");

        Assert.Equal("BEGIN\r\n  SELECT a\r\n  FROM b\r\n  WHERE c = 1\r\nEND", text);
    }

    /// <remarks>
    /// 內容自己的換行原樣保留，縮排補在<b>整串</b>換行之後：CRLF 補在 <c>\n</c> 後面
    /// 而不是夾在 <c>\r</c> 與 <c>\n</c> 中間——夾在中間會把一個換行拆成兩個，
    /// 而且縮排跑到 <c>\r</c> 與 <c>\n</c> 之間，看起來像是一行空白。
    /// </remarks>
    [Theory]
    [InlineData("a\nb", "BEGIN\n  a\n  b\nEND")]
    [InlineData("a\r\nb", "BEGIN\n  a\r\n  b\nEND")]
    [InlineData("a\rb", "BEGIN\n  a\r  b\nEND")]
    public void 三種換行都只補一次縮排(string content, string expected)
    {
        var text = SqlBlockPairAnalyzer.Wrap(
            SqlBlockCloser.Block,
            content,
            string.Empty,
            "  ",
            "\n");

        Assert.Equal(expected, text);
    }
}
