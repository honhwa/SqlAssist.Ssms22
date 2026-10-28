using System;
using System.Collections.Generic;

namespace SqlAssist.Ssms22.Editor;

/// <summary>
/// SSMS 內建 IntelliSense「重新整理本機快取」那顆命令的名字判準。
/// </summary>
/// <remarks>
/// 為什麼要靠名字找：那個服務沒有公開 API。<c>RadLangSvc</c> 的
/// <c>RefreshLocalCache</c> 與 <c>RefreshIntellisense</c> 都是組件內部成員，
/// 命令表也只活在執行期，所以殼層的命令清單是唯一不必硬編 GUID 的入口。
///
/// 比對的是命令表宣告的<b>正式名稱</b>（「選項 → 環境 → 鍵盤」上打得到的那一個），
/// 不是選單上看見的那一句——後者跟著介面語言變，拿它比對等於把中文或英文寫進程式。
///
/// 判準與列舉分開：這裡沒有殼層相依，可以在單元測試裡驗；真的去問 DTE 的留在
/// <see cref="SqlIntellisenseCache"/>。
/// </remarks>
internal static class SqlIntellisenseCacheNames
{
    /// <summary>命令名稱裡出現這一段就是它。</summary>
    private const string CacheSuffix = "RefreshLocalCache";

    /// <summary>RadLangSvc 另一支同樣意思的方法名，命令若改叫這個名字也在守備範圍。</summary>
    private const string IntellisenseSuffix = "RefreshIntellisense";

    /// <summary>
    /// 先試的名字：命中時不必列舉整份命令表，按下去幾乎不花時間。
    /// </summary>
    /// <remarks>
    /// 候選只涵蓋「同一顆命令掛在不同命令集」的幾種寫法，不是猜謎——列舉那一條路
    /// 才是真的答案來源，這裡只是省掉一次幾百毫秒到數秒的掃描。四個前綴分別是
    /// 殼層編輯器、查詢視窗、SQL 工作台與 IntelliSense 子選單的慣例。
    /// </remarks>
    public static IReadOnlyList<string> Candidates { get; } = new[]
    {
        "Edit.RefreshLocalCache",
        "Edit.IntelliSense.RefreshLocalCache",
        "Query.RefreshLocalCache",
        "SqlWorkbench.RefreshLocalCache",
        "Edit.RefreshIntellisense",
        "Query.RefreshIntellisense",
    };

    /// <summary>這個名字看起來是不是那顆命令。</summary>
    /// <remarks>
    /// 用「包含」而不是「結尾等於」：命令集的段數由發行者決定，多一段前綴不影響
    /// 它是不是同一顆命令。名字裡同時帶這兩段字的名稱沒有第二種意思。
    /// </remarks>
    public static bool Matches(string? name) =>
        name is not null
        && (name.IndexOf(CacheSuffix, StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf(IntellisenseSuffix, StringComparison.OrdinalIgnoreCase) >= 0);
}
