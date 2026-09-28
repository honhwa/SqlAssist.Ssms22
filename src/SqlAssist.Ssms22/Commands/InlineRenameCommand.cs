using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using SqlAssist.Core.Diagnostics;
using SqlAssist.Core.Rewriting;
using SqlAssist.Ssms22.Editor;
using SqlAssist.Ssms22.Settings;

namespace SqlAssist.Ssms22.Commands;

/// <summary>
/// 查詢視窗的「就地改名」：把游標所在的 <c>@變數</c> 連同同一批次裡的其他出現位置一起改掉。
/// </summary>
/// <remarks>
/// 用法是按下命令之後直接打字，不必先選取：
///
/// <list type="bullet">
/// <item>游標處的名稱會被選起來，其他出現位置加上底色。</item>
/// <item>每一次按鍵都立刻同步到其他出現位置，<b><c>Enter</c> 才定案</b>。</item>
/// <item><c>Enter</c> 或焦點離開＝提交，<c>Esc</c>＝整批還原。</item>
/// </list>
///
/// 判斷「哪幾處算同一個變數」全部在 <see cref="SqlVariableRename"/>：那一段只看文字、
/// 可以完整單元測試。這裡只做編輯器才做得到的事——取焦點、選取、畫底色、
/// 把按鍵寫回緩衝區。
/// </remarks>
internal static class InlineRenameCommand
{
    /// <summary>其他出現位置的底色；這一層只有這個功能在用，不與區塊高亮或搜尋命中共用。</summary>
    private const string HighlightLayerName = "SqlAssistInlineRename";

    /// <summary>同時只允許一個改名中的變數；再按一次等於放棄上一輪。</summary>
    private static InlineRenameSession? _session;

    /// <summary>查詢視窗右鍵選單那一顆的狀態。</summary>
    /// <remarks>
    /// 游標不在變數上時回報停用，右鍵選單上那一項就是灰的；先看了才按的人
    /// 因此知道現在用不上，而不是按下去沒有反應。
    /// </remarks>
    public static bool IsAvailable() =>
        SqlAssistSettingsStore.Current.Enabled
        && ActiveSqlEditor.Current is { IsClosed: false } view
        && IsOnVariable(view);

    /// <summary>命令表 F2 那一顆的狀態。</summary>
    /// <remarks>
    /// 比右鍵選單多要求<b>鍵盤真的在編輯器上</b>。命令表的鍵繫結只能用全域範圍
    /// （理由見 <see cref="CommandIds.GoToDefinition"/>），而 F2 在 SSMS 是「重新命名
    /// 物件總管的節點」——少了這一個條件，使用者在物件總管按 F2 會被 SqlAssist
    /// 搶走，而且改的是最後一次拿過焦點的那個查詢視窗裡的變數：畫面上完全看不出關聯，
    /// 等發現時那一份 SQL 已經被改過了。停用的命令殼層不會派送，鍵就照常落回原本的用途。
    /// </remarks>
    public static bool IsKeyBindingAvailable() =>
        IsAvailable() && ActiveSqlEditor.Current is { HasAggregateFocus: true };

    /// <summary>開始改名；失敗時把原因交給呼叫端顯示在狀態列。</summary>
    /// <param name="view">目標編輯器。</param>
    /// <param name="package">要顯示訊息的服務提供者。</param>
    /// <param name="message">沒有開始時要讓使用者看到的原因；開始了就是空字串。</param>
    /// <returns>真的進入改名模式時為 <c>true</c>。</returns>
    /// <remarks>
    /// 失敗一律回訊息而不是安靜地不做：這條路徑使用者是從選單或按鍵明確啟動的，
    /// 什麼都沒發生等於故障。
    /// </remarks>
    public static bool TryBegin(IWpfTextView view, IServiceProvider package, out string message)
    {
        var snapshot = view.TextBuffer.CurrentSnapshot;

        if (!SqlVariableRename.TryLocate(
                snapshot.GetText(),
                view.Caret.Position.BufferPosition.Position,
                out var target,
                out message)
            || target is null)
        {
            return false;
        }

        // 上一輪還沒結束就先收掉：兩個 session 同時掛在緩衝區上時，後進來的那一個
        // 會把前一個的底色與鍵盤攔截一起留著，症狀是打字時出現兩份不同步的改名。
        _session?.Dispose();

        var session = new InlineRenameSession(view, package, target);
        session.Begin();
        _session = session;

        message = string.Empty;
        return true;
    }

    /// <remarks>
    /// 一律以<b>緩衝區目前那一份快照</b>算：檢視的快照可能落後一個編輯，而游標位置
    /// 與文字要取自同一份，否則比對到的是上一次按鍵之前的內容。
    /// </remarks>
    private static bool IsOnVariable(IWpfTextView view) =>
        SqlVariableRename.TryLocate(
            view.TextBuffer.CurrentSnapshot.GetText(),
            view.Caret.Position.BufferPosition.Position,
            out _,
            out _);

