using System.ComponentModel;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlAssist.Core.Diagnostics;
using SqlAssist.Core.Localization;
using SqlAssist.Core.Rewriting;
using SqlAssist.Ssms22.Settings;

namespace SqlAssist.Ssms22.Editor;

/// <summary>查詢視窗右鍵的兩種就地文字改寫。</summary>
internal enum SqlTextRewriteKind
{
    /// <summary>把沒帶結構描述的物件名稱補成 <c>dbo.</c>。</summary>
    SchemaQualification,

    /// <summary>把 <c>TOP 10</c> 補成 <c>TOP (10)</c>。</summary>
    TopParenthesis
}

/// <summary>
/// 查詢視窗右鍵的「補齊結構描述」與「TOP 補上括號」。
/// </summary>
/// <remarks>
/// 兩個命令只差「叫哪一支分析器」，所以共用同一條路：取範圍（有選取用選取，
/// 沒有就是整份文件）、交給 Core 換好整段文字、寫回編輯器。各寫一份的下場是
/// 其中一份忘了處理唯讀或忘了框選的拒絕，而那不會有任何徵兆。
///
/// 判斷與改寫全部在 <c>SqlAssist.Core.Rewriting</c>，那一段只看文字、可以完整
/// 單元測試；這裡只做編輯器才做得到的事。
///
/// <b>刻意不做成存檔或輸入時自動整理。</b>這兩條路都會改到使用者沒指名的地方，
/// 而猜錯時畫面上只看得出「SQL 怪的」，看不出是擴充動的手腳。做成右鍵命令之後，
/// 範圍由使用者決定，改完的結果就在眼前。
/// </remarks>
internal static class SqlTextRewriteAction
{
    /// <summary>命令狀態：SqlAssist 開著而且有 SQL 編輯器。</summary>
    public static bool IsAvailable() =>
        SqlAssistSettingsStore.Current.Enabled && ActiveSqlEditor.Current is not null;

    /// <summary>改寫游標所在的選取範圍；沒有選取時改整份文件。</summary>
    /// <param name="view">目標編輯器。</param>
    /// <param name="kind">要做哪一種改寫。</param>
    /// <param name="message">結果說明；使用者按了右鍵就一定看得到一句。</param>
    /// <returns>真的改了東西為 true。</returns>
    /// <remarks>
    /// 成功也回一句：這一種命令動的是散在整份文件裡的幾十處，不講一聲的話
    /// 使用者得自己捲一遍才知道到底有沒有生效。
    /// </remarks>
    public static bool TryRun(IWpfTextView view, SqlTextRewriteKind kind, out string message)
    {
        if (view is null || view.IsClosed)
        {
            message = CommonText.QueryWindowClosed;
            return false;
        }

        if (view.Caret.InVirtualSpace)
        {
            message = EditorText.CaretInVirtualSpace;
            return false;
        }

        if (!view.Selection.IsEmpty && view.Selection.Mode == TextSelectionMode.Box)
        {
            // 框選是好幾段不連續的範圍，整段換掉會一起蓋掉每一段。
            message = EditorText.BoxSelectionCannotRewrite;
            return false;
        }

        var buffer = view.TextBuffer;

        // 範圍以緩衝區目前這一份快照算：檢視的快照可能落後一個編輯，
        // 而等一下要改的是緩衝區。
        var snapshot = buffer.CurrentSnapshot;
        var target = view.Selection.StreamSelectionSpan.SnapshotSpan
            .TranslateTo(snapshot, SpanTrackingMode.EdgeInclusive);

        if (buffer.IsReadOnly(target.Span))
        {
            message = EditorText.SelectionReadOnlyNoChange;
            return false;
        }

        var caret = view.Caret.Position.BufferPosition.TranslateTo(snapshot, PointTrackingMode.Positive);
        var caretInSpan = caret.Position >= target.Start.Position && caret.Position <= target.End.Position
            ? caret.Position - target.Start.Position
            : -1;

        var affected = -1;
        var tracking = snapshot.CreateTrackingSpan(target.Span, SpanTrackingMode.EdgeExclusive);

        new TextViewEditCoordinator(view, buffer).ReplaceTracked(
            tracking,
            OperationName(kind),
            current =>
            {
                var result = Rewrite(kind, current.GetText(), caretInSpan);

                if (!result.HasChanges)
                {
                    return null;
                }

                affected = result.AffectedCount;

                // 游標停在換算後的位置，而不是預設的結尾：這一種改寫動的是
                // 游標附近那幾處，跳到文件結尾等於把使用者丟到別的地方。
                return new TextReplacement(
                    result.Text,
                    ActivityKind(kind),
                    ResultMessage(kind, result.AffectedCount),
                    caretOffset: result.CaretPosition);
            });

        if (affected < 0)
        {
            message = EmptyMessage(kind);
            return false;
        }

        message = ResultMessage(kind, affected);
        return true;
    }

    private static SqlTextRewriteResult Rewrite(SqlTextRewriteKind kind, string text, int caretPosition) =>
        kind == SqlTextRewriteKind.SchemaQualification
            ? SqlSchemaQualification.Qualify(text, caretPosition)
            : SqlTopClauseParenthesis.Parenthesize(text, caretPosition);

    // 操作名稱只進平台防護的診斷紀錄（ReplaceTracked 的 operationName 也標了同一個屬性），
    // 給維護者比對，不隨介面語言切換，所以固定繁中不進 resjson。
    [Localizable(false)]
    private static string OperationName(SqlTextRewriteKind kind) =>
        kind == SqlTextRewriteKind.SchemaQualification ? "補齊結構描述" : "TOP 括號";

    private static SqlAssistActivityKind ActivityKind(SqlTextRewriteKind kind) =>
        kind == SqlTextRewriteKind.SchemaQualification
            ? SqlAssistActivityKind.SchemaQualified
            : SqlAssistActivityKind.TopParenthesized;

    private static string ResultMessage(SqlTextRewriteKind kind, int affected) =>
        kind == SqlTextRewriteKind.SchemaQualification
            ? EditorText.SchemaQualified(affected)
            : EditorText.TopParenthesized(affected);

    private static string EmptyMessage(SqlTextRewriteKind kind) =>
        kind == SqlTextRewriteKind.SchemaQualification
            ? EditorText.NoSchemaQualificationNeeded
            : EditorText.NoTopParenthesisNeeded;
}
