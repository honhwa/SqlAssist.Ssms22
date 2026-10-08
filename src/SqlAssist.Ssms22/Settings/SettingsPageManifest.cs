using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SqlAssist.Core.Json;
using SqlAssist.Core.Localization;
using SqlAssist.Core.Notifications;

namespace SqlAssist.Ssms22.Settings;

/// <summary>
/// 把 Unified Settings 註冊檔換成來源語言（繁體中文）的字面值，並更新 pkgdef 的快取鍵。
/// </summary>
/// <remarks>
/// 為什麼非得這樣做見 <see cref="SettingsManifestText"/>。這裡補的是殼層之外的三件事：
/// 取得樣板（內嵌資源，與版控那一份同源）、取得來源語言的文字、以及讓殼層知道內容換了。
///
/// **設定頁不提供在地化**：殼層只拿**它自己的**介面語言查表，SqlAssist 的介面語言設定碰不到
/// 那條鏈，每個語言都得再賭一次探測路徑。與其再賭，不如一律換成來源語言的字面值——殼層
/// 沒有鍵可以查，只能照著畫，設定頁於是固定顯示繁體中文，不隨介面語言設定改變。
///
/// 文字讀的是內嵌的 <c>SettingsPageText.zh-Hant.resjson</c> 原文，不走資源組件：
/// 那要載衛星組件，而探測路徑失效時 <c>ResourceManager</c> 只會安靜地退回中性資源——
/// 設定頁一直是英文正是這樣來的。殼層用的 .resources 與衛星組件照舊，只是再也查不到鍵。
///
/// 殼層用 <c>CacheTag</c> 當定義快取的鍵——它自己的
/// <c>%LOCALAPPDATA%\Microsoft\SSMS\&lt;版&gt;\UnifiedSettings\DefinitionCache.dat</c> 裡，
/// 標籤就緊接著註冊檔的路徑。而套件在殼層讀完註冊檔之後才載入，所以這裡寫的是**下一次**
/// 啟動用的內容；內容換了必須告訴使用者重新啟動。
///
/// 標籤取產物的內容雜湊而不是時戳：內容一樣就不寫檔、也不發通知，重複啟動幾次都一樣。
/// 換版重新部署會把註冊檔還原成樣板，那時內容真的變了，通知就是對的。
/// </remarks>
internal static class SettingsPageManifest
{
    private const string ManifestName = "SqlAssist.registration.json";
    private const string DefinitionName = "SqlAssist.Ssms22.pkgdef";

    /// <summary>
    /// 設定頁文字表的內嵌資源名；由 SettingsPageText.targets 的 LogicalName 決定，
    /// <c>{0}</c> 是 <see cref="SqlLanguage.Name"/>。
    /// </summary>
    private const string TableNameFormat = "SqlAssist.Ssms22.Settings.SettingsPageText.{0}.resjson";

    /// <summary>樣板資源名；由專案檔的 EmbeddedResource LogicalName 決定。</summary>
    private const string TemplateResource = "SqlAssist.Ssms22.SqlAssist.registration.json";

    private const string CacheTagMarker = "\"CacheTag\"=qword:";

    private static readonly Regex CacheTagPattern = new Regex(
        "\"CacheTag\"=qword:([0-9A-Fa-f]+)",
        RegexOptions.CultureInvariant);

    private static readonly object Gate = new object();

    private static string? s_tag;

    /// <summary>讓註冊檔在下一次啟動時以來源語言（繁體中文）顯示。</summary>
    /// <remarks>內容已經正確時什麼都不做，所以啟動時無條件呼叫是安全的。</remarks>
    internal static void Apply(NotificationOrigin origin)
    {
        SqlAssistPlatformGuard.Run("套用設定頁的語言", () => Rewrite(origin));
    }

