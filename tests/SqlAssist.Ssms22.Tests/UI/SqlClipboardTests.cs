using System;
using System.Runtime.InteropServices;
using SqlAssist.Ssms22.UI;
using Xunit;

namespace SqlAssist.Ssms22.Tests.UI;

/// <summary>
/// 剪貼簿讀取的三種結果。
/// </summary>
/// <remarks>
/// 只測讀取。寫入那一半要真的動系統剪貼簿（<c>Clipboard.SetDataObject</c>），
/// 在測試裡做會與使用者的剪貼簿互相覆蓋，所以那一邊的重試邏輯沒有測試。
/// </remarks>
public sealed class SqlClipboardTests
{
    [Fact]
    public void 讀到文字時原樣回傳()
    {
        Assert.Equal("A01\r\nB02", SqlClipboard.TryReadText(out var failure, () => "A01\r\nB02"));
        Assert.Equal(string.Empty, failure);
    }

    /// <remarks>
    /// 複製的是圖片或檔案，或剪貼簿本來就是空的。空字串與沒有文字在這一條路上
    /// 是同一件事：都沒有值可以拆，而拆出零個值只會得到一段語法錯誤的 SQL。
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 沒有純文字時說明白(string? text)
    {
        Assert.Null(SqlClipboard.TryReadText(out var failure, () => text));
        Assert.Equal(SqlClipboard.NoTextMessage, failure);
    }

    /// <remarks>
    /// 剪貼簿被別的程式開著是常態（遠端桌面、剪貼簿管理員、Excel 剛複製完）。
    /// 這條路徑不重試——重試就得在 UI 執行緒上等，而這句訊息是使用者唯一的線索。
    /// </remarks>
    [Fact]
    public void 剪貼簿打不開時回一句看得懂的訊息()
    {
        Assert.Null(SqlClipboard.TryReadText(
            out var failure,
            () => throw new ExternalException("CLIPBRD_E_CANT_OPEN")));
        Assert.Equal(SqlClipboard.BusyReadMessage, failure);
    }

    /// <remarks>
    /// 寫入那一邊的訊息說「未複製」，讀取沿用同一句會讓使用者以為是複製壞了。
    /// 兩句都要在，而且要不一樣。
    /// </remarks>
    [Fact]
    public void 讀寫的忙碌訊息不是同一句()
    {
        Assert.NotEqual(SqlClipboard.BusyMessage, SqlClipboard.BusyReadMessage);
    }
}
