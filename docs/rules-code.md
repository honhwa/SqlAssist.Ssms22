# 程式碼、測試與工具護欄

修改 `.cs`、專案檔、測試或 `tools/` 前必讀。

## 分層

- **禁止** Core 或 Metadata 參照 Visual Studio／SSMS 組件。Metadata 只依賴 `System.Data`。
- **禁止**把只看文字即可判斷的邏輯放在 Ssms22；那層只拿服務、接事件及寫回編輯器。
- **禁止** `Core/Matching` 參照 `Core/Completion`；Matching 必須與領域無關。

## 路徑與名稱

- 資料夾與命名空間一致；測試鏡像來源路徑。不要為單一檔案建立資料夾。
- 不用 `Metadata.SqlObjectInfo` 這類相對限定；用 `using` 加簡名。
- 禁止手改 `Keywords/SqlKeywordCatalog.Generated.cs`；改 `tools/Generate-Keywords.ps1` 後重跑。

## 對 SSMS 組件的參考

- 只有 `Microsoft.VisualStudio.*` 與 SSMS 專有組件可以用 `HintPath` 指向安裝目錄。
- `System.*` 這類 BCL 外掛組件一律走 NuGet 的契約版本，並加 `ExcludeAssets="runtime"`
  讓它不進 VSIX。抓安裝目錄那份會把建置機的修補版本（例如 `10.0.0.10`）寫進參考，
  而 `Ssms.exe.config` 的 bindingRedirect 只涵蓋到宿主自己那份；裝到較舊的 SSMS 上
  就是 `FileNotFoundException`，沒有降級，只有用到的功能整組消失。
- `tools/Test-VsixPackage.ps1` 會擋下版本不是 `x.0.0.0` 的參考。

## 品質與公開內容

- `TreatWarningsAsErrors` 與 Nullable 必須維持啟用。SSMS 更新換掉參考組件的註解時，
  先照新契約改寫，`!` 與 `#pragma` 是最後手段且要寫明理由，見[開發](development.md)。
- 測試使用 Microsoft.Testing.Platform；執行 `tools/Run-CoreTests.ps1` 或 `dotnet test <方案>`，
  不得加回 VSTest 轉接層。過濾用 `--filter-class`／`--filter-method`：`--filter` 只吃 VSTest
  語法，給裸類別名會回 `Zero tests ran` 並以結束碼 5 收場，看起來像測試不存在。
- 寫死 DIP 的版面斷言要先 `WpfTest.PinLayoutDpi` 把量測釘在 100%。純 WPF 的測試沒有呈現來源，
  WPF 改用行程看到的系統 DPI 做 `UseLayoutRounding`，於是 150% 螢幕下 1 DIP 的邊框量到 1.333，
  自動高度比設計值多 0.667。**放寬斷言不是修好**——那只是把契約磨掉，失敗會往下一條搬。
  真正的多 DPI 覆蓋由測試自己用 `RenderTargetBitmap` 指定 96／144／192，不靠主機縮放。
- 測試動到行程共用的靜態欄位時，那些類別歸同一個
  `[CollectionDefinition(DisableParallelization = true)]` 集合（`SqlIconFactoryCollection`、
  `MetadataFailureCollection`、`SqlSuggestionUsageCollection`）。只共用一個集合不夠：那擋不住
  第三個類別在靜態值被換掉的期間讀它。症狀是單獨跑一定過、整份跑起來必失敗。
- 合成鍵盤事件要傳 `TestKeyboardDevice`，不要傳 `Keyboard.PrimaryDevice`：後者讀的是執行緒的
  Win32 按鍵狀態，實體鍵盤上壓著 Ctrl／Shift／Alt／Win 就會混進去。產品碼讀
  `KeyEventArgs.KeyboardDevice.Modifiers`，於是合成的 Home 被當成有修飾鍵、處理常式提前
  `return`，`Handled` 留在 false。症狀與上一條同一個形狀（單獨跑、`--max-threads 1` 都綠，
  整份跑偶發一條紅），但成因不在靜態欄位而在 OS 輸入狀態，加 `[Collection]` 治不好。
  兩側都有守護測試：`KeyboardDeviceUsageTests` 掃產品碼的靜態 `Keyboard` 讀取（例外只有
  `ShiftHeld`），也掃測試裡的 `Keyboard.PrimaryDevice`。產品碼唯一的合法用法是
  `SqlSnippetSurroundPicker` 把殼層解析掉的按鍵推回輸入管線——那裡要的正是實體修飾鍵。
- `SqlAssist.Ssms22.Tests` 是**逐檔列出**產品的純 WPF 原始碼（`<Compile Include>` 加 `Link`），
  不是專案參考。新增、改名或刪除那條清單上的產品檔時要**同步改 `SqlAssist.Ssms22.Tests.csproj`**；
  漏改的症狀是建置失敗說找不到檔案，而產品專案自己編得過。
- 註解只寫理由、失敗方案或不照做的症狀，不逐行翻譯程式碼。
- 公開 repo 的程式、註解、測試、文件、commit 訊息與 PR 內文禁止出現真實系統的伺服器、
  資料庫、schema、資料表、欄位或程序名；使用者回報裡的名稱先換掉再寫下來。只用既有的
  圖書館領域：`Lib_Reader`／`Lib_Tag`、`PUBLISHER`／`PUBL_CODE`、
  `Cat_BookCopy`／`CopyNo`、`Loan`／`LoanDetail`／`Copy`／`Branch`；例外只有 T-SQL
  保留字案例與產品內建捷徑。
- 工具不得寫死 SSMS 路徑或擴充 Identity Id；從 `tools/SqlAssist.Tools.psm1` 取得，並支援
  `-SsmsInstallDir` 覆寫。

## 文字格式

直接保留 LF 與 UTF-8 無 BOM，不在收尾時批次「修復」換行或重寫無關檔案。原始診斷只放
被忽略的 `artifacts/` 並保留原格式。完成前執行 `tools/Check-TextFiles.ps1`。
