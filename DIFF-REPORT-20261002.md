# 差異報告：符號端點與搜尋命中改用螢光筆黃（2026-10-02）

使用者要求：單引號的 mark 顏色改為黃色，SQL Search 匹配到的結果也用黃色 mark。
澄清後確定為「螢光筆黃（亮底黑字）」，範圍是**整組符號端點**（括號、方括號、字串引號）
＋**搜尋命中的兩級**；關鍵字端點（BEGIN／END、TRY／CATCH、CASE）維持強調色那一組。

## 一、原本為什麼不是黃色

兩處都從**主題強調色**推導，而 `TextMarkColors` 只有一組規則，且那一組的底色**一律落在深的那一側**：

| 事實 | 位置 | 後果 |
|---|---|---|
| 底色亮度上限 0.18、字色只回前景色或白色 | `TextMarkColors.MaximumLuminance`、`Ink` | 算不出亮底，也不可能配黑字 |
| 命中與符號端點共用同一條路 | `MatchPalette.Create(accent, …)`、`BlockPalette.Endpoint` 的 `seed == accent` 分支 | 兩者同色，且都跟著佈景由深紫變亮紫 |
| 單一色票 | `TextMarkColors.Fill` | 沒有第二組常數可放固定色 |

## 二、設計

`TextMarkColors` 由一組拆成**兩組**，差別在底色落在明度軸哪一側、顏色從哪裡來：

| 組 | 用在哪 | 顏色 | 字色 | 與表面的門檻 |
|---|---|---|---|---|
| 螢光筆組 | 搜尋命中兩級、符號端點 | 固定亮黃（`#FFE066`／再劃一層 `#FFB900`） | 深字 | 1.25 |
| 深色組 | 關鍵字端點 | 強調色推導 | 淺字 | 1.45 |

- **不從強調色推導**是這一輪的重點：標記說的是「這一段被劃起來了」，與主題色無關；
  跟著強調色走的那一版，同一個搜尋字在淺色主題是深紫底、深色主題是亮紫底。
  `MatchPalette.Create` 因此**移掉 `accent` 參數**，`ThemePalette` 與 `SqlScriptTheme` 兩個呼叫端同步。
- **門檻分開**（1.25 對 1.45）：螢光筆靠色相辨識——白紙上的螢光筆看得到是因為它是黃的，
  不是因為它比紙暗；要求到 1.45 只能把亮黃壓成土黃。字讀得到由黑字負責（12:1 以上）。
- **推開表面只在必要時發生**：固定亮黃與表面分不開時才往遠離表面的方向推，推的距離由表面算出
  （`JustBeyond`），並收在亮度帶 0.5–0.85 內；推不進去時停在邊上，比推出帶外更像螢光筆。
- **兩級是同一支筆的兩層**：目前那一處深一階（`FurtherThan` 依實際對比換算），不是換色相；
  表面亮到兩級被壓在一起時，目前那一處再按 `LevelSeparation` 推開一次。
- **符號端點與關鍵字端點分流**：`BlockPalette.Endpoint` 新增 `symbol` 旗標。一個顏色都沒自訂時
  符號走螢光筆黃、關鍵字仍走強調色深色組；使用者自訂過顏色與高對比的行為完全不變。
- `Pair`／`Weak`（深色組的兩級推導）與 `Ink`／`Fill` 更名為 `DarkFill`／`LightInk`，
  並補上螢光筆組的 `DarkInk`。深色組用不到兩級，那一份推導直接移除。

## 三、變更清單

| 檔案 | 變更 |
|---|---|
| `Ssms22/UI/TextMarkColors.cs` | 拆成兩組常數與兩條推導；新增 `HighlightSeed`／`HighlightCurrentSeed`／`HighlightFill`／`HighlightPair`／`DarkInk`；`Pair`、`Weak` 移除；`Fill`→`DarkFill`、`Ink`→`LightInk`、`MinimumSeparation`→`DarkSeparation`、`MaximumLuminance`→`MaximumDarkLuminance` |
| `Ssms22/UI/MatchPalette.cs` | `Create` 移除 `accent`，改走 `HighlightPair` ＋ `DarkInk` |
| `Ssms22/UI/ThemePalette.cs` | 呼叫端改齊；註解改成「連色相都不借」 |
| `Ssms22/Preview/SqlScriptTheme.cs` | 呼叫端改齊（指令碼高亮不再借 `AccentBorder`） |
| `Ssms22/UI/BlockPalette.cs` | `Endpoint` 新增 `symbol` 旗標；無自訂色時符號走螢光筆黃配深字 |
| `Ssms22/Settings/SettingsPageText.{zh-Hant,en}.resjson` | 符號高亮前景／背景的說明補上「兩者都留空時用螢光筆黃」 |
| `docs/text-marks.md` | 「三條規則」改成「兩組」＋門檻表補螢光筆那一列 |
| `docs/search-highlight.md` | 命中色票改述為固定亮黃，連色相都不借 |
| `docs/block-colors.md` | 符號高亮預設、縮排理由改述 |
| `tests/…/MatchPaletteTests.cs` | 論證改成亮側（黑字讀得到、字色比底色深）＋螢光筆黃不被推開的兩條案例 |
| `tests/…/ThemePaletteTests.cs` | 命中門檻改 `TextMarkColors.HighlightSeparation` |
| `tests/…/BlockPaletteTests.cs` | 新增「符號端點未自訂時預設是螢光筆黃配深字」六個主題案例 |

