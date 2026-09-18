using SqlAssist.Core.Parsing;
using SqlAssist.Core.Settings;
using Xunit;

namespace SqlAssist.Core.Tests.Parsing;

public sealed class BlockDisplayRulesTests
{
    [Fact]
    public void 預設顯示最近區間概覽與提示但不啟用重複摺疊()
    {
        var settings = new SqlAssistSettings();
        Assert.True(settings.BlockRangeInside);
        Assert.True(settings.BlockKeywordHighlight);
        Assert.True(settings.BlockRangeBackground);
        Assert.True(settings.BlockGlyphs);
        Assert.True(settings.BlockStructure);
        Assert.True(settings.BlockMatchCase);
        Assert.True(settings.BlockMatchParentheses);
        Assert.False(settings.BlockOutlining);
        Assert.True(settings.BlockOverview);
        Assert.True(settings.BlockSameLineBackground);
        Assert.True(settings.BlockContextHint);
        Assert.True(settings.BlockSymbolHighlight);
    }

    [Fact]
    public void 區塊內一般文字能找到最近一層且不需要端點()
    {
        const string sql = "BEGIN\nBEGIN\nSELECT 1;\nEND\nEND";
        var matcher = new BlockMatcher(sql);
        var position = sql.IndexOf("SELECT", System.StringComparison.Ordinal);
        Assert.Null(matcher.FindPairAt(position));
        var pair = BlockDisplayRules.FindContext(matcher, position, new SqlAssistSettings());
        Assert.NotNull(pair);
        Assert.Equal(6, pair.Span.Start);
        Assert.Null(BlockDisplayRules.FindContext(matcher, position, new SqlAssistSettings { Enabled = false }));
    }

    [Fact]
    public void 略過同行括號後仍選外層跨行背景()
    {
        const string sql = "BEGIN\nSELECT (1 + 2);\nEND";
        var matcher = new BlockMatcher(sql);
        var position = sql.IndexOf("1 +", System.StringComparison.Ordinal);
        bool CrossLine(BlockPair pair) => sql.Substring(pair.Span.Start, pair.Span.Length).Contains('\n');
        var settings = new SqlAssistSettings();
        Assert.Equal(BlockKind.Parenthesis, BlockDisplayRules.FindContext(matcher, position, settings)!.Kind);
        Assert.Equal(BlockKind.Block, BlockDisplayRules.FindContext(matcher, position, settings, CrossLine)!.Kind);
        Assert.Equal(BlockKind.Block, BlockDisplayRules.FindContext(matcher, position,
            new SqlAssistSettings { BlockMatchParentheses = false })!.Kind);
    }

    [Fact]
    public void 關閉CASE時上下文回退外層且不影響配對索引()
    {
        const string sql = "BEGIN\nSELECT CASE WHEN 1 = 1 THEN 2 END;\nEND";
        var matcher = new BlockMatcher(sql);
        var position = sql.IndexOf("THEN", System.StringComparison.Ordinal);
        Assert.Equal(BlockKind.Case, matcher.GetEnclosingBlock(position)!.Kind);
        Assert.Equal(BlockKind.Block, BlockDisplayRules.FindContext(matcher, position,
            new SqlAssistSettings { BlockMatchCase = false })!.Kind);
    }

    [Fact]
    public void 同行預設塗背景且可獨立關閉()
    {
        var settings = new SqlAssistSettings();
        Assert.True(BlockDisplayRules.ShowRange(settings, sameLine: true, highContrast: false));
        Assert.True(BlockDisplayRules.ShowRange(settings, sameLine: false, highContrast: false));
        Assert.True(BlockDisplayRules.ShowRange(new SqlAssistSettings { BlockSameLineBackground = true }, true, false));
        Assert.False(BlockDisplayRules.ShowRange(new SqlAssistSettings { BlockSameLineBackground = false }, true, false));
    }

    [Fact]
    public void 兩個呈現層可獨立開關()
    {
        Assert.True(BlockDisplayRules.ShowRange(new SqlAssistSettings { BlockKeywordHighlight = false }, false, false));
        Assert.False(BlockDisplayRules.ShowRange(new SqlAssistSettings { BlockRangeBackground = false }, false, false));
    }

