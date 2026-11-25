using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SqlBackupManager
{
    public class ServerInfo
    {
        public string ServerName = "", Edition = "", Version = "", ProductLevel = "";
        public int EngineEdition;
        public string ServiceAccount = "";
        public string DefaultDataPath = "", DefaultLogPath = "", DefaultBackupPath = "";
        public bool IsExpress { get { return EngineEdition == 4; } }
        public bool SupportsCompression { get { return EngineEdition == 2 || EngineEdition == 3 || EngineEdition == 8; } }
        public string Summary { get { return Edition + "  " + Version + " " + ProductLevel; } }
    }

    public class DbInfo
    {
        public string Name;
        public int Id;
        public string State, RecoveryModel;
        public double SizeMB;
        public bool IsSystem { get { return Id <= 4; } }
    }

    public class BackupHeader
    {
        public string File;
        public int BackupType;            // 1=完整 2=記錄 5=差異
        public string DatabaseName;
        public DateTime StartDate, FinishDate;
        public decimal FirstLsn, LastLsn, DatabaseBackupLsn, CheckpointLsn;
        public long Size;
        public string TypeText { get { return BackupType == 1 ? "完整" : BackupType == 5 ? "差異" : BackupType == 2 ? "記錄" : "其他(" + BackupType + ")"; } }
    }

    public class DbFile
    {
        public string LogicalName, PhysicalName, Type; // D = 資料, L = 記錄, S = FILESTREAM
    }

    public static class SqlOps
    {
        public static string BuildConnString(ConnectionSettings c, string database = "master")
        {
            var b = new SqlConnectionStringBuilder
            {
                DataSource = c.Server,
                InitialCatalog = database,
                ConnectTimeout = Math.Max(5, c.ConnectTimeoutSec),
                ApplicationName = "SqlBackupManager",
                Pooling = false
            };
            if (c.UseWindowsAuth) b.IntegratedSecurity = true;
            else { b.UserID = c.User; b.Password = c.Password; }
            return b.ConnectionString;
        }

        public static SqlConnection Open(ConnectionSettings c)
        {
            var cn = new SqlConnection(BuildConnString(c));
            cn.Open();
            return cn;
        }

        public static string Q(string name) { return "[" + name.Replace("]", "]]") + "]"; }
        public static string S(string text) { return "N'" + text.Replace("'", "''") + "'"; }

        public static ServerInfo GetServerInfo(ConnectionSettings c)
        {
            using (var cn = Open(c))
            {
                var si = new ServerInfo();
                using (var cmd = new SqlCommand(@"
SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(256)), CAST(SERVERPROPERTY('Edition') AS nvarchar(256)),
       CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)), CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(64)),
       CAST(SERVERPROPERTY('EngineEdition') AS int),
       CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)), CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(512))", cn))
                using (var r = cmd.ExecuteReader())
                {
                    r.Read();
                    si.ServerName = r.IsDBNull(0) ? "" : r.GetString(0);
                    si.Edition = r.IsDBNull(1) ? "" : r.GetString(1);
                    si.Version = r.IsDBNull(2) ? "" : r.GetString(2);
                    si.ProductLevel = r.IsDBNull(3) ? "" : r.GetString(3);
                    si.EngineEdition = r.IsDBNull(4) ? 0 : r.GetInt32(4);
                    si.DefaultDataPath = r.IsDBNull(5) ? "" : r.GetString(5);
                    si.DefaultLogPath = r.IsDBNull(6) ? "" : r.GetString(6);
                }
                // 舊版 (2008/2008R2) 沒有 InstanceDefaultDataPath：改用 master 的檔案位置
                if (string.IsNullOrEmpty(si.DefaultDataPath))
                {
                    using (var cmd = new SqlCommand("SELECT physical_name FROM sys.master_files WHERE database_id=1 AND file_id=1", cn))
                    {
                        string p = Convert.ToString(cmd.ExecuteScalar());
                        si.DefaultDataPath = System.IO.Path.GetDirectoryName(p);
                    }
                }
                if (string.IsNullOrEmpty(si.DefaultLogPath)) si.DefaultLogPath = si.DefaultDataPath;
                try
                {
                    using (var cmd = new SqlCommand("SELECT TOP 1 service_account FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server (%'", cn))
                        si.ServiceAccount = Convert.ToString(cmd.ExecuteScalar());
                }
                catch { si.ServiceAccount = "(需 VIEW SERVER STATE 權限)"; }
                try
                {
                    using (var cmd = new SqlCommand(@"DECLARE @p nvarchar(4000);
EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'BackupDirectory', @p OUTPUT;
SELECT @p;", cn))
                        si.DefaultBackupPath = Convert.ToString(cmd.ExecuteScalar());
                }
                catch { }
                // 讀不到登錄檔 (權限不足) 時的備援：2019+ 的 SERVERPROPERTY，或資料檔旁邊的 Backup 資料夾
                if (string.IsNullOrWhiteSpace(si.DefaultBackupPath))
                {
                    try
                    {
                        using (var cmd = new SqlCommand("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(512))", cn))
                            si.DefaultBackupPath = Convert.ToString(cmd.ExecuteScalar());
                    }
                    catch { }
                }
                if (string.IsNullOrWhiteSpace(si.DefaultBackupPath) && !string.IsNullOrWhiteSpace(si.DefaultDataPath))
                {
                    try
                    {
                        string parent = System.IO.Path.GetDirectoryName(si.DefaultDataPath.TrimEnd('\\'));
                        si.DefaultBackupPath = string.IsNullOrEmpty(parent) ? si.DefaultDataPath : System.IO.Path.Combine(parent, "Backup");
                    }
                    catch { si.DefaultBackupPath = si.DefaultDataPath; }
                }
                si.DefaultBackupPath = (si.DefaultBackupPath ?? "").Trim().TrimEnd('\\');
                return si;
            }
        }

        public static List<DbInfo> GetDatabases(ConnectionSettings c)
        {
            var list = new List<DbInfo>();
            using (var cn = Open(c))
            using (var cmd = new SqlCommand(@"
SELECT d.name, d.database_id, d.state_desc, d.recovery_model_desc,
       CAST(ISNULL((SELECT SUM(CAST(size AS bigint)) FROM sys.master_files f WHERE f.database_id = d.database_id),0) * 8 / 1024.0 AS float)
FROM sys.databases d
WHERE d.name <> 'tempdb' AND d.source_database_id IS NULL
ORDER BY CASE WHEN d.database_id <= 4 THEN 0 ELSE 1 END, d.name", cn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    list.Add(new DbInfo { Name = r.GetString(0), Id = r.GetInt32(1), State = r.GetString(2), RecoveryModel = r.GetString(3), SizeMB = r.GetDouble(4) });
            }
            return list;
        }

        /// <summary>從登錄檔找出本機安裝的 SQL Server 執行個體。</summary>
        public static List<string> DetectLocalInstances()
        {
            var list = new List<string>();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var k = hk.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"))
                    {
                        if (k == null) continue;
                        foreach (var n in k.GetValueNames())
                        {
                            string s = n.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ? "." : @".\" + n;
                            if (!list.Contains(s)) list.Add(s);
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        /// <summary>執行長時間指令並回報 STATS 進度 (百分比)。</summary>
        public static void ExecWithProgress(ConnectionSettings c, string sql, Action<int> progress)
        {
            using (var cn = new SqlConnection(BuildConnString(c)))
            {
                var rx = new Regex(@"^(\d{1,3}) percent", RegexOptions.IgnoreCase);
                cn.InfoMessage += (s, e) =>
                {
                    foreach (SqlError err in e.Errors)
                    {
                        var m = rx.Match(err.Message.Trim());
                        if (m.Success && progress != null) progress(int.Parse(m.Groups[1].Value));
                    }
                };
                cn.Open();
                using (var cmd = new SqlCommand(sql, cn) { CommandTimeout = 0 })
                    cmd.ExecuteNonQuery();
            }
        }

        public static object Scalar(ConnectionSettings c, string sql, params SqlParameter[] ps)
        {
            using (var cn = Open(c))
            using (var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 })
            {
                cmd.Parameters.AddRange(ps);
                return cmd.ExecuteScalar();
            }
        }

        public static void Exec(ConnectionSettings c, string sql)
        {
            using (var cn = Open(c))
            using (var cmd = new SqlCommand(sql, cn) { CommandTimeout = 0 })
                cmd.ExecuteNonQuery();
        }

        public static List<BackupHeader> ReadHeaders(ConnectionSettings c, string file)
        {
            var list = new List<BackupHeader>();
            using (var cn = Open(c))
            using (var cmd = new SqlCommand("RESTORE HEADERONLY FROM DISK = " + S(file), cn) { CommandTimeout = 300 })
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var h = new BackupHeader { File = file };
                    h.BackupType = Convert.ToInt32(r["BackupType"]);
                    h.DatabaseName = Convert.ToString(r["DatabaseName"]);
                    h.StartDate = Convert.ToDateTime(r["BackupStartDate"]);
                    h.FinishDate = Convert.ToDateTime(r["BackupFinishDate"]);
                    h.FirstLsn = Convert.ToDecimal(r["FirstLSN"]);
                    h.LastLsn = Convert.ToDecimal(r["LastLSN"]);
                    h.CheckpointLsn = Convert.ToDecimal(r["CheckpointLSN"]);
                    h.DatabaseBackupLsn = r["DatabaseBackupLSN"] is DBNull ? 0 : Convert.ToDecimal(r["DatabaseBackupLSN"]);
                    h.Size = Convert.ToInt64(r["BackupSize"]);
                    list.Add(h);
                }
            }
            return list;
        }

        public static List<DbFile> ReadFileList(ConnectionSettings c, List<string> files)
        {
            var list = new List<DbFile>();
            using (var cn = Open(c))
            using (var cmd = new SqlCommand("RESTORE FILELISTONLY FROM " + string.Join(", ", files.ConvertAll(f => "DISK = " + S(f))), cn) { CommandTimeout = 300 })
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    list.Add(new DbFile { LogicalName = Convert.ToString(r["LogicalName"]), PhysicalName = Convert.ToString(r["PhysicalName"]), Type = Convert.ToString(r["Type"]) });
            }
            return list;
        }

        public static bool DatabaseExists(ConnectionSettings c, string db)
        {
            return Scalar(c, "SELECT DB_ID(@n)", new SqlParameter("@n", db)) != DBNull.Value;
        }
    }
}

