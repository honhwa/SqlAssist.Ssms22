# 分支比較報告：`dev_261012`（當前）vs `master`

日期：2026-10-12　當前分支 HEAD：`cd31580`　master：`b9fcd05`　共同祖先：`44f3c47`（2026-09-24）

---

## 1. 骨架

| 項目                    | 數值                                             |
| --------------------- | ---------------------------------------------- |
| 落後 master             | **199 個提交**（2026-09-24 → 10-08）                |
| 領先 master             | **26 個提交**（2026-09-24 → 10-12）                 |
| master 側相對共同祖先的變更     | **843 檔，+96,054 / −14,876 行**                  |
| master 新增的 `src` 原始碼檔 | **80 個 `.cs`**                                 |
| master 刪除的 `src` 原始碼檔 | **13 個 `.cs`**                                 |
| 當前分支獨有的 `src` 原始碼檔    | **31 個 `.cs`**                                 |
| 測試檔數                  | 271 → **302** `.cs`                            |
| `docs/*.md`           | 101 → **109**                                  |
| `.resjson`            | 88 → **90**                                    |
| 版本號                   | `1.4.34`（當前）／`1.4`（master，Nerdbank 自行推算 patch） |

提交型態分布（master 側 199 筆）：`fix` 121、`feat` 37、`refactor` 19、`docs` 13、`build` 4、`test` 2、`perf` 2。  
scope 分布：**`completion` 130**、`keywords` 11、`builtin-help` 11、`tools` 8、`preview` 7、`metadata` 4、`l10n` 4、`signatures` 2、`audit` 2，其餘各 1。

> 已確認重疊：dev 的 4 個 l10n 提交（`6354310`／`af477d5`／`3db4adc`／`7ee565f`）**已在 master**（`f14dcdc`／`2b042dc`／`d0c538f`／`86990d6`）。其餘 22 筆為 dev 真正獨有。

---

## 2. master 有、dev 沒有的功能（主體）

### 2.1 補全引擎：位置分析與候選集合全面重構（130 筆提交的最大宗）

| 面向      | master 的做法                                              | 代表檔案                                                                                                                 |
| ------- | ------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| 語句界線    | 改用**單一語句界線規則**，錨點不再跨到上一句                                | `SqlStatementBoundaries`、`SqlStatementHeads`、`SqlCaretPosition`                                                      |
| 範圍分析    | 與位置分析**共用判準**，`SqlScopeAnalyzer` 刪掉寫死的 `StatementStart` | `SqlScopeAnalyzer`（+504/−97）                                                                                         |
| 候選分派    | 搬進 Core，稽核與產品走同一條                                       | `SqlCompletionCandidates`、`SqlCompletionMetadata`、`ISqlCompletionMetadata`、`SqlCompletionSlot`、`SqlCompletionPolicy` |
| 清單開關／選取 | 由「位置分類」**單一規則**推出，不再各自判斷                                | `SuggestionCategory`、`SuggestionCategorySet`、`SuggestionCategoryFilter`、`SuggestionContextFilter`                    |
| 清單本體    | 新的清單／檢視／評分／標記模型                                         | `SuggestionList`、`SuggestionListView`、`SuggestionScore`、`SuggestionMark`                                             |
| 名稱格     | 第一層物件名稱格**由種類規則推出**，刪掉 `DetermineTarget` 逐句比對           | `SqlCatalogEntity`、`SqlCatalogEntityPosition`、`SqlColumnOwner`、`SqlScriptObjectSuggestions`                          |
| 別名      | 相互關聯子查詢看得到外層別名、欄位位置也列別名                                 | `SqlScopeAliasSuggestions`                                                                                           |

具體行為改進（節錄，全部 dev 尚無）：

