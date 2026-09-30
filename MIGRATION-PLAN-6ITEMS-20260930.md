# master 六項優勢移植計畫

日期：2026-09-30　目標分支：`dev_260925`（HEAD `9d814e4`）　來源：`master`（`0288d22`）

---

## 0. 移植前的兩項關鍵查證

| 查證 | 結果 | 對計畫的影響 |
|---|---|---|
| 在地化基礎建設是否兩套 | **幾乎完全相同**：`SqlText.cs`／`SqlLanguage.cs`／`SqlLanguageCache.cs`／`SqlTextLanguagesAttribute.cs` 差異 **0 行**；`SqlTextOverlay.cs` 只差 8 行註解 | 41 個 `add/add` 衝突其實只有文字內容差異（每個 `.resjson` 差 3–7 行），**不是兩套實作**。不需要先做 l10n 收斂決策 |
| 當前分支的改動是「在地化」還是「自有功能」 | **自有功能**：受影響檔案的新增行裡 `SqlText.` 出現 0 次 | 不能用「檔案層級取 master 版」一刀切，dev 的自有改動要重貼 |

當前分支的自有改動量（相對共同祖先 `44f3c47`）：

| 檔案 | dev 新增行數 | 內容 |
|---|---|---|
| `Keywords/SqlKeywordPositionAnalyzer.cs` | 18 | `IsPredicateStart`（配對鍵用） |
| `Keywords/SqlArgumentCatalog.cs` | 75 | 自有引數補充 |
| `Keywords/SqlBuiltInDocCatalog.cs` | 30 | 自有說明調整 |
| `Keywords/SqlBuiltInDoc.cs` | 7 | 同上 |
| `Completion/SqlCompletionContextAnalyzer.cs` | 131 | `Predicate` 目標、`INSERT` 省略 `INTO`、`IsMergeInsertAction`、`mayAppendTableAlias` |
| `Completion/SqlCompletionContext.cs` | 23 | `mayAppendTableAlias` |
| `Completion/SqlInsertionText.cs` | 32 | 自動別名接點 |

**這些都是小量改動，可以重貼到 master 的版本上**，不需要重寫。

---

## 1. 六項的依賴關係（不是六件獨立的事）

```
#2 關鍵字與位置（Generate-Keywords.ps1）
        │  第四階段輸出 SqlClausePhraseCatalog.cs
        ▼
#1 片語與選項清單 ────────┐
        │  片語的位置判定要問語句界線      │  DBCC 說明由片語給
        ▼                                ▼
#4 語句界線（SqlStatementBoundaries）   #6 內建說明
                                            │
#3 定序／語言／時區（SqlInstanceList）◄──────┘ 共用 CompletionTarget 與上下文分析
        │
        ▼
#5 建議清單新管線（SuggestionList／Category／Score）
```

三個真正的耦合點：

1. `SqlClausePhraseCatalog.cs` 是 **產生檔**，由 `#2` 的產生器第四階段輸出 → **#1 不能早於 #2**
2. `SqlKeywordPositionAnalyzer.cs` 被 **#1、#4 同時改**（2183 行分歧）
3. `SqlBuiltInDocCatalog.cs` 被 **#1、#6 同時改**（730 行分歧）→ **#1 與 #6 必須同批**

→ 結論：#1 #2 #4 #6 是**一個耦合區塊**，分開做會在同一批檔案上反覆重工。

---

## 2. 建議的三個區塊

### Block A：關鍵字與內建說明鏈（#2 → #4 → #1 → #6）

