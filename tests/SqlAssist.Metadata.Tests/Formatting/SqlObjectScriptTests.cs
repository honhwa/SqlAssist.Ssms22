using System;
using SqlAssist.Core.Scripting;
using SqlAssist.Metadata.Formatting;
using SqlAssist.Metadata.Model;
using Xunit;

namespace SqlAssist.Metadata.Tests.Formatting;

/// <summary>
/// F12 送進新查詢視窗的那一份指令碼。
/// </summary>
/// <remarks>
/// 這裡固定四件事：開頭有沒有指名資料庫、批次分隔對不對、模組才改寫成 ALTER、
/// 以及游標停在名稱之後。前三件錯了指令碼就執行不了，第四件錯了只是難用——
/// 但四件都不會在編譯時被發現。
/// </remarks>
public sealed class SqlObjectScriptTests
{
    /// <remarks>
    /// 兩個 SET 各自是一個敘述，因此各自跟著一個 GO——F12 那條路把
    /// <c>BatchSeparation</c> 開著，而 <c>ALTER PROCEDURE</c> 必須是批次裡的
    /// 第一個敘述。順序照 SSMS 的「編寫指令碼為」。
    /// </remarks>
    private const string Header =
        "SET ANSI_NULLS ON\r\nGO\r\nSET QUOTED_IDENTIFIER ON\r\nGO\r\n";

    /// <summary>F12 送進新查詢視窗用的那一組選項。</summary>
    /// <remarks>
    /// 資料庫與伺服器預設都不指名，所以除了專講 <c>USE</c> 的那幾條之外，
    /// 其餘案例的輸出裡不會多出開頭那一行。
    /// </remarks>
    private static SqlScriptContext Execution(
        string? newLine,
        string? databaseName = null,
        string? serverName = null,
        bool includeDatabaseContext = true) =>
        new(
            SqlScriptOptions.Fidelity with
            {
                SetOptions = SqlSetOptionOutput.AlwaysOn,
                BatchSeparation = SqlBatchSeparation.BetweenStatements,
                ModuleStatement = SqlModuleStatement.Alter,
                IncludeDatabaseContext = includeDatabaseContext
            },
            newLine: newLine,
            serverName: serverName,
            databaseName: databaseName);

    private static SqlObjectStructure Module(
        SqlObjectKind kind,
        string name,
        string? definition,
        string? databaseName = null)
    {
        return new SqlObjectStructure(
            new SqlObjectDetail(
                new SqlObjectInfo(1, "dbo", name, kind, databaseName),
                definition: definition));
    }

    private static SqlObjectStructure Table()
    {
        return new SqlObjectStructure(
            new SqlObjectDetail(
                new SqlObjectInfo(2, "dbo", "Lib_Reader", SqlObjectKind.Table),
                new[]
                {
                    new SqlColumnInfo(1, "Id", "int", false, isIdentity: true, isPrimaryKey: true),
                    new SqlColumnInfo(2, "DisplayName", "nvarchar(60)", false)
                }));
    }

