using System.Windows.Media;
using SqlAssist.Ssms22.UI;
using Xunit;

namespace SqlAssist.Ssms22.Tests.UI;

/// <summary>
/// 文字標記的推導：兩級落在哪一側、與表面分不分得開、字色配不配得出來。
/// </summary>
/// <remarks>
/// 直接驗 <see cref="TextMarkColors"/> 而不是它的呼叫端。這一層是活的——區塊端點的高亮走
/// <c>UI/BlockPalette</c> 呼叫它；搜尋命中在清單列用固定的記號黃，預覽那一邊自己從同一個黃
/// 推（見 <c>Preview/SqlScriptTheme</c>）。門檻值由 <c>docs/text-marks.md</c> 定，這裡驗的是
/// 它真的照那些門檻推出來。
/// </remarks>
public class TextMarkColorsTests
{
    // 種子色與表面都挑得過；兩者在深色主題下不同深淺，而標記的基準正是各表面自己給的那一個。
    // 最後三組是刻意挑的極端：中灰佈景、純黑上的飽和色、幾乎沒有色度的種子。
    [Theory]
    [InlineData(0x5649B0, 0xFFFFFF, 0x000000)]
    [InlineData(0x693D0F, 0xFFFDFC, 0x000000)]
    [InlineData(0x1E394A, 0xFCFDFF, 0x000000)]
    [InlineData(0xD3C1EC, 0x323134, 0xFFFFFF)]
    [InlineData(0x8FDB9F, 0x313432, 0xFFFFFF)]
    [InlineData(0x9184EE, 0x2C2C2C, 0xFFFFFF)]
    [InlineData(0x9184EE, 0x1E1E1E, 0xD4D4D4)]
    [InlineData(0x5649B0, 0xF5F5F5, 0x1F1F1F)]
    [InlineData(0x8FDB9F, 0x808080, 0xFFFFFF)]
    [InlineData(0xFF0000, 0x000000, 0xFFFFFF)]
    [InlineData(0x101010, 0xFFFFFF, 0x000000)]
    public void 兩級落在同一側標得出來也分得開而且字色讀得到(int seed, int surface, int foreground)
    {
        var (strong, weak) = TextMarkColors.Pair(Rgb(seed), Rgb(surface));

        // 同側：兩級都落在深的那一邊。分居兩側的那一版在深色佈景上會讓「目前那一處」變成
        // 亮底深字，而它旁邊較弱的那一級卻是暗底淺字，看起來比它還強。
        Assert.True(ThemeColorMath.Luminance(strong) <= TextMarkColors.MaximumLuminance + 0.01);
        Assert.True(ThemeColorMath.Luminance(weak) <= TextMarkColors.MaximumLuminance + 0.01);

        // 標得出來：兩級都要與所在表面分得開，否則等於沒有標記。
        Assert.True(ThemeColorMath.Contrast(strong, Rgb(surface)) >= TextMarkColors.MinimumSeparation);
        Assert.True(ThemeColorMath.Contrast(weak, Rgb(surface)) >= TextMarkColors.MinimumSeparation);

        // 兩級之間：亮度上就看得出深淺，色度差與字重另外再補一層。
        Assert.True(ThemeColorMath.Contrast(strong, weak) >= TextMarkColors.LevelSeparation);

        // 字色走這一側能到的最純的那一端，4.5 只是下限；停在剛好及格的那一版是中灰。
        Assert.True(ThemeColorMath.Contrast(TextMarkColors.Ink(strong, Rgb(foreground)), strong) >= 4.5);
        Assert.True(ThemeColorMath.Contrast(TextMarkColors.Ink(weak, Rgb(foreground)), weak) >= 4.5);
    }

    [Fact]
    public void 表面自己的前景讀得到就留著它()
    {
        // 白字在深底上讀得到就不換——換掉會讓同一列出現第三種顏色。
        Assert.Equal(Colors.White, TextMarkColors.Ink(Colors.Black, Colors.White));
    }

    [Fact]
    public void 表面自己的前景讀不到時才換成白()
    {
        // 底色依規範一定落在深的那一側，所以「讀不到的前景」是與底色差不多深的那一種；
        // 這時往白走。用一個亮度幾乎與底色相同的灰當前景。
        var fill = Color.FromRgb(120, 120, 120);
        var preferred = Color.FromRgb(130, 130, 130);
        Assert.True(ThemeColorMath.Contrast(preferred, fill) < 4.5);
        Assert.Equal(Colors.White, TextMarkColors.Ink(fill, preferred));
    }

    private static Color Rgb(int value) =>
        Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
}
