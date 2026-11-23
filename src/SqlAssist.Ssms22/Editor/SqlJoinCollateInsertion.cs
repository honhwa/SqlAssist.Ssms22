using System;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Parsing;
using SqlAssist.Core.Settings;
using SqlAssist.Ssms22;
using SqlAssist.Ssms22.Completion;
using SqlAssist.Ssms22.Settings;
using SqlAssist.Ssms22.Snippets;

namespace SqlAssist.Ssms22.Editor;

/// <summary>
/// 手打的聯結條件兩側定序不同時，在游標處補上 <c>COLLATE</c>。
/// </summary>
/// <remarks>
/// 形狀判斷全部在 <see cref="SqlJoinCollateAnalyzer"/>（只看文字，測得到）；
/// 這裡做編輯器那一半，外加一件只有這裡問得到的事：<b>兩欄的定序</b>。
///
/// 定序來自中繼資料，而這一條在按鍵路徑上，所以只讀快取
/// （<see cref="ISqlCompletionMetadata.PeekColumns"/>），不為此查一次資料庫：
/// 停在這一格之前，清單已經把兩個來源的欄位載進來過，命中是常態；
/// 沒命中就這一輪不補，下一次打字還會再問一次。
///
/// 補的時機是「打完右邊那一欄、接著打分隔字元」，不是「打完最後一個字元」：
/// <c>b.CustomerId</c> 打完的那一刻還可能是 <c>b.CustomerIdOld</c> 的前半段，
/// 那時插進去的字會擋在中間。分隔字元才是「這一條寫完了」的證據。
///
/// 在字元真的被插入<b>之前</b>補：游標停在右邊那一欄的尾端，插入的字就落在
/// 使用者要的位置，接著編輯器照常插入那一個分隔字元，游標自然停在最後。
/// </remarks>
internal static class SqlJoinCollateInsertion
{
    /// <summary>
    /// 使用者即將輸入 <paramref name="separator"/>（Enter 是
    /// <see cref="SqlKeywordCasing.NewLine"/>），先看前面那一條條件要不要補定序。
    /// </summary>
    /// <remarks>
    /// Enter 多一道讓開：那一刻建議清單多半開著，而 Enter 是提交鍵。先收掉清單
    /// 等於把使用者正要選的那一項取消掉，所以清單開著時整件事不做——提交完
    /// 再打一個空白，同一條規則照樣成立。
    /// </remarks>
    public static void ApplyBeforeSeparator(
        ITextView textView,
        ITextBuffer buffer,
        char separator,
        IAsyncCompletionBroker? broker,
        IServiceProvider serviceProvider)
    {
        if (!SqlJoinCollateAnalyzer.IsPredicateSeparator(separator) || !CanApply(textView, buffer))
        {
            return;
        }

        if (separator == SqlKeywordCasing.NewLine && broker?.GetSession(textView) is not null)
        {
            return;
        }

        var caret = textView.Caret.Position.BufferPosition;
        var snapshot = caret.Snapshot;

        if (SqlJoinCollateAnalyzer.TryFindBefore(new SnapshotTextSource(snapshot), caret.Position, separator)
            is not { } comparison)
        {
            return;
        }

        var settings = SqlAssistSettingsStore.Current;
        var context = SqlCompletionContextAnalyzer.Analyze(snapshot.GetText(0, snapshot.Length), caret.Position);
        var metadata = SqlCompletionServices.GetMetadataService(textView, serviceProvider).Completion;

        var left = CollationOf(context, metadata, comparison.LeftQualifier, comparison.LeftName, settings);
        var right = CollationOf(context, metadata, comparison.RightQualifier, comparison.RightName, settings);

        if (SqlJoinKey.CollationToApply(right, left) is not { } collation)
        {
            return;
        }

        // 要動緩衝區之前先收掉清單：插入的字會落進 session 的適用範圍。
        // 清單收掉之後平台自己會照新的上下文再開一份（SqlCompletionReopen）。
        broker?.GetSession(textView)?.Dismiss();

        using var edit = buffer.CreateEdit();
        edit.Insert(caret.Position, SqlJoinKey.CollateClause(collation));

        if (edit.Apply() is null || edit.Canceled)
        {
            return;
        }

        SqlAssistDiagnostics.Write($"自動補上 COLLATE {collation}", textView);
    }

    /// <summary>
    /// 這個來源的這個欄位有沒有定序；沒有查到時為 null。
    /// </summary>
    /// <remarks>
    /// 限定字比對不分大小寫，與配對鍵認同名欄位同一個標準。
    /// 只讀快取的理由見 <see cref="ApplyBeforeSeparator"/>。
    /// </remarks>
    private static string? CollationOf(
        SqlCompletionContext context,
        ISqlCompletionMetadata metadata,
        string qualifier,
        string name,
        SqlAssistSettings settings)
    {
        foreach (var source in context.ScopeSources)
        {
            if (source.Kind != SqlColumnSourceKind.Table ||
                source.Table is null ||
                !string.Equals(source.Qualifier, qualifier, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var column in metadata.PeekColumns(source.Table, source.Qualifier, settings))
            {
                if (string.Equals(column.DisplayText, name, StringComparison.OrdinalIgnoreCase))
                {
                    return column.CollationName;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 這個編輯器與這一次按鍵能不能動緩衝區。
    /// </summary>
    /// <remarks>
    /// 欄位 session 開著時讓開，理由與自動配對相同：那是 Snippet Engine 之外的
    /// 緩衝區編輯，會打斷欄位標記與同名欄位同步。虛擬空白也讓開——那時游標的
    /// 緩衝區位置停在行尾，寫進去的字會出現在看得到的位置<b>之前</b>。
    /// </remarks>
    private static bool CanApply(ITextView textView, ITextBuffer buffer)
    {
        if (textView is null || buffer is null || textView.IsClosed || textView.Caret.InVirtualSpace)
        {
            return false;
        }

        var settings = SqlAssistSettingsStore.Current;

        return settings.Enabled &&
            textView.Selection.IsEmpty &&
            ReferenceEquals(textView.Caret.Position.BufferPosition.Snapshot.TextBuffer, buffer) &&
            SqlSnippetExpansionController.Peek(textView)?.HasActiveSession != true;
    }
}
