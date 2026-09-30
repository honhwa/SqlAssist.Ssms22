# 差異報告：i18n 移植收尾（2026-09-29）

本頁記錄 2026-09-29 在 `dev_260925` 上收尾 i18n 移植這一輪的驗證結果、期間發現並修掉的
選單文字缺口，以及 `docs/index.md` 超預算的兩條處理路徑。尚未動 zh-Hans 與設定頁語言變更。

## 一、驗證矩陣

| 項目 | 指令 | 結果 |
|---|---|---|
| 建置 | `dotnet build SqlAssist.Ssms22.sln -c Release -p:Platform=x64 --no-restore` | **0 Warning(s) 0 Error(s)** |
| Core.Tests | 直接跑執行檔 | **2753/2753** |
| Metadata.Tests | 直接跑執行檔 | **981/981** |
| SqlMemory.Sqlite.Tests | 直接跑執行檔 | **146/146** |
| Ssms22.Tests | 直接跑執行檔 | **493/494**（見下） |
| `tools/Check-TextFiles.ps1` | 就地執行 | **通過**（1135 個 UTF-8／LF 檔） |
| `tools/Check-DocLinks.ps1` | 就地執行 | **通過**（206 個位址） |
| `tools/Check-Docs.ps1` | 就地執行 | **未通過**：僅 `docs/index.md` 4536/4500 |
| `tools/Test-CommandTable.ps1` | 就地執行 | **通過**（2 選單、13 群組、38 命令） |

### 兩條失敗的視覺斷言與本輪無關

| 斷言 | 實測 | 判定 |
|---|---|---|
| `UI.NotificationIslandTests.所有內容對齊同一條圖示中線與文字起點` | `Expected: 20, Actual: 20.699999999999999`（`NotificationIslandTests.cs:436`） | **穩定失敗**，skill 記載 2026-09-28 在未含當輪變更的基線上重跑照樣失敗、master 也沒修 |
| `UI.SqlMemoryVisualTests.SqlSummaryRowsStayVirtualizedAndRenderAcrossThemesAndDpi` | `Assert.NotNull() Failure`（`SqlMemoryVisualTests.cs:616`） | **時好時壞**：本輪三次執行中兩次失敗、一次全過 |

判定依據：兩個測試檔與 `NotificationLayout` 本輪都沒被改到；失敗的實測值與移植前逐字相同；
Ssms22 測試套件 494 條中其餘全過。

## 二、本輪修掉的真缺口：6 顆命令的選單文字

`tools/Test-CommandTable.ps1` 這輪由移植帶進了 38 行新檢查，一跑就抓到 **6 顆按鈕**
缺 `TextChanges`、也不在 `SqlAssistCommands.MenuLabel` 的 switch 裡：

| 命令 | 常數 | ID |
|---|---|---|
| `cmdidPasteAsInPredicate` | `PasteAsInPredicate` | `0x021D` |
| `cmdidPasteAsValues` | `PasteAsValues` | `0x021E` |
| `cmdidQualifySchema` | `QualifySchema` | `0x021F` |
| `cmdidParenthesizeTop` | `ParenthesizeTop` | `0x0220` |
| `cmdidInlineRename` | `InlineRename` | `0x0221` |
| `cmdidInlineRenameKey` | `InlineRenameKey` | `0x0222` |

**這是使用者可見的缺陷，不是文件問題。** 這 6 顆是 `master` 沒有的 dev 獨有功能
（貼上值清單、文字改寫、就地改名），沒被移植涵蓋；`MenuLabel` 查不到就回 `null`，
命令文字停在命令表的中性 `ButtonText`——**英文介面的使用者看到中文**。
建置、四套測試、另外三個檢查全綠，因為編譯器和測試都看不到這件事。

### 處置（比照移植時對其餘 32 顆按鈕的作法）

| 檔案 | 變更 |
|---|---|
| `Ssms22/Menus.vsct` | 6 顆加 `<CommandFlag>TextChanges</CommandFlag>`；`ButtonText` 改英文；`LocCanonicalName` 改 `SqlAssist.<常數名>`；**移除 `ToolTipText`** |
| `Ssms22/Commands/MenuText.en.resjson` | 加 6 個鍵 |
| `Ssms22/Commands/MenuText.zh-Hant.resjson` | 加 6 個鍵（中文沿用原 `ButtonText`，不改變使用者原本看到的字） |
| `Ssms22/Commands/SqlAssistCommands.cs` | `MenuLabel` 的 switch 加 6 行 |
| `Ssms22/SqlAssistPackage.cs` | `ProvideMenuResource("Menus.ctmenu", 38)` → **`39`** |

`ToolTipText` 一併移除有實測依據（`docs/shell-commands.md:84`）：標了 `TextChanges` 之後
工具列提示顯示的是目前的 `Text`，命令表的 `ToolTipText` 不會再出現。移除後 vsct 已無任何
`ToolTipText`，38 顆按鈕也全部帶 `TextChanges`。

`ProvideMenuResource` 版號依 `docs/rules-platform.md:60` 的禁止條款加一；改完命令表**必須**
用 `Install-Extension.ps1` 重新安裝，只部署 DLL 不會生效。

### 新增的選單文字

| 鍵 | zh-Hant | en |
|---|---|---|
| `PasteAsInPredicate` | 貼上為 IN 條件 | Paste as IN Predicate |
| `PasteAsValues` | 貼上為值清單 | Paste as Value List |
| `QualifySchema` | 補齊結構描述（dbo.） | Qualify Schema (dbo.) |
| `ParenthesizeTop` | TOP 補上括號 | Parenthesize TOP |
| `InlineRename` | 就地改名（@變數） | Rename Variable in Place |
| `InlineRenameKey` | 就地改名（@變數，F2） | Rename Variable in Place (F2) |

