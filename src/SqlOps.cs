using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
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