- 候選集合封閉的位置**空前綴就開清單**，自己開的清單一律軟選（`e4b57a58`）
- **左方括號之後照樣列建議，提交時保留方括號**（`0df6ea12`）
- **平台會自己開清單時不再重開**，消除打 `[` 時的開→關→開（`a895705c`）
- **清單開著時自動配對照常補括號**（`07381287`）— 與 dev 的自動閉合功能直接相鄰
- 括號裡逗號之後回到該左括號、列得出 `NULL`（`939ed2dd`）
- 文法強制別名的括號之後改為軟選、列得出 `AS`（`ffa3fe1b`）
- `CREATE` 的名稱第一段列得出結構描述（`b2b945e5`）
- 資料表變數當欄位限定字時一律寫成 `[@t]`（`c633ca43`）
- 行註解結尾的句點不再被當成限定字（`be8546f5`）
- 子句尾端與語句開頭不列資料庫物件（`48fda292`）
- `MERGE`／`USING` 目標別名：`INSERT … FROM (MERGE dbo.Loan AS t` 之後列得出 `t`（最新 `b9fcd05e`）
- DML 動詞或其 `TOP` 之後是目標，`DELETE`/`INSERT`/`MERGE` 省略 `FROM`/`INTO` 也一樣（`7db8a793`）

### 2.2 新補全位置（dev 全部沒有）

子句尾端與選項：`EXEC … WITH RESULT SETS` 的結果集／資料行定義、`BACKUP`/`RESTORE` 的 `TO`/`FROM` 備份裝置、`OPEN`/`FETCH … FROM` 的指令碼資料指標、`inserted`/`deleted` 與 `UPDATE` 的 `OUTPUT`、`WINDOW` 子句與具名視窗、觸發程序事件清單、模組標頭／函式參數／`EXEC`/`RAISERROR` 的 `WITH`、外部索引鍵／函式呼叫／視窗排序／`OFFSET`、函式內片語與引數目錄、`CREATE OR ALTER` 的 `OR`/`ALTER`。

資料定義：`GROUPING SETS` 括號裡的 `ROLLUP`/`CUBE`、時態表 `GENERATED ALWAYS AS` 五種、序列 `NO MAXVALUE`／`NO MINVALUE`、資料壓縮、`Always Encrypted`、外部程式庫、資料分割函式、`CREATE PARTITION FUNCTION` 帶方括號、內嵌索引（`INCLUDE`/`WHERE`/`HASH`）、`ALTER TABLE ADD` 的內嵌索引、資料行型別之後的 `ROWGUIDCOL`、條件約束 `NONCLUSTERED` 之後的 `HASH`。

其他語系：`Service Broker` 全系列（優先順序／佇列／合約／對話／路由／遠端服務繫結）、XML/JSON 索引與 `JSON_ARRAYAGG`/`JSON_OBJECTAGG`、全文檢索與停用字詞表、`PolyBase`、擴充事件、稽核與稽核規格、安全性原則、圖形資料表 `MATCH` 與 `GRAPH PATH`、`AI_GENERATE_EMBEDDINGS`、`VECTOR`/`VECTOR_SEARCH`、SQL Server 2025 的 `JSON`/`VECTOR` 型別、`ODBC {fn EXTRACT(`/`{fn TIMESTAMPADD(`/`{fn TIMESTAMPDIFF(` 的引數清單（`EXTRACT` 六個欄位、`TIMESTAMP*` 九個 interval）。

### 2.3 子句片語（Clause Phrase）產生器

- 型別：`SqlClausePhrase`（536）、`SqlClausePhraseCatalog`（620）、`SqlClausePhraseMatch`、`SqlKeywordPositionAnalyzer.OptionLists`（1,354）、`SqlKeywordPositionExtensions`、`SqlOperand`
- 產生器搬到 C#：`tools/SqlAssist.KeywordGenerator/PhraseExplorer.cs`（1,363）、`KeywordProber.cs`（506）、`ProbeFacts.cs`（341）、`PhraseDeclaration.cs`（221）+ 14 個語系片語資料檔（`DdlPhrases`、`SecurityPhrases`、`IndexPhrases`、`QueryPhrases`、`DdlPhrases`…）
- `Generate-Keywords.ps1` 只剩啟動器；剖析結果存快取、第三階段 C# 平行跑（`1f5135a3`、`4676062d`）
- 成效：DBCC 命令與 `WITH` 選項、`LOGIN`/`USER` 的 `WITH`、金鑰／憑證／時態表／序列皆改由產生器推出；`WITHIN GROUP`、`WHERE CURRENT OF`、DBCC 引數補齊
- `SqlKeywordCatalog.Generated.cs` 淨增 **20,740 行**

