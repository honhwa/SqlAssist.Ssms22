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
    /// 值取的是原文切片而不是詞法單元重組：重組會把 <c>N'a b'</c> 的空白與
    /// <c>'a''b'</c> 的跳脫字元寫成別的樣子，而這一段是要原樣填進宣告裡的。
    /// </remarks>
    [Fact]
    public void 預設值取原文()
    {
        var values = SqlModuleParameterDefaults.FindValues(@"
CREATE PROCEDURE dbo.usp_Loan_Renew
    @Days INT = 7,
    @Note NVARCHAR(200) = N'逾期 通知',
    @At DATETIME = GETDATE(),
    @Tag NVARCHAR(20) = 'a''b'
AS
SELECT 1");

        Assert.Equal("7", values["@Days"]);
        Assert.Equal("N'逾期 通知'", values["@Note"]);
        Assert.Equal("GETDATE()", values["@At"]);
        Assert.Equal("'a''b'", values["@Tag"]);
    }

    /// <remarks>
    /// <c>CONVERT(varchar(10), GETDATE(), 120)</c> 裡的逗號在括號內，深度與參數那一層
    /// 不同，所以不會把值切在函式的中間。值的深度必須從值本身重新起算——
    /// 沿用參數那一層的話，函式的右括號會被當成清單收尾。
    /// </remarks>
    [Fact]
    public void 值裡的函式逗號不算分隔()
    {
        var values = SqlModuleParameterDefaults.FindValues(@"
CREATE PROCEDURE dbo.usp_Loan_List
    @From DATETIME = CONVERT(varchar(10), GETDATE(), 120),
    @Days INT = 7
AS
SELECT 1");

        Assert.Equal("CONVERT(varchar(10), GETDATE(), 120)", values["@From"]);
        Assert.Equal("7", values["@Days"]);
    }

    /// <remarks>負數與括號都可能在值裡；兩者都不該把值切斷。</remarks>
    [Fact]
    public void 負數與括號的預設值()
    {
        var values = SqlModuleParameterDefaults.FindValues(
            "CREATE PROCEDURE dbo.usp_Loan_Adjust @Delta INT = -1, @Rate DECIMAL(9,4) = (0.05) AS SELECT 1");

        Assert.Equal("-1", values["@Delta"]);
        Assert.Equal("(0.05)", values["@Rate"]);
    }

    /// <remarks>
    /// 收尾的右括號要算進值裡。少算那個字元會切出 <c>GETDATE</c> 與 <c>(0.05</c>
    /// 這種當場語法錯誤的值，而錯誤會出現在展開後的宣告行上，看起來像展開壞了。
    /// </remarks>
    [Fact]
    public void 值尾端的右括號不會被吃掉()
    {
        var values = SqlModuleParameterDefaults.FindValues(
            "CREATE PROCEDURE dbo.usp_Loan_Stamp @At DATETIME = GETDATE(), @Next INT = 1 AS SELECT 1");

        Assert.Equal("GETDATE()", values["@At"]);
        Assert.Equal("1", values["@Next"]);
    }

    /// <remarks>
    /// <c>NULL</c> 是常見的預設值，而且它不是識別字也不是字串——取原文才不會
    /// 被型別預留值覆蓋掉。<c>N''</c> 的空字串同理：看得出使用者真的寫了空字串。
    /// </remarks>
    [Fact]
    public void NULL與空字串的預設值()
    {
        var values = SqlModuleParameterDefaults.FindValues(
            "CREATE PROCEDURE dbo.usp_Loan_Note @Note NVARCHAR(200) = NULL, @Tag VARCHAR(10) = 'x''y', @Empty NVARCHAR(10) = N'' AS SELECT 1");

        Assert.Equal("NULL", values["@Note"]);
        Assert.Equal("'x''y'", values["@Tag"]);
        Assert.Equal("N''", values["@Empty"]);
    }

    [Fact]
    public void 沒有預設值的參數不進字典()
    {
        var values = SqlModuleParameterDefaults.FindValues(
            "CREATE PROCEDURE dbo.usp_Loan_Renew @LoanId INT, @Days INT = 7 AS SELECT 1");

        Assert.False(values.ContainsKey("@LoanId"));
        Assert.Equal("7", values["@Days"]);
    }

    /// <remarks>
    /// 主體裡的 <c>SET @Days = 1</c> 不能被當成預設值：那會讓使用者拿一個
    /// 程序內部的暫存值當呼叫的起點，而那個值只在程序跑到那一行之後才成立。
    /// </remarks>
    [Fact]
    public void 主體裡的等號不進字典()
    {
        var values = SqlModuleParameterDefaults.FindValues(@"
CREATE PROCEDURE dbo.usp_Loan_Renew
    @LoanId INT
AS
BEGIN
    DECLARE @Days INT;
    SET @Days = 7;
END");

        Assert.Empty(values);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("這不是 SQL")]
    public void 讀不出定義時值是空的(string? definition)
    {
        Assert.Empty(SqlModuleParameterDefaults.FindValues(definition));
    }
}
