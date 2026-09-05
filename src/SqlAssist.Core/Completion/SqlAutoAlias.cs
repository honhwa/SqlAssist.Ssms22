using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlAssist.Core.Parsing;
using SqlAssist.Core.Settings;

namespace SqlAssist.Core.Completion;

/// <summary>
/// 在資料來源位置提交資料表、檢視或資料表值函式時，自動為物件名補上的別名。
/// </summary>
/// <remarks>
/// 產生規則由需求定死：名稱分段取首字母、轉小寫（<c>Lib_Reader</c> → <c>lr</c>、
/// <c>LoanDetail</c> → <c>ld</c>）；同一個敘述裡已經用掉的別名不重複，衝突就加
/// 序號（<c>lr2</c>、<c>lr3</c>…）。比較的字首大小寫不敏感，因為 SQL 的識別項
/// 本來就不分大小寫。
///
/// 這支只做「取名」這件純文字的事：現在這個位置是不是資料來源、設定開不開，
/// 由呼叫端先問 <see cref="SqlCompletionContext.MayAppendTableAlias"/> 與設定值，
/// 別名要接在插入文字的哪一段後面則由各呼叫端自己決定。
/// </remarks>
public static class SqlAutoAlias
{
    /// <summary>
    /// 依據資料來源名稱產生別名底稿：分段取首字母並轉小寫。
    /// </summary>
    /// <remarks>
    /// 分段規則：<c>_</c>、空白與連字號一定是段界；大小寫交替也是
    /// （<c>LoanDetail</c> 拆成 <c>Loan</c>、<c>Detail</c>，連續大寫後接小寫時
    /// 也在交界處拆，<c>HTTPServer</c> 拆成 <c>HTTP</c>、<c>Server</c>）。
    /// 取名失敗（名稱裡一個字母都沒有）時回傳空字串，由呼叫端自行決定退回不補。
    /// </remarks>
    public static string Create(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return string.Empty;
        }

        var words = SplitWords(sourceName);
        var alias = new string(
            words
                .Select(word => word.FirstOrDefault(char.IsLetter))
                .Where(letter => letter != default)
                .Select(char.ToLowerInvariant)
                .ToArray());

        if (alias.Length > 0)
        {
            return alias;
        }

        // 名稱全是數字或符號的極端狀況：退而求其次取整串去分隔後的小寫，
        // 至少補上去的還是可以用的識別項。
        return new string(
            sourceName
                .Where(character => char.IsLetterOrDigit(character))
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    /// <summary>
    /// 讓別名不與目前已使用的名稱重複：撞名時依序加 2、3…（<c>lr</c> → <c>lr2</c>）。
    /// </summary>
    /// <param name="used">同一個敘述裡已經可以拿來限定欄位的名稱，大小寫不分。</param>
    public static string MakeUnique(string alias, IEnumerable<string> used)
    {
        if (string.IsNullOrEmpty(alias) || used is null)
        {
            return alias;
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in used)
        {
            if (!string.IsNullOrEmpty(name))
            {
                usedNames.Add(name);
            }
        }

        if (!usedNames.Contains(alias))
        {
            return alias;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = alias + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// 組出要拼在插入文字後面的整段別名，前後都帶一個空格（<c> AS lr </c>、
    /// <c> lr </c>）；這一格不適用自動別名時回傳 null。
    /// </summary>
    /// <remarks>
    /// 回傳 null 的三種情形：設定關閉、這個位置不是資料來源名稱位置、
    /// 建議項不是資料表／檢視／資料表值函式。名稱一個字母都沒有時也回 null——
    /// 寧可不補也不要補出一個奇怪的別名。
    /// </remarks>
    public static string? ComposeSuffix(
        SqlSuggestion suggestion,
        SqlCompletionContext context,
        SqlAssistSettings settings)
    {
        if (suggestion is null)
        {
            throw new ArgumentNullException(nameof(suggestion));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var style = settings.TableSourceAliasStyle;
        if (style == SqlTableSourceAliasStyle.Off || !context.MayAppendTableAlias)
        {
            return null;
        }

        if (suggestion.Kind is not (SuggestionKind.Table or SuggestionKind.View or SuggestionKind.TableFunction))
        {
            return null;
        }

        var alias = MakeUnique(Create(suggestion.DisplayText), CollectUsedNames(context.ScopeSources));
        if (string.IsNullOrEmpty(alias))
        {
            return null;
        }

        var keyword = style == SqlTableSourceAliasStyle.As ? "AS " : string.Empty;
        return $" {keyword}{alias} ";
    }

    /// <summary>
    /// 從游標可見的資料來源收集「已經拿來限定欄位」的名稱：有別名用別名，
    /// 沒有就用物件名。子查詢與 CTE 這類來源也把外層限定名算進去，因為
    /// 它們在 FROM 清單裡一樣佔著名字。
    /// </summary>
    private static IEnumerable<string> CollectUsedNames(IEnumerable<SqlColumnSource> sources)
    {
        if (sources is null)
        {
            yield break;
        }

        foreach (var source in sources)
        {
            var name = source.Table?.EffectiveName;
            if (string.IsNullOrEmpty(name))
            {
                name = source.Qualifier;
            }

            if (!string.IsNullOrEmpty(name))
            {
                // 已通過 IsNullOrEmpty 守衛,但迭代器的 null 狀態追蹤會丟失,
                // 用 ! 告訴編譯器這裡 name 必非 null。
                yield return name!;
            }
        }
    }

    private static List<string> SplitWords(string sourceName)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        for (var index = 0; index < sourceName.Length; index++)
        {
            var character = sourceName[index];

            if (IsWordSeparator(character))
            {
                Flush();
                continue;
            }

            if (current.Length > 0 && char.IsUpper(character))
            {
                // netstandard2.0 沒有 System.Index,改用 Length-1 走索引,
                // 避免 CS0656 'System.Index..ctor' 缺失。
                var previous = current[current.Length - 1];
                var boundaryAfterLower = char.IsLower(previous) || char.IsDigit(previous);
                var boundaryAfterAcronym =
                    char.IsUpper(previous) &&
                    index + 1 < sourceName.Length &&
                    char.IsLower(sourceName[index + 1]);

                if (boundaryAfterLower || boundaryAfterAcronym)
                {
                    Flush();
                }
            }

            current.Append(character);
        }

        Flush();
        return words;
    }

    private static bool IsWordSeparator(char character) =>
        character == '_' || character == '-' || character == '.' || char.IsWhiteSpace(character);
}
