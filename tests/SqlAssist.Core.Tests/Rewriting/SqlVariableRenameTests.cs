using System;
using SqlAssist.Core.Rewriting;
using Xunit;

namespace SqlAssist.Core.Tests.Rewriting;

/// <summary>
/// 就地改名的判定：批次界線、哪些出現位置算同一組、以及新名稱能不能用。
/// </summary>
/// <remarks>
/// 測試用 <c>|</c> 標出游標位置，再從字串裡拿掉——直接寫位移的話，改一個字
/// 就要重算整段，而算錯的那個數字只會讓測試悄悄驗到別的地方去。
/// </remarks>
public sealed class SqlVariableRenameTests
{
    private const char Caret = '|';

    private static (SqlVariableRenameTarget Target, string Sql) Locate(string marked)
    {
        var caret = marked.IndexOf(Caret);
        var sql = marked.Remove(caret, 1);

        Assert.True(
            SqlVariableRename.TryLocate(sql, caret, out var target, out var message),
            message);

        return (target!, sql);
    }

    private static SqlVariableRenameTarget Target(string marked) => Locate(marked).Target;

    private static string Reason(string marked)
    {
        var caret = marked.IndexOf(Caret);
        var sql = marked.Remove(caret, 1);

        Assert.False(SqlVariableRename.TryLocate(sql, caret, out _, out var message), "應該要認不出來。");
        return message;
    }

    private static string Offsets(SqlVariableRenameTarget target) =>
        string.Join(",", target.Occurrences);

    // ── 出現位置 ────────────────────────────────────────────────────────

    [Fact]
    public void 列出同一批次裡的全部出現位置()
    {
        var target = Target("DECLARE @Copy|No INT; SET @CopyNo = 1; SELECT @CopyNo;");

        Assert.Equal("@CopyNo", target.Name);
        Assert.Equal("8,25,45", Offsets(target));
        Assert.Equal(0, target.CursorIndex);
        Assert.Equal(8, target.CursorOffset);
    }

    [Fact]
    public void 游標貼在名稱右邊也算在它身上()
    {
        var target = Target("DECLARE @CopyNo| INT; SELECT @CopyNo;");

        Assert.Equal("@CopyNo", target.Name);
        Assert.Equal(2, target.Occurrences.Count);
    }

    [Fact]
    public void 游標停在游標處那一處時索引跟著它()
    {
        var target = Target("DECLARE @CopyNo INT; SET @Copy|No = 1;");

        Assert.Equal(1, target.CursorIndex);
        Assert.Equal(25, target.CursorOffset);
    }

    [Fact]
    public void 游標不在區域變數上時說得出原因()
    {
        Assert.Contains("游標不在區域變數", Reason("DECLARE @CopyNo |INT;"));
        Assert.Contains("游標不在區域變數", Reason("DECLARE @CopyNo INT; SET @CopyNo = 1;|"));

        // 超出範圍要用參數直接指定：標記法只能標在字串裡的位置，而這一條要驗的
        // 正好是「位置不在字串上」。
        Assert.False(
            SqlVariableRename.TryLocate("SELECT 1;", 999, out _, out var outOfRange),
            "游標超出文字範圍時不該認得出來。");
        Assert.Contains("超出文字範圍", outOfRange);
    }

    /// <remarks>
    /// <c>@@</c> 那一族是系統函式，不是宣告出來的東西。放行改名不會產生一個新變數，
    /// 只會讓原本那一段全部失效，而錯誤要到執行時才出現。
    /// </remarks>
    [Fact]
    public void 系統函式不能改()
    {
        Assert.Contains("系統函式", Reason("SELECT @@ROWCOU|NT;"));
    }

    // ── 批次界線 ────────────────────────────────────────────────────────

    /// <remarks>
    /// 變數與 <c>DECLARE</c> 同屬一個批次，跨過 <c>GO</c> 之後同名的變數是另一個變數。
    /// 一起改掉的話第二個批次會憑空多出一個沒有宣告的名稱。
    /// </remarks>
    [Fact]
    public void 跨GO的同名變數不會一起改()
    {
        var (target, sql) = Locate(
            "DECLARE @CopyNo INT;\nSELECT @Copy|No;\nGO\nDECLARE @CopyNo INT;\nSELECT @CopyNo;");

        Assert.Equal(2, target.Occurrences.Count);
        Assert.Equal(0, target.BatchStart);
        Assert.Equal(sql.IndexOf("GO\n", StringComparison.Ordinal), target.BatchEnd);
    }

    /// <remarks>
    /// <b>這一條是「批次界線不能找 GO 這個字」的證據。</b>字串常值裡的 <c>'GO'</c>、
    /// 註解裡的 <c>-- GO</c> 與資料行名稱 <c>[GO]</c> 都不是批次分隔；當成分隔的話
    /// 批次會被切在它們身上，而症狀是「有幾處沒改到」——使用者得捲到很後面才看得出來。
    ///
    /// 三段刻意分居游標的前後：切在游標之前會少掉前面那一處，切在之後會少掉後面那一處。
    /// </remarks>
    [Fact]
    public void 字串註解與方括號裡的GO不是批次分隔()
    {
        var target = Target(
            "DECLARE @CopyNo INT;\n" +
            "SELECT 'GO' AS Label, [GO] FROM dbo.Loan WHERE CopyNo = @Copy|No;\n" +
            "-- GO\n" +
            "SELECT @CopyNo;");

        Assert.Equal(3, target.Occurrences.Count);
    }

    // ── 被呼叫程序的參數名 ──────────────────────────────────────────────

    [Fact]
    public void EXEC的參數名不能改()
    {
        Assert.Contains("參數名", Reason("EXEC dbo.LoanByReader @Co|pyNo = 1"));
    }