namespace SqlBackupManager
{
    /// <summary>
    /// 讓 SQL Server 服務本身用指定帳號連到網路分享 (xp_cmdshell 'net use ...')，
    /// 之後 BACKUP / RESTORE 就能直接存取 \\NAS\...。
    /// 適用 SQL Server 在別台電腦的情況 (Microsoft 文件記載的做法)；需要 sysadmin。
    /// xp_cmdshell 原本關閉時，用完會自動關回去。
    /// </summary>
    public sealed class SqlNetUse : IDisposable
    {
        readonly ConnectionSettings conn;
        readonly string share;
        bool restoreAdv, restoreCmd, connected;
        public readonly List<string> Output = new List<string>();
        public string UsedUser;   // 實際連線成功的帳號寫法

        public SqlNetUse(ConnectionSettings conn, string uncPath, string user, string password)
        {
            this.conn = conn;
            share = NetShare.ShareRoot(NetPath.ToUnc(uncPath));
            if ((user ?? "").Contains("\"") || (password ?? "").Contains("\""))
                throw new Exception("網路帳號或密碼不能包含雙引號 (\")");
            EnableCmdShell();
            try
            {
                Run("net use \"" + share + "\" /delete /y");   // 先清掉舊連線 (避免 1219：同一台伺服器不同帳號)
                // 帳號只寫「knox」時，NAS / 別台電腦常要求「主機名稱\帳號」：失敗 (1312 / 1326) 就自動再試一次
                var candidates = new List<string> { user ?? "" };
                string shareHost = share.TrimStart('\\').Split('\\')[0];
                if (!string.IsNullOrWhiteSpace(user) && user.IndexOf('\\') < 0 && user.IndexOf('@') < 0) candidates.Add(shareHost + "\\" + user);
                string all = "";
                foreach (var u in candidates)
                {
                    var outp = Run(string.IsNullOrWhiteSpace(u)
                        ? "net use \"" + share + "\" /persistent:no"
                        : "net use \"" + share + "\" \"" + password + "\" /user:\"" + u + "\" /persistent:no");
                    all = string.Join(" ", outp).Trim();
                    if (outp.Any(l => l.IndexOf("successfully", StringComparison.OrdinalIgnoreCase) >= 0 || l.Contains("成功"))) { connected = true; UsedUser = u; break; }
                    Run("net use \"" + share + "\" /delete /y");
                }
                if (!connected)
                {
                    string hint = "";
                    if (all.Contains("1312"))
                    {
                        // 1312 最常見原因：SQL 主機開啟了「不允許存放網路驗證的密碼與認證」原則 (DisableDomainCreds)
                        bool policy = false;
                        try
                        {
                            using (var cn = SqlOps.Open(conn))
                            using (var c = new SqlCommand("DECLARE @v int; EXEC master.dbo.xp_regread N'HKEY_LOCAL_MACHINE', N'SYSTEM\\CurrentControlSet\\Control\\Lsa', N'disabledomaincreds', @v OUTPUT; SELECT ISNULL(@v, 0);", cn))
                                policy = Convert.ToInt32(c.ExecuteScalar()) == 1;
                        }
                        catch { }
                        string host = NetPath.HostOf(conn.Server);
                        hint = policy
                            ? "\n【原因】SQL 主機 " + host + " 開啟了安全性原則「網路存取: 不允許存放網路驗證的密碼與認證」。\n【處理】在 " + host + " 執行 secpol.msc → 本機原則 → 安全性選項，把這個原則改成「已停用」，重新開機後再驗證。"
                            : "\n【處理】1. 網路帳號改成「" + shareHost + "\\帳號」格式再驗證。\n2. 若仍失敗，在 SQL 主機 " + host + " 執行 secpol.msc → 本機原則 → 安全性選項，確認「網路存取: 不允許存放網路驗證的密碼與認證」為「已停用」，並重新開機。";
                    }
                    else if (all.Contains("1326")) hint = "\n【處理】帳號或密碼錯誤：請確認 NAS 帳號密碼，帳號可試試「" + shareHost + "\\帳號」格式。";
                    throw new Exception("SQL Server 無法連線到 " + share + "（已試帳號：" + string.Join("、", candidates) + "）：" + (all == "" ? "(沒有回應)" : all) + hint);
                }
            }
            catch { Dispose(); throw; }
        }

