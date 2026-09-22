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
        DefaultValue = string.IsNullOrEmpty(defaultValue) ? null : defaultValue;
    }

    /// <summary>含 <c>@</c> 前綴的參數名稱。</summary>
    public string Name { get; }

    public string DataType { get; }

    public bool IsOutput { get; }

    /// <summary>模組定義裡寫了預設值，因此呼叫時可以整個省略。</summary>
    public bool IsOptional { get; }

    /// <summary>
    /// 模組定義裡替這個參數寫的預設值原文，例如 <c>7</c>、<c>NULL</c>、<c>GETDATE()</c>；
    /// 沒寫預設值或是讀不出定義時是 null。
    /// </summary>
    /// <remarks>
    /// 與 <see cref="IsOptional"/> 一起從同一份定義讀出來，但兩者回答的問題不同：
    /// 那一個問「這一列能不能整列刪掉」，這一個問「留著的話先填什麼」。
    /// 兩個都要，因為使用者常常是留著參數只改值。
    /// </remarks>
    public string? DefaultValue { get; }
}
