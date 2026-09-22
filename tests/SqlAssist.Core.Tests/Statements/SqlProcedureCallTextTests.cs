using System.Collections.Generic;
using SqlAssist.Core.Statements;
using Xunit;

namespace SqlAssist.Core.Tests.Statements;

public sealed class SqlProcedureCallTextTests
{
    private static readonly SqlStatementParameter[] Renew =
    {
        new("@LoanId", "int", isOutput: false, isOptional: false),
        new("@Days", "int", isOutput: false, isOptional: true, defaultValue: "7"),
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
    /// 宣告裡填的是模組定義寫的預設值（<c>@Days = 7</c>），沒寫的那兩個才退回型別預留值。
    /// 游標停在呼叫那一側的第一個值上——展開之後要做的第一件事就是從那裡開始改。
    /// </remarks>
    [Fact]
    public void 每個參數都先宣告並對齊到第一個參數()
    {
        var text = Build(Renew, out _);

        Assert.Equal(
            "DECLARE @LoanId int = 0;\r\n" +
            "DECLARE @Days int = 7;\r\n" +
            "DECLARE @NewDueDate datetime2(7) OUTPUT = NULL;\r\n" +
            "EXEC dbo.usp_Loan_Renew @LoanId = @LoanId,               -- int\r\n" +
            "                        @Days = @Days,                   -- int，選擇性\r\n" +
            "                        @NewDueDate = @NewDueDate OUTPUT -- datetime2(7)\r\n" +
            "\r\n" +
            "SELECT @NewDueDate AS NewDueDate;",
            text);
    }

    /// <remarks>
    /// 以前的規則是「只有 OUTPUT 才宣告」，理由是只有它非變數不可。改成一律宣告之後，
    /// 一般參數也住在有名字的變數裡，改值與重跑都只動宣告那一行。
    /// </remarks>
    [Fact]
    public void 沒有OUTPUT參數時仍然全部宣告()
    {
        var text = Build(
            new[] { new SqlStatementParameter("@ReaderId", "int", isOutput: false, isOptional: false) },
            out _);

        Assert.Equal(
            "DECLARE @ReaderId int = 0;\r\n" +
            "EXEC dbo.usp_Loan_Renew @ReaderId = @ReaderId -- int",
            text);
    }

    /// <remarks>
    /// 一個 OUTPUT 都沒有時整段不寫：沒有參數要看的 SELECT 只是雜訊，
    /// 而且使用者按一次執行就看到一行多餘的結果集。
    /// </remarks>
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

        Assert.DoesNotContain("SELECT", text);
    }

    /// <remarks>
    /// OUTPUT 參數的值是程序寫回來的，那正是執行完最想看的東西。兩個以上時每列一個，
    /// 續行的欄對齊在 SELECT 之後，而別名去掉了 <c>@</c>——結果集欄名帶小老鼠看起來
    /// 像個變數，不像欄。
    /// </remarks>
    [Fact]
    public void SELECT把每個OUTPUT參數各印一列()
    {
        var text = Build(
            new[]
            {
                new SqlStatementParameter("@Total", "decimal(18,2)", isOutput: true, isOptional: false),
                new SqlStatementParameter("@Note", "nvarchar(200)", isOutput: true, isOptional: true, defaultValue: "N''")
            },
            out _);

        Assert.EndsWith(
            "\r\n\r\n" +
            "SELECT @Total AS Total,\r\n" +
            "       @Note AS Note;",
            text);
    }

    [Fact]
    public void 游標停在第一個參數的值上()
    {
        var text = Build(Renew, out var caretOffset);

        Assert.Equal("@LoanId,", text.Substring(caretOffset, 8));
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

        Assert.Contains(keyword + " dbo.usp_Loan_Renew ", text);
    }

    /// <remarks>
    /// 縮排裡有定位字元時，續行只補「EXEC 名稱 」那一段的寬度：
    /// 一個定位字元只算一個字元，把它算進續行的空白數就會歪掉。
    ///
    /// 第一個宣告與 EXEC 那一行都不帶縮排——替換範圍從 <c>EXEC</c> 那個字起算，
    /// 行首的空白留在緩衝區裡沒被換掉，第一行再補一次就會多縮一格。
    /// 因此這裡期望的第一行是 <c>DECLARE</c> 開頭，<c>EXEC</c> 也從頭開始，
    /// 只有第二個宣告與第二個參數帶定位字元。
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
            "DECLARE @ReaderId int = 0;\r\n" +
            "\tDECLARE @TagId int = 0;\r\n" +
            "EXEC dbo.usp_Loan_Renew @ReaderId = @ReaderId, -- int\r\n" +
            "\t                        @TagId = @TagId        -- int",
            text);
    }

    /// <remarks>
    /// 定義讀不到時 DefaultValue 是 null，退回型別預留值而不是留空——
    /// 空的宣告是編譯錯誤，展開出來的東西連執行都執行不了。
    /// </remarks>
    [Fact]
    public void 沒有預設值時用型別預留值()
    {
        var text = Build(
            new[]
            {
                new SqlStatementParameter("@Name", "nvarchar(100)", isOutput: false, isOptional: false),
                new SqlStatementParameter("@DueDate", "date", isOutput: false, isOptional: false)
            },
            out _);

        Assert.Contains("DECLARE @Name nvarchar(100) = N'';", text);
        Assert.Contains("DECLARE @DueDate date = NULL;", text);
    }
}
