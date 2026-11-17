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
        Collate() +
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
    /// </remarks>
    private string Collate() => Collation is null ? string.Empty : " COLLATE " + Collation;
}
