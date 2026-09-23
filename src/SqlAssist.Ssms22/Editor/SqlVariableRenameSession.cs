using System;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlAssist.Core.Parsing;
using SqlAssist.Ssms22.Snippets;

namespace SqlAssist.Ssms22.Editor;

/// <summary>
/// F2 變數重新命名的工作階段：選取名稱、把每一處一起改、按鍵結束它。
/// </summary>
/// <remarks>
/// <b>核心機制。</b>使用 <see cref="ITrackingSpan"/> 追蹤同一批次裡每一處變數名稱的位置。
/// 游標那一處（主出現處）使用 <see cref="SpanTrackingMode.EdgeInclusive"/>，讓它在邊界
/// 插入時自動擴展；其餘出現處使用 <see cref="SpanTrackingMode.EdgeExclusive"/>，由工作階段
/// 統一整段替換。主出現處的每一個變更都會被鏡射到其他位置，達到「打一處、改全部」的效果。
///
/// <b>為什麼不用多重選取。</b>與原始版本相同：沒有 SSMS 測試環境，錯誤代價太高。
///
/// <b>復原粒度。</b>使用者打一個字是一次編輯，鏡射是另一次；Ctrl+Z 會先還原鏡射、再還原
/// 主出現處。這是已知的取捨，換來的是每一處結果都正確。
/// </remarks>
internal sealed class SqlVariableRenameSession
{
    private static SqlVariableRenameSession? _active;

    /// <summary>
    /// 是否有工作階段正在進行中。
    /// </summary>
    /// <remarks>
    /// 只有事件處理常式全部掛好、<see cref="Start"/> 成功完成後才算數。
    /// 這個判斷要快，因為 <see cref="SqlShellCommandFilter"/> 在每一個按鍵上都要問一次。
    /// </remarks>
    public static bool IsActive => _active is { _started: true, _disposed: false };

    private readonly IWpfTextView _view;
    private readonly ITextBuffer _buffer;
    private readonly IServiceProvider _serviceProvider;
    private readonly SqlVariableRenameTarget _target;
    private readonly ITrackingSpan[] _spans;
    private readonly int _primaryIndex;
    private readonly string _originalName;

    private bool _started;
    private bool _disposed;
    private bool _applyingEdit;
    private bool _hasSeenFirstChange;

    private SqlVariableRenameSession(
        IWpfTextView view,
        IServiceProvider serviceProvider,
        SqlVariableRenameTarget target)
    {
        _view = view;
        _buffer = view.TextBuffer;
        _serviceProvider = serviceProvider;
        _target = target;
        _originalName = target.Name;

        var snapshot = _buffer.CurrentSnapshot;
        var spans = new ITrackingSpan[target.NameStarts.Count];

        for (var i = 0; i < spans.Length; i++)
        {
            var span = new SnapshotSpan(
                snapshot,
                new Span(target.NameStarts[i], target.NameLength));

            spans[i] = snapshot.CreateTrackingSpan(
                span,
                i == target.PrimaryIndex
                    ? SpanTrackingMode.EdgeInclusive
                    : SpanTrackingMode.EdgeExclusive);
        }

        _spans = spans;
        _primaryIndex = target.PrimaryIndex;
    }

