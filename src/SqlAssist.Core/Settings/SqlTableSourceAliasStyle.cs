namespace SqlAssist.Core.Settings;

/// <summary>
/// 在資料來源位置提交資料表、檢視或資料表值函式時，要不要在名稱後自動補上別名，
/// 以及別名前面要不要寫 <c>AS</c>。
/// </summary>
/// <remarks>
/// 三種狀態分別回答兩個問題：自動補別名嗎（<see cref="Off"/> 以外都是「要」），
/// 以及要寫 <c>AS</c> 嗎（<see cref="As"/> 寫、<see cref="None"/> 不寫）。
/// 拆成「總開關」加「是否寫 AS」兩個布林的話，可以調出「寫了 AS 卻根本不補別名」
/// 這種說不通的組合；三個值排在一起沒有這種問題。
/// </remarks>
public enum SqlTableSourceAliasStyle
{
    /// <summary>
    /// 自動補上別名，但不寫 <c>AS</c>：<c>FROM dbo.Lib_Reader lr</c>。
    /// </summary>
    None,

    /// <summary>
    /// 自動補上別名，並寫上 <c>AS</c>：<c>FROM dbo.Lib_Reader AS lr</c>。
    /// </summary>
    As,

    /// <summary>不自動補上別名。</summary>
    Off
}
