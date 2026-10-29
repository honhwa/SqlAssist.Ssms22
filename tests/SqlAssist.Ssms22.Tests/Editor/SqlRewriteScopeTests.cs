using SqlAssist.Ssms22.Editor;
using Xunit;

namespace SqlAssist.Ssms22.Tests.Editor;

/// <summary>「補齊結構描述」與「TOP 補上括號」要動哪一段。</summary>
public sealed class SqlRewriteScopeTests
{
    /// <summary>
    /// 沒有選取時是整份文件。這是兩顆右鍵命令能不能作用的關鍵：空選取是
    /// 游標上的一個零長度範圍，照它改寫等於什麼都沒做。
    /// </summary>
    [Fact]
    public void 沒有選取時改整份文件()
    {
        var scope = SqlRewriteScope.Resolve(selectionStart: 0, selectionLength: 0, documentLength: 120);

        Assert.Equal(0, scope.Start);
        Assert.Equal(120, scope.Length);
    }

    /// <summary>游標停在檔案中間，沒有選取時仍然從頭改到尾。</summary>
    [Fact]
    public void 游標在段落中間也從頭改到尾()
    {
        var scope = SqlRewriteScope.Resolve(selectionStart: 80, selectionLength: 0, documentLength: 120);

        Assert.Equal(0, scope.Start);
        Assert.Equal(120, scope.Length);
    }

    [Fact]
    public void 有選取時只改選取()
    {
        var scope = SqlRewriteScope.Resolve(selectionStart: 20, selectionLength: 5, documentLength: 120);

        Assert.Equal(20, scope.Start);
        Assert.Equal(5, scope.Length);
    }

    [Fact]
    public void 空文件不會給出超出長度的範圍()
    {
        var scope = SqlRewriteScope.Resolve(selectionStart: 0, selectionLength: 0, documentLength: 0);

        Assert.Equal(0, scope.Start);
        Assert.Equal(0, scope.Length);
    }
}