### 2.4 內建說明（builtin-help，11 筆）

- `BuiltInDocs` 由單一 `BuiltInDocs.json` **拆檔並升到 version 2**：`functions`／`statements`／`system-procedures`／`tables`／`types-hints`，各附 `.en.json`
- **多段範例**（`SqlBuiltInExampleText`），段間加 `GO`；系統程序第一段改成可直接執行
- 游標下的系統程序與語句認得出來，**說明搶在物件解析之前**（`SqlBuiltInObjectResolution`）
- 語句說明補到 32 筆（交易、錯誤處理、DDL、維護命令），補上 `EXEC`、`RAISERROR`、`THROW`、`MERGE`、`WAITFOR`、`BULK INSERT`
- 預覽與提示接上多段範例，四條入口共用同一個說明決策點

### 2.5 召回稽核（Completion Audit）— 全新工具鏈

- `tools/SqlAssist.CompletionAudit`（`CompletionAuditor` 841、`AuditDefinitions` 1,295、`AuditWords`、`AuditMiss`、`AuditSignature`、`RecallCorpusSource`）
- `tools/SqlAssist.CompletionAudit.Runner`（連接 SSMS 當第二意見、`SaltedMask` 遮罩、`MicrosoftCorpusSource`/`ModuleSource`/`SqlMemorySource`、快取與報表）
- 腳本 `tools/Audit-Completions.ps1`、`tools/Set-CompletionAuditConnection.ps1`
- 語料 `tests/SqlAssist.Core.Tests/Keywords/RecallCorpus.sql`（1,295 行）+ `SqlKeywordRecallTests`
- 實質產出：10-05～10-08 共 **12 輪、數十個缺口**全數補齊並移除豁免名單

### 2.6 預覽（Preview，7 筆）

- **分離外殼並可移至工具窗**：`PreviewSurface`（788）、`SqlStructureToolWindow`、`SqlStructurePresenter`、`PreviewLifecycle`、`PreviewDragEngine`、`PreviewReveal`
- 進場改走**膠囊等待**、外觀改**動態島風格**（`SurfaceCapsule`、`SurfaceMotion`）；釘住後可自由搬動縮放，尺寸只記一份
- 片段與語句骨架**可預覽**，抬頭「更多／收起」、一顆箭頭收合
- 修正：箭頭先建再加入、平台邊界未預期失敗改送通知、借用釘住的窗時位置保留、未釘住拖抬頭也能拖出

### 2.7 搜尋（Search）

- **以「完整度」取代「掃描預算」**：新增 `SearchRun`、`SearchTarget`、`SearchProgress`、`SearchEta`；刪除 `SearchBudget`、`SearchProviderProgress`、`SearchExamineCounter`
- 補上進度、停止與建索引通知；`SqlSearchCoverage`、`SqlCatalogServerTextSearch`

### 2.8 參數提示（Signatures）

- **`SqlParameterHintRevival`：提示被收掉後自動請回**，外層簽章讓位給內層呼叫（`daa90bbe`）
- `SqlParameterHintKeeper`；只在真的浮出時才通知，不再每次停手就冒一列（`d195c1ae`）
- README 附示意動畫 `docs/images/parameter-hint-demo.gif`

### 2.9 執行個體名單（取代定序專屬實作）

- 定序抽成**共用的執行個體名單**：`SqlInstanceList`、`SqlInstanceListData`、`SqlInstanceListEntry`、`SqlInstanceValueForm`、`SqlServerInstanceListCache`、`SqlInstanceListQuery`
- 接上 `SET LANGUAGE` 的語言與 `AT TIME ZONE` 的時區
- 同步刪除整組 collation 舊實作（見 §4）

### 2.10 篩選列、標記與 UI 細節

- 篩選列改為**位置數字快捷鍵與單一規則**，預覽沒有結構時不出現（`cf956c8e`、`SqlCompletionFilterBar`）
- 建議清單列尾加入**例外標記**（`SuggestionMark`），意思寫在說明面板最上方
- 文字選取改用**專屬覆蓋色**，不再借列選取而淡到看不見（`dd33701a`、`TextSelectionColors`）
- `SqlProgressStrip`、`CaretHint`（共用游標提示）、`SqlToolWindowPane`（工具窗共同基底）
- 自動隱藏滑出後補排殼層根（`d292ed56`）
- wildcard 展開後收起 Tab 提示（`03ce8cec`）
- 右側說明與物件種類名稱改走 `SqlKindText`

