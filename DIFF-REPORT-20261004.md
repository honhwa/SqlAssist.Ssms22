# 差異報告 2026-10-04：設定頁固定顯示繁體中文，並修掉中性資源被塞繁中的殘留

## 一、需求

原話：`把setting里的文字全部改为繁体中文`。

工作區當時的狀態是「有人已經動過、但動法會讓建置失敗」：

- `SettingsPageText.en.resjson` 被**整份換成和 zh-Hant 一模一樣的繁中**（原檔是英文）。
- 旁邊多了兩個英文本備份 `SettingsPageText.en - Copy.resjson`、`SettingsPageText.en1.resjson`。
- `SqlAssist.Ssms22.csproj` 加了 `AdditionalFiles`／`None`／`EmbeddedResource` 三段排除規則，
  把上面兩個備份檔擋在產生器外（其中一段還寫死 `P:\...` 絕對路徑）。

**為什麼會失敗**：語言清單是 `Directory.Build.props` 的 `zh-Hant,en`，而文字產生器對
非中日韓語言有一條漏翻檢查（`tools/SqlAssist.TextGenerator/SqlTextGenerator.cs:179`
→ `SqlTextDiagnostics.Untranslated`）：`en` 這種語言出現中日韓字元就是建置錯誤。
`docs/localization.md` 也明寫「中性語言一律是英文」。所以把 en 檔塞繁中，建置會紅燈。

澄清後定案：**設定頁一律顯示繁中，用改程式的方式做，en 資源維持英文**（不破壞中性語言規則）。

## 二、改了什麼

### 1. 設定頁固定用來源語言（核心）

設定頁的文字走的是唯一一條路：`SettingsPageManifest` 在套件載入時，把安裝資料夾那份
`SqlAssist.registration.json` 裡的 `"@鍵;{packageGuid}"` 換成字面值，再改寫 pkgdef 的
`CacheTag` 讓殼層重讀。殼層沒有鍵可以查，只能照著畫——**所以換成哪個語言，設定頁就是哪個語言**。

原本傳進去的是「介面語言設定（或 `auto` 挑出來的宿主語言）」，現在改成一律用
`SqlLanguage.Source`（來源語言＝繁體中文）：

| 檔案 | 改了什麼 |
|---|---|
| `Ssms22/Settings/SettingsPageManifest.cs` | `Apply(language, origin)` → `Apply(origin)`；`Rewrite` 內固定 `var language = SqlLanguage.Source`；類別註解補上「設定頁不提供在地化」的理由；產物檔頭說明改成「固定換成來源語言」 |
| `Ssms22/Settings/SqlLanguageSwitch.cs` | 呼叫端 `Apply(language, origin)` → `Apply(origin)`，`SqlText.SetLanguage(language)` 不動（其餘介面照舊即時切換） |

順手簡化：`s_language` 欄位移除。語言固定之後，`ReferenceEquals(language, s_language)` 永遠為真，
快取條件就等價於「pkgdef 的標籤是不是上次寫下去的那一個」，留著只是死重量。

**行為差異**：切換「介面語言」下拉框不再影響設定頁（設定頁永遠繁中）；其餘介面文字照舊即時切換。
`SettingsPageLanguageRestartNotice` 那則通知只在產物內容真的變了時才發（首次套用、換版重新部署、
來源文字改動），所以之後改介面語言不會再冒出來。

### 2. 翻譯檔回復到「en 英文、zh-Hant 繁中」

- `SettingsPageText.en.resjson` 用英文備份還原＝HEAD 的英文版 **＋ 兩句顏色修正**
  （`white ink on the gold` / `the gold is used … with white ink picked automatically`，
  對應 2026-10-03 的符號端點改金黃底白字）。229 個鍵與 zh-Hant 逐一對齊、順序相同。
- 刪除 `SettingsPageText.en - Copy.resjson`、`SettingsPageText.en1.resjson`（內容已回收進 en 檔）。
- `SqlAssist.Ssms22.csproj` 的三段排除規則與一處無關的排版改動全部還原（`git diff` 現為空）。

### 3. 「介面語言」這項設定的說明（zh-Hant 與 en 同步）

