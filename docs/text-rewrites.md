# 文字改寫：補齊結構描述與 TOP 括號

本頁說明查詢視窗右鍵的兩個就地改寫命令——「補齊結構描述（dbo.）」與「TOP 補上括號」
——各自會動什麼、不會動什麼，以及為什麼判斷條件刻意這麼窄。

兩者都只作用在目前查詢視窗：有選取就只改選取，沒有選取就改整份文件。判斷與改寫全部
在 `SqlAssist.Core.Rewriting`，只看文字、不需要連線，也不查中繼資料。

## 補齊結構描述（dbo.）

補的是 `dbo.`，不是「目前使用者的預設結構描述」：預設結構描述要看連線才問得到，
而那些指令碼的物件本來就掛在 `dbo`。不是 `dbo` 的物件寫在別人的結構描述下時本來就會
帶限定字，不會落到這條路上。

| 原來寫的 | 改成 | 說明 |
|---|---|---|
| `FROM Loan` | `FROM dbo.Loan` | 沒有限定字 |
| `FROM LibArchive..Loan` | `FROM LibArchive.dbo.Loan` | 空的中間段 |
| `FROM LIBSQL02.LibArchive..Loan` | `FROM LIBSQL02.LibArchive.dbo.Loan` | 跨資料庫加跨伺服器 |
| `FROM dbo.Loan` | 不動 | 已經有結構描述 |
| `FROM sys.objects` | 不動 | 已經有結構描述 |

會被處理的位置只認文法上確定接物件名稱的那幾個字：

- **資料來源**：`FROM`、`JOIN`、`APPLY`、`USING`、`INTO`、`REFERENCES`，以及
  `CREATE INDEX … ON` 那一族的 `ON`（沿用既有的 DDL 判斷）。
- **模組**：`EXEC`／`EXECUTE` 後面那一個，含 `EXEC @rc = 模組` 的傳回值寫法。
- **DML 目標**：**敘述開頭**的 `UPDATE`／`DELETE`／`MERGE` 後面那一個。
- **DDL 物件**：`TABLE`、`VIEW`、`PROCEDURE`／`PROC`、`FUNCTION` 後面那一個。

一律不處理：

- **`sp_`／`xp_` 開頭的模組**。這兩族住在 `master` 的 `sys` 結構描述，
  補成 `dbo.sp_help` 會直接找不到。
- **暫存表與資料表變數**（`#tmp`、`##tmp`、`@rows`），以及**這份文字裡宣告的 CTE 名稱**。
  它們只存在於這份指令碼裡。
- **別名**。`UPDATE a SET … FROM dbo.Loan a` 的 `a` 不是物件，補成 `dbo.a` 之後那句
  UPDATE 會指到別的東西。
- **`ON DELETE CASCADE` 這一類動作子句**。`CASCADE` 就在 `DELETE` 後面，但那個 `DELETE`
  不是敘述開頭。
- **內建的資料來源函式**（`OPENJSON`、`STRING_SPLIT`、`OPENROWSET` 這些）。
  自訂的資料表值函式（`FROM LoanByReader(1)`）則要補。
- **字串與註解**裡的文字。

改寫只插入文字，不動原本的大小寫與空白：`select top 5 * from Loan` 只會變成
`select top 5 * from dbo.Loan`。

## TOP 補上括號

| 原來寫的 | 改成 |
|---|---|
| `SELECT TOP 10 *` | `SELECT TOP (10) *` |
| `SELECT TOP 10 PERCENT *` | `SELECT TOP (10) PERCENT *` |
| `SELECT TOP (10) *` | 不動 |
| `SELECT TOP @n *` | 不動 |

只處理**數字字面值**：`TOP @n` 是合法語法，改它只是風格統一，而使用者按這條命令時
期待的是「把 T-SQL 要求括號的地方補上」。條件也因此窄到只剩「`TOP` 後面緊接著一個
數字」——`TOP` 也可以是資料行或別名，但那時候它後面不會是一個數字。

## 兩條命令都不是自動的

**刻意不做成存檔時自動整理。**判斷錯了就是改壞使用者的 SQL，而存檔是最不希望被打斷的
動作。做成右鍵命令之後，作用範圍由使用者決定，改完的結果就在眼前；按下它的人看得見
選單文字，也就看得到自己按了什麼。

判斷條件一律選擇「認不出來就不要動」：漏掉一處只是少補一個地方，使用者捲一遍就看得出來；
補錯一處會讓那句 SQL 指到別的物件，而且從文字上看完全合理。

## 結果怎麼回報

按下去之後，狀態列會說動了幾處（例如「已補齊 12 處物件的結構描述（dbo.）」）；
一處都沒動時也會說明，不會安靜地什麼都不做。動到的處數同時寫進診斷紀錄的活動欄位，
但那裡只有數量，不含任何 SQL 文字或物件名稱。

## 相關

- 功能開關與其他命令的入口見[功能與設定](settings.md)。
- 文件總覽與其他主題見[文件路由](index.md)。
