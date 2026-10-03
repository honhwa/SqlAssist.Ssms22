# 差異報告：等號之後的引號補不上（2026-10-02，本日第二項）

使用者回報：**在 `WHERE` 打完一個欄位與等號之後，再打單引號不會自動補上另一半。**

修好的是**自動配對的參與條件**，不是取色推導：`WHERE Status = ` 之後打 `'` 時，
建議清單還開著，而「清單開著就整條讓開」那道守衛把**補上結尾字元**也一起擋掉了。

## 一、定位：文字那一半沒問題

`Core/Pairing` 的四條規則只看文字，所以先把它們釘死。用一個臨時測試把
`AutoCloseFor`、`ShouldReopen` 與 `Analyze` 的實際回傳值倒出來
（`Assert.Fail` 傾印，跑完即刪，未進 commit）：

| 輸入（`|` 是游標） | `AutoCloseFor('\'')` | `ShouldReopen` | `Analyze` |
|---|---|---|---|
| `SELECT * FROM dbo.Loan WHERE Status = \|` | **`'`** | false | invalid、`Any` |
| `SELECT * FROM dbo.Loan WHERE Status =\|` | **`'`** | false | invalid、`Any` |
| `SELECT * FROM dbo.Loan WHERE Status = \| `（右邊還有字） | `'` | false | invalid、`Any` |
| `SELECT * FROM dbo.Loan WHERE \|` | `'` | **true** | valid、`Predicate` |
| `SELECT * FROM dbo.Loan WHERE Status = 1 AND \|` | `'` | **true** | valid、`Predicate` |
| `SELECT * FROM dbo.Loan WHERE Status = \| AND Fee > 1` | **null** | false | invalid、`Any` |

兩件事因此確定：

1. **該補。** 游標在行尾（或右邊只有空白）時這條規則回 `'`，不是 null。
   第五列那個 null 是「右邊還有字」那條邊界規則，與本次回報無關。
2. **清單是前面那個 `WHERE ` 開的。** `WHERE ` 之後的目標是述詞，會重開清單；
   而 `Status` 是一般的識別字（不重開）、`=` 之後的文法位置沒有東西可列
   （`Analyze` 直接回 invalid，也就不會重開）。**這兩個位置都不會收掉那份清單**，
   於是整段值的輸入都在清單開著的情況下發生。

## 二、根因

`SqlAssistCompletionCommandHandler` 的 TypeChar 路徑在 2026-09-02 加入自動配對時，
把自動配對的呼叫放在**既有**的一道守衛裡面：

```csharp
// 建議清單開著時一律讓開：那一次 TypeChar 可能是提交鍵，
// 吞掉它等於提交不了；而在 session 中途插字元也會讓適用範圍失準。
if (Broker.GetSession(args.TextView) is not null)
{
    return false;
}
```

那道守衛是為**自動大寫**寫的——它會改寫剛打進去的字，清單開著時與 session 打架。
（`d6dcbb20` 的 commit message 自己寫著「與自動大寫同一條規則」。）但自動配對的
四個方向裡，只有兩條會**吞掉按鍵**：

| 方向 | 吞按鍵 | 清單開著時該不該讓開 |
|---|---|---|
| 打開頭字元 | 否，只多插一個字元再交還按鍵 | **不該**——提交照常發生 |
| 打結尾字元，而它就在游標右邊 | 是 | 該 |
| 先選取再打開頭字元 | 是（自己插入兩個字元） | 該 |
| Backspace 夾在空配對中間 | 是（但 Backspace 不可能是提交鍵） | 見下 |

「補上結尾字元」不吞按鍵，卻被那道守衛一起擋掉，這就是回報的症狀。
第二輪的症狀是它的必然結果：第一個引號補不上，就沒有「打第二個引號跳過去」可言。

## 三、修正

只有一條規則改變：**建議清單開著時，自動配對只有「包夾選取範圍」讓開。**

| 方向 | 修正後 |
|---|---|
| 打開頭字元 | 清單開著照補 |
| 打結尾字元，而它就在游標右邊 | 清單開著照跳過（跳過的是一個自己補的字元，交還按鍵的方式不變） |
| Backspace 夾在空配對中間 | 清單開著照刪整對（Backspace 不可能是提交鍵） |
| 先選取再打開頭字元 | 維持讓開：它會自己插入兩個字元，等於吃掉那次按鍵 |

