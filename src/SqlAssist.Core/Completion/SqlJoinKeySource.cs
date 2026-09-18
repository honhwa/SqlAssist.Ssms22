using System;
using System.Collections.Generic;

namespace SqlAssist.Core.Completion;

/// <summary>
/// 配對鍵比對的一個輸入：敘述裡的一個資料來源，以及它在游標處已知的欄位名稱。
/// </summary>
/// <remarks>
/// 名稱由呼叫端餵進來，因為資料表與檢視的欄位只有中繼資料知道，而那只存在於
/// Ssms22 那一層；這裡只做純文字的比對，讓規則本身測得到。
///
/// <see cref="Qualifier"/> 為 null 代表敘述裡讀不出可用來限定欄位的名稱
/// （沒有別名的衍生資料表）。這種來源仍然可以配對，只是寫出來的條件兩側都沒有名字。
/// </remarks>
public sealed class SqlJoinKeySource
{
    public SqlJoinKeySource(string? qualifier, IReadOnlyList<string> names)
    {
        Qualifier = qualifier;
        Names = names ?? throw new ArgumentNullException(nameof(names));
    }

    /// <summary>在敘述中限定這個來源的名稱；讀不出來時為 null。</summary>
    public string? Qualifier { get; }

    /// <summary>已知的欄位名稱，順序就是它們的定義順序。</summary>
    public IReadOnlyList<string> Names { get; }
}
