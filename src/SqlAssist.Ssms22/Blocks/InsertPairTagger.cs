using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Ssms22.UI;

namespace SqlAssist.Ssms22.Blocks;

[Export(typeof(IViewTaggerProvider))]
[ContentType("SQL")]
[TextViewRole(PredefinedTextViewRoles.Editable)]
[TagType(typeof(ClassificationTag))]
internal sealed class InsertPairProvider : IViewTaggerProvider
{
    [Import] internal IClassificationTypeRegistryService Types { get; set; } = null!;
    [Import] internal IEditorFormatMapService Formats { get; set; } = null!;

    public ITagger<T>? CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag =>
        SqlAssistPlatformGuard.Create("建立 INSERT 欄值對應分類", () =>
            textView is IWpfTextView view && !view.IsClosed && buffer == view.TextBuffer
                ? new InsertPairTagger(view, Formats.GetEditorFormatMap(view), Types) as ITagger<T> : null);
}

/// <summary>游標落在 INSERT 欄位或值上時，只把另一端上黃底。</summary>
internal sealed class InsertPairTagger : ITagger<ClassificationTag>, IDisposable
{
    // 與搜尋命中同一族螢光筆黃：小面積實色標記，深字才讀得到。
    private static readonly Color Fill = Color.FromRgb(0xFF, 0xEE, 0x58);
    private static readonly Color Ink = Color.FromRgb(0x1A, 0x1A, 0x1A);

    private readonly IWpfTextView _view;
    private readonly IEditorFormatMap _formats;
    private readonly InsertPairViewState _state;
    private readonly ThemeRefreshQueue _queue;
    private readonly ClassificationTag? _tag;
    private ITagSpan<ClassificationTag>[] _tags = Array.Empty<ITagSpan<ClassificationTag>>();
    private bool _disposed;
    public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

    public InsertPairTagger(IWpfTextView view, IEditorFormatMap formats, IClassificationTypeRegistryService types)
    {
        _view = view;
        _formats = formats;
        _state = InsertPairViewState.Get(view);
        if (types.GetClassificationType(InsertPairFormat.Name) is { } type)
        {
            _tag = new ClassificationTag(type);
        }
        else
        {
            SqlAssistDiagnostics.WriteAlways($"找不到分類型別 {InsertPairFormat.Name}，INSERT 欄值對應不上色");
        }

        _queue = new ThemeRefreshQueue(view.VisualElement.Dispatcher,
            () => SqlAssistPlatformGuard.Probe("更新 INSERT 欄值對應配色", UpdateColors));
        _state.Changed += OnState;
        _formats.FormatMappingChanged += OnFormat;
        view.Closed += OnClosed;
        _queue.Request();
        Refresh();
    }

    public IEnumerable<ITagSpan<ClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (spans.Count == 0) yield break;
        foreach (var tag in _tags)
        {
            var span = tag.Span.Snapshot.Version == spans[0].Snapshot.Version
                ? tag.Span
                : tag.Span.TranslateTo(spans[0].Snapshot, SpanTrackingMode.EdgeExclusive);
            if (span.IsEmpty || !spans.IntersectsWith(span)) continue;
            yield return span == tag.Span ? tag : new TagSpan<ClassificationTag>(span, tag.Tag);
        }
    }

    private void UpdateColors()
    {
        if (_disposed || _view.IsClosed) return;
        // 高對比沒有中間色可調：跟系統選取色走，不放自訂黃底。
        var fill = SystemParameters.HighContrast ? SystemColors.HighlightColor : Fill;
        var ink = SystemParameters.HighContrast ? SystemColors.HighlightTextColor : Ink;
        var properties = _formats.GetProperties(InsertPairFormat.Name);
        if (properties[EditorFormatDefinition.ForegroundColorId] is Color oldInk && oldInk == ink &&
            properties[EditorFormatDefinition.BackgroundColorId] is Color oldFill && oldFill == fill &&
            properties[EditorFormatDefinition.ForegroundBrushId] is SolidColorBrush oldInkBrush && oldInkBrush.Color == ink &&
            properties[EditorFormatDefinition.BackgroundBrushId] is SolidColorBrush oldFillBrush && oldFillBrush.Color == fill)
        {
            return;
        }

        properties[EditorFormatDefinition.ForegroundColorId] = ink;
        properties[EditorFormatDefinition.ForegroundBrushId] = new SolidColorBrush(ink);
        properties[EditorFormatDefinition.BackgroundColorId] = fill;
        properties[EditorFormatDefinition.BackgroundBrushId] = new SolidColorBrush(fill);
        _formats.SetProperties(InsertPairFormat.Name, properties);
    }

    private void Refresh()
    {
        if (_disposed || _view.IsClosed) return;
        var snapshot = _view.TextSnapshot;
        var source = _state.Snapshot;
        var mate = _state.Highlight;
        var next = mate is { } span && source is not null && _tag is not null
            ? new[] { new TagSpan<ClassificationTag>(Project(source, span, snapshot), _tag) }
                as ITagSpan<ClassificationTag>[]
            : Array.Empty<ITagSpan<ClassificationTag>>();
        if (_tags.Length == next.Length && _tags.Length > 0 && _tags[0].Span == next[0].Span) return;
        var old = _tags;
        _tags = next;
        foreach (var tag in old.Concat(next))
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(tag.Span.TranslateTo(snapshot, SpanTrackingMode.EdgeExclusive)));
    }

    private static SnapshotSpan Project(ITextSnapshot source, SnapshotSpan span, ITextSnapshot target) =>
        span.Snapshot.Version == target.Version ? span : span.TranslateTo(target, SpanTrackingMode.EdgeExclusive);

    private void OnState(object? sender, EventArgs args) => Refresh();
    private void OnFormat(object? sender, FormatItemsEventArgs args)
    {
        if (EditorFormatChanges.Affects(args.ChangedItems, InsertPairFormat.Name)) _queue.Request();
    }
    private void OnClosed(object sender, EventArgs args) => SqlAssistPlatformGuard.Run("關閉 INSERT 欄值對應分類", Dispose);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.Dispose();
        _state.Changed -= OnState;
        _formats.FormatMappingChanged -= OnFormat;
        _view.Closed -= OnClosed;
        _tags = Array.Empty<ITagSpan<ClassificationTag>>();
    }
}