英文用語照 `docs/localization-glossary.md` 與既有資源（`EditorText` 的
`Qualified {count} object references with a schema (dbo.)`、`Added parentheses to … TOP clauses`、
`Pasted {count} values`，以及 `MenuText` 的 `Copy as IN Predicate`），沒有自創。

## 三、`docs/index.md` 超預算：兩條路

| 版本 | 字元數 | 對 4500 |
|---|---|---|
| 移植前 `d0ba97f` | 4449 | 餘 51 |
| `master` | 4485 | 餘 15 |
| 本輪移植後 | **4536** | **超 36** |

移植淨增 87 字元；`master` 自己的 `docs/index.md` 只剩 15 字元餘裕，而 dev 比 master
多三個功能列（`paste-values.md`、`text-rewrites.md`、`inline-rename.md`，合計 51 字元）。

### 方案 ①：刪冗餘（4 行，−39 → 4497，餘 3）

不動任何連結、不改錨點、不刪關鍵字種類，只去重複前綴與次要細節：

| 行 | 現在 | 改成 | 省 |
|---|---|---|---|
| L26 | `| SQL Memory 保留、清理、部署 |` | `| 保留、清理、部署 |` | 11 |
| L69 | `| 貼上為 IN 條件、剪貼簿值清單、哪些值不加引號 |` | `| 貼上為 IN 條件、值清單 |` | 11 |
| L70 | ``| 補齊 `dbo.`、`db..obj`、`TOP 10` 補括號 |`` | ``| 補齊 `dbo.`、`TOP 10` |`` | 14 |
| L71 | ``| `@變數` 改名、批次界線、F2 |`` | ``| `@變數` 改名、批次界線 |`` | 3 |

L26 的「SQL Memory 」與 L24／L25 重複；L69～L71 的連結標籤本身就含被刪掉的詞
（貼上值清單、文字改寫、就地改名），仍搜得到。付的代價是
「哪些值不加引號」「`db..obj`」「F2」不再當關鍵字。

若想留多一點餘裕，可再加三處低風險的：L39 尾端 `、配對鍵`（−4）、L11 的 `碰到才讀`（−4）、
L30 尾端 `、命中`（−2），合計 −10，總長 4487。

### 方案 ②：放寬這頁的預算（1 行）

`tools/Check-Docs.ps1` 第 14 行 `[int]$IndexMdBudget = 4500,` → **`4600,`**。

`$WarnAt = 4500` 不動，所以這頁仍會出現在「接近單檔上限」的黃字清單裡。
代價是這頁的護欄放寬；`Check-Docs.ps1` 自己的訊息是「請**先刪冗餘**，必要時才拆分」，
`master` 也有 `33b8c81 docs: 刪冗餘讓接近預算的七份文件回到警告線以下` 的前例，
所以專案慣例偏方案 ①。

## 四、其他被推過警告線的文件

警告不擋，但同一條慣例（`33b8c81`）會想一起處理：

| 檔案 | 移植前 | 現在 | 變化 |
|---|---|---|---|
| `docs/code-map.md` | 4465 | 4574 | +109 |
| `docs/localization.md` | （新增） | 4800 | 整頁新增 |
| `docs/shared-components.md` | 4403 | 4665 | +262 |
| `README.md` | 5498 | 5603 | +105 |
| `README.zh-TW.md` | 4496 | 4556 | +60 |

`docs/search-navigation.md`（4525）與 `docs/shared-components-platform.md`（4945）
在移植前就已超過 4500，不是這輪造成的。

## 五、尚未處理

1. **zh-Hans（簡體中文）**：43 份 resjson、6 份資料型覆蓋檔要補；`SqlAssistTextLanguages`
   加 `zh-Hans`；語言列舉加值；`SqlLanguage.Match` 把 zh-CN／zh-SG 對到 zh-Hans；
   Deploy 白名單與 `vsixlangpack` 跟上。
2. **設定頁改成跟隨 SqlAssist 語言設定**（master 現行是跟隨宿主語言）——排在上面兩項之後。
3. `version.json` 的 `1.3.2 → 1.4.11` 仍待確認來源（建置前後 mtime 未變，不是建置造成的）。
4. `master` 上其餘 l10n 相關 commit（`8357bc2`、`1b1d478`、`cf956c8`、`daa90bb` 等）是否移植。
5. 這輪的變更尚未提交；`docs/index.md` 依方案 ① 或 ② 決定後再一起收。

## 六、環境事實（可重用）

- `tools/Test-CommandTable.ps1` 與三個 `Check-*.ps1` 都要**在 PowerShell 工具裡就地執行**
  （`& '…\腳本.ps1'`）。在工具裡 spawn 子 `pwsh.exe` 會讓整個 process tree 被砍掉：
  回 exit 1、日誌 0 byte，連同一段指令後面的幾行都不會執行。
- 日誌檔（`*>` 或 `Out-File -Encoding utf8` 寫的）就是 UTF-8，用 Python 直接
  `raw.decode('utf-8')` 讀，不要做 cp936 轉換。
- `Check-TextFiles.ps1` 內有 `exit 1`，要單獨一個呼叫跑。
- 建置加 `--no-restore` 可避開 NuGet 還原的網路逾時；`-t:CoreCompile` 缺參考解析，
  會產生大量假 `CS0234`／`CS0518`，不能用來判斷。
