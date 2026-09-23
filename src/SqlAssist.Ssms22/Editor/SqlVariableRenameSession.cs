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
/// <b>為什麼要有工作階段。</b>重新命名不是一次性的取代，而是一段「使用者還在打字」
/// 的期間：那段期間裡每一處同名變數都要跟著游標那一處變，而 ↑／↓／Enter 要拿來
/// 結束它而不是移動游標或換行。這兩件事都需要一個活著的物件記住「哪幾處正在一起
/// 改」，所以狀態放在這裡，而不是散在命令處理常式裡。
///
/// <b>為什麼自己鏡射而不用編輯器的多重選取。</b>多重選取
/// （<c>IMultiSelectionBroker</c>）乍看正好合用——原生、復原也是一格——但本擴充沒有
/// 別的地方用過它，而這個功能在開發環境裡沒有 SSMS 可以實測，錯了會是「按下 F2
/// 之後整份指令碼被改壞」這種等級的災難。這裡只用本擴充已經在用、行為已知的幾樣
/// 東西：<c>ITrackingSpan</c>、一次 <c>CreateEdit</c>／<c>Apply</c> 的原子替換，
/// 以及 <c>Selection.Select</c>。
///
/// <b>復原的粒度。</b>使用者打一個字是一次編輯，這裡的鏡射是另一次，所以 Ctrl+Z
/// 會先還原其他出現處、再還原游標那一處，而不是一次還原整個重新命名。要合併成
/// 同一格得動用編輯器的復原管理員服務，現在沒有那樣做；這是已知的取捨，
/// 換來的是每一處的結果都對。
///
/// <b>離開就當成改完。</b>游標離開名稱（滑鼠點走、按 ←／→）時直接提交，與其他
/// 編輯器的就地重新命名一致。不這樣做的話，使用者點到別處之後這一份還在改，
/// 下一個字會落到他根本沒在看的地方。
/// </remarks>
internal sealed class SqlVariableRenameSession
{
    private static SqlVariableRenameSession? _active;

    /// <summary>
    /// 是否有工作階段進行中。
    /// </summary>
    /// <remarks>
    /// 殼層命令濾鏡在每一個按鍵上都要問一次，所以這裡刻意寫成一次靜態欄位讀取，
    /// 與 <see cref="SqlSnippetSurroundPicker.IsOpen"/> 同一個理由。
    /// </remarks>
    public static bool IsActive => _active is not null;

    private readonly IWpfTextView _view;
    private readonly ITextBuffer _buffer;
    private readonly IServiceProvider _serviceProvider;
    private readonly SqlVariableRenameTarget _target;
    private readonly ITrackingSpan[] _spans;

    /// <summary>游標那一處在 <see cref="_spans"/> 裡的索引；只有它跟著打字長大。</summary>
    private readonly int _primary;

    private bool _applying;
    private bool _closed;

    private SqlVariableRenameSession(
        IWpfTextView view,
        IServiceProvider serviceProvider,
        SqlVariableRenameTarget target)
    {
        _view = view;
        _buffer = view.TextBuffer;
        _serviceProvider = serviceProvider;
        _target = target;

        var snapshot = _buffer.CurrentSnapshot;
        var spans = new ITrackingSpan[target.NameStarts.Count];
        var primary = 0;

        for (var index = 0; index < spans.Length; index++)
        {
            var start = target.NameStarts[index];
            var span = new SnapshotSpan(snapshot, new Span(start, target.NameLength));

            if (start == target.NameStart)
            {
                primary = index;
            }

            // 游標那一處要跟著打字往後長（EdgePositive），其餘的由我們整段換掉，
            // 不需要跟著邊界跑。
            spans[index] = snapshot.CreateTrackingSpan(
                span,
                index == primary ? SpanTrackingMode.EdgePositive : SpanTrackingMode.EdgeExclusive);
        }

        _spans = spans;
        _primary = primary;
    }

