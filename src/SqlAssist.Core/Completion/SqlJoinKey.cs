using SqlAssist.Core.Settings;

namespace SqlAssist.Core.Completion;

/// <summary>
/// 當前對象的一個欄位與前面某個來源的同名欄位形成的配對。
/// </summary>
/// <remarks>
/// 這一筆配對就是一個完整的聯結條件，因此插入的是整條條件而不是單一欄位：
/// 使用者剛打完 <c>ON </c>，要的是 <c>b.CopyNo = a.CopyNo</c>，不是先挑一邊
/// 再回頭補另一邊。挑選項就等於寫完條件，游標留在後面接 <c>AND</c>。
///
/// 兩側的限定字都照「有名字就寫名字」：這裡的欄位名稱<b>一定</b>有兩個來源在競爭，
/// 裸名在敘述裡是模稜兩可的，寫出來會執行失敗。一般欄位的插入文字只有在
/// 相異限定字兩個以上時才補名字，那一條管不到這裡——同一份敘述就是因為有兩個
/// 來源才配得出對。
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

    /// <summary>當前對象（游標前最後加入的那個來源）的欄位名稱。</summary>
    public string ColumnName { get; }

    /// <summary>當前對象在敘述中的名稱；讀不出來時為 null。</summary>
    public string? Qualifier { get; }

    /// <summary>配對到的欄位名稱，來自前面的某個來源。</summary>
    public string CounterpartName { get; }

    /// <summary>那個來源在敘述中的名稱；讀不出來時為 null。</summary>
    public string? CounterpartQualifier { get; }

    /// <summary>整條條件的插入文字，例如 <c>b.CopyNo = a.CopyNo</c>。</summary>
    public string ComposeInsertionText(SqlAssistSettings settings)
    {
        return SqlInsertionText.Qualify(ColumnName, Qualifier, settings) +
            " = " +
            SqlInsertionText.Qualify(CounterpartName, CounterpartQualifier, settings);
    }

    /// <summary>建議清單說明欄的那一段，例如 <c>= a.CopyNo</c>。</summary>
    /// <remarks>
    /// 左邊那一半就是顯示文字本身，再寫一次只是雜訊；說明欄要說的是它對到誰。
    /// </remarks>
    public string ComposeSuffix(SqlAssistSettings settings)
    {
        return "= " + SqlInsertionText.Qualify(CounterpartName, CounterpartQualifier, settings);
    }
}