| 項 | 檔案 | dev 是否動過 | 移植方式 |
|---|---|---|---|
| #2 | `tools/Generate-Keywords.ps1`、`Keywords/SqlKeywordCatalog.Generated.cs`（247→5724 行）、`SqlKeywordCatalog.cs` | **沒有** | 取 master 版，零衝突 |
| #4 | `Keywords/SqlStatementBoundaries.cs`（新）、`Parsing/SqlScopeAnalyzer.cs`（+? 行） | **沒有** | 取 master 版，零衝突 |
| #1 | 新增 7 檔：`SqlClausePhrase.cs`、`SqlClausePhraseCatalog.cs`、`SqlClausePhraseMatch.cs`、`SqlCaretPosition.cs`、`SqlStatementCandidates.cs`、`SqlKeywordPositionExtensions.cs`、`SqlKeywordPositionAnalyzer.OptionLists.cs`<br>改：`SqlKeywordPositionAnalyzer.cs`、`SqlKeywordPosition.cs`、`SqlArgumentCatalog.cs`、`ArgumentText.*.resjson` | 部分動過（18／75 行） | 新檔直接取；改過的檔以 master 版為底，**重貼 dev 的 18／75 行** |
| #6 | 新增 10 份 `Keywords/BuiltInDocs/*.json`（約 8,000 行）、`SqlBuiltInExampleText.cs`、`Editor/SqlBuiltInObjectResolution.cs`、`Metadata/Model/SqlSystemObjectFallback.cs`、`SqlObjectImplementation.cs`<br>改：`SqlBuiltInDocCatalog.cs`、`SqlBuiltInDoc.cs` | 部分動過（30／7 行） | 同上 |

**重要：不需要在沙箱裡跑產生器。** `Generate-Keywords.ps1` 用 `Add-Type`（本環境被安全策略擋），但 `SqlKeywordCatalog.Generated.cs` 與 `SqlClausePhraseCatalog.cs` 都是**入庫的產生檔**，直接取 master 的內容即可。

**Block A 的 dev 功能保全清單**（重貼時逐條核對）：
- `IsPredicateStart`（配對鍵的啟動條件，**不可用 `SqlKeywordPosition.Predicate` 的裸位元取代**）
- `INSERT` 省略 `INTO` 也認、`IsMergeInsertAction`（`WHEN NOT MATCHED THEN INSERT` 不列資料表）
- `mayAppendTableAlias`（`FROM dbo.T, |` 之後接別名）

### Block B：#3 執行個體名單（定序／語言／時區）

- 新增：`Completion/SqlInstanceList.cs`（403 行）、`SqlInstanceListData.cs`、`SqlInstanceListEntry.cs`、`SqlInstanceValueForm.cs`、`Metadata/Querying/SqlInstanceListQuery.cs`、`Metadata/Caching/SqlServerInstanceListCache.cs`
- 移除：`Keywords/SqlCollationCatalog.cs`（dev 改過）、`Metadata/Caching/SqlServerCollationCache.cs`、`Metadata/Model/SqlCollations.cs`、`Completion/SqlScriptCollationSuggestions.cs`（dev 改過）、`SqlScriptDataSourceSuggestions.cs`（dev 改過）、`docs/completion-collation.md`
- 改：`CompletionTarget`（新增 `Collation`／`Language`／`TimeZone`）、`SqlCompletionContextAnalyzer`、設定四步同步（`registration.json` → `SqlAssistMonikers` → `SqlAssistSettingsReader` → `SqlAssistSettings`）
- **風險**：移除的 4 個檔當前分支都改過 → 需確認沒有其他呼叫端

### Block C：#5 建議清單新管線（最貴）

- 新增 15 檔：`SuggestionList`、`SuggestionListView`、`SuggestionScore`、`SuggestionMark`、`SuggestionCategory`、`SuggestionCategorySet`、`SuggestionCategoryFilter`、`SuggestionContextFilter`、`SqlCompletionPolicy`、`SqlCompletionSlot`、`SqlColumnOwner`（285 行）、`SqlScopeAliasSuggestions`、`SqlScriptObjectSuggestions`、`SqlParameterHintRevival`、`Ssms22/Completion/SqlCompletionFilterBar`
- 刪除：`SuggestionMatcher.cs`（dev 改 565 行）、`SuggestionMatch.cs`、`Ssms22/Completion/SqlCompletionFilters.cs`（**dev 自有檔**）
- 改寫：`SqlAsyncCompletionSource.cs`（275）、`SqlAsyncCompletionItemManager.cs`（328）、`SqlCompletionContextAnalyzer.cs`（545）、`SqlCompletionContext.cs`（198）、`SqlInsertionText.cs`、`SqlSuggestion.cs`、`SqlCommitExpansions.cs`
- **必須重接的當前分支功能**：配對鍵（`SqlJoinKeyMatcher` 掛在 `SqlCompletionFilters`）、自動別名（`SqlAutoAlias` 掛在 `SqlInsertionText`／`SqlAsyncCompletionSource`）

