using System.Windows.Media;
using SqlAssist.Ssms22.UI;
using Xunit;

namespace SqlAssist.Ssms22.Tests.UI;

public class MatchPaletteTests
{
    private static readonly (Color Background, Color Foreground) Selection = (Colors.Yellow, Colors.Black);

    private static readonly Color Highlighter = Color.FromRgb(0xFF, 0xEE, 0x58);
    private static readonly Color HighlighterCurrent = Color.FromRgb(0xFF, 0xC4, 0x00);

    // 螢光筆黃不隨佈景改變：淺色、深色與中灰表面上都是同一塊亮黃，字色固定走深色那一端。
    // 表面只影響高對比那一支；這裡逐一切換底色，確認一般命中不受它影響。
    [Theory]
    [InlineData(0xFFFFFF)]
    [InlineData(0x1E1E1E)]
    [InlineData(0x808080)]
    public void 命中固定螢光筆黃且深淺底色都讀得到(int surface)
    {
        var palette = MatchPalette.Create(Rgb(surface), Rgb(0xF5F5F5), highContrast: false, Selection);

        Assert.Equal(Highlighter, palette.Background);
        Assert.Equal(HighlighterCurrent, palette.CurrentBackground);

        // 黃底都夠亮，字色不必跟著表面翻轉；4.5:1 是下限，實際遠高於它。
        Assert.True(ThemeColorMath.Contrast(palette.Foreground, palette.Background) >= 4.5);
        Assert.True(ThemeColorMath.Contrast(palette.CurrentForeground, palette.CurrentBackground) >= 4.5);

        // 字色是深色那一端：黃底上留白字等於看不見。
        Assert.True(ThemeColorMath.Luminance(palette.Foreground) < 0.1);
    }

    [Fact]
    public void 兩級只差飽和度仍在同一色系()
    {
        var palette = MatchPalette.Create(Colors.Black, Colors.White, highContrast: false, Selection);

        // 黃色系的亮度都擠在頂端，靠明度分級量不出差別；分得開的是色度。
        Assert.True(ThemeColorMath.Luminance(palette.Background) > 0.7);
        Assert.True(ThemeColorMath.Luminance(palette.CurrentBackground) > 0.4);
        Assert.True(ThemeColorMath.Contrast(palette.CurrentBackground, palette.Background) >= 1.2);
        Assert.True(palette.Background.B > palette.CurrentBackground.B);
    }

    [Fact]
    public void 高對比改用系統選取色仍然分得出兩級()
    {
        var surface = Colors.Black;
        var foreground = Colors.White;
        var palette = MatchPalette.Create(surface, foreground, highContrast: true, Selection);

        // 高對比沒有中間色可調，兩級差的是色相不是亮度；對比比值在那裡判不出來，只要求不同。
        Assert.NotEqual(palette.Background, palette.CurrentBackground);
        Assert.True(ThemeColorMath.Contrast(palette.Foreground, palette.Background) >= 4.5);
        Assert.True(ThemeColorMath.Contrast(palette.CurrentForeground, palette.CurrentBackground) >= 4.5);
    }

    private static Color Rgb(int value) =>
        Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
}
