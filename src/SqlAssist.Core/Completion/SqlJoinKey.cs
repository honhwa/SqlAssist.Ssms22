using System;
using SqlAssist.Core.Settings;

namespace SqlAssist.Core.Completion;

/// <summary>
/// 一個配對好的同名欄位：當前來源的那一個，以及往前配到的對象。
/// </summary>
/// <remarks>
/// 兩側都寫限定字，即使其中一邊的來源沒有別名也一樣是多打幾個字：這個條件的
/// 用途是「把兩個來源接起來」，兩側的欄位名稱必定相同，只寫一邊等於留了一個
/// 裸名給使用者自己補——而裸名在有兩個來源時是模稜兩可的，補錯的那一邊
/// 語法照樣過，接出來的卻是兩份沒有關係的資料。
/// </remarks>
public sealed class SqlJoinKey
{
    public SqlJoinKey(
        string columnName,
        string? qualifier,
        string counterpartName,
        string? counterpartQualifier,
        string? collation = null)
    {
        ColumnName = columnName;
        Qualifier = qualifier;
        CounterpartName = counterpartName;
        CounterpartQualifier = counterpartQualifier;
        Collation = collation;
    }

    /// <summary>當前來源的欄位名稱。</summary>
    public string ColumnName { get; }

    /// <summary>當前來源的限定字；沒有時為 null。</summary>
    public string? Qualifier { get; }

    /// <summary>往前配到的同名欄位。</summary>
    public string CounterpartName { get; }

    /// <summary>往前配到的來源的限定字；沒有時為 null。</summary>
    public string? CounterpartQualifier { get; }

    /// <summary>
    /// 兩側定序不同時要寫進 <c>COLLATE</c> 的定序名稱；相同或有一邊取不到時為 null。
    /// </summary>
    /// <remarks>
    /// 定序名稱不加方括號：<c>COLLATE [Latin1_General_CI_AS]</c> 是語法錯誤，
    /// 它與「插入物件時加上方括號」那個偏好無關。
    /// </remarks>
    public string? Collation { get; }

    /// <summary>整條條件，例如 <c>b.CopyNo = a.CopyNo</c>。</summary>
    public string ComposeInsertionText(SqlAssistSettings settings) =>
        SqlInsertionText.Column(ColumnName, Qualifier, settings) +
        CollateClause(Collation) +
        " = " +
        SqlInsertionText.Column(CounterpartName, CounterpartQualifier, settings);

    /// <summary>
    /// 只寫右半邊，接在已寫好的 <c>b.CopyNo</c> 之後，例如 <c>= a.CopyNo</c>。
    /// </summary>
    /// <remarks>
    /// 要補 <c>COLLATE</c> 時它排在最前面（<c>COLLATE X = a.CopyNo</c>）：
    /// 那一段接在已寫好的欄位名稱後面，位置與整條條件的寫法一致。
    /// </remarks>
    public string ComposeSuffix(SqlAssistSettings settings) =>
        (Collation is null ? string.Empty : "COLLATE " + Collation + " ") +
        "= " +
        SqlInsertionText.Column(CounterpartName, CounterpartQualifier, settings);

    /// <summary>
    /// <c>COLLATE</c> 那一段，含前面的空白；不需要時是空字串。
    /// </summary>
    /// <remarks>
    /// 掛在<b>當前</b>那一欄後面而不是條件的最尾端：那一欄是使用者從清單裡挑的，
    /// 說明欄與插入文字都以它開頭。掛在尾端的話，提交時「文字結尾就是這個名稱」
    /// 那條方括號改寫就認不出欄位（見 <c>SqlInsertionText.BracketName</c>），
    /// 而症狀是使用者自己打了左方括號卻沒有作用。
    ///
    /// 打字那一條（<see cref="SqlJoinCollateAnalyzer"/>）補的也是這一段，兩邊共用同一份寫法。
    /// </remarks>
    public static string CollateClause(string? collation) =>
        collation is null ? string.Empty : " COLLATE " + collation;

    /// <summary>
    /// 兩欄相比時要不要補 <c>COLLATE</c>，要補的話寫哪一個定序。
    /// </summary>
    /// <param name="current">剛決定的那一欄的定序：清單裡挑的那一欄，或剛打完的那一欄。</param>
    /// <param name="counterpart">對面那一欄的定序。</param>
    /// <remarks>
    /// 兩邊都有定序而且不同時才補：只有一邊有定序代表另一邊不是字元型別，
    /// 那樣的比較不會撞上定序衝突，寫了反而是多餘的字。
    ///
    /// 寫的是<b>對面</b>那一欄的定序，掛在當前那一欄後面
    /// （<c>c.CopyNo COLLATE Chinese_Taiwan_Stroke_CI_AS = l.CopyNo</c>）：
    /// 對面那一欄是使用者已經寫好、這條條件要接上的目標，以它的定序為準等於
    /// 「把剛加入的那張表轉過來比」。反過來把對面的定序寫在對面自己身上，
    /// 看起來像是把一欄轉成它原本的定序，讀的人分不出那是必要的還是多打的。
    ///
    /// 不判斷兩張表在不在同一個資料庫：同一個資料庫裡兩欄定序不同，等值比較
    /// 一樣會失敗（錯誤 468），跨庫只是最常見的來源。
    /// </remarks>
    public static string? CollationToApply(string? current, string? counterpart) =>
        current is not null &&
        counterpart is not null &&
        !string.Equals(current, counterpart, StringComparison.OrdinalIgnoreCase)
            ? counterpart
            : null;
}
