using System;
using SqlAssist.Core.Keywords;
using SqlAssist.Core.Parsing;

namespace SqlAssist.Core.Completion;

/// <summary>
/// 游標前剛寫完的那一條條件是不是「兩欄相比」，是的話兩側各是哪一欄。
/// </summary>
/// <remarks>
/// 配對鍵只在使用者從清單裡挑的時候寫得出整條條件；自己打的人拿到的是
/// <c>a.CustID = b.CustomerId</c>，跨庫時那一條執行起來是錯誤 468。
/// 所以打完右邊那一欄、接著打分隔字元時再看一次：形狀對、兩側都有定序而且不同，
/// 就把 <c>COLLATE</c> 補在游標處。
///
/// 判斷刻意<b>只認封閉的形狀</b>，不猜：
///
/// <list type="bullet">
/// <item>兩側都要有限定字，而且不同——裸名分不出是哪一個來源的欄位。</item>
/// <item>前面那個字必須是 <c>ON</c>、<c>WHERE</c>、<c>HAVING</c>、<c>AND</c>、<c>OR</c>
/// 或左括號。<c>SET</c>、<c>SELECT</c> 後面同樣是「名稱 <c>=</c> 名稱」，
/// 但那是指定而不是比較，補上 <c>COLLATE</c> 會變成語法錯誤。</item>
/// <item>變數不算欄位：<c>SET @x = a.CopyNo</c> 的左邊是 <c>@x</c>。</item>
/// <item>分隔字元只收空白、<c>)</c>、<c>,</c>、<c>;</c>。運算子與左括號後面的字
/// 不是「這一條寫完了」，那時補上去會插在一個還沒寫完的運算式中間。</item>
/// </list>
///
/// 這個判斷在按鍵路徑上，所以順序由便宜到昂貴：先看分隔字元，再往回讀幾十個字元，
/// 全部成立之後才做需要從頭掃的語彙狀態判斷。
/// </remarks>
public static class SqlJoinCollateAnalyzer
{
    /// <summary>
    /// 這個字元會不會結束一個運算元，而且它後面可以接 <c>COLLATE</c>。
    /// </summary>
    /// <remarks>
    /// 刻意不是 <see cref="SqlKeywordCase.IsWordSeparator"/>：那一份把 <c>=</c>、
    /// <c>+</c>、<c>(</c> 也算成結束一個字，而那些位置上的條件都還沒寫完。
    /// </remarks>
    public static bool IsPredicateSeparator(char value) =>
        value is ' ' or '\t' or '\r' or '\n' or ')' or ',' or ';';

    /// <summary>
    /// <paramref name="position"/> 前面剛好是一條兩欄相比的等值條件時，回傳那兩欄。
    /// </summary>
    /// <param name="text">游標所在的整份文字；右邊那一段要在 <paramref name="position"/> 之前。</param>
    /// <param name="position">游標位置，也就是分隔字元還沒寫進去的地方。</param>
    /// <param name="separator">使用者即將輸入的分隔字元。</param>
    public static SqlColumnComparison? TryFindBefore(ISqlTextSource text, int position, char separator)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (!IsPredicateSeparator(separator) || position <= 0 || position > text.Length)
        {
            return null;
        }

        var index = position;

        if (!TryReadColumn(text, ref index, out var rightQualifier, out var rightName) ||
            !TrySkipEquals(text, ref index) ||
            !TryReadColumn(text, ref index, out var leftQualifier, out var leftName))
        {
            return null;
        }

        if (leftQualifier is null ||
            rightQualifier is null ||
            string.Equals(leftQualifier, rightQualifier, StringComparison.OrdinalIgnoreCase) ||
            !PrecededByPredicateClause(text, ref index))
        {
            return null;
        }

        // 語彙狀態要從頭掃，放在最後：字串與註解裡的同名形狀不是條件。
        if (!SqlLexicalContext.IsCode(text, position))
        {
            return null;
        }

