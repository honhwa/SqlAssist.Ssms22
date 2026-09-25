# 差異報告：備份工作樹（2026-09-22）vs 目前方案（dev_260925）

本頁含：備份目錄已實現的功能盤點、與目前方案的差異對照、以及尚未處理的風險。
不含合併步驟；做法見最後一節的建議。

## 一、比對基準

| 項目 | 備份目錄 | 目前方案 |
|---|---|---|
| 路徑 | `C:\Users\yhwa\WorkBuddy\Worktrees\SqlAssist.Ssms22-worktree-backup-20260922\src` | `P:\github\SqlAssist.Ssms22_260924\src` |
| Git 定位 | 工作樹內容最接近 `d2fa277`（2026-09-22，`v1.0`）：519 檔中 436 檔與該提交逐位元組相同 | `dev_260925`，HEAD `44f3c47`（2026-09-24 21:53），`v1.1` |
| 相對基準的規模 | `d2fa277` ＋ 未提交工作：9 新增／74 修改／10 刪除，`+2,462 / -2,660`（92 檔） | `d2fa277` 之後 39 個提交，197 檔、`+12,284 / -4,419` |
| 版號 | `version.json` = 0.20.3（與 `origin/dev_260905` 相同） | `version.json` = 1.1 |

判定重點：

- `d2fa277` 是目前 HEAD 的祖先，所以兩邊是**同一條主線的前後關係**，不是兩套獨立產品。
- 備份未提交的工作中，31 個檔案與 `origin/dev_260905` 逐位元組相同、**0 個與 HEAD 相同**：
  那批工作後來只落在 `dev_260905`，沒有進目前這條線。
- 備份根目錄的 `version.json`、`README` 帶著 `dev_260905` 的值，src 卻停在 `d2fa277`：
  快照混合了兩個來源，不能直接當成某一個提交的還原點。
- 直接對比兩棵樹：158 檔內容不同；備份僅有 24 個 `.cs`（＋1 個 `.xaml`），目前僅有 72 個
  `.cs`（＋`BannedSymbols.txt`）。

## 二、備份目錄已實現的擴充功能

### Core（netstandard2.0，純邏輯）

| 功能 | 主要檔案 | 目前方案 |
|---|---|---|
| 位置感知補全、插入文字、重開清單 | `Completion/SqlCompletionContext(Analyzer)`、`SqlInsertionText`、`SqlSuggestion`、`SuggestionMatcher` | 有（實作不同） |
| `ON`／`WHERE` 配對鍵補全 | `Completion/SqlJoinKey*.cs`（5 檔） | **無** |
| 資料來源自動別名 | `Completion/SqlAutoAlias.cs`、`Settings/SqlTableSourceAliasStyle.cs` | **無** |
| 定序／資料來源／指令碼變數建議 | `Completion/SqlScript*Suggestions.cs` | 有 |
| 關鍵字、內建函式／型別／全域變數目錄 | `Keywords/*` | 有 |
| Tokenizer、範圍分析、欄位來源、指令碼宣告 | `Parsing/*` | 有 |
| 區塊配對與符號高亮開關 | `Parsing/BlockMatcher.cs`、`BlockDisplayRules.cs`、`BlockSymbolHighlight` 設定 | 區塊有，符號開關**無** |
| 模糊比對 | `Matching/FuzzyMatcher.cs`、`MatchProjection.cs` | 有（已收成 `TextMatcher`） |
| 自動配對、星號展開 | `Pairing/*`、`Wildcards/*` | 有 |
| `INSERT`／`EXEC`／`MERGE` 展開與預留值 | `Statements/*` | 有（`EXEC` 行為簡化，見 3.2） |
| 片段模型、包夾、合併、驗證 | `Snippets/*`、`DefaultSnippets.json` | 有 |
| 通知中心、種類開關、合併、摘要 | `Notifications/*` | 有（已改為通知島） |
| SQL Memory：擷取、保留、清理、租約、用量、收藏版本 | `SqlMemory/*` | 有（＋批次刪除／複製） |
| 預覽版面計算、指令碼選項、設定、診斷、更新檢查 | `Preview/*`、`Scripting/*`、`Settings/*`、`Diagnostics/*`、`Updates/*` | 有 |

### Metadata

| 功能 | 主要檔案 | 目前方案 |
|---|---|---|
| 資料庫快照、物件／欄位／索引／參數模型 | `Model/*` | 有（`SqlColumnInfo.IsIndexKey` **已移除**） |
| 索引鍵欄位排前 | `Model/SqlColumnOrdering.cs` | **無** |
| 中繼資料讀取、目錄限定、連線來源 | `Querying/*` | 有 |
| 目錄快取、定序快取、失敗降級 | `Caching/*` | 有 |
| DDL 產生與格式化 | `Formatting/TSqlScriptRenderer.cs` 等 | 有 |
| 結構健檢規則 | `Analysis/*` | 有 |
| 目錄本文／Agent Job 搜尋、索引、資料庫列舉 | `Search/*` | 有（＋ `ISqlSearchTarget`、`SqlSearchOrigin`） |
| 結果格線輸出（`#temp`、`IN`、JSON、Markdown、欄位剖析） | `ResultGrid/*` | 有 |

