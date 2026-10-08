using System;
using System.Linq;
using SqlAssist.Core.Statements;
using Xunit;

namespace SqlAssist.Core.Tests.Statements;

/// <summary>
/// <see cref="SqlVariableNames"/>：問「這個名字被用掉了嗎」與「換到哪個名字」。
/// </summary>
/// <remarks>
/// 這一支的價值全在「算錯會安靜地出錯」：漏掉一個同名變數，展開出來的第二個
/// <c>DECLARE</c> 就把使用者原本的值覆蓋掉了，而那一句跑得動。因此每個判斷
/// （批次界線、大小寫、落點）都各有一條測試釘著。
/// </remarks>
public sealed class SqlVariableNamesTests
{
    // ── Collect ────────────────────────────────────────────────────────

    [Fact]
    public void 收集同一批次裡的變數()
    {
        var names = SqlVariableNames.Collect(
            "DECLARE @LoanId int = 1;\r\nDECLARE @Days int = 7;\r\nSELECT @LoanId;",
            0);

        Assert.Equal(2, names.Count);
        Assert.Contains("@LoanId", names);
        Assert.Contains("@Days", names);
    }

    /// <remarks>
    /// 比對不分大小寫：<c>@LoanId</c> 與 <c>@loanid</c> 在 T-SQL 裡是同一個變數。
    /// 這裡收到的雖然是原始字串，但呼叫端用的是 <see cref="StringComparer.OrdinalIgnoreCase"/>，
    /// 因此兩個大小寫不同的寫法都要能讓 <see cref="SqlVariableNames.Resolve"/> 判成衝突。
    /// </remarks>
    [Fact]
    public void 大小寫不同算同一個變數()
    {
        var taken = SqlVariableNames.Collect("DECLARE @loanid int;", 0);

        // 落點接在「要求的那個名字」尾巴，大小寫照要求的帶回去，
        // 只有「算不算衝突」是不分大小寫的。
        Assert.Equal("@LoanId1", SqlVariableNames.Resolve("@LoanId", taken));
    }

    /// <remarks>
    /// 跨過 <c>GO</c> 之後同名的變數是<b>另一個</b>變數，不算衝突——把它算進去只會
    /// 讓展開出來的名字多一個沒必要的後綴。
    ///
    /// 探針位置刻意放在<b>第二個</b>批次的 <c>DECLARE</c> 上：批次由「包含這個位置的
    /// 那一段 <c>GO</c> 區間」決定，放在 <c>EXEC</c> 上問的是同一件事，但少了
    /// 「第二批次的宣告也看得到」這一半。
    /// </remarks>
    [Fact]
    public void 不收集其他批次的變數()
    {
        var sql = "DECLARE @LoanId int;\r\nGO\r\nDECLARE @Other int;";

        var names = SqlVariableNames.Collect(sql, sql.IndexOf("@Other", StringComparison.Ordinal));

        Assert.DoesNotContain("@LoanId", names);
        Assert.Contains("@Other", names);
    }

    [Fact]
    public void 第一批次的變數不含第二批次的()
    {
        var sql = "DECLARE @First int;\r\nGO\r\nDECLARE @Second int;";

        var names = SqlVariableNames.Collect(sql, 0);

        Assert.Contains("@First", names);
        Assert.DoesNotContain("@Second", names);
    }

    /// <remarks>
    /// 字串常值與註解裡的 <c>@名稱</c> 不是變數。誤收的症狀是展開出來的名字多一個
    /// 後綴，而使用者找不到那個「同名」的變數在哪裡。
    /// </remarks>
    [Fact]
    public void 字串與註解裡的變數不算()
    {
        var sql = "PRINT '@LoanId';\r\n-- @Days\r\n/* @Hours */\r\nSELECT 1;";

        var names = SqlVariableNames.Collect(sql, 0);

        Assert.Empty(names);
    }

    /// <remarks>
    /// <c>@@ROWCOUNT</c> 是系統函式，不是宣告出來的變數；但它是 <c>Variable</c> 權杖，
    /// 會被收進來。這一條測試把「目前會收」的事實釘住——收進來只讓避免撞名的判定
    /// 稍微保守一點（多一個後綴），而漏收一個真的變數才是安靜出錯的那一邊。
    /// </remarks>
    [Fact]
    public void 系統函式也會被收到()
    {
        var names = SqlVariableNames.Collect("SELECT @@ROWCOUNT;", 0);

        Assert.Contains("@@ROWCOUNT", names);
    }

    // ── GO 的判定 ──────────────────────────────────────────────────────

    /// <remarks>
    /// <c>GO</c> 之後接重複次數也是批次分隔（<c>GO 3</c>），因此仍然是界線。
    /// </remarks>
    [Fact]
    public void GO加重複次數仍是分隔()
    {
        var sql = "DECLARE @First int;\r\nGO 3\r\nDECLARE @Second int;";

        var names = SqlVariableNames.Collect(sql, 0);

        Assert.Contains("@First", names);
        Assert.DoesNotContain("@Second", names);
    }

