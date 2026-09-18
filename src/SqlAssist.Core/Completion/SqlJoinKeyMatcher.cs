using System;
using System.Collections.Generic;
using System.Text;
using SqlAssist.Core.Keywords;
using SqlAssist.Core.Matching;

namespace SqlAssist.Core.Completion;

/// <summary>
/// 在 <c>ON</c> 與 <c>WHERE</c> 之後，把當前對象的欄位與前面來源的同名欄位配起來。
/// </summary>
/// <remarks>
/// 使用者剛打完 <c>ON </c> 時，清單裡同時躺著兩三張表的全部欄位，而他這一句要寫的是
/// 那兩個同名的欄位。同名是最強的線索，而且幾乎不需要判斷：<c>CopyNo</c> 對
/// <c>CopyNo</c> 就是他要的聯結條件。
///
/// <b>當前對象是游標前最後加入的來源</b>，不是清單裡的某一張表的名字。這一問有答案
/// 是因為範圍分析回傳的來源順序就是它們在敘述裡出現的順序：<c>FROM A a JOIN B b ON |</c>
/// 的當前對象是 <c>b</c>，<c>… JOIN C c ON |</c> 之後換成 <c>c</c>，而 <c>WHERE |</c>
/// 沿用的是同樣的規則——那裡最後加入的還是 <c>c</c>。
///
/// 配不到就什麼都不做：名稱沒有交集時清單維持原樣，這條路徑不猜。
/// </remarks>
public static class SqlJoinKeyMatcher
{
    /// <summary>
    /// 這個位置要不要做配對。
    /// </summary>
    /// <remarks>
    /// 認的是述詞的起點：<c>ON</c>、<c>WHERE</c>、<c>HAVING</c>（以及 <c>AND</c>／<c>OR</c>）
    /// 的正後方。條件的左邊寫完之後就不是了，而那裡本來也不該再給整條條件——
    /// <c>ON a.CopyNo = |</c> 之後要的是右邊那一個欄位，插入整條條件會寫出
    /// <c>= a.CopyNo = b.CopyNo</c>。
    ///
    /// <c>SELECT |</c> 與 <c>FROM |</c> 不在這裡：那些位置要的是欄位或資料表本身，
    /// 把某兩張表之間的聯結條件塞到最前面只是佔位。
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
    /// 同名比對用的正規化名稱：轉小寫並去掉分隔符。
    /// </summary>
    /// <remarks>
    /// 分隔符與模糊比對的詞界共用同一份（<see cref="FuzzyMatcher.SqlDelimiters"/>），
    /// 所以 <c>CopyNo</c>、<c>copy_no</c> 與 <c>COPYNO</c> 是同一個名稱。
    /// 全部由分隔符組成的名稱回空字串，呼叫端據此跳過——那樣的兩個名稱互相配對
    /// 等於說「所有名稱都同名」。
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
    /// 算出當前對象（最後一個來源）有哪些欄位配得到前面的來源。
    /// </summary>
    /// <param name="sources">
    /// 敘述裡的資料來源，順序就是它們在敘述中出現的順序；每筆帶著已知的欄位名稱。
    /// 名稱還不知道的來源要照樣傳進來（名稱給空集合），這樣索引才對得上呼叫端的來源。
    /// </param>
    /// <remarks>
    /// 回傳的鍵是當前對象的欄位名稱。同一個欄位只會有一筆配對，取<b>最近的</b>
    /// 那個來源：<c>A a JOIN B b … JOIN C c</c> 之後，<c>c</c> 的欄位要接的是
    /// <c>b</c>，接 <c>a</c> 是同一個名稱卻跨過了一張表。近的來源配不到時才往外找。
    ///
    /// 同一個名稱攤平出來的來源會先合併（<c>(SELECT Id, * FROM T) d</c> 是兩筆都叫
    /// <c>d</c>）：不合併的話當前來源可能只拿得到寫死的那幾個名稱，而它明明還有
    /// 一整套欄位。兩側限定字相同的來源直接跳過——同一個名稱配自己不是聯結條件。
    ///
    /// 一筆都配不到時<b>照樣</b>回報當前群組的索引：那份索引說的是「當前對象是誰」，
    /// 與配不配得到無關，而呼叫端要拿它認出該動哪幾個來源。
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