    /// <summary>一次改名的完整生命週期：選取、打字同步、提交或還原。</summary>
    private sealed class InlineRenameSession : IDisposable
    {
        private readonly IWpfTextView _view;
        private readonly IServiceProvider _package;
        private readonly SqlVariableRenameTarget _target;
        private readonly ITextBuffer _buffer;
        private readonly ITrackingSpan[] _spans;
        private readonly int _cursorIndex;
        private IAdornmentLayer? _layer;
        private bool _syncing;
        private bool _focused;
        private bool _disposed;

        public InlineRenameSession(
            IWpfTextView view,
            IServiceProvider package,
            SqlVariableRenameTarget target)
        {
            _view = view;
            _package = package;
            _target = target;
            _buffer = view.TextBuffer;
            _cursorIndex = target.CursorIndex;

            var snapshot = _buffer.CurrentSnapshot;
            _spans = new ITrackingSpan[target.Occurrences.Count];

            for (var index = 0; index < _spans.Length; index++)
            {
                // EdgeInclusive：使用者在名稱尾端接著打字時範圍要跟著長大，
                // 否則同步出去的永遠是上一次按鍵為止的那個名稱。
                _spans[index] = snapshot.CreateTrackingSpan(
                    new Span(target.Occurrences[index], target.Name.Length),
                    SpanTrackingMode.EdgeInclusive);
            }
        }

        public void Begin()
        {
            if (_disposed)
            {
                return;
            }

            // 只選 @ 之後那一段：@ 是語法的一部分，連它一起選起來的話，
            // 使用者打的第一個字會把 @ 吃掉，整批名稱當場變成不合法的識別字。
            _view.Selection.Select(
                new SnapshotSpan(
                    _buffer.CurrentSnapshot,
                    _target.CursorOffset + 1,
                    _target.Name.Length - 1),
                isReversed: false);

            _focused = _view.HasAggregateFocus;
            _buffer.Changed += OnBufferChanged;
            _view.LayoutChanged += OnLayoutChanged;
            _view.GotAggregateFocus += OnGotAggregateFocus;
            _view.LostAggregateFocus += OnLostAggregateFocus;
            _view.VisualElement.PreviewKeyDown += OnPreviewKeyDown;

            Highlight();
        }

        // ── 打字同步 ────────────────────────────────────────────────────

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e) =>
            SqlAssistPlatformGuard.Run("同步就地改名的其他出現位置", Sync);

        private void Sync()
        {
            if (_disposed || _syncing)
            {
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            _syncing = true;

            try
            {
                var snapshot = _buffer.CurrentSnapshot;
                var name = _spans[_cursorIndex].GetSpan(snapshot).GetText();

                // 名稱被刪到空掉或連 @ 都不見時先不要同步：照著同步下去會把其他出現
                // 位置一起刪掉或去頭，而使用者還在打，下一步說不定就是把 @ 補回來。
                // 這一段的還原由 Esc 負責，不必急著把中間狀態擴散出去。
                if (name.Length == 0 || name[0] != '@')
                {
                    return;
                }

                using (var edit = _buffer.CreateEdit())
                {
                    for (var index = 0; index < _spans.Length; index++)
                    {
                        if (index == _cursorIndex)
                        {
                            continue;
                        }

                        var span = _spans[index].GetSpan(snapshot);

                        if (!string.Equals(span.GetText(), name, StringComparison.Ordinal))
                        {
                            edit.Replace(span.Start, span.Length, name);
                        }
                    }

                    if (edit.HasEffectiveChanges)
                    {
                        edit.Apply();
                    }
                }

                // 其他出現位置的長度變了，底色要照新的範圍重畫。
                Highlight();
            }
            finally
            {
                _syncing = false;
            }
        }

        // ── 底色 ────────────────────────────────────────────────────────

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e) =>
            SqlAssistPlatformGuard.Run("重畫就地改名的底色", Highlight);

