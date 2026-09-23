using System.Collections.Generic;
using SqlAssist.Core.Parsing;
using Xunit;

namespace SqlAssist.Core.Tests.Parsing;

public sealed class SqlVariableRenameTests
{
    private static (SqlVariableRenameTarget? Target, string Text) Find(string sqlWithCaret)
    {
        var input = SqlWithCaret.Parse(sqlWithCaret);
        return (SqlVariableRename.FindAt(input.Text, input.Caret), input.Text);
    }

    private static SqlVariableRenameTarget Target(string sqlWithCaret)
    {
        var (target, _) = Find(sqlWithCaret);
        Assert.NotNull(target);
        return target!;
    }

    /// <summary>把每一處的名稱起點還原成字串，失敗訊息裡就看得到改了哪幾個字。</summary>
    private static string[] Occurrences(string text, SqlVariableRenameTarget target)
    {
        var names = new List<string>(target.NameStarts.Count);

        foreach (var start in target.NameStarts)
        {
            names.Add(text.Substring(start, target.NameLength));
        }

        return names.ToArray();
    }

    [Fact]
    public void 游標在變數上時找得到名稱與每一處()
    {
        var (target, text) = Find("DECLARE @rea|derId INT;\nSELECT @readerId");

        Assert.NotNull(target);
        Assert.Equal("readerId", target!.Name);
        Assert.Equal(new[] { "readerId", "readerId" }, Occurrences(text, target));
    }

    [Fact]
    public void 游標停在名稱結尾也算()
    {
        var (target, _) = Find("DECLARE @readerId| INT");

        Assert.NotNull(target);
        Assert.Equal("readerId", target!.Name);
    }

    /// <remarks>
    /// 名稱起點要是<b>游標那一處</b>的小老鼠之後。取第一個出現處的話，編輯器會把
    /// 選取範圍畫在宣告上，而使用者正在改的是底下那一個——看起來像跳錯地方。
    /// </remarks>
    [Fact]
    public void 名稱起點是游標那一處而不是第一個出現處()
    {
        var input = SqlWithCaret.Parse("DECLARE @readerId INT;\nSELECT @rea|derId");

        var target = SqlVariableRename.FindAt(input.Text, input.Caret);

        Assert.NotNull(target);
        Assert.Equal(input.Caret, target!.NameStart + 3);
    }

    /// <remarks>
    /// 主出現處的追蹤模式與其他出現處不同（只有它要跟著打字長），而游標可以停在
    /// <b>任何</b>一處。呼叫端若拿 <c>NameStart</c> 自己去比對，排在主出現處前面的
    /// 那些會在還沒找到之前就先被判成主出現處——實例是編輯器那一層的建構迴圈，
    /// 它把索引寫成迴圈變數，於是第一處永遠拿到主出現處的追蹤模式。
    /// </remarks>
    [Theory]
    [InlineData("DECLARE @rea|derId INT;\nSELECT @readerId", 0)]
    [InlineData("DECLARE @readerId INT;\nSELECT @rea|derId", 1)]
    [InlineData("DECLARE @readerId INT;\nSELECT @readerId;\nSELECT @rea|derId", 2)]
    [InlineData("DECLARE @readerId INT;\nSELECT @READERID;\nSELECT @Rea|derId", 2)]
    public void 主出現處的索引跟著游標走(string sqlWithCaret, int expected)
    {
        var input = SqlWithCaret.Parse(sqlWithCaret);

        var target = SqlVariableRename.FindAt(input.Text, input.Caret);

        Assert.NotNull(target);
        Assert.Equal(expected, target!.PrimaryIndex);
        Assert.Equal(target.NameStart, target.NameStarts[target.PrimaryIndex]);
    }

    [Fact]
    public void 游標不在變數上時找不到()
    {
        var (target, _) = Find("SELECT |1");

        Assert.Null(target);
    }

    [Fact]
    public void 只有小老鼠沒有名字時找不到()
    {
        var (target, _) = Find("DECLARE @| INT");

        Assert.Null(target);
    }

    [Fact]
    public void 全域變數不是重新命名的對象()
    {
        var (target, _) = Find("SELECT @@ROWCO|UNT");

        Assert.Null(target);
    }

    [Fact]
    public void 方括號識別字裡的文字不是變數()
    {
        var (target, _) = Find("SELECT [a|@b] FROM dbo.Loan");

        Assert.Null(target);
    }