        void EnableCmdShell()
        {
            int adv = Convert.ToInt32(SqlOps.Scalar(conn, "SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'show advanced options'"));
            int cmd = Convert.ToInt32(SqlOps.Scalar(conn, "SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'xp_cmdshell'"));
            if (cmd == 1) return;
            try
            {
                if (adv == 0) { SqlOps.Exec(conn, "EXEC sp_configure 'show advanced options', 1; RECONFIGURE;"); restoreAdv = true; }
                SqlOps.Exec(conn, "EXEC sp_configure 'xp_cmdshell', 1; RECONFIGURE;"); restoreCmd = true;
            }
            catch (Exception ex) { throw new Exception("無法啟用 xp_cmdshell（需要 sysadmin 權限的 SQL 帳號）：" + ex.Message); }
        }

        List<string> Run(string cmd)
        {
            var lines = new List<string>();
            using (var cn = SqlOps.Open(conn))
            using (var c = new SqlCommand("EXEC master.dbo.xp_cmdshell " + SqlOps.S(cmd), cn) { CommandTimeout = 120 })
            using (var r = c.ExecuteReader())
                while (r.Read()) if (!r.IsDBNull(0)) lines.Add(r.GetString(0));
            Output.AddRange(lines);
            return lines;
        }

        public void Dispose()
        {
            if (connected) { try { Run("net use \"" + share + "\" /delete /y"); } catch { } connected = false; }
            try { if (restoreCmd) SqlOps.Exec(conn, "EXEC sp_configure 'xp_cmdshell', 0; RECONFIGURE;"); } catch { }
            try { if (restoreAdv) SqlOps.Exec(conn, "EXEC sp_configure 'show advanced options', 0; RECONFIGURE;"); } catch { }
            restoreCmd = restoreAdv = false;
        }

        /// <summary>驗證：SQL Server 連線到分享後，實際備份 model 資料庫到該處 (很小)，再刪除。</summary>
        public static void Test(ConnectionSettings conn, string path, string user, string password)
        {
            string unc = NetPath.ToUnc(path);
            using (new SqlNetUse(conn, unc, user, password))
            {
                try { SqlOps.Exec(conn, "EXEC master.dbo.xp_create_subdir " + SqlOps.S(unc)); } catch { }
                string f = System.IO.Path.Combine(unc, "_sbm_sqltest_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bak");
                try { SqlOps.Exec(conn, "BACKUP DATABASE [model] TO DISK = " + SqlOps.S(f) + " WITH COPY_ONLY, INIT"); }
                catch (Exception ex) { throw new Exception("SQL Server 已連上 " + unc + "，但無法寫入備份檔：" + ex.Message); }
                try { SqlOps.Exec(conn, "EXEC master.dbo.xp_cmdshell " + SqlOps.S("del /q \"" + f + "\"")); } catch { }
            }
        }
    }
}
