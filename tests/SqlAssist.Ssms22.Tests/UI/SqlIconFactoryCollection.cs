using Xunit;

namespace SqlAssist.Ssms22.Tests.UI;

/// <summary>
/// 會換掉 <see cref="SqlIconImage.Factory"/> 的測試類別都放這一個集合。
/// </summary>
/// <remarks>
/// <see cref="SqlIconImage.Factory"/> 是<b>行程共用的一個靜態欄位</b>——純 WPF 的測試沒有
/// VS 的影像服務，所以由測試自己接一個方塊進去當圖示。代價是：一個類別在靜態建構子裡
/// 換掉它時，同時在別的執行緒上跑的類別所建立、量測的圖示也跟著換成那一個。
///
/// 症狀很具體：<c>SqlMemoryVisualTests</c> 用 <c>VisualTreeHelper.GetDrawing</c> 讀圖示的實際
/// 筆墨範圍來驗證中心線，而它自己接的方塊帶背景、別的類別接的不帶——畫不出東西，
/// <c>GetDrawing</c> 因此回 null，斷言說「值不該是 null」。它單獨跑一定過、整份跑起來必失敗
/// （實測 <c>--max-threads 1</c> 就消失），很容易被當成環境問題放過去。
///
/// 停用平行化而不是只把這幾個類別放進同一個集合：同一個集合只保證這幾個<b>彼此</b>不同時跑，
/// 擋不住第三個類別在工廠被換掉的期間建立圖示。理由與做法同 Metadata 的
/// <c>MetadataFailureCollection</c>。
///
/// 新增會建立 <see cref="SqlIconImage"/> 或設定這個欄位的測試類別時，記得一併掛上
/// <c>[Collection(SqlIconFactoryCollection.Name)]</c>，否則偶發失敗會再回來。
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlIconFactoryCollection
{
    public const string Name = "圖示工廠";
}