    /// <summary>
    /// 在游標所在的區域變數上開始重新命名。
    /// </summary>
    public static bool Begin(
        IWpfTextView view,
        IServiceProvider serviceProvider,
        out string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        message = string.Empty;

        // 若上一個工作階段還在，先結算它。直接丟棄會讓撞名永遠不被發現。
        if (_active is { } previous)
        {
            previous.TryCommit(out var warning);
            if (warning.Length > 0)
            {
                SqlAssistStatusBar.Show(previous._serviceProvider, warning);
            }
        }

        var target = SqlVariableRename.FindAt(
            view.TextSnapshot.GetText(),
            view.Caret.Position.BufferPosition.Position);

        if (target is null)
        {
            message = "游標處不是區域變數。";
            return false;
        }

        if (!SqlAssistPlatformGuard.Run(
                "開始變數重新命名",
                () =>
                {
                    new SqlVariableRenameSession(view, serviceProvider, target).Start();
                    return true;
                },
                fallback: false))
        {
            message = "重新命名沒能開始，詳見診斷紀錄檔。";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 把殼層命令交給進行中的工作階段處理。
    /// </summary>
    public static bool TryHandleShellCommand(
        IWpfTextView view,
        Guid group,
        uint commandId,
        bool execute)
    {
        var session = For(view);
        if (session is null)
            return false;

        var key = SqlSnippetSurroundKeys.MapKey(group, commandId);

        if (key is Key.Up or Key.Down or Key.Enter or Key.Tab)
        {
            if (!execute)
                return true;

            if (!session.TryCommit(out var message) && message.Length > 0)
            {
                SqlAssistStatusBar.Show(session._serviceProvider, message);
            }
            return true;
        }

        if (key is Key.Escape)
        {
            if (execute)
            {
                session.Cancel();
            }
            return true;
        }

        return false;
    }

    private static SqlVariableRenameSession? For(IWpfTextView view) =>
        _active is { _started: true, _disposed: false } session && ReferenceEquals(session._view, view)
            ? session
            : null;

    private void Start()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var span = _spans[_primaryIndex].GetSpan(_buffer.CurrentSnapshot);
        _view.Selection.Select(span, isReversed: false);
        _view.Caret.EnsureVisible();

        // 若選取被殼層收掉，再試一次。這在 SSMS 22 某些版本上實測有效。
        if (_view.Selection.IsEmpty)
        {
            _view.Selection.Select(span, isReversed: false);
        }

        _buffer.Changed += OnBufferChanged;
        _view.Caret.PositionChanged += OnCaretPositionChanged;
        _view.Closed += OnViewClosed;

        _active = this;
        _started = true;

        SqlAssistDiagnostics.WriteAlways(
            $"F2 重新命名開始：@{_originalName}，同一批次 {_spans.Length} 處，" +
            $"選取{(_view.Selection.IsEmpty ? "未生效" : "已生效")}");
    }

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e) =>
        SqlAssistPlatformGuard.Run("鏡射變數重新命名", () => MirrorEdit(e));

    private void MirrorEdit(TextContentChangedEventArgs e)
    {
        if (_applyingEdit || _disposed)
            return;

        var name = _spans[_primaryIndex].GetSpan(e.After).GetText();

        // 處理「選取未生效」的情況：使用者的第一個字插進了舊名裡。
        // 只在第一次變更上做補償，之後游標已經在新名稱裡，插在哪裡就是哪裡。
        if (!_hasSeenFirstChange)
        {
            _hasSeenFirstChange = true;
            if (TryExtractTypedText(e) is { } typed)
            {
                SqlAssistDiagnostics.WriteAlways(
                    $"F2 重新命名：選取未生效，以「{typed}」取代舊名");
                name = typed;
            }
        }

        // 中間狀態（空白、減號）不鏡射，下一個按鍵通常就修好了。
        if (!SqlVariableRename.IsValidName(name))
            return;

        ReplaceAllOccurrences(name, e.After);
    }

    /// <summary>
    /// 從第一次緩衝區變更中，判斷使用者打進來的字是什麼。
    /// </summary>
    /// <remarks>
    /// 正常情況下 F2 後名稱是被選取的，第一個字會取代它（有移除也有插入）。
    /// 如果選取被殼層收掉，游標落在名稱裡，第一個字就是純插入。
    ///
    /// 檢測兩種情形：
    /// 1. 單一變更、純插入（最常見的選取未生效情況）。
    /// 2. 變更後的文本在原始名稱基礎上多了一個字元（游標在中間插入）。
    /// </remarks>
    private string? TryExtractTypedText(TextContentChangedEventArgs e)
    {
        if (e.Changes.Count != 1)
            return null;

        var change = e.Changes[0];

        // 情形一：單一變更、純插入。
        if (change.OldLength == 0 && change.NewLength > 0)
            return change.NewText;

        // 情形二：替換了整個名稱（如貼上、全選後打字），直接回傳新文本。
        if (change.OldLength == _originalName.Length && change.NewLength > 0)
            return change.NewText;

        return null;
    }