---

## 3. 每區塊的驗證協定（沿用既有紀律）

1. `git worktree add` 開獨立工作區 + 備份分支 `-20260930`
2. 每個區塊完成後：`build clean` → 4 個測試套件（Core／Metadata／SqlMemory／Ssms22）→ 檔案檢查工具（`Check-TextFiles`／`Check-DocLinks`／`Check-Docs`）→ 結束碼 **exit 0**
3. 出 `DIFF-REPORT-20260930.md`（每區塊一節）
4. 通過後才進下一區塊

**已知基線**（今日日誌）：Core 2769／2769、Metadata 981、SqlMemory 146、Ssms22 492／494（剩兩條環境性視覺斷言）。移植後對齊這個基線。

---

## 4. 需要先確認的三個分叉

1. **分批方式**：三區塊一次排完，或先做 Block A?
2. **Block C 對配對鍵與自動別名的處理**：重接到新管線並保留，或暫時停用?
3. **Block B 是否連帶移除舊的定序建議管線**（4 個當前分支改過的檔會被刪）?

---

## 5. 工作量預估（相對）

| 區塊 | 新增／改寫行數 | 衝突數 | 難度 |
|---|---|---|---|
| Block A | 約 +15,000（大多是產生檔與說明資料） | 約 10 | 中（新檔直接取，改過的檔重貼小量 delta） |
| Block B | 約 +900 | 約 8 | 中（含刪檔與設定四步同步） |
| Block C | 約 +1,800 新 / −900 刪 / 改寫 ~2,000 | 約 15 | **高**（自有功能需要重接） |

---

# 6. 實測結果（2026-09-30 執行後修正）

## 6.1 原計畫的前提被推翻：三區塊**不能獨立編譯**

實際動手後以 `dotnet build ... -p:Platform=x64` 逐輪收斂錯誤，證明六項在 Core 層互相咬合，
分三塊做會在同一批檔案上反覆重工：

| 輪次 | 剩餘錯誤 | 缺的東西 | 屬於哪一塊 |
|---|---|---|---|
| 1 | 2 | `SqlInstanceValueForm` | Block B |
| 2 | 86 | `SqlInstanceList`、`SqlScopeAliasSuggestions`、`SqlScriptObjectSuggestions`、`SqlColumnOwner`、`SqlCursorDeclaration`、`SqlStringLiteral` + `SqlTokenNavigator`／`SqlIdentifier`／`SqlStatementScope`／`SqlColumnSourceResolver`／`SqlCallSignature` 的 API 變更 | Block B + C |
| 3 | 74 | 多為 resjson 缺鍵 | A + B + C |
| 4 | 48 | 全部落在 dev 專屬檔 | Block B 移除 + Block C 重接 |

**結論：六項要一次移植。** 錯誤驅動的依賴圖如下（可直接照做）：

```
片語（#1）→ 需要 SqlCompletionSlot（橋接）、SqlStatementBoundaries（#4）
片語目錄    → 是產生檔，由 #2 的 Generate-Keywords.ps1 第四階段輸出
插入文字    → 需要 SqlInstanceValueForm（#3 橋接）
建議清單（#5）→ 需要 SqlColumnOwner、SqlScopeAliasSuggestions、SqlScriptObjectSuggestions
```

## 6.2 可直接取 master 版、零衝突的部分（已驗證可行）

