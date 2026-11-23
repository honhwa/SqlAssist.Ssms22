using System;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlAssist.Core.Parsing;
using SqlAssist.Core.Settings;
using SqlAssist.Core.Statements;
using SqlAssist.Ssms22.Editor;
using SqlAssist.Ssms22.Settings;

namespace SqlAssist.Ssms22.Blocks;

/// <summary>
/// 同一檢視一份 INSERT 欄值對應狀態；監聽游標與文字變更，只保留「另一端」要標的位置。
/// </summary>
/// <remarks>
/// 分析是純詞法掃描，不需要像 <see cref="BlockAnalysis"/> 那樣排背景工作；
/// 但游標跟著編輯位移時 <c>Caret.PositionChanged</c> 不發（見 CaretHint），
/// 所以文字變更也要監聽，並一併排到命令後再算，避免每次按鍵都重掃整份文件。
/// </remarks>
internal sealed class InsertPairViewState : IDisposable
{
    private readonly IWpfTextView _view;
    private bool _disposed;
    public SqlAssistSettings Settings { get; private set; } = SqlAssistSettingsStore.Current;
    public SnapshotSpan? Highlight { get; private set; }
    public ITextSnapshot? Snapshot { get; private set; }
    public event EventHandler? Changed;

    public static InsertPairViewState Get(IWpfTextView view) =>
        view.Properties.GetOrCreateSingletonProperty(() => new InsertPairViewState(view));

    private InsertPairViewState(IWpfTextView view)
    {
        _view = view;
        view.Caret.PositionChanged += OnCaret;
        view.TextBuffer.Changed += OnText;
        view.LayoutChanged += OnLayout;
        view.Closed += OnClosed;
        SqlAssistSettingsStore.Changed += OnSettings;
        Refresh();
    }

    private void OnCaret(object sender, CaretPositionChangedEventArgs args) => Schedule();
    private void OnText(object sender, TextContentChangedEventArgs args) => Schedule();
    private void OnLayout(object sender, TextViewLayoutChangedEventArgs args)
    {
        if (args.NewSnapshot != args.OldSnapshot) Schedule();
    }
    private void OnSettings(object? sender, EventArgs args) => Schedule();
    private void OnClosed(object sender, EventArgs args) =>
        SqlAssistPlatformGuard.Run("關閉 INSERT 欄值對應狀態", Dispose);

    private void Schedule() =>
        TextViewDispatch.AfterCurrentCommand(_view, "更新 INSERT 欄值對應", _ => Refresh());

    private void Refresh()
    {
        if (_disposed || _view.IsClosed) return;
        var old = Highlight;
        var oldSnapshot = Snapshot;
        Settings = SqlAssistSettingsStore.Current;
        Snapshot = _view.TextSnapshot;

        if (!Settings.Enabled || !Settings.HighlightInsertColumnValue)
        {
            Highlight = null;
        }
        else
        {
            var position = _view.Caret.Position.BufferPosition;
            var span = position.Snapshot.Version == Snapshot!.Version
                ? position.Position
                : position.TranslateTo(Snapshot, PointTrackingMode.Negative).Position;
            var mate = new SqlInsertColumnValueMatcher(Snapshot.GetText()).FindCounterpart(span);
            Highlight = mate is { } counterpart && counterpart.Length > 0 && counterpart.End <= Snapshot.Length
                ? new SnapshotSpan(Snapshot, counterpart.Start, counterpart.Length)
                : null;
        }

        if (old == Highlight && oldSnapshot == Snapshot) return;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _view.Caret.PositionChanged -= OnCaret;
        _view.TextBuffer.Changed -= OnText;
        _view.LayoutChanged -= OnLayout;
        _view.Closed -= OnClosed;
        SqlAssistSettingsStore.Changed -= OnSettings;
        Changed = null;
        Highlight = null;
        Snapshot = null;
    }
}
