using System.Windows.Media;

namespace SqlAssist.Ssms22.UI;

/// <summary>
/// 搜尋命中的整組顏色：一般命中與「目前停在的那一處」各一對底色與字色。
/// </summary>
/// <remarks>
/// 底色固定是螢光筆黃，不由佈景強調色推導。命中要讀到的是「這幾個字被畫起來了」，
/// 而強調色在淺色佈景是深紫、深色佈景是亮紫，推導出來的是兩塊深色標籤，怎麼調都不像被筆畫過。
/// 黃底在深淺兩種佈景上都夠亮，字色因此固定走深色，不再回頭問表面原本的前景。
///
/// 兩級只差在飽和度：一般命中留亮黃，目前那一處往琥珀壓一階。黃色系的亮度都擠在頂端，
/// 靠明度分級在那裡幾乎量不出差別，色度才是分得開的那一維。
///
/// 面積決定呈現方式：命中的字數少，蓋掉底下的語法著色不影響判讀，周圍仍留著色彩。
/// 高對比沒有中間色可調，仍換成系統配對色。
/// </remarks>
internal readonly struct MatchPalette
{
    /// <summary>一般命中：螢光筆黃。</summary>
    private static readonly Color Highlighter = Color.FromRgb(0xFF, 0xEE, 0x58);

    /// <summary>目前停在那一處：同色系再壓一階，整片內容裡才跳得出來。</summary>
    private static readonly Color HighlighterCurrent = Color.FromRgb(0xFF, 0xC4, 0x00);

    /// <summary>黃底上的字色；兩種黃都遠高於 4.5:1。</summary>
    private static readonly Color Ink = Color.FromRgb(0x1A, 0x1A, 0x1A);

    private MatchPalette(Color background, Color foreground, Color currentBackground, Color currentForeground)
    {
        Background = background;
        Foreground = foreground;
        CurrentBackground = currentBackground;
        CurrentForeground = currentForeground;
    }

    /// <summary>一般命中的底色。</summary>
    public Color Background { get; }

    /// <summary>一般命中的字色。</summary>
    public Color Foreground { get; }

    /// <summary>目前停在那一處的底色；整片指令碼裡要一眼跳出來。</summary>
    public Color CurrentBackground { get; }

    /// <summary>目前停在那一處的字色。</summary>
    public Color CurrentForeground { get; }

    /// <param name="surface">這個表面自己的底色；高對比下當成一般命中的字色（反白）。</param>
    /// <param name="foreground">這個表面自己的前景色；高對比下是一般命中的底色。</param>
    /// <param name="selection">高對比下的系統選取色與配對文字。</param>
    public static MatchPalette Create(
        Color surface, Color foreground, bool highContrast,
        (Color Background, Color Foreground) selection)
    {
        // 高對比沒有中間色可調：一般命中用反白，目前那一處用系統選取色。兩者都是實色、都合規，
        // 而且差的是色相不是明度——明度在那裡本來就只有兩級。
        if (highContrast) return new MatchPalette(foreground, surface, selection.Background, selection.Foreground);

        return new MatchPalette(Highlighter, Ink, HighlighterCurrent, Ink);
    }
}