        return new SqlColumnComparison(leftQualifier, leftName, rightQualifier, rightName);
    }

    /// <summary>
    /// 往回讀一個「限定字.欄位」。
    /// </summary>
    /// <remarks>
    /// 限定字只取緊鄰欄位的那一段：<c>LibArchive.dbo.Copy.CopyNo</c> 的限定字是
    /// <c>Copy</c>，與敘述裡沒有別名時 <c>SqlColumnSource.Qualifier</c> 的寫法一致。
    /// 多段名稱整串比對的話，同一張表寫成兩段或四段就配不起來。
    /// </remarks>
    private static bool TryReadColumn(
        ISqlTextSource text,
        ref int index,
        out string? qualifier,
        out string name)
    {
        qualifier = null;

        // 游標前面可能已經有空白（`a.X = b.Y |` 的第二個空白），等號與左邊那一欄之間也是。
        SkipSpaces(text, ref index);

        if (!TryReadName(text, ref index, out name))
        {
            return false;
        }

        // 變數不是欄位：`SET @x = a.CopyNo` 的左邊是 @x。
        if (name[0] == '@')
        {
            return false;
        }

        if (index > 0 && text[index - 1] == '.')
        {
            index--;

            if (!TryReadName(text, ref index, out var owner))
            {
                return false;
            }

            qualifier = owner;

            // 更外層的段落讀掉但不採用：限定字只認緊鄰欄位的那一段。
            // 不讀掉的話 `LibArchive.dbo.Copy.CopyNo` 停在 `dbo` 前面，
            // 後面的 `=` 永遠找不到。
            while (index > 0 && text[index - 1] == '.')
            {
                index--;

                if (!TryReadName(text, ref index, out _))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>往回讀一個名稱：不加括號的識別字，或 <c>[方括號]</c> 包起來的那一種。</summary>
    private static bool TryReadName(ISqlTextSource text, ref int index, out string name)
    {
        name = string.Empty;

        if (index <= 0)
        {
            return false;
        }

        if (text[index - 1] == ']')
        {
            var start = index - 2;

            while (start >= 0 && text[start] != '[')
            {
                start--;
            }

            if (start < 0)
            {
                return false;
            }

            name = text.Substring(start + 1, index - start - 2).Replace("]]", "]");
            index = start;
            return name.Length > 0;
        }

        var begin = index;

        while (begin > 0 && IsNameCharacter(text[begin - 1]))
        {
            begin--;
        }

        if (begin == index)
        {
            return false;
        }

        name = text.Substring(begin, index - begin);
        index = begin;
        return true;
    }

    /// <summary>
    /// 跳過空白並吃掉一個 <c>=</c>。
    /// </summary>
    /// <remarks>
    /// <c>!=</c>、<c>&lt;=</c>、<c>&gt;=</c>、<c>==</c> 都不是等值比較，
    /// 而它們的尾端同樣是 <c>=</c>——不擋的話 <c>a.X != b.Y</c> 會被當成條件。
    /// </remarks>
    private static bool TrySkipEquals(ISqlTextSource text, ref int index)
    {
        SkipSpaces(text, ref index);

        if (index <= 0 || text[index - 1] != '=')
        {
            return false;
        }

        if (index >= 2 && text[index - 2] is '!' or '<' or '>' or '=')
        {
            return false;
        }

        index--;
        return true;
    }

    /// <summary>
    /// 左邊那一欄的前面是不是述詞子句的開頭。
    /// </summary>
    /// <remarks>
    /// 這是「這是一條比較」與「這是一個指定」唯一分得開的地方，所以只認那幾個字，
    /// 不在名單裡的一律當成不是。
    /// </remarks>
    private static bool PrecededByPredicateClause(ISqlTextSource text, ref int index)
    {
        SkipSpaces(text, ref index);

        if (index <= 0)
        {
            return false;
        }

        // 條件可以整條包在括號裡：WHERE (a.X = b.Y
        if (text[index - 1] == '(')
        {
            return true;
        }

        var end = index;

        while (index > 0 && IsNameCharacter(text[index - 1]))
        {
            index--;
        }

        var word = text.Substring(index, end - index);

        return word.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("WHERE", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("HAVING", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("OR", StringComparison.OrdinalIgnoreCase);
    }

    private static void SkipSpaces(ISqlTextSource text, ref int index)
    {
        while (index > 0 && char.IsWhiteSpace(text[index - 1]))
        {
            index--;
        }
    }

    /// <summary>
    /// 識別字可用的字元。
    /// </summary>
    /// <remarks>
    /// 比 <c>SqlTokenizer</c> 那份多收 <c>@</c>：限定字可以是資料表變數（<c>@rows.CopyNo</c>），
    /// 而它讀進來的名稱要能與敘述裡的來源對上。
    /// </remarks>
    private static bool IsNameCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '#' or '@' or '$';
}
