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
/// </remarks>
public sealed class SqlJoinKeySource
{
    public SqlJoinKeySource(string? qualifier, IReadOnlyList<string> names)
    {
        Qualifier = qualifier;
        Names = names ?? throw new ArgumentNullException(nameof(names));
    }

    /// <summary>這個來源的限定字（別名或名稱）；讀不出來時為 null。</summary>
    public string? Qualifier { get; }

    /// <summary>這個來源看得到的欄位名稱。</summary>
    public IReadOnlyList<string> Names { get; }
}
