using System;
using System.Collections.Generic;
using System.Windows.Media;
using SqlAssist.Core.Settings;

namespace SqlAssist.Ssms22.UI;

/// <summary>區塊各表面共用單一色相；輸入取自目前檢視，不保存深淺主題旗標。</summary>
internal static class BlockPalette
{
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
        var keyword = Endpoint(globalInk, settings?.BlockKeywordBackground, symbol: false);
        // 細項只覆寫指定通道；留空或無效值繼承全域「原始基準值」，再依實際背景校正。
        var symbol = Endpoint(ReadOptionalColor(settings?.BlockSymbolForeground) ?? globalInk, settings?.BlockSymbolBackground,
            symbol: true, ReadColor(settings?.BlockKeywordBackground, accent));

        (Color Foreground, Color Background) Endpoint(Color? explicitInk, string? backgroundPreference, bool symbol, Color? defaultBackground = null)
        {
            // 端點面積小，採實色高亮而非區間淡底；分類標籤才能改字色，marker 前景其實是框線。
            if (highContrast) return (background, text);

            var seed = ReadColor(backgroundPreference, defaultBackground ?? accent);

            // 一個顏色都沒自訂時，端點走標記層的推導而不是直接拿強調色當底色：強調色的明度跟著
            // 佈景翻轉，而字色只推到剛好 4.5 就停，深色佈景上會變成亮底灰字。
            if (explicitInk is null && seed == accent)
            {
                // 符號——括號、方括號、字串引號——的種子固定是金黃，不隨主題走：它與搜尋命中
                // 讀的是同一件事（「這裡被劃起來了」），與 SQL 的意思無關。但它與命中分居明度軸
                // 兩側——這裡是金底白字、命中是亮黃深字——兩者同色的話，「命中停在哪裡」與
                // 「配對的兩端在哪裡」就疊成同一塊顏色。字色走 GoldInk 而不是 LightInk：底色
                // 調亮之後淺色佈景的深字在金黃上讀得到，沿用「讀得到就留著」會變成黃底黑字。
                if (symbol) return (TextMarkColors.GoldInk, TextMarkColors.GoldFill(background));

                // 關鍵字端點讀的是「這是哪一層區塊」，跟著強調色走，字色留表面自己的前景。
                var mark = TextMarkColors.DarkFill(seed, background, TextMarkColors.Strong);
                return (TextMarkColors.LightInk(mark, text), mark);
            }

            // 使用者指定過顏色就以他指定的為準，只做對比校正；套標記層的亮度帶等於把他挑的顏色改掉。
            var fill = ThemeColorMath.EnsureGraphicContrast(seed, background);
            if (explicitInk is { } requested)
                return (requested, ThemeColorMath.EnsureBackgroundForText(fill, requested, background));
            return (ThemeColorMath.EnsureTextContrast(text, fill), fill);
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
