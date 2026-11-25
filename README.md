# SQL 備份管理 (SQL Backup Manager)

一支給 Windows Server 用的資料庫排程備份與還原工具，支援 **SQL Server（含 Express）**、**MySQL / MariaDB**、**PostgreSQL**。
介面是繁體中文，縮小後會常駐在右下角系統匣。SQL Server Express 沒有 SQL Agent，可以用這支程式來排程備份。

## 功能

### 備份類型
| 資料庫 | 備份類型 | 說明 |
|---|---|---|
| SQL Server | 完整備份 (Full) | 整個資料庫，所有還原的基礎 |
| | 差異備份 (Differential) | 只備份上次完整備份後變動的部分 |
| | 交易記錄備份 (Log) | 可還原到任一時間點（需 FULL 復原模式） |
| | 部分備份 (Partial) | 只備份可讀寫檔案群組，略過唯讀檔案群組 |
| | 部分差異備份 | 以部分備份為基底的差異備份 |
| | BACPAC 匯出 | 透過 SqlPackage 匯出結構 + 資料，可搬到其他版本或 Azure SQL |
| MySQL / MariaDB | mysqldump | `--single-transaction`，可含預存程序 / 觸發程序 / 事件，gzip 壓縮 |
| PostgreSQL | pg_dump | custom 格式 (-Fc) 或純 SQL (.sql.gz) |
| | pg_dumpall --globals-only | 角色、密碼、表格空間 |

### SQL Server 備份選項
- 僅複製 (COPY_ONLY)、CHECKSUM、備份後驗證 (RESTORE VERIFYONLY)
- 原生壓縮（Standard 以上）；Express 版可勾選「備份後壓縮成 ZIP」
- **分割備份 (Striped)**：同時寫入多個檔案
- **鏡像備份 (MIRROR TO)**：Enterprise / Developer 版
- **備份加密**：AES_256 等演算法，需在伺服器上建立憑證（Standard 以上，2014+）
- 可備份到本機、網路路徑或 **Azure Blob URL**
- 找不到完整備份時，差異 / 記錄備份會自動改做完整備份

### 連線
- **每個備份工作各自設定連線**：伺服器、驗證方式、帳號密碼都放在工作裡，不同工作可以備份不同伺服器
- **一定要「測試連線」成功才能儲存**，連線沒有驗證過的工作不會執行
- 總覽頁會顯示每個工作的連線狀態，每 10 分鐘自動檢查一次

### 其他功能
- **執行中狀態列**：任何頁面上方都看得到目前正在備份 / 還原的工作、步驟和百分比，點一下開啟「執行明細」（每個步驟的時間與狀態）
- **日誌全部清除**
- **字體大小可調整**（系統設定 → 外觀，9~16pt），間距和列高會一起等比例縮放，文字不會重疊
- **排程**：單次、每天、每週（可選星期幾）、每月（可選最後一天）、固定間隔（可設定時段）
- **保留天數**：超過天數的舊備份自動刪除，只刪除本程式產生的同類型檔案
- **第二備份位置**：備份完成後複製一份到 NAS 或其他磁碟做異地備份
- **Email 通知**：失敗時寄信（也可設定成功時寄），HTML 格式報表
- **備份日誌**：可依日期、狀態、關鍵字查詢，並匯出 CSV
- **總覽**：近 24 小時成功 / 失敗統計、下次備份時間、伺服器資訊、磁碟剩餘空間
- **系統匣**：縮小或關閉視窗後常駐在右下角，圖示顏色顯示最近一次結果（綠 = 成功、紅 = 失敗），完成時跳出通知
- 錯過的排程（程式沒開的時候）可在啟動後自動補做
- 密碼以 DPAPI（本機層級）加密儲存
- 命令列模式，可搭配 Windows 工作排程器使用

### 還原
- 掃描備份資料夾，依資料庫列出所有還原點
- **自動組出還原鏈**：完整 → 最近一次差異 → 之後的交易記錄
- **還原到指定時間點** (STOPAT)
- **尾端記錄備份 (Tail-log)**：覆寫前先備份最後的交易記錄，再一起還原到故障前的最新狀態
- **還原成新名稱**：例如還原成 `ERP_TEST`，不影響原資料庫，資料檔位置會自動調整 (MOVE)
- 覆寫前先自動備份現有資料庫、強制中斷其他連線
- 支援 ZIP 備份、分割備份，以及非本程式產生的 .bak / .trn（會讀取檔頭）
- BACPAC 匯入、MySQL 還原、PostgreSQL 還原（pg_restore / psql）、角色與權限還原
- 覆寫現有資料庫時，必須輸入資料庫名稱才會執行

