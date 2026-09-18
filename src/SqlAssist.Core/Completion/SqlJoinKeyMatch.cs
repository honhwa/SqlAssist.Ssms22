using System;
using System.Collections.Generic;

namespace SqlAssist.Core.Completion;

/// <summary>
/// 一次配對的結果：配到哪些欄位，以及「當前對象」是傳入清單裡的哪幾個來源。
/// </summary>
/// <remarks>
/// 光有一份「欄位名稱 → 配對」的字典不夠用。呼叫端要據此把候選項換成整條條件，
/// 而<b>同名欄位在別的來源裡也有</b>——<c>FROM A a JOIN B b ON |</c> 的候選清單裡
/// <c>a</c> 與 <c>b</c> 都有 <c>CopyNo</c>，只有 <c>b</c> 那一筆該被換掉。
/// 索引講的正是這件事：<see cref="SourceIndexes"/> 是當前群組在傳入清單中的位置，
/// 呼叫端拿它跟自己的來源序號對照就知道該動哪幾筆。
///
/// 群組可能是多筆來源合起來的（<c>(SELECT Id, * FROM T) d</c> 攤平成兩筆同名的
/// <c>d</c>），所以這是一份清單而不是一個索引。
/// </remarks>
public sealed class SqlJoinKeyMatch
{
    /// <summary>
    /// 沒有當前對象可談：來源不到兩個，連「前面」都沒有。
    /// </summary>
    /// <remarks>
    /// 這一筆只用在來源本來就少於兩個的時候。配不到<b>欄位</b>是另一回事——
    /// 那時候仍然回報當前群組的索引，因為「當前對象是誰」與配不配得到無關。
    /// </remarks>
    public static readonly SqlJoinKeyMatch Empty = new SqlJoinKeyMatch(
        Array.Empty<int>(),
        new Dictionary<string, SqlJoinKey>(StringComparer.Ordinal));

    public SqlJoinKeyMatch(
        IReadOnlyList<int> sourceIndexes,
        IReadOnlyDictionary<string, SqlJoinKey> pairs)
    {
        SourceIndexes = sourceIndexes ?? throw new ArgumentNullException(nameof(sourceIndexes));
        Pairs = pairs ?? throw new ArgumentNullException(nameof(pairs));
    }

    /// <summary>當前群組在傳入來源清單中的索引。</summary>
    public IReadOnlyList<int> SourceIndexes { get; }

    /// <summary>配對結果；鍵是當前對象的欄位名稱。</summary>
    public IReadOnlyDictionary<string, SqlJoinKey> Pairs { get; }

    /// <summary>一個欄位都沒配到。</summary>
    public bool IsEmpty => Pairs.Count == 0;
}
