using System;
using SqlAssist.Core.Rewriting;
using Xunit;

namespace SqlAssist.Core.Tests.Rewriting;

public sealed class SqlSchemaQualificationTests
{
    private static string Qualify(string sql) => SqlSchemaQualification.Qualify(sql).Text;

    private static int Count(string sql) => SqlSchemaQualification.Qualify(sql).AffectedCount;

    /// <remarks>
    /// 這一組是「哪些字後面接的是物件名稱」。漏掉任何一種的症狀是那一族寫法整批
    /// 補不到，而使用者只會覺得「有時候有效」——名單少一個字與分析器壞掉看起來一樣。
    ///
    /// 每一列都同時驗證「該補的補了」與「不該動的沒動」：<c>ON</c> 之後的述詞、
    /// 別名後面的欄位限定字都在同一段文字裡。
    /// </remarks>
    [Theory]
    [InlineData("SELECT * FROM Loan", "SELECT * FROM dbo.Loan")]
    [InlineData("SELECT * FROM Lib_Reader u JOIN Loan ON u.CopyNo = Loan.CopyNo",
        "SELECT * FROM dbo.Lib_Reader u JOIN dbo.Loan ON u.CopyNo = Loan.CopyNo")]
    [InlineData("SELECT * FROM Lib_Reader u CROSS APPLY LoanByReader(u.CopyNo) f",
        "SELECT * FROM dbo.Lib_Reader u CROSS APPLY dbo.LoanByReader(u.CopyNo) f")]
    [InlineData("INSERT INTO Loan (CopyNo) VALUES (1)", "INSERT INTO dbo.Loan (CopyNo) VALUES (1)")]
    [InlineData("SELECT * INTO Loan FROM PUBLISHER", "SELECT * INTO dbo.Loan FROM dbo.PUBLISHER")]
    [InlineData("UPDATE Loan SET CopyNo = 1 WHERE CopyNo = 2", "UPDATE dbo.Loan SET CopyNo = 1 WHERE CopyNo = 2")]
    [InlineData("UPDATE TOP (10) Loan SET CopyNo = 1", "UPDATE TOP (10) dbo.Loan SET CopyNo = 1")]
    [InlineData("DELETE FROM Loan WHERE CopyNo = 1", "DELETE FROM dbo.Loan WHERE CopyNo = 1")]
    [InlineData("DELETE Loan WHERE CopyNo = 1", "DELETE dbo.Loan WHERE CopyNo = 1")]
    [InlineData("EXEC LoanByReader 1", "EXEC dbo.LoanByReader 1")]
    [InlineData("EXEC @rc = LoanByReader 1", "EXEC @rc = dbo.LoanByReader 1")]
    [InlineData("CREATE TABLE Loan (CopyNo INT)", "CREATE TABLE dbo.Loan (CopyNo INT)")]
    [InlineData("TRUNCATE TABLE Loan", "TRUNCATE TABLE dbo.Loan")]
    [InlineData("ALTER VIEW LoanView AS SELECT 1 AS a", "ALTER VIEW dbo.LoanView AS SELECT 1 AS a")]
    [InlineData("DROP PROCEDURE LoanByReader", "DROP PROCEDURE dbo.LoanByReader")]
    [InlineData("DROP FUNCTION LoanCount", "DROP FUNCTION dbo.LoanCount")]
    [InlineData("CREATE INDEX ix ON Loan (CopyNo)", "CREATE INDEX ix ON dbo.Loan (CopyNo)")]
    [InlineData("select * from Loan", "select * from dbo.Loan")]
    [InlineData("SELECT * FROM Loan WHERE CopyNo = 'dbo.Loan'",
        "SELECT * FROM dbo.Loan WHERE CopyNo = 'dbo.Loan'")]
    public void 名稱位置補上結構描述(string sql, string expected)
    {
        Assert.Equal(expected, Qualify(sql));
    }

