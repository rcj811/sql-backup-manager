using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlBackupManager
{
    public class BackupItem
    {
        public DbEngine Engine = DbEngine.SqlServer;
        public List<string> Parts = new List<string>();   // 分割備份的其他檔案 (含 File 本身)
        public string File;
        public string Database;
        public BackupKind Kind;
        public DateTime Time;
        public long Size;
        public bool IsZip;
        public int Position = 1;
        public string KindText { get { return BackupJob.KindText(Kind); } }
        public List<string> AllFiles { get { return Parts.Count > 0 ? Parts : new List<string> { File }; } }
        public bool IsBase { get { return Kind == BackupKind.Full || Kind == BackupKind.Partial; } }
        public bool IsDiff { get { return Kind == BackupKind.Differential || Kind == BackupKind.PartialDiff; } }
    }

    public class RestorePlan
    {
        public List<BackupItem> Chain = new List<BackupItem>();
        public string TargetDb;
        public bool Overwrite;
        public bool SafetyBackup = true;
        public string SafetyBackupDir;
        public bool KillConnections = true;
        public bool LeaveRestoring = false;  // 保持 NORECOVERY 以便之後繼續套用記錄
        public DateTime? StopAt;
        public bool TailLog;                 // 覆寫前先做尾端記錄備份並套用 (還原到最新狀態)
    }

    public static class RestoreEngine
    {
        static readonly Regex NamePattern = new Regex(@"^(?<db>.+)_(?<k>FULL|DIFF|LOG|PART|PDIFF|BACPAC|MYSQL|PGSQL|PGROLES|PRE-RESTORE|TAIL)_(?<t>\d{8}_\d{6})(?<s>_(?<i>\d+)of(?<n>\d+))?\.(?<e>bak|trn|zip|bacpac|sql|sql\.gz|dump)$", RegexOptions.IgnoreCase);

        public static DbEngine EngineOf(string file)
        {
            var m = NamePattern.Match(Path.GetFileName(file));
            string k = m.Success ? m.Groups["k"].Value.ToUpperInvariant() : "";
            if (k == "MYSQL") return DbEngine.MySql;
            if (k == "PGSQL" || k == "PGROLES") return DbEngine.PostgreSql;
            if (k == "PRE-RESTORE" && !Regex.IsMatch(file, @"\.(bak|zip)$", RegexOptions.IgnoreCase))
                return file.EndsWith(".dump", StringComparison.OrdinalIgnoreCase) ? DbEngine.PostgreSql : DbEngine.MySql;
            return DbEngine.SqlServer;
        }

        /// <summary>掃描資料夾內的備份檔 (含子資料夾)。本程式產生的檔名直接解析；其他 .bak/.trn 會讀取檔頭。</summary>
        public static List<BackupItem> Scan(string folder, Action<string> status)
        {
            var list = new List<BackupItem>();
            if (!Directory.Exists(folder)) return list;
            string[] exts = { ".bak", ".trn", ".zip", ".bacpac", ".sql", ".gz", ".dump" };
            var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => f.IndexOf(@"\_restore_tmp\", StringComparison.OrdinalIgnoreCase) < 0)
                .ToList();
            var stripes = new Dictionary<string, BackupItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                var m = NamePattern.Match(fi.Name);
                if (m.Success)
                {
                    string k = m.Groups["k"].Value.ToUpperInvariant();
                    string e = m.Groups["e"].Value.ToLowerInvariant();
                    BackupKind kind;
                    switch (k)
                    {
                        case "FULL": case "PRE-RESTORE": kind = e == "bak" || e == "zip" ? BackupKind.Full : BackupKind.Dump; break;
                        case "DIFF": kind = BackupKind.Differential; break;
                        case "LOG": case "TAIL": kind = BackupKind.Log; break;
                        case "PART": kind = BackupKind.Partial; break;
                        case "PDIFF": kind = BackupKind.PartialDiff; break;
                        case "BACPAC": kind = BackupKind.Bacpac; break;
                        case "PGROLES": kind = BackupKind.Globals; break;
                        default: kind = BackupKind.Dump; break;
                    }
                    var item = new BackupItem
                    {
                        File = f, Engine = EngineOf(f), Database = m.Groups["db"].Value, Kind = kind,
                        Time = DateTime.ParseExact(m.Groups["t"].Value, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture),
                        Size = fi.Length, IsZip = e == "zip"
                    };
                    if (m.Groups["s"].Success)
                    {
                        // 分割備份：同一組合併成一個還原點
                        string key = Path.Combine(fi.DirectoryName, fi.Name.Replace(m.Groups["s"].Value, ""));
                        BackupItem g;
                        if (!stripes.TryGetValue(key, out g)) { g = item; g.Parts = new List<string>(); stripes[key] = g; list.Add(g); }
                        else g.Size += fi.Length;
                        g.Parts.Add(f);
                        g.Parts.Sort(StringComparer.OrdinalIgnoreCase);
                        g.File = g.Parts[0];
                    }
                    else list.Add(item);
                }
                else if (fi.Extension.Equals(".bak", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".trn", StringComparison.OrdinalIgnoreCase))
                {
                    if (status != null) status("讀取檔頭：" + fi.Name);
                    try { list.AddRange(FromHeaders(f)); }
                    catch { /* 不是有效的備份檔 */ }
                }
                else if (fi.Extension.Equals(".bacpac", StringComparison.OrdinalIgnoreCase))
                    list.Add(new BackupItem { File = f, Database = Path.GetFileNameWithoutExtension(f), Kind = BackupKind.Bacpac, Time = fi.LastWriteTime, Size = fi.Length });
                else if (fi.Extension.Equals(".dump", StringComparison.OrdinalIgnoreCase))
                    list.Add(new BackupItem { File = f, Engine = DbEngine.PostgreSql, Database = Path.GetFileNameWithoutExtension(f), Kind = BackupKind.Dump, Time = fi.LastWriteTime, Size = fi.Length });
            }
            return list.OrderBy(x => x.Database).ThenBy(x => x.Time).ToList();
        }

        public static List<BackupItem> FromHeaders(string file)
        {
            var res = new List<BackupItem>();
            var hs = SqlOps.ReadHeaders(SettingsStore.Current.Connection, file);
            for (int i = 0; i < hs.Count; i++)
            {
                var h = hs[i];
                if (h.BackupType != 1 && h.BackupType != 2 && h.BackupType != 5) continue;
                res.Add(new BackupItem
                {
                    File = file,
                    Database = h.DatabaseName,
                    Kind = h.BackupType == 1 ? BackupKind.Full : h.BackupType == 5 ? BackupKind.Differential : BackupKind.Log,
                    Time = h.StartDate,
                    Size = h.Size,
                    Position = i + 1
                });
            }
            return res;
        }

        /// <summary>依選擇的還原點自動組出還原鏈：完整 → (最近的差異) → 之後的交易記錄。</summary>
        public static List<BackupItem> BuildChain(List<BackupItem> all, BackupItem target)
        {
            var chain = new List<BackupItem>();
            if (target.Engine != DbEngine.SqlServer || target.Kind == BackupKind.Bacpac || target.IsBase) { chain.Add(target); return chain; }

            var same = all.Where(x => x.Engine == DbEngine.SqlServer && x.Database.Equals(target.Database, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Time).ToList();
            bool partial = target.Kind == BackupKind.PartialDiff;
            var full = same.LastOrDefault(x => (partial ? x.Kind == BackupKind.Partial : x.Kind == BackupKind.Full) && x.Time < target.Time);
            if (full == null) throw new InvalidOperationException("找不到 " + target.Time.ToString("yyyy/MM/dd HH:mm") + " 之前的" + (partial ? "部分備份" : "完整備份") + "，無法組成還原鏈。");
            chain.Add(full);

            if (target.IsDiff) { chain.Add(target); return chain; }

            var diff = same.LastOrDefault(x => x.Kind == BackupKind.Differential && x.Time > full.Time && x.Time < target.Time);
            DateTime baseTime = full.Time;
            if (diff != null) { chain.Add(diff); baseTime = diff.Time; }
            chain.AddRange(same.Where(x => x.Kind == BackupKind.Log && x.Time > baseTime && x.Time <= target.Time));
            return chain;
        }

        /// <summary>依備份檔類型分派到對應的還原方式。</summary>
        public static void Execute(RestorePlan plan)
        {
            if (plan.Chain.Count == 0) throw new InvalidOperationException("沒有選擇備份檔。");
            var first = plan.Chain[0];
            if (first.Engine == DbEngine.SqlServer && first.Kind != BackupKind.Bacpac) { ExecuteNative(plan); return; }

            string db = plan.TargetDb.Trim();
            BackupEngine.Gate.Wait();
            BackupEngine.Busy = true;
            var sw = Stopwatch.StartNew();
            string how = first.Engine == DbEngine.SqlServer ? "BACPAC 匯入" : first.Engine == DbEngine.MySql ? "MySQL 還原" : "PostgreSQL 還原";
            try
            {
                if (first.Kind == BackupKind.Globals)
                {
                    BackupEngine.Report("還原角色與權限…", -1);
                    PgTool.Restore("postgres", first.File, false, false);
                }
                else
                {
                    bool exists = first.Engine == DbEngine.SqlServer ? SqlOps.DatabaseExists(SettingsStore.Current.Connection, db)
                                : first.Engine == DbEngine.MySql ? MySqlTool.Exists(db) : PgTool.Exists(db);
                    if (exists && !plan.Overwrite)
                        throw new InvalidOperationException("資料庫 " + db + " 已存在。若要覆寫請勾選「覆寫現有資料庫」，或改用新的資料庫名稱。");

                    if (exists && plan.SafetyBackup)
                    {
                        string dir = string.IsNullOrWhiteSpace(plan.SafetyBackupDir) ? Path.GetDirectoryName(first.File) : plan.SafetyBackupDir;
                        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        BackupEngine.Report("還原前備份現有 " + db + "…", -1);
                        string safety;
                        if (first.Engine == DbEngine.SqlServer)
                        {
                            safety = Path.Combine(dir, BackupEngine.SafeName(db) + "_PRE-RESTORE_" + stamp + ".bak");
                            SqlOps.ExecWithProgress(SettingsStore.Current.Connection, "BACKUP DATABASE " + SqlOps.Q(db) + " TO DISK = " + SqlOps.S(safety) + " WITH COPY_ONLY, INIT, STATS = 10", null);
                        }
                        else if (first.Engine == DbEngine.MySql)
                        {
                            safety = Path.Combine(dir, BackupEngine.SafeName(db) + "_PRE-RESTORE_" + stamp + ".sql.gz");
                            MySqlTool.Dump(db, safety, new BackupJob(), null);
                        }
                        else
                        {
                            safety = Path.Combine(dir, BackupEngine.SafeName(db) + "_PRE-RESTORE_" + stamp + ".dump");
                            PgTool.Dump(db, safety, new BackupJob { PgCustomFormat = true }, null);
                        }
                        Logger.Write(new LogEntry { Job = "還原", Database = db, Kind = "還原前備份", Status = LogEntry.Success, File = safety, SizeBytes = SafeLen(safety), Message = "覆寫前的安全備份" });
                    }

                    BackupEngine.Report(how + " " + db + "…", -1);
                    if (first.Engine == DbEngine.SqlServer)
                    {
                        if (exists)
                        {
                            var conn = SettingsStore.Current.Connection;
                            if (plan.KillConnections) try { SqlOps.Exec(conn, "ALTER DATABASE " + SqlOps.Q(db) + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE"); } catch { }
                            SqlOps.Exec(conn, "DROP DATABASE " + SqlOps.Q(db));
                        }
                        SqlPackageTool.Import(db, first.File, line => BackupEngine.Report(how + "：" + (line.Length > 60 ? line.Substring(0, 60) : line), -1));
                    }
                    else if (first.Engine == DbEngine.MySql) MySqlTool.Restore(db, first.File, exists);
                    else PgTool.Restore(db, first.File, exists, plan.KillConnections);
                }
                sw.Stop();
                Logger.Write(new LogEntry { Job = "還原", Database = first.Kind == BackupKind.Globals ? "(伺服器)" : db, Kind = how, Status = LogEntry.Success, File = first.File, DurationSec = sw.Elapsed.TotalSeconds, Message = "還原完成" });
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.Write(new LogEntry { Job = "還原", Database = db, Kind = how, Status = LogEntry.Failed, File = first.File, DurationSec = sw.Elapsed.TotalSeconds, Message = ex.Message });
                throw;
            }
            finally
            {
                BackupEngine.Busy = false;
                BackupEngine.Gate.Release();
                BackupEngine.Report("就緒", 0);
            }
        }

        static void ExecuteNative(RestorePlan plan)
        {
            var conn = SettingsStore.Current.Connection;
            string db = plan.TargetDb.Trim();
            string[] sys = { "master", "model", "msdb", "tempdb" };
            if (sys.Contains(db, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("系統資料庫 " + db + " 不能在此還原，請使用 SQL Server 單一使用者模式手動處理。");
            if (plan.Chain.Count == 0) throw new InvalidOperationException("沒有選擇備份檔。");

            BackupEngine.Gate.Wait();
            BackupEngine.Busy = true;
            var sw = Stopwatch.StartNew();
            var temps = new List<string>();
            bool exists = false, singleUser = false;
            string chainText = string.Join(" → ", plan.Chain.Select(c => c.KindText + " " + c.Time.ToString("MM/dd HH:mm")));
            try
            {
                exists = SqlOps.DatabaseExists(conn, db);
                if (exists && !plan.Overwrite)
                    throw new InvalidOperationException("資料庫 " + db + " 已存在。若要覆寫請勾選「覆寫現有資料庫」，或改用新的資料庫名稱。");

                // 1. 解壓 ZIP (分割備份會有多個檔案)
                var files = new List<List<string>>();
                foreach (var it in plan.Chain)
                {
                    if (!it.IsZip) { files.Add(it.AllFiles); continue; }
                    BackupEngine.Report("解壓縮 " + Path.GetFileName(it.File) + "…", -1);
                    string tmpDir = Path.Combine(Path.GetDirectoryName(it.File), "_restore_tmp");
                    Directory.CreateDirectory(tmpDir);
                    var parts = new List<string>();
                    using (var za = ZipFile.OpenRead(it.File))
                    {
                        foreach (var entry in za.Entries.Where(e => e.Name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".trn", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            string outFile = Path.Combine(tmpDir, entry.Name);
                            entry.ExtractToFile(outFile, true);
                            temps.Add(outFile);
                            parts.Add(outFile);
                        }
                    }
                    if (parts.Count == 0) throw new InvalidOperationException("ZIP 檔內找不到 .bak / .trn：" + it.File);
                    files.Add(parts);
                }
                Func<List<string>, string> from = fs => string.Join(", ", fs.Select(f => "DISK = " + SqlOps.S(f)));

                // 2. 決定資料檔位置 (MOVE)
                var si = SqlOps.GetServerInfo(conn);
                var fileList = SqlOps.ReadFileList(conn, files[0]);
                var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (exists)
                {
                    using (var cn = SqlOps.Open(conn))
                    using (var cmd = new SqlCommand("SELECT name, physical_name FROM sys.master_files WHERE database_id = DB_ID(@n)", cn))
                    {
                        cmd.Parameters.AddWithValue("@n", db);
                        using (var r = cmd.ExecuteReader()) while (r.Read()) current[r.GetString(0)] = r.GetString(1);
                    }
                }
                var moves = new List<string>();
                int dataIdx = 0, logIdx = 0;
                foreach (var f in fileList)
                {
                    string dest;
                    if (!current.TryGetValue(f.LogicalName, out dest))
                    {
                        string ext = Path.GetExtension(f.PhysicalName);
                        if (f.Type == "L")
                            dest = Path.Combine(si.DefaultLogPath, BackupEngine.SafeName(db) + (logIdx++ == 0 ? "_log" : "_" + f.LogicalName) + (string.IsNullOrEmpty(ext) ? ".ldf" : ext));
                        else if (f.Type == "S")
                            dest = Path.Combine(si.DefaultDataPath, BackupEngine.SafeName(db) + "_" + f.LogicalName);
                        else
                            dest = Path.Combine(si.DefaultDataPath, BackupEngine.SafeName(db) + (dataIdx++ == 0 ? "" : "_" + f.LogicalName) + (string.IsNullOrEmpty(ext) ? ".mdf" : ext));
                    }
                    moves.Add("MOVE " + SqlOps.S(f.LogicalName) + " TO " + SqlOps.S(dest));
                }

                // 3. 還原前先備份現有資料庫
                if (exists && plan.SafetyBackup)
                {
                    string dir = string.IsNullOrWhiteSpace(plan.SafetyBackupDir) ? si.DefaultBackupPath : plan.SafetyBackupDir;
                    string safety = Path.Combine(dir, BackupEngine.SafeName(db) + "_PRE-RESTORE_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
                    BackupEngine.Report("還原前備份現有 " + db + "…", 0);
                    SqlOps.ExecWithProgress(conn, "BACKUP DATABASE " + SqlOps.Q(db) + " TO DISK = " + SqlOps.S(safety) + " WITH COPY_ONLY, INIT, STATS = 5",
                        p => BackupEngine.Report("還原前備份 " + db + " " + p + "%", p));
                    Logger.Write(new LogEntry { Job = "還原", Database = db, Kind = "還原前備份", Status = LogEntry.Success, File = safety, SizeBytes = SafeLen(safety), Message = "覆寫前的安全備份" });
                }

                // 4. 中斷其他連線
                if (exists && plan.KillConnections)
                {
                    try
                    {
                        SqlOps.Exec(conn, "IF DATABASEPROPERTYEX(" + SqlOps.S(db) + ", 'Status') = 'ONLINE' ALTER DATABASE " + SqlOps.Q(db) + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
                        singleUser = true;
                    }
                    catch (Exception ex) { Logger.Write(new LogEntry { Job = "還原", Database = db, Status = LogEntry.Warning, Message = "無法切換單一使用者模式：" + ex.Message }); }
                }

                // 4b. 尾端記錄備份 (Tail-log)：把最後一次記錄備份之後的交易也保留下來
                if (exists && plan.TailLog)
                {
                    string dir = string.IsNullOrWhiteSpace(plan.SafetyBackupDir) ? si.DefaultBackupPath : plan.SafetyBackupDir;
                    string tail = Path.Combine(dir, BackupEngine.SafeName(db) + "_TAIL_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".trn");
                    BackupEngine.Report("尾端記錄備份 " + db + "…", -1);
                    SqlOps.ExecWithProgress(conn, "BACKUP LOG " + SqlOps.Q(db) + " TO DISK = " + SqlOps.S(tail) + " WITH NORECOVERY, INIT, STATS = 10", null);
                    Logger.Write(new LogEntry { Job = "還原", Database = db, Kind = "尾端記錄備份", Status = LogEntry.Success, File = tail, SizeBytes = SafeLen(tail), Message = "還原前的 Tail-log 備份" });
                    plan.Chain.Add(new BackupItem { File = tail, Database = db, Kind = BackupKind.Log, Time = DateTime.Now });
                    files.Add(new List<string> { tail });
                    singleUser = false; // NORECOVERY 後資料庫已是 RESTORING 狀態
                }

                // 5. 依序還原
                for (int i = 0; i < plan.Chain.Count; i++)
                {
                    var it = plan.Chain[i];
                    bool last = i == plan.Chain.Count - 1;
                    var opts = new List<string> { "FILE = " + it.Position, "STATS = 5" };
                    if (i == 0) { opts.AddRange(moves); if (exists && !plan.TailLog) opts.Add("REPLACE"); if (it.Kind == BackupKind.Partial) opts.Add("PARTIAL"); }
                    opts.Add(last && !plan.LeaveRestoring ? "RECOVERY" : "NORECOVERY");
                    if (last && it.Kind == BackupKind.Log && plan.StopAt.HasValue)
                        opts.Add("STOPAT = '" + plan.StopAt.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "'");
                    string sql = (it.Kind == BackupKind.Log ? "RESTORE LOG " : "RESTORE DATABASE ") + SqlOps.Q(db) +
                                 " FROM " + from(files[i]) + " WITH " + string.Join(", ", opts);
                    string label = string.Format("還原 [{0}/{1}] {2} {3}", i + 1, plan.Chain.Count, it.KindText, it.Time.ToString("MM/dd HH:mm"));
                    BackupEngine.Report(label + " 0%", 0);
                    SqlOps.ExecWithProgress(conn, sql, p => BackupEngine.Report(label + " " + p + "%", p));
                }

                if (!plan.LeaveRestoring)
                {
                    try { SqlOps.Exec(conn, "ALTER DATABASE " + SqlOps.Q(db) + " SET MULTI_USER"); } catch { }
                }
                singleUser = false;
                sw.Stop();
                Logger.Write(new LogEntry
                {
                    Job = "還原", Database = db, Kind = "還原", Status = LogEntry.Success, File = plan.Chain.Last().File,
                    DurationSec = sw.Elapsed.TotalSeconds,
                    Message = "還原完成：" + chainText + (plan.StopAt.HasValue ? "，時間點 " + plan.StopAt.Value.ToString("yyyy/MM/dd HH:mm:ss") : "") + (plan.LeaveRestoring ? "（保持 RESTORING 狀態）" : "")
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.Write(new LogEntry { Job = "還原", Database = db, Kind = "還原", Status = LogEntry.Failed, DurationSec = sw.Elapsed.TotalSeconds, Message = ex.Message + "（" + chainText + "）" });
                if (singleUser) { try { SqlOps.Exec(conn, "ALTER DATABASE " + SqlOps.Q(db) + " SET MULTI_USER"); } catch { } }
                throw;
            }
            finally
            {
                foreach (var t in temps) { try { File.Delete(t); } catch { } }
                foreach (var d in temps.Select(Path.GetDirectoryName).Distinct())
                    try { if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d); } catch { }
                BackupEngine.Busy = false;
                BackupEngine.Gate.Release();
                BackupEngine.Report("就緒", 0);
            }
        }

        static long SafeLen(string f) { try { return new FileInfo(f).Length; } catch { return 0; } }
    }
}