- `src/SqlAssist.Core/Keywords/`（整目錄，含 `SqlKeywordCatalog.Generated.cs` 247→5724 行）
- `src/SqlAssist.Core/Parsing/`、`src/SqlAssist.Core/Completion/`、`src/SqlAssist.Core/Snippets/`、`src/SqlAssist.Core/Search/`
- `src/SqlAssist.Metadata/{Model,Querying,Caching}/`
- `src/SqlAssist.Core/SqlAssist.Core.csproj`（差異只有 BuiltInDocs 拆檔）
- 新檔：`SqlBuiltInExampleText`、`SqlCaretPosition`、`SqlClausePhrase*`、`SqlKeywordPositionExtensions`、`SqlStatementBoundaries`、`SqlStatementCandidates`、`BuiltInDocs/*.json` 10 份、`SqlBuiltInObjectResolution`、`SqlSystemObjectFallback`、`SqlObjectImplementation`、`SqlCompletionSlot`、`SqlCompletionPolicy`、`SqlInstanceValueForm`
- **`git checkout master -- <目錄>` 不會刪掉當前分支獨有的檔**（`SqlCollationCatalog`、`SqlJoinKey*`、`SqlAutoAlias`、`SuggestionMatcher` 都還在），所以取目錄是安全的

## 6.3 不需要重貼的部分（原計畫高估）

| 原以為要重貼 | 實測 |
|---|---|
| `SqlDataTypeCatalog.cs`（dev +97 行） | **master 已含同樣的在地化**（`DataTypeText.` 37 處）→ 不需重貼 |
| `SqlGlobalVariableCatalog.cs`（dev +87 行） | master 已含（`GlobalVariableText.` 32 處） |
| `SqlArgumentCatalog.cs`（dev +172 行） | master 已含（`ArgumentText.` 70 處） |
| `SqlBuiltInDoc.cs`／`SqlBuiltInDocCatalog.cs` | 同上，dev 的改動就是在地化遷移 |
| `SqlObjectNavigation.cs` | 同上 |
| **`ArgumentText.resjson` 等文字內容** | 兩邊同源，不需人工合併 |

→ **當前分支真正需要重貼的自有 delta 只有 6 處**（見 6.4）。

## 6.4 必須重貼的自有 delta（精確清單）

| # | 檔案 | 內容 | 來源 |
|---|---|---|---|
| 1 | `Completion/CompletionTarget.cs` | 加 `Predicate`（master 沒有；master 有 `Cursor`／`Collation`／`Language`／`TimeZone`，dev 沒有） | 配對鍵 |
| 2 | `Keywords/SqlKeywordPositionAnalyzer.cs` | 加 `IsPredicateStart`（**不可用裸 `SqlKeywordPosition.Predicate` 取代**：`Any` 是聯集，含 Predicate 位元） | 配對鍵 |
| 3 | `Completion/SqlCompletionContext.cs` | 加 `mayAppendTableAlias` 參數 + `MayAppendTableAlias` 屬性 + 4 個呼叫點 | 自動別名 |
| 4 | `Completion/SqlCompletionContextAnalyzer.cs` | `Predicate` 目標指派、`INSERT` 省略 `INTO` 也認、`IsMergeInsertAction`、`mayAppendTableAlias` 計算 | 配對鍵 + 別名 |
| 5 | `Completion/SqlInsertionText.cs` | 別名／限定字插入邏輯 + `Qualify(name, qualifier, settings)` 靜態方法 | 自動別名 |
| 6 | `Completion/SqlSuggestion.cs` | `JoinKey` 屬性（`SqlJoinKey?`） | 配對鍵 |
| — | `SqlCompletionContext.IsValid` | dev 用它、master 移除了 → 需保留或改寫 dev 呼叫端 | 待確認 |

已封存在 `artifacts/port/blockA/*.devdelta.patch`（12 份，可直接比對）。

## 6.5 resjson 的正確處理方式（重要）

