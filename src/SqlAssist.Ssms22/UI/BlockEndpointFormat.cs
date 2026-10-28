using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace SqlAssist.Ssms22.UI;

internal static class BlockEndpointFormat
{
    public const string Keyword = "SqlAssist.BlockKeyword";
    public const string Symbol = "SqlAssist.BlockSymbol";
    public const string Quote = "SqlAssist.BlockQuote";

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Keyword)]
    [BaseDefinition("text")]
    internal static ClassificationTypeDefinition KeywordType = null!;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Symbol)]
    [BaseDefinition("text")]
    internal static ClassificationTypeDefinition SymbolType = null!;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Quote)]
    [BaseDefinition("text")]
    internal static ClassificationTypeDefinition QuoteType = null!;
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = BlockEndpointFormat.Keyword)]
[Name(BlockEndpointFormat.Keyword)]
[Order(After = Priority.High)]
[UserVisible(false)]
internal sealed class BlockKeywordFormat : ClassificationFormatDefinition
{
    public BlockKeywordFormat() => DisplayName = ChromeText.BlockEndpointFormat;
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = BlockEndpointFormat.Symbol)]
[Name(BlockEndpointFormat.Symbol)]
[Order(After = Priority.High)]
[UserVisible(false)]
internal sealed class BlockSymbolFormat : ClassificationFormatDefinition
{
    public BlockSymbolFormat() => DisplayName = ChromeText.BlockSymbolFormat;
}

/// <summary>字串引號自成一個分類：預設色與括號不同，兩者不能共用一個格式。</summary>
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = BlockEndpointFormat.Quote)]
[Name(BlockEndpointFormat.Quote)]
[Order(After = Priority.High)]
[UserVisible(false)]
internal sealed class BlockQuoteFormat : ClassificationFormatDefinition
{
    public BlockQuoteFormat() => DisplayName = ChromeText.BlockQuoteFormat;
}
