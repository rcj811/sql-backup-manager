using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace SqlBackupManager
{
    public static class AppPaths
    {
        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SqlBackupManager");
                Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.xml"); } }
        public static string LogDir { get { string d = Path.Combine(DataDir, "logs"); Directory.CreateDirectory(d); return d; } }
    }

    /// <summary>密碼以 DPAPI (本機層級) 加密保存，只有這台伺服器能解開。</summary>
    public static class Crypto
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SqlBackupManager.v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(data);
        }

        public static string Unprotect(string enc)
        {
            if (string.IsNullOrEmpty(enc)) return "";
            try
            {
                var data = ProtectedData.Unprotect(Convert.FromBase64String(enc), Entropy, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(data);
            }
            catch { return ""; }
        }
    }

    public static class SettingsStore
    {
        static readonly object Sync = new object();
        public static AppSettings Current = new AppSettings();

        public static void Load()
        {
            lock (Sync)
            {
                try
                {
                    if (File.Exists(AppPaths.SettingsFile))
                    {
                        using (var fs = File.OpenRead(AppPaths.SettingsFile))
                            Current = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(fs);
                    }
                }
                catch (Exception ex)
                {
                    // 設定檔損毀時先備份一份再用預設值
                    try { File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + ".broken_" + DateTime.Now.ToString("yyyyMMddHHmmss")); } catch { }
                    Current = new AppSettings();
                    Logger.Write(new LogEntry { Status = LogEntry.Warning, Message = "設定檔讀取失敗，已改用預設值：" + ex.Message });
                }
                if (Current.Jobs == null) Current.Jobs = new List<BackupJob>();
                // 舊版設定：連線原本是全域設定，移轉到每個工作 (需重新驗證)
                foreach (var j in Current.Jobs)
                {
                    if (j.ConnSet) continue;
                    j.SqlConn = (Current.Connection ?? new ConnectionSettings()).Copy();
                    j.ToolConn = (j.Engine == DbEngine.PostgreSql ? Current.PgSql : Current.MySql ?? new ToolConnection()).Copy();
                    j.ConnSet = true;
                    j.ConnVerified = false;
                }
            }
        }

        public static void Save()
        {
            lock (Sync)
            {
                string tmp = AppPaths.SettingsFile + ".tmp";
                using (var fs = File.Create(tmp))
                    new XmlSerializer(typeof(AppSettings)).Serialize(fs, Current);
                if (File.Exists(AppPaths.SettingsFile)) File.Replace(tmp, AppPaths.SettingsFile, null);
                else File.Move(tmp, AppPaths.SettingsFile);
            }
        }
    }

    public static class ScheduleCalc
    {
        /// <summary>計算 after 之後的下一次執行時間；沒有下一次則回傳 null。</summary>
        public static DateTime? Next(BackupJob j, DateTime after)
        {
            DateTime start = j.StartDate.Date;
            TimeSpan tod = TimeSpan.FromMinutes(j.TimeOfDayMinutes);
            DateTime from = after < start ? start.AddTicks(-1) : after;

            switch (j.Schedule)
            {
                case ScheduleKind.Once:
                    {
                        DateTime t = start + tod;
                        return t > after ? (DateTime?)t : null;
                    }
                case ScheduleKind.Daily:
                    {
                        DateTime t = from.Date + tod;
                        if (t <= from) t = t.AddDays(1);
                        return t;
                    }
                case ScheduleKind.Weekly:
                    {
                        if ((j.WeekDaysMask & 0x7F) == 0) return null;
                        for (int i = 0; i <= 7; i++)
                        {
                            DateTime t = from.Date.AddDays(i) + tod;
                            if (t > from && (j.WeekDaysMask & (1 << (int)t.DayOfWeek)) != 0) return t;
                        }
                        return null;
                    }
                case ScheduleKind.Monthly:
                    {
                        for (int i = 0; i <= 13; i++)
                        {
                            DateTime m = new DateTime(from.Year, from.Month, 1).AddMonths(i);
                            int dim = DateTime.DaysInMonth(m.Year, m.Month);
                            int day = j.MonthDay <= 0 ? dim : Math.Min(j.MonthDay, dim);
                            DateTime t = m.AddDays(day - 1) + tod;
                            if (t > from) return t;
                        }
                        return null;
                    }
                default:
                    {
                        int iv = Math.Max(1, j.IntervalMinutes);
                        TimeSpan end = TimeSpan.FromMinutes(j.IntervalEndMinutes);
                        if (end <= tod) end = end + TimeSpan.FromDays(1); // 結束 <= 開始：跨午夜或全天
                        for (int d = -1; d <= 2; d++)
                        {
                            DateTime day = from.Date.AddDays(d);
                            if (day < start) continue;
                            DateTime ws = day + tod, we = day + end;
                            if (j.IntervalEndMinutes == j.TimeOfDayMinutes) we = ws.AddDays(1).AddTicks(-1);
                            if (we <= from) continue;
                            DateTime t = ws;
                            if (t <= from)
                            {
                                long steps = (long)Math.Floor((from - ws).TotalMinutes / iv) + 1;
                                t = ws.AddMinutes(steps * iv);
                            }
                            if (t <= we) return t;
                        }
                        return null;
                    }
            }
        }
    }

    public static class Logger
    {
        static readonly object Sync = new object();
        public static event Action<LogEntry> EntryAdded;

        static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n"); }
        static string Unesc(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char c = s[++i];
                    sb.Append(c == 't' ? '\t' : c == 'n' ? '\n' : c);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>目前執行中的作業 (由 Activity.Begin 設定)；同一工作寫入的日誌自動帶上同一個 RunId。</summary>
        public static volatile string CurrentRunId, CurrentRunJob;

        public static void Write(LogEntry e)
        {
            if (e.Time == default(DateTime)) e.Time = DateTime.Now;
            if (string.IsNullOrEmpty(e.RunId) && CurrentRunId != null && e.Job == CurrentRunJob) e.RunId = CurrentRunId;
            string line = string.Join("\t", new[] {
                e.Time.ToString("yyyy-MM-dd HH:mm:ss"), Esc(e.Job), Esc(e.Database), Esc(e.Kind), Esc(e.Status),
                Esc(e.File), e.SizeBytes.ToString(CultureInfo.InvariantCulture),
                e.DurationSec.ToString("0.0", CultureInfo.InvariantCulture), Esc(e.Message), Esc(e.RunId) });
            lock (Sync)
            {
                try { File.AppendAllText(Path.Combine(AppPaths.LogDir, "backup_" + e.Time.ToString("yyyyMM") + ".log"), line + Environment.NewLine, Encoding.UTF8); }
                catch { }
            }
            var h = EntryAdded;
            if (h != null) try { h(e); } catch { }
        }

        public static List<LogEntry> Read(DateTime from, DateTime to)
        {
            var list = new List<LogEntry>();
            lock (Sync)
            {
                for (var m = new DateTime(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
                {
                    string f = Path.Combine(AppPaths.LogDir, "backup_" + m.ToString("yyyyMM") + ".log");
                    if (!File.Exists(f)) continue;
                    foreach (var line in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        var p = line.Split('\t');
                        if (p.Length < 9) continue;
                        DateTime t;
                        if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                        if (t < from || t > to) continue;
                        long size; double dur;
                        long.TryParse(p[6], NumberStyles.Any, CultureInfo.InvariantCulture, out size);
                        double.TryParse(p[7], NumberStyles.Any, CultureInfo.InvariantCulture, out dur);
                        list.Add(new LogEntry { Time = t, Job = Unesc(p[1]), Database = Unesc(p[2]), Kind = Unesc(p[3]), Status = Unesc(p[4]), File = Unesc(p[5]), SizeBytes = size, DurationSec = dur, Message = Unesc(p[8]), RunId = p.Length > 9 ? Unesc(p[9]) : "" });
                    }
                }
            }
            return list.OrderByDescending(x => x.Time).ToList();
        }

        /// <summary>刪除所有日誌檔，回傳刪除的檔案數。</summary>
        public static int ClearAll()
        {
            int n = 0;
            lock (Sync)
            {
                foreach (var f in Directory.GetFiles(AppPaths.LogDir, "backup_*.log"))
                    try { File.Delete(f); n++; } catch { }
            }
            var h = EntryAdded;
            if (h != null) try { h(new LogEntry { Time = DateTime.Now, Status = LogEntry.Info }); } catch { }
            return n;
        }

        /// <summary>
        /// 把日誌整合成「一次執行一筆」：相同 RunId 合併；舊版沒有 RunId 的，
        /// 同一工作、前後間隔 2 分鐘內的連續紀錄視為同一次。回傳新到舊排序，每組內依時間先後。
        /// </summary>
        public static List<LogGroup> Group(List<LogEntry> entries)
        {
            var groups = new List<LogGroup>();
            var byId = new Dictionary<string, LogGroup>();
            LogGroup legacy = null;
            foreach (var e in entries.OrderBy(x => x.Time))
            {
                LogGroup g;
                if (!string.IsNullOrEmpty(e.RunId))
                {
                    if (!byId.TryGetValue(e.RunId, out g)) { g = new LogGroup { Id = e.RunId, Job = e.Job }; byId[e.RunId] = g; groups.Add(g); }
                }
                else
                {
                    if (legacy == null || legacy.Job != e.Job || (e.Time - legacy.Entries.Last().Time).TotalSeconds > 120)
                    {
                        legacy = new LogGroup { Id = "L" + groups.Count, Job = e.Job };
                        groups.Add(legacy);
                    }
                    g = legacy;
                }
                g.Entries.Add(e);
            }
            foreach (var g in groups)
            {
                var es = g.Entries;
                g.Start = es.First().Time;
                g.End = es.Last().Time;
                g.Kind = es.Select(x => x.Kind).FirstOrDefault(k => !string.IsNullOrEmpty(k) && k != "清理") ?? "";
                g.Status = es.Any(x => x.Status == LogEntry.Failed) ? LogEntry.Failed
                         : es.Any(x => x.Status == LogEntry.Warning) ? LogEntry.Warning
                         : es.Any(x => x.Status == LogEntry.Success) ? LogEntry.Success : LogEntry.Info;
                g.Databases = es.Where(x => !string.IsNullOrEmpty(x.Database) && x.Database != "(伺服器)").Select(x => x.Database).Distinct().ToList();
                g.SizeBytes = es.Where(x => x.Status == LogEntry.Success && x.Kind != "清理").Sum(x => x.SizeBytes);
                g.DurationSec = Math.Max(es.Where(x => x.Status != LogEntry.Info).Sum(x => x.DurationSec), (g.End - g.Start).TotalSeconds);
                int ok = es.Count(x => x.Status == LogEntry.Success), fail = es.Count(x => x.Status == LogEntry.Failed), warn = es.Count(x => x.Status == LogEntry.Warning);
                var firstFail = es.FirstOrDefault(x => x.Status == LogEntry.Failed);
                if (firstFail != null) g.Summary = "失敗 " + fail + "：" + (string.IsNullOrEmpty(firstFail.Database) ? "" : firstFail.Database + " ") + firstFail.Message.Split('\n')[0];
                else if (ok > 0) g.Summary = "成功 " + ok + (warn > 0 ? "、警告 " + warn : "") + "：" + string.Join("、", es.Where(x => x.Status == LogEntry.Success && !string.IsNullOrEmpty(x.Database)).Select(x => x.Database + (x.SizeBytes > 0 ? " " + Fmt.Size(x.SizeBytes) : "")).Distinct());
                else g.Summary = es.Last().Message.Split('\n')[0];
            }
            return groups.OrderByDescending(x => x.Start).ToList();
        }

        public static void Cleanup(int keepDays)
        {
            if (keepDays <= 0) return;
            DateTime cutoff = DateTime.Today.AddDays(-keepDays);
            foreach (var f in Directory.GetFiles(AppPaths.LogDir, "backup_*.log"))
            {
                DateTime m;
                string stamp = Path.GetFileNameWithoutExtension(f).Substring(7);
                if (DateTime.TryParseExact(stamp, "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out m) && m.AddMonths(1) < cutoff)
                    try { File.Delete(f); } catch { }
            }
        }
    }

    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "SqlBackupManager";

        public static void Apply(bool enable)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enable) k.SetValue(Name, "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --minimized");
                else if (k.GetValue(Name) != null) k.DeleteValue(Name, false);
            }
        }
    }

    public static class PathHelper
    {
        /// <summary>本機建議的備份資料夾：有 D 槽 (固定磁碟) 用 D:\SQLBackup，否則 C:\SQLBackup。</summary>
        public static string SuggestedBackupDir()
        {
            try
            {
                var d = new DriveInfo("D");
                if (d.IsReady && d.DriveType == DriveType.Fixed) return @"D:\SQLBackup";
            }
            catch { }
            return @"C:\SQLBackup";
        }

        /// <summary>路徑是否可用：非空白，且 (網路/網址路徑，或本機磁碟機存在)。</summary>
        public static bool LooksUsable(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return false;
            p = p.Trim();
            if (p.StartsWith(@"\\") || p.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                string root = Path.GetPathRoot(p);
                return !string.IsNullOrEmpty(root) && Directory.Exists(root);
            }
            catch { return false; }
        }
    }

    public static class Fmt
    {
        public static string Size(long b)
        {
            if (b <= 0) return "";
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = b; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.0") + " " + u[i];
        }
        public static string Duration(double s)
        {
            if (s <= 0) return "";
            var ts = TimeSpan.FromSeconds(s);
            return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.TotalMinutes >= 1 ? ts.ToString(@"m\:ss") : s.ToString("0.0") + " 秒";
        }
        public static string Time(DateTime? t) { return t.HasValue ? t.Value.ToString("yyyy/MM/dd HH:mm") : "—"; }
    }
}
