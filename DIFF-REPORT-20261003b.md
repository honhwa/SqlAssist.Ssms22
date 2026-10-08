# 差異報告 2026-10-03b：符號端點調亮，並把字色定死為白

## 一、需求

原話（轉繁體）：`我要求的是在編輯器裡，單引號、括號等閉合的 mark 顏色改成黃底白字，現在不太好辨識`。

「現在」指的是上一輪（[DIFF-REPORT-20261003](DIFF-REPORT-20261003.md)）的結果：符號端點
`#936E00` 配白字，白字 4.70:1。問題是它**偏褐**——白字要 4.5:1 會把底色鎖在亮度 0.183 以下，
而那個亮度帶的黃沒有別的長相。使用者要的是「一眼看得出是黃底」，不是「字面上的 4.5:1」。

澄清後定案：底色調亮到**白字 3:1** 那一檔；搜尋命中維持亮黃配深字不動。

## 二、改了什麼

### 1. 底色亮度上限 0.175 → 0.29

`Ssms22/UI/TextMarkColors.cs`：

- 新增 `GoldInkContrast = 3.0`——這一組自己的字色門檻，與其餘各組的 4.5 分開。
- `MaximumGoldLuminance` 0.175 → 0.29（`1.05 / 3 - 0.05 = 0.30` 減 8 位元捨入的餘裕）。

`GoldSeed`（`#FFC000`）與推導都沒動，所以這是**改一個常數**：六套主題仍收斂成同一個值。

| | 之前 | 現在 |
|---|---|---|
| 符號端點底色 | `#936E00`（亮度 0.174） | **`#B98B00`**（亮度 0.288） |
| 白字對比 | 4.70:1 | **3.11:1** |
| 底色 vs 表面（暗底 `#2C2C2C`） | 2.97:1 | **4.49:1** |
| 底色 vs 表面（亮底 `#FFFFFF`） | 4.70:1 | 3.11:1 |

暗底上分離度變大，亮底上變小但仍遠過 `DarkSeparation`（1.45）。**兩個方向都沒有掉到門檻附近**——
真正修掉的是「偏褐」，不是分離度。

### 2. 字色定死為白（這是調亮帶出來的必然後續）

底色調亮後，淺色佈景自己的前景（深字）在金黃上算得出 **5.7:1**——`LightInk` 的規則是
「讀得到就留著」，留著它就變成**黃底黑字**，同一個標記在兩套主題上是兩種組合。

所以符號端點改走新的 `TextMarkColors.GoldInk`（固定白），不再共用 `LightInk`。這是這一層裡
唯一兩個通道都定死的一組；關鍵字端點仍走 `LightInk`。

### 3. 沒動的

搜尋命中兩級（亮黃 `#FFE066`／`#FFB900` 配深字）、關鍵字端點（強調色推導）、使用者自訂色、
高對比、區間淡底——全部不變。

## 三、動到的檔案

| 檔案 | 改了什麼 |
|---|---|
| `Ssms22/UI/TextMarkColors.cs` | 新增 `GoldInkContrast`／`GoldInk`；`MaximumGoldLuminance` 0.175 → 0.29；三處 remarks 改寫 |
| `Ssms22/UI/BlockPalette.cs` | `Endpoint` 的預設分支拆成兩條：符號回 `(GoldInk, GoldFill)`，關鍵字維持 `(LightInk, DarkFill)` |
| `Ssms22/Settings/SettingsPageText.{zh-Hant,en}.resjson` | 「深金黃」→「金黃」 |
| `docs/text-marks.md` | 兩組表格的字色欄改成「固定白」；門檻表換掉那一列並加一列 3.0；補充符號端點為什麼不沿用「讀得到就留著」 |
| `docs/block-colors.md` | 「深金黃配白字」→「金黃底配白字」（三處） |
| `tests/SqlAssist.Ssms22.Tests/UI/BlockPaletteTests.cs` | 符號預設那條：4.5 → `GoldInkContrast`，並新增「刻意不到 4.5」的斷言；`指定白字…` 那條的符號門檻同步放寬到 3.0 |

## 四、驗收

| 項目 | 結果 |
|---|---|
| `dotnet build -c Release -p:Platform=x64` | **0 Warning 0 Error** |
| Core.Tests | 2769 全過 |
| Metadata.Tests | 981 全過 |
| SqlMemory.Sqlite.Tests | 146 全過 |
| Ssms22.Tests | 501 條；首跑 2 條失敗、其後**連跑兩次都只剩 1 條** |
| Check-TextFiles | exit 0（1147 檔 UTF-8／LF，除 .sln 外無 BOM） |
| Check-DocLinks | 206 個位址全部回應成功 |
| Check-Docs | 只卡在**既有的** `docs/index.md` 4536/4500（本輪沒動） |

Ssms22 的兩條都是已知的環境性斷言：`NotificationIslandTests`（穩定失敗）、
`SqlMemoryVisualTests`（時好時壞，本輪首跑失敗、連跑兩次通過）。改的兩頁文件
（`text-marks.md` 1898、`block-colors.md` 2287）都沒進超預算名單。

## 五、實機要看的

1. 游標停在 `WHERE CopyNo = 'A123'` 的任一引號上 → **兩端**都該是金黃底白字。
2. 同一行上同時有搜尋命中 → 命中仍是亮黃配深字，與端點不撞色。
3. Light 與 Dark 各看一次：亮底上金黃靠「比紙深」、暗底上靠「比底亮」，兩種都要一眼看到。
4. 高對比 → 回到系統配對色，不應出現金黃。
5. 設定裡自訂過符號前／背景 → 行為與之前完全相同（自訂色不走這一層）。
