namespace SqlAssist.Core.Completion;

/// <summary>
/// 游標前剛寫完的一個等值比較：兩側都是帶限定字的欄位。
/// </summary>
/// <remarks>
/// 只留名稱不留位置：要補的 <c>COLLATE</c> 接在游標處，而游標本來就停在右邊那個
/// 運算元的尾端，位置不必再從文字裡算回來。
/// </remarks>
public sealed class SqlColumnComparison
{
    public SqlColumnComparison(
        string leftQualifier,
        string leftName,
        string rightQualifier,
        string rightName)
    {
        LeftQualifier = leftQualifier;
        LeftName = leftName;
        RightQualifier = rightQualifier;
        RightName = rightName;
    }

    /// <summary><c>=</c> 左邊那一欄的限定字（別名或資料表名稱）。</summary>
    public string LeftQualifier { get; }

    public string LeftName { get; }

    /// <summary><c>=</c> 右邊那一欄的限定字；那是游標剛寫完的那一欄。</summary>
    public string RightQualifier { get; }

    public string RightName { get; }
}
