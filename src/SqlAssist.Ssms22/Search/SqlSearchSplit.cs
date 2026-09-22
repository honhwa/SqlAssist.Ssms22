using SqlAssist.Ssms22.UI;

namespace SqlAssist.Ssms22.Search;

/// <summary>
/// SQL Search 主從區的版面選擇：結果在上、預覽在下，不論工具窗多寬。
/// </summary>
/// <remarks>
/// 以前寬到 <see cref="MasterDetailView.DefaultSideBySideWidth"/> 就交給主從區自己轉成左右，
/// 理由是「上下分割在寬版面下會讓清單只剩幾列高」。實際用起來反過來——命中是一列一列掃的，
/// 清單要的是高度；預覽讀的是 SQL 原文行，一行的寬度就是它需要的全部。轉成左右之後
/// 清單只剩下半個欄寬，每一列的名稱與路徑都被截掉，掃起來比窄版面更慢。
///
/// 兩份瀏覽器共用這一條規則，所以判斷從 <see cref="SqlSearchBrowser"/> 搬到這裡：
/// 留在那裡的話，第二個呼叫端只會照著傳一次門檻，而那個數字看起來很合理，
/// 直到有人發現版面與第一份不一樣。
/// </remarks>
internal static class SqlSearchSplit
{
    /// <summary>
    /// 傳給 <see cref="MasterDetailView"/> 的轉向門檻；null 表示不轉向。
    /// </summary>
    /// <remarks>
    /// 門檻只有 <see cref="MasterDetailView"/> 讀得到，所以這個值就是「會不會變成左右」的
    /// 唯一開關——回歸測試驗的就是它，以及真的把它傳下去之後的版面。
    /// </remarks>
    public static double? Threshold => null;
}
