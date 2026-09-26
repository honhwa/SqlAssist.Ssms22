using System;
using System.Collections.Generic;

namespace SqlAssist.Core.Pairing;

/// <summary>
/// 可以包住一段內容的區塊骨架。
/// </summary>
/// <remarks>
/// 與 <see cref="SqlDelimiterPair"/> 的差別在<b>什麼時候決定</b>：單字元配對是
/// 「按下去就補上另一半」，而那一個字元本身已經說明了是哪一組；區塊骨架要先知道
/// 使用者打的是 <c>BEGIN</c> 還是 <c>BEGIN TRY</c>，那要等字元進了緩衝區才看得出來。
///
/// 兩端都是清單而非單一字串：<c>TRY</c>／<c>CATCH</c> 的結尾是三行
/// （<c>END TRY</c>／<c>BEGIN CATCH</c>／<c>END CATCH</c>）。
/// </remarks>
public sealed class SqlBlockCloser
{
    private SqlBlockCloser(string keyword, string[] opening, string[] closing)
    {
        Keyword = keyword;
        Opening = Array.AsReadOnly(opening);
        Closing = Array.AsReadOnly(closing);
    }

    /// <summary>觸發這個骨架的關鍵字，大寫。</summary>
    /// <remarks>
    /// 比對時不分大小寫，詞之間的空白幾個都算，但存的是大寫的單一空白版本——
    /// 診斷訊息與提示都用這一份，不另外記使用者當時打的是什麼。
    ///
    /// 因為空白數不拘，這個字串的<b>長度不能用來回推起點</b>；
    /// 要知道那一段從哪裡開始，用 <see cref="SqlBlockMatch.Start"/>。
    /// </remarks>
    public string Keyword { get; }

    /// <summary>放在內容前面的那幾行。</summary>
    public IReadOnlyList<string> Opening { get; }

    /// <summary>放在內容後面的那幾行。</summary>
    public IReadOnlyList<string> Closing { get; }

    /// <summary><c>BEGIN</c>…<c>END</c>。</summary>
    public static SqlBlockCloser Block { get; } =
        new("BEGIN", new[] { "BEGIN" }, new[] { "END" });

    /// <summary><c>BEGIN TRY</c>…<c>END TRY</c> 接 <c>BEGIN CATCH</c>…<c>END CATCH</c>。</summary>
    /// <remarks>
    /// 四行一次到齊，而不是先給 <c>TRY</c> 再讓使用者自己補 <c>CATCH</c>：
    /// 分兩次補的話，中間那段時間緩衝區裡是一句語法錯誤——<c>BEGIN TRY</c> 少了
    /// <c>END TRY</c>，查詢視窗會把整段標紅。一次補完就不會經過那個狀態，
    /// 反正 <c>CATCH</c> 幾乎總是跟著 <c>TRY</c> 出現。
    ///
    /// <c>CATCH</c> 裡不預先放 <c>THROW;</c>：那是錯誤處理的<b>策略</b>，
    /// 而這裡只負責語法的骨架。需要它的人走[程式碼片段](snippets.md)那一條路。
    /// </remarks>
    public static SqlBlockCloser TryCatch { get; } =
        new("BEGIN TRY", new[] { "BEGIN TRY" }, new[] { "END TRY", "BEGIN CATCH", "END CATCH" });
}

/// <summary>
/// 認出來的區塊開頭：哪一組骨架，以及它從哪一個位置開始。
/// </summary>
/// <remarks>
/// 位置要一起回傳，因為關鍵字裡的空白間距不拘（<c>BEGIN   TRY</c> 也算），
/// 於是「游標位置減關鍵字長度」推不出起點。呼叫端要拿它去問語彙狀態、
/// 也要拿它決定取代範圍從哪一行起算。
/// </remarks>
public readonly struct SqlBlockMatch
{
    internal SqlBlockMatch(SqlBlockCloser closer, int start)
    {
        Closer = closer;
        Start = start;
    }

    /// <summary>要用的骨架。</summary>
    public SqlBlockCloser Closer { get; }

    /// <summary>關鍵字第一個字元在緩衝區裡的位置。</summary>
    public int Start { get; }
}
