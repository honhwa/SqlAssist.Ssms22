using System;
using System.Windows.Media;

namespace SqlAssist.Ssms22.UI;

/// <summary>
/// 文字標記的底色與字色：把<b>一小段</b>文字整個染起來，要它從周圍跳出來的那一類。
/// 搜尋命中的兩級與區塊端點的高亮都走這裡。
/// </summary>
/// <remarks>
/// <b>鋪滿整行或整個區間的分類色不走這一層</b>（差異比對的加／刪、區塊範圍）。那一類要的是
/// 融入——底色淡、原本的文字顏色照舊讀得出來；這一類要的是跳出來——底色實、字色重算。
/// 兩類混成一份的下場是差異檢視變成一整片深色塊，而命中仍然淡到看不見。面積決定屬於哪一類。
///
/// 這一層有<b>兩組</b>，差別在底色落在明度軸的哪一側，以及顏色從哪裡來：
///
/// <b>螢光筆組</b>是固定的亮黃配深字，用在搜尋命中與符號端點（括號、方括號、字串引號）。
/// 它<b>不從強調色推導</b>：標記說的是「這一段被劃起來了」，與主題色無關，而在任何佈景上
/// 螢光筆黃都是同一個意思；跟著強調色走的那一版，同一個搜尋字會隨主題變成紫的、綠的、藍的。
///
/// <b>深色組</b>由強調色推導，用在關鍵字端點（BEGIN／END、TRY／CATCH、CASE）。那一組跟著
/// 主題走是因為它讀的是「這是哪一層區塊」，而層級的色相本來就是主題的一部分。
///
/// 兩組都要在深色與淺色佈景上分得開、字色讀得到，但方向相反——螢光筆組是亮底深字，深色組是
/// 暗底淺字——所以各有一組常數與各一條推導，門檻也不同。
///
/// 深色組的規則，每一條都來自實機上看得到的症狀：
///
/// <b>底色一律落在同一側</b>（深）。主題強調色的明度會<b>跟著佈景翻轉</b>——淺色佈景的
/// 強調色是深紫，深色佈景是亮紫。直接拿它當底色，配對的字色就跟著翻轉，於是同一組的兩級
/// 分居明度軸兩側：深色佈景上「目前那一處」變成亮底深字，而它旁邊較弱的那一級卻是暗底白字，
/// 看起來比它還強。
///
/// <b>字色走這一側能到的最純的那一端</b>，4.5 只是下限不是目標。一達到 4.5 就停的那一版停在
/// 中灰，而中灰配飽和彩底是所有組合裡最難看的一個。
///
/// 兩組共用的最後一條是<b>分級只動明度與飽和，不離色相那一族</b>：螢光筆的兩級是同一支筆在
/// 同一處再劃一層，差的是深淺；換成另一個色相就變成兩件事，而不是同一件事的兩種狀態。
/// </remarks>
internal static class TextMarkColors
{
    /// <summary>同一組的兩級之間至少要有的對比；色度與字重在這之上再補。</summary>
    public const double LevelSeparation = 1.3;

    /// <summary>深色組的底色與它所在表面至少要有的對比；低於它就等於沒有標記。</summary>
    /// <remarks>
    /// 比一般的 3:1 低，因為深色佈景上這兩件事會互相擠：表面本身就在深的那一側，而底色又不能
    /// 亮過 <see cref="MaximumDarkLuminance"/>，中間只剩這麼窄的一段。補在色度與字重上。
    /// </remarks>
    public const double DarkSeparation = 1.45;

    /// <summary>深色組底色的亮度上限；再亮下去淺色字就過不了 4.5。</summary>
    public const double MaximumDarkLuminance = 0.18;

    /// <summary>深色組的那一級：飽和，貼著亮度上限，讓它在整片內容裡最先被看到。</summary>
    public static MarkStrength Strong { get; } = new(MaximumDarkLuminance, 1.0);

    /// <summary>螢光筆的底色與它所在表面至少要有的對比。</summary>
    /// <remarks>
    /// 比 <see cref="DarkSeparation"/> 低，因為這一組靠的是色相：白紙上的螢光筆看得到是因為它
    /// 是黃的，不是因為它比紙暗。要求到深色組那個門檻，亮黃只能一路壓成土黃，而那已經不像
    /// 螢光筆了。字讀得到由這一組的深字負責——它與底色的對比在 12:1 以上，不靠這個門檻。
    /// </remarks>
    public const double HighlightSeparation = 1.25;

