using System;
using System.Collections.Generic;
using System.Text;
using SqlAssist.Core.Keywords;
using SqlAssist.Core.Matching;

namespace SqlAssist.Core.Completion;

/// <summary>
/// 在 <c>ON</c> 與 <c>WHERE</c> 的正後方，把當前來源的同名欄位與前面來源的同名欄位配起來。
/// </summary>
/// <remarks>
/// 手寫 JOIN 的苦工幾乎都在這裡：<c>ON b.CopyNo = a.CopyNo</c> 的兩邊是同一個欄位名稱，
/// 打第二次的時候只是為了湊語法。所以做法是——游標停在述詞起點時，把「最近加入的那個來源」
/// 的每一個欄位，拿去跟前面每一個來源比對名稱，同名的就產出一條完整的條件。
///
/// 為什麼是「最近的那一個」：<c>FROM a JOIN b JOIN c ON …</c> 的 <c>ON</c> 接的是
/// 剛剛才寫完的 <c>c</c>，把它跟 <c>b</c> 接起來才是使用者正要寫的。往前一層一層找，
/// 第一個找到同名欄位的就停——<c>a</c>、<c>b</c> 都有 <c>Id</c> 時取 <c>b</c>，
/// 因為那一個離得最近。
///
/// 欄位名稱的比對去掉了分隔符並轉小寫：<c>Copy_No</c> 與 <c>CopyNo</c> 是同一件事，
/// 而 T-SQL 的識別碼在大多排序規則下不分大小寫。分隔符名單與模糊比對共用
/// （<see cref="FuzzyMatcher.SqlDelimiters"/>），免得兩邊各有一份而慢慢分岔。
///
/// 兩側定序不同時，配出來的那一條條件會多一段 <c>COLLATE</c>（見
/// <see cref="SqlJoinKey.Collation"/>）：跨資料庫的兩張表各自用自己的定序，
/// 直接比較是錯誤 468，而使用者按下這一項時要的是一條<b>跑得動</b>的條件。
/// </remarks>
public static class SqlJoinKeyMatcher
{
    /// <summary>
    /// 這個位置要不要配對鍵。
    /// </summary>
    /// <remarks>
    /// 問的是「述詞起點」而不是「在 ON 後面」：<c>WHERE</c>、<c>HAVING</c> 與接在
    /// <c>AND</c>／<c>OR</c> 後面也都是同一個情境——上一條條件寫完了，接著要寫一條
    /// 把兩個來源接起來的。真正的判斷在
    /// <see cref="SqlKeywordPositionAnalyzer.IsPredicateStart"/>。
    /// </remarks>
    public static bool WantsJoinKeys(SqlCompletionContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return SqlKeywordPositionAnalyzer.IsPredicateStart(context.KeywordPosition);
    }