    [Fact]
    public void EXECUTE拼法一樣處理()
    {
        Assert.Contains("參數名", Reason("EXECUTE dbo.LoanByReader @Co|pyNo = 1"));
    }

    [Fact]
    public void 改名時略過EXEC的參數名()
    {
        var target = Target("DECLARE @Copy|No INT; EXEC dbo.LoanByReader @CopyNo = @CopyNo;");

        // DECLARE 的宣告，以及傳進去當值的那一個；參數名那一處不算。
        Assert.Equal(2, target.Occurrences.Count);
    }

    /// <remarks>
    /// <c>EXEC @rc = dbo.Proc @p = 1</c> 的 <c>@rc</c> 與 <c>@p</c> 長得一模一樣
    /// （都是「<c>@名稱 =</c>」），差別只在程序名稱的哪一側：左邊的是接回傳值的區域變數，
    /// 右邊的才是程序的參數名。兩邊一起排除的話，<c>@rc</c> 就永遠改不了。
    /// </remarks>
    [Fact]
    public void EXEC的回傳值變數可以改()
    {
        var target = Target("EXEC @r|c = dbo.LoanByReader @p = 1;\nSELECT @rc;");

        Assert.Equal("@rc", target.Name);
        Assert.Equal(2, target.Occurrences.Count);
    }

    /// <remarks>
    /// 參數清單沒有結束標記，所以掃描要停在下一道敘述的起頭。不停下來的話，
    /// <c>SELECT @x = …</c> 這種同樣是「<c>@名稱 =</c>」的寫法會被算成參數名，
    /// 而那正好是最常見的一種指派。
    /// </remarks>
    [Fact]
    public void EXEC之後的新敘述不是參數清單()
    {
        var target = Target(
            "DECLARE @rc INT;\n" +
            "EXEC dbo.LoanByReader @p = 1;\n" +
            "SELECT @r|c = CopyNo FROM dbo.Loan;");

        Assert.Equal("@rc", target.Name);
        Assert.Equal(2, target.Occurrences.Count);
    }

    // ── 名稱檢核 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("@CopyNo", true)]
    [InlineData("@a", true)]
    [InlineData("@Reel_No1", true)]
    [InlineData("@@ROWCOUNT", false)]
    [InlineData("@", false)]
    [InlineData("CopyNo", false)]
    [InlineData("@1", false)]
    [InlineData("@Copy No", false)]
    [InlineData("@Copy-No", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 名稱合法性(string? name, bool expected)
    {
        Assert.Equal(expected, SqlVariableRename.IsValidName(name));
    }

    // ── 撞名 ────────────────────────────────────────────────────────────

    /// <remarks>
    /// 撞名只算<b>同一個批次</b>：跨過 <c>GO</c> 之後同名的變數是另一個物件，
    /// 把它當成衝突的話，兩個批次各有一個 <c>@rc</c> 這種完全正常的寫法就改不動了。
    /// </remarks>
    [Fact]
    public void 撞名只在同一個批次裡算()
    {
        const string sql =
            "DECLARE @ReelNo INT;\n" +
            "DECLARE @CopyNo INT;\n" +
            "GO\n" +
            "DECLARE @CopyNo INT;";

        // 第一個批次裡：兩個名字都被用掉了，第三個沒有。
        Assert.True(SqlVariableRename.IsNameTaken(sql, 5, "@ReelNo"));
        Assert.True(SqlVariableRename.IsNameTaken(sql, 5, "@CopyNo"));
        Assert.False(SqlVariableRename.IsNameTaken(sql, 5, "@Other"));

        // 第二個批次裡 @ReelNo 沒有出現過，即使第一個批次有。
        Assert.False(SqlVariableRename.IsNameTaken(sql, 50, "@ReelNo"));
        Assert.True(SqlVariableRename.IsNameTaken(sql, 50, "@CopyNo"));
    }

    /// <remarks>
    /// 正在改名的那幾處不能把自己報成衝突：判定用的文字是<b>已經改完</b>的那一份，
    /// 每一處現在都叫新名稱。少了這一道，任何改名都會當場被自己擋下來。
    /// </remarks>
    [Fact]
    public void 正在改名的那幾處不算撞名()
    {
        const string sql = "DECLARE @CopyNo INT;\nSET @CopyNo = 1;";

        Assert.True(SqlVariableRename.IsNameTaken(sql, 5, "@CopyNo"));
        Assert.False(SqlVariableRename.IsNameTaken(sql, 5, "@CopyNo", new[] { 8, 25 }));
    }

    // ── 改名 ────────────────────────────────────────────────────────────

    [Fact]
    public void 改名會換掉全部出現位置並換算游標()
    {
        var (target, sql) = Locate(
            "DECLARE @CopyNo INT;\nSET @Copy|No = 1;\nSELECT @CopyNo;");

        var result = SqlVariableRename.Rename(sql, target, "@Reel");

        Assert.Equal("DECLARE @Reel INT;\nSET @Reel = 1;\nSELECT @Reel;", result.Text);
        Assert.Equal(3, result.AffectedCount);
        Assert.Equal(23, result.CaretPosition);
    }

    /// <remarks>
    /// 名稱不合法時直接丟出來而不是回一份沒改的結果：呼叫端把不合法的新名稱寫進緩衝區
    /// 之後，那一份 SQL 已經壞了，而「安靜地什麼都沒做」會讓呼叫端以為已經改好。
    /// </remarks>
    [Fact]
    public void 名稱不合法時不改名()
    {
        var (target, sql) = Locate("DECLARE @Copy|No INT;");

        Assert.Throws<ArgumentException>(() => SqlVariableRename.Rename(sql, target, "@1bad"));
    }
}