    [Fact]
    public void 模組的CREATE改寫成ALTER並包進批次樣板()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.Procedure, "usp_LoanFinish", "CREATE PROCEDURE dbo.usp_LoanFinish\r\nAS\r\nSELECT 1;"),
            Execution("\r\n"));

        Assert.Equal(
            Header + "ALTER PROCEDURE dbo.usp_LoanFinish\r\nAS\r\nSELECT 1;\r\nGO\r\n",
            script.Text);
    }

    [Fact]
    public void CREATE_OR_ALTER併成單一個ALTER()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.View, "v_LoanDetail", "CREATE OR ALTER VIEW dbo.v_LoanDetail AS SELECT 1 AS x;"),
            Execution("\r\n"));

        Assert.Contains("ALTER VIEW dbo.v_LoanDetail", script.Text);
        Assert.DoesNotContain("CREATE", script.Text);
    }

    /// <remarks>
    /// 資料表沒有對應的 ALTER TABLE 整體寫法。改寫下去得到的是一段執行不了的
    /// 指令碼，而且是執行到一半才失敗的那一種。
    /// </remarks>
    [Fact]
    public void 資料表維持CREATE_TABLE()
    {
        var script = SqlObjectScript.BuildEditable(Table(), Execution("\r\n"));

        Assert.StartsWith(Header + "CREATE TABLE [dbo].[Lib_Reader]", script.Text);
        Assert.EndsWith("GO\r\n", script.Text);
    }

    /// <remarks>
    /// 檢視同時是模組也有欄位。定義取不到時 BuildScript 已經把整段換成註解，
    /// 這裡要確認的是那段註解原樣帶出來，沒有被當成 CREATE 改寫掉。
    /// </remarks>
    [Fact]
    public void 取不到定義時整段註解原樣保留()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.View, "v_LoanDetail", definition: null),
            Execution("\r\n"));

        Assert.StartsWith("-- 取不到 [dbo].[v_LoanDetail] 的定義。", script.Text);
        Assert.Contains("WITH ENCRYPTION", script.Text);
        Assert.DoesNotContain("CREATE TABLE", script.Text);
    }

    /// <remarks>
    /// 欄位查得回來與否是這一輪的事，不是種類的事：資料表過得了種類那一關，
    /// 卻可能一列都沒有回來（物件被卸除、權限被收回）。BuildScript 那一端已經
    /// 換成整段註解，這裡要確認的是它原樣帶出來，沒有被當成 CREATE 改寫掉，
    /// 也沒有留下一段只剩空括號、卻仍然貼得上去的 CREATE TABLE。
    /// </remarks>
    [Fact]
    public void 取不到欄位的資料表整段註解()
    {
        var script = SqlObjectScript.BuildEditable(
            new SqlObjectStructure(
                new SqlObjectDetail(new SqlObjectInfo(5, "dbo", "Lib_Tag", SqlObjectKind.Table))),
            Execution("\r\n"));

        Assert.StartsWith("-- 取不到 [dbo].[Lib_Tag] 的欄位。", script.Text);
        Assert.Contains("sys.columns", script.Text);
        Assert.DoesNotContain("CREATE TABLE", script.Text);
    }

    /// <remarks>
    /// 資料表型別有欄位，落到資料表那一支就會被寫成 CREATE TABLE——照著執行
    /// 會多出一張同名的資料表。與檢視取不到定義時不能掉進 CREATE TABLE 同一條理由。
    ///
    /// 這一條與浮動預覽的指令碼分頁走同一個判斷（<c>HasExecutableScript</c>），
    /// 所以 F12 拿到的就是 BuildScript 組出來的 CREATE TYPE，只多包了批次樣板。
    /// 兩邊各留一份判斷的症狀，就是同一個型別在預覽是 CREATE TABLE、在這裡是註解。
    /// </remarks>
    [Fact]
    public void 資料表型別寫成CREATE_TYPE()
    {
        var script = SqlObjectScript.BuildEditable(
            new SqlObjectStructure(
                new SqlObjectDetail(
                    new SqlObjectInfo(3, "dbo", "LoanIdList", SqlObjectKind.TableType),
                    new[] { new SqlColumnInfo(1, "LoanId", "int", false) })),
            Execution("\r\n"));

        Assert.StartsWith(Header + "CREATE TYPE [dbo].[LoanIdList] AS TABLE", script.Text);
        Assert.Contains("[LoanId] int NOT NULL", script.Text);
        Assert.EndsWith("GO\r\n", script.Text);
        Assert.DoesNotContain("CREATE TABLE", script.Text);

        // 型別沒有 ALTER 的整體寫法，開頭的 CREATE 不可以被改寫掉。
        Assert.DoesNotContain("ALTER", script.Text);
    }

    /// <remarks>
    /// 同義字與序列沒有 <c>ALTER</c> 的整體寫法，維持 <c>CREATE</c>——
    /// 改寫成 ALTER 得到的是一段執行到一半才失敗的指令碼。
    /// </remarks>
    [Theory]
    [InlineData(SqlObjectKind.Synonym, "CREATE SYNONYM [dbo].[syn_Loan]\r\nFOR [Lib].[dbo].[Loan];")]
    [InlineData(SqlObjectKind.Sequence, "CREATE SEQUENCE [dbo].[seq_LoanNo]\r\n    AS int;")]
    public void 同義字與序列維持CREATE(SqlObjectKind kind, string definition)
    {
        var script = SqlObjectScript.BuildEditable(
            new SqlObjectStructure(
                new SqlObjectDetail(new SqlObjectInfo(5, "dbo", "obj", kind), definition: definition)),
            Execution("\r\n"));

        Assert.StartsWith(Header + definition, script.Text);
        Assert.EndsWith("GO\r\n", script.Text);
        Assert.DoesNotContain("ALTER", script.Text);
        Assert.DoesNotContain("--", script.Text);
    }

    /// <remarks>
    /// 查不到 <c>sys.synonyms</c> 那一列時（物件被卸除、權限被收回）就沒有定義，
    /// 而 BuildScript 給的那段說明不是 T-SQL——原樣送進查詢視窗就是一執行就錯。
    /// </remarks>
    [Fact]
    public void 取不到指向的同義字整段註解掉()
    {
        var script = SqlObjectScript.BuildEditable(
            new SqlObjectStructure(
                new SqlObjectDetail(new SqlObjectInfo(4, "dbo", "syn_Loan", SqlObjectKind.Synonym))),
            Execution("\r\n"));

        foreach (var line in script.Text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.True(
                line.StartsWith("--", StringComparison.Ordinal) ||
                line is "SET ANSI_NULLS ON" or "SET QUOTED_IDENTIFIER ON" or "GO",
                $"這一行不是註解也不是樣板：{line}");
        }
    }

    [Fact]
    public void 游標停在標頭的物件名稱之後()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.Procedure, "usp_LoanFinish", "CREATE PROCEDURE dbo.usp_LoanFinish\r\n@Id int\r\nAS\r\nSELECT 1;"),
            Execution("\r\n"));

        Assert.Equal(
            Header + "ALTER PROCEDURE dbo.usp_LoanFinish",
            script.Text.Substring(0, script.CaretOffset));
    }

    /// <remarks>
    /// 認不出標頭時停在本體的第一個字元，不是停在結尾——停在結尾等於
    /// 一打開就被捲到最後一行。
    /// </remarks>
    [Fact]
    public void 認不出標頭時停在本體開頭()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.Procedure, "usp_LoanFinish", definition: null),
            Execution("\r\n"));

        // 整段是註解，前面沒有 SET 批次可以跳過，所以停在第一個字元。
        Assert.Equal(0, script.CaretOffset);
    }

    /// <remarks>
    /// 資料庫裡存的定義用哪一種換行完全看當初是誰建的，而樣板是本擴充寫死的。
    /// 不統一的話兩者會在同一份檔案裡混著出現。
    /// </remarks>
    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void 樣板與定義的換行統一成同一種(string newLine)
    {
        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.Procedure, "usp_LoanFinish", "CREATE PROCEDURE dbo.usp_LoanFinish\nAS\rSELECT 1;\r\nRETURN;"),
            Execution(newLine));

        // 兩個 SET 敘述各佔 2 行、定義內 3 個、定義結尾補 1 個、結尾的 GO 1 個。
        Assert.Equal(9, script.Text.Split(new[] { newLine }, StringSplitOptions.None).Length - 1);

        var remainder = script.Text.Replace(newLine, " ");
        Assert.DoesNotContain('\r', remainder);
        Assert.DoesNotContain('\n', remainder);
    }

    [Fact]
    public void 認不得的換行退回作業系統預設值()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.Procedure, "usp_LoanFinish", "CREATE PROCEDURE dbo.usp_LoanFinish AS SELECT 1;"),
            Execution(" "));

        Assert.StartsWith("SET ANSI_NULLS ON" + Environment.NewLine, script.Text);
    }

    /// <remarks>已經是 ALTER 的定義不能再被動一次，否則關鍵字會被吃掉。</remarks>
    [Fact]
    public void 已經是ALTER的定義原樣帶出()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.Procedure, "usp_LoanFinish", "ALTER PROCEDURE dbo.usp_LoanFinish AS SELECT 1;"),
            Execution("\r\n"));

        Assert.Equal(
            Header + "ALTER PROCEDURE dbo.usp_LoanFinish AS SELECT 1;\r\nGO\r\n",
            script.Text);
    }

    /// <remarks>
    /// 定義本身以換行結尾時不要多墊一行；GO 前面固定只有一個換行。
    /// </remarks>
    [Fact]
    public void 定義結尾已有換行時不重複墊行()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.Procedure, "usp_LoanFinish", "CREATE PROCEDURE dbo.usp_LoanFinish AS SELECT 1;\r\n"),
            Execution("\r\n"));

        Assert.EndsWith("SELECT 1;\r\nGO\r\n", script.Text);
    }

    /// <summary>
    /// 指名資料庫時開頭寫一行 <c>USE</c>，而且自己一個批次。
    /// </summary>
    /// <remarks>
    /// 新的查詢視窗只沿用<b>來源</b>視窗那條連線，而定義本身不帶資料庫。少了這一行，
    /// 游標停在 <c>LibArchive.dbo.usp_LoanFinish</c> 按 F12 之後再按 F5，改的是目前
    /// 資料庫裡同名的那一個，或直接失敗，而畫面上看不出兩者的差別。
    /// </remarks>
    [Fact]
    public void 指名資料庫時開頭寫USE()
    {
        const string definition = "CREATE PROCEDURE dbo.usp_LoanFinish AS SELECT 1;";

        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.Procedure, "usp_LoanFinish", definition, databaseName: "LibArchive"),
            Execution("\r\n", databaseName: "LibArchive"));

        Assert.Equal(
            "USE [LibArchive]\r\nGO\r\n" + Header +
            "ALTER PROCEDURE dbo.usp_LoanFinish AS SELECT 1;\r\nGO\r\n",
            script.Text);
    }

    /// <remarks>
    /// 方括號不是可選的排版：資料庫名稱只要是合法識別字就帶得動，而跳脫規則與其他
    /// 識別字同一份——不跳脫的話 <c>USE [Lib]Archive]</c> 是一個壞掉的敘述，
    /// 而 <c>SqlIdentifier</c> 以外的寫法不會知道這件事。
    /// </remarks>
    [Fact]
    public void 資料庫名稱加方括號並跳脫()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(
                SqlObjectKind.Procedure,
                "usp_LoanFinish",
                "CREATE PROCEDURE dbo.usp_LoanFinish AS SELECT 1;",
                databaseName: "Lib]Archive"),
            Execution("\r\n", databaseName: "Lib]Archive"));

        Assert.StartsWith("USE [Lib]]Archive]\r\nGO\r\n", script.Text);
    }

    /// <remarks>
    /// 開頭那一行沒有被跳過的話，<c>SqlModuleScript.FindHeaderNameEnd</c> 找不到
    /// <c>CREATE</c>／<c>ALTER</c>，游標會落在整份指令碼的最前面——那等於一打開
    /// 就被丟回第一行。
    /// </remarks>
    [Fact]
    public void 有USE時游標仍然停在名稱之後()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(
                SqlObjectKind.Procedure,
                "usp_LoanFinish",
                "CREATE PROCEDURE dbo.usp_LoanFinish\r\n@Id int\r\nAS\r\nSELECT 1;",
                databaseName: "LibArchive"),
            Execution("\r\n", databaseName: "LibArchive"));

        Assert.Equal(
            "USE [LibArchive]\r\nGO\r\n" + Header + "ALTER PROCEDURE dbo.usp_LoanFinish",
            script.Text.Substring(0, script.CaretOffset));
    }

    /// <summary>
    /// 連結伺服器上的物件不寫 <c>USE</c>。
    /// </summary>
    /// <remarks>
    /// <c>USE</c> 只換得動本機連線的資料庫，寫了會切到本機同名的資料庫——比不寫更糟，
    /// 因為它會安靜地成功。這種定義本來就沒辦法在這裡執行（要 <c>EXEC … AT</c>）。
    /// </remarks>
    [Fact]
    public void 連結伺服器上的物件不寫USE()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(
                SqlObjectKind.Procedure,
                "usp_LoanFinish",
                "CREATE PROCEDURE dbo.usp_LoanFinish AS SELECT 1;",
                databaseName: "LibArchive"),
            Execution("\r\n", databaseName: "LibArchive", serverName: "LibMirror"));

        Assert.StartsWith(Header, script.Text);
        Assert.DoesNotContain("USE [", script.Text);
    }

    /// <summary>
    /// 沒有資料庫名稱時不寫 <c>USE</c>。
    /// </summary>
    /// <remarks>
    /// 指令碼自己宣告的暫存資料表與資料表變數沒有資料庫可言，而連線還沒選定資料庫時
    /// 名稱是空字串。兩種都不能拿來組 <c>USE</c>——空字串組出來的是兩個方括號。
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 沒有資料庫名稱時不寫USE(string? databaseName)
    {
        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.Procedure, "usp_LoanFinish", "CREATE PROCEDURE dbo.usp_LoanFinish AS SELECT 1;"),
            Execution("\r\n", databaseName: databaseName));

        Assert.StartsWith(Header, script.Text);
        Assert.DoesNotContain("USE [", script.Text);
    }

    /// <remarks>
    /// 唯讀的預覽表面顯示的是「這個物件的定義」，而 <c>USE</c> 不屬於定義——
    /// 那一條走 <c>SqlScriptPreferences.Create</c>，這一項維持預設的 <c>false</c>。
    /// </remarks>
    [Fact]
    public void 選項關閉時不寫USE()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(
                SqlObjectKind.Procedure,
                "usp_LoanFinish",
                "CREATE PROCEDURE dbo.usp_LoanFinish AS SELECT 1;",
                databaseName: "LibArchive"),
            Execution("\r\n", databaseName: "LibArchive", includeDatabaseContext: false));

        Assert.StartsWith(Header, script.Text);
        Assert.DoesNotContain("USE [", script.Text);
    }

    /// <summary>
    /// 取不到定義時整段都是註解，連 <c>USE</c> 都不寫。
    /// </summary>
    /// <remarks>
    /// 「從頭到尾都是註解」是缺資料時唯一的保證，而那一整段不是 T-SQL——前面多一行
    /// 可以執行的 <c>USE</c> 就讓它看起來像一份跑得起來的指令碼。
    /// </remarks>
    [Fact]
    public void 取不到定義時連USE都不寫()
    {
        var script = SqlObjectScript.BuildEditable(
            Module(SqlObjectKind.Procedure, "usp_LoanFinish", definition: null, databaseName: "LibArchive"),
            Execution("\r\n", databaseName: "LibArchive"));

        Assert.StartsWith("--", script.Text);
        Assert.DoesNotContain("USE [", script.Text);
    }
}
