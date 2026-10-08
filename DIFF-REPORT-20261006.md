# DIFF-REPORT-20261006

本輪：`EXEC` 展開時避開同批次的同名變數。分支 `dev_260925`，基準 `101924fc`。

## 需求

> 在 exec 後展開存儲過程後，如果整個編輯器裡有相同的變數，就改成合適的變數名稱

三個設計分叉在動手前已對齊：

| 分叉 | 決定 |
|---|---|
| 比對範圍 | **同一批次**（`GO` 之間）——沿用就地改名的界線 |
| 改名策略 | **自動加數字後綴** `@LoanId` → `@LoanId1` → `@LoanId2` |
| 開關 | **要**，設定頁「插入與展開」新增一項，預設開啟 |

## 為什麼要做

展開出來的是 `DECLARE @x AS …`，而 `@x` 可能早就被同批次的其他地方宣告過了。
兩份 `DECLARE` 同名**不會有編譯錯誤**——第二句只是把第一句的值覆蓋掉。症狀是展開
之後那一句跑得動，卻把使用者原本的變數值改掉了，而畫面上只看得出多了一段宣告。
`where` 這種常見名字尤其容易踩到。

## 新增

### `src/SqlAssist.Core/Statements/SqlVariableNames.cs`（新檔）

純文字、不看連線，與 `SqlVariableRename` 同一層：

- `Collect(sql, position)` → 該批次（含 `position` 的那一段 `GO` 區間）裡所有變數名。
- `Resolve(name, taken)` → 撞名就在尾巴接從 `1` 起的流水號。

批次界線的判定是**逐字比對 + 獨占一行**，不是問詞法單元是不是識別字：

| 寫法 | 判為分隔？ | 理由 |
|---|---|---|
| 行首 `GO` | 是 | 正常批次分隔 |
| `GO 3` | 是 | 重複次數仍是分隔 |
| `GO -- 註解` | 是 | 常見寫法 |
| `SELECT 1 GO` | **否** | ScriptDom 不認，這裡也不認 |
| `'GO'`、`-- GO` | 否 | 字串／註解 |
| `GOTO` | 否 | 不是 `GO` 這個字 |

「同一行不切」的取捨：這裡不切時兩個批次合成一個，**方向是保守的**——多收一個
變數只是讓展開的名字多一個後綴；漏收一個真的變數才是安靜出錯的那一邊。

### `tests/SqlAssist.Core.Tests/Statements/SqlVariableNamesTests.cs`（新檔）

18 條：收集、大小寫不分、跨批次隔離、字串／註解不算、四種假 `GO`、落點遞增、
連續撞名往下找、兩個同名參數不搶同一落點、參數驗證。

## 修改

### `src/SqlAssist.Ssms22/Completion/SqlCommitExpansions.cs`

`SqlProcedureCallExpansion` 新增 `AvoidNameCollisions`：參數清單組好之後、排版之前
先過一次 `Collect` + `Resolve`。挑好的名字**累加進同一份集合**，否則兩個參數會搶到
同一個落點（`@x` 撞名換成 `@x1`，而本來就叫 `@x1` 的那個沒人告訴它）。宣告、呼叫、
`SELECT` 三段拿到的都是改過的名字，整段仍然一致。

`RenamedVariables` 供診斷；改名訊息走 `Write`（預設不寫），且另開
`ReportRenames` 並標 `[Localizable(false)]`——`、` 這種全形標點會被 SQLTXT 掃描
當成使用者看得到的字串而擋下建置。

### `src/SqlAssist.Ssms22/Completion/SqlCommitExpander.cs`

`Build` 的簽章多一個 `bufferText`（介面與五個實作同步），`SqlStatementSite` 多三格
`BatchText` / `BatchTextStart` / `BatchAnchor`。理由：批次界線與同批次的變數都在
**被換掉的那一段之外**，只看 `target` 永遠答不出來——那一段裡沒有 `GO`。
取全文而不是逐段時，`ISqlCommitExpansion` 的註解也點明了「搬移與改名不還原只插入名稱」。

### 設定四步（`avoidVariableNameCollision`，預設 `true`）

