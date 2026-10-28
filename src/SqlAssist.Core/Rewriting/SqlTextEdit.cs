using System;

namespace SqlAssist.Core.Rewriting;

/// <summary>把原文的某一段換成另一段文字。</summary>
/// <remarks>
/// 位置全部是<b>原文</b>的字元座標，不是算完之後的座標。多個編輯一起套用時，
/// 前一個編輯改了長度不會影響後一個的位置；改成邊算邊改字串的話，第二個編輯之後
/// 全部落在錯的位置，而症狀看起來只像是「有幾處沒補到」。
///
/// <see cref="Length"/> 為零代表純插入。刪除用不到，因此沒有留那個方向的建構式。
/// </remarks>
public readonly struct SqlTextEdit
{
    public SqlTextEdit(int start, int length, string text)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Start = start;
        Length = length;
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>要換掉的那一段在原文裡的起點。</summary>
    public int Start { get; }

    /// <summary>要換掉的字元數；零代表插入。</summary>
    public int Length { get; }

    /// <summary>換成什麼。</summary>
    public string Text { get; }

    /// <summary>要換掉的那一段在原文裡的終點（不含）。</summary>
    public int End => Start + Length;
}
