# Master → dev_260925 遷移分析（第二次盤點）

日期：2026-09-27（前一版為 2026-09-26）
對象：master（領先 25 個提交）→ dev_260925（領先 10 個提交）
共同祖先：`44f3c47`（與上一版相同）

## 一、這次的變化

| 項目 | 上一版 | 這一版 |
|---|---|---|
| master 獨有提交 | 20 | **25**（+5） |
| dev_260925 獨有提交 | 9 | 10（多了 `a40d47f`，即上一版的分析檔） |
| 整體差異 | 585 檔 / +21,410 −10,850 | **620 檔 / +26,044 −12,966** |
| 雙方都改過的檔案 | 48 | **48**（不變） |
| master 新增、dev 沒有的 `src/*.cs` | — | 35 個 |

新增的 5 個提交（都是 2026-09-26 的作品，稍晚才推上來）：

| 提交 | 主題 | 規模 | 與 dev 重疊檔數 |
|---|---|---|---|
| `c535849` | 子句片語帶前一格位置，補上 FOR 之後的建議 | 14 檔 / +714 −292 | 3 |
| `d51c36c` | 位置分析補上 DESC、IF 條件、區塊邊界與游標選項的 Any 缺口 | 12 檔 / +510 −88 | 1 |
| `b15adf5` | **以完整度取代掃描預算**，補上進度、停止與建索引通知 | 56 檔 / +3,147 −1,895 | 1 |
| `76f457f` | 位置分析改用單一語句界線規則，錨點不再跨到上一句 | 9 檔 / +965 −439 | 1 |
| `af713d8` | 預覽資訊列按鍵測試改用測試鍵盤裝置 | 2 檔 / +32 −4 | **0** |

## 二、最重要的兩個新發現

### 1. `af713d8` 直接修掉 dev 已知的間歇測試失敗（零衝突，應立刻做）

dev 的 `tests/SqlAssist.Ssms22.Tests/UI/SqlMemoryVisualTests.cs:893` 正是：

```csharp
var keyboard = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { ... };
```

master 把它換成 `new TestKeyboardDevice()`。**`TestKeyboardDevice.cs` 在 dev 上已經存在**，
所以這是一行替換＋一條「禁止測試程式碼再用 `Keyboard.PrimaryDevice`」的防護測試。

成因：實體鍵盤按著 Shift／Ctrl 時，合成的 Home／End 被當成組合鍵放過，`Handled` 斷言
只在那一輪失敗、單獨重跑又通過——完全符合 dev 上記錄的間歇失敗現象。

⚠️ 另一條 `NotificationIslandTests.所有內容對齊同一條圖示中線與文字起點` **master 也沒修**
（該檔最近的提交都是通知功能本身，沒有修測試），遷移後仍會在。

### 2. L10n 不再是「最後再做」，它已經是別的功能的硬依賴

`SqlSearchCoverage.cs`（`b15adf5` 新增）第一行就 `using SqlAssist.Core.Localization;`，
句子由 `SqlText` 從 resjson 產生。`b15adf5` 還動了 6 個 dev 上根本不存在的 resjson
（`NotificationCatalog.*`、`SearchSourceText.*`、`SqlSearchText.*`），`8357bc2` 也依賴
`SqlKindText`。

**上一版把 L10n 排在第 6 批是錯的**——現在它卡住了 search 完整度與種類名稱兩條路。
建議改成：先只進 `d0c538f` 的**基礎建設**（76 檔、與 dev 只重疊 3 檔、絕大部分是新增
resjson 與產生器），全區遷移（`2b042dc`，407 檔、重疊 19）仍留到最後。

## 三、各提交的衝突分數（與 dev 重疊檔數，越小越好做）

| 提交 | 重疊 | 規模 | 備註 |
|---|---|---|---|
| `af713d8` | **0** | 2 檔 | 修 flaky test |
| `81f1168` | 0 | 9 檔 | 文件 demos |
| `64867ee` | 0 | 2 檔 | README |
| `03ce8ce` | 1 | 5 檔 | CaretHint |
| `2d613ab` | 1 | 1 檔 | 版號（需裁決） |
| `33b8c81` | 1 | 7 檔 | 文件瘦身 |
| `d51c36c` | 1 | 12 檔 | Any 缺口 |
| `b15adf5` | 1 | 56 檔 | search 完整度（**依賴 l10n**） |
| `76f457f` | 1 | 9 檔 | 語句界線重構 |
| `8357bc2` | 2 | 15 檔 | SqlKindText（依賴 l10n） |
| `9f6e6cc` | 2 | 4 檔 | 列尾標記說明 |
| `06723d6` | 2 | 10 檔 | VIEW DEFINITION 訊息 |
| `d0c538f` | 3 | 76 檔 | **l10n 基礎建設（建議提前）** |
| `1b1d478` | 3 | 8 檔 | 列尾例外標記 |
| `48fda29` | 3 | 9 檔 | 子句尾端不列物件 |
| `c535849` | 3 | 14 檔 | FOR 之後的建議 |
| `cf956c8` | 4 | 23 檔 | 篩選列快捷鍵 |
| `86990d6` | 5 | 112 檔 | 共用詞 SqlKindText |
| `cb741de` | 5 | 20 檔 | 系統模組／CLR |
| `8a4f731` | 7 | 24 檔 | ScriptDom 子句片語 |
| `daa90bb` | 8 | 20 檔 | 參數提示請回 |
| `c633ca4` | 9 | 13 檔 | `[@t]`（碰 `SqlInsertionText`） |
| `dd0a0f6` | 11 | 63 檔 | **最大重構** |
| `f14dcdc` | 11 | 45 檔 | 語言設定註冊 |
| `2b042dc` | 19 | 407 檔 | l10n 全區遷移（最難） |