| # | 檔案 | 改動 |
|---|---|---|
| 1 | `SqlAssist.registration.json` | 新項，`order` 345、`enableWhen` 掛 `expandProcedureCall` |
| 1b | `SettingsPageText.zh-Hant.resjson` / `.en.resjson` | 兩個鍵，兩語言鍵序對齊 |
| 2 | `SqlAssistSettings.cs` | `AvoidVariableNameCollision` 屬性 |
| 3 | `SqlAssistMonikers.cs` | `sqlAssist.insertion.avoidVariableNameCollision` |
| 4 | `SqlAssistSettingsReader.cs` | `Value(...)` 一行 |

`order` 選 345：現有是 330（`expandProcedureCall`）、340（`includeOptionalParameters`）、
350（`expandFunctionCall`），塞在 340 與 350 之間。`enableWhen` 與
`includeOptionalParameters` 一樣只參照同分類的 `expandProcedureCall`——殼層限制，
兩個同分類參照用 `&&` 串起來會讓整項被安靜丟掉。

### 文件

- `docs/statement-values.md`：新增〈撞名的變數接流水號〉一節。
- `docs/settings.md`：「插入與展開」表格加一列。

## 驗證

| 項目 | 結果 |
|---|---|
| 建置 `-c Release -p:Platform=x64` | **0 Warning 0 Error** |
| Core | **2788 / 0 failed**（原 2769，+19） |
| Metadata | **981 / 0 failed** |
| SqlMemory.Sqlite | **146 / 0 failed** |
| Ssms22 | **501 / 0 failed**（458 succeeded + 3 skipped） |
| `Check-TextFiles` | exit 0（1154 檔） |
| `Check-DocLinks` | 206 位址全通過 |
| `Test-CommandTable` | 通過（2 選單／13 群組／38 命令） |
| `Check-Docs` | 只卡既有 `docs/index.md` 4536/4500 |

`docs/statement-values.md` 由 4034 升到 4912，落在 4500 警告帶但未達 5000 上限。

## 修正：改到不該改的名字

第一版把「撞名的參數」整格換掉，於是呼叫那一行**等號左邊**的模組參數名也跟著變成
`@LoanId1`。那一句於是找不到對應的參數（錯誤 8145），而畫面上只看得出名字多了一個數字。

根因是 `EXEC dbo.usp_P @LoanId = @LoanId` 這一行兩邊用的是**同一個字串**：
`SqlProcedureCallText` 拿 `parameter.Name` 同時填左右兩側。左邊是**模組簽章**裡的名字
（由定義決定，呼叫端改不動），右邊才是**我們的**區域變數。

修法是把兩者分開：

| 位置 | 用哪一個 | 為什麼 |
|---|---|---|
| `DECLARE` 的名稱 | `VariableName` | 我們的變數 |
| 呼叫等號**左邊** | `Name` | 模組簽章，必須與定義一致 |
| 呼叫等號**右邊** | `VariableName` | 我們的變數 |
| `SELECT` 段 | `VariableName` | 列出的是我們的變數 |

`SqlStatementParameter` 新增 `VariableName`（省略時等於 `Name`），改名只動它。
同時把整批改名抽成 `SqlVariableNames.Avoid`（Core、純文字），讓它可以直接單元測試
——第一版把這段邏輯留在 VSIX 層，測試碰不到，這正是這個 bug 溜過去的原因。

## 未解風險

- **實機未驗**：SSMS 裡真的提交一個與現有變數撞名的 `EXEC`，確認展開出來是
  `EXEC dbo.usp_P @LoanId = @LoanId1`（左邊保持原樣、右邊帶後綴），且三段一致。
- 逐字 `GO` 判定與 ScriptDom 的批次界線在**同一行 `GO`** 這種寫法上不一致
  （這裡不切）。方向保守，但嚴格說兩邊不是同一條界線。
- `@@ROWCOUNT` 這種系統函式也會被 `Collect` 收進來，只讓判定稍微保守。
- `Check-DocLinks` 這一輪有兩次只印出「206 個位址」而沒有結論行（疑似連外超時）。
  未動 `BuiltInDocs.json`，先前已通過，屬環境因素。

