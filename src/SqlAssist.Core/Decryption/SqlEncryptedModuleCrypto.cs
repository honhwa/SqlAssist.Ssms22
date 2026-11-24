using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SqlAssist.Core.Decryption;

/// <summary>
/// 讀回 <c>WITH ENCRYPTION</c> 模組本文的算術：造一個已知明文的假定義、從兩份密文還原本文、
/// 並判斷還原出來的字真的是不是模組定義。
/// </summary>
/// <remarks>
/// SQL Server 沒有可以握在手上的金鑰：它把模組文字與一條由（資料庫家族 GUID、object_id、subobject_id）
/// 導出的金流做 XOR。那條金流與文字內容、長度都無關，所以同一個物件的兩份不同定義被同一條金流遮罩：
///
///     plain(i) = cipher_original(i) XOR cipher_dummy(i) XOR plain_dummy(i)
///
/// 第二份密文靠短暫 ALTER 成假定義取得；ALTER 與 ROLLBACK 必須在同一批，見 Ssms22 的解密服務。
/// 直接從家族 GUID 推金流可以避免 ALTER，但位元組配置沒有文件、也無法驗證，猜錯只會得到很像樣的亂碼。
/// 本檔刻意不依賴 SqlClient 與 WPF，讓 Core 測試能直接驗證。
/// </remarks>
internal static class SqlEncryptedModuleCrypto
{
    /// <summary>
    /// 引擎儲存模組文字時用的關鍵字。這是最容易出錯且安靜出錯的細節：Submitted 用 ALTER，
    /// 但被加密、被存起來的是同一個句子的 CREATE 形式。拿 ALTER 去 XOR 只有前五個字對，
    /// 之後全部錯一位，而且仍然回傳字串。
    /// </summary>
    public const string StoredKeyword = "CREATE";

    /// <summary>實際執行用的關鍵字；物件已經存在，所以必須用 ALTER。</summary>
    public const string ExecutedKeyword = "ALTER";

    /// <summary>
    /// 造出假定義的主體（不含開頭關鍵字，第一個字元是空白），讓 ALTER 與 CREATE 共用同一段本文。
    /// 每種物件類型有自己的形狀：ALTER FUNCTION 不能改純量／內聯／多述句型態，ALTER TRIGGER
    /// 必須指回原來那張資料表。回傳 null 表示這種類型不支援，呼叫端應報告而不是猜。
    /// </summary>
    /// <param name="targetTotalLength">完成後的 CREATE 形式我要補到的長度，不是主體長度。</param>
    public static string? BuildDummyBody(
        string objectType,
        string schema,
        string name,
        string? parentSchema,
        string? parentName,
        int targetTotalLength)
    {
        var qualified = Quote(schema, name);

        var body = (objectType ?? "").Trim().ToUpperInvariant() switch
        {
            "P" or "PC" or "RF" => $" PROCEDURE {qualified} WITH ENCRYPTION AS RETURN",
            "V" => $" VIEW {qualified} WITH ENCRYPTION AS SELECT 1 AS c",
            "FN" => $" FUNCTION {qualified}() RETURNS int WITH ENCRYPTION AS BEGIN RETURN 1 END",
            "IF" => $" FUNCTION {qualified}() RETURNS TABLE WITH ENCRYPTION AS RETURN SELECT 1 AS c",
            "TF" => $" FUNCTION {qualified}() RETURNS @r TABLE (c int) WITH ENCRYPTION AS BEGIN RETURN END",
            "TR" when !string.IsNullOrEmpty(parentName) =>
                $" TRIGGER {qualified} ON {Quote(parentSchema!, parentName!)} WITH ENCRYPTION AFTER INSERT AS RETURN",
            _ => null,
        };

        return body is null ? null : Pad(body, Math.Max(0, targetTotalLength - StoredKeyword.Length));
    }

    /// <summary>要執行的敘述：主體套 ALTER，因為物件已存在。</summary>
    public static string? AlterStatement(string? body) => body is null ? null : ExecutedKeyword + body;

    /// <summary>引擎真正儲存的明文：同一段主體套 CREATE；XOR 用的是這一份。</summary>
    public static string? StoredPlaintext(string? body) => body is null ? null : StoredKeyword + body;

    /// <summary>
    /// 用行註解補白；不到三個字元的縫隙沒有地方放註解記號，改用尾端空白。
    /// </summary>
    private static string Pad(string head, int targetLength)
    {
        var pad = targetLength - head.Length;
        if (pad <= 0) return head;
        if (pad < 3) return head + new string(' ', pad);
        return head + " --" + new string('-', pad - 3);
    }

