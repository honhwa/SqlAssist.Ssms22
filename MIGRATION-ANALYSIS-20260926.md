# Master → dev_260925 遷移分析

日期：2026-09-26
對象：master（領先 20 個提交）→ dev_260925（領先 9 個提交）
共同祖先：`44f3c47`

## 一、現況

| 項目 | 值 |
|---|---|
| 目前分支 | `dev_260925`（工作區乾淨） |
| master 獨有提交 | 20 |
| dev_260925 獨有提交 | 9 |
| 整體差異 | 585 檔、+21,410 / −10,850 |
| `src/` 差異 | 426 檔、+14,915 / −6,972（Ssms22 181、Core 164、Metadata 64、SqlMemory 16） |
| 雙方都改過的檔案 | 48（其中原始碼 33、測試 6、docs 9） |

一個容易誤判的點：`git diff dev_260925..master` 裡標 `D` 的 `SqlAutoAlias.cs`、
`SqlJoinKey*.cs`、`SqlBlockCloser.cs`、`SqlPastedValueList.cs`、`SqlColumnOrdering.cs`、
`SqlTableSourceAliasStyle.cs` **不是 master 刪掉的**——經 `git cat-file` 驗證，這些檔在
共同祖先與 master 都不存在，純粹是 dev 獨有。「只有 `SuggestionMatcher.cs` 是真正
在 base 有、master 已刪、dev 還留著」的重構落差。

## 二、master 20 個提交分組

| 組 | 提交 | 規模 | 性質 |
|---|---|---|---|
| L10n 本地化 | `d0c538f` `2b042dc` `f14dcdc` `86990d6` | 4 提交 / 640 檔 / +12,107 | 基礎建設＋全區遷移 |
| Completion 行為 | `cf956c8` `8357bc2` `1b1d478` `9f6e6cc` `dd0a0f6` `8a4f731` `48fda29` `c633ca4` | 8 提交 / 159 檔 / +8,375 | 行為修正＋一次大重構 |
| Metadata 正確性 | `cb741de` `06723d6` | 2 提交 / 30 檔 / +374 | 純修正，獨立性最高 |
| 參數提示 | `daa90bb` | 1 提交 / 20 檔 / +926 | 新功能 |
| Wildcard | `03ce8ce` | 1 提交 / 5 檔 / +235 | 提示收起邏輯 |
| 文件／工具／版號 | `81f1168` `33b8c81` `64867ee` `2d613ab` | 4 提交 / 19 檔 | 低風險 |

## 三、可遷移改進清單（按建議順序）

| # | 提交 | 改進內容 | 價值 | 主要衝突檔 | 難度 |
|---|---|---|---|---|---|
| 1 | `cb741de` | 系統模組參數改查 `sys.all_parameters`／`sys.all_sql_modules`；新增 `SqlObjectImplementation` 判別本文是 T-SQL／CLR／擴充預存程序；`X` 型別改由 `SqlObjectKinds` 對應，移除查詢裡的 `X→P` | 預覽、F12、CLR 觸發程序不再誤報成「加密或沒權限」 | `TSqlScriptRenderer.cs` | 低 |
| 2 | `06723d6` | 取不到運算式時明確說是 VIEW DEFINITION 權限（名稱與運算式同列回來，NULL 只可能是權限遮蔽），並逐項點名缺的 DEFAULT／CHECK／計算資料行 | 移除「讀取期間被修改」的錯誤猜測，訊息可行動 | `TSqlScriptRenderer.cs`、`TSqlScriptRendererRegressionTests.cs` | 低 |
| 3 | `03ce8ce` | `*` 展開後收起 Tab 提示，提示改由共用 `CaretHint` 重新判斷 | 展開後不再殘留誤導提示 | 無（只動 `Ssms22/Editor/CaretHint.cs`、`Wildcards/SqlWildcardHint.cs`） | 低 |
| 4 | `daa90bb` | 參數提示被收掉後自動請回；外層簽章讓位給內層呼叫 | 巢狀呼叫時提示不再被外層搶走 | `SqlAssistCompletionCommandHandler.cs`、`SqlAsyncCompletionCommitManager.cs` | 中 |
| 5 | `48fda29` | 子句尾端與語句開頭不列資料庫物件 | 減少誤選 | `SqlCompletionContext.cs`、`SqlCompletionContextAnalyzer.cs`、`SqlKeywordPositionAnalyzer.cs` | 中 |
| 6 | `c633ca4` | 資料表變數當欄位限定字一律寫成 `[@t]` | 插入文字不會產生無效識別字 | `SqlInsertionText.cs`、`SqlIdentifier.cs` | 中 |
| 7 | `1b1d478`＋`9f6e6cc` | 建議清單列尾加入例外標記（Destructive／Deprecated／SystemObject／RecentlyUsed），標記說明放在說明面板最上方 | 只標例外，清單可讀性明顯提升 | `SqlSuggestion.cs`（依賴 `SqlKindText`） | 中 |
| 8 | `8357bc2` | 右側說明與物件種類名稱改走共用 `SqlKindText` | 消除各處各自寫種類名稱 | 依賴 l10n 組的 `86990d6` | 中 |
| 9 | `cf956c8` | 篩選列改位置數字快捷鍵＋單一規則；預覽沒有結構時不出現 | 篩選操作一致化 | `SqlCompletionFilterBar.cs`、`SqlAsyncCompletionItemManager.cs` | 中高 |
| 10 | `8a4f731` | 以 ScriptDom 探測子句片語，補上 `SET` 選項等非保留字建議；`SqlClausePhraseCatalog` 改成資料驅動 | 新增片語只需在產生器加一行 | `CompletionTarget.cs`、`SqlCompletionContextAnalyzer.cs` | 中高 |
| 11 | `dd0a0f6` | 建議清單開關與選取改由「位置分類」單一規則推出（三份判斷合一成 `SqlCompletionPolicy`） | 根治「某位置打字有清單、打分隔字元卻沒有」 | `SuggestionMatcher.cs`（master 已刪，dev 還在改）、`SqlCompletionContextAnalyzer.cs` | 高 |
| 12 | L10n 4 提交 | resjson 文字產生器、`SqlText` 語言切換、`SqlAssistTextLanguages`、SQLTXT100 檢查、介面語言即時切換 | 介面語言切換＋文字集中管理 | dev 目前 **0 個 resjson**，全區 UI 文字對撞 | 極高 |