原本「清單開著就整條讓開」這條規則在 `docs/auto-pairing.md` 是寫在
「什麼時候**不補**」的表裡；修正後那一列移出該表，改成獨立一節說明四個方向
各自的判準。

## 四、變更清單

| 檔案 | 變更 |
|---|---|
| `Ssms22/Editor/SqlAutoPairing.cs` | `TryHandleTypedCharacter` 新增 `completionListOpen` 參數；只有包夾選取範圍那一條讓開，跳過與補上兩條不再看它 |
| `Ssms22/Completion/SqlAssistCompletionCommandHandler.cs` | TypeChar 把 `Broker.GetSession(...) is not null` 當參數傳進去，不再整條提前 return；Backspace 的建議清單那一項移除；兩處 remarks 改述判準與理由 |
| `docs/auto-pairing.md` | 「什麼時候不補」表移除「建議清單開著」那一列，新增「建議清單開著時」一節（四個方向對照表＋`WHERE Status = ` 那個情境） |

行為改變只有這一項。`Core/Pairing` 的四條規則、`AutoCloseFor` 的邊界與語彙條件、
Snippet 欄位 session 讓開、方塊／多重選取不介入、高對比與自訂色全部不變。

## 五、驗收

| 項目 | 結果 |
|---|---|
| `dotnet build SqlAssist.Ssms22.sln -c Release -p:Platform=x64` | 0 Warning(s) 0 Error(s) |
| Core.Tests | 2769 / 2769，failed 0 |
| Metadata.Tests | 981 / 981，failed 0 |
| SqlMemory.Sqlite.Tests | 146 / 146，failed 0 |
| Ssms22.Tests | 501 條，failed 1（下方那條已知的穩定失敗） |
| `tools/Check-TextFiles.ps1` | 通過：exit 0 |
| `tools/Check-DocLinks.ps1` | 通過：206 個位址全部回應成功 |
| `tools/Check-Docs.ps1` | **未通過（既有，與本輪無關）**：`docs/index.md` 4536/4500 |

Ssms22.Tests 唯一失敗是 `UI.NotificationIslandTests.所有內容對齊同一條圖示中線與文字起點`
——本專案已知的穩定失敗（2026-09-28 基線比對確認過 master 也沒修）。
`UI.SqlMemoryVisualTests.SqlSummaryRowsStayVirtualizedAndRenderAcrossThemesAndDpi` 本輪通過。

`Check-Docs` 的紅燈是既有的超預算頁面（`git show HEAD:docs/index.md` 也是 4536），本輪未動那一頁；
本輪改的 `docs/auto-pairing.md` 是 4460 字元，未進「接近單檔上限」名單。

這一項沒有新增單元測試：改的是編輯器那一半（`ITextView` ＋ broker），
而測試專案只編譯得到 `Core` 與 `Metadata`，`Ssms22/Editor` 與命令處理常式
都不在測試專案裡——與這一族功能的既有分工相同，驗收靠實機。

## 六、實機要看的

「配對端點高亮」與「自動補上成對的括號與引號」兩個開關都開著，在清單會自動開的
位置打 `(`、`'`：

1. `SELECT * FROM dbo.Loan WHERE Status = ` 打 `'` → 應得到 `=|'` 且游標在中間。
2. 接著打第二個 `'` 收掉字串 → 應跳過去，不是插出 `''`。
3. 打了 `'` 馬上按 Backspace → 兩個字元一起收掉。
4. `SELECT * FROM ` 之後打 `(` → 清單開著也要補上 `)`。
5. 打關鍵字讓清單開著（例如 `SELECT ` 後按 Ctrl+Space），再選一段文字打 `'`
   → 維持不包夾（那條刻意讓開）。
6. 高對比模式下重看一次第 1 與第 4 項。

**若第 1 項仍然沒補**，兩個一句話就能定位的觀察：

- 同一個位置打 `(` 會不會補 `)`？連 `(` 都只出現一個字元 → 擋住的是一道
  與字元無關的閘門；`(` 會補而 `'` 不會 → 本報告的推論不是主因，要改查語彙狀態。
- 「關於與診斷」頁的「分隔字元自動配對」是不是「開啟」——那一項關著時整族功能都不動作，
  而它的症狀與此完全相同。