        /// <remarks>
        /// 每一層都重畫而不是只補新的位置：位置是螢幕座標，捲動與換行之後全部作廢，
        /// 只補新的那一版會在捲動後留下一整排錯位的方塊，比不畫更難看懂。
        /// </remarks>
        private void Highlight()
        {
            if (_disposed || _view.IsClosed)
            {
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();

            _layer ??= _view.GetAdornmentLayer(HighlightLayerName);
            _layer.RemoveAllAdornments();

            var lines = _view.TextViewLines;

            if (lines is null)
            {
                return;
            }

            var snapshot = _buffer.CurrentSnapshot;

            for (var index = 0; index < _spans.Length; index++)
            {
                // 游標那一處已經被選取起來了；再塗一層只會把它蓋掉。
                if (index == _cursorIndex)
                {
                    continue;
                }

                var span = _spans[index].GetSpan(snapshot);

                if (span.IsEmpty)
                {
                    continue;
                }

                // 捲出畫面的那幾處沒有行可以問，直接跳過；捲回來時 LayoutChanged
                // 會再叫一次這裡。
                var line = lines.GetTextViewLineContainingBufferPosition(span.Start);

                if (line is null)
                {
                    continue;
                }

                var start = line.GetCharacterBounds(span.Start);
                var end = line.GetCharacterBounds(span.End);

                var box = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(48, 255, 140, 0)),
                    Width = Math.Max(end.Left - start.Left, 2),
                    Height = start.TextHeight
                };

                Canvas.SetLeft(box, start.Left);
                Canvas.SetTop(box, start.Top);

                _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, box, null);
            }
        }

        // ── 提交與還原 ──────────────────────────────────────────────────

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SqlAssistPlatformGuard.Run("取消就地改名", Cancel);
                return;
            }

            if (e.Key == Key.Enter)
            {
                // Enter 吃掉而不讓它換行：這個模式下它是「這樣可以」，
                // 而換行只會在中間產生一段半毀的名稱。
                e.Handled = true;
                SqlAssistPlatformGuard.Run("提交就地改名", Commit);
            }
        }

        private void OnGotAggregateFocus(object sender, EventArgs e) => _focused = true;

        /// <remarks>
        /// 選單關閉、焦點回到編輯器之前也會走一次這裡。那一次不是「使用者離開了」，
        /// 拿它當提交會讓剛開始的改名當場結束——而使用者只會覺得「按了之後打字沒反應」。
        /// 只有真的先拿到過焦點才認。
        /// </remarks>
        private void OnLostAggregateFocus(object sender, EventArgs e)
        {
            if (!_focused)
            {
                return;
            }

            _focused = false;
            SqlAssistPlatformGuard.Run("離開編輯器時提交就地改名", Commit);
        }

        private void Commit()
        {
            if (_disposed)
            {
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();

            var snapshot = _buffer.CurrentSnapshot;
            var cursor = _spans[_cursorIndex].GetSpan(snapshot);
            var name = cursor.GetText();

            if (string.Equals(name, _target.Name, StringComparison.Ordinal))
            {
                Finish();
                return;
            }

            // 名稱不合法或撞名時<b>不還原、也不彈對話框</b>：改名模式還留著，鍵盤還在
            // 編輯器上，使用者只要再修一個字。還原成原狀再跳一個要按確定的視窗，
            // 等於把他剛才打的整段丟掉，而且那個視窗出現在按鍵路徑上。
            if (!SqlVariableRename.IsValidName(name))
            {
                Report($"{name} 不是合法的變數名稱；名稱要用 @ 開頭，後面接字母、數字或底線。");
                return;
            }

            if (SqlVariableRename.IsNameTaken(
                    snapshot.GetText(),
                    cursor.Start.Position,
                    name,
                    CurrentOffsets(snapshot)))
            {
                Report($"{name} 在這個批次裡已經有別的變數在用；先改掉那一個再回來。");
                return;
            }

            SqlAssistDiagnostics.WriteAlways(
                $"就地改名完成：{_target.Name} → {name}，共 {_spans.Length} 處");
            Finish();
        }

        private void Cancel()
        {
            if (_disposed)
            {
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            Finish(restore: true);
        }

        private List<int> CurrentOffsets(ITextSnapshot snapshot)
        {
            var offsets = new List<int>(_spans.Length);

            foreach (var span in _spans)
            {
                offsets.Add(span.GetSpan(snapshot).Start.Position);
            }

            return offsets;
        }

        /// <param name="restore">要不要把出現位置還原成原本的名稱。</param>
        private void Finish(bool restore = false)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Unsubscribe();

            if (restore && !_view.IsClosed)
            {
                Restore();
            }

            _layer?.RemoveAllAdornments();
            _layer = null;
        }

        /// <remarks>
        /// 只還原<b>我們改過的那幾處</b>，不是把整份文件換回一個先前的字串。整份還原會把
        /// 這段期間在別的地方做的編輯一起蓋掉，而且整份取代會讓摺疊、書籤與選取全部重算
        /// ——症狀是按下 Esc 之後游標與捲軸一起跳掉。
        /// </remarks>
        private void Restore()
        {
            var snapshot = _buffer.CurrentSnapshot;

            using (var edit = _buffer.CreateEdit())
            {
                foreach (var span in _spans)
                {
                    var current = span.GetSpan(snapshot);

                    if (!string.Equals(current.GetText(), _target.Name, StringComparison.Ordinal))
                    {
                        edit.Replace(current.Start, current.Length, _target.Name);
                    }
                }

                if (edit.HasEffectiveChanges)
                {
                    edit.Apply();
                }
            }
        }

        private void Unsubscribe()
        {
            _buffer.Changed -= OnBufferChanged;
            _view.LayoutChanged -= OnLayoutChanged;
            _view.GotAggregateFocus -= OnGotAggregateFocus;
            _view.LostAggregateFocus -= OnLostAggregateFocus;

            if (!_view.IsClosed)
            {
                _view.VisualElement.PreviewKeyDown -= OnPreviewKeyDown;
            }
        }

        private void Report(string message) => SqlAssistStatusBar.Show(_package, message);

        /// <remarks>
        /// 沒有提交就結束時整批還原：改到一半的名稱留在緩衝區裡比什麼都沒做更糟，
        /// 而使用者按 Esc 的意思正是「當作沒發生過」。
        /// </remarks>
        public void Dispose() => SqlAssistPlatformGuard.Run("結束就地改名", () =>
        {
            if (_disposed)
            {
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            Finish(restore: true);
        });
    }
}
