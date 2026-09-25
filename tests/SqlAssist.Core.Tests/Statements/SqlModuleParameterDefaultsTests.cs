using SqlAssist.Core.Statements;
using Xunit;

namespace SqlAssist.Core.Tests.Statements;

/// <remarks>
/// 這一份只能從定義本文讀出來：<c>sys.parameters.has_default_value</c> 對 T-SQL 模組
/// 永遠是 0，中繼資料層拿得到的就只有那一欄。
/// </remarks>
public sealed class SqlModuleParameterDefaultsTests
{
    [Fact]
    public void 找出寫了預設值的參數()
    {
        var defaults = SqlModuleParameterDefaults.Find(@"
CREATE PROCEDURE dbo.usp_Loan_Renew
    @LoanId INT,
    @Days INT = 7,
    @Note NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
END");

        Assert.DoesNotContain("@LoanId", defaults);
        Assert.Contains("@Days", defaults);
        Assert.Contains("@Note", defaults);
    }

    /// <remarks>
    /// <c>decimal(18,2)</c> 的逗號在括號裡，深度與參數本身不同，
    /// 只看「同一層的第一個逗號或等號」因此不會誤判。
    /// </remarks>
    [Fact]
    public void 型別裡的逗號不算參數分隔()
    {
        var defaults = SqlModuleParameterDefaults.Find(@"
CREATE PROCEDURE dbo.usp_Loan_Charge
    @Fee DECIMAL(18,2),
    @Rate DECIMAL(9,4) = 0.05
AS
SELECT 1");

        Assert.DoesNotContain("@Fee", defaults);
        Assert.Contains("@Rate", defaults);
    }

    /// <remarks>函式的參數清單包在括號裡，收掉那一對就代表清單結束。</remarks>
    [Fact]
    public void 認得函式的參數清單()
    {
        var defaults = SqlModuleParameterDefaults.Find(@"
CREATE OR ALTER FUNCTION dbo.fn_Loan_Fee (@Days INT, @Rate DECIMAL(9,4) = 0.05)
RETURNS DECIMAL(18,2)
AS
BEGIN
    DECLARE @Total DECIMAL(18,2) = 0;
    RETURN @Total;
END");

        Assert.DoesNotContain("@Days", defaults);
        Assert.Contains("@Rate", defaults);

        // 主體裡的 DECLARE @Total ... = 0 在參數清單之外，不該被算進來。
        Assert.DoesNotContain("@Total", defaults);
    }

    /// <remarks>
    /// 主體裡到處都是等號。掃描必須停在參數清單的結尾，否則
    /// <c>SET @Days = 1</c> 會讓一個必填參數被標成選擇性，而使用者會照著刪掉它。
    /// </remarks>
    [Fact]
    public void 主體裡的等號不算預設值()
    {
        var defaults = SqlModuleParameterDefaults.Find(@"
CREATE PROCEDURE dbo.usp_Loan_Renew
    @LoanId INT
AS
BEGIN
    DECLARE @Days INT;
    SET @Days = 7;
    UPDATE dbo.Loan SET DueDate = DATEADD(DAY, @Days, DueDate) WHERE LoanId = @LoanId;
END");

        Assert.Empty(defaults);
    }

    [Fact]
    public void 沒有參數時是空的()
    {
        Assert.Empty(SqlModuleParameterDefaults.Find(
            "CREATE PROCEDURE dbo.usp_Loan_Expire AS SELECT 1"));
    }

    /// <remarks>加密模組取不到定義；讀不出來就少標幾個「選擇性」，不猜。</remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("這不是 SQL")]
    public void 讀不出定義時是空的(string? definition)
    {
        Assert.Empty(SqlModuleParameterDefaults.Find(definition));
    }

    [Fact]
    public void 參數名稱比對不分大小寫()
    {
        var defaults = SqlModuleParameterDefaults.Find(
            "CREATE PROCEDURE dbo.usp_Tag_Read @tagId INT = 0 AS SELECT 1");

        Assert.Contains("@TAGID", defaults);
    }

    /// <remarks>
    /// <see cref="SqlModuleParameterDefaults.Resolve"/> 多帶回預設值本身，
    /// 展開成 <c>DECLARE</c> 時要拿它當初始值。
    /// </remarks>
    [Fact]
    public void 取回預設值的字面值()
    {
        var values = SqlModuleParameterDefaults.Resolve(@"
CREATE PROCEDURE dbo.usp_Loan_Renew
    @Days INT = 7,
    @Note NVARCHAR(200) = NULL,
    @Fee DECIMAL(18,2) = 0.05,
    @Tag NVARCHAR(50) = N'逾期',
    @Created DATETIME = GETDATE()
AS
SELECT 1");

        Assert.Equal("7", values["@Days"]);
        Assert.Equal("NULL", values["@Note"]);
        Assert.Equal("0.05", values["@Fee"]);
        Assert.Equal("N'逾期'", values["@Tag"]);
        Assert.Equal("GETDATE()", values["@Created"]);
    }

    /// <remarks>
    /// 運算式的預設值整組放棄。收成運算式的前半段（<c>1</c>、<c>'x'</c>）會組出
    /// 一個語法正確卻算錯的值，比留給使用者自己填糟糕得多。
    /// </remarks>
    [Theory]
    [InlineData("@A INT = 1 + 2")]
    [InlineData("@C NVARCHAR(10) = 'x' + 'y'")]
    [InlineData("@E DATETIME = GETDATE() + 1")]
    [InlineData("@F INT = -1")]
    public void 運算式的預設值不取(string declaration)
    {
        var values = SqlModuleParameterDefaults.Resolve(
            $"CREATE PROCEDURE dbo.usp_Calc {declaration} AS SELECT 1");

        Assert.Empty(values);
    }

    /// <remarks>
    /// <c>Token.Text</c> 只有名稱本身，少了那一對括號，
    /// <c>DECLARE @d DATETIME = GETDATE</c> 是語法錯誤。
    /// </remarks>
    [Fact]
    public void 函式呼叫的預設值連括號一起帶回()
    {
        var values = SqlModuleParameterDefaults.Resolve(
            "CREATE PROCEDURE dbo.usp_Touch @At DATETIME = GETDATE() AS SELECT 1");

        Assert.Equal("GETDATE()", values["@At"]);
    }

    [Fact]
    public void 識別字開頭但不完整的預設值不取()
    {
        var values = SqlModuleParameterDefaults.Resolve(
            "CREATE PROCEDURE dbo.usp_Flag @On BIT = ON OFF AS SELECT 1");

        Assert.Empty(values);
    }
}
