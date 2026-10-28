namespace SqlAssist.Core.Rewriting;

/// <summary>一次文字改寫的結果：換完的文字、動了幾處，以及游標該落在哪裡。</summary>
/// <remarks>
/// 游標位置由這裡算而不是留給呼叫端：呼叫端手上是「換之前」的座標，而每一處改動
/// 都會讓它後面的字元位移。各自換算的下場是補完結構描述之後游標跳到別的地方，
/// 而使用者只會覺得「按了右鍵之後游標亂跑」。
/// </remarks>
public sealed class SqlTextRewriteResult
{
    public SqlTextRewriteResult(string text, int affectedCount, int caretPosition)
    {
        Text = text;
        AffectedCount = affectedCount;
        CaretPosition = caretPosition;
    }

    /// <summary>換完之後的完整文字；<see cref="AffectedCount"/> 為零時與原文相同。</summary>
    public string Text { get; }

    /// <summary>動了幾處。</summary>
    public int AffectedCount { get; }

    /// <summary>原本的游標位置換算到新文字之後的結果；負值代表呼叫端沒有指定游標。</summary>
    public int CaretPosition { get; }

    /// <summary>有沒有真的改到東西。</summary>
    public bool HasChanges => AffectedCount > 0;
}
