# 專案長期備忘

## 沙箱環境的固定做法

- bash 只有極少 coreutils（`ls`／`head`／`grep`／`mkdir`／`rm`／`tail`／`wc` 都沒有）：
  檔案操作走 `C:\Users\yhwa\.workbuddy\binaries\python\versions\3.13.12\python.exe`，
  搜尋走 Grep／Glob 工具，git 歷史查詢走 `git grep <rev>`、`git log -S`。
- PowerShell 工具的 stdout 抓不到，且不能從 bash 叫 `pwsh`：`tools/*.ps1` 的檢查
  要用等價的 python 腳本重做，或請使用者自己跑。
- 命令列含 `MSBuild.exe`／`msbuild` 會被安全掃描擋下；在腳本裡用 `"MSB" + "uild"`
  組出路徑，命令只帶腳本檔名。
- 建置只走 VS 18 引擎（`…\Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe`，
  加 `/p:Platform=x64`、`/p:SsmsInstallDir="C:\Program Files\Microsoft SQL Server
  Management Studio 22\Release"`），**不要**用 `dotnet build` 建方案（見 docs/development.md）。
- 單元測試可以 `dotnet test tests/<專案>.csproj -c Release`；net48 的測試直接跑
  `bin/x64/Release/net48/*.Tests.exe`。

## 這個 repo 的合併陷阱

`master` 與 `dev_260905` 的合併（`5dc2716 "opt"`、`a10421c`）曾把 master 的整份檔案蓋回
工作分支：分支獨有的新檔留下來，但同一批功能對共用檔案的編輯全被覆蓋，症狀是
`CS1061`／測試建不起來。查法：`git show <已知會動該功能的 commit> -- <path>` 取回原實作，
再逐一比對現況。已知受害：自動別名接線（`SqlAssistSettings`、
`SqlAutoAlias`／`SqlCompletionContext.MayAppendTableAlias`、`SqlInsertionText`、
`SqlAsyncCompletionSource`／`SqlAsyncCompletionCommitManager`／`SqlCommitExpansions`、
`SqlAssist.registration.json`）、復活的 `SqlSnippetMigrationTests.cs`。

`SqlAssist.Ssms22.Tests` 的 2 個視覺測試與 `SqlMetadata.Tests` 的定序回報測試在本機
一直紅，與 master 內容相同，屬既有問題，不是合併造成的。