### Ssms22（net48 VSIX 接線層）

| 功能 | 主要檔案 | 目前方案 |
|---|---|---|
| 原生非同步補全管線、提交展開 | `Completion/SqlAsyncCompletion*.cs`、`SqlCommitExpansions.cs` | 有 |
| 自動配對、關鍵字大小寫、Tab、參數提示、F12 定義 | `Editor/*`、`Signatures/*`、`QuickInfo/*` | 有（＋ Ctrl＋點擊導覽） |
| 區塊標記、總覽邊距、底色 | `Blocks/*`、`UI/Block*.cs` | 有 |
| 物件結構預覽浮層 | `Preview/SqlStructurePreview*.cs`、`PreviewChrome.cs` | 有（已改版） |
| SQL Search 工具視窗、瀏覽、預覽、範圍 | `Search/*`、`UI/SqlSearch*.cs` | 有（＋伺服器亮起、命中導覽） |
| 主從區版面固定上下 | `Search/SqlSearchSplit.cs` | **無** |
| SQL Memory：History／Favorites、清理、用量、收藏版本 | `SqlMemory/*`、`UI/SqlMemory*.cs` | 有 |
| 片段管理、展開、包夾動作／面板／選擇器 | `Snippets/*` | 有 |
| 結果格線動作、單格內容、剖析視窗 | `ResultGrid/*` | 有 |
| 提醒呈現（舊宿主：Surface／WindowHost／Handover） | `Notifications/Notification*.cs`、`UI/NotificationCard.xaml` | **已由通知島取代** |
| 連線監看、中繼資料服務、物件總管、指令碼視窗 | `Connections/*` | 有（`SsmsObjectExplorerServers` **改版**） |
| 工具選單命令、關於、診斷、更新檢查 | `Commands/*`、`Menus.vsct` | 有（＋ `cmdidFocusNotifications`） |
| 自製 UI：Chrome、主題、篩選（含 chip 列）、列與卡片、圖示、對話框 | `UI/*` | 有（chip 列**已移除**） |

### 儲存層

`SqlAssist.SqlMemory.Sqlite`（SQLite 存放、維護批次、用量、租約、收藏）與
`SqlAssist.SqlMemory.Isolation`（隔離行程、自我測試）兩邊都有，只有欄位與批次細節不同。

## 三、差異明細

### 3.1 目前方案新增（備份沒有）

39 個提交帶來的主題，依序：

| 主題 | 代表檔案 |
|---|---|
| 通知島（兩步）與測試通知 | `Notifications/NotificationIsland*`、`NotificationOverlay.cs`、`NotificationAnchor.cs`、`NotificationPlacement.cs`、`NotificationActionRouter.cs`；`UI/NotificationIsland.cs`、`NotificationTicker.cs`、`NotificationActivityStrip.cs`、`NotificationMotion.cs`、`SpringMotion.cs`；Core 的 `NotificationAction*`、`NotificationPrompt.cs`、`NotificationRehearsal*` |
| Ctrl＋點擊預覽、Ctrl+Shift＋點擊定義 | `Editor/SqlClickNavigator.cs`、`SqlClickGestures.cs`、`SqlClickNavigationProviders.cs`、`SqlObjectNavigation.cs`；Core `Parsing/SqlClickTarget.cs`；`UI/SqlClickLinkFormat.cs`；設定 `ClickNavigationEnabled` |
| 字面比對收成共用層、命中導覽 | `Core/Matching/TextMatcher.cs`、`TextMatchState.cs`、`TextMatchOptions.cs`、`MatchCursor.cs`、`MatchHighlights.cs`；`UI/SqlMatchNavigation.cs`、`SqlMatchToggles.cs`、`TextMarkColors.cs`、`MatchPalette.cs` |
| 物件總管與伺服器整合 | `Connections/SsmsObjectExplorerServer.cs`；Metadata `SqlObjectExplorerUrn.cs`、`SqlObjectParent.cs`、`ISqlSearchTarget.cs`、`SqlSearchOrigin.cs`；`Search/SqlSearchCatalogs.Servers.cs`、`SqlSearchConnection.cs`、`SqlSearchActivation.Targets.cs` |
| SQL Memory 批次操作與多選 | `Core/SqlMemory/SqlMemoryBulk.cs`、`SqlMemoryDeletion.cs`、`SqlMemoryCopy.cs`；`UI/SqlCardSelection.cs`、`SqlSelectionBar.cs`、`SqlRowCheck.cs`、`SqlAssistChrome.Selection.cs` |
| 物件預覽改版（膠囊抬頭、分頁、高亮、換行、前景） | `Preview/SqlStructurePreviewControl.cs`（+749 / -296）、`PreviewChrome.cs`、`SqlPreviewPopupAgent.cs` |
| 剪貼簿與表格輸出統一 | `UI/SqlClipboard.cs`；Core `Tabular/SqlTabularText.cs`、`SqlTabularColumn.cs` |
| 清單分頁共用化 | `Core/Lists/PagedLoadState.cs`、`SqlListFooter.cs`；`UI/SqlListPager.cs` |
| 殼層按鍵交還、視窗工具 | `Editor/ShellKeyCapture.cs`、`ShellKeyMap.cs`；`UI/SsmsWindows.cs`、`SqlTabHeader.cs`、`OverlayScrollCues.cs`、`SqlPill.cs` |
| 連線標籤與範圍、編輯器連線文字 | `Core/Connections/SqlConnectionLabel.cs`、`SqlConnectionScope.cs`；`UI/SqlEditorConnectionText.cs` |
| 建置護欄 | `SqlAssist.Ssms22/BannedSymbols.txt` |

