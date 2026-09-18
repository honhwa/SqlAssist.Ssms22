using SqlAssist.Core.Keywords;

namespace SqlAssist.Core.Completion;

public sealed class SqlSuggestion
{
    public SqlSuggestion(
        string displayText,
        string insertionText,
        string description,
        string preview,
        SuggestionKind kind,
        bool triggerFollowUp = false,
        string? schemaName = null,
        object? tag = null,
        SqlKeywordPosition positions = SqlKeywordPosition.Any,
        bool isDestructive = false,
        SqlJoinKey? joinKey = null)
    {
        DisplayText = displayText;
        InsertionText = insertionText;
        Description = description;
        Preview = preview;
        Kind = kind;
        TriggerFollowUp = triggerFollowUp;
        SchemaName = schemaName;
        Tag = tag;
        Positions = positions;
        IsDestructive = isDestructive;
        JoinKey = joinKey;
    }

    public string DisplayText { get; }

    public string InsertionText { get; }

    public string Description { get; }

    public string Preview { get; }

    public SuggestionKind Kind { get; }

    public bool TriggerFollowUp { get; }

    public string? SchemaName { get; }

    /// <summary>
    /// 建立這筆建議的來源資料。資料庫物件會放入中繼資料層的物件描述，
    /// 讓呼叫端可以在使用者選取時才去載入欄位與定義，而不必在建立建議時就全部帶齊。
    /// </summary>
    public object? Tag { get; }

    /// <summary>
    /// 這筆建議可以出現的位置。
    /// </summary>
    /// <remarks>
    /// 關鍵字、內建函式與 Snippet 會收斂到特定位置；資料庫物件的過濾走
    /// <see cref="CompletionTarget"/> 那條路。
    /// </remarks>
    public SqlKeywordPosition Positions { get; }

    /// <summary>沒有輸入前綴時不主動顯示的危險項目。</summary>
    public bool IsDestructive { get; }

    /// <summary>
    /// 這一筆欄位是當前對象與前面某個來源的配對鍵；不是配對鍵時為 null。
    /// </summary>
    /// <remarks>
    /// 帶著它就是帶著插入文字：<c>ON </c> 與 <c>WHERE </c> 之後使用者要的是整條
    /// 聯結條件，而那是配對本身決定的，不是在建立建議時另外拼一次。
    /// 排名也問這一個欄位，見 <see cref="SuggestionMatcher"/>。
    /// </remarks>
    public SqlJoinKey? JoinKey { get; }
}
