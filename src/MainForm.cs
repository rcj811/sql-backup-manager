using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlBackupManager
{
    public class MainForm : Form
    {
        readonly List<Page> pages = new List<Page>();
        readonly List<NavItem> navs = new List<NavItem>();
        Page current;
        Label lblTitle = new Label { AutoSize = true };
        Label lblConn = new Label { AutoSize = true };
        Label lblSched = new Label { AutoSize = true };
        ToolStripStatusLabel stText = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        ToolStripProgressBar stBar = new ToolStripProgressBar { Width = 180, Visible = false };
        ToolStripStatusLabel stRight = new ToolStripStatusLabel();
        NotifyIcon tray = new NotifyIcon();
        ToolStripMenuItem miPause;
        Scheduler scheduler;
        System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        volatile bool dirty;
        bool exiting, startHidden, balloonShown;
        Icon iconOk, iconFail, iconIdle;

        public ServerInfo Server;
        public string ServerError;
        public int DatabaseCount;

        public MainForm(bool startMinimized)
        {
            startHidden = startMinimized || SettingsStore.Current.General.StartMinimized;
            Text = "SQL 備份管理";
            Font = Theme.F(9.5f);
            BackColor = Theme.Page;
            Size = new Size(1320, 840);
            MinimumSize = new Size(1120, 720);
            StartPosition = FormStartPosition.CenterScreen;
            iconIdle = Theme.MakeIcon(Color.Empty);
            iconOk = Theme.MakeIcon(Color.FromArgb(46, 160, 67));
            iconFail = Theme.MakeIcon(Color.FromArgb(218, 54, 51));
            Icon = iconIdle;
            Theme.DoubleBuffer(this);

            // ── 側邊欄
            var side = new Panel { Dock = DockStyle.Left, Width = 228, BackColor = Theme.Sidebar };
            var logo = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Sidebar };
            logo.Paint += (s, e) =>
            {
                e.Graphics.DrawIcon(iconIdle, new Rectangle(22, 28, 34, 34));
                using (var f = Theme.F(13f, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, "SQL 備份管理", f, new Point(64, 26), Color.White);
                using (var f = Theme.F(8.5f)) TextRenderer.DrawText(e.Graphics, "Backup & Restore Manager", f, new Point(66, 52), Theme.SidebarText, TextFormatFlags.NoPrefix);
            };
            var navHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Padding = new Padding(0, 8, 0, 0) };
            var ver = new Label { Dock = DockStyle.Bottom, Height = 54, ForeColor = Color.FromArgb(120, 132, 150), Font = Theme.F(8.5f), Padding = new Padding(22, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft,
                Text = "v" + Application.ProductVersion + "\n" + Environment.MachineName };

            pages.Add(new DashboardPage(this));
            pages.Add(new JobsPage(this));
            pages.Add(new RestorePage(this));
            pages.Add(new LogPage(this));
            pages.Add(new ConnectionPage(this));
            pages.Add(new NotifyPage(this));
            pages.Add(new SystemPage(this));
            string[] glyphs = { "", "", "", "", "", "", "" };
            string[] names = { "總覽", "備份工作", "還原資料庫", "備份日誌", "連線設定", "通知設定", "系統設定" };
            for (int i = names.Length - 1; i >= 0; i--)
            {
                var n = new NavItem(names[i], glyphs[i]);
                int idx = i;
                n.Click += (s, e) => ShowPage(idx);
                navHost.Controls.Add(n);
                navs.Insert(0, n);
            }
            side.Controls.Add(navHost); side.Controls.Add(ver); side.Controls.Add(logo);

            // ── 標題列
            var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Color.White };
            lblTitle.Font = Theme.F(16f, FontStyle.Bold); lblTitle.ForeColor = Theme.Text; lblTitle.Location = new Point(24, 16);
            header.Controls.Add(lblTitle);
            var chips = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, Width = 760, Padding = new Padding(0, 22, 20, 0), BackColor = Color.White, WrapContents = false };
            lblConn.Font = lblSched.Font = Theme.F(9.5f);
            lblConn.Margin = new Padding(18, 0, 0, 0);
            lblSched.Cursor = Cursors.Hand;
            lblSched.Click += (s, e) => TogglePause();
            chips.Controls.Add(lblConn); chips.Controls.Add(lblSched);
            header.Controls.Add(chips);
            header.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });

            var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Page };
            foreach (var p in pages) content.Controls.Add(p);
            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(content); right.Controls.Add(header);

            var status = new StatusStrip { BackColor = Color.White, SizingGrip = true, Font = Theme.F(9f) };
            stRight.Text = "資料位置：" + AppPaths.DataDir;
            stRight.ForeColor = Theme.SubText;
            status.Items.AddRange(new ToolStripItem[] { stText, stBar, stRight });

            Controls.Add(right);
            Controls.Add(side);
            Controls.Add(status);

            // ── 系統匣
            var menu = new ContextMenuStrip { Font = Theme.F(9.5f) };
            menu.Items.Add("開啟主畫面", null, (s, e) => ShowFromTray()).Font = Theme.F(9.5f, FontStyle.Bold);
            menu.Items.Add("立即執行全部工作", null, (s, e) => RunJobs(SettingsStore.Current.Jobs.Where(j => j.Enabled).ToList()));
            miPause = new ToolStripMenuItem("暫停排程", null, (s, e) => TogglePause());
            menu.Items.Add(miPause);
            menu.Items.Add("備份日誌", null, (s, e) => { ShowFromTray(); ShowPage(3); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("結束程式", null, (s, e) => ExitApp());
            tray.ContextMenuStrip = menu;
            tray.Icon = iconIdle;
            tray.Text = "SQL 備份管理";
            tray.Visible = true;
            tray.DoubleClick += (s, e) => ShowFromTray();
            tray.BalloonTipClicked += (s, e) => { ShowFromTray(); ShowPage(3); };

            // ── 事件
            BackupEngine.Progress += (msg, pct) => SetStatus(msg, pct);
            BackupEngine.JobFinished += r => Invoke2(() => OnJobFinished(r));
            Logger.EntryAdded += e => dirty = true;
            uiTimer.Tick += (s, e) =>
            {
                if (dirty && current != null && Visible) { dirty = false; current.RefreshData(); }
            };
            uiTimer.Start();

            scheduler = new Scheduler();
            scheduler.Changed += () => dirty = true;

            ShowPage(0);
            UpdateSchedLabel();
            RefreshServerInfo();
            Logger.Cleanup(SettingsStore.Current.General.LogRetentionDays);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !IsHandleCreated) { CreateHandle(); value = false; ShowInTaskbar = false; }
            base.SetVisibleCore(value);
        }

        void Invoke2(Action a)
        {
            if (IsDisposed) return;
            try { if (InvokeRequired) BeginInvoke(a); else a(); } catch { }
        }

        public void ShowPage(int idx)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                navs[i].Selected = i == idx; navs[i].Invalidate();
            }
            current = pages[idx];
            current.RefreshData();
            current.Visible = true;
            current.BringToFront();
            foreach (var p in pages) if (p != current) p.Visible = false;
            lblTitle.Text = current.Title;
        }

        public void SetStatus(string msg, int pct)
        {
            Invoke2(() =>
            {
                stText.Text = msg;
                bool active = msg != "就緒" && (pct != 0 || BackupEngine.Busy);
                stBar.Visible = active;
                if (active)
                {
                    if (pct < 0) stBar.Style = ProgressBarStyle.Marquee;
                    else { stBar.Style = ProgressBarStyle.Continuous; stBar.Value = Math.Max(0, Math.Min(100, pct)); }
                }
                tray.Text = Trim63(BackupEngine.Busy ? "備份中：" + msg : "SQL 備份管理 - " + NextText());
            });
        }

        static string Trim63(string s) { return s.Length > 63 ? s.Substring(0, 60) + "…" : s; }

        string NextText()
        {
            var n = SettingsStore.Current.Jobs.Where(j => j.Enabled && j.NextRun.HasValue).OrderBy(j => j.NextRun).FirstOrDefault();
            return n == null ? "無排程" : "下次 " + n.NextRun.Value.ToString("MM/dd HH:mm");
        }

        public void RefreshServerInfo()
        {
            lblConn.ForeColor = Theme.SubText;
            lblConn.Text = "● 連線中…  " + SettingsStore.Current.Connection.Server;
            Task.Run(() =>
            {
                try
                {
                    var si = SqlOps.GetServerInfo(SettingsStore.Current.Connection);
                    int count = SqlOps.GetDatabases(SettingsStore.Current.Connection).Count;
                    Invoke2(() =>
                    {
                        Server = si; ServerError = null; DatabaseCount = count;
                        lblConn.ForeColor = Theme.Success;
                        lblConn.Text = "● " + SettingsStore.Current.Connection.Server + "　" + ShortEdition(si);
                        if (current != null) current.RefreshData();
                    });
                }
                catch (Exception ex)
                {
                    Invoke2(() =>
                    {
                        Server = null; ServerError = "無法連線：" + ex.Message;
                        lblConn.ForeColor = Theme.Danger;
                        lblConn.Text = "● SQL Server 未連線（" + SettingsStore.Current.Connection.Server + "）";
                        if (current != null) current.RefreshData();
                    });
                }
            });
        }

        static string ShortEdition(ServerInfo si)
        {
            string e = si.Edition.Split('(')[0].Trim();
            string v = si.Version.Split('.')[0];
            string year;
            switch (v) { case "10": year = "2008"; break; case "11": year = "2012"; break; case "12": year = "2014"; break; case "13": year = "2016"; break; case "14": year = "2017"; break; case "15": year = "2019"; break; case "16": year = "2022"; break; case "17": year = "2025"; break; default: year = v; break; }
            return "SQL Server " + year + " " + e;
        }

        void UpdateSchedLabel()
        {
            bool paused = SettingsStore.Current.General.SchedulerPaused;
            lblSched.ForeColor = paused ? Theme.Warning : Theme.Success;
            lblSched.Text = paused ? "⏸ 排程已暫停（點此恢復）" : "● 排程執行中";
            miPause.Text = paused ? "恢復排程" : "暫停排程";
        }

        void TogglePause()
        {
            var g = SettingsStore.Current.General;
            g.SchedulerPaused = !g.SchedulerPaused;
            if (!g.SchedulerPaused)
                foreach (var j in SettingsStore.Current.Jobs) Scheduler.Reschedule(j); // 恢復時不補做暫停期間的排程
            SettingsStore.Save();
            Logger.Write(new LogEntry { Status = LogEntry.Info, Message = g.SchedulerPaused ? "排程已暫停" : "排程已恢復" });
            UpdateSchedLabel();
            if (current != null) current.RefreshData();
        }

        public void RunJobs(List<BackupJob> jobs)
        {
            if (jobs.Count == 0) { MessageBox.Show("沒有可執行的工作。", "備份", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (BackupEngine.Busy) SetStatus("已加入佇列，等待目前作業完成…", -1);
            Task.Run(() =>
            {
                foreach (var j in jobs)
                {
                    try { BackupEngine.RunJob(j); }
                    catch (Exception ex) { Logger.Write(new LogEntry { Job = j.Name, Status = LogEntry.Failed, Message = ex.Message }); }
                }
            });
        }

        void OnJobFinished(JobResult r)
        {
            tray.Icon = r.Ok ? iconOk : iconFail;
            dirty = true;
            if (SettingsStore.Current.General.ShowBalloon || !r.Ok)
                Notify(r.Ok ? "備份完成" : "備份失敗", r.Job.Name + "：" + r.Summary, r.Ok ? ToolTipIcon.Info : ToolTipIcon.Error);
        }

        public void Notify(string title, string text, ToolTipIcon icon)
        {
            Invoke2(() => tray.ShowBalloonTip(5000, title, text, icon));
        }

        public void ShowFromTray()
        {
            startHidden = false;
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            if (current != null) current.RefreshData();
        }

        void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
            if (!balloonShown)
            {
                balloonShown = true;
                tray.ShowBalloonTip(3000, "SQL 備份管理", "程式在右下角持續執行，排程備份會照常進行。雙擊圖示可開啟主畫面。", ToolTipIcon.Info);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && Visible) HideToTray();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing && SettingsStore.Current.General.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            if (!exiting && e.CloseReason == CloseReason.UserClosing && !ConfirmExit()) { e.Cancel = true; return; }
            exiting = true;
            tray.Visible = false;
            scheduler.Dispose();
            base.OnFormClosing(e);
        }

        bool ConfirmExit()
        {
            string msg = BackupEngine.Busy ? "目前有備份/還原正在進行，結束程式會中斷作業。\n確定要結束嗎？" : "結束程式後，排程備份將不會執行。\n確定要結束嗎？";
            return MessageBox.Show(msg, "結束程式", MessageBoxButtons.YesNo, BackupEngine.Busy ? MessageBoxIcon.Warning : MessageBoxIcon.Question) == DialogResult.Yes;
        }

        void ExitApp()
        {
            if (!Visible) ShowFromTray();
            if (!ConfirmExit()) return;
            exiting = true;
            Close();
        }
    }
}
