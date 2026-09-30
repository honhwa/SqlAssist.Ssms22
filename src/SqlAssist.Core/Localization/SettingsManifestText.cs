using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SqlAssist.Core.Json;

namespace SqlAssist.Core.Localization;

/// <summary>
/// 把 Unified Settings 註冊檔裡的 <c>"@鍵;{packageGuid}"</c> 換成指定語言的字面值。
/// </summary>
/// <remarks>
/// 設定頁的文字不是 SqlAssist 畫的。殼層讀到 <c>"@鍵;{packageGuid}"</c> 之後，拿**它自己的**
/// 介面語言（順位是 IDE 的 International Settings、OS 的介面語言，最後才退回英文）到套件組件的
/// 資源查表；SqlAssist 的語言設定不在那條鏈上。所以殼層是英文、而套件只出中文衛星時，
/// 整頁永遠是英文，翻譯再齊也一樣。
///
/// <see cref="Materialize"/> 反過來做：先把鍵換成該語言的字面值，殼層沒有鍵可以查，只能照著畫。
/// 殼層在啟動時讀註冊檔，所以寫進去的內容是**下一次**啟動才生效。
///
/// 只換「值」：整行 <c>//</c> 註解原樣跳過——樣板的檔頭就在說明這個機制，順手改掉它反而讓
/// 人分不出手上這一份是樣板還是產物。要換掉檔頭請用 <paramref name="note"/>。
/// </remarks>
public static class SettingsManifestText
{
    /// <summary>
    /// 一個引用值的形狀。鍵與 GUID 的字元集各自收斂，所以檔頭說明裡的
    /// <c>"@鍵;{packageGuid}"</c> 這種示意寫法不會被撈進來。
    /// </summary>
    private static readonly Regex Reference = new Regex(
        "\"@([A-Za-z0-9_]+);\\{[0-9A-Fa-f-]+\\}\"",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// 產生一份把引用換成字面值的註冊檔。
    /// </summary>
    /// <param name="manifest">樣板內容，逐字保留結構、空白與換行。</param>
    /// <param name="lookup">
    /// 依鍵取該語言的文字；回 <c>null</c> 時保留原本的引用，讓殼層照它自己的規則查。
    /// 查不到比整句空白好：留著引用至少還有原本的後備路徑。
    /// </param>
    /// <param name="note">
    /// 取代檔頭說明區塊的註解（每行自帶 <c>//</c>、不含結尾換行）；<c>null</c> 時保留樣板的檔頭。
    /// </param>
    /// <returns>換完的內容。沒有任何引用、也沒有給 <paramref name="note"/> 時與輸入逐字相同。</returns>
    public static string Materialize(string manifest, Func<string, string?> lookup, string? note = null)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (lookup is null) throw new ArgumentNullException(nameof(lookup));

        var body = note is null ? manifest : ReplaceHeader(manifest, note);
        var builder = new StringBuilder(body.Length + (body.Length / 8));
        foreach (var line in Lines(body))
        {
            if (IsComment(line))
            {
                builder.Append(line);
                continue;
            }

            builder.Append(Reference.Replace(line, match =>
            {
                var text = lookup(match.Groups[1].Value);
                return text is null ? match.Value : Quote(text);
            }));
        }

        return builder.ToString();
    }

    /// <summary>讀一份扁平的「鍵 → 文字」表，也就是 <c>.resjson</c> 的形狀。</summary>
    /// <remarks>
    /// 設定頁的 <c>.resjson</c> 除了編成殼層要掃的 <c>.resources</c>，也原樣收成內嵌資源；
    /// 這裡讀的是後者。刻意不從資源組件查表：那要載衛星組件，而探測路徑失效時
    /// <c>ResourceManager</c> 會安靜地退回中性資源——設定頁一直是英文正是這樣來的。
    /// 剖析交給 <see cref="JsonReader"/>，它收 <c>//</c> 註解與尾隨逗號，正是 <c>.resjson</c> 的寬容。
    /// </remarks>
    public static IReadOnlyDictionary<string, string> ReadTable(string table)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));

        var root = JsonReader.Parse(table);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in root.Names)
        {
            var value = root[name];
            if (value.Kind == JsonKind.String)
            {
                entries[name] = value.AsString();
            }
        }

        return entries;
    }

    /// <summary>把檔頭的說明區塊換成 <paramref name="note"/>。</summary>
    /// <remarks>
    /// 檔頭＝開頭連續的空白與 <c>//</c> 註解。只認 <c>//</c>：這份檔案自己就是這樣寫的，
    /// 支援 <c>/* */</c> 要多顧一個狀態，而多出來的那個狀態只為了一個用不到的寫法。
    /// </remarks>
    private static string ReplaceHeader(string manifest, string note) =>
        note + "\n" + manifest.Substring(HeaderEnd(manifest));

    private static int HeaderEnd(string manifest)
    {
        var index = 0;
        while (index < manifest.Length)
        {
            var end = manifest.IndexOf('\n', index);
            var line = end < 0 ? manifest.Substring(index) : manifest.Substring(index, end - index + 1);
            if (!IsComment(line))
            {
                break;
            }

            index = end < 0 ? manifest.Length : end + 1;
        }

        return index;
    }

    /// <summary>這一行有沒有東西要換——只有空白與 <c>//</c> 註解沒有。</summary>
    private static bool IsComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal);
    }

    /// <summary>逐行切開並保留換行，讓沒動到的行連換行都不變。</summary>
    private static IEnumerable<string> Lines(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
            {
                yield return text.Substring(start);
                yield break;
            }

            yield return text.Substring(start, end - start + 1);
            start = end + 1;
        }
    }

    /// <summary>把文字寫成一個 JSON 字串值。控制字元一律跳脫，避免產物在編輯器裡看不出壞掉。</summary>
    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default:
                    if (ch < ' ')
                    {
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
