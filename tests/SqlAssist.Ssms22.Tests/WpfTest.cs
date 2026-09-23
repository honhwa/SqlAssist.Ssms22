using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SqlAssist.Ssms22.Tests;

internal static class WpfTest
{
    public static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                // 測試不能吞掉背景執行緒的斷言，必須回傳原堆疊讓執行器判定失敗。
                failure = exception;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// 把一棵還沒接上呈現來源的視覺樹釘在 100% 縮放，讓寫死 DIP 的斷言量到設計值。
    /// </summary>
    /// <remarks>
    /// 純 WPF 的測試沒有 <c>PresentationSource</c>，WPF 於是用<b>行程看到的系統 DPI</b> 做版面
    /// 捨入。螢幕縮放 150% 時，1 DIP 的邊框被 <c>UseLayoutRounding</c> 捨入成 2 實體像素
    /// （1.333 DIP），自動高度因此比設計值多 0.667 DIP——量到的是捨入，不是設計值，
    /// 而斷言失敗看起來像產品壞了。實際案例：通知卡片設計 188 DIP，150% 下量到 188.6667。
    ///
    /// 真正的多 DPI 覆蓋由測試自己用 <c>RenderTargetBitmap</c> 指定 96／144／192 去做，
    /// 不靠主機的螢幕縮放；這裡只是把量測基準釘回 100%。
    ///
    /// <b>只在純量測的測試裡呼叫。</b>會輸出 QA 圖片的測試不要用：那會讓圖上的邊框回到
    /// 未捨入的 DIP 寬度，等於改掉要人眼檢查的那份輸出。
    /// </remarks>
    /// <param name="visual">自己的樹根；已經有父節點的視覺會被 WPF 拒絕。</param>
    public static void PinLayoutDpi(Visual visual) =>
        VisualTreeHelper.SetRootDpi(visual, new DpiScale(1, 1));
}