## 四、衝突熱點（雙方都改過的原始碼）

| 區域 | 檔案 |
|---|---|
| Core／Completion | `CompletionTarget.cs`、`SqlCompletionContext.cs`、`SqlCompletionContextAnalyzer.cs`、`SqlInsertionText.cs` |
| Core | `SqlKeywordPositionAnalyzer.cs`、`SqlScriptOptions.cs`、`SqlAssistMonikers.cs`、`SqlAssistSettings.cs`、`SqlAssistSettingsReader.cs` |
| Core／Statements | `SqlProcedureCallText.cs`（dev 的 EXEC 參數宣告）、`SqlStatementParameter.cs` |
| Metadata | `SqlObjectScript.cs`、`TSqlScriptRenderer.cs`、`SqlColumnInfo.cs`、`SqlObjectInfo.cs`、`SqlMetadataQueries.cs`、`SqlMetadataReader.cs` |
| Ssms22 | `SqlAssistCommands.cs`、`SqlAssistCompletionCommandHandler.cs`、`SqlAsyncCompletionCommitManager.cs`、`SqlAsyncCompletionItemManager.cs`、`SqlAsyncCompletionSource.cs`、`SqlCommitExpansions.cs`、`SqlMetadataService.cs`、`SqlDefinitionScript.cs`（dev 的 USE 先行）、`Menus.vsct`、`SqlAssist.registration.json`、`SqlAssistPackage.cs`、`UI/SqlClipboard.cs` |
| 測試 | `SqlCompletionContextAnalyzerTests`、`SqlInsertionTextTests`、`SqlScriptTableCompletionTests`、`SqlAssistRegistrationTests`、`SqlObjectScriptTests` |
| 版號 | `version.json`（master `1.2`／dev `1.2.5`） |

## 五、dev 獨有、master 沒有（合併時不可丟）

- `SqlAutoAlias.cs` — 自動別名
- `SqlJoinKey*.cs`（4 檔）— 配對鍵
- `SqlBlockCloser.cs`＋`SqlBlockPairAnalyzer.cs` — BEGIN／END、TRY／CATCH 自動閉合
- `SqlPastedValueList.cs`＋`SqlPasteValuesAction.cs` — 剪貼簿貼上為 IN
- `SqlColumnOrdering.cs`、`SqlTableSourceAliasStyle.cs`

⚠️ 需確認：master 已有 `SqlInPredicateScript.cs`（結果格線 → `IN` 述詞，含複合鍵展開成 OR、
`NULL` 改寫成 `IS NULL`）；dev 另建 `SqlPastedValueList`＋`SqlPasteValuesAction`。
兩者是否重疊、是否該共用同一份排版，建議先釐清再決定留下哪一份。

## 六、建議遷移順序

1. **第 1 批（低風險、可立即驗證）**：`cb741de` → `06723d6` → `03ce8ce`
2. **第 2 批（功能獨立）**：`daa90bb`
3. **第 3 批（Completion 小改）**：`48fda29` → `c633ca4`
4. **第 4 批（依賴 SqlKindText）**：`86990d6` 的共用詞部分 → `8357bc2` → `1b1d478` → `9f6e6cc`
5. **第 5 批（大重構，需完整 4 套件驗證）**：`cf956c8` → `8a4f731` → `dd0a0f6`
6. **第 6 批（獨立排期）**：L10n 全區遷移

每批維持專案既有紀律：一次一個行為變更 → 完整建置 → 4 個測試套件（Core／Metadata／
SqlMemory／Ssms22）→ 檔案檢查工具（exit 0）。

## 七、待確認

1. `version.json`：master 降到 `1.2`、dev 是 `1.2.5`，要跟哪一個？
2. L10n 是否要全量採用？若只要「介面語言切換」而不要全區 resjson 遷移，可只進 `d0c538f`
   的基礎建設＋`f14dcdc`，但 `8357bc2` 之後的提交都會用到 `SqlKindText`。
3. dev 的 9 個提交是否都已完成並通過 4 套件驗證？若尚未，建議先收尾再開始遷移。
4. 貼上為 IN 的兩份實作是否合併（見第五節）。