    private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e) =>
        SqlAssistPlatformGuard.Run("檢查游標是否離開變數", () => CheckCaretLeave(e));

    private void CheckCaretLeave(CaretPositionChangedEventArgs e)
    {
        if (_applyingEdit || _disposed)
            return;

        var span = _spans[_primaryIndex].GetSpan(_buffer.CurrentSnapshot);
        var position = e.NewPosition.BufferPosition.Position;

        // 停在名稱裡是正常打字位置，不算離開。起點往左多算一格（變數自己的「@」），
        // 與 F2 的進入條件一致：停在「@」上不算離開。
        if (position < span.Start.Position - 1 || position > span.End.Position)
        {
            TryCommit(out _);
        }
    }

    private void OnViewClosed(object sender, EventArgs e) => Dispose();

    /// <summary>
    /// 結束重新命名；名稱不合法或撞名時整批還原。
    /// </summary>
    private bool TryCommit(out string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        message = string.Empty;

        if (_disposed)
            return true;

        try
        {
            var name = _spans[_primaryIndex].GetSpan(_buffer.CurrentSnapshot).GetText();
            var problem = SqlVariableRename.Validate(_target, name);

            if (problem != SqlVariableRenameProblem.None)
            {
                ReplaceAllOccurrences(_originalName, _buffer.CurrentSnapshot);
                message = DescribeProblem(problem, name);
                SqlAssistDiagnostics.WriteAlways($"F2 重新命名未套用：{message}");
                return false;
            }

            SqlAssistDiagnostics.WriteAlways($"F2 重新命名完成：@{_originalName} → @{name}");
            return true;
        }
        finally
        {
            Dispose();
        }
    }

    private void Cancel()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_disposed)
            return;

        try
        {
            ReplaceAllOccurrences(_originalName, _buffer.CurrentSnapshot);
            SqlAssistDiagnostics.WriteAlways("F2 重新命名已取消，名稱還原成原樣");
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>
    /// 把所有出現處（包含主出現處）的名稱換成 <paramref name="newName"/>。
    /// </summary>
    private void ReplaceAllOccurrences(string newName, ITextSnapshot snapshot)
    {
        _applyingEdit = true;
        try
        {
            using var edit = _buffer.CreateEdit();

            foreach (var tracking in _spans)
            {
                var span = tracking.GetSpan(snapshot);
                if (!string.Equals(span.GetText(), newName, StringComparison.Ordinal))
                {
                    edit.Replace(span, newName);
                }
            }

            if (edit.HasEffectiveChanges)
            {
                edit.Apply();
            }
            else
            {
                edit.Cancel();
            }
        }
        finally
        {
            _applyingEdit = false;
        }
    }

    private void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _buffer.Changed -= OnBufferChanged;
        _view.Caret.PositionChanged -= OnCaretPositionChanged;
        _view.Closed -= OnViewClosed;

        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }
    }

    private static string DescribeProblem(SqlVariableRenameProblem problem, string name) => problem switch
    {
        SqlVariableRenameProblem.Empty => "變數名稱不能是空的，已還原原名稱。",
        SqlVariableRenameProblem.InvalidName => $"「{name}」不是合法的變數名稱，已還原原名稱。",
        SqlVariableRenameProblem.Duplicate => $"已經有另一個變數叫「{name}」，已還原原名稱。",
        _ => "重新命名未套用，已還原原名稱。"
    };
}
