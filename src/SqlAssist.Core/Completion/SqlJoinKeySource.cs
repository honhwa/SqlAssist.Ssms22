using System;
using System.Collections.Generic;

namespace SqlAssist.Core.Completion;

/// <summary>
/// 配對時的一個來源：它的限定字與它看得到的欄位名稱。
/// </summary>
/// <remarks>
/// 刻意不吃 <see cref="SqlColumnSource"/>：配對只在乎「這個來源叫什麼、有哪些欄位」，
/// 而欄位清單在別的來源上是懶載入的。<see cref="Names"/> 是空的代表「這個來源的
/// 欄位還不知道」——那時它配不出任何東西，但不算錯，因為查中繼資料本來就可能失敗。
///
/// 定序與名稱平行存放而不是另立一份對照表：兩者由同一次走訪一起收齊，
/// 而名稱在清單裡可以重複（同一張表被攤平成兩筆來源時），用名稱當鍵反而要處理碰撞。
/// </remarks>
public sealed class SqlJoinKeySource
{
    public SqlJoinKeySource(
        string? qualifier,
        IReadOnlyList<string> names,
        IReadOnlyList<string?>? collations = null)
    {
        Qualifier = qualifier;
        Names = names ?? throw new ArgumentNullException(nameof(names));
        Collations = collations;
    }

    /// <summary>這個來源的限定字（別名或名稱）；讀不出來時為 null。</summary>
    public string? Qualifier { get; }

    /// <summary>這個來源看得到的欄位名稱。</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>
    /// 與 <see cref="Names"/> 逐項對應的欄位定序；整份不傳或某一項為 null 代表「不知道」。
    /// </summary>
    /// <remarks>
    /// 不知道就當成沒有衝突：猜一個定序出來會讓寫進編輯器的條件多一段錯的
    /// <c>COLLATE</c>，而那比原本就少寫那一段更難查——原本的失敗至少是引擎自己報的。
    /// </remarks>
    public IReadOnlyList<string?>? Collations { get; }

    /// <summary>第 <paramref name="index"/> 個名稱的定序；沒有時為 null。</summary>
    public string? CollationAt(int index) =>
        Collations is not null && index >= 0 && index < Collations.Count ? Collations[index] : null;
}
