using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlAssist.Core.Parsing;
using SqlAssist.Core.Settings;

namespace SqlAssist.Core.Completion;

/// <summary>
/// 在資料來源位置提交資料表、檢視或資料表值函式時，替它取一個別名。
/// </summary>
/// <remarks>
/// 這支只做「取名」這件純文字的事。「現在這個位置是不是資料來源」由呼叫端先問
/// <see cref="SqlCompletionContext.MayAppendTableAlias"/>，而「設定開不開」由呼叫端
/// 先看 <see cref="SqlAssistSettings.TableSourceAliasStyle"/>——兩個判斷都需要上下文，
/// 放進來只會讓這支變成第二個版本的上下文分析器。
/// </remarks>
public static class SqlAutoAlias
{
    /// <summary>
    /// 由物件名稱取出別名：各段的首字母小寫。
    /// </summary>
    /// <remarks>
    /// <c>Lib_Reader</c> → <c>lr</c>、<c>LoanDetail</c> → <c>ld</c>。
    /// 分段看的是底線、空白、連字號、點號，以及大小寫交替——所以
    /// <c>HTTPServer</c> 會切成 <c>HTTP</c> 與 <c>Server</c>，兩個首字母是 <c>hs</c>。
    ///
    /// 名稱裡一個字母都沒有時（例如 <c>dbo.[2024]</c>）退回「去掉分隔符後的小寫」；
    /// 連一個字元都不剩就回傳空字串，由呼叫端決定要怎麼處理。
    /// </remarks>
    public static string Create(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return string.Empty;
        }

        var alias = new string(
            SplitWords(sourceName)
                .Select(word => word.FirstOrDefault(char.IsLetter))
                .Where(letter => letter != default)
                .Select(char.ToLowerInvariant)
                .ToArray());

        if (alias.Length > 0)
        {
            return alias;
        }

        return new string(
            sourceName
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    /// <summary>
    /// 別名在同一個敘述裡撞名時加上序號。
    /// </summary>
    /// <remarks>
    /// 比對不區分大小寫：T-SQL 的識別碼在大多排序規則下就是這樣，
    /// 而 <c>LR</c> 與 <c>lr</c> 並存只是看起來像兩個東西。
    /// </remarks>
    public static string MakeUnique(string alias, IEnumerable<string> used)
    {
        if (string.IsNullOrEmpty(alias) || used is null)
        {
            return alias;
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in used)
        {
            if (!string.IsNullOrEmpty(name))
            {
                taken.Add(name);
            }
        }

        if (!taken.Contains(alias))
        {
            return alias;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = alias + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// 這個位置、這一筆建議要不要接別名；要的話回傳連同前後空白的字尾。
    /// </summary>
    /// <remarks>
    /// 回傳字串而不是名稱，是因為它會被直接接在插入文字後面：前後各留一個空白
    /// 讓 <c>FROM dbo.Loan l</c> 與 <c>FROM dbo.Loan AS l</c> 兩種寫法都成立。
    ///
    /// 資料表值函式在「展開函式呼叫」開著時不在這裡接別名——那一條路徑由
    /// <c>SqlFunctionCallExpansion</c> 負責把別名接到右括號之後，這裡再接一次
    /// 會變成 <c>fn() f f</c>。
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
    /// 敘述裡已經用掉的來源名稱。
    /// </summary>
    /// <remarks>
    /// 取的是資料表名稱本身或限定字：<c>FROM dbo.Loan l</c> 會被剖析成一個
    /// 別名為 <c>l</c> 的來源，兩者取其一就足以判斷撞名。
    ///
    /// 用迭代器寫是為了不在來源為空時配置清單，代價是 <c>name</c> 的可空狀態
    /// 跨過 <c>yield</c> 之後會遺失，所以要顯式標成 <c>null</c> 檢查過的
    /// <c>name!</c>。
    /// </remarks>
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