    /// <summary>螢光筆底色的亮度下緣；再暗下去不像螢光筆，所以推不進去時停在這裡。</summary>
    public const double MinimumHighlightLuminance = 0.5;

    /// <summary>螢光筆底色的亮度上緣；再亮下去色相被白吃掉，同樣停在這裡。</summary>
    public const double MaximumHighlightLuminance = 0.85;

    /// <summary>劃一層的螢光筆黃：一般的搜尋命中與符號端點。</summary>
    public static Color HighlightSeed { get; } = Color.FromRgb(0xFF, 0xE0, 0x66);

    /// <summary>同一支筆在同一處再劃一層：目前停在的那一處。</summary>
    /// <remarks>
    /// 疊第二層會往金黃偏，也自然比一層深，所以這一個不是把種子壓暗而是另一個顏色。挑得比真實的
    /// 再劃一層深一些——真的疊一層只有一成左右的明度差，而 <see cref="LevelSeparation"/> 要的
    /// 那一段它給不出來。
    /// </remarks>
    public static Color HighlightCurrentSeed { get; } = Color.FromRgb(0xFF, 0xB9, 0x00);

    /// <summary>單獨一處螢光筆標記的底色：固定亮黃，只在表面也亮到分不開時才往遠離表面推。</summary>
    public static Color HighlightFill(Color surface) => PushAway(HighlightSeed, surface, HighlightSeparation);

    /// <summary>螢光筆的兩級底色：目前那一處與一般的命中。</summary>
    public static (Color Current, Color Match) HighlightPair(Color surface)
    {
        var match = HighlightFill(surface);
        var current = PushAway(HighlightCurrentSeed, surface, HighlightSeparation);

        // 表面亮到必須把底色壓深時，壓的動作會把兩級擠在一起；目前那一處再往同一個方向推。
        // 推多遠由兩者實際達到的對比算而不是固定值——固定值在中灰表面上會推過頭或推不到。
        if (ThemeColorMath.Contrast(current, match) < LevelSeparation)
        {
            current = WithLuminance(current, InBand(FurtherThan(surface, match, LevelSeparation)));
        }

        return (current, match);
    }

    /// <summary>螢光筆底上的字色：固定的深字。</summary>
    /// <remarks>
    /// 這一組的底色是亮的，所以字色往深的那一端走，與 <see cref="LightInk"/> 正好相反。留著表面
    /// 原本的前景是為了自訂過的淺色佈景（它可能不是純黑），讀不到才真的用黑。
    /// </remarks>
    public static Color DarkInk(Color fill, Color preferred) =>
        ThemeColorMath.Contrast(preferred, fill) >= 4.5 ? preferred : Colors.Black;

    /// <summary>一級標記的強度：落在哪一條亮度帶、色度留多少。</summary>
    public readonly struct MarkStrength
    {
        public MarkStrength(double luminance, double chroma)
        {
            Luminance = luminance;
            Chroma = chroma;
        }

        /// <summary>目標相對亮度。</summary>
        public double Luminance { get; }

        /// <summary>保留原色多少色度；1 是原樣，0 是同亮度的灰。</summary>
        public double Chroma { get; }
    }

    /// <summary>把種子色調成一塊深色組的標記底：同色相、指定的強度、與表面分得開。</summary>
    public static Color DarkFill(Color seed, Color surface, MarkStrength strength)
    {
        var fill = WithLuminance(Desaturate(seed, strength.Chroma), strength.Luminance);

        // 落在與表面同一條亮度帶時要推開，而方向是**遠離表面**：表面比這一塊暗就往淺推，反之
        // 往深推。往固定方向推的那一版，在深色佈景上會先穿過表面的亮度再往黑走，推完是一塊
        // 幾乎全黑、色相也沒了的方塊。往淺推守住 MaximumDarkLuminance——字讀不到是這一層唯一
        // 不能讓的事，寧可對比停在門檻上由色度與字重補。
        var surfaceLuminance = ThemeColorMath.Luminance(surface);

        for (var step = 0; step < 8 && ThemeColorMath.Contrast(fill, surface) < DarkSeparation; step++)
        {
            var current = ThemeColorMath.Luminance(fill);
            var next = surfaceLuminance < current
                ? Math.Min(current * 1.6, MaximumDarkLuminance)
                : current * 0.6;
            if (Math.Abs(next - current) < 0.0001 || next < 0.005) break;
            fill = WithLuminance(fill, next);
        }

        return fill;
    }