    [Fact]
    public void 總開關與高對比不塗背景()
    {
        Assert.False(BlockDisplayRules.ShowRange(new SqlAssistSettings { Enabled = false }, false, false));
        Assert.False(BlockDisplayRules.ShowRange(new SqlAssistSettings { BlockMatchingEnabled = false }, false, false));
        Assert.False(BlockDisplayRules.ShowRange(new SqlAssistSettings { BlockSameLineBackground = true }, false, true));
    }

    [Fact]
    public void 關閉CASE與括號不影響一般區塊()
    {
        var settings = new SqlAssistSettings { BlockMatchCase = false, BlockMatchParentheses = false };
        Assert.True(BlockDisplayRules.IsKindEnabled(BlockKind.Block, settings));
        Assert.False(BlockDisplayRules.IsKindEnabled(BlockKind.Case, settings));
        Assert.False(BlockDisplayRules.IsKindEnabled(BlockKind.Parenthesis, settings));
        Assert.False(BlockDisplayRules.IsKindEnabled(BlockKind.Bracket, settings));
        Assert.False(BlockDisplayRules.IsKindEnabled(BlockKind.String, settings));
        Assert.False(BlockDisplayRules.IsKindEnabled(BlockKind.Try, new SqlAssistSettings { Enabled = false }));
    }

    /// <remarks>
    /// 符號的 mark 直接蓋在被指到的字元上，所以另外給了一個只關它的開關：
    /// 關掉之後 <c>( )</c>、<c>[ ]</c>、<c>' '</c> 照樣配對、照樣有區間淡底，
    /// 只是不再塗色；關鍵字那一類完全不受影響。
    /// </remarks>
    [Fact]
    public void 符號端點可單獨關閉而其他種類照舊()
    {
        var settings = new SqlAssistSettings();
        Assert.True(BlockDisplayRules.ShowEndpoint(BlockKind.Block, settings));
        Assert.True(BlockDisplayRules.ShowEndpoint(BlockKind.Case, settings));
        Assert.True(BlockDisplayRules.ShowEndpoint(BlockKind.Parenthesis, settings));
        Assert.True(BlockDisplayRules.ShowEndpoint(BlockKind.Bracket, settings));
        Assert.True(BlockDisplayRules.ShowEndpoint(BlockKind.String, settings));

        var noSymbols = new SqlAssistSettings { BlockSymbolHighlight = false };
        Assert.False(BlockDisplayRules.ShowEndpoint(BlockKind.Parenthesis, noSymbols));
        Assert.False(BlockDisplayRules.ShowEndpoint(BlockKind.Bracket, noSymbols));
        Assert.False(BlockDisplayRules.ShowEndpoint(BlockKind.String, noSymbols));
        Assert.True(BlockDisplayRules.ShowEndpoint(BlockKind.Block, noSymbols));
        Assert.True(BlockDisplayRules.ShowEndpoint(BlockKind.Try, noSymbols));

        // 關掉的只是端點上色：括號照樣參與配對，區間背景也照舊。
        Assert.True(BlockDisplayRules.IsKindEnabled(BlockKind.Parenthesis, noSymbols));
        Assert.True(BlockDisplayRules.ShowRange(noSymbols, sameLine: false, highContrast: false));
    }

    /// <summary>端點高亮的上一層開關仍然管得住符號。</summary>
    [Fact]
    public void 關閉端點高亮與總開關時符號也不上色()
    {
        var noEndpoints = new SqlAssistSettings { BlockKeywordHighlight = false };
        Assert.False(BlockDisplayRules.ShowEndpoint(BlockKind.Block, noEndpoints));
        Assert.False(BlockDisplayRules.ShowEndpoint(BlockKind.Parenthesis, noEndpoints));
        Assert.True(BlockDisplayRules.ShowRange(noEndpoints, sameLine: false, highContrast: false));

        foreach (var settings in new[]
                 {
                     new SqlAssistSettings { Enabled = false },
                     new SqlAssistSettings { BlockMatchingEnabled = false }
                 })
        {
            Assert.False(BlockDisplayRules.ShowEndpoint(BlockKind.Block, settings));
            Assert.False(BlockDisplayRules.ShowEndpoint(BlockKind.Bracket, settings));
        }
    }
}
