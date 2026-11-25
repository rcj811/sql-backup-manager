using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("SQL 備份管理")]
[assembly: AssemblyProduct("SQL Backup Manager")]
[assembly: AssemblyDescription("SQL Server / MySQL / PostgreSQL 排程備份與還原")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyVersion("1.5.1.0")]
[assembly: AssemblyFileVersion("1.5.1.0")]

namespace SqlBackupManager
{
    static class Program
    {
        const string MutexName = "SqlBackupManager_UI_7B1E";
        const string ShowEventName = "SqlBackupManager_Show_7B1E";

        [STAThread]
        static int Main(string[] args)
        {
            SettingsStore.Load();

            // 自動測試 (GitHub Actions)
            int ti = Array.FindIndex(args, x => x.Equals("--selftest", StringComparison.OrdinalIgnoreCase) || x.Equals("--selftest-ui", StringComparison.OrdinalIgnoreCase));
            if (ti >= 0)
            {
                string dir = ti + 1 < args.Length ? args[ti + 1] : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sbm_selftest");
                return args[ti].Equals("--selftest-ui", StringComparison.OrdinalIgnoreCase) ? SelfTest.RunUi(dir) : SelfTest.Run(dir);
            }

            // 命令列模式：給 Windows 工作排程器使用，不顯示畫面
            int ri = Array.FindIndex(args, a => a.Equals("--run", StringComparison.OrdinalIgnoreCase));
            bool runAll = args.Any(a => a.Equals("--run-all", StringComparison.OrdinalIgnoreCase));
            if (ri >= 0 || runAll)
            {
                var jobs = runAll ? SettingsStore.Current.Jobs.Where(j => j.Enabled).ToList()
                    : SettingsStore.Current.Jobs.Where(j => ri + 1 < args.Length && j.Name.Equals(args[ri + 1], StringComparison.OrdinalIgnoreCase)).ToList();
                if (jobs.Count == 0)
                {
                    Logger.Write(new LogEntry { Status = LogEntry.Failed, Message = "命令列：找不到工作「" + (ri + 1 < args.Length ? args[ri + 1] : "") + "」" });
                    return 2;
                }
                bool ok = true;
                foreach (var j in jobs)
                {
                    Logger.Write(new LogEntry { Job = j.Name, Status = LogEntry.Info, Message = "由命令列 / 工作排程器啟動" });
                    ok &= BackupEngine.RunJob(j).Ok;
                }
                return ok ? 0 : 1;
            }

            bool created;
            Mutex mutex;
            try { mutex = new Mutex(true, @"Global\" + MutexName, out created); }
            catch { mutex = new Mutex(true, @"Local\" + MutexName, out created); }
            if (!created)
            {
                // 已在執行：叫出現有視窗
                try { using (var ev = EventWaitHandle.OpenExisting(@"Local\" + ShowEventName)) ev.Set(); } catch { }
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception);

            bool minimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
            MainForm form = null;

            var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + ShowEventName);
            var t = new Thread(() =>
            {
                while (true)
                {
                    show.WaitOne();
                    var f = form;
                    try { if (f != null && !f.IsDisposed) f.BeginInvoke((Action)f.ShowFromTray); } catch { }
                }
            }) { IsBackground = true };
            t.Start();

            // 變更字體大小時會關閉並重新建立主視窗
            while (true)
            {
                Theme.Init(SettingsStore.Current.General.FontSize);
                form = new MainForm(minimized);
                Application.Run(form);
                if (!form.RebuildRequested) break;
                minimized = false;
            }
            GC.KeepAlive(mutex);
            return 0;
        }

        static void Report(Exception ex)
        {
            if (ex == null) return;
            Logger.Write(new LogEntry { Status = LogEntry.Failed, Message = "程式錯誤：" + ex });
            try { MessageBox.Show(ex.Message, "SQL 備份管理 - 錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
        }
    }
}
