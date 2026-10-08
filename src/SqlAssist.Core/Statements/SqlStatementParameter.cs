using System;
using System.ComponentModel;

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
    [Localizable(false)]
    public SqlStatementParameter(
        string name,
        string dataType,
        bool isOutput,
        bool isOptional,
        string? defaultValue = null,
        string? variableName = null)
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

        // 先擋掉 null 再擋空字串：netstandard2.0 的參考組件沒有 IsNullOrEmpty 的
        // NotNullWhen 標註，只寫那一句的話下一行仍被判成可能為 null。
        VariableName = variableName is null || variableName.Length == 0 ? name : variableName;
    }

    /// <summary>含 <c>@</c> 前綴的模組參數名稱，也就是這個參數在<b>模組簽章</b>裡的名字。</summary>
    /// <remarks>
    /// 這個名字由模組的宣告決定，呼叫端改不動：<c>EXEC dbo.usp_P @LoanId = …</c> 左邊那個
    /// <c>@LoanId</c> 一定要是模組定義裡寫的那一個，換成別的名字那一句就找不到參數了。
    /// 因此改名時動的是 <see cref="VariableName"/>，不是這裡。
    /// </remarks>
    public string Name { get; }

    /// <summary>
    /// 宣告與傳值用的區域變數名稱，含 <c>@</c> 前綴；沒有另外指定時等於 <see cref="Name"/>。
    /// </summary>
    /// <remarks>
    /// 與 <see cref="Name"/> 分開正是為了避免撞名：模組的參數名不是呼叫端能選的，而
    /// 展開出來的 <c>DECLARE</c> 需要一個<b>沒有被同批次用掉</b>的名字。兩者共用一格時，
    /// 為了避開撞名把 <c>Name</c> 改掉，連帶把呼叫那一行左邊的參數名也改掉了——
    /// 那一句於是找不到對應的參數（錯誤 8145），而畫面上只看得出名字多了一個數字。
    ///
    /// 唯一該跟著改的是宣告那一行與呼叫那一行<b>右邊</b>的值，
    /// 以及 <c>SELECT</c> 段裡列出來的東西——那三處都是這個區域變數。
    /// </remarks>
    public string VariableName { get; }

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
