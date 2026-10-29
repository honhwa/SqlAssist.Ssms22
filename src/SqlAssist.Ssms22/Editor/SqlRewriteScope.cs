namespace SqlAssist.Ssms22.Editor;

/// <summary>就地改寫要動的那一段：有選取就是選取，沒有選取就是整份文件。</summary>
/// <remarks>
/// 空選取在編輯器裡不是「沒有範圍」，而是游標上的一個<c>零長度</c>範圍。
/// 直接把它當成改寫目標，等於拿空字串去比對：結果永遠是「沒有需要補的地方」，
/// 使用者按了右鍵選單只覺得這兩顆命令沒實作。判斷集中在這裡，才不會有一份漏掉。
/// </remarks>
internal readonly struct SqlRewriteScope
{
    private SqlRewriteScope(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary>範圍起點（含）。</summary>
    public int Start { get; }

    /// <summary>範圍長度；整份文件時等於文件長度。</summary>
    public int Length { get; }

    /// <summary>算出這次要改寫的範圍。</summary>
    /// <param name="selectionStart">選取起點；沒有選取時是游標位置。</param>
    /// <param name="selectionLength">選取長度；0 代表沒有選取。</param>
    /// <param name="documentLength">整份文件的長度。</param>
    /// <remarks>
    /// 沒有選取時從 0 起算而不是從游標起算：這兩顆命令整理的是整份指令碼
    /// （散在各處的 <c>dbo.</c> 與 <c>TOP</c>），只做游標之後那半份會留下前後不一致的檔案。
    /// </remarks>
    public static SqlRewriteScope Resolve(int selectionStart, int selectionLength, int documentLength) =>
        selectionLength > 0
            ? new SqlRewriteScope(selectionStart, selectionLength)
            : new SqlRewriteScope(0, documentLength);
}
