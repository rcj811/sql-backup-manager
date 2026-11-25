using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace SqlBackupManager
{
    public enum DbEngine { SqlServer = 0, MySql = 1, PostgreSql = 2 }

    /// <summary>
    /// SQL Server：Full/Differential/Log/Partial/PartialDiff/Bacpac
    /// MySQL：Dump (mysqldump)
    /// PostgreSQL：Dump (pg_dump)、Globals (pg_dumpall --globals-only，角色與權限)
    /// </summary>
    public enum BackupKind { Full = 0, Differential = 1, Log = 2, Partial = 3, PartialDiff = 4, Bacpac = 5, Dump = 6, Globals = 7 }

    public class ToolConnection
    {
        public string Host = "localhost";
        public int Port;
        public string User = "";
        public string PasswordEnc = "";
        public string BinDir = "";   // 空白 = 自動尋找

        public ToolConnection Copy() { return (ToolConnection)MemberwiseClone(); }
        public string Key { get { return (Host + ":" + Port + ":" + User).ToLowerInvariant(); } }

        [XmlIgnore]
        public string Password
        {
            get { return Crypto.Unprotect(PasswordEnc); }
            set { PasswordEnc = Crypto.Protect(value); }
        }
    }

    public enum ScheduleKind { Once = 0, Daily = 1, Weekly = 2, Monthly = 3, Interval = 4 }

    public class ConnectionSettings
    {
        public string Server = @".\SQLEXPRESS";
        public bool UseWindowsAuth = true;
        public string User = "sa";
        public string PasswordEnc = "";
        public int ConnectTimeoutSec = 15;
        public string SqlPackagePath = "";   // BACPAC 用，空白 = 自動尋找

        // SQL Server 在別台電腦時，備份到網路路徑的「主機中轉」：
        // SQL 先寫到它自己電腦的 HostStageLocal，本程式再用 HostUser 經 HostStageUnc 取回，送到 NAS
        public string HostStageLocal = "";   // SQL 主機上的資料夾，例如 C:\SqlBackupStage
        public string HostStageUnc = "";     // 本程式存取它的網路路徑，例如 \\chin-sys\C$\SqlBackupStage
        public string HostUser = "";         // SQL 主機的 Windows 帳號 (需能寫入上面的分享)
        public int RemoteNetMode;            // SQL 在別台電腦時：0 = SQL Server 自己用 NAS 帳號連線 (xp_cmdshell net use)，1 = SQL 主機中轉
        public string HostPasswordEnc = "";

        [XmlIgnore]
        public string HostPassword
        {
            get { return Crypto.Unprotect(HostPasswordEnc); }
            set { HostPasswordEnc = Crypto.Protect(value); }
        }

        [XmlIgnore]
        public bool HasRelay { get { return !string.IsNullOrWhiteSpace(HostStageLocal) && !string.IsNullOrWhiteSpace(HostStageUnc); } }

        public ConnectionSettings Copy() { return (ConnectionSettings)MemberwiseClone(); }
        public string Key { get { return (Server + ":" + (UseWindowsAuth ? "win" : User)).ToLowerInvariant(); } }

        [XmlIgnore]
        public string Password
        {
            get { return Crypto.Unprotect(PasswordEnc); }
            set { PasswordEnc = Crypto.Protect(value); }
        }
    }

    public class MailSettings
    {
        public bool Enabled = false;
        public string Host = "";
        public int Port = 587;
        public bool EnableSsl = true;
        public string User = "";
        public string PasswordEnc = "";
        public string From = "";
        public string To = "";
        public bool NotifyOnSuccess = false;
        public bool NotifyOnFailure = true;

        [XmlIgnore]
        public string Password
        {
            get { return Crypto.Unprotect(PasswordEnc); }
            set { PasswordEnc = Crypto.Protect(value); }
        }
    }

    public class GeneralSettings
    {
        public bool StartWithWindows = false;
        public bool StartMinimized = false;
        public bool MinimizeToTrayOnClose = true;
        public bool RunMissedJobs = true;
        public bool ShowBalloon = true;
        public bool SchedulerPaused = false;
        public int LogRetentionDays = 365;
        public float FontSize = 10f;   // 介面字體大小 (pt)
    }

    public class BackupJob
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "新備份工作";
        public bool Enabled = true;
        public DbEngine Engine = DbEngine.SqlServer;

        // 連線 (每個工作各自設定)
        public ConnectionSettings SqlConn = new ConnectionSettings();
        public ToolConnection ToolConn = new ToolConnection { Port = 3306, User = "root" };
        public bool ConnSet;            // 舊版設定檔移轉用
        public bool ConnVerified;       // 連線已驗證成功
        public DateTime? ConnVerifiedAt;

        public string ServerText()
        {
            if (Engine == DbEngine.SqlServer) return SqlConn.Server;
            return ToolConn.Host + ":" + ToolConn.Port;
        }
        public string ConnKey { get { return Engine + "|" + (Engine == DbEngine.SqlServer ? SqlConn.Key : ToolConn.Key); } }

        // 資料庫
        public bool AllUserDatabases = true;
        public bool IncludeSystemDatabases = false;
        public List<string> Databases = new List<string>();

        // 備份選項
        public BackupKind Kind = BackupKind.Full;
        public string TargetPath = "";           // 由使用者指定
        public bool SubFolderPerDatabase = true;
        public bool Verify = true;
        public bool Checksum = true;
        public bool Compression = true;      // 原生壓縮 (Express 不支援時自動略過)
        public bool ZipAfterBackup = false;  // 備份後另外壓成 zip (Express 也可用)
        public bool CopyOnly = false;
        public bool FallbackToFull = true;   // 找不到完整備份時，差異/記錄備份改做完整備份
        public string SecondaryPath = "";    // 第二份副本 (例如網路磁碟 / NAS)
        public bool StageNetwork = true;     // 目標是網路路徑時：先備份到 SQL Server 本機，再由本程式複製過去
        public string NetUser = "";          // 網路路徑的帳號 (空白 = 目前登入的帳號)
        public string NetPasswordEnc = "";
        public bool NetVerified;             // 網路路徑已驗證可寫入

        [XmlIgnore]
        public string NetPassword
        {
            get { return Crypto.Unprotect(NetPasswordEnc); }
            set { NetPasswordEnc = Crypto.Protect(value); }
        }
        public int RetentionDays = 14;       // 0 = 永久保留

        // SQL Server 進階
        public int StripeCount = 1;          // 分割成幾個檔案平行寫入
        public string MirrorPath = "";       // 鏡像備份 (Enterprise / Developer)
        public string EncryptCertificate = ""; // 伺服器憑證名稱，空白 = 不加密 (Standard 以上)
        public string EncryptAlgorithm = "AES_256";

        // MySQL / PostgreSQL
        public bool SingleTransaction = true; // mysqldump --single-transaction (InnoDB 不鎖表)
        public bool DumpRoutines = true;      // 預存程序、函數、觸發程序、事件
        public bool PgCustomFormat = true;    // pg_dump -Fc；否則純 SQL
        public bool GzipDump = true;          // 純 SQL 輸出以 gzip 壓縮

        // 排程
        public ScheduleKind Schedule = ScheduleKind.Daily;
        public int TimeOfDayMinutes = 2 * 60;      // 02:00
        public DateTime StartDate = DateTime.Today;
        public int WeekDaysMask = 0x3E;            // 一~五
        public int MonthDay = 1;                   // 0 = 每月最後一天
        public int IntervalMinutes = 60;
        public int IntervalEndMinutes = 0;         // 間隔排程的結束時間 (等於開始時間 = 全天)

        // 執行狀態
        public DateTime? NextRun;
        public DateTime? LastRun;
        public string LastStatus = "";
        public string LastMessage = "";

        public BackupJob Clone()
        {
            var x = (BackupJob)MemberwiseClone();
            x.Databases = new List<string>(Databases);
            x.SqlConn = SqlConn.Copy();
            x.ToolConn = ToolConn.Copy();
            return x;
        }

        public static string KindText(BackupKind k)
        {
            switch (k)
            {
                case BackupKind.Full: return "完整備份";
                case BackupKind.Differential: return "差異備份";
                case BackupKind.Log: return "交易記錄備份";
                case BackupKind.Partial: return "部分備份";
                case BackupKind.PartialDiff: return "部分差異備份";
                case BackupKind.Bacpac: return "BACPAC 匯出";
                case BackupKind.Dump: return "邏輯備份 (Dump)";
                default: return "角色與權限";
            }
        }

        public static string EngineText(DbEngine e)
        {
            return e == DbEngine.SqlServer ? "SQL Server" : e == DbEngine.MySql ? "MySQL / MariaDB" : "PostgreSQL";
        }

        public static BackupKind[] KindsFor(DbEngine e)
        {
            if (e == DbEngine.SqlServer) return new[] { BackupKind.Full, BackupKind.Differential, BackupKind.Log, BackupKind.Partial, BackupKind.PartialDiff, BackupKind.Bacpac };
            if (e == DbEngine.MySql) return new[] { BackupKind.Dump };
            return new[] { BackupKind.Dump, BackupKind.Globals };
        }

        public string DatabaseText()
        {
            if (Kind == BackupKind.Globals) return "(整個伺服器)";
            if (AllUserDatabases) return IncludeSystemDatabases ? "全部資料庫 (含系統)" : "全部使用者資料庫";
            if (Databases.Count == 0) return "(未選擇)";
            return string.Join(", ", Databases);
        }

        public string ScheduleText()
        {
            string t = TimeSpan.FromMinutes(TimeOfDayMinutes).ToString(@"hh\:mm");
            switch (Schedule)
            {
                case ScheduleKind.Once: return "單次 " + StartDate.ToString("yyyy/MM/dd") + " " + t;
                case ScheduleKind.Daily: return "每天 " + t;
                case ScheduleKind.Weekly:
                    var names = new[] { "日", "一", "二", "三", "四", "五", "六" };
                    var list = new List<string>();
                    for (int i = 1; i <= 7; i++) { int d = i % 7; if ((WeekDaysMask & (1 << d)) != 0) list.Add(names[d]); }
                    return "每週 " + string.Join("、", list) + " " + t;
                case ScheduleKind.Monthly:
                    return "每月 " + (MonthDay == 0 ? "最後一天" : MonthDay + " 日") + " " + t;
                default:
                    string e = TimeSpan.FromMinutes(IntervalEndMinutes).ToString(@"hh\:mm");
                    string iv = IntervalMinutes % 60 == 0 ? (IntervalMinutes / 60) + " 小時" : IntervalMinutes + " 分鐘";
                    return "每隔 " + iv + (IntervalEndMinutes == TimeOfDayMinutes ? " (全天)" : " (" + t + "~" + e + ")");
            }
        }
    }

    public class AppSettings
    {
        public ConnectionSettings Connection = new ConnectionSettings();
        public ToolConnection MySql = new ToolConnection { Port = 3306, User = "root" };
        public ToolConnection PgSql = new ToolConnection { Port = 5432, User = "postgres" };
        public MailSettings Mail = new MailSettings();
        public GeneralSettings General = new GeneralSettings();
        public List<BackupJob> Jobs = new List<BackupJob>();
    }

    public class LogEntry
    {
        public DateTime Time;
        public string Job = "";
        public string Database = "";
        public string Kind = "";
        public string Status = "";   // 成功 / 失敗 / 警告 / 資訊
        public string File = "";
        public long SizeBytes;
        public double DurationSec;
        public string Message = "";

        public const string Success = "成功", Failed = "失敗", Warning = "警告", Info = "資訊";
    }
}
