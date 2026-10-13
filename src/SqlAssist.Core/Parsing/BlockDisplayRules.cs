using SqlAssist.Core.Settings;

namespace SqlAssist.Core.Parsing;

/// <summary>共用呈現篩選；關掉某一類只影響顯示，不改變 END 的解析歸屬。</summary>
public static class BlockDisplayRules
{
    public static bool IsSymbol(BlockKind kind) => kind is BlockKind.Parenthesis or BlockKind.Bracket or BlockKind.String;

    /// <summary>游標停在這一對的結尾端點上：已經進到裡面，而不是還待在它的左邊。</summary>
    /// <remarks>
    /// 配對只在游標貼著端點時選得到，而選到之後分得出兩種狀態：停在兩個引號中間（剛打完
    /// <c>'</c> 的 <c>'|'</c>、或 <c>'abc|'</c>）命中的是結尾那一個，停在引號左邊
    /// （<c>|'abc'</c>）命中的是開頭那一個。兩者拿到的是同一個配對、同一個游標位置只落在
    /// 其中一邊，所以狀態要另外問這一格。
    /// </remarks>
    public static bool CaretAtClosing(BlockPair pair, int position)
    {
        foreach (var span in pair.Closing)
        {
            if (span.Contains(position))
            {
                return true;
            }
        }

        return false;
    }

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

    public static bool ShowRange(SqlAssistSettings settings, bool sameLine, bool highContrast) =>
        settings.Enabled && settings.BlockMatchingEnabled && settings.BlockRangeBackground &&
        (!sameLine || settings.BlockSameLineBackground) && !highContrast;
}