    /// <summary>方括號引用名稱並加倍 <c>]</c>；假定義是動態 SQL，這是擋住名稱注入的唯一的那一道。</summary>
    public static string Quote(string? schema, string name)
    {
        var quotedName = "[" + (name ?? "").Replace("]", "]]") + "]";
        return string.IsNullOrEmpty(schema) ? quotedName : "[" + schema!.Replace("]", "]]") + "]." + quotedName;
    }

    /// <summary>
    /// 從兩份密文與假定義的明文還原本文。兩份密文都是 UTF-16LE，以字元為單位 XOR；
    /// 任一邊不夠長就回 null——部分解密看起來和定義被截斷完全一樣，比不給答案更糟。
    /// </summary>
    public static string? Xor(byte[]? originalCipher, byte[]? dummyCipher, string? dummyPlain)
    {
        if (originalCipher is null || dummyCipher is null || dummyPlain is null) return null;

        var originalChars = originalCipher.Length / 2;
        if (originalChars == 0 || originalChars > 8 * 1024 * 1024) return null;
        if (dummyCipher.Length / 2 < originalChars || dummyPlain.Length < originalChars) return null;

        var chars = new char[originalChars];
        for (var i = 0; i < originalChars; i++)
        {
            var original = originalCipher[i * 2] | (originalCipher[(i * 2) + 1] << 8);
            var dummy = dummyCipher[i * 2] | (dummyCipher[(i * 2) + 1] << 8);
            chars[i] = (char)(original ^ dummy ^ dummyPlain[i]);
        }

        return new string(chars);
    }

    /// <summary>
    /// 還原出來的字是不是模組定義。任何上游假設錯了 XOR 都還是會給字串，
    /// 所以必須要求它略過前導註解後以 CREATE／ALTER 開頭，而且提到它自稱的那個物件。
    /// </summary>
    public static bool LooksLikeModuleDefinition(string? text, string? objectName)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var i = SkipLeadingTrivia(text!);
        if (i >= text!.Length) return false;

        if (!StartsWithKeyword(text, i, "CREATE") && !StartsWithKeyword(text, i, "ALTER")) return false;

        return string.IsNullOrEmpty(objectName)
            || text.IndexOf(objectName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int SkipLeadingTrivia(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]) || text[i] == '\uFEFF')
            {
                i++;
                continue;
            }

            if (text[i] == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                var end = text.IndexOf('\n', i);
                if (end < 0) return text.Length;
                i = end + 1;
                continue;
            }

            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) return text.Length;
                i = end + 2;
                continue;
            }

            break;
        }

        return i;
    }

    private static bool StartsWithKeyword(string text, int start, string keyword)
    {
        if (start + keyword.Length > text.Length) return false;
        if (string.Compare(text, start, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;

        var after = start + keyword.Length;
        return after >= text.Length || (!char.IsLetterOrDigit(text[after]) && text[after] != '_');
    }

    private static readonly Regex LeadingAlter = new(@"^(\s*)ALTER\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 把開頭的 ALTER 改回 CREATE。解密拿到的是最後一次提交的形狀，而 F12／預覽的排版層
    /// 會自己把 CREATE 改成 ALTER；若這裡留著 ALTER，排版層不會改，內容仍可用但來源不一致。
    /// 前導註解後面的 ALTER 刻意不碰：看不看得懂由排版層決定，不猜。
    /// </summary>
    public static string? NormalizeToCreate(string? definition) =>
        string.IsNullOrEmpty(definition) ? definition : LeadingAlter.Replace(definition!, "$1CREATE", 1);

    /// <summary>強制 CRLF，與本人的定義指令碼排版一致。</summary>
    public static string? NormalizeLineEndings(string? text) =>
        text is null ? null : text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

    /// <summary>把同一個物件的 <c>imageval</c> 區塊依 <c>valnum</c> 順序接起來。</summary>
    public static byte[]? Concat(IReadOnlyList<byte[]>? chunks)
    {
        if (chunks is null || chunks.Count == 0) return null;
        if (chunks.Count == 1) return chunks[0];

        var total = 0;
        foreach (var chunk in chunks) total += chunk?.Length ?? 0;

        var result = new byte[total];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            if (chunk is null || chunk.Length == 0) continue;
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }

        return result;
    }
}