行為改變只有一項：**沒有自訂任何顏色時**，符號端點與搜尋命中的顏色。自訂色、高對比、
區間淡底、關鍵字端點都不變。

## 四、驗收

| 項目 | 結果 |
|---|---|
| `dotnet build SqlAssist.Ssms22.sln -c Release -p:Platform=x64` | 0 Warning(s) 0 Error(s) |
| Core.Tests | 2769 / 2769，failed 0 |
| Metadata.Tests | 981 / 981，failed 0 |
| SqlMemory.Sqlite.Tests | 146 / 146，failed 0 |
| Ssms22.Tests | 501 條；連跑多次，失敗數 1–2，只有下面兩條 |
| `tools/Check-TextFiles.ps1` | 通過：1142 個 UTF-8／LF 檔案，除 `.sln` 外皆無 BOM |
| `tools/Check-DocLinks.ps1` | 通過：206 個位址全部回應成功 |
| `tools/Check-Docs.ps1` | **未通過（既有，與本輪無關）**：`docs/index.md` 4536/4500 |

Ssms22.Tests 的失敗只有兩條，都是**字形／ink 量測**的環境性斷言，與顏色無關：

- `UI.NotificationIslandTests.所有內容對齊同一條圖示中線與文字起點`：本專案已知的穩定失敗
  （2026-09-28 用基線 worktree 確認過 master 也沒修）。失敗內容是 `20` 對 `20.67` 的次像素差。
- `UI.SqlMemoryVisualTests.SqlSummaryRowsStayVirtualizedAndRenderAcrossThemesAndDpi`：時好時壞。
  失敗點固定在 `InkCenter` 的 `Assert.NotNull(drawing)`——`VisualTreeHelper.GetDrawing` 在
  非互動工作階段還沒產生繪圖時回 null，正是本專案在 `BlockPaletteTests` 註解裡記過的那個現象。

**基線比對**（`git worktree add ../sa-baseline-20261002 HEAD`，比完已 `git worktree remove --force`
＋ `prune`）：乾淨 HEAD 連跑兩次都是 494 條、只失敗 NotificationIsland 那一條。
本輪工作樹連跑六次為 1／2／2／1／2／1 條失敗，其中 `SqlMemoryVisualTests` 三次失敗、三次通過
——與「時好時壞」相符，不是本輪改壞的。（另註：`../sa-head-baseline` 是 2026-09-28 留下的
未登記目錄，已缺 `.sln` 建不起來，本輪未動它。）

`Check-Docs` 的紅燈是既有的超預算頁面：`git show HEAD:docs/index.md` 也是 4536 字元，
本輪未動那一頁。放寬 `-IndexMdBudget 5000` 重跑時，其餘 105 份文件與 H1／錨點規則全部通過。

數值另以獨立模擬驗過 11 組表面（含純白、0xF5F5F5、0xFFF8D0、中灰、純黑）：
`current` 與 `match` 與表面都 ≥ 1.25、兩級之間 1.30–1.32、黑字 12:1 以上、兩級同側。

## 五、實機要看的

靜態示意在 `artifacts/mark-preview-20261002.html`（值由同一組公式算出，不是手挑；六套主題各一格，
附每一格的實測對比）。它是 WPF 之外的靜態頁，字型與 DPI 不參與，只拿來對顏色。

兩級分不分得出來只有眼睛驗得了。Light／Dark／高對比各看一次，深色主題另加 150% DPI；
彩色深色主題（月光、神祕森林、辣紅）單獨看過——這幾套的編輯器底色與工具窗底色不同深淺，
命中推開的量正好在那裡不一樣。符號端點要在「配對端點高亮」開著、且四個色彩欄位都留空時看。