    [Fact]
    public void GO後面接註解仍是分隔()
    {
        var sql = "DECLARE @First int;\r\nGO -- 換一批\r\nDECLARE @Second int;";

        var names = SqlVariableNames.Collect(sql, 0);

        Assert.DoesNotContain("@Second", names);
    }

    /// <remarks>
    /// 同一行的 <c>GO</c> 不是批次分隔：ScriptDom 只認獨占一行的 <c>GO</c>。
    /// 這裡不切，兩個批次就合成一個——而這一邊多收一個變數只是保守，比漏收安全。
    /// </remarks>
    [Fact]
    public void 同一行的GO不是分隔()
    {
        var sql = "DECLARE @First int; GO DECLARE @Second int;";

        var names = SqlVariableNames.Collect(sql, 0);

        Assert.Contains("@First", names);
        Assert.Contains("@Second", names);
    }

    [Fact]
    public void 字串裡的GO不是分隔()
    {
        var sql = "DECLARE @First int;\r\nPRINT 'GO';\r\nDECLARE @Second int;";

        var names = SqlVariableNames.Collect(sql, 0);

        Assert.Contains("@Second", names);
    }

    [Fact]
    public void 註解裡的GO不是分隔()
    {
        var sql = "DECLARE @First int;\r\n-- GO\r\nDECLARE @Second int;";

        var names = SqlVariableNames.Collect(sql, 0);

        Assert.Contains("@Second", names);
    }

    /// <remarks>
    /// <c>GOTO</c> 是同一行的識別字但不叫 <c>GO</c>，逐字比對因此不會誤判。
    /// </remarks>
    [Fact]
    public void GOTO不是分隔()
    {
        var sql = "DECLARE @First int;\r\nGOTO done;\r\nDECLARE @Second int;";

        var names = SqlVariableNames.Collect(sql, 0);

        Assert.Contains("@Second", names);
    }

    // ── Resolve ────────────────────────────────────────────────────────

    [Fact]
    public void 沒有衝突時名字原樣回傳()
    {
        var taken = SqlVariableNames.Collect("DECLARE @Other int;", 0);

        Assert.Equal("@LoanId", SqlVariableNames.Resolve("@LoanId", taken));
    }

    [Fact]
    public void 撞名時接上第一個流水號()
    {
        var taken = SqlVariableNames.Collect("DECLARE @LoanId int;", 0);

        Assert.Equal("@LoanId1", SqlVariableNames.Resolve("@LoanId", taken));
    }

    /// <remarks>
    /// 連續展開第二次時落點不能又是 <c>@LoanId1</c>：那是「固定後綴」寫法會踩到的坑，
    /// 流水號天生保證找得到下一個空位。
    /// </remarks>
    [Fact]
    public void 連續撞名時往下找()
    {
        var taken = SqlVariableNames.Collect(
            "DECLARE @LoanId int;\r\nDECLARE @LoanId1 int;\r\nDECLARE @LoanId2 int;",
            0);

        Assert.Equal("@LoanId3", SqlVariableNames.Resolve("@LoanId", taken));
    }

    [Fact]
    public void 落點要跟已用掉的名字比對大小寫()
    {
        var taken = SqlVariableNames.Collect("DECLARE @LoanId int;\r\nDECLARE @LOANID1 int;", 0);

        Assert.Equal("@LoanId2", SqlVariableNames.Resolve("@LoanId", taken));
    }

    /// <remarks>
    /// 呼叫端會把挑好的名字加進集合再處理下一個參數，因此兩個本來同名（或落點相同）
    /// 的參數不會搶到同一個名字。這裡直接驗那一條：連續問兩次同名要拿到不同答案。
    /// </remarks>
    [Fact]
    public void 兩個同名參數拿到不同落點()
    {
        var taken = SqlVariableNames.Collect("DECLARE @LoanId int;", 0);
        var takenSet = taken.ToList();

        var first = SqlVariableNames.Resolve("@LoanId", takenSet);
        takenSet.Add(first);
        var second = SqlVariableNames.Resolve("@LoanId", takenSet);

        Assert.Equal("@LoanId1", first);
        Assert.Equal("@LoanId2", second);
    }

