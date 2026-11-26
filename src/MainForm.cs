using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlBackupManager
{
    public class MainForm : Form
    {
        readonly List<Page> pages = new List<Page>();
        readonly List<NavItem> navs = new List<NavItem>();
        Page current;
        readonly Label lblTitle = new Label { AutoSize = true, UseMnemonic = false };
        readonly Label lblConn = new Label { AutoSize = true, UseMnemonic = false };
        readonly Label lblSched = new Label { AutoSize = true, UseMnemonic = false };
        readonly ToolStripStatusLabel stText = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly ToolStripProgressBar stBar = new ToolStripProgressBar { Visible = false };
        readonly ToolStripStatusLabel stRight = new ToolStripStatusLabel();
        readonly NotifyIcon tray = new NotifyIcon();
        ToolStripMenuItem miPause;
        Scheduler scheduler;
        readonly Timer uiTimer = new Timer { Interval = 1500 };
        readonly Timer connTimer = new Timer { Interval = 10 * 60 * 1000 };
        volatile bool dirty;
        bool exiting, startHidden, balloonShown;
        readonly Icon iconOk, iconFail, iconIdle;
        Action<string, int> onProgress;
        Action<JobResult> onFinished;
        Action<LogEntry> onLog;

        /// <summary>各工作最近一次連線檢查結果 (工作 Id → 文字，✔ 開頭 = 正常)。</summary>
        public readonly ConcurrentDictionary<string, string> ConnStatus = new ConcurrentDictionary<string, string>();

        /// <summary>變更字體後由 Program 重新建立主視窗。</summary>
        public bool RebuildRequested;

        public MainForm(bool startMinimized)
        {
            startHidden = startMinimized || SettingsStore.Current.General.StartMinimized;
            Text = "SQL 備份管理";
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.F(9.5f);
            BackColor = Theme.Page;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(wa.Width, Theme.S(1320)), Math.Min(wa.Height, Theme.S(840)));
            MinimumSize = new Size(Math.Min(wa.Width, Theme.S(1000)), Math.Min(wa.Height, Theme.S(640)));
            StartPosition = FormStartPosition.CenterScreen;
            iconIdle = Theme.MakeIcon(Color.Empty);
            iconOk = Theme.MakeIcon(Color.FromArgb(46, 160, 67));
            iconFail = Theme.MakeIcon(Color.FromArgb(218, 54, 51));
            Icon = iconIdle;
            Theme.DoubleBuffer(this);

            // ── 側邊欄
            var side = new Panel { Dock = DockStyle.Left, Width = Theme.S(228), BackColor = Theme.Sidebar };
            var fLogo = Theme.F(13f, FontStyle.Bold); var fSub = Theme.F(8.5f);
            int hLogo = TextRenderer.MeasureText("國", fLogo).Height, hSub = TextRenderer.MeasureText("A", fSub).Height;
            var logo = new Panel { Dock = DockStyle.Top, Height = hLogo + hSub + Theme.S(46), BackColor = Theme.Sidebar };
            logo.Paint += (s, e) =>
            {
                int iy = (logo.Height - Theme.S(34)) / 2;
                e.Graphics.DrawIcon(iconIdle, new Rectangle(Theme.S(20), iy, Theme.S(34), Theme.S(34)));
                int tx = Theme.S(64), ty = (logo.Height - hLogo - hSub) / 2;
                TextRenderer.DrawText(e.Graphics, "SQL 備份管理", fLogo, new Point(tx, ty), Color.White, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, "Backup & Restore Manager", fSub, new Point(tx + Theme.S(2), ty + hLogo), Theme.SidebarText, TextFormatFlags.NoPrefix);
            };
            var navHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Padding = new Padding(0, Theme.S(8), 0, 0) };
            var verFont = Theme.F(8.5f);
            var ver = new Label { Dock = DockStyle.Bottom, Height = TextRenderer.MeasureText("A\nA", verFont).Height + Theme.S(20), ForeColor = Color.FromArgb(120, 132, 150), Font = verFont, Padding = new Padding(Theme.S(22), 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
                Text = "v" + Application.ProductVersion + "\n" + Environment.MachineName };

            pages.Add(new DashboardPage(this));
            pages.Add(new JobsPage(this));
            pages.Add(new RestorePage(this));
            pages.Add(new LogPage(this));
            pages.Add(new NotifyPage(this));
            pages.Add(new SystemPage(this));
            string[] glyphs = { "", "", "", "", "", "" };
            string[] names = { "總覽", "備份工作", "還原資料庫", "備份日誌", "通知設定", "系統設定" };
            for (int i = names.Length - 1; i >= 0; i--)
            {
                var n = new NavItem(names[i], glyphs[i]);
                int idx = i;
                n.Click += (s, e) => ShowPage(idx);
                navHost.Controls.Add(n);
                navs.Insert(0, n);
            }
            side.Controls.Add(navHost); side.Controls.Add(ver); side.Controls.Add(logo);

            // ── 標題列 (左標題、右狀態，用 TableLayout 自動排版不會互蓋)
            lblTitle.Font = Theme.F(16f, FontStyle.Bold); lblTitle.ForeColor = Theme.Text;
            lblConn.Font = lblSched.Font = Theme.F(9.5f);
            lblSched.Cursor = Cursors.Hand; lblSched.Click += (s, e) => TogglePause();
            lblConn.Cursor = Cursors.Hand; lblConn.Click += (s, e) => ShowPage(0);
            var header = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 1, BackColor = Color.White, Padding = new Padding(Theme.S(24), 0, Theme.S(20), 0) };
            header.Height = TextRenderer.MeasureText("國", lblTitle.Font).Height + Theme.S(30);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            lblTitle.Anchor = AnchorStyles.Left; lblSched.Anchor = AnchorStyles.Right; lblConn.Anchor = AnchorStyles.Right;
            lblSched.Margin = new Padding(Theme.S(12), 0, Theme.S(18), 0);
            header.Controls.Add(lblTitle, 0, 0); header.Controls.Add(lblSched, 1, 0); header.Controls.Add(lblConn, 2, 0);
            var headerLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };

            var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Page };
            foreach (var p in pages) content.Controls.Add(p);
            var right = new Panel { Dock = DockStyle.Fill };
            var banner = new ActivityBanner();
            banner.OpenDetails += ShowActivity;
            right.Controls.Add(content); right.Controls.Add(banner); right.Controls.Add(headerLine); right.Controls.Add(header);

            var status = new StatusStrip { BackColor = Color.White, SizingGrip = true, Font = Theme.F(9f) };
            stBar.Size = new Size(Theme.S(180), Theme.S(16));
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
            menu.Items.Add("執行明細", null, (s, e) => { ShowFromTray(); ShowActivity(); });
            menu.Items.Add("備份日誌", null, (s, e) => { ShowFromTray(); ShowPage(3); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("結束程式", null, (s, e) => ExitApp());
            tray.ContextMenuStrip = menu;
            tray.Icon = iconIdle;
            tray.Text = "SQL 備份管理";
            tray.Visible = true;
            tray.DoubleClick += (s, e) => ShowFromTray();
            tray.BalloonTipClicked += (s, e) => { ShowFromTray(); ShowPage(3); };

            // ── 事件 (重建視窗時要解除，避免重複)
            onProgress = (msg, pct) => SetStatus(msg, pct);
            onFinished = r => Invoke2(() => OnJobFinished(r));
            onLog = e => dirty = true;
            BackupEngine.Progress += onProgress;
            BackupEngine.JobFinished += onFinished;
            Logger.EntryAdded += onLog;
            uiTimer.Tick += (s, e) => { if (dirty && current != null && Visible) { dirty = false; current.RefreshData(); } };
            uiTimer.Start();
            connTimer.Tick += (s, e) => CheckConnections();
            connTimer.Start();

            scheduler = new Scheduler();
            scheduler.Changed += () => dirty = true;

            ShowPage(0);
            UpdateSchedLabel();
            CheckConnections();
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

        ActivityForm activityForm;

        /// <summary>開啟「執行明細」(同時只開一個)。</summary>
        public void ShowActivity()
        {
            if (activityForm == null || activityForm.IsDisposed) { activityForm = new ActivityForm(); activityForm.Show(this); }
            else { activityForm.WindowState = FormWindowState.Normal; activityForm.Activate(); }
        }

        public Page CurrentPage { get { return current; } }

        public void ShowPage(int idx)
        {
            for (int i = 0; i < pages.Count; i++) { navs[i].Selected = i == idx; navs[i].Invalidate(); }
            current = pages[idx];
            current.Visible = true;
            current.BringToFront();
            current.RefreshData();
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

        /// <summary>在背景檢查每個工作的連線 (相同伺服器只測一次)。</summary>
        public void CheckConnections()
        {
            var jobs = SettingsStore.Current.Jobs.ToList();
            UpdateConnLabel();
            if (jobs.Count == 0) return;
            foreach (var j in jobs) if (j.ConnVerified) ConnStatus[j.Id] = "檢查中…";
            dirty = true;
            Task.Run(() =>
            {
                var cache = new Dictionary<string, string>();
                foreach (var j in jobs.Where(x => x.ConnVerified))
                {
                    string st;
                    if (!cache.TryGetValue(j.ConnKey, out st))
                    {
                        try
                        {
                            List<DbInfo> dbs;
                            st = "✔ " + ConnTest.Test(j.Engine, j.SqlConn, j.ToolConn, out dbs).Split('｜')[0] + "（" + DateTime.Now.ToString("HH:mm") + "）";
                        }
                        catch (Exception ex) { st = "✖ " + ex.Message.Split('\n')[0]; }
                        cache[j.ConnKey] = st;
                    }
                    ConnStatus[j.Id] = st;
                }
                dirty = true;
                Invoke2(UpdateConnLabel);
            });
        }

        void UpdateConnLabel()
        {
            var jobs = SettingsStore.Current.Jobs;
            int total = jobs.Count, unverified = jobs.Count(j => !j.ConnVerified);
            int fail = jobs.Count(j => { string s; return j.ConnVerified && ConnStatus.TryGetValue(j.Id, out s) && s.StartsWith("✖"); });
            if (total == 0) { lblConn.ForeColor = Theme.SubText; lblConn.Text = "● 尚未建立備份工作"; }
            else if (fail > 0) { lblConn.ForeColor = Theme.Danger; lblConn.Text = "● " + fail + " 個工作無法連線"; }
            else if (unverified > 0) { lblConn.ForeColor = Theme.Warning; lblConn.Text = "● " + unverified + " 個工作連線未驗證"; }
            else { lblConn.ForeColor = Theme.Success; lblConn.Text = "● 全部 " + total + " 個工作連線正常"; }
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
            var bad = jobs.Where(j => !j.ConnVerified).ToList();
            if (bad.Count > 0)
                MessageBox.Show("以下工作的連線尚未驗證，不會執行：\n\n" + string.Join("\n", bad.Select(j => "• " + j.Name)) + "\n\n請編輯工作並「測試連線」成功後再儲存。", "備份", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            jobs = jobs.Where(j => j.ConnVerified).ToList();
            if (jobs.Count == 0) return;
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

        /// <summary>變更字體後重新建立視窗 (排程不中斷太久，設定已存檔)。</summary>
        public void RebuildUi()
        {
            if (BackupEngine.Busy)
            {
                MessageBox.Show("目前有備份/還原正在進行，字體會在下次開啟程式時套用。", "字體大小", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RebuildRequested = true;
            exiting = true;
            Close();
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
            tray.Dispose();
            uiTimer.Stop(); connTimer.Stop();
            scheduler.Dispose();
            BackupEngine.Progress -= onProgress;
            BackupEngine.JobFinished -= onFinished;
            Logger.EntryAdded -= onLog;
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