    /// <remarks>
    /// 區域變數活到批次結束，所以 <c>GO</c> 另一邊那個同名的是另一個變數。
    /// 一起改掉的話會改出一份編不過的指令碼，而且使用者只按了一次 F2。
    /// </remarks>
    [Fact]
    public void 跨GO的另一個批次不算同一個變數()
    {
        var (target, _) = Find("DECLARE @reader|Id INT;\nGO\nSELECT @readerId");

        Assert.NotNull(target);
        Assert.Single(target!.NameStarts);
    }

    [Fact]
    public void 字串與註解裡的同名文字不算出現處()
    {
        var (target, text) = Find("DECLARE @reader|Id INT;\nSELECT '@readerId', @readerId -- @readerId");

        Assert.NotNull(target);
        Assert.Equal(new[] { "readerId", "readerId" }, Occurrences(text, target!));
    }

    [Fact]
    public void 大小寫不同的拼法算同一個變數()
    {
        var (target, text) = Find("DECLARE @Reader|Id INT;\nSELECT @readerid");

        Assert.NotNull(target);
        Assert.Equal("ReaderId", target!.Name);
        Assert.Equal(new[] { "ReaderId", "readerid" }, Occurrences(text, target));
        Assert.Empty(target.OtherNames);
    }

    [Fact]
    public void 同批次的其他變數收進撞名清單()
    {
        var (target, _) = Find("DECLARE @a INT, @b INT;\nSELECT @|a");

        Assert.NotNull(target);
        Assert.Equal(new[] { "b" }, target!.OtherNames);
    }

    [Fact]
    public void 空名稱不能用()
    {
        var target = Target("DECLARE @a INT;\nSELECT @|a");

        Assert.Equal(SqlVariableRenameProblem.Empty, SqlVariableRename.Validate(target, string.Empty));
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("a b")]
    [InlineData("a-b")]
    [InlineData("a.b")]
    [InlineData("@a")]
    public void 不是合法識別字的名稱不能用(string candidate)
    {
        var target = Target("DECLARE @a INT;\nSELECT @|a");

        Assert.Equal(SqlVariableRenameProblem.InvalidName, SqlVariableRename.Validate(target, candidate));
    }

    [Fact]
    public void 撞到同批次的其他變數時擋下來()
    {
        var target = Target("DECLARE @a INT, @b INT;\nSELECT @|a");

        Assert.Equal(SqlVariableRenameProblem.Duplicate, SqlVariableRename.Validate(target, "b"));
    }

    [Fact]
    public void 撞名不分大小寫()
    {
        var target = Target("DECLARE @a INT, @b INT;\nSELECT @|a");

        Assert.Equal(SqlVariableRenameProblem.Duplicate, SqlVariableRename.Validate(target, "B"));
    }

    /// <remarks>
    /// 撞名比對的是其他變數，所以這兩種都不是撞名。當成撞名的話，使用者把名字
    /// 改回原樣、或只調整大小寫，都會被擋下來而且找不到原因。
    /// </remarks>
    [Theory]
    [InlineData("a")]
    [InlineData("A")]
    public void 改回原本的名字或只改大小寫不算撞名(string candidate)
    {
        var target = Target("DECLARE @a INT, @b INT;\nSELECT @|a");

        Assert.Equal(SqlVariableRenameProblem.None, SqlVariableRename.Validate(target, candidate));
    }

    /// <remarks>
    /// T-SQL 允許 Unicode 字母，中文變數名稱合法；<c>@</c> 不算名稱的一部分，
    /// 帶著小老鼠傳進來要擋下來。
    /// </remarks>
    [Fact]
    public void 合法名稱的邊界()
    {
        Assert.True(SqlVariableRename.IsValidName("讀者"));
        Assert.True(SqlVariableRename.IsValidName("reader_2"));
        Assert.True(SqlVariableRename.IsValidName("_temp"));
        Assert.True(SqlVariableRename.IsValidName("#t"));
        Assert.False(SqlVariableRename.IsValidName("2reader"));
        Assert.False(SqlVariableRename.IsValidName("@reader"));
        Assert.False(SqlVariableRename.IsValidName(string.Empty));
        Assert.False(SqlVariableRename.IsValidName(null));
    }
}
