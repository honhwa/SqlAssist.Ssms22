using System;
using System.Collections.Generic;
using System.Text;

namespace SqlAssist.Metadata.ResultGrid;

/// <summary>
/// 貼上的一欄值要寫成什麼形狀。
/// </summary>
/// <remarks>
/// 兩個入口共用同一份排版，差別只在頭尾那兩個符號。各寫一份症狀是兩邊的縮排
/// 與逗號規則慢慢分岔，而那不會報錯，只會讓同一份清單在兩個入口長得不一樣。
/// </remarks>
public enum SqlPasteShape
{
    /// <summary>含 <c>IN</c> 與括號，貼上去自成一段述詞。</summary>
    InPredicate,

    /// <summary>
    /// 只有值本身。
    /// </summary>
    /// <remarks>
    /// 給「<c>IN (</c> 已經打好、游標停在括號裡」那個位置用，所以前後各留一個
    /// 換行與一層內縮：插進去之後游標本來就有的 <c>)</c> 會落在述詞那一行的縮排上，
    /// 使用者不必再自己排版。
    /// </remarks>
    ValuesOnly,
}

/// <summary>
/// 把剪貼簿上的一欄值寫成 <c>IN</c> 條件或值清單。<b>一行一個值。</b>
/// </summary>
/// <remarks>
/// 與 <see cref="SqlInPredicateScript"/> 是同一件事的兩個方向：那一支把結果格線的值
/// 複製出去，這一支把別人給的一欄值貼進來。字面值的寫法兩者共用
/// <see cref="SqlValueLiteral"/>，不再各跳脫一次單引號。
///
/// 四條規則，每一條背後都是一種「跑得動而答案是錯的」：
///
/// 一、<b>純十進位才不加引號。</b><c>007</c> 這種前導零的編號少了引號就變成 7；
/// <c>2024-03-04</c> 少了引號會被當成減法，算出來是 2017。兩者都不報錯，
/// 只是永遠比不到那一列。
///
/// 二、<b>文字加 <c>N</c> 前綴。</b>多一個 <c>N</c> 插進 <c>varchar</c> 只是一次
/// 隱含轉換；少一個 <c>N</c> 插進 <c>nvarchar</c> 是把非拉丁字元換成問號，
/// 沒有錯誤訊息。與 <see cref="SqlValueLiteral"/> 同一條。
///
/// 三、<b>多欄就不做。</b>剪貼簿在畫面上看不出是幾欄，取第一欄會安靜地丟掉其餘
/// 欄位的值——那正是使用者以為貼上去了、其實沒有的那一種。
///
/// 四、<b>字面 <c>NULL</c> 當字串。</b>貼上來的文字沒有欄位型別，
/// <c>NULL</c> 這四個字分不出「真的 NULL」與「內容剛好是 NULL 的字串」
/// （SSMS 自己的複製就是這樣，實測記在文件「結果格線的值與輸出」的
/// 「為什麼不走剪貼簿」那一節）。當字串至少是一筆比得到的值；當成關鍵字的話
/// <c>x IN (NULL)</c> 恆為 UNKNOWN，條件永遠不成立，而畫面上完全看不出來。
/// 真的要比 <c>NULL</c> 得自己寫 <c>IS NULL</c>。
/// </remarks>
public static class SqlPastedValueList
{
    /// <summary>整份內容都是空白時要說的那一句。</summary>
    private const string EmptyFailure = "剪貼簿裡沒有可用的值。";

    /// <summary>偵測到多欄時要說的那一句。</summary>
    private const string MulticolumnFailure =
        "剪貼簿內容是多欄（Tab 分隔）的表格；請只複製單一欄再試一次。";

    /// <summary>
    /// 把剪貼簿的文字拆成一行一個字面值。
    /// </summary>
    /// <param name="text">剪貼簿上的純文字。</param>
    /// <param name="literals">
    /// 成功時是每個值的 T-SQL 字面值，順序與內容都照原文；失敗時是空的。
    /// </param>
    /// <param name="failure">失敗時要顯示給使用者的原因；成功時是空字串。</param>
    /// <remarks>
    /// 空行直接跳過——複製一整欄時頭尾多一個空行是常態，而在 <c>IN</c> 清單裡
    /// 多的那一個逗號是語法錯誤。行內前後的空白（含 Tab）一併去掉：
    /// 那些是儲存格與欄位之間的排版，不是值的一部分。其餘空白字元原樣留著，
    /// 不去猜哪些「看起來像」空白。
    /// </remarks>
    public static bool TryRead(string? text, out IReadOnlyList<string> literals, out string failure)
    {
        var result = new List<string>();

        // 寫成 `text is null || text.Length == 0` 而不是 IsNullOrEmpty：netstandard2.0 的
        // 參考組件沒有那個 NotNullWhen 標註，分析器因此不會把 text 收窄成非 null。
        if (text is null || text.Length == 0)
        {
            literals = result;
            failure = EmptyFailure;
            return false;
        }

        var start = 0;

        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && text[index] != '\n' && text[index] != '\r')
            {
                continue;
            }

            // 空行連子字串都不必切；`index == start` 就是空行。
            if (index > start && !TryReadLine(text.Substring(start, index - start), result, out failure))
            {
                literals = Array.Empty<string>();
                return false;
            }