    /// <summary>
    /// 在游標所在的區域變數上開始重新命名。
    /// </summary>
    /// <returns>開始得了就 true；否則 false，並在 <paramref name="message"/> 說明原因。</returns>
    public static bool Begin(IWpfTextView view, IServiceProvider serviceProvider, out string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        message = string.Empty;

        // 上一次沒收乾淨（換了編輯器、焦點被搶走）就先結算它：直接丟掉的話，
        // 那一次的撞名永遠不會被發現，指令碼裡會留著兩個同名變數。
        if (_active is { } previous)
        {
            previous.Commit(out var warning);

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

        new SqlVariableRenameSession(view, serviceProvider, target).Start();
        return true;
    }

    /// <summary>
    /// 把殼層命令交給進行中的工作階段。
    /// </summary>
    /// <returns>
    /// 認得這個命令就回 true（<paramref name="execute"/> 為 false 時代表「歸我管」，
    /// 殼層才會派送 <c>Exec</c>）；不是它的鍵就回 false，讓命令照常往下走——
    /// 打字、Backspace、←／→ 都必須維持原本的編輯行為。
    /// </returns>
    public static bool TryHandleShellCommand(IWpfTextView view, Guid group, uint commandId, bool execute)
    {
        var session = For(view);

        if (session is null)
        {
            return false;
        }

        var key = SqlSnippetSurroundKeys.MapKey(group, commandId);

        if (key is Key.Up or Key.Down or Key.Enter)
        {
            if (!execute)
            {
                return true;
            }

            session.Commit(out var message);

            if (message.Length > 0)
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
        _active is { _closed: false } session && ReferenceEquals(session._view, view) ? session : null;

    private void Start()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _active = this;

        // 訂閱放在選取之後：那一次選取會動到游標，先訂閱會立刻把自己結束掉。
        _buffer.Changed += OnBufferChanged;
        _view.Caret.PositionChanged += OnCaretChanged;
        _view.Closed += OnViewClosed;

        var span = _spans[_primary].GetSpan(_buffer.CurrentSnapshot);
        _view.Selection.Select(span, isReversed: false);
        _view.Caret.MoveTo(span.End);
        _view.Caret.EnsureVisible();
    }

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        if (_applying || _closed)
        {
            return;
        }

        var name = _spans[_primary].GetSpan(e.After).GetText();

        // 打到一半的名稱（空白、減號）先不動其他出現處。鏡射下去會把它們一起改成
        // 編不過的樣子，而下一個按鍵通常就修好了——那種中間狀態不該留下痕跡。
        if (!SqlVariableRename.IsValidName(name))
        {
            return;
        }

        Replace(name, e.After);
    }

    private void OnCaretChanged(object sender, CaretPositionChangedEventArgs e)
    {
        if (_applying || _closed)
        {
            return;
        }

        var span = _spans[_primary].GetSpan(_buffer.CurrentSnapshot);
        var position = e.NewPosition.BufferPosition.Position;

        // 停在名稱結尾是打字時的正常位置，不算離開。
        if (position < span.Start.Position || position > span.End.Position)
        {
            Commit(out _);
        }
    }

    private void OnViewClosed(object sender, EventArgs eventArgs) => End();

    /// <summary>
    /// 結束重新命名；名稱不合法或撞名時整批還原。
    /// </summary>
    private bool Commit(out string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        message = string.Empty;

        if (_closed)
        {
            return true;
        }

        var name = _spans[_primary].GetSpan(_buffer.CurrentSnapshot).GetText();
        var problem = SqlVariableRename.Validate(_target, name);

        if (problem != SqlVariableRenameProblem.None)
        {
            // 禁止修改：整批還原成原名稱，不留下改了一半的結果。
            Replace(_target.Name, _buffer.CurrentSnapshot);
            message = Describe(problem, name);
            End();
            return false;
        }

        End();
        return true;
    }

    private void Cancel()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_closed)
        {
            return;
        }

        Replace(_target.Name, _buffer.CurrentSnapshot);
        End();
    }

    /// <summary>
    /// 把每一處的名稱換成 <paramref name="name"/>。
    /// </summary>
    /// <remarks>
    /// 一次 <c>CreateEdit</c> 涵蓋所有出現處，所以它們在同一格復原裡。
    /// <see cref="_applying"/> 是重入防護：自己的編輯會再觸發一次
    /// <c>Changed</c>，不擋的話就是無窮遞迴。
    /// </remarks>
    private void Replace(string name, ITextSnapshot snapshot)
    {
        _applying = true;

        try
        {
            using var edit = _buffer.CreateEdit();

            foreach (var tracking in _spans)
            {
                var span = tracking.GetSpan(snapshot);

                if (!string.Equals(span.GetText(), name, StringComparison.Ordinal))
                {
                    edit.Replace(span, name);
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
            _applying = false;
        }
    }

    private void End()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _buffer.Changed -= OnBufferChanged;
        _view.Caret.PositionChanged -= OnCaretChanged;
        _view.Closed -= OnViewClosed;

        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }
    }

    private static string Describe(SqlVariableRenameProblem problem, string name) => problem switch
    {
        SqlVariableRenameProblem.Empty => "變數名稱不能是空的，已還原原名稱。",
        SqlVariableRenameProblem.InvalidName => $"「{name}」不是合法的變數名稱，已還原原名稱。",
        SqlVariableRenameProblem.Duplicate => $"已經有另一個變數叫「{name}」，已還原原名稱。",
        _ => "重新命名未套用，已還原原名稱。"
    };
}