    /// <summary>深色組標記底上的字色：優先用這個表面原本的前景，讀不到就走到最淺的那一端。</summary>
    public static Color LightInk(Color fill, Color preferred) =>
        ThemeColorMath.Contrast(preferred, fill) >= 4.5 ? preferred : Colors.White;

    /// <summary>往自己的中間灰靠，保留色相只收色度。</summary>
    private static Color Desaturate(Color color, double chroma)
    {
        if (chroma >= 1) return color;

        var middle = (Math.Max(color.R, Math.Max(color.G, color.B))
            + Math.Min(color.R, Math.Min(color.G, color.B))) / 2.0;
        return Color.FromRgb(Mix(color.R), Mix(color.G), Mix(color.B));

        byte Mix(byte channel) => (byte)Math.Round(middle + ((channel - middle) * chroma));
    }

    /// <summary>把固定亮黃往遠離表面的方向推到過門檻，最多推到亮度帶的邊上。</summary>
    private static Color PushAway(Color seed, Color surface, double separation) =>
        ThemeColorMath.Contrast(seed, surface) >= separation
            ? seed
            : WithLuminance(seed, InBand(JustBeyond(surface, seed, separation)));

    /// <summary>與表面剛好相差指定對比所需的亮度；方向是遠離表面。</summary>
    /// <remarks>
    /// 用表面算，不用固定值：固定值在表面本身就很亮或很暗時會推過頭。表面選定之後方向也跟著定，
    /// 往回推就會穿過表面。
    /// </remarks>
    private static double JustBeyond(Color surface, Color reference, double separation)
    {
        var surfaceLuminance = ThemeColorMath.Luminance(surface);
        return ThemeColorMath.Luminance(reference) > surfaceLuminance
            ? ((surfaceLuminance + 0.05) * separation) - 0.05
            : ((surfaceLuminance + 0.05) / separation) - 0.05;
    }

    /// <summary>比對照色再遠離表面若干倍對比所需的亮度。</summary>
    /// <remarks>
    /// 同一側的兩個顏色之間，對比就等於各自與表面對比的比值，所以倍率乘在對照色上換算回亮度即可；
    /// 乘在表面上算出來的是「與表面差多少倍」，那是另一個問題。
    /// </remarks>
    private static double FurtherThan(Color surface, Color reference, double ratio)
    {
        var referenceLuminance = ThemeColorMath.Luminance(reference);
        return referenceLuminance > ThemeColorMath.Luminance(surface)
            ? ((referenceLuminance + 0.05) * ratio) - 0.05
            : ((referenceLuminance + 0.05) / ratio) - 0.05;
    }

    /// <summary>把亮度收進螢光筆那一條帶；推不進去時停在邊上，推出帶外就不像螢光筆了。</summary>
    private static double InBand(double luminance) =>
        Math.Min(Math.Max(luminance, MinimumHighlightLuminance), MaximumHighlightLuminance);

    /// <summary>往黑或往白混到目標亮度；二分只在配色變更時跑，不進捲動或游標路徑。</summary>
    private static Color WithLuminance(Color color, double target)
    {
        var toward = ThemeColorMath.Luminance(color) > target ? Colors.Black : Colors.White;
        var low = 0.0;
        var high = 1.0;
        var result = color;

        for (var step = 0; step < 12; step++)
        {
            var mid = (low + high) / 2;
            result = ThemeColorMath.Composite(
                Color.FromArgb((byte)Math.Round(mid * 255), toward.R, toward.G, toward.B), color);
            if (ThemeColorMath.Luminance(result) > target == (toward == Colors.Black)) low = mid;
            else high = mid;
        }

        return result;
    }
}
