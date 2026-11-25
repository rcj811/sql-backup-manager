using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SqlBackupManager
{
    public class JobResult
    {
        public BackupJob Job;
        public int Success, Failed, Warnings;
        public List<LogEntry> Entries = new List<LogEntry>();
        public bool Ok { get { return Failed == 0; } }
        public string Summary
        {
            get { return string.Format("成功 {0}、失敗 {1}、警告 {2}", Success, Failed, Warnings); }
        }
    }

    public static class BackupEngine
    {
        // 同一時間只跑一個備份/還原作業，避免搶 I/O
        public static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        public static event Action<string, int> Progress;   // (訊息, 百分比 -1 = 不確定)
        public static event Action<JobResult> JobFinished;
        public static volatile bool Busy;

        public static void Report(string msg, int pct)
        {
            var h = Progress;
            if (h != null) try { h(msg, pct); } catch { }
        }

        public static Task<JobResult> RunJobAsync(BackupJob job)
        {
            return Task.Run(() => RunJob(job));
        }

        public static JobResult RunJob(BackupJob job)
        {
            var res = new JobResult { Job = job };
            Gate.Wait();
            Busy = true;
            try
            {
                Report("執行工作：" + job.Name, -1);
                if (!job.ConnVerified)
                {
                    Add(res, new LogEntry { Job = job.Name, Kind = BackupJob.KindText(job.Kind), Status = LogEntry.Failed, Message = "連線尚未驗證：請到「備份工作」編輯此工作，在「連線」頁按「測試連線」成功後再儲存" });
                    return Finish(res);
                }
                List<string> done = job.Engine == DbEngine.SqlServer ? RunSqlServer(res, job) : RunTool(res, job);
                if (job.RetentionDays > 0 && done != null && !IsUrl(job.TargetPath))
                {
                    Cleanup(res, job, job.TargetPath, done);
                    if (!string.IsNullOrWhiteSpace(job.SecondaryPath)) Cleanup(res, job, job.SecondaryPath, done);
                }
                return Finish(res);
            }
            finally
            {
                Busy = false;
                Gate.Release();
                Report("就緒", 0);
            }
        }

        /// <summary>
        /// 本機暫存資料夾：C:\\ProgramData\\SqlBackupManager\\stage，並授權 SQL 服務帳戶寫入。
        /// (SQL 預設的 Backup 資料夾一般權限的程式讀不到，所以不用它，除非授權失敗)
        /// </summary>
        public static string StageDir(ServerInfo si)
        {
            string d = Path.Combine(AppPaths.DataDir, "stage");
            try
            {
                Directory.CreateDirectory(d);
                var sec = Directory.GetAccessControl(d);
                System.Security.Principal.IdentityReference who;
                try
                {
                    who = string.IsNullOrWhiteSpace(si.ServiceAccount) || si.ServiceAccount.StartsWith("(")
                        ? (System.Security.Principal.IdentityReference)new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.AuthenticatedUserSid, null)
                        : new System.Security.Principal.NTAccount(si.ServiceAccount).Translate(typeof(System.Security.Principal.SecurityIdentifier));
                }
                catch { who = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.AuthenticatedUserSid, null); }
                sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(who,
                    System.Security.AccessControl.FileSystemRights.Modify,
                    System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                    System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
                Directory.SetAccessControl(d, sec);
                return d;
            }
            catch { return si.DefaultBackupPath; }
        }

        public static bool IsUrl(string p) { return p != null && p.StartsWith("http", StringComparison.OrdinalIgnoreCase); }

        // ─────────── SQL Server ───────────
        static List<string> RunSqlServer(JobResult res, BackupJob job)
        {
            var conn = job.SqlConn;
            ServerInfo si;
            List<DbInfo> dbs;
            try
            {
                si = SqlOps.GetServerInfo(conn);
                dbs = SqlOps.GetDatabases(conn);
            }
            catch (Exception ex)
            {
                Add(res, new LogEntry { Job = job.Name, Kind = BackupJob.KindText(job.Kind), Status = LogEntry.Failed, Message = "無法連線 SQL Server：" + ex.Message });
                return null;
            }

            var targets = PickTargets(res, job, dbs);
            string kt = BackupJob.KindText(job.Kind);
            if (job.Kind != BackupKind.Bacpac)
            {
                if (job.Compression && !si.SupportsCompression)
                    Add(res, new LogEntry { Job = job.Name, Kind = kt, Status = LogEntry.Info, Message = si.Edition + " 不支援原生備份壓縮，本次以未壓縮方式備份（可改用「備份後壓縮成 ZIP」）" });
                if (!string.IsNullOrWhiteSpace(job.MirrorPath) && si.EngineEdition != 3)
                    Add(res, new LogEntry { Job = job.Name, Kind = kt, Status = LogEntry.Warning, Message = "鏡像備份 (MIRROR TO) 僅 Enterprise / Developer 版支援，已略過鏡像" });
                if (!string.IsNullOrWhiteSpace(job.EncryptCertificate) && si.IsExpress)
                    Add(res, new LogEntry { Job = job.Name, Kind = kt, Status = LogEntry.Warning, Message = "Express 版不支援備份加密，本次未加密" });
            }

            var done = new List<string>();
            for (int i = 0; i < targets.Count; i++)
            {
                var d = targets[i];
                if (!d.State.Equals("ONLINE", StringComparison.OrdinalIgnoreCase))
                {
                    Add(res, new LogEntry { Job = job.Name, Database = d.Name, Kind = kt, Status = LogEntry.Warning, Message = "資料庫狀態為 " + d.State + "，已略過" });
                    continue;
                }
                if (job.Kind == BackupKind.Bacpac) BacpacOne(res, job, d, i + 1, targets.Count);
                else BackupOne(res, job, d, si, i + 1, targets.Count);
                done.Add(d.Name);
            }
            return done;
        }

        static List<DbInfo> PickTargets(JobResult res, BackupJob job, List<DbInfo> dbs)
        {
            var targets = new List<DbInfo>();
            if (job.AllUserDatabases)
                targets = dbs.Where(d => !d.IsSystem || job.IncludeSystemDatabases).ToList();
            else
            {
                foreach (var n in job.Databases)
                {
                    var d = dbs.FirstOrDefault(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                    if (d != null) targets.Add(d);
                    else Add(res, new LogEntry { Job = job.Name, Database = n, Kind = BackupJob.KindText(job.Kind), Status = LogEntry.Warning, Message = "資料庫不存在，已略過" });
                }
            }
            if (targets.Count == 0)
                Add(res, new LogEntry { Job = job.Name, Kind = BackupJob.KindText(job.Kind), Status = LogEntry.Warning, Message = "沒有可備份的資料庫" });
            return targets;
        }

        static string DirFor(BackupJob job, string basePath, string db)
        {
            if (IsUrl(basePath)) return basePath.TrimEnd('/');
            return job.SubFolderPerDatabase ? Path.Combine(basePath, SafeName(db)) : basePath;
        }

        static string Join(string dir, string name) { return IsUrl(dir) ? dir + "/" + Uri.EscapeDataString(name) : Path.Combine(dir, name); }

        static void BackupOne(JobResult res, BackupJob job, DbInfo d, ServerInfo si, int idx, int total)
        {
            var kind = job.Kind;
            string kindText = BackupJob.KindText(kind);
            bool url = IsUrl(job.TargetPath);

            if (kind == BackupKind.Log && d.RecoveryModel.Equals("SIMPLE", StringComparison.OrdinalIgnoreCase))
            {
                Add(res, new LogEntry { Job = job.Name, Database = d.Name, Kind = kindText, Status = LogEntry.Warning, Message = "復原模式為 SIMPLE，無法做交易記錄備份，已略過（如需此功能請改為 FULL 復原模式）" });
                return;
            }
            if (d.IsSystem && (kind == BackupKind.Partial || kind == BackupKind.PartialDiff || (kind != BackupKind.Full && d.Name.Equals("master", StringComparison.OrdinalIgnoreCase))))
            {
                Add(res, new LogEntry { Job = job.Name, Database = d.Name, Kind = kindText, Status = LogEntry.Info, Message = "系統資料庫不支援此備份類型，已略過" });
                return;
            }

            var sw = Stopwatch.StartNew();
            var files = new List<string>();
            string shown = "";
            bool fellBack = false;
            try
            {
                string finalDir = DirFor(job, job.TargetPath, d.Name);
                string dir = finalDir;
                bool stage = false;
                if (!url)
                {
                    // 網路路徑：SQL 服務帳戶看不到對應磁碟機 (Z:)，也常沒有 NAS 權限
                    // → 先備份到 SQL Server 本機的預設備份資料夾，完成後由本程式複製過去
                    bool net = NetPath.IsNetwork(finalDir);
                    if (net) NetShare.Connect(finalDir, job.NetUser, job.NetPassword);
                    bool local = NetPath.IsLocalServer(job.SqlConn.Server);
                    if (net && local && !string.IsNullOrWhiteSpace(si.DefaultBackupPath))
                    {
                        // 一律走本機暫存：SQL 服務帳戶不碰 NAS，由本程式用工作設定的帳號複製
                        stage = true;
                        dir = StageDir(si);
                        if (idx == 1)
                            Add(res, new LogEntry { Job = job.Name, Kind = kindText, Status = LogEntry.Info, File = NetPath.ToUnc(finalDir), Message = "網路路徑：先備份到本機 " + dir + "，再複製到 NAS" + (string.IsNullOrEmpty(job.NetUser) ? "（使用目前登入的帳號）" : "（帳號 " + job.NetUser + "）") });
                    }
                    else if (net)
                    {
                        dir = NetPath.ToUnc(finalDir);   // 至少把 Z:\ 換成 \\伺服器\分享
                        if (idx == 1)
                            Add(res, new LogEntry { Job = job.Name, Kind = kindText, Status = LogEntry.Warning, File = dir,
                                Message = !local ? "SQL Server (" + job.SqlConn.Server + ") 不在這台電腦，無法本機暫存，將由 SQL Server 服務帳戶直接寫入 NAS，該帳戶需有 NAS 權限"
                                                 : "讀不到 SQL Server 的預設備份資料夾，無法本機暫存，改由 SQL Server 服務帳戶直接寫入 NAS" });
                    }
                    try { Directory.CreateDirectory(stage ? finalDir : dir); } catch { }
                    // 用 SQL Server 服務帳戶的身分建立資料夾 (避免作業系統錯誤 3：找不到路徑)
                    try { SqlOps.Exec(job.SqlConn, "EXEC master.dbo.xp_create_subdir " + SqlOps.S(dir)); } catch { }
                    try
                    {
                        string root = Path.GetPathRoot(Path.GetFullPath(dir));
                        if (!string.IsNullOrEmpty(root) && !root.StartsWith(@"\\"))
                        {
                            var di = new DriveInfo(root);
                            if (di.IsReady && di.AvailableFreeSpace < (long)(d.SizeMB * 1024 * 1024))
                                Add(res, new LogEntry { Job = job.Name, Database = d.Name, Kind = kindText, Status = LogEntry.Warning, Message = "磁碟 " + root + " 剩餘 " + Fmt.Size(di.AvailableFreeSpace) + "，可能不足 (資料庫 " + d.SizeMB.ToString("0") + " MB)" });
                        }
                    }
                    catch { }
                }

                string mirrorDir = null;
                if (!string.IsNullOrWhiteSpace(job.MirrorPath) && si.EngineEdition == 3)
                {
                    mirrorDir = DirFor(job, job.MirrorPath, d.Name);
                    if (!IsUrl(mirrorDir)) try { Directory.CreateDirectory(mirrorDir); } catch { }
                }

                while (true)
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    int n = Math.Max(1, Math.Min(64, job.StripeCount));
                    files = new List<string>();
                    for (int s = 1; s <= n; s++) files.Add(Join(dir, BuildFileName(job.Engine, d.Name, kind, job, stamp, n > 1 ? "_" + s + "of" + n : "")));
                    var mirror = mirrorDir == null ? null : files.Select(f => Join(mirrorDir, IsUrl(f) ? Uri.UnescapeDataString(f.Substring(f.LastIndexOf('/') + 1)) : Path.GetFileName(f))).ToList();
                    string sql = BuildBackupSql(job, d.Name, kind, files, mirror, si);
                    string label = string.Format("[{0}/{1}] {2} {3}", idx, total, d.Name, BackupJob.KindText(kind));
                    Report(label + " 0%", 0);
                    try
                    {
                        SqlOps.ExecWithProgress(job.SqlConn, sql, p => Report(label + " " + p + "%", p));
                        break;
                    }
                    catch (SqlException ex)
                    {
                        // 3035: 沒有完整備份無法做差異；4214: 沒有完整備份無法做記錄備份
                        bool noBase = ex.Errors.Cast<SqlError>().Any(er => er.Number == 3035 || er.Number == 4214);
                        if (noBase && job.FallbackToFull && (kind == BackupKind.Differential || kind == BackupKind.Log || kind == BackupKind.PartialDiff))
                        {
                            kind = kind == BackupKind.PartialDiff ? BackupKind.Partial : BackupKind.Full;
                            fellBack = true;
                            continue;
                        }
                        throw;
                    }
                }
                shown = files[0];

                if (job.Verify)
                {
                    Report(d.Name + " 驗證備份檔…", -1);
                    SqlOps.ExecWithProgress(job.SqlConn,
                        "RESTORE VERIFYONLY FROM " + string.Join(", ", files.Select(f => (IsUrl(f) ? "URL = " : "DISK = ") + SqlOps.S(f))) + (job.Checksum ? " WITH CHECKSUM, STATS = 25" : " WITH STATS = 25"), null);
                }

                long size = 0;
                if (!url)
                {
                    if (job.ZipAfterBackup)
                    {
                        Report(d.Name + " 壓縮 ZIP…", -1);
                        string first = files[0];
                        string zip = Regex.Replace(first, @"(_\d+of\d+)?\.(bak|trn)$", ".zip", RegexOptions.IgnoreCase);
                        if (File.Exists(zip)) File.Delete(zip);
                        using (var za = ZipFile.Open(zip, ZipArchiveMode.Create))
                            foreach (var f in files) za.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
                        foreach (var f in files) File.Delete(f);
                        files = new List<string> { zip };
                        shown = zip;
                    }
                    if (stage)
                    {
                        Report(d.Name + " 複製到網路位置…", -1);
                        Directory.CreateDirectory(finalDir);
                        var moved = new List<string>();
                        foreach (var f in files)
                        {
                            string dst = Path.Combine(finalDir, Path.GetFileName(f));
                            File.Copy(f, dst, true);
                            if (new FileInfo(dst).Length != new FileInfo(f).Length) throw new IOException("複製到網路位置後檔案大小不符：" + dst);
                            File.Delete(f);
                            moved.Add(dst);
                        }
                        files = moved;
                        shown = files[0];
                    }
                    foreach (var f in files) try { size += new FileInfo(f).Length; } catch { }

                    if (!string.IsNullOrWhiteSpace(job.SecondaryPath))
                    {
                        Report(d.Name + " 複製到第二備份位置…", -1);
                        if (NetPath.IsNetwork(job.SecondaryPath)) NetShare.Connect(job.SecondaryPath, job.NetUser, job.NetPassword);
                        string dir2 = DirFor(job, job.SecondaryPath, d.Name);
                        Directory.CreateDirectory(dir2);
                        foreach (var f in files) File.Copy(f, Path.Combine(dir2, Path.GetFileName(f)), true);
                    }
                }

                sw.Stop();
                string msg = (fellBack ? "找不到基底備份，已自動改做" + BackupJob.KindText(kind) + "。" : "") + (job.Verify ? "已驗證" : "完成") + (files.Count > 1 ? "，分割 " + files.Count + " 個檔案" : "");
                Add(res, new LogEntry { Job = job.Name, Database = d.Name, Kind = BackupJob.KindText(kind), Status = LogEntry.Success, File = shown, SizeBytes = size, DurationSec = sw.Elapsed.TotalSeconds, Message = msg });
            }
            catch (Exception ex)
            {
                sw.Stop();
                string hint = "";
                if (ex.Message.Contains("Operating system error 5") || ex.Message.Contains("作業系統錯誤 5"))
                    hint = "（SQL Server 服務帳戶 " + si.ServiceAccount + " 對備份資料夾沒有寫入權限）";
                else if (ex.Message.Contains("error 1326") || ex.Message.Contains("錯誤 1326"))
                    hint = "（SQL Server 服務帳戶 " + si.ServiceAccount + " 無法登入 NAS：請確認工作的 SQL Server 在這台電腦上，讓程式改用本機暫存再複製；或把服務帳戶改成有 NAS 權限的帳號）";
                else if (ex.Message.Contains("Operating system error 3") || ex.Message.Contains("作業系統錯誤 3"))
                    hint = "（SQL Server 所在電腦找不到這個資料夾：請確認磁碟機存在；網路位置請用 \\\\伺服器\\分享 並勾選「先備份到本機再複製」，服務帳戶看不到 Z: 這類對應磁碟機）";
                Add(res, new LogEntry { Job = job.Name, Database = d.Name, Kind = BackupJob.KindText(kind), Status = LogEntry.Failed, File = files.Count > 0 ? files[0] : "", DurationSec = sw.Elapsed.TotalSeconds, Message = ex.Message + hint });
            }
        }

        static void BacpacOne(JobResult res, BackupJob job, DbInfo d, int idx, int total)
        {
            var sw = Stopwatch.StartNew();
            string file = "";
            string kt = BackupJob.KindText(BackupKind.Bacpac);
            try
            {
                if (d.IsSystem) { Add(res, new LogEntry { Job = job.Name, Database = d.Name, Kind = kt, Status = LogEntry.Info, Message = "系統資料庫不能匯出 BACPAC，已略過" }); return; }
                string dir = DirFor(job, job.TargetPath, d.Name);
                Directory.CreateDirectory(dir);
                file = Path.Combine(dir, BuildFileName(job.Engine, d.Name, BackupKind.Bacpac, job, DateTime.Now.ToString("yyyyMMdd_HHmmss"), ""));
                string label = string.Format("[{0}/{1}] {2} BACPAC", idx, total, d.Name);
                Report(label + "…", -1);
                SqlPackageTool.Export(job.SqlConn, d.Name, file, line => Report(label + "：" + Short(line), -1));
                CopySecondary(job, d.Name, file);
                sw.Stop();
                Add(res, new LogEntry { Job = job.Name, Database = d.Name, Kind = kt, Status = LogEntry.Success, File = file, SizeBytes = Len(file), DurationSec = sw.Elapsed.TotalSeconds, Message = "完成（結構 + 資料，可匯入 Azure SQL / 其他版本）" });
            }
            catch (Exception ex)
            {
                sw.Stop();
                Add(res, new LogEntry { Job = job.Name, Database = d.Name, Kind = kt, Status = LogEntry.Failed, File = file, DurationSec = sw.Elapsed.TotalSeconds, Message = ex.Message });
            }
        }

        // ─────────── MySQL / PostgreSQL ───────────
        static List<string> RunTool(JobResult res, BackupJob job)
        {
            bool my = job.Engine == DbEngine.MySql;
            string kt = BackupJob.KindText(job.Kind);
            if (job.Kind == BackupKind.Globals)
            {
                var sw = Stopwatch.StartNew();
                string file = "";
                try
                {
                    string dir = job.TargetPath; Directory.CreateDirectory(dir);
                    file = Path.Combine(dir, BuildFileName(job.Engine, "server", BackupKind.Globals, job, DateTime.Now.ToString("yyyyMMdd_HHmmss"), ""));
                    Report("pg_dumpall 角色與權限…", -1);
                    PgTool.DumpGlobals(job.ToolConn, file);
                    if (!string.IsNullOrWhiteSpace(job.SecondaryPath)) { Directory.CreateDirectory(job.SecondaryPath); File.Copy(file, Path.Combine(job.SecondaryPath, Path.GetFileName(file)), true); }
                    Add(res, new LogEntry { Job = job.Name, Database = "(伺服器)", Kind = kt, Status = LogEntry.Success, File = file, SizeBytes = Len(file), DurationSec = sw.Elapsed.TotalSeconds, Message = "完成" });
                }
                catch (Exception ex) { Add(res, new LogEntry { Job = job.Name, Database = "(伺服器)", Kind = kt, Status = LogEntry.Failed, File = file, DurationSec = sw.Elapsed.TotalSeconds, Message = ex.Message }); }
                return new List<string> { "server" };
            }

            List<DbInfo> dbs;
            try { dbs = my ? MySqlTool.Databases(job.ToolConn) : PgTool.Databases(job.ToolConn); }
            catch (Exception ex)
            {
                Add(res, new LogEntry { Job = job.Name, Kind = kt, Status = LogEntry.Failed, Message = "無法連線 " + BackupJob.EngineText(job.Engine) + "：" + ex.Message });
                return null;
            }
            var targets = PickTargets(res, job, dbs);
            var done = new List<string>();
            for (int i = 0; i < targets.Count; i++)
            {
                var d = targets[i];
                if (my && (d.Name == "information_schema" || d.Name == "performance_schema")) continue;
                var sw = Stopwatch.StartNew();
                string file = "";
                try
                {
                    string dir = DirFor(job, job.TargetPath, d.Name);
                    Directory.CreateDirectory(dir);
                    file = Path.Combine(dir, BuildFileName(job.Engine, d.Name, BackupKind.Dump, job, DateTime.Now.ToString("yyyyMMdd_HHmmss"), ""));
                    string label = string.Format("[{0}/{1}] {2} {3}", i + 1, targets.Count, d.Name, my ? "mysqldump" : "pg_dump");
                    Report(label + "…", -1);
                    if (my) MySqlTool.Dump(job.ToolConn, d.Name, file, job, null);
                    else PgTool.Dump(job.ToolConn, d.Name, file, job, null);

                    string verifyMsg = "完成";
                    if (job.Verify)
                    {
                        Report(d.Name + " 驗證備份檔…", -1);
                        verifyMsg = VerifyDump(job, file);
                    }
                    CopySecondary(job, d.Name, file);
                    sw.Stop();
                    Add(res, new LogEntry { Job = job.Name, Database = d.Name, Kind = kt, Status = LogEntry.Success, File = file, SizeBytes = Len(file), DurationSec = sw.Elapsed.TotalSeconds, Message = verifyMsg });
                    done.Add(d.Name);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Add(res, new LogEntry { Job = job.Name, Database = d.Name, Kind = kt, Status = LogEntry.Failed, File = file, DurationSec = sw.Elapsed.TotalSeconds, Message = ex.Message });
                }
            }
            return done;
        }

        /// <summary>檢查 dump 檔是否完整：pg custom 格式用 pg_restore -l；SQL 檔檢查結尾標記。</summary>
        static string VerifyDump(BackupJob job, string file)
        {
            if (file.EndsWith(".dump", StringComparison.OrdinalIgnoreCase))
            {
                string toc = PgTool.VerifyCustom(job.ToolConn, file);
                int n = toc.Split('\n').Count(l => l.Length > 0 && !l.StartsWith(";"));
                return "已驗證（" + n + " 個物件）";
            }
            string tail = ReadTail(file, 4096);
            string mark = job.Engine == DbEngine.MySql ? "Dump completed" : "PostgreSQL database dump complete";
            if (tail.IndexOf(mark, StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("備份檔不完整（找不到結尾標記「" + mark + "」）");
            return "已驗證";
        }

        static string ReadTail(string file, int bytes)
        {
            using (var fs = File.OpenRead(file))
            {
                if (!file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    fs.Seek(Math.Max(0, fs.Length - bytes), SeekOrigin.Begin);
                    using (var r = new StreamReader(fs)) return r.ReadToEnd();
                }
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                {
                    var ring = new byte[bytes]; var buf = new byte[1 << 16]; long total = 0; int n;
                    while ((n = gz.Read(buf, 0, buf.Length)) > 0)
                        for (int i = 0; i < n; i++) ring[(total++) % bytes] = buf[i];
                    int len = (int)Math.Min(total, bytes); var outb = new byte[len];
                    for (int i = 0; i < len; i++) outb[i] = ring[(total - len + i) % bytes];
                    return Encoding.UTF8.GetString(outb);
                }
            }
        }

        static void CopySecondary(BackupJob job, string db, string file)
        {
            if (string.IsNullOrWhiteSpace(job.SecondaryPath)) return;
            Report(db + " 複製到第二備份位置…", -1);
            if (NetPath.IsNetwork(job.SecondaryPath)) NetShare.Connect(job.SecondaryPath, job.NetUser, job.NetPassword);
            string dir2 = DirFor(job, job.SecondaryPath, db);
            Directory.CreateDirectory(dir2);
            File.Copy(file, Path.Combine(dir2, Path.GetFileName(file)), true);
        }

        static long Len(string f) { try { return new FileInfo(f).Length; } catch { return 0; } }
        static string Short(string s) { s = (s ?? "").Trim(); return s.Length > 60 ? s.Substring(0, 60) + "…" : s; }

        static JobResult Finish(JobResult res)
        {
            var job = res.Job;
            job.LastRun = DateTime.Now;
            job.LastStatus = res.Failed > 0 ? LogEntry.Failed : res.Warnings > 0 ? LogEntry.Warning : LogEntry.Success;
            job.LastMessage = res.Summary;
            try { SettingsStore.Save(); } catch { }
            try { Mailer.SendJobReport(res); }
            catch (Exception ex) { Add(res, new LogEntry { Job = job.Name, Status = LogEntry.Warning, Message = "通知信寄送失敗：" + ex.Message }); }
            var h = JobFinished;
            if (h != null) try { h(res); } catch { }
            return res;
        }

        static void Add(JobResult res, LogEntry e)
        {
            if (e.Status == LogEntry.Success) res.Success++;
            else if (e.Status == LogEntry.Failed) res.Failed++;
            else if (e.Status == LogEntry.Warning) res.Warnings++;
            res.Entries.Add(e);
            Logger.Write(e);
        }

        public static string SafeName(string n)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n;
        }

        public static string Tag(DbEngine e, BackupKind k)
        {
            switch (k)
            {
                case BackupKind.Full: return "FULL";
                case BackupKind.Differential: return "DIFF";
                case BackupKind.Log: return "LOG";
                case BackupKind.Partial: return "PART";
                case BackupKind.PartialDiff: return "PDIFF";
                case BackupKind.Bacpac: return "BACPAC";
                case BackupKind.Globals: return "PGROLES";
                default: return e == DbEngine.MySql ? "MYSQL" : "PGSQL";
            }
        }

        static string BuildFileName(DbEngine e, string db, BackupKind kind, BackupJob job, string stamp, string stripe)
        {
            string ext;
            switch (kind)
            {
                case BackupKind.Log: ext = ".trn"; break;
                case BackupKind.Bacpac: ext = ".bacpac"; break;
                case BackupKind.Globals: ext = ".sql"; break;
                case BackupKind.Dump:
                    ext = e == DbEngine.PostgreSql && job.PgCustomFormat ? ".dump" : job.GzipDump ? ".sql.gz" : ".sql"; break;
                default: ext = ".bak"; break;
            }
            return SafeName(db) + "_" + Tag(e, kind) + "_" + stamp + stripe + ext;
        }

        static string BuildBackupSql(BackupJob job, string db, BackupKind kind, List<string> files, List<string> mirror, ServerInfo si)
        {
            Func<List<string>, string> dest = fs => string.Join(", ", fs.Select(f => (IsUrl(f) ? "URL = " : "DISK = ") + SqlOps.S(f)));
            var sb = new StringBuilder();
            sb.Append(kind == BackupKind.Log ? "BACKUP LOG " : "BACKUP DATABASE ");
            sb.Append(SqlOps.Q(db));
            if (kind == BackupKind.Partial || kind == BackupKind.PartialDiff) sb.Append(" READ_WRITE_FILEGROUPS");
            sb.Append(" TO ").Append(dest(files));
            if (mirror != null) sb.Append(" MIRROR TO ").Append(dest(mirror));
            var opts = new List<string> { "INIT", "STATS = 5", "NAME = " + SqlOps.S(db + " " + BackupJob.KindText(kind) + " by SqlBackupManager") };
            opts.Add(mirror != null ? "FORMAT" : "SKIP");
            if (kind == BackupKind.Differential || kind == BackupKind.PartialDiff) opts.Add("DIFFERENTIAL");
            if (job.CopyOnly && (kind == BackupKind.Full || kind == BackupKind.Log)) opts.Add("COPY_ONLY");
            if (job.Checksum) opts.Add("CHECKSUM");
            if (job.Compression && si.SupportsCompression) opts.Add("COMPRESSION");
            if (!string.IsNullOrWhiteSpace(job.EncryptCertificate) && !si.IsExpress)
                opts.Add("ENCRYPTION (ALGORITHM = " + (string.IsNullOrEmpty(job.EncryptAlgorithm) ? "AES_256" : job.EncryptAlgorithm) + ", SERVER CERTIFICATE = " + SqlOps.Q(job.EncryptCertificate.Trim()) + ")");
            sb.Append(" WITH ").Append(string.Join(", ", opts));
            return sb.ToString();
        }

        static readonly Regex OwnFileRx = new Regex(@"^(?<db>.+)_(?<tag>FULL|DIFF|LOG|PART|PDIFF|BACPAC|MYSQL|PGSQL|PGROLES)_\d{8}_\d{6}(_\d+of\d+)?\.(bak|trn|zip|bacpac|sql|sql\.gz|dump)$", RegexOptions.IgnoreCase);

        static void Cleanup(JobResult res, BackupJob job, string basePath, List<string> dbs)
        {
            DateTime cutoff = DateTime.Now.AddDays(-job.RetentionDays);
            string tag = Tag(job.Engine, job.Kind);
            int deleted = 0; long freed = 0;
            foreach (var db in dbs)
            {
                string dir = job.Kind == BackupKind.Globals ? basePath : DirFor(job, basePath, db);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir))
                {
                    // 只刪除本程式產生、同資料庫、同類型的備份檔
                    var m = OwnFileRx.Match(Path.GetFileName(f));
                    if (!m.Success || !m.Groups["db"].Value.Equals(SafeName(db), StringComparison.OrdinalIgnoreCase) || !m.Groups["tag"].Value.Equals(tag, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.LastWriteTime < cutoff) { long len = fi.Length; fi.Delete(); deleted++; freed += len; }
                    }
                    catch (Exception ex)
                    {
                        Add(res, new LogEntry { Job = job.Name, Database = db, Status = LogEntry.Warning, File = f, Message = "刪除過期備份失敗：" + ex.Message });
                    }
                }
            }
            if (deleted > 0)
                Add(res, new LogEntry { Job = job.Name, Kind = "清理", Status = LogEntry.Info, File = basePath, SizeBytes = freed, Message = "已刪除超過 " + job.RetentionDays + " 天的備份檔 " + deleted + " 個" });
        }
    }

    /// <summary>網路路徑判斷與對應磁碟機 → UNC 轉換。</summary>
    public static class NetPath
    {
        [System.Runtime.InteropServices.DllImport("mpr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

        public static bool IsNetwork(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return false;
            if (p.StartsWith(@"\\")) return true;
            try
            {
                string root = Path.GetPathRoot(p);
                if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':') return false;
                return new DriveInfo(root.Substring(0, 1)).DriveType == DriveType.Network;
            }
            catch { return false; }
        }

        /// <summary>Z:\Backup → \\NAS\share\Backup (對應磁碟機才轉換)。</summary>
        public static string ToUnc(string p)
        {
            try
            {
                if (p.Length < 2 || p[1] != ':') return p;
                var sb = new StringBuilder(512); int len = sb.Capacity;
                if (WNetGetConnection(p.Substring(0, 2), sb, ref len) == 0)
                    return sb.ToString().TrimEnd('\\') + p.Substring(2);
            }
            catch { }
            return p;
        }

        /// <summary>SQL Server 是否就在這台電腦上 (才能使用本機暫存再複製)。</summary>
        public static bool IsLocalServer(string server)
        {
            string h = (server ?? "").Trim();
            if (h.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) h = h.Substring(4);
            if (h.StartsWith("np:", StringComparison.OrdinalIgnoreCase) || h.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase)) h = h.Substring(h.IndexOf(':') + 1);
            h = h.Split('\\')[0].Split(',')[0].Trim();
            if (h == "" || h == "." || h == "(local)" || h == "(localdb)") return true;
            if (h.Equals("localhost", StringComparison.OrdinalIgnoreCase) || h == "127.0.0.1" || h == "::1") return true;
            if (h.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                var mine = System.Net.Dns.GetHostAddresses(Environment.MachineName);
                return System.Net.Dns.GetHostAddresses(h).Any(a => mine.Any(m => m.Equals(a)));
            }
            catch { return false; }
        }
    }

    /// <summary>用指定帳號連線到網路分享 (\\\\伺服器\\分享)，並測試是否可寫入。</summary>
    public static class NetShare
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        class NETRESOURCE
        {
            public int dwScope, dwType = 1 /* DISK */, dwDisplayType, dwUsage;
            public string lpLocalName, lpRemoteName, lpComment, lpProvider;
        }

        [System.Runtime.InteropServices.DllImport("mpr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int WNetAddConnection2(NETRESOURCE res, string password, string user, int flags);
        [System.Runtime.InteropServices.DllImport("mpr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int WNetCancelConnection2(string name, int flags, bool force);

        /// <summary>\\\\server\\share\\a\\b → \\\\server\\share</summary>
        public static string ShareRoot(string unc)
        {
            var parts = unc.TrimStart('\\').Split('\\');
            return parts.Length >= 2 ? @"\\" + parts[0] + "\\" + parts[1] : unc;
        }

        /// <summary>以帳號密碼連線；帳號空白則使用目前登入身分。失敗會丟出例外。</summary>
        public static void Connect(string path, string user, string password)
        {
            if (string.IsNullOrWhiteSpace(user)) return;
            string root = ShareRoot(NetPath.ToUnc(path));
            var nr = new NETRESOURCE { lpRemoteName = root };
            int rc = WNetAddConnection2(nr, password, user, 0);
            if (rc == 1219)
            {
                // 已經用其他帳號連過同一台伺服器：先中斷這個分享再重連
                WNetCancelConnection2(root, 0, true);
                rc = WNetAddConnection2(nr, password, user, 0);
                if (rc == 1219) rc = 0; // 仍有其他分享以不同帳號連線中，沿用現有連線並以實際寫入測試為準
            }
            if (rc != 0 && rc != 85 /* 已連線 */)
                throw new System.ComponentModel.Win32Exception(rc, "無法連線到 " + root + "：" + new System.ComponentModel.Win32Exception(rc).Message + "（代碼 " + rc + "）");
        }

        /// <summary>連線後建立資料夾、寫入並刪除測試檔，確認可寫入。</summary>
        public static void TestWrite(string path, string user, string password)
        {
            string unc = NetPath.ToUnc(path);
            Connect(unc, user, password);
            try
            {
                Directory.CreateDirectory(unc);
                string f = Path.Combine(unc, "_sbm_write_test_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
                File.WriteAllText(f, "SqlBackupManager " + DateTime.Now.ToString("s"));
                File.Delete(f);
            }
            catch (UnauthorizedAccessException) { throw new Exception(unc + "：帳號沒有寫入權限"); }
            catch (IOException ex) { throw new Exception(unc + "：" + ex.Message); }
        }
    }

    public static class Mailer
    {
        public static void SendJobReport(JobResult res)
        {
            var m = SettingsStore.Current.Mail;
            if (!m.Enabled) return;
            if (res.Ok && !m.NotifyOnSuccess) return;
            if (!res.Ok && !m.NotifyOnFailure) return;

            var sb = new StringBuilder();
            sb.Append("<div style='font-family:Microsoft JhengHei,Segoe UI,sans-serif;font-size:14px'>");
            sb.AppendFormat("<h3 style='color:{0}'>{1}：{2}</h3>", res.Ok ? "#107C10" : "#C42B1C", res.Ok ? "備份成功" : "備份失敗", WebUtility.HtmlEncode(res.Job.Name));
            sb.AppendFormat("<p>伺服器：{0}<br/>主機：{1}<br/>時間：{2:yyyy/MM/dd HH:mm:ss}<br/>結果：{3}</p>",
                WebUtility.HtmlEncode(BackupJob.EngineText(res.Job.Engine) + " " + res.Job.ServerText()), Environment.MachineName, DateTime.Now, res.Summary);
            sb.Append("<table cellpadding='6' style='border-collapse:collapse;font-size:13px' border='1'><tr style='background:#f0f0f0'><th>資料庫</th><th>類型</th><th>狀態</th><th>大小</th><th>耗時</th><th>訊息</th></tr>");
            foreach (var e in res.Entries)
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td></tr>",
                    WebUtility.HtmlEncode(e.Database), e.Kind, e.Status, Fmt.Size(e.SizeBytes), Fmt.Duration(e.DurationSec), WebUtility.HtmlEncode(e.Message));
            sb.Append("</table></div>");
            Send((res.Ok ? "[備份成功] " : "[備份失敗] ") + res.Job.Name + " - " + Environment.MachineName, sb.ToString());
        }

        public static void Send(string subject, string html)
        {
            var m = SettingsStore.Current.Mail;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            using (var msg = new MailMessage())
            using (var client = new SmtpClient(m.Host, m.Port))
            {
                msg.From = new MailAddress(string.IsNullOrWhiteSpace(m.From) ? m.User : m.From, "SQL 備份管理");
                foreach (var to in m.To.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    msg.To.Add(to.Trim());
                msg.Subject = subject;
                msg.SubjectEncoding = msg.BodyEncoding = Encoding.UTF8;
                msg.Body = html;
                msg.IsBodyHtml = true;
                client.EnableSsl = m.EnableSsl;
                client.Timeout = 30000;
                if (!string.IsNullOrEmpty(m.User)) client.Credentials = new NetworkCredential(m.User, m.Password);
                client.Send(msg);
            }
        }
    }

    /// <summary>每 15 秒檢查一次各工作的下次執行時間。</summary>
    public class Scheduler : IDisposable
    {
        readonly System.Threading.Timer timer;
        readonly HashSet<string> running = new HashSet<string>();
        public event Action Changed;

        public Scheduler()
        {
            var now = DateTime.Now;
            bool changed = false;
            foreach (var j in SettingsStore.Current.Jobs)
            {
                if (!j.NextRun.HasValue) { j.NextRun = ScheduleCalc.Next(j, now); changed = true; }
            }
            if (changed) try { SettingsStore.Save(); } catch { }
            timer = new System.Threading.Timer(_ => Tick(), null, 5000, 15000);
        }

        void Tick()
        {
            var g = SettingsStore.Current.General;
            if (g.SchedulerPaused) return;
            var now = DateTime.Now;
            foreach (var j in SettingsStore.Current.Jobs.ToList())
            {
                if (!j.Enabled || !j.NextRun.HasValue || j.NextRun.Value > now) continue;
                lock (running) { if (running.Contains(j.Id)) continue; }

                bool missed = (now - j.NextRun.Value).TotalMinutes > 10;
                DateTime due = j.NextRun.Value;
                j.NextRun = ScheduleCalc.Next(j, now);
                try { SettingsStore.Save(); } catch { }
                RaiseChanged();

                if (missed && !g.RunMissedJobs)
                {
                    Logger.Write(new LogEntry { Job = j.Name, Status = LogEntry.Warning, Message = "錯過排程時間 " + due.ToString("yyyy/MM/dd HH:mm") + "（程式未執行），依設定不補執行" });
                    continue;
                }
                if (missed)
                    Logger.Write(new LogEntry { Job = j.Name, Status = LogEntry.Info, Message = "補執行錯過的排程 " + due.ToString("yyyy/MM/dd HH:mm") });

                var job = j;
                lock (running) running.Add(job.Id);
                Task.Run(() =>
                {
                    try { BackupEngine.RunJob(job); }
                    catch (Exception ex) { Logger.Write(new LogEntry { Job = job.Name, Status = LogEntry.Failed, Message = ex.Message }); }
                    finally { lock (running) running.Remove(job.Id); RaiseChanged(); }
                });
            }
        }

        void RaiseChanged() { var h = Changed; if (h != null) try { h(); } catch { } }

        public static void Reschedule(BackupJob j) { j.NextRun = j.Enabled ? ScheduleCalc.Next(j, DateTime.Now) : null; }

        public void Dispose() { timer.Dispose(); }
    }
}
