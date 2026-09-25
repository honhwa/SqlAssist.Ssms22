using System;
using System.Collections.Generic;

namespace SqlAssist.Core.Completion;

/// <summary>
/// 一次配對的結果：哪些來源算「當前」的，以及它們各自配到的欄位。
/// </summary>
/// <remarks>
/// 分開兩份而不是合併成一張表：<see cref="Pairs"/> 只涵蓋當前來源真的配到的欄位，
/// 而「哪些欄位算當前來源的」是另一個問題——單一來源時索引鍵要排前面，
/// 有兩個以上來源時還要靠 <see cref="SourceIndexes"/> 分辨。
/// </remarks>
public sealed class SqlJoinKeyMatch
{
    /// <summary>沒有配到任何東西。</summary>
    public static readonly SqlJoinKeyMatch Empty = new SqlJoinKeyMatch(
        Array.Empty<int>(),
        new Dictionary<string, SqlJoinKey>(StringComparer.Ordinal));

    public SqlJoinKeyMatch(IReadOnlyList<int> sourceIndexes, IReadOnlyDictionary<string, SqlJoinKey> pairs)
    {
        SourceIndexes = sourceIndexes ?? throw new ArgumentNullException(nameof(sourceIndexes));
        Pairs = pairs ?? throw new ArgumentNullException(nameof(pairs));
    }

    /// <summary>算「當前」的那些來源在原始清單裡的索引。</summary>
    public IReadOnlyList<int> SourceIndexes { get; }

    /// <summary>欄位名稱到配對結果的對照；鍵是欄位名稱，比對時不分大小寫。</summary>
    public IReadOnlyDictionary<string, SqlJoinKey> Pairs { get; }

    /// <summary>配對結果是不是空的。</summary>
    public bool IsEmpty => Pairs.Count == 0;
}
