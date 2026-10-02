using System.Windows.Media;
using SqlAssist.Ssms22.UI;
using Xunit;

namespace SqlAssist.Ssms22.Tests.UI;

public class MatchPaletteTests
{
    private static readonly (Color Background, Color Foreground) Selection = (Colors.Yellow, Colors.Black);

    // 工具窗的清單底色與指令碼借來的編輯器底色都要走得過；兩者在深色主題下不同深淺，
    // 而命中高亮的基準正是各表面自己給的那一個。後面幾組是刻意挑的極端：中灰佈景、
    // 自己就是淡黃的佈景（推開的動作要把兩級一起往下帶）、純黑，以及幾乎讀不到的前景。
    [Theory]
    [InlineData(0xFFFFFF, 0x000000)]
    [InlineData(0xFFFDFC, 0x000000)]
    [InlineData(0xFCFDFF, 0x000000)]
    [InlineData(0xF5F5F5, 0x1F1F1F)]
    [InlineData(0xFFF8D0, 0x000000)]
    [InlineData(0x808080, 0xFFFFFF)]
    [InlineData(0x323134, 0xFFFFFF)]
    [InlineData(0x313432, 0xFFFFFF)]
    [InlineData(0x2C2C2C, 0xFFFFFF)]
    [InlineData(0x1E1E1E, 0xD4D4D4)]
    [InlineData(0x000000, 0xFFFFFF)]
    public void 兩級落在亮側標得出來也分得開而且深字讀得到(int surface, int foreground)
    {
        var palette = MatchPalette.Create(Rgb(surface), Rgb(foreground), highContrast: false, Selection);

        // 亮側：底色是固定的螢光筆黃配深字。從強調色推導的那一版會跟著佈景翻轉，於是深色
        // 佈景上「目前那一處」變成亮底深字，而它旁邊較弱的那一級卻是暗底淺字，看起來比它還強。
        Assert.True(ThemeColorMath.Contrast(Colors.Black, palette.Background) >= 4.5);
        Assert.True(ThemeColorMath.Contrast(Colors.Black, palette.CurrentBackground) >= 4.5);

        // 標得出來：兩級都要與所在表面分得開，否則等於沒有標記。
        Assert.True(ThemeColorMath.Contrast(palette.Background, Rgb(surface)) >= TextMarkColors.HighlightSeparation);
        Assert.True(ThemeColorMath.Contrast(palette.CurrentBackground, Rgb(surface)) >= TextMarkColors.HighlightSeparation);

        // 兩級之間：亮度上就看得出深淺，色度差與字重另外再補一層。
        Assert.True(ThemeColorMath.Contrast(palette.CurrentBackground, palette.Background) >= 1.25);

        // 深字：字色要比底色深，而且讀得到。換成淺字的那一版在亮黃上是一塊糊掉的方塊。
        Assert.True(ThemeColorMath.Luminance(palette.Foreground) < ThemeColorMath.Luminance(palette.Background));
        Assert.True(ThemeColorMath.Contrast(palette.Foreground, palette.Background) >= 4.5);
        Assert.True(ThemeColorMath.Contrast(palette.CurrentForeground, palette.CurrentBackground) >= 4.5);
    }

    [Fact]
    public void 一般命中就是螢光筆黃不隨佈景或強調色改變()
    {
        // 白底與近黑底各一次：同一個搜尋字在兩種佈景上要是同一個顏色，唯一的差別是為了與
        // 表面分得開而整體推開的距離。
        var onWhite = MatchPalette.Create(Colors.White, Colors.Black, highContrast: false, Selection);
        var onBlack = MatchPalette.Create(Colors.Black, Colors.White, highContrast: false, Selection);

        Assert.Equal(Color.FromRgb(0xFF, 0xE0, 0x66), onWhite.Background);
        Assert.Equal(onWhite.Background, onBlack.Background);
        Assert.Equal(Colors.Black, onBlack.Foreground);
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