    /// <remarks>
    /// <c>MERGE</c> 的目標與來源在同一個敘述裡，而 <c>INTO</c> 與 <c>USING</c>
    /// 指的是兩個不同的位置——只看其中一個的話會少補一處，而且少的那一處
    /// 正好是使用者最不容易自己發現的那一邊。
    ///
    /// <c>ON Loan.CopyNo</c> 的 <c>Loan</c> 是<b>欄位限定字</b>而不是資料來源，
    /// 所以那句裡的 <c>ON</c> 不是 DDL 的 <c>ON</c>，一個字都不該動。
    /// </remarks>
    [Fact]
    public void MERGE的目標與來源都補()
    {
        Assert.Equal(
            "MERGE INTO dbo.Loan USING dbo.Loan AS s ON Loan.CopyNo = s.CopyNo",
            Qualify("MERGE INTO Loan USING Loan AS s ON Loan.CopyNo = s.CopyNo"));
    }

    /// <remarks>
    /// 已經帶了限定字就不動——這是「同一個名稱只會被處理一次」的保證。
    /// 多補一段的症狀是 <c>dbo.dbo.Loan</c>，那比不補更難看出是工具做的。
    /// </remarks>
    [Theory]
    [InlineData("SELECT * FROM dbo.Loan")]
    [InlineData("SELECT * FROM LibArchive.dbo.Loan")]
    [InlineData("SELECT * FROM LIBSQL02.LibArchive.dbo.Loan")]
    [InlineData("SELECT * FROM sys.objects")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.TABLES")]
    public void 已有結構描述時不動(string sql)
    {
        Assert.Equal(sql, Qualify(sql));
        Assert.Equal(0, Count(sql));
    }

    /// <remarks>
    /// <c>db..obj</c> 的意思是「這個資料庫，結構描述照預設解析」，而那份指令碼的物件
    /// 就在 <c>dbo</c>。把空的那一段換成 <c>dbo</c> 才讓這個名稱前後一致；
    /// 不動它的話同一份指令碼裡兩種寫法並存，看不出哪一種才是這裡的慣例。
    /// </remarks>
    [Theory]
    [InlineData("SELECT * FROM LibArchive..Loan", "SELECT * FROM LibArchive.dbo.Loan")]
    [InlineData("SELECT * FROM LIBSQL02.LibArchive..Loan", "SELECT * FROM LIBSQL02.LibArchive.dbo.Loan")]
    [InlineData("SELECT * FROM ..Loan", "SELECT * FROM dbo.Loan")]
    [InlineData("SELECT * FROM [LibArchive]..[Loan]", "SELECT * FROM [LibArchive].dbo.[Loan]")]
    public void 空的中間段補成dbo(string sql, string expected)
    {
        Assert.Equal(expected, Qualify(sql));
        Assert.Equal(1, Count(sql));
    }

    /// <remarks>
    /// 字串與註解裡的文字不是 SQL。動到它們就是把註解改壞、把資料改壞，而那種改動
    /// 完全不會有徵兆——使用者只會在某一天發現某段動態 SQL 的字面值變了。
    /// </remarks>
    [Theory]
    [InlineData("SELECT 'FROM Loan' AS x")]
    [InlineData("SELECT 1 AS x -- FROM Loan")]
    [InlineData("SELECT 1 AS x /* FROM Loan */")]
    public void 字串與註解不動(string sql)
    {
        Assert.Equal(sql, Qualify(sql));
    }

    /// <remarks>
    /// 暫存表、資料表變數與 CTE 都只存在於這份指令碼裡，前面補上 <c>dbo.</c> 之後
    /// 三個都會找不到。CTE 尤其像：<c>FROM cte</c> 與 <c>FROM Loan</c> 形狀一模一樣。
    /// </remarks>
    [Theory]
    [InlineData("SELECT * FROM #tmp")]
    [InlineData("SELECT * FROM ##tmp")]
    [InlineData("INSERT INTO @rows (CopyNo) VALUES (1)")]
    public void 指令碼內的名稱不動(string sql)
    {
        Assert.Equal(sql, Qualify(sql));
    }

    /// <remarks>
    /// CTE 名稱不動，但 CTE 主體裡的來源要動——整組跳過的話，
    /// <c>WITH … AS (SELECT … FROM Loan)</c> 這種最常見的寫法會整段漏掉。
    /// </remarks>
    [Fact]
    public void CTE名稱不動但主體的來源要補()
    {
        Assert.Equal(
            "WITH cte AS (SELECT CopyNo FROM dbo.Loan) SELECT * FROM cte",
            Qualify("WITH cte AS (SELECT CopyNo FROM Loan) SELECT * FROM cte"));
    }

    /// <remarks>
    /// <c>UPDATE a SET … FROM dbo.Loan a</c> 的 <c>a</c> 是別名不是物件。補成
    /// <c>dbo.a</c> 之後那句 UPDATE 會指到一個不存在的物件——比不補更糟，
    /// 而且從文字上看完全合理。
    /// </remarks>
    [Theory]
    [InlineData("UPDATE a SET a.CopyNo = 1 FROM dbo.Loan a")]
    [InlineData("DELETE FROM a FROM dbo.Loan a")]
    [InlineData("UPDATE a SET a.CopyNo = 1 FROM dbo.Loan AS a")]
    public void 別名不動(string sql)
    {
        Assert.Equal(sql, Qualify(sql));
    }

    /// <remarks>
    /// <c>ON DELETE CASCADE</c> 與 <c>ON UPDATE NO ACTION</c> 的 <c>CASCADE</c> 就在
    /// <c>DELETE</c>／<c>UPDATE</c> 後面，而外鍵的 DDL 幾乎都寫成一行。
    /// 少了敘述開頭的限制就會補出 <c>dbo.CASCADE</c>。
    /// </remarks>
    [Fact]
    public void 外鍵的動作子句不動()
    {
        const string sql =
            "ALTER TABLE Loan ADD CONSTRAINT fk_loan FOREIGN KEY (CopyNo) REFERENCES Copy (CopyNo) ON DELETE CASCADE";

        Assert.Equal(
            "ALTER TABLE dbo.Loan ADD CONSTRAINT fk_loan FOREIGN KEY (CopyNo) REFERENCES dbo.Copy (CopyNo) ON DELETE CASCADE",
            Qualify(sql));
    }

    /// <remarks>
    /// <c>sp_</c> 與 <c>xp_</c> 兩族住在 <c>master</c> 的 <c>sys</c> 結構描述，
    /// 補成 <c>dbo.sp_help</c> 會直接找不到。使用者把這條命令按下去時期待的是
    /// 「整理我的指令碼」，不是「把系統程序改壞」。
    /// </remarks>
    [Theory]
    [InlineData("EXEC sp_help 'Loan'")]
    [InlineData("EXEC xp_cmdshell 'dir'")]
    [InlineData("EXEC master.dbo.sp_executesql N'SELECT 1'")]
    public void 系統程序不動(string sql)
    {
        Assert.Equal(sql, Qualify(sql));
    }

    /// <remarks>
    /// <c>OPENJSON</c>、<c>STRING_SPLIT</c> 這些是內建函式而不是使用者自訂的
    /// 資料表值函式。兩者在 <c>FROM</c> 後面的形狀一樣，只有名字分得出來。
    /// </remarks>
    [Theory]
    [InlineData("SELECT * FROM OPENJSON(@json)")]
    [InlineData("SELECT * FROM OPENDATASOURCE('SQLNCLI', 'x').LibArchive.dbo.Loan")]
    [InlineData("SELECT * FROM STRING_SPLIT('A,B', ',')")]
    [InlineData("SELECT value FROM OPENJSON(@json) WITH (CopyNo INT)")]
    public void 內建的資料來源函式不動(string sql)
    {
        Assert.Equal(sql, Qualify(sql));
    }

    /// <remarks>
    /// 自訂的資料表值函式沒有帶結構描述時要補。它與內建函式的差別只有「名字在不在
    /// 內建清單裡」，所以這一條與上一條必須成對看。
    /// </remarks>
    [Fact]
    public void 自訂的資料表值函式要補()
    {
        Assert.Equal(
            "SELECT * FROM dbo.LoanByReader(1) AS f",
            Qualify("SELECT * FROM LoanByReader(1) AS f"));
    }

    /// <remarks>
    /// 加引號的名稱也是名稱：<c>[My Table]</c> 一樣是那張沒帶結構描述的表。
    /// 關鍵字與型別名的排除不能連引號名稱一起跳過。
    /// </remarks>
    [Fact]
    public void 加引號的名稱也補()
    {
        Assert.Equal("SELECT * FROM dbo.[My Table]", Qualify("SELECT * FROM [My Table]"));
        Assert.Equal("SELECT * FROM dbo.\"Loan\"", Qualify("SELECT * FROM \"Loan\""));
    }

    /// <remarks>
    /// 巢狀子查詢的來源也要補。只補外層會留下半份，而使用者選取整份文件時
    /// 期待的是整份都整理過。
    /// </remarks>
    [Fact]
    public void 巢狀子查詢也補()
    {
        Assert.Equal(
            "SELECT * FROM (SELECT * FROM dbo.Loan) x WHERE x.CopyNo IN (SELECT CopyNo FROM dbo.Copy)",
            Qualify("SELECT * FROM (SELECT * FROM Loan) x WHERE x.CopyNo IN (SELECT CopyNo FROM Copy)"));
    }

    /// <remarks>
    /// <c>FROM</c> 後面的逗號清單也要走進去；只認緊接在 <c>FROM</c> 後面那一個的話，
    /// 逗號之後的來源整批補不到。
    /// </remarks>
    [Fact]
    public void 逗號分隔的來源清單都補()
    {
        Assert.Equal("SELECT * FROM dbo.Loan, dbo.Copy", Qualify("SELECT * FROM Loan, Copy"));
        Assert.Equal("SELECT * FROM dbo.Loan l, dbo.Copy c", Qualify("SELECT * FROM Loan l, Copy c"));
    }

    /// <remarks>
    /// 欄位限定字（<c>u.CopyNo</c>）不是物件名稱。補錯的話每一行欄位都會變成
    /// <c>dbo.u.CopyNo</c>，那整份指令碼都不能執行了。
    /// </remarks>
    [Fact]
    public void 欄位限定字不動()
    {
        Assert.Equal(
            "SELECT u.CopyNo, b.PublCode FROM dbo.Loan u JOIN dbo.PUBLISHER b ON u.PublCode = b.PublCode",
            Qualify("SELECT u.CopyNo, b.PublCode FROM Loan u JOIN PUBLISHER b ON u.PublCode = b.PublCode"));
    }

    /// <remarks>
    /// 多處一起改時位置全部以原文為準。邊算邊改字串的話第二處之後會偏掉，
    /// 而症狀只像是「有幾處沒補到」。
    /// </remarks>
    [Fact]
    public void 多處一次改寫()
    {
        var result = SqlSchemaQualification.Qualify(
            "SELECT * FROM Loan;\r\nSELECT * FROM Copy;\r\nEXEC LoanByReader 1");

        Assert.Equal(
            "SELECT * FROM dbo.Loan;\r\nSELECT * FROM dbo.Copy;\r\nEXEC dbo.LoanByReader 1",
            result.Text);
        Assert.Equal(3, result.AffectedCount);
    }

    /// <remarks>
    /// 游標要跟著它所在的那個名稱一起往後移。不換算的話，在名稱結尾按完右鍵，
    /// 游標會留在補進去的 <c>dbo.</c> 之前——接著打的字就落在名稱中間。
    /// </remarks>
    [Fact]
    public void 游標跟著名稱往後移()
    {
        const string sql = "SELECT * FROM Loan";
        var start = sql.IndexOf("Loan", StringComparison.Ordinal);

        // 游標在名稱結尾（這裡也是文件結尾）：整段往後移四個字元。
        Assert.Equal(sql.Length + 4, SqlSchemaQualification.Qualify(sql, sql.Length).CaretPosition);

        // 游標正好在名稱開頭：插入的文字接在游標後面，游標留在原地。
        Assert.Equal(start, SqlSchemaQualification.Qualify(sql, start).CaretPosition);

        // 沒指定游標時維持「不處理」。
        Assert.Equal(-1, SqlSchemaQualification.Qualify(sql).CaretPosition);
    }

    /// <remarks>
    /// 一處都補不到時必須回「沒有改動」，呼叫端才不會把同一份文字再寫回緩衝區
    /// ——那會清掉使用者的復原歷程。
    /// </remarks>
    [Fact]
    public void 沒有可補的地方時不改動()
    {
        const string sql = "SELECT * FROM dbo.Loan;";

        var result = SqlSchemaQualification.Qualify(sql);

        Assert.False(result.HasChanges);
        Assert.Equal(0, result.AffectedCount);
        Assert.Equal(sql, result.Text);
    }

    /// <remarks>空文字不該丟例外；使用者在空白查詢視窗上按右鍵是常態。</remarks>
    [Fact]
    public void 空文字不丟例外()
    {
        var result = SqlSchemaQualification.Qualify(string.Empty);

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(0, result.AffectedCount);
    }
}