            // CRLF 算一次換行；只認 \r 或只認 \n 的來源也照樣切得開。
            if (index < text.Length && text[index] == '\r' &&
                index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }

            start = index + 1;
        }

        if (result.Count == 0)
        {
            literals = result;
            failure = EmptyFailure;
            return false;
        }

        literals = result;
        failure = string.Empty;
        return true;
    }

    /// <summary>
    /// 把值清單排成要插進編輯器的文字。
    /// </summary>
    /// <param name="literals"><see cref="TryRead"/> 產出的字面值，至少一個。</param>
    /// <param name="shape">含不含 <c>IN</c> 與括號。</param>
    /// <param name="indent">游標那一行的前導空白；端點對齊它。</param>
    /// <param name="unit">一層縮排；值比端點再深一層。</param>
    /// <param name="newLine">要用的換行（依檔案現況，不依設定）。</param>
    public static string Build(
        IReadOnlyList<string> literals,
        SqlPasteShape shape,
        string indent,
        string unit,
        string newLine)
    {
        if (literals is null)
        {
            throw new ArgumentNullException(nameof(literals));
        }

        if (indent is null)
        {
            throw new ArgumentNullException(nameof(indent));
        }

        if (unit is null)
        {
            throw new ArgumentNullException(nameof(unit));
        }

        if (newLine is null)
        {
            throw new ArgumentNullException(nameof(newLine));
        }

        if (literals.Count == 0)
        {
            throw new ArgumentException("至少要有一個值。", nameof(literals));
        }

        var inner = indent + unit;
        var builder = new StringBuilder(
            (inner.Length + newLine.Length + 8) * literals.Count + indent.Length + 16);

        if (shape == SqlPasteShape.InPredicate)
        {
            builder.Append("IN (");
        }

        for (var index = 0; index < literals.Count; index++)
        {
            builder.Append(newLine).Append(inner).Append(literals[index]);

            // 最後一個不加逗號：T-SQL 不接受右括號前面多出來的那一個。
            if (index + 1 < literals.Count)
            {
                builder.Append(',');
            }
        }

        builder.Append(newLine).Append(indent);

        if (shape == SqlPasteShape.InPredicate)
        {
            builder.Append(')');
        }

        return builder.ToString();
    }

    /// <summary>處理一行的內容；多欄或整行空白時回 false 或直接把值吃掉。</summary>
    private static bool TryReadLine(string line, List<string> literals, out string failure)
    {
        failure = string.Empty;

        var cells = line.Split('\t');
        var value = Trim(cells[0]);

        for (var index = 1; index < cells.Length; index++)
        {
            if (Trim(cells[index]).Length > 0)
            {
                failure = MulticolumnFailure;
                return false;
            }
        }

        if (value.Length > 0)
        {
            literals.Add(IsPlainNumber(value) ? value : SqlValueLiteral.Text(value));
        }

        return true;
    }

    /// <summary>只去掉空格與定位字元。</summary>
    /// <remarks>
    /// 不用 <see cref="string.Trim()"/>：它連不分行空格與其他 Unicode 空白一起吃掉，
    /// 而那些有可能是值的一部分——資料庫裡的字串本來就什麼都可能含。
    /// </remarks>
    private static string Trim(string text) => text.Trim(' ', '\t');

    /// <summary>
    /// 這串文字能不能直接當數值字面值寫出去。
    /// </summary>
    /// <remarks>
    /// 可帶正負號與一個小數點，其餘一律當字串。收窄掉的都是「不加引號會安靜地
    /// 換一個值」的形狀：
    ///
    /// <c>007</c>、<c>0912</c> 這種前導零的編號不加引號就變成 7 與 912，
    /// 條件永遠比不到；<c>2024-03-04</c> 是減法；<c>1e5</c> 與 <c>0x1F</c> 雖然
    /// 是合法的數值字面值，但來源幾乎不會是數字欄位，而猜錯時代價是整段條件
    /// 貼上去執行得動、只是比不到東西。加引號的那一邊最多是多一次隱含轉換。
    ///
    /// 用 <c>'0'</c> 到 <c>'9'</c> 而不是 <c>char.IsDigit</c>：後者對全形與
    /// 阿拉伯－印度數字也回 true，那些不是 T-SQL 的數值字面值。
    /// </remarks>
    private static bool IsPlainNumber(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var index = 0;

        if (value[0] == '+' || value[0] == '-')
        {
            if (value.Length == 1)
            {
                return false;
            }

            index++;
        }

        var digitsStart = index;
        var integerDigits = 0;

        while (index < value.Length && value[index] >= '0' && value[index] <= '9')
        {
            integerDigits++;
            index++;
        }

        if (integerDigits == 0)
        {
            return false;
        }

        // 前導零：只有 "0" 本身與 "0.…" 可以，其餘都是編號。
        if (integerDigits > 1 && value[digitsStart] == '0')
        {
            return false;
        }

        if (index == value.Length)
        {
            return true;
        }

        if (value[index] != '.')
        {
            return false;
        }

        index++;
        var fractionDigits = 0;

        while (index < value.Length && value[index] >= '0' && value[index] <= '9')
        {
            fractionDigits++;
            index++;
        }

        // "1." 這種寫法留給字串：小數點後面什麼都沒有，多半是打錯或截斷。
        return fractionDigits > 0 && index == value.Length;
    }
}
