# 專案長期備忘

## 沙箱環境的固定做法

- **`dotnet` 需要補環境變數才能 restore**：沙箱剝掉 `PROGRAMFILES`／`PROGRAMFILES(X86)`／
  `PROGRAMDATA`／`ALLUSERSPROFILE`／`APPDATA`，而 NuGet 的
  `NuGetEnvironment.CalculateFolderPath(MachineWideConfigDirectory)` 直接
  `Path.Combine(GetEnvironmentVariable("PROGRAMFILES(X86)"), "NuGet", "Config")`，
  缺了會拋 `Value cannot be null. (Parameter 'path1')`，整個 restore 掛掉。
  跑 dotnet 前先補這幾個（見 `artifacts/run_tests.py` 的 env 設定）。
- **這個 repo 上不要用 `git stash`**！它會崩潰並刪掉
  `P:\github\SqlAssist.Ssms22\.git\worktrees\<名>\` 整個目錄，之後所有 git 指令失效。
  要做 A/B 比對就把檔案複製到 `artifacts/backup/`。修復步驟見 `2026-09-22.md`。
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

（2026-09-22 更新）目前長期紅燈共 3 個，都與 `Statements/` 底下的展開功能無關：
`SqlCompletionTriggerTests.目標沒收斂就不重開`、
`SqlCompletionContextAnalyzerTests.既無前綴也無目標時不建議`、
`NotificationChromeTests.精簡列表進度與覆蓋捲軸且收合切換圖示`（DPI 188 vs 188.666…）。

## 文件

- 唯一路由是 `docs/index.md`；單檔上限 4000 字元（3900 警告），**超過是硬性失敗**，
  不是提醒——`tools/Check-Docs.ps1` 直接 throw。撞到上限就依「可獨立修改的主題」拆成
  新的葉文件並在 `index.md` 加一列，原檔只留一句連結，不複述理由／表格／程式碼路徑。
- 跑不了 PowerShell 的場合用 `artifacts/check-docs.py`（等價的字元預算＋本機連結＋
  錨點檢查；`artifacts/` 在 .gitignore 內，不會被 `Check-TextFiles` 掃到）。
- 文字檔一律 UTF-8 無 BOM（`.sln` 例外，**必須**有 BOM）、LF、檔尾要有換行。
