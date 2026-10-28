using System;
using System.ComponentModel;
using System.Diagnostics;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace SqlAssist.Ssms22.Editor;

/// <summary>「重新整理快取」在 SSMS 內建那一半的結果。</summary>
internal enum SqlIntellisenseRefreshResult
{
    /// <summary>SSMS 已經重新讀取物件清單。</summary>
    Refreshed,

    /// <summary>這個 SSMS 版本找不到那顆命令，這一輪什麼都沒做。</summary>
    Unavailable,

    /// <summary>命令找到了，執行時失敗。</summary>
    Failed,
}

/// <summary>
/// 要求 SSMS 重新整理內建 IntelliSense 的物件快取。
/// </summary>
/// <remarks>
/// 紅色錯誤波浪線畫的是 SSMS 內建 IntelliSense 的判斷，而它讀的是連線當下抓進來的那份
/// 物件清單：連線之後才建的資料表、檢視與資料表函式，在它眼裡一律「名稱無效」。
/// 清掉 SqlAssist 自己的分層快取（<see cref="Connections.SqlMetadataService.InvalidateAll"/>）
/// 對它沒有影響，這也是為什麼選單上需要第二顆命令。
///
/// 作法只有一條路：找到殼層那顆「重新整理本機快取」命令再執行它。那個服務沒有公開 API
/// （<c>RadLangSvc</c> 的成員一律是組件內部），而寫死 GUID＋命令碼會在 SSMS 換版時
/// 安靜失效——所以先試幾個慣例名稱，沒命中才列舉命令表，兩者都不硬編識別碼。
/// 找不到就回報 <see cref="SqlIntellisenseRefreshResult.Unavailable"/>，讓呼叫端
/// 誠實地說明波浪線可能還在，而不是假裝成功。
/// </remarks>
internal static class SqlIntellisenseCache
{
    private static readonly object Gate = new();

    /// <summary>找到的命令名稱；<c>null</c> 代表找過但沒有。</summary>
    private static string? _commandName;

    /// <summary>是否已經找過。</summary>
    /// <remarks>
    /// 一個行程只找一次：列舉命令表要掃過幾千顆命令，而結果在套件存活的期間不會變。
    /// 找錯的代價只有一次——SSMS 換版是新行程，下一次啟動就會重找。
    /// </remarks>
    private static bool _resolved;

    /// <summary>重新整理原生快取；必須在 UI 執行緒上呼叫。</summary>
    public static SqlIntellisenseRefreshResult Refresh(IServiceProvider serviceProvider)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // DTE 由殼層註冊與釋放，不是我們建立的物件，用完不必也不該收。
        if (serviceProvider.GetService(typeof(DTE)) is not DTE dte)
        {
            SqlAssistDiagnostics.WriteAlways("重新整理 IntelliSense 快取：取不到 DTE，這一輪沒有動作");
            return SqlIntellisenseRefreshResult.Unavailable;
        }

        var name = Resolve(dte);

        if (name is null)
        {
            return SqlIntellisenseRefreshResult.Unavailable;
        }

        return Execute(dte, name)
            ? SqlIntellisenseRefreshResult.Refreshed
            : SqlIntellisenseRefreshResult.Failed;
    }

    private static string? Resolve(DTE dte)
    {
        lock (Gate)
        {
            if (_resolved)
            {
                return _commandName;
            }

            _resolved = true;

            foreach (var candidate in SqlIntellisenseCacheNames.Candidates)
            {
                if (!Exists(dte, candidate))
                {
                    continue;
                }

                SqlAssistDiagnostics.WriteAlways($"重新整理 IntelliSense 快取：使用命令 {candidate}");
                return _commandName = candidate;
            }

            _commandName = Search(dte);

            SqlAssistDiagnostics.WriteAlways(_commandName is null
                ? "重新整理 IntelliSense 快取：這個 SSMS 版本找不到那顆命令"
                : $"重新整理 IntelliSense 快取：列舉命令表找到 {_commandName}");

            return _commandName;
        }
    }

    /// <remarks>
    /// 走 <c>Probe</c> 而不是先執行看看：候選名單本來就允許猜錯，猜錯是預期內的事，
    /// 記完整堆疊只會把真正的錯誤埋掉。真正執行的是命中的那一個，它的失敗由
    /// <see cref="Execute"/> 記錄。
    /// </remarks>
    private static bool Exists(DTE dte, [Localizable(false)] string name) =>
        SqlAssistPlatformGuard.Probe($"查詢殼層命令 {name}", () =>
        {
            dte.Commands.Item(name, -1);
            return true;
        }, fallback: false);

    /// <summary>整份命令表掃一遍，找名字符合判準的那一顆。</summary>
    /// <remarks>
    /// 只在候選名單沒命中時才走這裡。掃描成本與命令數量成正比，因此照專案的門檻
    /// 記一筆耗時——它會出現在紀錄檔裡，也就有辦法判斷是不是該改成更窄的比對。
    /// </remarks>
    private static string? Search(DTE dte)
    {
        var watch = Stopwatch.StartNew();

        var found = SqlAssistPlatformGuard.Run("列舉殼層命令", () =>
        {
            foreach (Command command in dte.Commands)
            {
                if (SqlIntellisenseCacheNames.Matches(command.Name))
                {
                    return command.Name;
                }
            }

            return (string?)null;
        }, fallback: null);

        if (watch.ElapsedMilliseconds > 200)
        {
            SqlAssistDiagnostics.Write($"耗時 {watch.ElapsedMilliseconds} ms：列舉殼層命令");
        }

        return found;
    }

    private static bool Execute(DTE dte, string name) =>
        SqlAssistPlatformGuard.Run($"執行殼層命令 {name}", () =>
        {
            dte.ExecuteCommand(name, string.Empty);
            return true;
        }, fallback: false);
}
