# 分支比較報告：`dev_260925`（當前）vs `master`

日期：2026-09-30　當前分支 HEAD：`9d814e4`　master：`0288d22`　共同祖先：`44f3c47`（2026-09-24）

---

## 1. 骨架

| 項目 | 數值 |
|---|---|
| 落後 master | **86 個提交**（2026-09-24 → 09-29） |
| 領先 master | **21 個提交**（2026-09-24 → 09-30） |
| master 側相對共同祖先的變更 | 716 檔，+56,622 / −13,052 行 |
| master 新增的 `src` 原始碼檔 | 57 個 `.cs` |
| master 刪除的 `src` 原始碼檔 | 14 個 `.cs` |
| 當前分支獨有的 `src` 原始碼檔 | 35 個 `.cs` |
| 測試檔數 | 270 → 283 `.cs` |
| 版本號 | `1.4.15`（當前）／ `1.4`（master） |

**合併試算（`git merge-tree --write-tree`，未動工作區）：97 個衝突檔**

| 衝突型態 | 檔數 | 說明 |
|---|---|---|
| `add/add` | 41 | **全部是兩邊各做一套在地化**（36 個 `.resjson` + `SqlTextOverlay.cs` + `SqlLanguageSwitch.cs` + `SettingsPageText.targets` + `localization.md` + `SqlTextOverlayTests.cs`） |
| `content` | 50 | 補全管線、VSIX 接線、文件、測試 |
| `modify/delete` | 6 | master 刪、當前分支改過（見 §5） |

副檔名分布：`.cs` 49、`.resjson` 36、`.md` 6、`.json` 2、`.csproj` 2、`.vsct` 1、`.targets` 1。

---

## 2. master 有、當前分支沒有的**功能**

### 2.1 子句片語（Clause Phrase）— 全新的補全層

「只在某條尾巴之後出現」的字：`SET` 選項、`ALTER INDEX` 動作、`FOR XML` 模式、`DBCC` 命令與 `WITH` 選項、`LOGIN`／`USER` 的 `WITH` 選項、金鑰／憑證／時態表／序列、`WITHIN GROUP`、`WHERE CURRENT OF`、`FETCH ABSOLUTE／RELATIVE`。

- 新增型別：`SqlClausePhrase`、`SqlClausePhraseCatalog`、`SqlClausePhraseMatch`、`SqlCaretPosition`、`SqlKeywordPositionAnalyzer.OptionLists`、`SqlKeywordPositionExtensions`、`SqlStatementCandidates`
- 文件：`docs/completion-phrases.md`、`docs/phrase-generator.md`（當前分支皆無）
- 為什麼要做：這些字在 ScriptDom 詞法器眼中是識別字，`TSqlTokenType` 沒有它們，位置旗標也切不到 `SET NOCOUNT`／`SET DATEFORMAT` 的差別
- 相關提交：`8a4f731`、`99b49b5`、`45dbf7b`、`0ff5782`、`09aca07`、`3cd56b9`、`9cdf2f4`、`4b5d1bc`、`71ba569`、`dc71bb2`、`0288d22`、`8f91ac6`

### 2.2 執行個體名單：定序 + 語言 + 時區三份統一

- 新增型別：`SqlInstanceList`、`SqlInstanceListData`、`SqlInstanceListEntry`、`SqlInstanceValueForm`、`SqlInstanceListQuery`、`SqlServerInstanceListCache`
- 三份名單（`sys.fn_helpcollations()` / `sys.syslanguages` / `sys.time_zone_info`）共用一條管線，位置、指令碼已用值、排名分級與插入文字只寫一次
- **當前分支只有定序**（`SqlCollationCatalog` + `SqlServerCollationCache`），缺 `SET LANGUAGE`／`DEFAULT_LANGUAGE =` 的語言名單與 `AT TIME ZONE` 的時區名單
- master 已刪 `docs/completion-collation.md`，改由 `docs/completion-instance-lists.md` 取代

### 2.3 位置與範圍分析的統一規則

- 新增 `SqlStatementBoundaries`（`IsStatementHead`／`IntroducesDataSource`），**刪掉 `SqlScopeAnalyzer` 裡寫死的 `StatementKeywords`**
- `FROM` 只在 `SELECT`／`UPDATE`／`DELETE` 底下接資料來源，由動詞決定；位置分析、`ClauseAnchors`／`ListAnchors`、`DetermineTarget`、範圍分析共用同一條
- 未關括號改一次算完查表 `MapUnclosedParentheses`，避免整份掃描變成平方
- 新增位置：觸發程序事件清單、`MERGE`／權限／`SELECT INTO`／`FETCH`／索引鍵／`UPDATE SET` 尾端、外部索引鍵／函式呼叫／視窗排序／`OFFSET` 之後、模組標頭／函式參數清單／`EXEC` 與 `RAISERROR` 的 `WITH`、`EXEC … WITH RESULT SETS`、`BACKUP`／`RESTORE` 的 `TO`／`FROM` 備份裝置、`OPEN`／`FETCH … FROM` 指令碼游標、`FOR` 之後

