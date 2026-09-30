# 差異報告：設定頁文字跟隨 SqlAssist 語言設定（2026-09-30）

使用者回報：設定頁的文字不會跟著「介面語言（Interface language）」下拉框切換。

根因不是漏翻，是**機制**：設定頁的文字由 SSMS 殼層解析，而那條路看不到 SqlAssist 的語言設定。
本輪把註冊檔改成「依設定換成該語言的字面值」，讓殼層沒有鍵可查、只能照著畫。

## 一、根因（已實證）

| 環節 | 事實 | 證據 |
|---|---|---|
| 誰畫設定頁 | 殼層。文字是註冊檔裡的 `"@鍵;{packageGuid}"` | `SqlAssist.registration.json` 233 處引用 |
| 用哪個語言查表 | **殼層自己的**介面語言：IDE 的 International Settings → OS 介面語言 → 英文 | Microsoft 文件〈Localizing your VS package resources〉；`settings.json` 的 `environment.internationalSettings.language = zh-cn`，且 HKCU 的 SSMS／VisualStudio 樹無覆寫 |
| 本機的結果 | 殼層 zh-CN → .NET 後備鏈要 `zh-Hans` 衛星；我們只出 `zh-Hant`，`zh-Hans\` 只有 ScriptDom 的 → 退回中性資源（英文） | 部署資料夾 `ljk05jmd.h3r\zh-Hant\` 有 `SqlAssist.Ssms22.resources.dll`、`zh-Hans\` 沒有 |
| 快取在哪 | 殼層的定義快取 `…\UnifiedSettings\DefinitionCache.dat` 以 `CacheTag` 為鍵，標籤就緊接在註冊檔路徑前 | 該檔在路徑前 8 bytes 就是現行 `08df1e95b7eabcd7`，舊值已不在 |
| 何時讀註冊檔 | 啟動時，**早於套件載入** | pkgdef 的 `SettingsManifests\{guid}` → `ManifestPath` |

四種可能的「指定語言」寫法（`@鍵;{guid}`、`@鍵;組件檔`、`#id;{guid}`、純字面值）與
`registration.schema.json`（根層只有 `properties`／`categories`、`additionalProperties: false`）
都查過：**沒有任何 locale 維度**，全部走上面那條鏈。

## 二、設計

`SettingsPageManifest` 在切換語言時，把安裝資料夾裡的註冊檔換成該語言的字面值，並改寫 pkgdef 的
`CacheTag`（取產物的內容雜湊），讓殼層下次啟動重讀。

- **樣板**：`SqlAssist.registration.json` 另外收成內嵌資源，與版控那一份同源，不另存副本。
- **文字**：讀內嵌的 `SettingsPageText.<語言>.resjson` **原文**，不走 `ResourceManager`。
  那要載衛星組件，而探測路徑失效時它只會安静退回中性資源——正是這次的失敗模式；
  本專案文件自己也記過「衛星載不到只會安靜退回」。殼層用的 `.resources` 與衛星組件照舊。
- **快取鍵**：內容雜湊而非時戳 → 內容沒變就不寫檔、不發通知，每次啟動都呼叫是安全的。

被排除的路：補 `zh-Hans` 衛星（只能跟隨 SSMS，做不到跟隨本設定）、
執行期改登錄／`ManifestPath`（`$PackageFolder$` 在 pkgdef，改不動也不必改）。

**代價**：殼層在啟動時讀註冊檔，所以切換語言要**重新啟動 SSMS**。切換當下會發一則通知
（`NotificationKind.Settings`、`Notice` 等級，不受詳細度過濾）說明這件事。

## 三、變更清單

| 檔案 | 變更 |
|---|---|
| `Core/Localization/SettingsManifestText.cs` | 新增。實體化器（跳過註解行、JSON 跳脫、換檔頭說明）＋ `ReadTable` 讀 `.resjson` |
| `Ssms22/Settings/SettingsPageManifest.cs` | 新增。取得樣板與文字表、以 `JsonReader` 驗證、改寫兩個檔、發通知 |
| `Ssms22/Settings/SqlLanguageSwitch.cs` | `Apply` 加 `NotificationOrigin`，接上 `SettingsPageManifest.Apply` |
| `Ssms22/Settings/SqlAssistSettingsStore.cs` | 兩處呼叫改傳 `Startup`／`User` |
| `Ssms22/Settings/SettingsPageText.targets` | 同一批 `.resjson` 再收一份成內嵌純文字（`WithCulture="false"`、`Type="Non-Resx"`） |
| `Ssms22/SqlAssist.Ssms22.csproj` | 註冊檔收成 `EmbeddedResource`（與 `Content` 同源） |
| `Ssms22/SqlAssist.registration.json` | 檔頭說明改寫成新機制 |
| `Notifications/NotificationCatalog.*.resjson` | 加 `SettingsPageLanguagePending`／`…Done`／`…RestartNotice` |
| `Settings/SettingsPageText.*.resjson` | `GeneralLanguageDescription` 改成「本設定頁要重新啟動 SSMS」 |
| `docs/localization.md` | 設定頁那一條與「即時切換」表最後一列改寫 |
| `tests/Core.Tests/Localization/SettingsManifestTextTests.cs` | 新增 16 個測試 |

