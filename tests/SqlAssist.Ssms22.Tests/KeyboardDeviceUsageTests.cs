using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SqlAssist.Ssms22.Tests;

public sealed class KeyboardDeviceUsageTests
{
    /// <summary>
    /// 例外一：滑鼠事件上沒有隨事件帶的鍵盤裝置。
    /// </summary>
    /// <remarks>
    /// <c>MouseWheelEventArgs</c> 帶的是滑鼠裝置，所以 Shift＋滾輪只問得到目前的鍵盤狀態。
    /// 這條規則防的是<b>合成的按鍵</b>混進實體鍵盤狀態，而滾輪不會被合成。例外只有這兩個
    /// 名字，要再加第三個得連同這一段一起說得出理由。
    /// </remarks>
    private const string ShiftHeldException = "ShiftHeld";

    /// <summary>
    /// 例外二：清單多選的 Ctrl／Shift+點擊（<c>SqlCardListBase.ModifierSource</c>）。
    /// </summary>
    /// <remarks>
    /// 與滾輪同一個理由：<c>MouseButtonEventArgs</c> 也只帶滑鼠裝置。它另外做成可替換的屬性，
    /// 測試換掉它，所以合成的點擊不會讀到實體鍵盤——這正是這條規則要的。按鍵路徑（空白鍵、
    /// Ctrl+A、Ctrl+C）仍然只讀 <c>KeyEventArgs.KeyboardDevice</c>。
    /// </remarks>
    private const string ModifierSourceException = "ModifierSource";

    [Fact]
    public void 產品碼判斷按鍵狀態只讀事件帶的鍵盤裝置()
    {
        // 靜態 Keyboard 讀執行緒的 Win32 按鍵狀態，會混入實體鍵盤；
        // 測試無法替換，push 時按著 Ctrl 就讓合成的 Enter 變成 Ctrl+Enter。
        var pattern = new Regex(@"\bKeyboard\.(Modifiers|IsKeyDown|IsKeyUp|IsKeyToggled|GetKeyStates)\b");
        var root = FindProductSourceDirectory();
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (path, line, number: index + 1)))
            .Where(entry => pattern.IsMatch(entry.line) && !entry.line.Contains(ShiftHeldException) &&
                !entry.line.Contains(ModifierSourceException))
            .Select(entry => $"{entry.path.Substring(root.Length + 1)}:{entry.number}: {entry.line.Trim()}")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "改用 KeyEventArgs.KeyboardDevice：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 測試合成按鍵事件要傳 <see cref="TestKeyboardDevice"/>，不能傳 <c>Keyboard.PrimaryDevice</c>。
    /// </summary>
    /// <remarks>
    /// 產品碼讀 <c>KeyEventArgs.KeyboardDevice.Modifiers</c>，而 <c>Keyboard.PrimaryDevice</c>
    /// 讀的是執行緒的 Win32 按鍵狀態：實體鍵盤上壓著 Ctrl／Shift／Alt／Win 任一個，合成出來的
    /// Home 就會被當成有修飾鍵、處理常式提前 <c>return</c>，斷言 <c>Handled</c> 的那一條紅掉。
    /// 症狀與行程共用靜態欄位的競態同形（單獨跑、<c>--max-threads 1</c> 都綠，整份跑偶發一條紅），
    /// 但成因不在靜態欄位，加 <c>[Collection]</c> 治不好。實例見
    /// <c>SqlMemoryVisualTests.PreviewMetadataStaysSingleLineAndOnlyItsViewportScrolls</c>。
    /// <para>
    /// 產品碼有一處刻意的例外，所以這條只管測試：<c>SqlSnippetSurroundPicker</c> 把殼層解析掉的
    /// 按鍵重新推回輸入管線時，要的正是實體修飾鍵（Shift＋↑ 的語意由清單自己處理）。
    /// </para>
    /// </remarks>
    [Fact]
    public void 測試合成按鍵事件不讀實體鍵盤()
    {
        var pattern = new Regex(@"\bKeyboard\.PrimaryDevice\b");
        var root = Path.Combine(RepositoryRoot(), "tests");
        var offenders = SourceLines(root)
            .Where(entry => pattern.IsMatch(entry.Line))
            .Select(entry => Describe(root, entry))
            .ToArray();

        Assert.True(offenders.Length == 0,
            "改用 TestKeyboardDevice：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 掃描範圍裡每個 <c>.cs</c> 的每一行。
    /// </summary>
    /// <remarks>
    /// 註解行不算：說明這條規則的文字本來就會提到那幾個名字，把它們算成違規的話，
    /// 唯一能寫下的說明方式就只剩刪掉說明。建置產物（<c>obj</c>／<c>bin</c>）也不掃。
    /// </remarks>
    private static IEnumerable<(string Path, string Line, int Number)> SourceLines(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (Path: path, Line: line, Number: index + 1)))
            .Where(entry => !entry.Line.TrimStart().StartsWith("//"));

    private static bool IsBuildOutput(string path) =>
        path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
        || path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar);

    private static string Describe(string root, (string Path, string Line, int Number) entry) =>
        $"{entry.Path.Substring(root.Length + 1)}:{entry.Number}: {entry.Line.Trim()}";

    private static string RepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "SqlAssist.Ssms22")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("找不到 src/SqlAssist.Ssms22。");
    }
}