    [Fact]
    public void 參數為空時擲出例外()
    {
        Assert.Throws<ArgumentNullException>(() => SqlVariableNames.Collect(null!, 0));
        Assert.Throws<ArgumentNullException>(
            () => SqlVariableNames.Resolve(null!, Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(
            () => SqlVariableNames.Resolve("@x", null!));
    }

    // ── Avoid：整批參數一起調 ──────────────────────────────────────────

    /// <remarks>
    /// 改名改的是<b>變數名</b>。<c>Name</c> 是模組簽章裡的名字，呼叫那一行左邊那個
    /// <c>@LoanId</c> 一定要是模組定義裡寫的那一個；跟著改掉那一句就找不到對應的參數
    /// （錯誤 8145），而畫面上只看得出名字多了一個數字。
    /// </remarks>
    [Fact]
    public void 撞名時只動變數名不動參數名()
    {
        var adjusted = SqlVariableNames.Avoid(
            new[] { new SqlStatementParameter("@LoanId", "int", isOutput: false, isOptional: false) },
            "DECLARE @LoanId int = 12345;\r\nEXEC dbo.usp_Renew",
            0);

        Assert.Equal("@LoanId", adjusted[0].Name);
        Assert.Equal("@LoanId1", adjusted[0].VariableName);
    }

    [Fact]
    public void 沒撞名時原樣回傳同一份()
    {
        var parameters = new[]
        {
            new SqlStatementParameter("@LoanId", "int", isOutput: false, isOptional: false)
        };

        var adjusted = SqlVariableNames.Avoid(parameters, "EXEC dbo.usp_Renew", 0);

        Assert.Same(parameters, adjusted);
        Assert.Equal("@LoanId", adjusted[0].VariableName);
    }

    /// <remarks>
    /// 批次的判定用的是<b>展開位置</b>：跨過 <c>GO</c> 的同名變數不算衝突，
    /// 因為它是另一個批次裡的另一個變數。
    /// </remarks>
    [Fact]
    public void 其他批次的同名變數不算衝突()
    {
        var sql = "DECLARE @LoanId int = 12345;\r\nGO\r\nEXEC dbo.usp_Renew";

        var adjusted = SqlVariableNames.Avoid(
            new[] { new SqlStatementParameter("@LoanId", "int", isOutput: false, isOptional: false) },
            sql,
            sql.IndexOf("EXEC", StringComparison.Ordinal));

        Assert.Equal("@LoanId", adjusted[0].VariableName);
    }

    /// <remarks>
    /// 兩個參數本來同名時不能搶到同一個落點：第一個拿到 <c>@x1</c> 之後，第二個要
    /// 拿到 <c>@x2</c>。少了累加，兩份 <c>DECLARE</c> 會撞在一起——正是這個功能
    /// 要修掉的那個問題。
    /// </remarks>
    [Fact]
    public void 兩個同名參數拿到不同變數名()
    {
        var adjusted = SqlVariableNames.Avoid(
            new[]
            {
                new SqlStatementParameter("@x", "int", isOutput: false, isOptional: false),
                new SqlStatementParameter("@x", "int", isOutput: false, isOptional: false)
            },
            "DECLARE @x int;",
            0);

        Assert.Equal("@x1", adjusted[0].VariableName);
        Assert.Equal("@x2", adjusted[1].VariableName);

        // 兩個參數的簽章名仍然是同一個，沒有被動到。
        Assert.Equal("@x", adjusted[0].Name);
        Assert.Equal("@x", adjusted[1].Name);
    }

    /// <remarks>
    /// 落點撞到<b>模組自己的另一個參數名</b>時也要讓開。第一個 <c>@x</c> 撞到批次裡的
    /// <c>@x</c>，換成 <c>@x1</c>；第二個本來就叫 <c>@x1</c>，這時 <c>@x1</c> 已經被
    /// 前一個拿走了，於是它再往後找到 <c>@x11</c>。兩個變數名因此不重複——
    /// 少了累加，兩份宣告就會撞在一起，正是這個功能要修掉的那個問題。
    /// </remarks>
    [Fact]
    public void 落點撞到另一個參數時一起讓開()
    {
        var adjusted = SqlVariableNames.Avoid(
            new[]
            {
                new SqlStatementParameter("@x", "int", isOutput: false, isOptional: false),
                new SqlStatementParameter("@x1", "int", isOutput: false, isOptional: false)
            },
            "DECLARE @x int;",
            0);

        Assert.Equal("@x1", adjusted[0].VariableName);
        Assert.Equal("@x11", adjusted[1].VariableName);
        Assert.NotEqual(adjusted[0].VariableName, adjusted[1].VariableName);

        // 簽章名沒有被動到。
        Assert.Equal("@x", adjusted[0].Name);
        Assert.Equal("@x1", adjusted[1].Name);
    }

    /// <remarks>
    /// 型別、OUTPUT 與選擇性都要原封不動搬過去：改名只換名字那一格。
    /// </remarks>
    [Fact]
    public void 改名時其餘欄位不變()
    {
        var adjusted = SqlVariableNames.Avoid(
            new[]
            {
                new SqlStatementParameter(
                    "@Total",
                    "decimal(18,2)",
                    isOutput: true,
                    isOptional: true,
                    defaultValue: "0")
            },
            "DECLARE @Total int;",
            0);

        Assert.Equal("@Total1", adjusted[0].VariableName);
        Assert.Equal("@Total", adjusted[0].Name);
        Assert.Equal("decimal(18,2)", adjusted[0].DataType);
        Assert.True(adjusted[0].IsOutput);
        Assert.True(adjusted[0].IsOptional);
        Assert.Equal("0", adjusted[0].DefaultValue);
    }
}
