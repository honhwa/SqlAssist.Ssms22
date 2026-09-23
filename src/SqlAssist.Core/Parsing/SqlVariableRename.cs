using System;
using System.Collections.Generic;

namespace SqlAssist.Core.Parsing;

/// <summary>候選新名不能用的原因。</summary>
public enum SqlVariableRenameProblem
{
    /// <summary>可以用。</summary>
    None,

    /// <summary>沒有輸入任何名稱。</summary>
    Empty,

    /// <summary>不是合法的 T-SQL 區域變數名稱。</summary>
    InvalidName,

    /// <summary>同一個批次裡已經有另一個變數叫這個名字。</summary>
    Duplicate
}

/// <summary>
/// F2 重新命名的對象：游標所在的區域變數，以及它在同一個批次裡的所有出現處。
/// </summary>
/// <remarks>
/// 範圍取的是<b>批次</b>，不是敘述，也不是整份指令碼。T-SQL 的區域變數活到批次
/// 結束——<c>GO</c> 之後那個名字就不存在了——所以「同一個變數」的邊界正好是
/// <c>GO</c> 的邊界。取敘述會漏掉宣告與其他敘述裡的用法；取整份指令碼則會把另一個
/// 批次裡恰好同名的變數一起改掉，而那是兩個不同的變數。
///
/// 名稱比對不分大小寫：預設定序下 <c>@Id</c> 與 <c>@id</c> 是同一個變數。改完之後
/// 每一處的拼法會被統一成使用者剛打的那一種，那正是他要的結果。
///
/// 只看詞法單元，因此字串與註解裡的 <c>@x</c> 不算出現處——
/// <see cref="SqlTokenizer.Tokenize(string)"/> 把它們讀成字串與註解，
/// 不是變數。
/// </remarks>
public sealed class SqlVariableRenameTarget
{
    internal SqlVariableRenameTarget(
        string name,
        int nameStart,
        IReadOnlyList<int> nameStarts,
        IReadOnlyList<string> otherNames)
    {
        Name = name;
        NameStart = nameStart;
        NameStarts = nameStarts;
        OtherNames = otherNames;
    }

    /// <summary>目前的名稱，不含小老鼠。</summary>
    public string Name { get; }

    /// <summary>游標那一處的名稱起點，也就是小老鼠之後的那一個字元。</summary>
    public int NameStart { get; }

    /// <summary>
    /// 名稱長度。
    /// </summary>
    /// <remarks>
    /// 同一個變數的每一處拼法相同，所以只有一個長度。呼叫端拿它把
    /// <see cref="NameStarts"/> 還原成編輯器裡的區段。
    /// </remarks>
    public int NameLength => Name.Length;

    /// <summary>同一個批次裡每一處的名稱起點，遞增排列，包含游標那一處。</summary>
    public IReadOnlyList<int> NameStarts { get; }

    /// <summary>同一個批次裡其他區域變數的名稱，不含小老鼠，用來擋撞名。</summary>
    public IReadOnlyList<string> OtherNames { get; }
}

/// <summary>
/// 找出游標所在的區域變數並驗證新名稱，供 F2 重新命名使用。
/// </summary>
/// <remarks>
/// 這裡只回答「要改哪幾處」與「這個新名字能不能用」，實際的取代由編輯器那一層
/// 用一次編輯套用。分開的理由是這一半只看文字就判斷得出來，因此測得到——
/// 編輯器那一層在測試專案裡連不到 SSMS 服務。
/// </remarks>
public static class SqlVariableRename
{
    /// <summary>
    /// 找出 <paramref name="caretPosition"/> 所在的區域變數。
    /// </summary>
    /// <remarks>
    /// 游標停在名稱結尾也算在裡面：使用者剛打完字時游標就在那裡。
    /// 兩個相鄰變數共用一個邊界（<c>@a@b</c>）時靠左的那一個勝出——
    /// 那是游標剛離開的那一個。
    ///
    /// 回傳 null 的三種情形：游標不在變數上、只有一個小老鼠沒有名字（<c>@</c>）、
    /// 以及 <c>@@</c> 開頭的全域變數。最後一種不是使用者取的名字，改不動也不該改。
    /// </remarks>
    public static SqlVariableRenameTarget? FindAt(string sql, int caretPosition)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        if (caretPosition < 0 || caretPosition > sql.Length)
        {
            return null;
        }