    private static void Rewrite(NotificationOrigin origin)
    {
        // 設定頁固定用來源語言，不隨介面語言設定改變；理由見類別註解。
        var language = SqlLanguage.Source;
        var folder = Path.GetDirectoryName(typeof(SettingsPageManifest).Assembly.Location);
        var manifestPath = folder is null ? null : Path.Combine(folder, ManifestName);
        var definitionPath = folder is null ? null : Path.Combine(folder, DefinitionName);
        if (manifestPath is null || definitionPath is null
            || !File.Exists(manifestPath) || !File.Exists(definitionPath))
        {
            SqlAssistDiagnostics.WriteAlways("找不到設定頁的註冊檔或 pkgdef，設定頁語言未套用");
            return;
        }

        var definition = File.ReadAllText(definitionPath);
        var current = CacheTagPattern.Match(definition);
        if (!current.Success)
        {
            SqlAssistDiagnostics.WriteAlways("pkgdef 沒有 CacheTag，設定頁語言未套用");
            return;
        }

        var currentTag = current.Groups[1].Value;

        // 這條路在套件載入與每一個查詢視窗開啟時都會走到，不值得每次都材料化一份 50 KB 的
        // 註冊檔。pkgdef 的標籤正是上一次寫下去的那一個，就表示產物還是對的。
        lock (Gate)
        {
            if (string.Equals(currentTag, s_tag, StringComparison.Ordinal))
            {
                return;
            }
        }

        if (Template() is not { } template || Table(language) is not { } table)
        {
            return;
        }

        var materialized = SettingsManifestText.Materialize(
            template,
            key => table.TryGetValue(key, out var text) ? text : null,
            Note(language));

        // 先驗證再落地：寫壞的註冊檔會讓設定頁整頁消失，而那是下一次啟動才看得出來的事。
        JsonReader.Parse(materialized);

        var tag = CacheTagOf(materialized);
        if (!string.Equals(currentTag, tag, StringComparison.Ordinal))
        {
            Write(manifestPath, materialized, new UTF8Encoding(false));
            Write(
                definitionPath,
                definition.Replace(CacheTagMarker + currentTag, CacheTagMarker + tag),
                EncodingOf(definitionPath));

            SqlAssistDiagnostics.WriteAlways("設定頁文字已排定為 " + language.Name + "；重新啟動 SSMS 後生效");
            NotificationCenter.Default.Post(
                NotificationCatalog.SettingsPageLanguagePending,
                NotificationKind.Settings,
                origin,
                NotificationLevel.Notice,
                NotificationStatus.Succeeded,
                message: NotificationCatalog.SettingsPageLanguageRestartNotice);
        }

        lock (Gate)
        {
            s_tag = tag;
        }
    }

    /// <summary>取代樣板檔頭那段說明的註解：產物是給人看的，要一眼看出它是產物。</summary>
    [Localizable(false)]
    private static string Note(SqlLanguage language) =>
        "// 這一份是 SqlAssist 產生的：顯示文字固定換成來源語言（" + language.Name + "）的字面值，\n" +
        "// 因為殼層只用它自己的介面語言去查 \"@鍵;{packageGuid}\"，換不到 SqlAssist 的語言設定。\n" +
        "// 內容摘自 src/SqlAssist.Ssms22/SqlAssist.registration.json；\n" +
        "// 換版部署或來源文字改動後會重新產生，請勿手改。";

    /// <summary>讀出版控那一份樣板；它與部署到安裝資料夾的那一份同源。</summary>
    private static string? Template()
    {
        using var stream = typeof(SettingsPageManifest).Assembly.GetManifestResourceStream(TemplateResource);
        if (stream is null)
        {
            SqlAssistDiagnostics.WriteAlways("組件缺少設定頁的註冊樣板，設定頁語言未套用");
            return null;
        }

        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>讀出該語言的設定頁文字表。</summary>
    private static IReadOnlyDictionary<string, string>? Table(SqlLanguage language)
    {
        var name = string.Format(CultureInfo.InvariantCulture, TableNameFormat, language.Name);
        using var stream = typeof(SettingsPageManifest).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            SqlAssistDiagnostics.WriteAlways("組件缺少設定頁的 " + language.Name + " 文字，設定頁語言未套用");
            return null;
        }

        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return SettingsManifestText.ReadTable(reader.ReadToEnd());
    }

    /// <summary>產物的內容雜湊，寫成 pkgdef 那種大寫十六進位（FNV-1a 64）。</summary>
    private static string CacheTagOf(string content)
    {
        ulong hash = 14695981039346656037UL;
        foreach (var value in new UTF8Encoding(false).GetBytes(content))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        return hash.ToString("X16", CultureInfo.InvariantCulture);
    }

    /// <summary>pkgdef 由 VS 以 UTF-16LE 加 BOM 產生；寫回去要維持同一種編碼。</summary>
    private static Encoding EncodingOf(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[3];
        var read = stream.Read(head, 0, head.Length);
        if (read >= 2 && head[0] == 0xFF && head[1] == 0xFE)
        {
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        }

        if (read >= 2 && head[0] == 0xFE && head[1] == 0xFF)
        {
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        }

        return new UTF8Encoding(read >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF);
    }

    /// <summary>先寫暫存再換掉：設定頁的註冊檔只被讀一次，但那一次讀到半截就整頁消失。</summary>
    private static void Write(string path, string content, Encoding encoding)
    {
        var temp = path + ".new";
        File.WriteAllText(temp, content, encoding);
        try
        {
            File.Replace(temp, path, destinationBackupFileName: null);
        }
        catch (IOException)
        {
            // 換不掉就退回覆寫。暫存檔一定要收掉，否則安裝資料夾會多一個沒人認得的檔案。
            File.Copy(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
