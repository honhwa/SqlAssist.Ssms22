using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Json;
using SqlAssist.Core.Localization;
using Xunit;

namespace SqlAssist.Core.Tests.Localization;

/// <summary>
/// 設定頁的註冊檔實體化：殼層不吃 SqlAssist 的語言設定，只能把鍵先換成字面值。
/// 這裡鎖住「換哪些、不換哪些」與跳脫，因為產物壞掉時 Symptom 是整個設定頁消失，
/// 而那是下一次啟動才看得到的事。
/// </summary>
public sealed class SettingsManifestTextTests
{
    /// <summary>一個只認得兩個鍵的查表；其餘一律回 null，走「保留原本引用」那條路。</summary>
    private static readonly Dictionary<string, string> Table = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["EnabledTitle"] = "啟用",
        ["EnabledDescription"] = "說明",
    };

    private static string Materialize(string manifest, string? note = null) =>
        SettingsManifestText.Materialize(manifest, key => Table.TryGetValue(key, out var text) ? text : null, note);

    [Fact]
    public void 值裡的引用換成該語言的字面值()
    {
        const string Manifest = "{\n  \"title\": \"@EnabledTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\"\n}\n";

        Assert.Equal("{\n  \"title\": \"啟用\"\n}\n", Materialize(Manifest));
    }

    [Fact]
    public void 陣列裡的引用也要換()
    {
        const string Manifest =
            "{\n  \"enumItemLabels\": [\n" +
            "    \"@EnabledTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\",\n" +
            "    \"@EnabledDescription;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\"\n" +
            "  ]\n}\n";

        Assert.Equal("{\n  \"enumItemLabels\": [\n    \"啟用\",\n    \"說明\"\n  ]\n}\n", Materialize(Manifest));
    }

    [Fact]
    public void 整行註解不動()
    {
        // 樣板的檔頭就是這樣寫的；連引用一起改掉，就再也分不出手上哪一份是樣板。
        const string Manifest =
            "// 一律寫成 \"@EnabledTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\" 這種形狀\n" +
            "{\n  \"title\": \"@EnabledTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\"\n}\n";

        var result = Materialize(Manifest);

        Assert.Contains("// 一律寫成 \"@EnabledTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\" 這種形狀", result);
        Assert.Contains("\"title\": \"啟用\"", result);
    }

    [Fact]
    public void 行尾註解留著但那行的值照換()
    {
        const string Manifest =
            "{\n  \"title\": \"@EnabledTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\", // 標題\n}\n";

        Assert.Equal("{\n  \"title\": \"啟用\", // 標題\n}\n", Materialize(Manifest));
    }

    [Fact]
    public void 查不到鍵時保留原本的引用()
    {
        // 查不到就留著引用，殼層還有它自己的後備路徑；換成空字串會安靜地少一段字。
        const string Manifest = "{\n  \"title\": \"@MissingTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\"\n}\n";

        Assert.Equal(Manifest, Materialize(Manifest));
    }

    [Fact]
    public void 引號反斜線與控制字元都會跳脫()
    {
        var manifest = "{\n  \"title\": \"@Odd;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\"\n}\n";
        var text = "說 \"好\" 與 \\ 以及 \t 定位";
        var result = SettingsManifestText.Materialize(
            manifest,
            key => key == "Odd" ? text : null);

        Assert.Equal("{\n  \"title\": \"說 \\\"好\\\" 與 \\\\ 以及 \\t 定位\"\n}\n", result);
    }

    [Fact]
    public void 控制字元以四位十六進位跳脫()
    {
        var manifest = "{\n  \"title\": \"@Odd;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\"\n}\n";

        var result = SettingsManifestText.Materialize(manifest, key => key == "Odd" ? "\u0001" : null);

        Assert.Equal("{\n  \"title\": \"\\u0001\"\n}\n", result);
    }

    [Fact]
    public void 沒有引用也沒有檔頭說明時逐字不變()
    {
        // 內容不變，產物的雜湊才不會每次都變一次；那次就會多寫一次檔、多發一則通知。
        const string Manifest = "{\n  \"default\": true,\n  \"order\": 100\n}\n";

        Assert.Equal(Manifest, Materialize(Manifest));
    }

    [Fact]
    public void 檔頭說明被換掉而本文不動()
    {
        const string Manifest =
            "// 樣板的說明第一行\n// 樣板的說明第二行\n\n{\n  \"title\": \"@EnabledTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\"\n}\n";

        var result = Materialize(Manifest, "// 由 SqlAssist 產生，請勿手改");

        Assert.Equal("// 由 SqlAssist 產生，請勿手改\n{\n  \"title\": \"啟用\"\n}\n", result);
    }

    [Fact]
    public void 換行與行數不變()
    {
        const string Manifest =
            "// 說明\n{\n  \"title\": \"@EnabledTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\",\n" +
            "  \"description\": \"@EnabledDescription;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\"\n}\n";

        var result = Materialize(Manifest);

        Assert.Equal(Manifest.Count(ch => ch == '\n'), result.Count(ch => ch == '\n'));
        Assert.All(result.Split('\n'), line => Assert.DoesNotContain('\r', line));
    }

    [Fact]
    public void 同一個鍵出現幾次就換幾次()
    {
        const string Manifest =
            "{\n  \"title\": \"@EnabledTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\",\n" +
            "  \"other\": \"@EnabledTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\"\n}\n";

        var result = Materialize(Manifest);

        Assert.Equal(2, result.Split(new[] { "\"啟用\"" }, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("@EnabledTitle", result);
    }

    [Fact]
    public void 不同GUID的同名鍵都會換()
    {
        // 換的是鍵名，不是 (鍵, GUID)；同一份檔案引用同一個套件的 GUID 只是現況。
        const string Manifest =
            "{\n  \"a\": \"@EnabledTitle;{b386e18d-f34b-4db4-a40d-b9092a31d89f}\",\n" +
            "  \"b\": \"@EnabledTitle;{9d2e0a3a-8b6e-4f2d-9a1d-7c0a2f5b3e41}\"\n}\n";

        Assert.Equal("{\n  \"a\": \"啟用\",\n  \"b\": \"啟用\"\n}\n", Materialize(Manifest));
    }

    [Fact]
    public void 讀表讀出扁平的鍵與文字()
    {
        var table = SettingsManifestText.ReadTable("{\n  \"EnabledTitle\": \"啟用\",\n  \"EnabledDescription\": \"說明\"\n}\n");

        Assert.Equal(2, table.Count);
        Assert.Equal("啟用", table["EnabledTitle"]);
        Assert.Equal("說明", table["EnabledDescription"]);
    }

    [Fact]
    public void 讀表接受註解與尾隨逗號()
    {
        // .resjson 的寬容：譯者會在鍵旁邊留話，而後面多一個逗號不該讓設定頁的語言整批失效。
        const string Table = "{\n  // 標題\n  \"EnabledTitle\": \"啟用\",\n}\n";

        Assert.Equal("啟用", SettingsManifestText.ReadTable(Table)["EnabledTitle"]);
    }

    [Fact]
    public void 讀表略過不是字串的值()
    {
        // 產生器早就擋掉這種檔；這裡只是不要讓它把整張表拖垮。
        var table = SettingsManifestText.ReadTable("{\n  \"A\": \"1\",\n  \"B\": [ \"2\" ]\n}\n");

        Assert.Equal("1", table["A"]);
        Assert.False(table.ContainsKey("B"));
    }

    [Fact]
    public void 讀表壞掉時直接擲出()
    {
        // 安靜地回一張空表等於整個設定頁退回英文，而那正好是最難察覺的失敗。
        Assert.Throws<JsonParseException>(() => SettingsManifestText.ReadTable("{ 壞掉"));
    }
}