## 四、修訂後的建議順序

| 批 | 提交 | 理由 |
|---|---|---|
| 0 | `af713d8` | 零衝突、一行改、立刻消掉一條間歇失敗 |
| 1 | `cb741de` → `06723d6` → `03ce8ce` | Metadata 正確性＋CaretHint，獨立性最高 |
| 2 | `daa90bb` | 參數提示自動請回 |
| 3 | `48fda29` → `c633ca4` | Completion 小改（後者與自動別名同檔，需手整合） |
| 4 | **L10n 基礎建設 `d0c538f`** | 提前：解開 search 與 SqlKindText 的依賴 |
| 5 | `b15adf5` | search 完整度（重疊只有 1，價值高） |
| 6 | `86990d6` → `8357bc2` → `1b1d478` → `9f6e6cc` | 共用詞／列尾標記 |
| 7 | `8a4f731` → `c535849` → `d51c36c` → `76f457f` | 子句片語與位置分析四連，**必須照這個順序** |
| 8 | `cf956c8` → `dd0a0f6` | 篩選列與最大重構 |
| 9 | `f14dcdc` → `2b042dc` → 文件／版號 | 語言切換與全區遷移，獨立排期 |

## 五、dev 獨有、master 沒有（合併時不可丟）

- `SqlAutoAlias.cs` — 自動別名
- `SqlJoinKey*.cs`（4 檔）— 配對鍵
- `SqlBlockCloser.cs`＋`SqlBlockPairAnalyzer.cs` — BEGIN／END、TRY／CATCH 自動閉合
- `SqlPastedValueList.cs`＋`SqlPasteValuesAction.cs` — 剪貼簿貼上為 IN
- `SqlColumnOrdering.cs`、`SqlTableSourceAliasStyle.cs`

查證方式：`git cat-file -e <rev>:<path>` 三方比對，確認這些檔在共同祖先與 master 都不存在，
所以 diff 裡標 `D` 只是「dev 有、master 沒有」的假象。**唯一真正的重構落差是
`SuggestionMatcher.cs`：base 有、master 已刪（換成 `SqlCompletionPolicy`／`SuggestionList`／
`SuggestionCategoryFilter`）、dev 還在改**——它會與「回車帶出第一個匹配物件」正面衝突，
是第 8 批的主要風險。

## 六、master 新增、dev 沒有的 35 個原始碼檔（遷移時會一起帶進來）

- **Completion**：`SqlCompletionPolicy`、`SqlCompletionSlot`、`SuggestionCategory(Set/Filter)`、
  `SuggestionContextFilter`、`SuggestionList(View)`、`SuggestionMark`、`SuggestionScore`、
  `SqlParameterHintRevival`
- **Keywords**：`SqlCaretPosition`、`SqlClausePhrase(Catalog/Match)`、`SqlKeywordPositionExtensions`
- **Localization**：`SqlText`、`SqlLanguage(Cache)`、`SqlTextLanguagesAttribute`、`SqlTextOverlay`
- **Search**：`SearchEta`、`SearchProgress`、`SearchRun`、`SearchTarget`、`SqlCatalogServerTextSearch`
- **其他**：`SqlObjectImplementation`、`NotificationTitle`、`JsonParseException.Text`、
  `SqlCompletionFilterBar`、`CaretHint`、`SqlSearchCoverage`、`SqlLanguageSwitch`、
  `SqlParameterHintKeeper`、`SqlProgressStrip`

## 七、待確認（與上一版相同，仍待裁決）

1. `version.json`：master `1.2` vs dev `1.2.5`。
2. L10n 是否全量採用？若只要基礎建設（供 search 與種類名稱用），可停在 `d0c538f`；
   若要介面語言切換，還要 `f14dcdc`＋`2b042dc`。
3. 貼上為 IN 是否重複：master 有 `SqlInPredicateScript`（結果格線 → IN 述詞），
   dev 另建 `SqlPastedValueList`＋`SqlPasteValuesAction`（剪貼簿貼上）。方向不同，
   但應確認是否共用同一份排版。
4. `NotificationIslandTests` 那條間歇失敗 master 也沒修，遷移後仍在；是否要一併处理。
