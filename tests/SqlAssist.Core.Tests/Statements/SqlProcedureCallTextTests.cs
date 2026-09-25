using System.Collections.Generic;
using SqlAssist.Core.Statements;
using Xunit;

namespace SqlAssist.Core.Tests.Statements;

/// <remarks>
/// 展開出來的是三段：所有參數的 <c>DECLARE</c>、只傳變數的 <c>EXEC</c>、
/// 以及把 OUTPUT 參數列出來的 <c>SELECT</c>。理由見
/// <see cref="SqlProcedureCallText"/>。
/// </remarks>
public sealed class SqlProcedureCallTextTests
{
    private static readonly SqlStatementParameter[] Renew =
    {
        new("@LoanId", "int", isOutput: false, isOptional: false),
        new("@Days", "int", isOutput: false, isOptional: true, "7"),
        new("@NewDueDate", "datetime2(7)", isOutput: true, isOptional: false)
    };

    private static string Build(
        IReadOnlyList<SqlStatementParameter> parameters,
        out int caretOffset,
        string indent = "",
        string keyword = "EXEC")
    {
        return SqlProcedureCallText.Build(
            keyword,
            "dbo.usp_Loan_Renew",
            parameters,
            indent,
            "\r\n",
            out caretOffset);
    }

    /// <remarks>
    /// 續行對齊到第一個參數所在的欄，每一列的 @ 因此落在同一個位置：
    /// 掃過去就知道有幾個參數、少填了哪一個。
    ///
    /// 值擺在 DECLARE 而不是呼叫上：呼叫那一行只留變數名稱，要改的值集中在一處，
    /// 而且 <c>@Days</c> 拿到的是模組的預設值 <c>7</c> 而不是型別的預留值 <c>0</c>。
    ///
    /// <c>OUTPUT</c> 只寫在呼叫那一行——寫在宣告上是語法錯誤。
    /// </remarks>
    [Fact]
    public void 先宣告所有參數再具名呼叫()
    {
        var text = Build(Renew, out _);

        Assert.Equal(
            "DECLARE @LoanId AS int              = 0;\r\n" +
            "DECLARE @Days AS int                = 7;\r\n" +
            "DECLARE @NewDueDate AS datetime2(7) = NULL;\r\n" +
            "EXEC dbo.usp_Loan_Renew @LoanId = @LoanId,               -- int\r\n" +
            "                        @Days = @Days,                   -- int，選擇性\r\n" +
            "                        @NewDueDate = @NewDueDate OUTPUT -- datetime2(7)\r\n" +
            "\r\n" +
            "SELECT @NewDueDate AS NewDueDate;",
            text);
    }

    /// <remarks>
    /// <c>OUTPUT</c> 是呼叫端的語意，不是變數宣告的一部分：
    /// <c>DECLARE @x INT OUTPUT</c> 根本是語法錯誤。它只寫在 <c>EXEC</c> 那一行的引數上。
    /// </remarks>
    [Fact]
    public void 宣告不寫OUTPUT()
    {
        var text = Build(
            new[] { new SqlStatementParameter("@rc", "int", isOutput: true, isOptional: false) },
            out _);

        Assert.StartsWith("DECLARE @rc AS int = 0;", text);
        Assert.DoesNotContain("OUTPUT;", text, System.StringComparison.Ordinal);
    }

    /// <remarks>
    /// 從前只有 OUTPUT 參數需要變數，所以只宣告那一種。現在呼叫端每一個都是變數，
    /// 少宣告任何一個，那一句連編譯都過不了。
    /// </remarks>
    [Fact]
    public void 沒有OUTPUT參數時仍宣告每一個參數()
    {
        var text = Build(
            new[] { new SqlStatementParameter("@ReaderId", "int", isOutput: false, isOptional: false) },
            out _);

        Assert.Equal(
            "DECLARE @ReaderId AS int = 0;\r\n" +
            "EXEC dbo.usp_Loan_Renew @ReaderId = @ReaderId -- int",
            text);
    }

    /// <remarks>沒有輸出參數就不該多一個空的 SELECT。</remarks>
    [Fact]
    public void 沒有OUTPUT參數時不寫SELECT()
    {
        var text = Build(
            new[]
            {
                new SqlStatementParameter("@ReaderId", "int", isOutput: false, isOptional: false),
                new SqlStatementParameter("@TagId", "int", isOutput: false, isOptional: false)
            },
            out _);

        Assert.DoesNotContain("SELECT", text, System.StringComparison.Ordinal);
    }

    /// <remarks>
    /// OUTPUT 參數的值只在變數裡，SSMS 的「訊息」頁看不到；不 SELECT 出來，
    /// 使用者就得到一個永遠讀不到的結果。
    /// </remarks>
    [Fact]
    public void 輸出參數用SELECT列出來()
    {
        var text = Build(
            new[]
            {
                new SqlStatementParameter("@ReaderId", "int", isOutput: false, isOptional: false),
                new SqlStatementParameter("@Total", "decimal(18,2)", isOutput: true, isOptional: false),
                new SqlStatementParameter("@Note", "nvarchar(200)", isOutput: true, isOptional: false)
            },
            out _);

        Assert.EndsWith(
            "SELECT @Total AS Total,\r\n" +
            "       @Note AS Note;",
            text);
    }

