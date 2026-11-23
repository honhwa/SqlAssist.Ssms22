using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace SqlAssist.Ssms22.UI;

/// <summary>INSERT 欄位與值互相標記的分類型別與格式；黃底深字由 tagger 寫入。</summary>
internal static class InsertPairFormat
{
    public const string Name = "SqlAssist.InsertColumnValue";

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Name)]
    [BaseDefinition("text")]
    internal static ClassificationTypeDefinition Type = null!;
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = InsertPairFormat.Name)]
[Name(InsertPairFormat.Name)]
[Order(After = Priority.High)]
[UserVisible(false)]
internal sealed class InsertPairFormatDefinition : ClassificationFormatDefinition
{
    public InsertPairFormatDefinition() => DisplayName = ChromeText.InsertColumnValueFormat;
}