### 2.4 內建說明大擴充

| 項目 | 當前分支 | master |
|---|---|---|
| 資源結構 | `BuiltInDocs.json` 單檔，`version 1` | 拆成 `functions`／`statements`／`system-procedures`／`tables`／`types-hints` 五類，`version 2` |
| 說明條目 | 213 條 | 263 條（194 + 32 + 18 + 19） |
| 範例 | 單段 | **多段範例**，段間自動加 `GO` |
| 系統程序 | 無 | 18 支常用系統預存程序的參數、寫法與範例 |
| 語句說明 | 少 | 32 筆，涵蓋交易、錯誤處理、DDL 與維護命令 |
| 名稱辨識 | 無 | 游標下的系統程序與語句認得出來，**說明搶在物件解析之前**（`SqlBuiltInObjectResolution`） |
| 系統物件退路 | 無 | `SqlSystemObjectFallback`：系統結構描述限定與未限定的 `sp_`／`xp_` 退回系統物件 |

- 四條入口（提示、預覽、快速資訊、說明面板）共用同一個說明決策點（`e8fcba5`）
- 品質修正：`COALESCE` 型別陷阱必重現、`CONVERT style` 補 108、`QUOTENAME` 預期結果、`MERGE` 範例用別名、名稱跨種類不得重複

### 2.5 關鍵字／位置資料改由產生器推出

| 檔案 | 當前分支 | master |
|---|---|---|
| `SqlKeywordCatalog.Generated.cs` | 247 行 | **5,724 行**（關鍵字、位置、保留字全部由 ScriptDom 探測產生） |
| `SqlKeywordCatalog.cs` | 176 行 | 225 行 |
| `Keywords/` 目錄 `.cs` 檔 | 15 | 23 |

- `tools/Generate-Keywords.ps1`：新增第四階段片語探測（從 `CodeGenerationSupporter` 全部字串常數找候選，用剖析器驗證接不接得上）、**剖析結果存成快取**、第三階段改由 C# 平行跑（`1f5135a`）

### 2.6 預覽：動態島 / 釘住 / 可移至工具窗

- **動態島風格**，收起時機統一為一條規則（`43d5043`）
- **釘住後可自由搬動縮放**，尺寸只記一份（`adeb9e7`）
- 預覽分離外殼、**可移至工具窗**、進場走膠囊等待、釘住支援借用（`d4d72fb`、`d292ed5`）
- **片段與語句骨架可預覽**，抬頭一顆箭頭收合、改「更多／收起」連結（`855098b`、`f6daf0d`、`8d3a32a`）
- 文字選取改用專屬覆蓋色，不再借列選取而淡到看不見（`dd33701`）
- 新檔：`PreviewSurface`（788 行）、`SqlStructurePresenter`、`SqlStructureToolWindow`、`SurfaceCapsule`、`SurfaceMotion`、`TextSelectionColors`、`SqlToolWindowPane`、`PreviewLifecycle`、`PreviewReveal`、`PreviewDragEngine`
- 當前分支仍是舊的 `PreviewResizeEngine` + `SqlPreviewPlacement`

### 2.7 SQL Search：以完整度取代掃描預算

- **取消**每 provider 100 筆 / 2 萬候選 / 400 ms 的預算；改為**一律掃完**，延遲靠取消控制（多打一個字、按停止、收起視窗）
- 補上**進度、停止與建索引通知**；頁尾照實說「找到 N 項，列出前 M 項」
- 完整與不完整**兩種都要說**；沒說結局的目標預設記成已取消，**預設不是完整**
- 範圍「全部」時，進不去的資料庫照樣宣告成目標並當場說讀不到，不開連線
- 新檔：`SearchRun`、`SearchProgress`、`SearchEta`、`SearchTarget`、`SqlSearchCoverage`、`SqlProgressStrip`、`SqlCatalogServerTextSearch`（伺服器端比對）
- 文件：`docs/search-coverage.md`（當前分支無）
- 當前分支仍是舊的 `SearchBudget`／`SearchProviderProgress`／`SearchExamineCounter`

