using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SqlBackupManager
{
    /// <summary>
    /// 自動測試 (給 GitHub Actions 的 Windows 主機使用)：
    ///   --selftest &lt;輸出資料夾&gt;      實際備份 / 還原 / 網路分享 / 命令列 / MySQL / PostgreSQL
    ///   --selftest-ui &lt;輸出資料夾&gt;   各頁面在不同字體大小下的截圖
    /// 結果寫成 report.md，結束代碼 0 = 全部通過。
    /// </summary>
    public static class SelfTest
    {
        class Result { public bool Ok; public string Name, Detail; }
        static readonly List<Result> results = new List<Result>();
        static string outDir;

        static void Check(string name, Func<string> test)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                string d = test();
                results.Add(new Result { Ok = true, Name = name, Detail = (d ?? "") + " (" + sw.Elapsed.TotalSeconds.ToString("0.0") + "s)" });
            }
            catch (Exception ex)
            {
                results.Add(new Result { Ok = false, Name = name, Detail = ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " ") });
            }
            File.AppendAllText(Path.Combine(outDir, "progress.log"), (results.Last().Ok ? "PASS " : "FAIL ") + name + " | " + results.Last().Detail + Environment.NewLine);
        }

        static void Skip(string name, string why) { results.Add(new Result { Ok = true, Name = name, Detail = "略過：" + why }); }

        static void Assert(bool cond, string msg) { if (!cond) throw new Exception(msg); }

        static int WriteReport(string title)
        {
            var sb = new StringBuilder();
            int pass = results.Count(r => r.Ok), fail = results.Count(r => !r.Ok);
            sb.AppendLine("## " + title);
            sb.AppendLine();
            sb.AppendLine("**" + (fail == 0 ? "✅ 全部通過" : "❌ 有失敗") + "**：通過 " + pass + "、失敗 " + fail + "　（" + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "）");
            sb.AppendLine();
            sb.AppendLine("| 結果 | 測試項目 | 說明 |");
            sb.AppendLine("|---|---|---|");
            foreach (var r in results)
                sb.AppendLine("| " + (r.Ok ? "✅" : "❌") + " | " + r.Name + " | " + (r.Detail ?? "").Replace("|", "/") + " |");
            File.WriteAllText(Path.Combine(outDir, "report.md"), sb.ToString(), new UTF8Encoding(false));
            return fail == 0 ? 0 : 1;
        }

        // ───────────────────────── 功能測試 ─────────────────────────
        const string Db = "SbmTest";

        static ConnectionSettings Sql;
        static string Server { get { return Environment.GetEnvironmentVariable("SBM_SQL") ?? @".\SQLEXPRESS"; } }

        static object Q(string sql, string db = "master")
        {
            using (var cn = new SqlConnection(SqlOps.BuildConnString(Sql, db)))
            {
                cn.Open();
                using (var cmd = new SqlCommand(sql, cn) { CommandTimeout = 300 }) return cmd.ExecuteScalar();
            }
        }

        static int Count(string db) { return Convert.ToInt32(Q("SELECT COUNT(*) FROM dbo.T", db)); }
        static void Insert(int n) { Q("INSERT INTO dbo.T(v) SELECT TOP (" + n + ") NEWID() FROM sys.all_objects", Db); }

        static BackupJob NewJob(string name, BackupKind kind, string path)
        {
            return new BackupJob
            {
                Name = name, Kind = kind, TargetPath = path, Engine = DbEngine.SqlServer,
                SqlConn = Sql.Copy(), ConnSet = true, ConnVerified = true,
                AllUserDatabases = false, Databases = new List<string> { Db },
                Verify = true, Checksum = true, Compression = true, RetentionDays = 0
            };
        }

        static string RunOk(BackupJob j)
        {
            Thread.Sleep(1100); // 檔名精確到秒，避免同一秒重複
            var r = BackupEngine.RunJob(j);
            var fail = r.Entries.Where(e => e.Status == LogEntry.Failed).Select(e => e.Message).ToList();
            Assert(r.Ok, "工作失敗：" + string.Join(" / ", fail));
            var ok = r.Entries.LastOrDefault(e => e.Status == LogEntry.Success && e.Database == Db);
            Assert(ok != null, "沒有成功的備份紀錄");
            Assert(File.Exists(ok.File), "備份檔不存在：" + ok.File);
            return Path.GetFileName(ok.File) + "，" + Fmt.Size(ok.SizeBytes) + "，" + ok.Message;
        }

        public static int Run(string dir)
        {
            outDir = dir;
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "progress.log"), "");
            Sql = new ConnectionSettings { Server = Server, UseWindowsAuth = true, ConnectTimeoutSec = 30 };
            string local = Path.Combine(outDir, "local");
            string nasUnc = Environment.GetEnvironmentVariable("SBM_NAS") ?? @"\\localhost\nas";
            string nasUser = Environment.GetEnvironmentVariable("SBM_NAS_USER") ?? "nasuser";
            string nasPwd = Environment.GetEnvironmentVariable("SBM_NAS_PWD") ?? "";
            string nasPath = nasUnc + @"\backup";

            SettingsStore.Load();
            SettingsStore.Current.Jobs.Clear();
            SettingsStore.Current.Mail.Enabled = false;

            // 0. 排程計算
            Check("排程計算 (每天/每週/每月/間隔)", () =>
            {
                var now = new DateTime(2026, 11, 24, 7, 30, 0);
                var j = new BackupJob { Schedule = ScheduleKind.Daily, TimeOfDayMinutes = 120, StartDate = new DateTime(2026, 1, 1) };
                Assert(ScheduleCalc.Next(j, now) == new DateTime(2026, 11, 25, 2, 0, 0), "每天");
                j.Schedule = ScheduleKind.Weekly; j.WeekDaysMask = 1; j.TimeOfDayMinutes = 60;
                Assert(ScheduleCalc.Next(j, now) == new DateTime(2026, 11, 29, 1, 0, 0), "每週");
                j.Schedule = ScheduleKind.Monthly; j.MonthDay = 0; j.TimeOfDayMinutes = 0;
                Assert(ScheduleCalc.Next(j, now) == new DateTime(2026, 11, 30, 0, 0, 0), "每月最後一天");
                j.Schedule = ScheduleKind.Interval; j.IntervalMinutes = 30; j.IntervalEndMinutes = 0;
                Assert(ScheduleCalc.Next(j, now) == new DateTime(2026, 11, 24, 8, 0, 0), "間隔");
                return "OK";
            });

            // 1. 連線
            List<DbInfo> dbs = null;
            Check("連線 SQL Server (" + Server + ")", () =>
            {
                string info = ConnTest.Test(DbEngine.SqlServer, Sql, null, out dbs);
                var si = SqlOps.GetServerInfo(Sql);
                return info + "｜預設備份資料夾 " + si.DefaultBackupPath;
            });
            if (dbs == null) return WriteReport("SQL 備份管理 自動測試");

            Check("建立測試資料庫 (FULL 復原模式)", () =>
            {
                Q("IF DB_ID('" + Db + "') IS NOT NULL BEGIN ALTER DATABASE [" + Db + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + Db + "]; END");
                foreach (var x in new[] { "_R", "_P", "_N", "_Z", "_S", "_B", "_X" })
                    Q("IF DB_ID('" + Db + x + "') IS NOT NULL BEGIN ALTER DATABASE [" + Db + x + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + Db + x + "]; END");
                Q("CREATE DATABASE [" + Db + "]; ALTER DATABASE [" + Db + "] SET RECOVERY FULL;");
                Q("CREATE TABLE dbo.T(id int IDENTITY PRIMARY KEY, v nvarchar(50), t datetime2 DEFAULT SYSDATETIME())", Db);
                Insert(100);
                return "100 筆";
            });

            // 2. 本機備份
            Check("完整備份 → 本機資料夾", () => RunOk(NewJob("測試-完整", BackupKind.Full, local)));
            Check("執行明細：步驟與百分比", () =>
            {
                var st = Activity.Snapshot();
                Assert(!Activity.Running && Activity.Result == LogEntry.Success, "狀態不正確：" + Activity.Result);
                Assert(Activity.Overall == 100, "整體百分比 " + Activity.Overall);
                Assert(st.Count >= 3 && st.All(x => x.Done), "步驟數 " + st.Count);
                Assert(st.Any(x => x.Percent == 100), "沒有記錄到百分比進度");
                return st.Count + " 個步驟：" + string.Join(" → ", st.Select(x => x.Text).Take(5));
            });
            Check("差異備份", () => { Insert(50); return RunOk(NewJob("測試-差異", BackupKind.Differential, local)); });
            int countAtT1 = 0, countLog2 = 0;
            DateTime t1 = DateTime.MinValue;
            Check("交易記錄備份 #1", () => { Insert(30); return RunOk(NewJob("測試-記錄", BackupKind.Log, local)); });
            Check("交易記錄備份 #2 (中間記下時間點)", () =>
            {
                Insert(20); countAtT1 = Count(Db);
                Thread.Sleep(1500);
                t1 = Convert.ToDateTime(Q("SELECT SYSDATETIME()"));
                Thread.Sleep(1500);
                Insert(40); countLog2 = Count(Db);
                return RunOk(NewJob("測試-記錄", BackupKind.Log, local)) + "｜時間點 " + t1.ToString("HH:mm:ss") + " 時有 " + countAtT1 + " 筆";
            });
            Check("ZIP 壓縮備份", () => { var j = NewJob("測試-ZIP", BackupKind.Full, Path.Combine(outDir, "zip")); j.ZipAfterBackup = true; j.CopyOnly = true; return RunOk(j); });
            Check("分割備份 (3 個檔案)", () =>
            {
                var j = NewJob("測試-分割", BackupKind.Full, Path.Combine(outDir, "stripe")); j.StripeCount = 3; j.CopyOnly = true;
                string r = RunOk(j);
                int n = Directory.GetFiles(Path.Combine(outDir, "stripe", Db), "*of3.bak").Length;
                Assert(n == 3, "分割檔數量 = " + n);
                return r;
            });
            Check("部分備份 (Partial)", () => { var j = NewJob("測試-部分", BackupKind.Partial, Path.Combine(outDir, "partial")); return RunOk(j); });
            Check("第二備份位置 (本機複製)", () =>
            {
                var j = NewJob("測試-第二位置", BackupKind.Full, Path.Combine(outDir, "main2")); j.CopyOnly = true; j.SecondaryPath = Path.Combine(outDir, "copy2");
                string r = RunOk(j);
                Assert(Directory.GetFiles(Path.Combine(outDir, "copy2", Db), "*.bak").Length == 1, "第二位置沒有檔案");
                return r;
            });
            Check("保留天數：刪除過期的舊備份", () =>
            {
                string dirDb = Path.Combine(local, Db);
                string old = Path.Combine(dirDb, Db + "_FULL_20200101_000000.bak");
                string other = Path.Combine(dirDb, "不要刪我.bak");
                File.WriteAllText(old, "x"); File.SetLastWriteTime(old, new DateTime(2020, 1, 1));
                File.WriteAllText(other, "x"); File.SetLastWriteTime(other, new DateTime(2020, 1, 1));
                var j = NewJob("測試-保留", BackupKind.Full, local); j.RetentionDays = 7; j.CopyOnly = true;
                RunOk(j);
                Assert(!File.Exists(old), "過期檔沒有被刪除");
                Assert(File.Exists(other), "誤刪了不是本程式產生的檔案");
                File.Delete(other);
                return "過期檔已刪除，其他檔案保留";
            });
            Check("連線未驗證的工作不會執行", () =>
            {
                var j = NewJob("測試-未驗證", BackupKind.Full, local); j.ConnVerified = false;
                var r = BackupEngine.RunJob(j);
                Assert(!r.Ok && r.Entries.Any(e => e.Message.Contains("連線尚未驗證")), "竟然執行了");
                return "已拒絕執行";
            });

            // 3. 網路分享 (需要帳號密碼)
            if (nasPwd == "") Skip("網路分享測試", "沒有設定 SBM_NAS_PWD");
            else
            {
                Check("網路分享：錯誤密碼要驗證失敗", () =>
                {
                    try { NetShare.TestWrite(nasPath, nasUser, nasPwd + "_wrong"); }
                    catch (Exception ex) { return "正確擋下：" + ex.Message; }
                    throw new Exception("錯誤密碼竟然驗證成功");
                });
                Check("網路分享：正確帳密驗證可寫入", () => { NetShare.TestWrite(nasPath, nasUser, nasPwd); return "OK"; });
                Check("網路分享：SQL 服務帳戶本身無法直接寫入 (重現 1326 情境)", () =>
                {
                    try
                    {
                        Q("BACKUP DATABASE [" + Db + "] TO DISK = N'" + nasPath + "\\direct_test.bak' WITH COPY_ONLY, INIT");
                    }
                    catch (SqlException ex) { return "如預期失敗：" + ex.Message.Split('\n')[0]; }
                    return "注意：服務帳戶可以直接寫入（此環境無法重現權限問題）";
                });
                Check("完整備份 → 網路分享 (本機暫存再複製)", () =>
                {
                    var j = NewJob("測試-NAS", BackupKind.Full, nasPath); j.NetUser = nasUser; j.NetPassword = nasPwd; j.NetVerified = true; j.CopyOnly = true;
                    string r = RunOk(j);
                    Assert(Directory.GetFiles(Path.Combine(AppPaths.DataDir, "stage")).Length == 0, "暫存資料夾沒有清乾淨");
                    return r;
                });
                Check("遠端 SQL Server：讓 SQL 用 NAS 帳號連線 (xp_cmdshell net use) 驗證", () =>
                {
                    NetPath.ForceRemote = true;
                    try { SqlNetUse.Test(Sql, nasPath, nasUser, nasPwd); }
                    finally { NetPath.ForceRemote = false; }
                    int cmd = Convert.ToInt32(Q("SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name='xp_cmdshell'"));
                    Assert(cmd == 0, "xp_cmdshell 沒有關回去");
                    return "SQL Server 實際寫入 NAS 成功，xp_cmdshell 已自動關回";
                });
                Check("遠端 SQL Server：完整備份 → NAS (SQL 直接連線)", () =>
                {
                    NetPath.ForceRemote = true;
                    try
                    {
                        var j = NewJob("測試-遠端NAS", BackupKind.Full, nasPath + @"\remote"); j.NetUser = nasUser; j.NetPassword = nasPwd; j.CopyOnly = true;
                        j.SqlConn.RemoteNetMode = 0;
                        string r = RunOk(j);
                        Assert(r.Contains("SbmTest_FULL"), r);
                        return r;
                    }
                    finally { NetPath.ForceRemote = false; }
                });
                Check("遠端 SQL Server：錯誤的 NAS 密碼要失敗並說明", () =>
                {
                    NetPath.ForceRemote = true;
                    try
                    {
                        var j = NewJob("測試-遠端NAS錯誤", BackupKind.Full, nasPath + @"\remote"); j.NetUser = nasUser; j.NetPassword = nasPwd + "x"; j.CopyOnly = true;
                        Thread.Sleep(1100);
                        var r = BackupEngine.RunJob(j);
                        Assert(!r.Ok, "竟然成功");
                        string m = r.Entries.First(e => e.Status == LogEntry.Failed).Message;
                        Assert(m.Contains("已試帳號") && m.Contains("【處理】"), "錯誤訊息沒有處理說明：" + m);
                        return m.Replace("\n", " ");
                    }
                    finally { NetPath.ForceRemote = false; }
                });
                string hostUser = Environment.GetEnvironmentVariable("SBM_HOST_USER"), hostPwd = Environment.GetEnvironmentVariable("SBM_HOST_PWD");
                if (string.IsNullOrEmpty(hostUser)) Skip("遠端 SQL Server：主機中轉", "沒有設定 SBM_HOST_USER");
                else Check("遠端 SQL Server：主機中轉 (經 C$ 取回再送 NAS)", () =>
                {
                    NetPath.ForceRemote = true;
                    try
                    {
                        var j = NewJob("測試-中轉", BackupKind.Full, nasPath + @"\relay"); j.NetUser = nasUser; j.NetPassword = nasPwd; j.CopyOnly = true;
                        j.SqlConn.RemoteNetMode = 1; j.SqlConn.HostStageLocal = @"C:\sbmrelay"; j.SqlConn.HostStageUnc = NetPath.AdminShare("localhost", @"C:\sbmrelay");
                        j.SqlConn.HostUser = hostUser; j.SqlConn.HostPassword = hostPwd;
                        NetShare.TestRelay(j.SqlConn, j.SqlConn.HostStageLocal, j.SqlConn.HostStageUnc, hostUser, hostPwd);
                        string r = RunOk(j);
                        Assert(Directory.GetFiles(@"C:\sbmrelay").Length == 0, "中轉資料夾沒有清乾淨");
                        return r;
                    }
                    finally { NetPath.ForceRemote = false; }
                });
                Check("記錄備份 → 網路分享 (Z: 對應磁碟機)", () =>
                {
                    string drive = Environment.GetEnvironmentVariable("SBM_NAS_DRIVE");
                    if (string.IsNullOrEmpty(drive)) return "略過：沒有對應磁碟機";
                    var j = NewJob("測試-NAS-Z", BackupKind.Log, drive + @"\zlog"); j.NetUser = nasUser; j.NetPassword = nasPwd;
                    return RunOk(j);
                });
            }

            // 4. 還原
            var items = new List<BackupItem>();
            Check("掃描備份資料夾", () =>
            {
                items = RestoreEngine.Scan(local, Sql, null);
                return items.Count + " 個還原點：" + string.Join("、", items.Where(i => i.Database == Db).Select(i => i.KindText));
            });
            Func<string, BackupItem, DateTime?, bool, bool, int> restore = (target, point, stopAt, overwrite, tail) =>
            {
                var chain = RestoreEngine.BuildChain(items, point);
                RestoreEngine.Execute(new RestorePlan { Chain = chain, TargetDb = target, Overwrite = overwrite, SafetyBackup = overwrite, SafetyBackupDir = Path.Combine(outDir, "safety"), KillConnections = true, StopAt = stopAt, TailLog = tail, SqlConn = Sql });
                return Count(target);
            };
            var lastLog = items.Where(i => i.Database == Db && i.Kind == BackupKind.Log).OrderBy(i => i.Time).LastOrDefault();
            Check("還原到最新記錄備份 (完整→差異→記錄鏈) 成新資料庫", () =>
            {
                Assert(lastLog != null, "找不到記錄備份");
                var chain = RestoreEngine.BuildChain(items, lastLog);
                int n = restore(Db + "_R", lastLog, null, false, false);
                Assert(n == countLog2, "筆數 " + n + "，應為 " + countLog2);
                return "還原鏈：" + string.Join(" → ", chain.Select(c => c.KindText)) + "｜" + n + " 筆正確";
            });
            Check("還原到指定時間點 (STOPAT)", () =>
            {
                int n = restore(Db + "_P", lastLog, t1, false, false);
                Assert(n == countAtT1, "筆數 " + n + "，應為 " + countAtT1);
                return n + " 筆正確";
            });
            Check("還原 ZIP 備份", () =>
            {
                var z = RestoreEngine.Scan(Path.Combine(outDir, "zip"), Sql, null).First(i => i.IsZip);
                RestoreEngine.Execute(new RestorePlan { Chain = new List<BackupItem> { z }, TargetDb = Db + "_Z", SqlConn = Sql });
                return Count(Db + "_Z") + " 筆";
            });
            Check("還原分割備份", () =>
            {
                var s = RestoreEngine.Scan(Path.Combine(outDir, "stripe"), Sql, null).First();
                Assert(s.Parts.Count == 3, "沒有合併成 1 個還原點");
                RestoreEngine.Execute(new RestorePlan { Chain = new List<BackupItem> { s }, TargetDb = Db + "_S", SqlConn = Sql });
                return Count(Db + "_S") + " 筆";
            });
            if (nasPwd != "")
                Check("從網路分享還原 (先複製到本機)", () =>
                {
                    NetShare.Connect(nasPath, nasUser, nasPwd);
                    var n = RestoreEngine.Scan(nasPath, Sql, null).Where(i => i.Database == Db && i.Kind == BackupKind.Full).OrderBy(i => i.Time).Last();
                    RestoreEngine.Execute(new RestorePlan { Chain = new List<BackupItem> { n }, TargetDb = Db + "_N", SqlConn = Sql });
                    return Count(Db + "_N") + " 筆";
                });
            if (nasPwd != "")
                Check("遠端 SQL Server：從 NAS 還原 (SQL 直接連線)", () =>
                {
                    NetShare.Connect(nasPath, nasUser, nasPwd);
                    var n = RestoreEngine.Scan(nasPath + @"\remote", Sql, null).First(i => i.Database == Db);
                    NetPath.ForceRemote = true;
                    try { RestoreEngine.Execute(new RestorePlan { Chain = new List<BackupItem> { n }, TargetDb = Db + "_X", SqlConn = Sql, NetUser = nasUser, NetPassword = nasPwd }); }
                    finally { NetPath.ForceRemote = false; }
                    return Count(Db + "_X") + " 筆";
                });
            Check("覆寫原資料庫 + 尾端記錄 (Tail-log) 還原到最新", () =>
            {
                Insert(7);
                int live = Count(Db);
                int n = restore(Db, lastLog, null, true, true);
                Assert(n == live, "筆數 " + n + "，應為 " + live + "（含最後一次記錄備份之後的資料）");
                Assert(Convert.ToString(Q("SELECT state_desc FROM sys.databases WHERE name='" + Db + "'")) == "ONLINE", "資料庫不是 ONLINE");
                Assert(Convert.ToString(Q("SELECT user_access_desc FROM sys.databases WHERE name='" + Db + "'")) == "MULTI_USER", "資料庫不是 MULTI_USER");
                return n + " 筆正確，並已先做覆寫前安全備份";
            });

            // 5. BACPAC (有 SqlPackage 才測)
            Check("BACPAC 匯出與匯入", () =>
            {
                try { SqlPackageTool.Exe(Sql); } catch { return "略過：此主機沒有 SqlPackage"; }
                var j = NewJob("測試-BACPAC", BackupKind.Bacpac, Path.Combine(outDir, "bacpac"));
                string r = RunOk(j);
                var b = RestoreEngine.Scan(Path.Combine(outDir, "bacpac"), Sql, null).First(i => i.Kind == BackupKind.Bacpac);
                RestoreEngine.Execute(new RestorePlan { Chain = new List<BackupItem> { b }, TargetDb = Db + "_B", SqlConn = Sql });
                return r + "｜匯入後 " + Count(Db + "_B") + " 筆";
            });

            // 6. 命令列 --run (給工作排程器)
            Check("命令列 --run \"工作名稱\"", () =>
            {
                var j = NewJob("命令列測試", BackupKind.Full, Path.Combine(outDir, "cli")); j.CopyOnly = true;
                SettingsStore.Current.Jobs.Clear(); SettingsStore.Current.Jobs.Add(j); SettingsStore.Save();
                var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--run \"命令列測試\"") { UseShellExecute = false });
                p.WaitForExit(600000);
                Assert(p.ExitCode == 0, "結束代碼 " + p.ExitCode);
                Assert(Directory.GetFiles(Path.Combine(outDir, "cli", Db), "*.bak").Length == 1, "沒有產生備份檔");
                var p2 = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--run \"不存在的工作\"") { UseShellExecute = false });
                p2.WaitForExit(60000);
                Assert(p2.ExitCode == 2, "找不到工作時結束代碼應為 2，實際 " + p2.ExitCode);
                return "結束代碼 0；找不到工作時回傳 2";
            });

            // 7. PostgreSQL
            string pgBin = Environment.GetEnvironmentVariable("SBM_PG_BIN");
            if (string.IsNullOrEmpty(pgBin)) Skip("PostgreSQL 測試", "沒有 PostgreSQL");
            else
            {
                var pg = new ToolConnection { Host = "localhost", Port = 5432, User = "postgres", BinDir = pgBin };
                pg.Password = Environment.GetEnvironmentVariable("SBM_PG_PWD") ?? "root";
                Check("PostgreSQL：pg_dump (custom) 備份與還原", () =>
                {
                    PgTool.Query(pg, "DROP DATABASE IF EXISTS sbmpg"); PgTool.Query(pg, "DROP DATABASE IF EXISTS sbmpg_r");
                    PgTool.Query(pg, "CREATE DATABASE sbmpg");
                    PgTool.Query(pg, "CREATE TABLE t(id serial primary key, v text); INSERT INTO t(v) SELECT md5(i::text) FROM generate_series(1,122) i; INSERT INTO t(v) VALUES ('中文測試');", "sbmpg");
                    var j = new BackupJob { Name = "PG 測試", Engine = DbEngine.PostgreSql, Kind = BackupKind.Dump, ToolConn = pg, ConnSet = true, ConnVerified = true, AllUserDatabases = false, Databases = new List<string> { "sbmpg" }, TargetPath = Path.Combine(outDir, "pg"), Verify = true };
                    var r = BackupEngine.RunJob(j);
                    Assert(r.Ok, string.Join(" / ", r.Entries.Where(e => e.Status == LogEntry.Failed).Select(e => e.Message)));
                    var it = RestoreEngine.Scan(Path.Combine(outDir, "pg"), null, null).First();
                    RestoreEngine.Execute(new RestorePlan { Chain = new List<BackupItem> { it }, TargetDb = "sbmpg_r", ToolConn = pg });
                    string n = PgTool.Query(pg, "SELECT count(*) FROM t", "sbmpg_r").Trim();
                    Assert(n == "123", "還原後筆數 " + n);
                    string zh = PgTool.Query(pg, "SELECT v FROM t WHERE id = 123", "sbmpg_r").Trim();
                    Assert(zh == "中文測試", "中文內容不符：" + zh);
                    return r.Entries.Last(e => e.Status == LogEntry.Success).Message + "｜還原後 123 筆正確（含中文）";
                });
                Check("PostgreSQL：角色與權限 (pg_dumpall)", () =>
                {
                    var j = new BackupJob { Name = "PG 角色", Engine = DbEngine.PostgreSql, Kind = BackupKind.Globals, ToolConn = pg, ConnSet = true, ConnVerified = true, TargetPath = Path.Combine(outDir, "pgroles") };
                    var r = BackupEngine.RunJob(j);
                    Assert(r.Ok, string.Join(" / ", r.Entries.Where(e => e.Status == LogEntry.Failed).Select(e => e.Message)));
                    return "OK";
                });
            }

            // 8. MySQL
            string myBin = Environment.GetEnvironmentVariable("SBM_MYSQL_BIN");
            if (string.IsNullOrEmpty(myBin)) Skip("MySQL 測試", "沒有 MySQL");
            else
            {
                var my = new ToolConnection { Host = "127.0.0.1", Port = 3306, User = "root", BinDir = myBin };
                my.Password = Environment.GetEnvironmentVariable("SBM_MYSQL_PWD") ?? "";
                Check("MySQL：mysqldump 備份與還原", () =>
                {
                    MySqlTool.Query(my, "DROP DATABASE IF EXISTS sbmmy; DROP DATABASE IF EXISTS sbmmy_r; CREATE DATABASE sbmmy; CREATE TABLE sbmmy.t(id int auto_increment primary key, v varchar(50)) ENGINE=InnoDB; INSERT INTO sbmmy.t(v) VALUES ('中文測試'),('b'),('c');");
                    var j = new BackupJob { Name = "MySQL 測試", Engine = DbEngine.MySql, Kind = BackupKind.Dump, ToolConn = my, ConnSet = true, ConnVerified = true, AllUserDatabases = false, Databases = new List<string> { "sbmmy" }, TargetPath = Path.Combine(outDir, "my"), Verify = true };
                    var r = BackupEngine.RunJob(j);
                    Assert(r.Ok, string.Join(" / ", r.Entries.Where(e => e.Status == LogEntry.Failed).Select(e => e.Message)));
                    var it = RestoreEngine.Scan(Path.Combine(outDir, "my"), null, null).First();
                    RestoreEngine.Execute(new RestorePlan { Chain = new List<BackupItem> { it }, TargetDb = "sbmmy_r", ToolConn = my });
                    string v = MySqlTool.Query(my, "SELECT GROUP_CONCAT(v ORDER BY id) FROM sbmmy_r.t").Trim();
                    Assert(v == "中文測試,b,c", "還原後內容不符：" + v);
                    return "還原後內容正確（含中文）";
                });
            }

            Check("日誌整合：同一次執行合成一筆", () =>
            {
                var groups = Logger.Group(Logger.Read(DateTime.Today.AddDays(-1), DateTime.Now.AddMinutes(1)));
                var full = groups.Where(g => g.Job == "測試-完整").ToList();
                Assert(full.Count == 1, "「測試-完整」執行 1 次，卻整合成 " + full.Count + " 筆");
                var g1 = full[0];
                Assert(g1.Status == LogEntry.Success && g1.Databases.Contains(Db), "結果不正確：" + g1.Status);
                Assert(g1.Entries.Count >= 2 && g1.Entries.Select(e => e.RunId).Distinct().Count() == 1, "明細沒有合在同一次執行");
                Assert(g1.Entries.Zip(g1.Entries.Skip(1), (a, b) => a.Time <= b.Time).All(x => x), "明細順序不是依時間先後");
                var logRuns = groups.Count(g => g.Job == "測試-記錄");
                Assert(logRuns == 2, "「測試-記錄」執行 2 次，卻整合成 " + logRuns + " 筆");
                var restores = groups.Where(g => g.Job == "還原").ToList();
                Assert(restores.Count >= 5 && restores.All(g => g.Entries.Select(e => e.RunId).Distinct().Count() == 1), "還原紀錄沒有各自整合");
                return groups.Count + " 次執行（原始日誌 " + groups.Sum(g => g.Entries.Count) + " 筆）｜完整備份那次含 " + g1.Entries.Count + " 筆明細";
            });
            Check("日誌：全部清除", () =>
            {
                Assert(Logger.Read(DateTime.Today.AddDays(-1), DateTime.Now.AddMinutes(1)).Count > 0, "清除前沒有日誌");
                File.Copy(Directory.GetFiles(AppPaths.LogDir, "backup_*.log").First(), Path.Combine(outDir, "log-before-clear.log"), true);
                int n = Logger.ClearAll();
                Assert(Logger.Read(DateTime.Today.AddYears(-1), DateTime.Now.AddMinutes(1)).Count == 0, "清除後還有日誌");
                return "刪除 " + n + " 個日誌檔";
            });

            return WriteReport("SQL 備份管理 自動測試");
        }

        // ───────────────────────── 介面截圖 ─────────────────────────
        public static int RunUi(string dir)
        {
            outDir = dir;
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "progress.log"), "");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            foreach (var fs in new[] { 10f, 14f })
            {
                Check("介面截圖 " + fs + "pt", () =>
                {
                    SettingsStore.Current = new AppSettings();
                    SettingsStore.Current.General.FontSize = fs;
                    var js = SettingsStore.Current.Jobs;
                    js.Add(new BackupJob { Name = "ERP 每週完整", ConnSet = true, ConnVerified = true, Kind = BackupKind.Full, Schedule = ScheduleKind.Weekly, WeekDaysMask = 1, TimeOfDayMinutes = 60, TargetPath = @"D:\SQLBackup", LastRun = DateTime.Now.AddDays(-1), LastStatus = LogEntry.Success, LastMessage = "成功 3、失敗 0、警告 0" });
                    js.Add(new BackupJob { Name = "會計 每日差異 (NAS)", ConnSet = true, ConnVerified = true, Kind = BackupKind.Differential, TargetPath = @"\\knox_nas\mydisk\SQLBackup", NetUser = @"knox_nas\admin", AllUserDatabases = false, Databases = new List<string> { "NACC" } });
                    js.Add(new BackupJob { Name = "MySQL 網站", ConnSet = true, ConnVerified = false, Engine = DbEngine.MySql, Kind = BackupKind.Dump, TargetPath = @"D:\SQLBackup" });
                    foreach (var j in js) Scheduler.Reschedule(j);
                    Theme.Init(fs);
                    var f = new MainForm(false) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), Size = new Size(1400, 900), WindowState = FormWindowState.Normal };
                    f.Show();
                    string[] names = { "總覽", "備份工作", "還原", "日誌", "通知", "系統設定" };
                    for (int i = 0; i < names.Length; i++)
                    {
                        f.ShowPage(i); Pump(800);
                        Shot(f, fs + "pt_" + (i + 1) + "_" + names[i] + ".png");
                    }
                    // 模擬執行中：狀態列與執行明細
                    Activity.Begin("備份：會計 每日差異 (NAS)", 3);
                    Activity.Step("SQL Server 連線到 NAS…", -1);
                    Activity.SetUnits(3); Activity.SetUnit(1);
                    Activity.Step("[1/3] NACC 差異備份", 100);
                    Activity.Step("NACC 驗證備份檔…", -1);
                    Activity.Step("[2/3] ERP 差異備份", 45);
                    f.ShowPage(0); Pump(1200);
                    Shot(f, fs + "pt_7_執行中狀態列.png");
                    f.ShowActivity(); Pump(1200);
                    var af = Application.OpenForms.OfType<ActivityForm>().FirstOrDefault();
                    if (af != null) { Shot(af, fs + "pt_8_執行明細.png"); af.Close(); }
                    Activity.Finish(LogEntry.Success, "成功 3、失敗 0、警告 0");
                    Pump(800);
                    Shot(f, fs + "pt_9_完成狀態列.png");
                    // 備份日誌：一開始不顯示，查詢後才出現，且同一次執行合成一筆
                    Logger.ClearAll();
                    for (int k = 0; k < 2; k++)
                    {
                        string jn = js[k].Name;
                        Activity.Begin("備份：" + jn, 1, jn);
                        Logger.Write(new LogEntry { Job = jn, Kind = "完整備份", Status = LogEntry.Info, Message = "Express Edition 不支援原生備份壓縮" });
                        Logger.Write(new LogEntry { Job = jn, Kind = "完整備份", Status = LogEntry.Info, Message = "SQL Server 在 chin-sys：已讓 SQL Server 連線 NAS" });
                        Logger.Write(new LogEntry { Job = jn, Database = "NACC", Kind = "完整備份", Status = LogEntry.Success, SizeBytes = 102563840, DurationSec = 3, File = @"\\knox_nas\mydisk\SQLBackup\NACC\NACC_FULL_20261126_190259.bak", Message = "已驗證" });
                        Activity.Finish(LogEntry.Success, "成功 1、失敗 0、警告 0");
                    }
                    f.ShowPage(3); Pump(600);
                    var lp = (LogPage)f.CurrentPage;
                    Assert(!lp.HasRows, "日誌頁一開始就顯示了資料");
                    Shot(f, fs + "pt_4a_日誌未查詢.png");
                    lp.RunQuery(); Pump(600);
                    Assert(lp.RunCount == 2, "兩次執行應整合成 2 筆，實際 " + lp.RunCount);
                    Shot(f, fs + "pt_4b_日誌查詢後.png");
                    // 切到其他頁再回來，查詢結果與點選的那筆都要保留
                    lp.SelectRun(1); Pump(200);
                    var pick = lp.DetailCount;
                    f.ShowPage(0); Pump(400); f.ShowPage(1); Pump(400); f.ShowPage(3); Pump(600);
                    Assert(lp.RunCount == 2 && lp.HasRows, "切換頁面後日誌被清除了");
                    Assert(lp.DetailCount == pick && pick >= 3, "切換頁面後明細沒有保留（" + lp.DetailCount + "/" + pick + "）");
                    var je = new JobEditForm(js[1].Clone(), js[1]) { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) };
                    je.Show(); Pump(800);
                    var tabs = (TabControl)je.Controls[0];
                    string[] tn = { "連線", "備份內容", "存放位置", "排程" };
                    for (int t = 0; t < 4; t++) { tabs.SelectedIndex = t; Pump(600); Shot(je, fs + "pt_job_" + (t + 1) + "_" + tn[t] + ".png"); }
                    // SQL Server 在另一台電腦的畫面 (強制 sa、簡化的 NAS 說明)
                    NetPath.ForceRemote = true;
                    var rj = js[1].Clone(); rj.SqlConn.Server = @"chin-sys\SQLEXPRESS"; rj.SqlConn.UseWindowsAuth = true; rj.SqlConn.User = "admin";
                    var je2 = new JobEditForm(rj, null) { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60) };
                    je2.Show(); Pump(1500);
                    var tabs2 = (TabControl)je2.Controls[0];
                    tabs2.SelectedIndex = 0; Pump(600); Shot(je2, fs + "pt_remote_1_連線.png");
                    tabs2.SelectedIndex = 2; Pump(600); Shot(je2, fs + "pt_remote_3_存放位置.png");
                    var ce = je2.Controls[0].Controls[0].Controls.OfType<ConnectionEditor>().FirstOrDefault();
                    Assert(ce != null && ce.IsRemote, "沒有判斷出 SQL Server 在另一台電腦");
                    var newJob = new BackupJob { ConnSet = true }; newJob.SqlConn = new ConnectionSettings { Server = "" };
                    Assert(newJob.SqlConn.Server == "", "新工作的伺服器名稱不是空白");
                    Assert(!ce.SqlConn.UseWindowsAuth && ce.SqlConn.User == "sa", "沒有自動切換成 sa 帳號");
                    Assert(!ce.Verified, "自動改成 sa 後應要求重新驗證");
                    var ov2 = FindOverlaps(je2).ToList();
                    // 密碼欄位：已儲存的不能顯示；新輸入的可以顯示
                    var pb = new PasswordBox(); var holder = new Form(); holder.Controls.Add(pb); holder.Show(); Pump(200);
                    pb.SetSaved("secret");
                    Assert(pb.Locked && !pb.CanReveal && pb.Text == "secret", "已儲存的密碼竟然可以顯示");
                    pb.SetSaved(""); pb.Text = "new";
                    Assert(!pb.Locked && pb.CanReveal, "新輸入的密碼應該可以顯示");
                    holder.Close();
                    string fixedUser = je2.FixNetUser("knox");
                    Assert(fixedUser == "knox_nas\\knox", "帳號沒有自動補主機名稱：" + fixedUser);
                    Assert(je2.FixNetUser(@"knox_nas\knox") == @"knox_nas\knox" && je2.FixNetUser("a@b.com") == "a@b.com", "不該更動已有格式的帳號");
                    je2.Close(); NetPath.ForceRemote = false;
                    var ov = FindOverlaps(je).Concat(FindOverlaps(f)).Concat(ov2).Distinct().ToList();
                    je.Close(); f.RebuildUi(); Pump(300);
                    Assert(ov.Count == 0, "發現重疊：" + string.Join("；", ov.Take(8)));
                    return "已截圖，沒有發現文字重疊";
                });
            }
            return WriteReport("SQL 備份管理 介面測試");
        }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(20); }
        }

        static void Shot(Control c, string name)
        {
            using (var bmp = new Bitmap(c.Width, c.Height))
            {
                c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                bmp.Save(Path.Combine(outDir, name), ImageFormat.Png);
            }
        }

        /// <summary>檢查同一容器內、可見的文字控制項是否互相重疊。</summary>
        static IEnumerable<string> FindOverlaps(Control root)
        {
            var list = new List<string>();
            Walk(root, list);
            return list;
        }

        static void Walk(Control parent, List<string> list)
        {
            var kids = parent.Controls.Cast<Control>()
                .Where(c => c.Visible && c.Width > 0 && c.Height > 0 && c.Dock == DockStyle.None && (c is Label || c is TextBoxBase || c is ComboBox || c is ButtonBase || c is NumericUpDown || c is DateTimePicker || c is CheckedListBox))
                .ToList();
            for (int i = 0; i < kids.Count; i++)
                for (int j = i + 1; j < kids.Count; j++)
                {
                    var a = Bounds(kids[i]); var b = Bounds(kids[j]);
                    var x = Rectangle.Intersect(a, b);
                    if (x.Width > 2 && x.Height > 2 && (kids[i].Text != "" || kids[j].Text != ""))
                        list.Add(Name(parent) + "：「" + Short(kids[i]) + "」與「" + Short(kids[j]) + "」");
                }
            foreach (Control c in parent.Controls) if (c.Visible) Walk(c, list);
        }

        static Rectangle Bounds(Control c)
        {
            if (c is Label && ((Label)c).AutoSize) return new Rectangle(c.Location, c.PreferredSize);
            if (c is CheckBox && ((CheckBox)c).AutoSize) return new Rectangle(c.Location, c.PreferredSize);
            if (c is RadioButton && ((RadioButton)c).AutoSize) return new Rectangle(c.Location, c.PreferredSize);
            return c.Bounds;
        }
        static string Short(Control c) { string t = c.Text ?? ""; return (t.Length > 14 ? t.Substring(0, 14) : t) + "(" + c.GetType().Name + ")"; }
        static string Name(Control c) { var f = c.FindForm(); return (f != null ? f.Text : "") + "/" + c.GetType().Name; }
    }
}
