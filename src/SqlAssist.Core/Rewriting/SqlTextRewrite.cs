using System;
using System.Collections.Generic;
using System.Text;

namespace SqlAssist.Core.Rewriting;

/// <summary>把一整組編輯一次套用到原文上，並把游標換算到新文字的位置。</summary>
/// <remarks>
/// 「一次套用」是這個型別唯一的理由：多處補結構描述時，若各處自己對字串做
/// <c>Replace</c>，第二處之後的位置就全部偏掉了。位置一律以原文為準，
/// 由這裡一趟組出結果。
/// </remarks>
public static class SqlTextRewrite
{
    /// <summary>套用編輯；沒有編輯時原樣回傳。</summary>
    /// <param name="edits">以原文為座標的編輯，順序不拘，但不得互相重疊。</param>
    /// <param name="text">原文。</param>
    /// <param name="caretPosition">游標在原文裡的位置；負值代表不處理游標。</param>
    /// <returns>換完的文字、動了幾處，以及換算後的游標位置。</returns>
    /// <exception cref="InvalidOperationException">編輯互相重疊或超出原文的長度。</exception>
    /// <remarks>
    /// 重疊直接視為程式錯誤而不是安靜地挑一個套用：位置是算出來的，重疊只有兩種可能
    /// ——分析器把同一處報了兩次，或某個編輯用了錯的座標系。兩種都會讓補出來的 SQL
    /// 少一段，而畫面上看不出來。
    /// </remarks>
    public static SqlTextRewriteResult Apply(
        IReadOnlyList<SqlTextEdit> edits,
        string text,
        int caretPosition)
    {
        if (edits is null)
        {
            throw new ArgumentNullException(nameof(edits));
        }

        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (edits.Count == 0)
        {
            return new SqlTextRewriteResult(text, 0, caretPosition);
        }

        var ordered = new SqlTextEdit[edits.Count];

        for (var index = 0; index < edits.Count; index++)
        {
            ordered[index] = edits[index];
        }

        Array.Sort(ordered, (left, right) => left.Start.CompareTo(right.Start));

        // 每一處最多多兩個字元（dbo.），先配好長度免得 StringBuilder 邊長邊複製。
        var builder = new StringBuilder(text.Length + (ordered.Length * 6));
        var cursor = 0;
        var caret = caretPosition;

        foreach (var edit in ordered)
        {
            if (edit.Start < cursor || edit.End > text.Length)
            {
                throw new InvalidOperationException(
                    $"文字改寫的編輯重疊或超出原文：{edit.Start}+{edit.Length}。");
            }

            builder.Append(text, cursor, edit.Start - cursor);
            builder.Append(edit.Text);

            // 游標正好落在編輯起點時不動：插入的文字接在游標後面，游標仍然停在
            // 它前面——與編輯器自己插字時的行為一致。游標在編輯之後才跟著往後移。
            if (caretPosition >= 0 && edit.Start < caretPosition)
            {
                caret += edit.Text.Length - edit.Length;
            }

            cursor = edit.End;
        }

        builder.Append(text, cursor, text.Length - cursor);

        return new SqlTextRewriteResult(
            builder.ToString(),
            ordered.Length,
            caretPosition < 0 ? -1 : Math.Min(caret, builder.Length));
    }
}