### 2.8 參數提示自動請回

- 提示被收掉後**自動請回**，外層簽章讓位給內層呼叫（`daa90bb`）
- 只在真的浮出時通知，判斷不再每次停手就冒一列（`d195c1a`）
- 新檔：`SqlParameterHintRevival`、`SqlParameterHintKeeper`；`SqlCallSignature` 新增 `IsQualified` 與 `includeKeywordFunctions`，純量函式只對有限定字的呼叫查中繼資料
- 附示意動畫 `docs/images/parameter-hint-demo.gif`

### 2.9 補全建議清單新管線（架構級替換）

master **刪除** `SuggestionMatch`／`SuggestionMatcher`／`SqlCompletionFilters`，改為：

`SuggestionList`、`SuggestionListView`、`SuggestionScore`、`SuggestionMark`、`SuggestionCategory`、`SuggestionCategorySet`、`SuggestionCategoryFilter`、`SuggestionContextFilter`、`SqlCompletionPolicy`、`SqlCompletionSlot`、`SqlColumnOwner`（285 行）、`SqlScopeAliasSuggestions`（相互關聯子查詢看得到外層別名，`a80e5b4`）

使用者可見的新東西：

- **建議清單列尾加入例外標記**（`1b1d478`、`9f6e6cc`）
- **篩選列改為位置數字快捷鍵**與單一規則，預覽沒有結構時不出現（`cf956c8`、`SqlCompletionFilterBar`）
- 右側說明與物件種類名稱改走 `SqlKindText`（`8357bc2`）
- 左方括號之後照樣列出建議，提交時保留方括號（`0df6ea1`）
- 候選集合封閉的位置空前綴就開清單，自己開的清單一律軟選（`e4b57a5`）
- 平台會自己開清單時不再重開，消除打 `[` 時的開、關、開（`a895705`）
- `EXEC #` 列出暫存程序（`0738128`）
- **以合法語料稽核關鍵字召回**，已知缺口逐條附理由，最後移除豁免名單（`84a03b7`、`a044535`）

### 2.10 Metadata 修正

- 系統模組、CLR 與擴充預存程序的定義與參數（`cb741de`、`SqlObjectImplementation`）
- 取不到運算式時說死是 `VIEW DEFINITION` 權限並**點名缺的項目**（`06723d6`）

### 2.11 在地化（master 版已收斂）

- 文字產生器升級為 C# 專案 `tools/SqlAssist.TextGenerator`（`SqlTextGenerator`、`SqlTextTemplate`、`LiteralTextAnalyzer`、`SqlTextDiagnostics`）+ 專屬測試專案
- Core 端：`SqlText`、`SqlLanguage`、`SqlLanguageCache`、`SqlTextOverlay`、`SqlTextLanguagesAttribute`、`SqlKindText`
- 各資料夾介面文字**全數**遷移進 resjson、設定頁改走資源並補英文（`2b042dc`）、註冊介面語言設定並接**即時切換**（`f14dcdc`）、命令表與擴充清單改英文中性（`86990d6`）
- 文件：`docs/localization.md`、`docs/localization-glossary.md`

> 當前分支有**另一套平行實作**（4 個重放的 l10n 提交 + `漢化設定`），另有 `SettingsManifestText.cs`／`SettingsPageManifest.cs`。兩邊解決同一個問題，`f14dcdc`（master）與 `9d814e4`（當前分支）處理的都是設定頁文字跟隨介面語言切換。

### 2.12 工具與建置

- `tools/SqlAssist.TextGenerator`（C# 產生器專案）、`tools/Test-CommandTable.ps1`（新增）
- `tools/Check-DocLinks.ps1` 改讀拆檔後的 `BuiltInDocs` 資料夾
- `src/Directory.Build.props`（新增）
- 8 份新文件：`completion-phrases`、`phrase-generator`、`completion-instance-lists`、`search-coverage`、`localization`、`localization-glossary` + 示意圖

---

## 3. master 的**方法**優勢（不只是功能）