### 3.2 備份獨有、目前沒有（未提交，可能遺失）

| 功能 | 檔案 | 規模 | 行為差異 |
|---|---|---|---|
| `ON`／`WHERE` 配對鍵補全 | `Core/Completion/SqlJoinKey.cs`、`SqlJoinKeyMatch.cs`、`SqlJoinKeyMatcher.cs`、`SqlJoinKeySource.cs` | 643 行 | 述詞起點把當前對象的同名欄位整條條件排到最前，插入即寫完 `b.CopyNo = a.CopyNo`；另有 `docs/completion-join-keys.md` |
| 資料來源自動別名 | `Core/Completion/SqlAutoAlias.cs`、`Settings/SqlTableSourceAliasStyle.cs` | 227＋27 行 | 提交資料表／檢視／TVF 時補 `lr`／`AS lr`，衝突加序號；設定三態 `None`／`As`／`Off` |
| 索引鍵欄位排前 | `Metadata/Model/SqlColumnOrdering.cs` + `SqlColumnInfo.IsIndexKey` | 70 行 | 單一資料來源時把索引鍵欄排到清單最前 |
| 主從區固定上下版面 | `Ssms22/Search/SqlSearchSplit.cs` | 28 行 | 門檻固定 `null`，寬版面也不轉左右 |
| 物件總管伺服器列舉改版 | `Ssms22/Connections/SsmsObjectExplorerServers.cs` | 169 行 | 只留顯示名／伺服器名／根 URN，開連線時才回頭問物件總管；取代 `SsmsObjectExplorer.cs` |
| 區塊符號高亮開關 | `Core/Settings/*`、`Parsing/BlockDisplayRules.cs` | 設定 1 項 | `BlockSymbolHighlight` 控制符號類區塊是否上色 |
| `EXEC` 展開較完整 | `Core/Statements/SqlProcedureCallText.cs`、`SqlModuleParameterDefaults.cs` | +171／+133 行 | 備份：每個參數都 `DECLARE`，尾端再 `SELECT` 印出 OUTPUT 值；目前：只為 OUTPUT 參數補 `DECLARE`，不再印值 |
| 通知展開狀態設定 | `Core/Settings/*` | 1 項 | `NotificationExpanded`；已隨通知島移除，屬正常淘汰 |

設定面對照：備份多 `TableSourceAliasStyle`、`BlockSymbolHighlight`、`NotificationExpanded`；
目前多 `ClickNavigationEnabled`。其餘設定兩邊一致。

### 3.3 名稱或位置變動（不是遺失）

| 備份 | 目前 |
|---|---|
| `UI/SqlMemoryPager.cs` | `UI/SqlListPager.cs`（R081） |
| `Snippets/SqlSnippetSurroundKeys.cs` | `Editor/ShellKeyMap.cs`（R091） |
| `Core/SqlMemory/PagedLoadState.cs` | `Core/Lists/PagedLoadState.cs`（R096） |
| `SqlMemory/SqlWindowConnections.cs` | `Connections/SqlWindowConnections.cs`（R090） |
| `UI/NotificationCardItem.cs` | `UI/NotificationActivityItem.cs`（R084） |
| `UI/NotificationCard.xaml(.cs)`、`Notifications/NotificationSurface*`、`NotificationWindowHost.cs`、`NotificationHandover.cs`、`NotificationHostPriority.cs`、`INotificationSurfaceHost.cs`、`Editor/NotificationEditorHost.cs` | 通知島系列（刻意刪除） |
| `UI/SqlFilterChipBar.cs`、`Core/Search/SearchOptions.cs` | 目前主動刪除（範圍篩選改為明確值） |
| `Notifications/SsmsObjectExplorerServers.cs` 的概念 | 拆成 `SsmsObjectExplorer.cs` ＋ `SsmsObjectExplorerServer.cs`，並另加 `SqlObjectExplorerUrn.cs`、`SqlObjectParent.cs` |

