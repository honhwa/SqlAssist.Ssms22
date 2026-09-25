using System;

namespace SqlAssist.Core.Statements;

/// <summary>EXEC 骨架裡的一個參數。</summary>
/// <remarks>
/// <see cref="IsOptional"/> 與 <see cref="SqlStatementColumn.HasDefault"/> 看起來是同一件事，
/// 來源卻完全不同：欄位的預設值在 <c>sys.default_constraints</c> 裡，
/// 參數的預設值<b>不在</b> <c>sys.parameters</c> 裡——<c>has_default_value</c> 那一欄
/// 只對 CLR 模組有效，T-SQL 模組一律是 0。要知道哪些參數可以不傳，只能去讀模組定義，
/// 那是 <see cref="SqlModuleParameterDefaults"/> 的工作。
/// </remarks>
public sealed class SqlStatementParameter
{
    public SqlStatementParameter(
        string name,
        string dataType,
        bool isOutput,
        bool isOptional,
        string? defaultValue = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("參數名稱不可為空。", nameof(name));
        }

        Name = name;
        DataType = dataType ?? string.Empty;
        IsOutput = isOutput;
        IsOptional = isOptional;
        DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue;
    }

    /// <summary>含 <c>@</c> 前綴的參數名稱。</summary>
    public string Name { get; }

    public string DataType { get; }

    public bool IsOutput { get; }

    /// <summary>模組定義裡寫了預設值，因此呼叫時可以整個省略。</summary>
    public bool IsOptional { get; }

    /// <summary>
    /// 模組定義裡預設值的字面值，可以直接嵌進 <c>DECLARE</c>；沒有或讀不出來時為 null。
    /// </summary>
    /// <remarks>
    /// 與 <see cref="IsOptional"/> 的差別是「可以省略」與「省略時值是多少」。
    /// 展開成 <c>DECLARE</c> 需要後者：省略掉預設值改用型別的預留值（數字 <c>0</c>、
    /// 字串 <c>N''</c>）在語法上成立，卻把使用者設定的預設值換掉了——
    /// 那是一句跑得動、結果卻不對的呼叫。
    ///
    /// 讀不出來時是 null，此時退回型別的預留值。猜錯的代價不對稱：留 null 只是
    /// 少一點方便，猜一個值卻可能安靜地寫錯資料。
    /// </remarks>
    public string? DefaultValue { get; }
}