| 面向 | 當前分支做法 | master 做法 |
|---|---|---|
| 片語與選項清單 | 手寫在分析器裡 | 由 `Generate-Keywords.ps1` 第四階段拿剖析器**探測**產生，手寫的只有尾巴 |
| 關鍵字與位置 | 手寫／少量產生 | 全量產生 + 快取，第三階段平行 |
| 定序／語言／時區 | 定序一支，另外兩份沒有 | 三份合一的描述子管線 `SqlInstanceList` |
| 語句界線 | `SqlScopeAnalyzer` 寫死 `StatementKeywords` | `SqlStatementBoundaries` 單一規則，四個呼叫端共用 |
| 建議清單 | `SuggestionMatcher` 逐項比對 | 類別／上下文過濾器 + 評分 + 位置分類單一規則 |
| 搜尋完整度 | 掃描預算（掃不完就說「部分結果」） | 掃完 + 取消 + 完整度**明確二選一**宣告 |
| 內建說明 | 單檔單段範例 | 依種類拆檔、多段範例、四入口共用決策點 |
| 預覽生命週期 | `ResizeEngine` + `Placement` | `PreviewLifecycle`／`PreviewReveal`／`PreviewDragEngine` 三權分離 + 共用殼層 |
| 內建物件辨識 | 只認目錄 | 目錄 + 系統物件退路 + 搶在物件解析之前 |
| 召回品質 | 無稽核 | 以合法語料稽核，缺口逐條附理由後歸零 |
| 文件 | 局部 | 8 份新文件覆蓋新機制，且維護在同一提交內 |

---

## 4. 當前分支獨有、**master 完全沒有**的功能

這些是合併時**不能弄丟**的東西，全部由當前分支新增（共同祖先都沒有）：

| # | 功能 | 關鍵檔案 | master 狀態 |
|---|---|---|---|
| 1 | **自動配對區塊骨架** `BEGIN…END`、`TRY/CATCH`（`autoPairBlocks` 開關） | `Pairing/SqlBlockCloser.cs`、`SqlBlockPairAnalyzer.cs` | 只有 `autoPairDelimiters`，**沒有** `autoPairBlocks` |
| 2 | **文字改寫**：補 `dbo.`、`db..obj` → `.dbo.`、`TOP 10` → `TOP (10)` | `Rewriting/SqlSchemaQualification.cs`、`SqlTopClauseParenthesis.cs`、`SqlTextRewrite*.cs` | 無 |
| 3 | **`@變數` 就地改名**（批次界線、F2） | `Rewriting/SqlVariableRename.cs`、`Commands/InlineRenameCommand.cs` | 無 |
| 4 | **六個查詢視窗右鍵指令**：`PasteAsInPredicate`(0x021D)、`PasteAsValues`(0x021E)、`QualifySchema`(0x021F)、`ParenthesizeTop`(0x0220)、`InlineRename`(0x0221)、`InlineRenameKey`(0x0222, F2) | `Commands/CommandIds.cs` | 無（CommandIds 已分岔） |
| 5 | **剪貼簿貼成 `IN (...)`**：每行加逗號、字元加單引號、帶小數的數值不加 | `Metadata/ResultGrid/SqlPastedValueList.cs`、`Editor/SqlPasteValuesAction.cs` | 無 |
| 6 | **`INSERT` 自動帶出全部欄位** | `Metadata/Model/SqlColumnOrdering.cs` | 無 |
| 7 | **自動別名** `SqlAutoAlias` + 資料表來源別名風格設定 | `Completion/SqlAutoAlias.cs`、`Settings/SqlTableSourceAliasStyle.cs` | 無 |
| 8 | **配對鍵（JOIN Key）建議** | `Completion/SqlJoinKey*.cs`（4 檔） | 無 |
| 9 | 介面**漢化** + 設定頁 manifest | `漢化設定`（`74da1ae`）、`Settings/SettingsPageManifest.cs`、`SettingsManifestText.cs` | 無 |
| 10 | 4 份獨有文件 | `docs/paste-values.md`、`text-rewrites.md`、`inline-rename.md`、`completion-join-keys.md` | 無 |

---

## 5. 合併會踩到的三類硬衝突

### A. 在地化重複實作（41 個 `add/add` 衝突）
36 個 `.resjson` + `SqlTextOverlay.cs` + `SqlLanguageSwitch.cs` + `SettingsPageText.targets` + `localization.md` + `SqlTextOverlayTests.cs`。
**這一類要先決策「留哪一套」，否則每個檔都要人工逐條挑。**

### B. 補全管線架構替換（當前分支的功能掛在 master 已刪掉的型別上）
master 刪掉、當前分支改過的 6 個 `modify/delete` 衝突：

- `Completion/SuggestionMatcher.cs` ← 當前分支的 `SqlSuggestion.cs`、`SqlAsyncCompletionSource.cs`、`SqlAsyncCompletionItemManager.cs` 都在用
- `Ssms22/Completion/SqlCompletionFilters.cs` ← 配對鍵 `SqlJoinKeyMatcher` 的接線處
- `Core/Completion/SqlScriptCollationSuggestions.cs`、`SqlScriptDataSourceSuggestions.cs`
- `Core/Keywords/SqlCollationCatalog.cs` ← 被 `SqlInstanceList` 取代
- `Core/Search/SearchProviderProgress.cs` ← 被 `SearchRun`／`SearchProgress` 取代