原本寫「本設定頁的文字由 SSMS 畫，要重新啟動 SSMS 才會跟著換」——這句話在新的行為下是錯的，
兩份都改掉：

- zh-Hant：`…切換後立即生效；本設定頁由 SSMS 畫，固定顯示繁體中文，不隨這一項改變，擴充功能清單則一律跟隨 SSMS。`
- en：`…Takes effect immediately. SSMS draws this settings page, which is always shown in Traditional Chinese regardless of this setting. The extension list always follows SSMS.`

## 三、動到的檔案

| 檔案 | 改了什麼 |
|---|---|
| `Ssms22/Settings/SettingsPageManifest.cs` | 固定來源語言、移除語言參數與 `s_language`、註解與產物檔頭說明改寫 |
| `Ssms22/Settings/SqlLanguageSwitch.cs` | 呼叫端對齊（`Apply(origin)`）＋一行理由註解 |
| `Ssms22/Settings/SettingsPageText.zh-Hant.resjson` | `GeneralLanguageDescription` 改成「設定頁固定繁中」 |
| `Ssms22/Settings/SettingsPageText.en.resjson` | 還原成英文（含顏色修正）＋同步改 `GeneralLanguageDescription` |
| `Ssms22/SqlAssist.registration.json` | 檔頭說明改成「一律換成來源語言」 |
| `Ssms22/SqlAssist.Ssms22.csproj` | 還原（移除備份檔排除規則與無關排版改動） |
| `docs/settings.md` | 「介面語言」段：設定頁固定繁中、不歸它管 |
| `docs/localization.md` | 設定頁那一段與「即時切換」表格列：不在地化、固定繁中 |

## 四、驗收

| 項目 | 結果 |
|---|---|
| `dotnet build SqlAssist.Ssms22.sln -c Release -p:Platform=x64` | **0 Warning 0 Error**（證明 en 檔繁中殘留已解除、229 鍵兩語言對齊） |
| Core.Tests | 2769 全過 |
| Metadata.Tests | 981 全過 |
| SqlMemory.Sqlite.Tests | 146 全過 |
| Ssms22.Tests | 501 條；`failed: 1` ＝ `UI.NotificationIslandTests.所有內容對齊同一條圖示中線與文字起點`（**已知的穩定失敗**，非本輪造成） |
| Check-TextFiles | exit 0（1150 檔 UTF-8／LF，除 `.sln` 外無 BOM；含本報告與 2026-10-04 日誌） |
| Check-DocLinks | 206 個位址全部回應成功 |
| Check-Docs | 只卡在**既有的** `docs/index.md` 4536/4500（本輪沒動）。`docs/localization.md` 由 4966 → **4947**（仍在 4500 警告帶，未新增超標） |

## 五、實機要看的

1. 改完後**重新啟動 SSMS**，開 `Ctrl+,` → SqlAssist：整頁（含分類標題、說明、下拉選項、按鈕）應全為繁中。
2. 把「介面語言」切成「英文」，重啟 SSMS：**設定頁仍應是繁中**，而建議清單／通知／選單命令等自製介面變英文。
   這是刻意的分歧，也是本輪的驗收重點。
3. 擴充功能清單與鍵盤頁命令名稱照舊跟隨 SSMS，不受影響。

## 六、未解風險與後續

- **中性 .resources 從此查不到鍵**：en 版設定頁文字只剩「組件裡有這份資源」的形式，
  以及萬一 `SettingsPageManifest` 失效時殼層的後備（那會退回英文）。en 檔仍必須存在且為英文，
  由 `SqlAssistSettingsPageNeutralLanguage` 與 SQLTXT 規則守著。
- **首次生效要重啟一次**：這是既有的機制（殼層在套件載入前就讀完註冊檔），不是本輪新增的限制。
- `docs/localization.md`（4947）與 `docs/index.md`（4536/4500）都在預算邊緣；
  `index.md` 超標是使用者先前表示要自行處理的既有項。
- 本輪沒動 2026-10-03 那批顏色變更（`TextMarkColors.cs`、`BlockPalette.cs`、`docs/text-marks.md`、
  `docs/block-colors.md`、`BlockPaletteTests.cs`）的內容，只把被覆蓋掉的英文備份還原回 en 檔。