- `.resjson` → 強型別類別是**建置期由 source generator 產生**（`tools/SqlAssist.TextGenerator` 掛成 Analyzer，`src/Directory.Build.props` 匯入）
- 所以**不能用 master 版覆蓋 dev 的 resjson**：dev 的檔含自有鍵（如 `CommandText.PasteAsInPredicate`），覆蓋後 dev 的程式碼會編譯失敗
- 正確做法：**保留 dev 的 resjson，只補 master 的新鍵**。已有現成腳本 `artifacts/port/merge-resjson-keys.py`（本輪已跑，12 檔補 132 鍵）
- master 的新鍵來源包含 `Completion/InstanceListText.{en,zh-Hant}.resjson`（只在 master 有）

## 6.6 當前狀態

| 項目 | 狀態 |
|---|---|
| `dev_260925` | **乾淨、綠燈**（`ae5489d`，`0 Warning(s) 0 Error(s)`，exit 0） |
| 備份分支 | `dev_260925-backup-20260930`（`ae5489d`） |
| 移植進度 | `dev_260925-port6-wip-20260930`（`c836076`）：**SqlAssist.Core 尚有 48 個錯誤** |
| 未動 | Ssms22 專案的錯誤面還沒看到（Core 綠了才會出現）；測試專案尚未調整 |

## 6.7 下一步的精確工作單

**Step 1 — 解決 Core 的 48 個錯誤**

| 錯誤來源檔 | 個數 | 處理 |
|---|---|---|
| `SuggestionMatcher.cs` | 16 | 刪除（master 已用 `SuggestionList`／`SuggestionCategory*`／`SuggestionContextFilter` 取代）；改寫 `SqlSuggestion`／`SqlAsyncCompletionSource`／`SqlAsyncCompletionItemManager` 的呼叫端 |
| `SqlCollationCatalog.cs` | 6 | 刪除（改由 `SqlInstanceList` 提供）；`Keywords/` 下並移除 `docs/completion-collation.md` |
| `SqlScriptCollationSuggestions.cs` | 6 | 刪除（同上） |
| `SqlScriptDataSourceSuggestions.cs` | 4 | 刪除（master 由 `SqlScopeAliasSuggestions`／`SqlScriptObjectSuggestions` 取代） |
| `SqlJoinKey.cs`／`SqlJoinKeyMatcher.cs` | 8 | 保留功能，改寫到新 API（`SuggestionKind`、`SqlSuggestion.JoinKey`） |
| `SqlAutoAlias.cs` | 2 | 保留功能，改寫到新 API |
| `SqlWildcardAnalyzer.cs` | 4 | 取 master 版（`SqlColumnSourceResolver` 建構子改吃 `tokens`） |
| `SqlSchemaQualification.cs` | 2 | 改用新簽章 |

**Step 2 — 重貼 6.4 的 7 處自有 delta**

**Step 3 — Core 綠了之後再看 Ssms22／Metadata／測試專案的錯誤面**（預期需要：`SqlCompletionFilters` 移除、`SqlAsyncCompletionSource`／`ItemManager` 改寫、選單與註冊檔同步、`Menus.vsct` 動到就要跑 `Test-CommandTable.ps1` 並把 `ProvideMenuResource` 版號加一）

**Step 4 — build clean + 四套測試 + 三個檢查工具全綠，出 DIFF-REPORT**

## 6.8 給後續執行者的兩個提醒

1. **不要用 `git merge master`** 繞過：那會把 master 的預覽／搜尋／UI 改造一起帶進來（使用者只要六項），而且 97 個衝突裡的 41 個是重複的在地化內容。
2. **驗證方式**：`git checkout master -- <目錄>` 後直接建置，用錯誤清單驅動補檔最快；每輪把錯誤彙總指令化（`grep -oE 'error [A-Z]+[0-9]+: [^[]*' ... | sort | uniq -c | sort -rn`），錯誤數會從 86 → 74 → 48 這樣收斂。