### 2.11 Metadata / Navigation / QuickInfo

- 系統結構描述限定與未限定的 `sp_`/`xp_` **退回系統物件**（`SqlSystemObjectFallback`、`SqlObjectImplementation`），停留時只排預載
- 系統模組、CLR、擴充預存程序的定義與參數（`cb741de7`）
- `vector` 資料行與參數帶出維度與基底型別（`45c8eba9`）
- 取不到運算式時明確說是 `VIEW DEFINITION` 權限並點名缺的項目（`06723d6e`）
- 導覽：一條底線就是一個目標—多字語句與提示整串加底線（`a87ab7ca`）
- QuickInfo：ODBC `{fn EXTRACT(`／`{fn TIMESTAMPADD(` 第一個引數的停留說明（`509e2bba`）

### 2.12 工具鏈與文件

- 全新專案：`SqlAssist.KeywordGenerator`、`SqlAssist.TextGenerator`（resjson 文字產生器）、`SqlAssist.CompletionAudit(.Runner)`
- `tools/Test-CommandTable.ps1`（+38）、`Run-CoreTests` 改用 `--solution` 並固定在 repo 根目錄執行、`Check-Docs` 擋跨頁重複句
- 新增文件 16 份：`completion-audit`、`completion-catalog-names`、`completion-instance-lists`、`completion-name-slots`、`completion-phrases`、`completion-scope`、`keyword-casing`、`phrase-generator`、`phrase-lists`、`preview-surface`、`search-coverage`、`shared-components-metadata`、`shared-components-ui`、`localization`、`localization-glossary`
- 新增測試 **59 檔**，含 3 個全新測試專案（`CompletionAudit.Tests`、`KeywordGenerator.Tests`、`TextGenerator.Tests`）

---

## 3. 與 dev 自有功能直接相鄰、移植時最該先看的

| dev 自有功能                                 | master 的對應改進                                                                           | 衝突風險 |   |
| ---------------------------------------- | -------------------------------------------------------------------------------------- | ---- | - |
| 自動閉合 `'`、`[`、`{`、`BEGIN END`、`TRY CATCH` | 平台自己開清單時不再重開（`a895705c`）；清單開著時自動配對照常補括號（`07381287`）；`[` 後照樣列建議且提交保留方括號（`0df6ea12`）     | 高    |   |
| 輸入 `FROM`/`ON`/`WHERE` 後回車帶出第一個物件        | 軟選規則、範圍/位置共用判準、類別篩選單一規則                                                                | 高    |   |
| EXEC 帶出參數並宣告                             | `SqlVariableNames`（dev 獨有）vs master `SqlParameterHintRevival`／`SqlParameterHintKeeper` | 中    |   |
| 單引號 mark 改黃色                             | master `TextSelectionColors` 專屬覆蓋色                                                     | 低    |   |
| 設定頁語言下拉切換                                | 見 §3.1：l10n 基礎設施與 master **逐位元組相同**，沒有重複                                               | 無    |   |

### 3.1 l10n 真實狀態（2026-10-12 複核，修正報告初版的誤判）

初版依「提交標題相同」推論兩邊各做一套 l10n，實際逐檔比對後**不成立**：

| 檔案                                                                                 | 狀態                                                                                                             |
| ---------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| `SqlText.cs`、`SqlLanguage.cs`、`SqlLanguageCache.cs`、`SqlTextLanguagesAttribute.cs` | **逐位元組相同**（dev 是 cherry-pick，非自行重做）                                                                            |
| 44 個有差異的 `.resjson`                                                                | 全部是「各自功能的字串」：dev 多的是 `Rewriting`/`PasteValues`/`InlineRename`/`autoPairBlocks`；master 多的是搜尋完整度、`SqlKindText` 等 |

所以**沒有任何重複可清**；硬清只會刪掉 dev 自有功能的翻譯鍵。

