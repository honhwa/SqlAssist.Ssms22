using System;
using System.Collections.Generic;
using System.Windows.Media;
using SqlAssist.Core.Settings;

namespace SqlAssist.Ssms22.UI;

/// <summary>區塊各表面共用單一色相；輸入取自目前檢視，不保存深淺主題旗標。</summary>
internal static class BlockPalette
{
    /// <summary>
    /// 符號端點的內建淡黃 mark；80% 不透明，與底色混合後深色主題自動轉為柔和芥黃，不會刺眼。
    /// </summary>
    /// <remarks>
    /// 只在全域與符號背景都留空時採用。刻意跳過 <see cref="ThemeColorMath.EnsureGraphicContrast"/>：
    /// 那個 3:1 會把淡黃往目標色推成橄欖綠，反而蓋住正在輸入的字元。
    /// </remarks>
    private static readonly Color SymbolMark = Color.FromArgb(204, 0xFF, 0xF9, 0xC4);

    public static IReadOnlyDictionary<ThemeBrush, Color> Create(Color background, Color foreground,
        Color accent, string? preference, bool highContrast, SqlAssistSettings? settings = null)
    {
        if (!highContrast && SqlColorPreference.TryParseRgb(preference, out var rgb))
            accent = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        var fallback = ThemeColorMath.Contrast(Colors.Black, background) >=
            ThemeColorMath.Contrast(Colors.White, background) ? Colors.Black : Colors.White;
        var text = ThemeColorMath.EnsureContrast(ThemeColorMath.Composite(foreground, background), background, fallback);
        var graphic = highContrast ? text : ThemeColorMath.EnsureGraphicContrast(accent, background);
        var range = highContrast ? Colors.Transparent : Tint(graphic, background, text, 0.12);
        var hint = highContrast ? background : ThemeColorMath.Composite(Tint(graphic, background, text, 0.06), background);
        var globalInk = ReadOptionalColor(settings?.BlockKeywordForeground);
        var inherited = ReadOptionalColor(settings?.BlockKeywordBackground);
        var keyword = Endpoint(globalInk, inherited, accent, true);
        // 細項只覆寫指定通道；留空或無效值繼承全域「原始基準值」，再依實際背景校正。
        // 全域也留空時，符號改用淡黃 mark 當預設：它直接蓋在游標旁的字元上，不套圖形對比才不會被壓成橄欖色。
        var symbol = Endpoint(ReadOptionalColor(settings?.BlockSymbolForeground) ?? globalInk,
            ReadOptionalColor(settings?.BlockSymbolBackground), inherited ?? SymbolMark, inherited is not null);

        (Color Foreground, Color Background) Endpoint(Color? explicitInk, Color? explicitBackground, Color fallback, bool adjustFallback)
        {
            // 端點面積小，採實色高亮而非區間淡底；分類標籤才能改字色，marker 前景其實是框線。
            if (highContrast) return (background, text);
            Color fill;
            if (explicitBackground is { } chosen)
                fill = ThemeColorMath.EnsureGraphicContrast(chosen, background);
            else if (adjustFallback)
                fill = ThemeColorMath.EnsureGraphicContrast(fallback, background);
            else
                fill = ThemeColorMath.Composite(fallback, background);
            if (explicitInk is { } requested)
                return (requested, ThemeColorMath.EnsureBackgroundForText(fill, requested, background));
            var ink = ThemeColorMath.EnsureTextContrast(foreground, fill);
            return (ink, fill);
        }
        return new Dictionary<ThemeBrush, Color>
        {
            [ThemeBrush.Block] = graphic,
            [ThemeBrush.BlockTry] = graphic,
            [ThemeBrush.BlockCatch] = graphic,
            [ThemeBrush.BlockCase] = graphic,
            [ThemeBrush.BlockParenthesis] = graphic,
            [ThemeBrush.BlockBracket] = graphic,
            [ThemeBrush.BlockRange] = range,
            [ThemeBrush.BlockHintBackground] = hint,
            [ThemeBrush.BlockHintForeground] = text,
            [ThemeBrush.BlockHintHoverForeground] = highContrast ? text : ThemeColorMath.EnsureTextContrast(graphic, hint),
            [ThemeBrush.BlockKeywordForeground] = keyword.Foreground,
            [ThemeBrush.BlockKeywordBackground] = keyword.Background,
            [ThemeBrush.BlockSymbolForeground] = symbol.Foreground,
            [ThemeBrush.BlockSymbolBackground] = symbol.Background
        };
    }

    private static Color ReadColor(string? preference, Color fallback) =>
        ReadOptionalColor(preference) ?? fallback;

    private static Color? ReadOptionalColor(string? preference) =>
        SqlColorPreference.TryParseRgb(preference, out var rgb)
            ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) : null;

    private static Color Tint(Color accent, Color background, Color text, double opacity)
    {
        var alpha = (byte)Math.Round(255 * opacity);
        // 一般文字至少 4.5:1；淡底不要求圖形的 3:1，避免大區間壓過 SQL 與原生選取。
        while (alpha > 0 && ThemeColorMath.Contrast(text,
            ThemeColorMath.Composite(Color.FromArgb(alpha, accent.R, accent.G, accent.B), background)) < 4.5)
            alpha--;
        return Color.FromArgb(alpha, accent.R, accent.G, accent.B);
    }
}
