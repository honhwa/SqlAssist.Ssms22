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
        string? counterpartQualifier)
    {
        ColumnName = columnName;
        Qualifier = qualifier;
        CounterpartName = counterpartName;
        CounterpartQualifier = counterpartQualifier;
    }

    /// <summary>當前來源的欄位名稱。</summary>
    public string ColumnName { get; }

    /// <summary>當前來源的限定字；沒有時為 null。</summary>
    public string? Qualifier { get; }

    /// <summary>往前配到的同名欄位。</summary>
    public string CounterpartName { get; }

    /// <summary>往前配到的來源的限定字；沒有時為 null。</summary>
    public string? CounterpartQualifier { get; }

    /// <summary>整條條件，例如 <c>b.CopyNo = a.CopyNo</c>。</summary>
    public string ComposeInsertionText(SqlAssistSettings settings) =>
        SqlInsertionText.Qualify(ColumnName, Qualifier, settings) +
        " = " +
        SqlInsertionText.Qualify(CounterpartName, CounterpartQualifier, settings);

    /// <summary>
    /// 只寫右半邊，接在已寫好的 <c>b.CopyNo</c> 之後，例如 <c>= a.CopyNo</c>。
    /// </summary>
    public string ComposeSuffix(SqlAssistSettings settings) =>
        "= " + SqlInsertionText.Qualify(CounterpartName, CounterpartQualifier, settings);
}