dev 真正獨有（master 無）的設定頁解法：`SettingsPageManifest.cs`（227）、`SettingsManifestText.cs`（181）、`SettingsPageText.targets` 的內嵌資源段、`SqlLanguageSwitch.Apply` 的 `NotificationOrigin` 參數、`registration.json` 的 `sqlAssist.general.autoPairBlocks`。  
注意 dev 這套的效果是**設定頁固定繁中**（見 `SettingsPageManifest` 註解），與 master「交給殼層查資源」是互斥的兩條路，不是誰包含誰。

---

## 4. master 清理掉的技術債（dev 仍留著）

| 刪除檔                                                                                                                                    | 取代者                                |
| -------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------- |
| `SqlCollationCatalog`、`SqlServerCollationCache`、`SqlCollations`、`SqlScriptCollationSuggestions`、`docs/completion-collation.md` + 3 個測試 | 共用執行個體名單（§2.9）                     |
| `SuggestionMatch`、`SuggestionMatcher`                                                                                                  | `SuggestionList`／`SuggestionScore` |
| `SqlCompletionFilters`                                                                                                                 | `SqlCompletionFilterBar`           |
| `PreviewResizeEngine`、`SqlPreviewPlacement`                                                                                            | `PreviewDragEngine` 與動態島編排         |
| `SearchBudget`、`SearchProviderProgress`、`SearchExamineCounter`                                                                         | 完整度模型（§2.7）                        |
| `BuiltInDocs.json`                                                                                                                     | `BuiltInDocs/` 拆檔 v2               |

---

## 5. dev 獨有、master 沒有（移植時不可丟）

31 個 `src` 原始碼檔：

- **Pairing**：`SqlBlockCloser`、`SqlBlockPairAnalyzer`（自動閉合）
- **Rewriting**：`SqlSchemaQualification`、`SqlNameSlotScanner`、`SqlTextEdit`、`SqlTextRewrite`、`SqlTextRewriteResult`、`SqlTopClauseParenthesis`、`SqlVariableRename`（`dbo.` 補齊、`..`→`.dbo.`、`TOP N` 加括號）
- **Completion**：`SqlAutoAlias`、`SqlJoinKey`、`SqlJoinKeyMatch`、`SqlJoinKeyMatcher`、`SqlJoinKeySource`（自動別名、配對鍵）
- **Editor**：`SqlPasteValuesAction`、`SqlTextRewriteAction` + `SqlPastedValueList`（剪貼板自動 `In ()`）
- **Commands**：`InlineRenameCommand`
- **Settings**：`SettingsPageManifest`、`SqlTableSourceAliasStyle`
- **Statements**：`SqlVariableNames`（EXEC 參數宣告）
- 其它：`SqlColumnOrdering`、`SqlLanguageSwitch`

---

## 6. 合併試算（`git merge-tree --write-tree`，未動工作區）

**394 個衝突檔**：`.cs` 205、`.resjson` 114、`.md` 44、`.csproj` 8、`.json` 6、`.vsct` 3、`.targets` 3、`.sln` 1、其他 7。


一次性合併不划算。建議按 §2 的分區做**增量移植**，每次一項、跑完整套驗證。

---

## 7. 建議移植順序

> **2026-10-12 修正**：原第 1 項「l10n 去重」經逐檔比對後**不成立**，已移除（理由見 §3.1）。  
> 原第 2 項起順移。

1. **工具鏈與驗證腳本**（`Run-CoreTests` 改用 `--solution` 並固定 repo 根目錄、`tools/Test-CommandTable.ps1`、`Check-Docs` 擋跨頁重複句）— 完全獨立於產品碼，風險最低，先做可讓後面每一步的驗證更可靠
2. **搜尋完整度模型**（§2.7，獨立性最高）
3. **通知catalog**（§2.7 的基礎）
4. **內建說明拆檔 + 多段範例**（§2.4，資料為主、風險低）
5. **子句片語產生器**（§2.3，量大但與既有補全耦合集中在 `SqlKeywordPositionAnalyzer`）
6. **位置分析 + 候選集合重構**（§2.1，最大宗，與 dev 自動閉合/自動別名正面衝突，留最後）
7. **預覽分離外殼**（§2.6，UI 層，可獨立）
8. 每次移植後：`build clean` + 4 套測試 + 檔案檢查工具（exit 0）
