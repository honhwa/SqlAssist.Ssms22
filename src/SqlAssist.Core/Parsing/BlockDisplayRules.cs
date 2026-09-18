using SqlAssist.Core.Settings;

namespace SqlAssist.Core.Parsing;

/// <summary>共用呈現篩選；關掉某一類只影響顯示，不改變 END 的解析歸屬。</summary>
public static class BlockDisplayRules
{
    public static bool IsSymbol(BlockKind kind) => kind is BlockKind.Parenthesis or BlockKind.Bracket or BlockKind.String;

    /// <summary>沿既有祖先索引找最近可見種類，不重新掃 SQL；可排除不需塗色的同行小括號。</summary>
    public static BlockPair? FindContext(BlockMatcher matcher, int position, SqlAssistSettings settings,
        System.Func<BlockPair, bool>? accepts = null)
    {
        return matcher.FindEnclosingBlock(position, (Settings: settings, Accepts: accepts),
            static (pair, state) => IsKindEnabled(pair.Kind, state.Settings) && (state.Accepts is null || state.Accepts(pair)));
    }

    public static bool IsKindEnabled(BlockKind kind, SqlAssistSettings settings) =>
        settings.Enabled && settings.BlockMatchingEnabled && kind switch
        {
            BlockKind.Case => settings.BlockMatchCase,
            BlockKind.Parenthesis or BlockKind.Bracket or BlockKind.String => settings.BlockMatchParentheses,
            _ => true
        };

    /// <summary>
    /// 游標所在配對的兩端要不要上色；符號那一類另外受
    /// <see cref="SqlAssistSettings.BlockSymbolHighlight"/> 管。
    /// </summary>
    /// <remarks>
    /// 與 <see cref="IsKindEnabled"/> 分開：那一條問的是「這個種類參不參與配對」，
    /// 關掉它連區間、色帶、導覽都會一起少一層；這裡只決定端點上不上色。
    /// 符號的 mark 直接蓋在被指到的字元上，因此留了一個只關它的開關——
    /// 關掉之後 <c>( )</c>、<c>' '</c> 照樣配對、照樣有區間淡底，只是不再被塗色。
    /// </remarks>
    public static bool ShowEndpoint(BlockKind kind, SqlAssistSettings settings) =>
        settings.Enabled && settings.BlockMatchingEnabled && settings.BlockKeywordHighlight &&
        (!IsSymbol(kind) || settings.BlockSymbolHighlight);

    public static bool ShowRange(SqlAssistSettings settings, bool sameLine, bool highContrast) =>
        settings.Enabled && settings.BlockMatchingEnabled && settings.BlockRangeBackground &&
        (!sameLine || settings.BlockSameLineBackground) && !highContrast;
}