    /// <summary>
    /// 欄位名稱的比對鍵：去掉分隔符並轉小寫。
    /// </summary>
    /// <remarks>
    /// <c>Copy_No</c>、<c>copy no</c> 與 <c>CopyNo</c> 都會收斂成 <c>copyno</c>。
    /// 一個字元都不剩時回傳空字串，呼叫端要自己跳過——空字串會把所有
    /// 「名稱剛好都是符號」的欄位兩兩配起來。
    /// </remarks>
    public static string Normalize(string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            if (FuzzyMatcher.SqlDelimiters.IndexOf(character) >= 0)
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>
    /// 把清單最後一個來源的欄位與前面的來源配起來。
    /// </summary>
    /// <remarks>
    /// 只回傳有配到東西的欄位：沒有同名欄位的欄位照常列在一般欄位清單裡，
    /// 這裡不替它捏造一個對象。
    ///
    /// 來源少於兩個時直接回傳空的——一個來源沒有「另一邊」可以配。
    /// </remarks>
    public static SqlJoinKeyMatch Pair(IReadOnlyList<SqlJoinKeySource> sources)
    {
        if (sources is null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        var groups = Group(sources);

        if (groups.Count < 2)
        {
            return SqlJoinKeyMatch.Empty;
        }

        var current = groups[groups.Count - 1];
        var pairs = new Dictionary<string, SqlJoinKey>(StringComparer.Ordinal);

        foreach (var column in current.Columns)
        {
            var name = column.Name;

            // 同一個名稱在來源裡出現兩次時只留第一筆：第二筆配到的是同一個對象，
            // 多一條清單項目只是讓使用者多看一次。
            if (pairs.ContainsKey(name))
            {
                continue;
            }

            var normalized = Normalize(name);

            if (normalized.Length == 0)
            {
                continue;
            }

            // 由近到遠找第一個配得上的來源：最近的來源才是使用者正要接的那一個。
            for (var index = groups.Count - 2; index >= 0; index--)
            {
                var other = groups[index];

                if (!CanPair(current, other))
                {
                    continue;
                }

                var counterpart = other.Find(normalized);

                if (counterpart is null)
                {
                    continue;
                }

                pairs.Add(
                    name,
                    new SqlJoinKey(
                        name,
                        current.Qualifier,
                        counterpart.Name,
                        other.Qualifier,
                        SqlJoinKey.CollationToApply(column.Collation, counterpart.Collation)));

                break;
            }
        }

        return new SqlJoinKeyMatch(current.Indexes, pairs);
    }

    /// <summary>
    /// 把來源依限定字收成群組：同一個限定字相鄰的來源是同一張表的延伸。
    /// </summary>
    /// <remarks>
    /// <c>FROM (SELECT …) d JOIN T t ON d.X = t.X</c> 這種情況下，衍生資料表與它
    /// 展開出的欄位共用一個限定字，兩筆來源在清單裡相鄰而限定字相同，該視為同一邊。
    /// 不相鄰就不合併：中間夾了別的來源代表使用者的接法已經換對象了。
    /// </remarks>
    private static List<SourceGroup> Group(IReadOnlyList<SqlJoinKeySource> sources)
    {
        var groups = new List<SourceGroup>();

        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var last = groups.Count > 0 ? groups[groups.Count - 1] : null;

            if (source.Qualifier is not null &&
                last?.Qualifier is not null &&
                string.Equals(last.Qualifier, source.Qualifier, StringComparison.OrdinalIgnoreCase))
            {
                last.Absorb(index, source);
            }
            else
            {
                groups.Add(new SourceGroup(source.Qualifier, index, source));
            }
        }

        return groups;
    }

    /// <summary>
    /// 這兩個群組能不能互配。
    /// </summary>
    /// <remarks>
    /// 限定字一樣的兩邊跳過：那代表它們是同一張表（<see cref="Group"/> 已經先合併過
    /// 相鄰的），把一張表跟自己配起來會產出 <c>a.CopyNo = a.CopyNo</c>。
    ///
    /// 限定字為 null 的衍生資料表不併進別人的群組，但它自己可以往後配——
    /// 那時它是一個獨立的來源，而對面的限定字一定要有值。
    /// </remarks>
    private static bool CanPair(SourceGroup current, SourceGroup other)
    {
        if (current.Qualifier is null)
        {
            return other.Qualifier is not null;
        }

        return !string.Equals(current.Qualifier, other.Qualifier, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SourceGroup
    {
        private readonly List<Column> _columns = new();
        private readonly Dictionary<string, Column> _byName = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Column>? _byNormalizedName;

        public SourceGroup(string? qualifier, int index, SqlJoinKeySource source)
        {
            Qualifier = qualifier;
            Indexes = new List<int> { index };
            Add(source);
        }

        public string? Qualifier { get; }

        public List<int> Indexes { get; }

        public IReadOnlyList<Column> Columns => _columns;

        public void Absorb(int index, SqlJoinKeySource source)
        {
            Indexes.Add(index);
            Add(source);
        }

        /// <summary>用正規化後的名稱找回原始欄位；找不到時為 null。</summary>
        public Column? Find(string normalized)
        {
            _byNormalizedName ??= BuildIndex();

            return _byNormalizedName.TryGetValue(normalized, out var column) ? column : null;
        }

        private void Add(SqlJoinKeySource source)
        {
            for (var index = 0; index < source.Names.Count; index++)
            {
                var name = source.Names[index];
                var collation = source.CollationAt(index);

                // 重名的只留第一筆，比對不分大小寫；但定序可以從後面那一筆補上——
                // 衍生資料表先攤出寫死的名稱、才攤出資料表那一筆，先到的那一筆沒有定序。
                if (_byName.TryGetValue(name, out var existing))
                {
                    if (existing.Collation is null)
                    {
                        existing.Collation = collation;
                    }

                    continue;
                }

                var column = new Column(name, collation);
                _byName.Add(name, column);
                _columns.Add(column);
            }
        }

        private Dictionary<string, Column> BuildIndex()
        {
            var index = new Dictionary<string, Column>(_columns.Count, StringComparer.Ordinal);

            foreach (var column in _columns)
            {
                var normalized = Normalize(column.Name);

                // 空鍵會把「名稱只有分隔符」的欄位配給任何人，直接不進索引。
                if (normalized.Length > 0 && !index.ContainsKey(normalized))
                {
                    index.Add(normalized, column);
                }
            }

            return index;
        }
    }

    /// <summary>群組裡的一個欄位：原始名稱與它的定序。</summary>
    private sealed class Column
    {
        public Column(string name, string? collation)
        {
            Name = name;
            Collation = collation;
        }

        public string Name { get; }

        public string? Collation { get; set; }
    }
}