### 3.4 兩邊都改過、方向不同（合併時會衝突）

備份相對 `d2fa277` 變動的 83 檔中，52 檔在 `dev_260905` 與 HEAD 兩邊都「不存在相同版本」，
也就是備份處於獨有狀態。集中在：

- **搜尋**：`SearchOptions.cs`、`SearchQuery.cs`、`SearchAggregator.cs`、`SearchHit.cs`、
  `SqlSearchActivation.cs`、`SqlSearchBrowser.cs`、`SqlSearchCatalogs.cs`、`SqlSearchDefinition.cs`、
  `SqlSearchPreview.cs`、`SqlSearchRow.cs`，以及 Metadata 的
  `SqlCatalogSearchProvider.cs`、`SqlCatalogBodySearch.cs`、`SqlAgentJobSearchProvider.cs`。
- **中繼資料**：`SqlMetadataQueries.cs`、`SqlMetadataReader.cs`、`SqlMetadataCatalog.cs`、
  `SqlDatabaseSnapshot.cs`、`SqlQualifierResolver.cs`。
- **UI 與主題**：`SqlAssistChrome.cs` 及其 Buttons／Filters／Rows／Search 分片、
  `SqlFilterBar.cs`、`SqlFilterFlyout.cs`、`SqlSearchList.cs`、`SqlSearchToolbar.cs`、
  `ThemePalette.cs`、`ThemeColorMath.cs`、`SqlIcon.cs`、`SqlIcons.Images.cs`、`SqlHighlightText.cs`、
  `SqlReadOnlyViewer.cs`、`MasterDetailView.cs`、`BlockPalette.cs`。
- **其他**：`Settings/*`（含 monikers）、`SqlAssist.registration.json`、
  `Core/SqlAssist.Core.csproj`、`Preview/SqlScriptDocument.cs`、`SqlScriptTheme.cs`、
  `SqlMemory/SqlMemoryBrowser.cs`、`SqlMemoryPreview.cs`、
  `Completion/SqlAsyncCompletionCommitManager.cs`、`Connections/SqlMetadataService.cs`、
  `Notifications/NotificationCatalog.cs`、`Parsing/SqlObjectPath.cs`、`Matching/MatchProjection.cs`。

## 四、風險與未確認事項

- **備份的 src 混合兩個來源**：src 停在 `d2fa277`，根目錄卻是 `dev_260905` 的值。要還原時不能
  直接整棵覆蓋，否則會把版號與 README 一起帶回 0.20.3。
- **3.2 的功能尚未在任何分支的 HEAD 上**：`SqlJoinKey*`、`SqlAutoAlias`、`SqlColumnOrdering`、
  `SqlSearchSplit` 只在備份與 `dev_260905`（31 檔相同）出現。要留下就得挑出來移植，
  且 `SqlColumnOrdering` 需要 `SqlColumnInfo.IsIndexKey` 一起回來。
- **`SsmsObjectExplorerServers` 與目前的物件總管實作是兩套**：目前另外加了
  `SqlObjectExplorerUrn`／`SqlObjectParent`／`ISqlSearchTarget`，移植時要選一邊，不能並存。
- **`EXEC` 展開的取捨未定**：備份版本會多出一段 `SELECT`，目前版本較短。這不是遺失，
  但是需要確認要哪一種的產品決策。
- **未驗證項目**：本次只做靜態比對，沒有建置或執行測試；`docs/` 與 `tests/` 的差異
  （備份多 `completion-join-keys.md`，目前多 `click-navigation.md`；測試檔目前多 35 個、
  備份多 7 個）未逐一確認內容是否互相涵蓋。

## 五、建議順序

1. 先確認 3.2 的七項哪些要留；不要整棵合併備份。
2. 要留的以「功能」為單位移植到 HEAD：配對鍵、自動別名、索引鍵排序三者互相獨立，可分次做；
   `SqlColumnOrdering` 必須與 `IsIndexKey` 同批。
3. `SsmsObjectExplorerServers` 與現行物件總管二選一，不要並存。
4. 決定 `EXEC` 展開要哪一種，再動 `Statements/`。
5. 3.4 的 52 檔先擱著，除非確定要採用備份那一版的搜尋／UI 行為；兩邊都動過的檔案
   逐檔比對成本高，且目前方案在這些區域的進展遠大於備份。