        var tokens = SqlTokenizer.Tokenize(sql);
        var index = FindVariableTokenAt(tokens, caretPosition);

        if (index < 0)
        {
            return null;
        }

        var target = tokens[index];
        var name = target.Text.Substring(1);

        if (name.Length == 0 || name[0] == '@')
        {
            return null;
        }

        var first = BatchStart(tokens, index);
        var last = BatchEnd(tokens, index);

        var starts = new List<int>();
        var others = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var position = first; position < last; position++)
        {
            var token = tokens[position];

            if (token.Kind != SqlTokenKind.Variable || token.Text.Length < 2)
            {
                continue;
            }

            var candidate = token.Text.Substring(1);

            if (candidate.Length == 0 || candidate[0] == '@')
            {
                continue;
            }

            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                starts.Add(token.Start + 1);
                continue;
            }

            if (seen.Add(candidate))
            {
                others.Add(candidate);
            }
        }

        return new SqlVariableRenameTarget(
            name,
            target.Start + 1,
            Array.AsReadOnly(starts.ToArray()),
            Array.AsReadOnly(others.ToArray()));
    }

    /// <summary>候選新名能不能用；不能時回報原因。</summary>
    /// <remarks>
    /// 撞名比對的是<b>其他</b>變數：改回原本的名字、或只改大小寫，都不算撞名。
    /// </remarks>
    public static SqlVariableRenameProblem Validate(SqlVariableRenameTarget target, string? candidateName)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (string.IsNullOrEmpty(candidateName))
        {
            return SqlVariableRenameProblem.Empty;
        }

        if (!IsValidName(candidateName))
        {
            return SqlVariableRenameProblem.InvalidName;
        }

        foreach (var other in target.OtherNames)
        {
            if (string.Equals(other, candidateName, StringComparison.OrdinalIgnoreCase))
            {
                return SqlVariableRenameProblem.Duplicate;
            }
        }

        return SqlVariableRenameProblem.None;
    }

    /// <summary>
    /// 名稱本身（不含小老鼠）是不是合法的 T-SQL 區域變數名稱。
    /// </summary>
    /// <remarks>
    /// 字元表來自 <see cref="SqlTokenizer"/>，不是另外抄一份：一個名字在編輯器裡
    /// 打得出來、詞法分析器卻不把它當成一個詞元的話，重新命名會改出一份
    /// 自己都解析不了的指令碼。
    ///
    /// 第一個字元另用 <see cref="SqlTokenizer.IsIdentifierStart"/>，
    /// 因此 <c>@1</c> 這種數字開頭的名字不合法——詞法分析器讀得出來，
    /// 但 T-SQL 不接受。
    /// </remarks>
    public static bool IsValidName(string? name)
    {
        // 不用 string.IsNullOrEmpty：netstandard2.0 的宣告沒有 NotNullWhen，
        // 編譯器因此不知道下面已經安全，會報一個誤報的 CS8602。
        if (name is null || name.Length == 0)
        {
            return false;
        }

        if (!SqlTokenizer.IsIdentifierStart(name[0]))
        {
            return false;
        }

        for (var index = 1; index < name.Length; index++)
        {
            if (!SqlTokenizer.IsIdentifierPart(name[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static int FindVariableTokenAt(IReadOnlyList<SqlToken> tokens, int caretPosition)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];

            if (token.Kind != SqlTokenKind.Variable)
            {
                continue;
            }

            // 詞法單元依起點排序，過了游標就不必再找。
            if (token.Start > caretPosition)
            {
                return -1;
            }

            if (caretPosition <= token.End)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>游標所在批次的第一個詞法單元索引，也就是上一個 <c>GO</c> 之後。</summary>
    private static int BatchStart(IReadOnlyList<SqlToken> tokens, int index)
    {
        for (var position = index - 1; position >= 0; position--)
        {
            if (tokens[position].IsKeyword("GO"))
            {
                return position + 1;
            }
        }

        return 0;
    }

    /// <summary>游標所在批次的最後一個詞法單元索引（不含），也就是下一個 <c>GO</c> 之前。</summary>
    private static int BatchEnd(IReadOnlyList<SqlToken> tokens, int index)
    {
        for (var position = index + 1; position < tokens.Count; position++)
        {
            if (tokens[position].IsKeyword("GO"))
            {
                return position;
            }
        }

        return tokens.Count;
    }
}
