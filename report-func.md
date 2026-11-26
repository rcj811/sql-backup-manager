## SQL 備份管理 自動測試

**✅ 全部通過**：通過 38、失敗 0　（2026/11/26 13:10:21）

| 結果 | 測試項目 | 說明 |
|---|---|---|
| ✅ | 排程計算 (每天/每週/每月/間隔) | OK (0.0s) |
| ✅ | 連線 SQL Server (.\SQLEXPRESS) | Express Edition (64-bit)  16.0.1000.6 RTM｜資料庫 3 個｜服務帳戶 NT Service\MSSQL$SQLEXPRESS｜Express 版不支援原生壓縮，可改用 ZIP｜預設備份資料夾 C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup (1.3s) |
| ✅ | 建立測試資料庫 (FULL 復原模式) | 100 筆 (0.6s) |
| ✅ | 完整備份 → 本機資料夾 | SbmTest_FULL_20261126_130847.bak，3.1 MB，已驗證 (1.9s) |
| ✅ | 執行明細：步驟與百分比 | 4 個步驟：開始執行工作：測試-完整 → [1/1] SbmTest 完整備份 → SbmTest 驗證備份檔… → 完成：成功 1、失敗 0、警告 0 (0.0s) |
| ✅ | 差異備份 | SbmTest_DIFF_20261126_130850.bak，884.0 KB，已驗證 (1.7s) |
| ✅ | 交易記錄備份 #1 | SbmTest_LOG_20261126_130851.trn，236.0 KB，已驗證 (2.0s) |
| ✅ | 交易記錄備份 #2 (中間記下時間點) | SbmTest_LOG_20261126_130856.trn，172.0 KB，已驗證｜時間點 13:08:54 時有 200 筆 (4.4s) |
| ✅ | ZIP 壓縮備份 | SbmTest_FULL_20261126_130858.zip，368.6 KB，已驗證 (18.0s) |
| ✅ | 分割備份 (3 個檔案) | SbmTest_FULL_20261126_130916_1of3.bak，3.4 MB，已驗證，分割 3 個檔案 (2.0s) |
| ✅ | 部分備份 (Partial) | SbmTest_PART_20261126_130918.bak，3.0 MB，已驗證 (1.7s) |
| ✅ | 第二備份位置 (本機複製) | SbmTest_FULL_20261126_130919.bak，3.1 MB，已驗證 (1.5s) |
| ✅ | 保留天數：刪除過期的舊備份 | 過期檔已刪除，其他檔案保留 (1.6s) |
| ✅ | 連線未驗證的工作不會執行 | 已拒絕執行 (0.0s) |
| ✅ | 網路分享：錯誤密碼要驗證失敗 | 正確擋下：無法連線到 \\127.0.0.1\nas：The specified network password is not correct（代碼 86） (2.2s) |
| ✅ | 網路分享：正確帳密驗證可寫入 | OK (0.0s) |
| ✅ | 網路分享：SQL 服務帳戶本身無法直接寫入 (重現 1326 情境) | 如預期失敗：Cannot open backup device '\\127.0.0.1\nas\backup\direct_test.bak'. Operating system error 5(Access is denied.). (0.1s) |
| ✅ | 完整備份 → 網路分享 (本機暫存再複製) | SbmTest_FULL_20261126_130925.bak，3.1 MB，已驗證 (1.5s) |
| ✅ | 遠端 SQL Server：讓 SQL 用 NAS 帳號連線 (xp_cmdshell net use) 驗證 | SQL Server 實際寫入 NAS 成功，xp_cmdshell 已自動關回 (0.4s) |
| ✅ | 遠端 SQL Server：完整備份 → NAS (SQL 直接連線) | SbmTest_FULL_20261126_130927.bak，3.1 MB，已驗證 (1.7s) |
| ✅ | 遠端 SQL Server：錯誤的 NAS 密碼要失敗並說明 | SQL Server 無法連線到 \\127.0.0.1\nas（已試帳號：runnervmbpeip\nasuser）：System error 1326 has occurred. The user name or password is incorrect. 【處理】帳號或密碼錯誤：請確認 NAS 帳號密碼，帳號可試試「127.0.0.1\帳號」格式。 (3.4s) |
| ✅ | 遠端 SQL Server：主機中轉 (經 C$ 取回再送 NAS) | SbmTest_FULL_20261126_130932.bak，3.1 MB，已驗證 (1.5s) |
| ✅ | 記錄備份 → 網路分享 (Z: 對應磁碟機) | 略過：沒有對應磁碟機 (0.0s) |
| ✅ | 掃描備份資料夾 | 5 個還原點：完整備份、差異備份、交易記錄備份、交易記錄備份、完整備份 (0.0s) |
| ✅ | 還原到最新記錄備份 (完整→差異→記錄鏈) 成新資料庫 | 還原鏈：完整備份 → 差異備份 → 交易記錄備份 → 交易記錄備份｜240 筆正確 (2.9s) |
| ✅ | 還原到指定時間點 (STOPAT) | 200 筆正確 (2.2s) |
| ✅ | 還原 ZIP 備份 | 240 筆 (0.9s) |
| ✅ | 還原分割備份 | 240 筆 (1.0s) |
| ✅ | 從網路分享還原 (先複製到本機) | 240 筆 (1.2s) |
| ✅ | 遠端 SQL Server：從 NAS 還原 (SQL 直接連線) | 240 筆 (1.6s) |
| ✅ | 覆寫原資料庫 + 尾端記錄 (Tail-log) 還原到最新 | 247 筆正確，並已先做覆寫前安全備份 (1.9s) |
| ✅ | BACPAC 匯出與匯入 | SbmTest_BACPAC_20261126_130945.bacpac，11.9 KB，完成（結構 + 資料，可匯入 Azure SQL / 其他版本）｜匯入後 247 筆 (30.3s) |
| ✅ | 命令列 --run "工作名稱" | 結束代碼 0；找不到工作時回傳 2 (1.3s) |
| ✅ | PostgreSQL：pg_dump (custom) 備份與還原 | 已驗證（7 個物件）｜還原後 123 筆正確（含中文） (4.4s) |
| ✅ | PostgreSQL：角色與權限 (pg_dumpall) | OK (0.7s) |
| ✅ | MySQL：mysqldump 備份與還原 | 還原後內容正確（含中文） (1.0s) |
| ✅ | 日誌整合：同一次執行合成一筆 | 31 次執行（原始日誌 51 筆）｜完整備份那次含 2 筆明細 (0.0s) |
| ✅ | 日誌：全部清除 | 刪除 1 個日誌檔 (0.0s) |
