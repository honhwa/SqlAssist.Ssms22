using System.Linq;
using SqlAssist.Ssms22.Editor;
using Xunit;

namespace SqlAssist.Ssms22.Tests.Editor;

/// <summary>
/// SSMS 內建 IntelliSense 那顆命令的名字判準：候選名單與列舉比對必須用同一把尺，
/// 否則省時間的那一條路會試一顆列舉永遠不會接受的名字。
/// </summary>
public sealed class SqlIntellisenseCacheNamesTests
{
    [Fact]
    public void 每個候選名單都符合判準()
    {
        Assert.All(SqlIntellisenseCacheNames.Candidates, name => Assert.True(SqlIntellisenseCacheNames.Matches(name), name));
    }

    /// <summary>命令集的段數由發行者決定，多一段前綴不影響它是不是同一顆命令。</summary>
    [Theory]
    [InlineData("Edit.RefreshLocalCache")]
    [InlineData("Query.RefreshLocalCache")]
    [InlineData("SqlWorkbench.IntelliSense.RefreshLocalCache")]
    [InlineData("Edit.RefreshIntellisense")]
    [InlineData("edit.refreshlocalcache")]
    public void 名字像就算命中(string name)
    {
        Assert.True(SqlIntellisenseCacheNames.Matches(name));
    }

    /// <summary>判準用「包含」比對，所以只有這兩個關鍵片段本身不能誤判成別的命令。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Edit.CompleteWord")]
    [InlineData("Edit.ListMembers")]
    [InlineData("Query.Execute")]
    [InlineData("SqlWorkbench.ToggleIntelliSense")]
    public void 其他命令不算命中(string? name)
    {
        Assert.False(SqlIntellisenseCacheNames.Matches(name));
    }

    /// <summary>候選名單是省一次掃描用的，不該長到把同一顆命令寫兩次。</summary>
    [Fact]
    public void 候選名單沒有重複()
    {
        Assert.Equal(
            SqlIntellisenseCacheNames.Candidates.Count,
            SqlIntellisenseCacheNames.Candidates.Distinct().Count());
    }
}
