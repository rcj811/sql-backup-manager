using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace SqlBackupManager
{
    /// <summary>執行外部命令列工具 (mysqldump、pg_dump、SqlPackage…)。</summary>
    public static class ToolRunner
    {
        /// <param name="stdoutFile">把標準輸出寫到這個檔案 (null = 收集成文字回傳)</param>
        /// <param name="gzipOut">輸出時以 gzip 壓縮</param>
        /// <param name="stdinFile">把這個檔案送進標準輸入 (.gz 會自動解壓)</param>
        public static string Run(string exe, string args, Dictionary<string, string> env, string stdoutFile = null, bool gzipOut = false,
                                 string stdinFile = null, Action<string> onLine = null)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdinFile != null,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            };
            if (stdoutFile == null) psi.StandardOutputEncoding = Encoding.UTF8;
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;

            var err = new StringBuilder();
            var outText = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) { lock (err) err.AppendLine(e.Data); if (onLine != null) onLine(e.Data); } };
                p.Start();
                p.BeginErrorReadLine();

                Thread feeder = null;
                if (stdinFile != null)
                {
                    feeder = new Thread(() =>
                    {
                        try
                        {
                            using (var fs = File.OpenRead(stdinFile))
                            using (Stream src = stdinFile.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? (Stream)new GZipStream(fs, CompressionMode.Decompress) : fs)
                                src.CopyTo(p.StandardInput.BaseStream, 1 << 20);
                        }
                        catch (IOException) { /* 程式提早結束 */ }
                        finally { try { p.StandardInput.Close(); } catch { } }
                    }) { IsBackground = true };
                    feeder.Start();
                }

                if (stdoutFile != null)
                {
                    using (var fs = File.Create(stdoutFile))
                    using (Stream dst = gzipOut ? (Stream)new GZipStream(fs, CompressionLevel.Optimal) : fs)
                        p.StandardOutput.BaseStream.CopyTo(dst, 1 << 20);
                }
                else
                {
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null) { outText.AppendLine(line); if (onLine != null) onLine(line); }
                }
                p.WaitForExit();
                if (feeder != null) feeder.Join(5000);
                if (p.ExitCode != 0)
                {
                    if (stdoutFile != null) try { File.Delete(stdoutFile); } catch { }
                    string msg = err.ToString().Trim();
                    if (msg == "") msg = outText.ToString().Trim();
                    throw new Exception(Path.GetFileName(exe) + " 失敗 (代碼 " + p.ExitCode + ")：" + Trunc(msg, 1500));
                }
            }
            return outText.ToString();
        }

        static string Trunc(string s, int n) { return s.Length <= n ? s : s.Substring(0, n) + "…"; }

        /// <summary>依 Windows 命令列規則加上引號 (處理反斜線與引號)。</summary>
        public static string Quote(string a)
        {
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in a ?? "")
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', bs); sb.Append(c); }
                bs = 0;
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>依設定的資料夾或常見安裝位置找出工具。</summary>
        public static string Find(string exeName, string configured, params string[] patterns)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (File.Exists(configured)) return configured;
                string p = Path.Combine(configured, exeName);
                if (File.Exists(p)) return p;
            }
            var pf = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
                .Where(x => !string.IsNullOrEmpty(x)).Distinct();
            foreach (var root in pf)
                foreach (var pat in patterns)
                {
                    // pat 例：PostgreSQL\*\bin
                    var parts = pat.Split('\\');
                    var dirs = new List<string> { root };
                    foreach (var part in parts)
                    {
                        var next = new List<string>();
                        foreach (var d in dirs)
                        {
                            try
                            {
                                if (part.Contains("*")) next.AddRange(Directory.GetDirectories(d, part).OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase));
                                else if (Directory.Exists(Path.Combine(d, part))) next.Add(Path.Combine(d, part));
                            }
                            catch { }
                        }
                        dirs = next;
                    }
                    foreach (var d in dirs) { string f = Path.Combine(d, exeName); if (File.Exists(f)) return f; }
                }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dt = Path.Combine(home, @".dotnet\tools", exeName);
            if (File.Exists(dt)) return dt;
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { string f = Path.Combine(d.Trim(), exeName); if (File.Exists(f)) return f; } catch { }
            }
            throw new FileNotFoundException("找不到 " + exeName + "，請在「連線設定」指定工具所在的資料夾。");
        }
    }

    public static class MySqlTool
    {
        public static string Exe(ToolConnection c, string name) { return ToolRunner.Find(name, c.BinDir, @"MySQL\MySQL Server *\bin", @"MariaDB *\bin", @"MySQL\MySQL Workbench *"); }

        /// <summary>密碼寫入暫存設定檔 (--defaults-extra-file)，避免出現在命令列。</summary>
        static string WithDefaults(ToolConnection c, Func<string, string> run)
        {
            string f = Path.Combine(Path.GetTempPath(), "sbm_" + Guid.NewGuid().ToString("N") + ".cnf");
            File.WriteAllText(f, "[client]\r\nuser=" + c.User + "\r\npassword=\"" + c.Password.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\r\nhost=" + c.Host + "\r\nport=" + c.Port + "\r\n", new UTF8Encoding(false));
            try { return run("--defaults-extra-file=" + ToolRunner.Quote(f)); }
            finally { try { File.Delete(f); } catch { } }
        }

        public static string Query(ToolConnection c, string sql)
        {
            return WithDefaults(c, d => ToolRunner.Run(Exe(c, "mysql.exe"), d + " -N -B -e " + ToolRunner.Quote(sql), null));
        }

        public static string Version(ToolConnection c) { return Query(c, "SELECT VERSION()").Trim(); }

        public static List<DbInfo> Databases(ToolConnection c)
        {
            var sys = new[] { "information_schema", "performance_schema", "mysql", "sys" };
            return Query(c, "SHOW DATABASES").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => new DbInfo { Name = n.Trim(), Id = sys.Contains(n.Trim()) ? 1 : 100, State = "ONLINE", RecoveryModel = "" }).ToList();
        }

        public static void Dump(ToolConnection c, string db, string file, BackupJob job, Action<string> onLine)
        {
            var a = new List<string> { "--default-character-set=utf8mb4", "--hex-blob", "--add-drop-table" };
            if (job.SingleTransaction) { a.Add("--single-transaction"); a.Add("--quick"); }
            if (job.DumpRoutines) { a.Add("--routines"); a.Add("--triggers"); a.Add("--events"); }
            a.Add(ToolRunner.Quote(db));
            WithDefaults(c, d => ToolRunner.Run(Exe(c, "mysqldump.exe"), d + " " + string.Join(" ", a), null, file, file.EndsWith(".gz"), null, onLine));
        }

        public static void Restore(ToolConnection c, string db, string file, bool dropFirst)
        {
            string q = "`" + db.Replace("`", "``") + "`";
            if (dropFirst) Query(c, "DROP DATABASE IF EXISTS " + q);
            Query(c, "CREATE DATABASE IF NOT EXISTS " + q + " CHARACTER SET utf8mb4");
            WithDefaults(c, d => ToolRunner.Run(Exe(c, "mysql.exe"), d + " --default-character-set=utf8mb4 " + ToolRunner.Quote(db), null, null, false, file));
        }

        public static bool Exists(ToolConnection c, string db) { return Query(c, "SHOW DATABASES").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Any(x => x.Trim() == db); }
    }

    public static class PgTool
    {
        public static string Exe(ToolConnection c, string name) { return ToolRunner.Find(name, c.BinDir, @"PostgreSQL\*\bin", @"pgAdmin 4\runtime", @"pgAdmin 4\*\runtime"); }

        static Dictionary<string, string> Env(ToolConnection c)
        {
            return new Dictionary<string, string> { { "PGPASSWORD", c.Password }, { "PGHOST", c.Host }, { "PGPORT", c.Port.ToString() }, { "PGUSER", c.User }, { "PGCLIENTENCODING", "UTF8" } };
        }

        public static string Query(ToolConnection c, string sql, string db = "postgres")
        {
            return ToolRunner.Run(Exe(c, "psql.exe"), "-X -A -t -v ON_ERROR_STOP=1 -d " + ToolRunner.Quote(db) + " -c " + ToolRunner.Quote(sql), Env(c));
        }

        public static string Version(ToolConnection c) { return Query(c, "SELECT version()").Trim(); }

        public static List<DbInfo> Databases(ToolConnection c)
        {
            return Query(c, "SELECT datname FROM pg_database WHERE NOT datistemplate AND datallowconn ORDER BY 1")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => new DbInfo { Name = n.Trim(), Id = n.Trim() == "postgres" ? 1 : 100, State = "ONLINE", RecoveryModel = "" }).ToList();
        }

        public static void Dump(ToolConnection c, string db, string file, BackupJob job, Action<string> onLine)
        {
            if (job.PgCustomFormat)
                ToolRunner.Run(Exe(c, "pg_dump.exe"), "-Fc -Z 6 --no-password -f " + ToolRunner.Quote(file) + " " + ToolRunner.Quote(db), Env(c), null, false, null, onLine);
            else
                ToolRunner.Run(Exe(c, "pg_dump.exe"), "-Fp --no-password --clean --if-exists " + ToolRunner.Quote(db), Env(c), file, file.EndsWith(".gz"), null, onLine);
        }

        public static void DumpGlobals(ToolConnection c, string file)
        {
            ToolRunner.Run(Exe(c, "pg_dumpall.exe"), "--globals-only --no-password", Env(c), file, file.EndsWith(".gz"));
        }

        public static bool Exists(ToolConnection c, string db)
        {
            return Query(c, "SELECT 1 FROM pg_database WHERE datname = '" + db.Replace("'", "''") + "'").Trim() == "1";
        }

        public static string VerifyCustom(ToolConnection c, string file)
        {
            return ToolRunner.Run(Exe(c, "pg_restore.exe"), "-l " + ToolRunner.Quote(file), null);
        }

        public static void Restore(ToolConnection c, string db, string file, bool dropFirst, bool kill)
        {
            string q = "\"" + db.Replace("\"", "\"\"") + "\"";
            bool globals = Path.GetFileName(file).IndexOf("_PGROLES_", StringComparison.OrdinalIgnoreCase) >= 0;
            if (globals)
            {
                ToolRunner.Run(Exe(c, "psql.exe"), "-X -d postgres", Env(c), null, false, file);
                return;
            }
            if (kill) Query(c, "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '" + db.Replace("'", "''") + "' AND pid <> pg_backend_pid()");
            if (dropFirst) Query(c, "DROP DATABASE IF EXISTS " + q);
            if (!Exists(c, db)) Query(c, "CREATE DATABASE " + q);
            if (file.EndsWith(".dump", StringComparison.OrdinalIgnoreCase))
                ToolRunner.Run(Exe(c, "pg_restore.exe"), "--no-password --no-owner --exit-on-error -d " + ToolRunner.Quote(db) + " " + ToolRunner.Quote(file), Env(c));
            else
                ToolRunner.Run(Exe(c, "psql.exe"), "-X -v ON_ERROR_STOP=1 -d " + ToolRunner.Quote(db), Env(c), null, false, file);
        }
    }

    public static class SqlPackageTool
    {
        public static string Exe(ConnectionSettings c)
        {
            return ToolRunner.Find("SqlPackage.exe", c.SqlPackagePath,
                @"Microsoft SQL Server\*\DAC\bin", @"Microsoft SQL Server Management Studio *\Common7\IDE\Extensions\Microsoft\SQLDB\DAC",
                @"Microsoft Visual Studio\*\*\Common7\IDE\Extensions\Microsoft\SQLDB\DAC", @"sqlpackage");
        }

        static string Conn(ConnectionSettings c, string db)
        {
            var b = new System.Data.SqlClient.SqlConnectionStringBuilder(SqlOps.BuildConnString(c, db));
            b.Remove("Application Name"); b.Remove("Pooling");
            return b.ConnectionString + ";TrustServerCertificate=True";
        }

        public static void Export(ConnectionSettings c, string db, string file, Action<string> onLine)
        {
            ToolRunner.Run(Exe(c), "/a:Export /scs:" + ToolRunner.Quote(Conn(c, db)) + " /tf:" + ToolRunner.Quote(file), null, null, false, null, onLine);
        }

        public static void Import(ConnectionSettings c, string db, string file, Action<string> onLine)
        {
            ToolRunner.Run(Exe(c), "/a:Import /tcs:" + ToolRunner.Quote(Conn(c, db)) + " /sf:" + ToolRunner.Quote(file), null, null, false, null, onLine);
        }
    }

    /// <summary>測試連線並回傳伺服器摘要；失敗會丟出例外。</summary>
    public static class ConnTest
    {
        public static string Test(DbEngine e, ConnectionSettings sql, ToolConnection tool, out List<DbInfo> dbs)
        {
            string dir;
            return Test(e, sql, tool, out dbs, out dir);
        }

        /// <summary>同上，另外回傳伺服器預設的備份資料夾 (SQL Server 才有，其他為 null)。</summary>
        public static string Test(DbEngine e, ConnectionSettings sql, ToolConnection tool, out List<DbInfo> dbs, out string defaultBackupDir)
        {
            defaultBackupDir = null;
            if (e == DbEngine.SqlServer)
            {
                var si = SqlOps.GetServerInfo(sql);
                defaultBackupDir = string.IsNullOrWhiteSpace(si.DefaultBackupPath) ? null : si.DefaultBackupPath.Trim();
                dbs = SqlOps.GetDatabases(sql);
                return si.Summary + "｜資料庫 " + dbs.Count + " 個｜服務帳戶 " + si.ServiceAccount + (si.IsExpress ? "｜Express 版不支援原生壓縮，可改用 ZIP" : "");
            }
            if (e == DbEngine.MySql)
            {
                string v = MySqlTool.Version(tool);
                dbs = MySqlTool.Databases(tool);
                return "MySQL " + v + "｜資料庫 " + dbs.Count + " 個｜工具 " + Path.GetDirectoryName(MySqlTool.Exe(tool, "mysqldump.exe"));
            }
            string pv = PgTool.Version(tool);
            dbs = PgTool.Databases(tool);
            return pv.Split(',')[0] + "｜資料庫 " + dbs.Count + " 個｜工具 " + Path.GetDirectoryName(PgTool.Exe(tool, "pg_dump.exe"));
        }
    }
}