        foreach (var name in current.Names)
        {
            if (pairs.ContainsKey(name))
            {
                continue;
            }

            var normalized = Normalize(name);

            if (normalized.Length == 0)
            {
                continue;
            }

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

                pairs.Add(name, new SqlJoinKey(name, current.Qualifier, counterpart, other.Qualifier));
                break;
            }
        }

        return new SqlJoinKeyMatch(current.Indexes, pairs);
    }

    /// <summary>
    /// 把來源依「同一個名稱」分群，順序不變，並記下每一群是傳入清單裡的哪幾筆。
    /// </summary>
    /// <remarks>
    /// 索引要留著：呼叫端要拿它認出「當前對象是哪幾筆來源」，才能在清單裡精準替換
    /// 那幾個欄位，而別的同名欄位一個都不動。
    /// </remarks>
    private static List<SourceGroup> Group(IReadOnlyList<SqlJoinKeySource> sources)
    {
        var groups = new List<SourceGroup>();

        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var last = groups.Count > 0 ? groups[groups.Count - 1] : null;

            // 限定字為 null 的那幾筆不併：兩個都沒有別名的衍生資料表是兩個來源，
            // 併起來會讓其中一個的欄位憑空跑到另一個身上。
            if (source.Qualifier is not null &&
                last?.Qualifier is not null &&
                string.Equals(last.Qualifier, source.Qualifier, StringComparison.OrdinalIgnoreCase))
            {
                last.Absorb(index, source.Names);
                continue;
            }

            groups.Add(new SourceGroup(source.Qualifier, index, source.Names));
        }

        return groups;
    }

    private static bool CanPair(SourceGroup current, SourceGroup other)
    {
        if (current.Qualifier is null)
        {
            return other.Qualifier is not null;
        }

        return !string.Equals(current.Qualifier, other.Qualifier, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>同名限定字的一組來源。</summary>
    private sealed class SourceGroup
    {
        private Dictionary<string, string>? _byNormalizedName;

        public SourceGroup(string? qualifier, int index, IReadOnlyList<string> names)
        {
            Qualifier = qualifier;
            Indexes = new List<int> { index };
            Names = new List<string>(names);
        }

        public string? Qualifier { get; }

        /// <summary>這一群在傳入清單裡的來源索引，依出現順序。</summary>
        public List<int> Indexes { get; }

        public List<string> Names { get; }

        /// <summary>把同群的下一筆來源併進來。</summary>
        /// <remarks>
        /// 重複的名稱只留第一筆。比對忽略大小寫，與 <see cref="Find"/> 的正規化一致：
        /// 一邊忽略、一邊不忽略的話，<c>CopyNo</c> 與 <c>copy_no</c> 會被當成兩個
        /// 名稱收進來，而它們在索引裡本來只算一個。
        /// </remarks>
        public void Absorb(int index, IReadOnlyList<string> names)
        {
            Indexes.Add(index);

            foreach (var name in names)
            {
                var duplicate = false;

                foreach (var existing in Names)
                {
                    if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    Names.Add(name);
                }
            }
        }

        /// <summary>
        /// 在這個來源裡找正規化後同名的欄位，回傳它的原始名稱。
        /// </summary>
        /// <remarks>
        /// 索引是延後才建的：一個位置只有當前群組與它前面的幾群會被查，而每一群都是
        /// 問一次就結束。這條路徑在每一次按鍵上，沒有理由為了一個不一定會用到的群組
        /// 先把整份名稱正規化一遍。
        ///
        /// 同一個正規化名稱對到多個欄位時取<b>定義順序最前面</b>那一筆，與清單的
        /// 排序一致；後面的同義名稱不再覆蓋它。
        /// </remarks>
        public string? Find(string normalized)
        {
            _byNormalizedName ??= BuildIndex();

            return _byNormalizedName.TryGetValue(normalized, out var name) ? name : null;
        }

        private Dictionary<string, string> BuildIndex()
        {
            var index = new Dictionary<string, string>(Names.Count, StringComparer.Ordinal);

            foreach (var name in Names)
            {
                var normalized = Normalize(name);

                if (normalized.Length > 0 && !index.ContainsKey(normalized))
                {
                    index.Add(normalized, name);
                }
            }

            return index;
        }
    }
}
