using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlAssist.Core.Diagnostics;
using SqlAssist.Core.Snippets;
using SqlAssist.Metadata.ResultGrid;
using SqlAssist.Ssms22.Settings;
using SqlAssist.Ssms22.UI;

namespace SqlAssist.Ssms22.Editor;

/// <summary>
/// 查詢視窗右鍵的「貼上為 IN 條件」與「貼上為值清單」。
/// </summary>
/// <remarks>
/// 兩個命令只差頭尾那兩個符號（<see cref="SqlPasteShape"/>），所以共用同一條路：
/// 讀剪貼簿、拆成一欄值、排版、寫回編輯器。各寫一份的下場是其中一份忘了取代
/// 選取範圍或忘了縮排，而那不會有任何徵兆。
///
/// 判斷與排版全部在 <see cref="SqlPastedValueList"/>，那一段只看文字、
/// 可以完整單元測試；這裡只做編輯器才做得到的事。
///
/// <b>刻意不做成攔截 Ctrl+V 自動轉換。</b>貼上是使用者最常按的鍵之一，而
/// 「這一段文字是不是一欄值」猜錯時是把正常的貼上弄壞——畫面上只看得出
/// 「貼上的內容不對」，看不出是擴充動的手腳。改成由使用者在右鍵選單明示，
/// 猜錯的責任就回到看得到選單文字的人身上。
/// </remarks>
internal static class SqlPasteValuesAction
{
    /// <summary>游標那一行沒有縮排時，一層縮排用四個空白。</summary>
    /// <remarks>與自動配對、區塊骨架同一組：都是「看不出檔案用什麼，就先用四個空白」。</remarks>
    private const string DefaultIndentUnit = "    ";

    /// <summary>命令狀態：SqlAssist 開著而且有 SQL 編輯器。</summary>
    /// <remarks>
    /// <b>刻意不在這裡問剪貼簿。</b><c>QueryStatus</c> 在每一次開右鍵選單時都會走過，
    /// 而開剪貼簿是跨程序的呼叫——Excel 之類的來源帶著一大段選取範圍時要幾百毫秒，
    /// 每一次右鍵都付一次那個代價，只為了把一個項目變成灰的，不划算。
    /// 剪貼簿裡真的沒有東西時，按下去會在狀態列說明白。
    /// </remarks>
    public static bool IsAvailable() =>
        SqlAssistSettingsStore.Current.Enabled && ActiveSqlEditor.Current is not null;

    /// <summary>讀剪貼簿、拆成一欄值，寫進游標處（有選取就取代它）。</summary>
    /// <param name="view">目標編輯器。</param>
    /// <param name="shape">含不含 <c>IN</c> 與括號。</param>
    /// <param name="message">沒有貼上時要讓使用者看到的原因；成功時是空字串。</param>
    /// <returns>真的寫進去了為 true。</returns>
    /// <remarks>
    /// 失敗一律回訊息而不是自己吞掉：這條路徑是使用者從選單按的（不是按鍵），
    /// 什麼都沒發生等於故障。訊息由呼叫端顯示在狀態列。
    /// </remarks>
    public static bool TryRun(IWpfTextView view, SqlPasteShape shape, out string message)
    {
        if (view is null || view.IsClosed)
        {
            message = "查詢視窗已關閉。";
            return false;
        }

        if (view.Caret.InVirtualSpace)
        {
            message = "游標在虛擬空白上，請把游標移到文字之間再試一次。";
            return false;
        }

        if (!view.Selection.IsEmpty && view.Selection.Mode == TextSelectionMode.Box)
        {
            // 框選是好幾段不連續的範圍，取代掉會一起蓋掉每一段。
            message = "框選範圍無法取代，請改用一般選取。";
            return false;
        }

        var buffer = view.TextBuffer;

        // 讀不到就什麼都不做。訊息由 SqlClipboard 給，它接的是剪貼簿被別的程式
        // 佔住那一種預期失敗。
        if (SqlClipboard.TryReadText(out message) is not { } text)
        {
            return false;
        }

        if (!SqlPastedValueList.TryRead(text, out var literals, out message))
        {
            return false;
        }

        // 選取範圍一律以緩衝區目前這一份快照算：檢視的快照可能落後一個編輯，
        // 而等一下要改的是緩衝區。
        var snapshot = buffer.CurrentSnapshot;
        var target = view.Selection.StreamSelectionSpan.SnapshotSpan
            .TranslateTo(snapshot, SpanTrackingMode.EdgeInclusive);

        if (buffer.IsReadOnly(target.Span))
        {
            message = "插入位置為唯讀，未貼上。";
            return false;
        }

        var tracking = snapshot.CreateTrackingSpan(target.Span, SpanTrackingMode.EdgeExclusive);

        new TextViewEditCoordinator(view, buffer).ReplaceTracked(
            tracking,
            "貼上值清單",
            current => BuildReplacement(current, shape, literals));

        message = string.Empty;
        return true;
    }

    /// <remarks>
    /// 縮排與換行在這裡重算而不是從呼叫端傳進來：追蹤範圍在編輯真正套用之前，
    /// 游標那一行可能已經被別的編輯推走，而縮排要看的是最後定案的那一份快照。
    /// 換行則依檔案現況（<see cref="SnapshotNewLine"/>），不依設定。
    /// </remarks>
    private static TextReplacement? BuildReplacement(
        SnapshotSpan target,
        SqlPasteShape shape,
        IReadOnlyList<string> literals)
    {
        var snapshot = target.Snapshot;
        var line = snapshot.GetLineFromPosition(target.Start.Position);
        var indent = SqlSnippetIndentation.LeadingWhitespace(
            line.GetText(),
            target.Start.Position - line.Start.Position);

        var text = SqlPastedValueList.Build(
            literals,
            shape,
            indent,
            indent.Length > 0 ? indent : DefaultIndentUnit,
            SnapshotNewLine.Resolve(snapshot, target.Start.Position));

        // 游標留在結尾（預設的 caretOffset）：貼完接著要打的是 AND 條件或那個
        // 右括號，兩者都在尾巴。
        return new TextReplacement(
            text,
            SqlAssistActivityKind.PastedValues,
            $"已貼上 {literals.Count} 筆值",
            affectedItemCount: literals.Count);
    }
}