## 系統需求
- Windows Server 2012 R2 / 2016 / 2019 / 2022，或 Windows 10 / 11
- .NET Framework 4.8（Server 2019 已內建）
- SQL Server 2008 R2 ~ 2022（含 Express）
- 選用：
  - BACPAC 需要 [SqlPackage](https://learn.microsoft.com/sql/tools/sqlpackage/sqlpackage-download)
  - MySQL 需要 mysqldump.exe / mysql.exe
  - PostgreSQL 需要 pg_dump.exe / pg_restore.exe / psql.exe
  - 這些工具會自動到常見的安裝位置尋找，也可以在工作的「1. 連線」頁手動指定

## 安裝（Server 2019）
1. 從 [Actions](../../actions) 下載最新的 `SqlBackupManager` artifact，或從 Releases 下載 zip
2. 解壓到例如 `C:\Tools\SqlBackupManager\`
3. 執行 `SqlBackupManager.exe`，到 **備份工作** → **新增工作**
4. 在「1. 連線」頁填好伺服器（可按「偵測本機」）→ **測試連線**，成功後再設定備份內容、存放位置和排程，最後儲存
5. **系統設定** → 勾選「Windows 登入後自動啟動」

### 備份到網路磁碟機 / NAS
備份資料夾填 `\\NAS\Backup` 或對應的磁碟機 `Z:\Backup`，程式會**自動偵測**是網路路徑，並要求輸入 NAS 帳號密碼、按「驗證網路路徑」成功才能儲存。
SQL Server 的備份檔是由 **SQL Server 服務帳戶**寫入的，它預設無法登入 NAS（作業系統錯誤 1326 / 5），所以依 SQL Server 所在位置自動處理：

| SQL Server 在哪裡 | 做法 |
|---|---|
| 和本程式同一台電腦 | SQL 先備份到本機暫存 (`C:\ProgramData\SqlBackupManager\stage`)，本程式再用 NAS 帳號複製過去 |
| 另一台電腦（建議） | 讓 SQL Server 自己用 NAS 帳號連線（`xp_cmdshell net use`，Microsoft 文件記載的做法），直接備份到 NAS；xp_cmdshell 原本關閉的話用完會自動關回。SQL 帳號需有 sysadmin |
| 另一台電腦（主機中轉） | SQL 先備份到它自己電腦的資料夾，本程式用該電腦的 Windows 帳號經 `\\主機\C$` 取回再送到 NAS |

還原時，NAS 上的備份檔也用同樣方式讀取。

### 重要：資料夾權限
SQL Server 的備份檔是由 **SQL Server 服務帳戶** 寫入的，不是你登入的 Windows 帳號。
服務帳戶預設是 `NT Service\MSSQL$SQLEXPRESS`，請在備份資料夾的「安全性」頁籤給它「修改」權限。
（在工作的「1. 連線」頁按「測試連線」，結果裡會顯示服務帳戶名稱。）

### 建議：用 Windows 工作排程器做保險
伺服器重開機後如果沒人登入，程式就不會啟動。可以在「工作排程器」另外建一個工作：
- 勾選「不論使用者登入與否均執行」
- 動作：`"C:\Tools\SqlBackupManager\SqlBackupManager.exe" --run "工作名稱"`

| 參數 | 說明 |
|---|---|
| `--run "名稱"` | 執行指定的工作，不顯示畫面，結束代碼 0 = 成功、1 = 有失敗、2 = 找不到工作 |
| `--run-all` | 執行所有啟用中的工作 |
| `--minimized` | 啟動後直接縮小到系統匣 |

## 檔案位置
- 設定檔：`C:\ProgramData\SqlBackupManager\settings.xml`
- 日誌：`C:\ProgramData\SqlBackupManager\logs\backup_yyyyMM.log`
- 備份檔名：`資料庫_類型_yyyyMMdd_HHmmss.副檔名`，例如 `ERP_FULL_20261124_020000.bak`

## 編譯
- **GitHub Actions**：推送到 `main` 時會自動編譯，推送 `v*` 標籤時會建立 Release
- **Windows**：`dotnet build SqlBackupManager.csproj -c Release`
- **Linux (Mono)**：`./build.sh`
