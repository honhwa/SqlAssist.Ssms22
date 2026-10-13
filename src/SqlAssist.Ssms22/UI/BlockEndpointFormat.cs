using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace SqlAssist.Ssms22.UI;

internal static class BlockEndpointFormat
{
    public const string Keyword = "SqlAssist.BlockKeyword";
    public const string Symbol = "SqlAssist.BlockSymbol";

    /// <summary>游標進到字串裡面時，那兩個引號用的那一組。</summary>
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

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = BlockEndpointFormat.Quote)]
[Name(BlockEndpointFormat.Quote)]
[Order(After = Priority.High)]
[UserVisible(false)]
internal sealed class BlockQuoteFormat : ClassificationFormatDefinition
{
    public BlockQuoteFormat() => DisplayName = ChromeText.BlockQuoteFormat;
}