## 四、驗證矩陣

| 項目 | 結果 |
|---|---|
| 建置 | **0 Warning 0 Error** |
| Core.Tests | **2769/2769**（+16） |
| Metadata.Tests | **981/981** |
| SqlMemory.Sqlite.Tests | **146/146** |
| Ssms22.Tests | 492/494（僅既有的兩條視覺斷言，見下） |
| Check-TextFiles | **通過**（1141 檔） |
| Check-DocLinks | **通過**（206 位址） |
| Check-Docs | 未通過：只有 `docs/index.md` 4536/4500（本輪未動它，等使用者決定） |
| 內嵌資源 | `SqlAssist.Ssms22.SqlAssist.registration.json`、`…SettingsPageText.{en,zh-Hant}.resjson` 都在組件裡 |

兩條 Ssms22 失敗都是已知的環境性斷言：`NotificationIslandTests.所有內容對齊同一條圖示中線與文字起點`
（`Expected: 20, Actual: 20.699999999999999`）與 `SqlMemoryVisualTests…`
（`InkCenter` 為 null，時好時壞；本輪 3 次執行中 1 次通過）。兩個測試檔與 `NotificationLayout` 本輪都沒動。

## 五、部署與驗證步驟

`Install-Extension.ps1` 會先斷言 SSMS 已關閉，再叫出官方 VSIXInstaller 介面（需人工確認），
所以這一步必須手動。

1. 關掉所有 SSMS 與 Extension Manager 視窗。
2. `"C:\Program Files\PowerShell\7\pwsh.exe" -File tools\Install-Extension.ps1`
3. **第一次啟動**：殼層讀到的還是樣板（設定頁英文），套件接著排定新內容並發通知。
   驗收：`%LOCALAPPDATA%\SqlAssist.Ssms22\SqlAssist.log` 出現
   `設定頁文字已排定為 zh-Hant；重新啟動 SSMS 後生效`。
4. **第二次啟動**：設定頁整頁變繁中。驗收：
   - 部署資料夾的 `SqlAssist.registration.json` 與 `artifacts/reference-zh-Hant.json` **逐字相同**
   - pkgdef 的 `CacheTag` = `4B6B35CE57C059D6`（參考產物的 FNV-1a 64）
   - `DefinitionCache.dat` 裡緊接在註冊檔路徑前的標籤換成上面那個值

`artifacts/materialize-reference.py` 是照 C# 規格重做的參考實作，用來產生上面的比對基準。

## 六、已知限制與後續

- **每次重新部署要多一次啟動**：VSIX 安裝會把註冊檔還原成樣板，第一次啟動才排定、第二次才生效。
  要省掉這一次，可以讓 `Install-Extension.ps1` 依 `settings.json` 的 `sqlAssist.general.language`
  就地產生（部署模組已有 `Policy` 分類可掛），但會多一套 PowerShell 版的材料化邏輯。
- 擴充功能清單（`zh-Hant/Extension.vsixlangpack`）與鍵盤頁的命令名稱（`LocCanonicalName`，
  ctmenu 是二進位）仍跟隨 SSMS，本設定管不到；後者要重建命令表才可能動。
- 簡中（`zh-Hans`）尚未有字串表，所以語言列舉仍是 `auto／zhHant／en`；補齊字串表後
  材料化不必改，它本來就是依 `SqlLanguage.All` 走。
- `docs/index.md` 超預算 36 字元（使用者先前決定自行決定）；兩條改法見 `DIFF-REPORT-20260929.md` 第三節。
  `docs/localization.md` 本輪加了設定頁那一列，來到 4966／5000（原 4929，屬「接近上限」警告），
  離上限只剩 34 字元，下次要補內容得先刪冗餘。