    /// <remarks>
    /// <c>DECLARE @x AS INT</c> 與 <c>DECLARE @x INT</c> 都合法，寫 <c>AS</c> 是為了
    /// 讓型別的位置在長度不一的參數名稱之間對齊起來。
    /// </remarks>
    [Fact]
    public void 宣告的型別前一律寫AS()
    {
        var text = Build(
            new[] { new SqlStatementParameter("@rc", "int", isOutput: true, isOptional: false) },
            out _);

        Assert.StartsWith("DECLARE @rc AS int = ", text);
    }

    /// <remarks>
    /// 沒有預設值可讀時才退回型別的預留值；中間那一個仍要拿到模組寫的 <c>7</c>。
    /// </remarks>
    [Fact]
    public void 有預設值就用預設值其餘依型別()
    {
        var text = Build(
            new[]
            {
                new SqlStatementParameter("@Name", "nvarchar(50)", isOutput: false, isOptional: false),
                new SqlStatementParameter("@Days", "int", isOutput: false, isOptional: true, "7"),
                new SqlStatementParameter("@Note", "nvarchar(200)", isOutput: false, isOptional: true)
            },
            out _);

        // 最寬的前綴是 @Note AS nvarchar(200)（30 字元），因此它後面只有一個空格。
        // @Note 沒有預設值，走的是型別的預留值——nvarchar 是 N'' 而不是 NULL。
        Assert.Contains("DECLARE @Name AS nvarchar(50)  = N'';", text, System.StringComparison.Ordinal);
        Assert.Contains("DECLARE @Days AS int           = 7;", text, System.StringComparison.Ordinal);
        Assert.Contains("DECLARE @Note AS nvarchar(200) = N'';", text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void 游標停在第一個宣告的初始值上()
    {
        var text = Build(Renew, out var caretOffset);

        // DECLARE @LoanId AS int              = 0;
        //                                       ^ 游標在這裡
        Assert.Equal("0;", text.Substring(caretOffset, 2));
    }

    /// <remarks>
    /// 統一改寫成 EXEC 也合法，但那是使用者沒有要求的改動——與展開萬用字元時
    /// 保留他自己寫的限定字是同一條。
    /// </remarks>
    [Theory]
    [InlineData("EXEC")]
    [InlineData("EXECUTE")]
    [InlineData("exec")]
    public void 照原文帶回EXEC關鍵字(string keyword)
    {
        var text = Build(
            new[] { new SqlStatementParameter("@ReaderId", "int", isOutput: false, isOptional: false) },
            out _,
            keyword: keyword);

        Assert.Contains($"\r\n{keyword} dbo.usp_Loan_Renew ", text, System.StringComparison.Ordinal);
    }

    /// <remarks>
    /// OUTPUT 參數在呼叫那一行要寫 <c>OUTPUT</c>——少了它就只是一般的傳值，
    /// 模組算出來的結果接不回來，而那一句執行得動。
    /// </remarks>
    [Fact]
    public void 呼叫時OUTPUT參數帶OUTPUT()
    {
        var text = Build(
            new[]
            {
                new SqlStatementParameter("@ReaderId", "int", isOutput: false, isOptional: false),
                new SqlStatementParameter("@Total", "decimal(18,2)", isOutput: true, isOptional: false)
            },
            out _);

        Assert.Contains("@Total = @Total OUTPUT", text, System.StringComparison.Ordinal);
        Assert.Contains("@ReaderId = @ReaderId,", text, System.StringComparison.Ordinal);
    }

    /// <remarks>
    /// 縮排裡有定位字元時，續行只補「EXEC 名稱 」那一段的寬度：
    /// 一個定位字元只算一個字元，把它算進續行的空白數就會歪掉。
    /// 縮排從第二行起才出現——第一行是使用者原本那一行，前面沒有任何前導文字。
    /// </remarks>
    [Fact]
    public void 定位字元縮排原樣重複()
    {
        var text = Build(
            new[]
            {
                new SqlStatementParameter("@ReaderId", "int", isOutput: false, isOptional: false),
                new SqlStatementParameter("@TagId", "int", isOutput: false, isOptional: false)
            },
            out _,
            indent: "\t");

        Assert.Equal(
            "DECLARE @ReaderId AS int = 0;\r\n" +
            "\tDECLARE @TagId AS int    = 0;\r\n" +
            "\tEXEC dbo.usp_Loan_Renew @ReaderId = @ReaderId, -- int\r\n" +
            "\t                        @TagId = @TagId        -- int",
            text);
    }
}