另外 50 個 `content` 衝突中，以下同時是「當前分支功能」與「master 重寫」的交會點：
`SqlCompletionContextAnalyzer.cs`、`SqlInsertionText.cs`（自動別名接在這）、`SqlArgumentCatalog.cs`、`SqlBuiltInDocCatalog.cs`、`SqlKeywordPositionAnalyzer.cs`、`SqlAsyncCompletionSource.cs`、`SqlAsyncCompletionItemManager.cs`、`SearchAggregator.cs`、`SqlCatalogSearchProvider/Index/Databases.cs`、`SqlStructurePreview.cs`、`SqlStructurePanel.cs`、`SqlSearchBrowser.cs/Model.cs`、`SqlObjectNavigation.cs`、`SqlMetadataService.cs`、`SqlQuickInfoContentBuilder.cs`

**結論：合併後 4 項功能必須重新接到新管線**（自動別名、配對鍵、定序建議、搜尋進度），其餘（配對區塊、文字改寫、就地改名、貼上值、欄位排序）與管線無關，可保留。

### C. VSIX 接線與中繼資料
`SqlAssistPackage.cs`、`SqlAssist.registration.json`、`Menus.vsct`、三個 `.csproj`、`version.json`（`1.4.15` vs `1.4`）、`SettingsPageText.*`、`SqlAssistSettingsStore.cs`、`SqlLanguageSwitch.cs`。
當前分支多了 6 個指令與 2 個設定的接線，**registration.json 的四步同步流程**（`registration.json` → `SqlAssistMonikers` → `SqlAssistSettingsReader` → `SqlAssistSettings` POCO）要人工合。

---

## 6. 建議

### 選項一：整條合併 master 進當前分支（一次收斂）
- 適合：想終止「永遠落後 86 個提交」、願意接受一次大整理
- 成本：97 個衝突 + §5B 的 4 項功能重接 + 兩套 l10n 收斂 + 全套驗證（build clean + 4 個測試套件 + 檔案檢查工具）
- 前提：**先單獨決定 l10n 留哪一套**，否則 41 個衝突無法收斂

### 選項二：維持目前的增量移植
- 適合：風險要最小、每次只動一項行為變更
- 代價：86 個提交永遠落後，補全管線的架構差異會**持續擴大**，越晚合併越貴；且 master 刪掉的型別與當前分支的功能會持續分歧

### 選項三（建議）：分兩階段，先取「不碰補全管線」的高價值項
第一階段只挑**低衝突、高價值**的：

| 項目 | 衝突面 |
|---|---|
| 內建說明內容擴充（多段範例、系統程序、語句 32 筆） | 只碰 `BuiltInDocs*` + 說明目錄，可控 |
| `BuiltInDocs` 拆檔 + `version 2` | 同上 |
| 關鍵字／位置產生器升級與快取 | `Generate-Keywords.ps1`、`Generated.cs`，當前分支未動 |
| Metadata 修正（系統模組／CLR／VIEW DEFINITION 訊息） | `Metadata/`，衝突少 |
| 參數提示自動請回 | `SqlCallSignature` + 2 個新檔，衝突中等 |
| SQL Search 完整度 | 需連帶 `SearchAggregator` 等，衝突中等 |

第二階段再處理補全管線與 l10n 收斂（選項一的內容）。

**動手前必須先回答的一個問題：在地化要留 master 版還是當前分支的漢化版？** 41 個 `add/add` 衝突全部由此而來。

---

## 附錄：一致性核對（本次比較實際執行的檢查）

- `git merge-base HEAD master` → `44f3c47`
- `git rev-list --left-right --count master...HEAD` → `86  21`
- `git cherry -v HEAD master` → 只有 1 個提交等價（4 個 l10n 提交因漢化改動而**不等價**，無法自動去重）
- `git merge-tree --write-tree --name-only master HEAD` → 97 個衝突（未動工作區）
- `git grep` 符號存在性：`SqlBlockCloser`／`SqlSchemaQualification`／`SqlVariableRename`／`SqlAutoAlias`／`SqlJoinKey`／`SqlColumnOrdering`／`SqlPastedValueList` 當前分支 > 0、master = 0；`SqlInstanceList` master = 14 檔、當前分支 = 0
- `git cat-file -e` 檔案存在性：共同祖先**全部沒有**上述兩組型別與 4 份獨有文件
