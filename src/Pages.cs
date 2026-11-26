using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlBackupManager
{
    public abstract class Page : Panel
    {
        public string Title;
        protected MainForm Main;

        protected Page(MainForm main, string title)
        {
            Main = main; Title = title;
            Dock = DockStyle.Fill; BackColor = Theme.Page; Padding = new Padding(Theme.S(24), Theme.S(20), Theme.S(24), Theme.S(20)); Visible = false;
            Theme.DoubleBuffer(this);
        }

        public virtual void RefreshData() { }

        /// <summary>視窗太小時頁面可捲動，內容不會被擠扁或切掉。</summary>
        protected void MinContent(int w, int h)
        {
            AutoScroll = true;
            AutoScrollMinSize = new Size(w, h);
        }

        /// <summary>由上而下排列；最後一個元素填滿剩餘空間。</summary>
        protected void Stack(params Control[] items)
        {
            int last = items.Length - 1;
            items[last].Dock = DockStyle.Fill; Controls.Add(items[last]);
            for (int i = last - 1; i >= 0; i--)
            {
                Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(16), BackColor = Color.Transparent });
                items[i].Dock = DockStyle.Top;
                Controls.Add(items[i]);
            }
        }

        /// <summary>水平工具列：控制項依實際大小自動排列、垂直置中，不會重疊。</summary>
        protected static FlowLayoutPanel Toolbar(params Control[] items)
        {
            var f = new FlowLayoutPanel { WrapContents = true, BackColor = Color.Transparent, Padding = new Padding(0), Margin = new Padding(0), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            int h = items.Max(c => c.Height);
            foreach (var c in items)
            {
                if (c is Label) { c.Margin = new Padding(0, (h - c.PreferredSize.Height) / 2 + 1, Theme.S(6), Theme.S(6)); }
                else c.Margin = new Padding(0, (h - c.Height) / 2, Theme.S(10), Theme.S(6));
                f.Controls.Add(c);
            }
            return f;
        }

        protected void UI(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch { }
        }

        protected static Card GridCard(string title, DataGridView grid)
        {
            var c = new Card(title);
            Theme.StyleGrid(grid);
            grid.Dock = DockStyle.Fill;
            c.Body.Controls.Add(grid);
            return c;
        }

        protected static string Browse(string current)
        {
            using (var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(current) ? current : "", ShowNewFolderButton = true })
                return d.ShowDialog() == DialogResult.OK ? d.SelectedPath : null;
        }

        protected static void ColorStatus(DataGridView g, int col)
        {
            var bold = Theme.F(9.5f, FontStyle.Bold);
            g.CellFormatting += (s, e) =>
            {
                if (e.ColumnIndex != col || e.Value == null) return;
                e.CellStyle.ForeColor = Theme.StatusColor(e.Value.ToString());
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                e.CellStyle.Font = bold;
            };
        }

        protected static TextBox Box(int w) { return new TextBox { Font = Theme.F(9.5f), Width = Theme.S(w) }; }
        protected static ComboBox Combo(int w) { return new ComboBox { Font = Theme.F(9.5f), Width = Theme.S(w), DropDownStyle = ComboBoxStyle.DropDownList }; }
        protected static DateTimePicker Date(int w) { return new DateTimePicker { Font = Theme.F(9.5f), Width = Theme.S(w), Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd" }; }
    }

    // ───────────────────────── 總覽 ─────────────────────────
    public class DashboardPage : Page
    {
        readonly StatTile tJobs = new StatTile { Caption = "啟用中的備份工作" };
        readonly StatTile tNext = new StatTile { Caption = "下次備份" };
        readonly StatTile tOk = new StatTile { Caption = "近 24 小時成功", Stripe = Theme.Success };
        readonly StatTile tFail = new StatTile { Caption = "近 24 小時失敗 / 警告", Stripe = Theme.Danger };
        readonly DataGridView connGrid = new DataGridView();
        readonly Panel diskHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true };
        readonly DataGridView grid = new DataGridView();

        public DashboardPage(MainForm m) : base(m, "總覽")
        {
            var run = Theme.Btn("立即執行全部工作", true);
            run.Click += (s, e) => Main.RunJobs(SettingsStore.Current.Jobs.Where(j => j.Enabled).ToList());
            var check = Theme.Btn("重新檢查連線");
            check.Click += (s, e) => Main.CheckConnections();
            var refresh = Theme.Btn("重新整理");
            refresh.Click += (s, e) => RefreshData();

            var tiles = new TableLayoutPanel { Height = tJobs.Height, ColumnCount = 4, RowCount = 1, BackColor = Color.Transparent };
            for (int i = 0; i < 4; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            foreach (var t in new[] { tJobs, tNext, tOk, tFail }) { t.Dock = DockStyle.Fill; t.Margin = new Padding(0, 0, t == tFail ? 0 : Theme.S(14), 0); tiles.Controls.Add(t); }

            var mid = new TableLayoutPanel { Height = Theme.S(230), ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            connGrid.Columns.AddRange(Theme.Col("工作", 22), Theme.Col("類型", 16), Theme.Col("伺服器", 24), Theme.Col("連線狀態", 38));
            var cc = GridCard("各工作的連線狀態", connGrid); cc.Dock = DockStyle.Fill; cc.Margin = new Padding(0, 0, Theme.S(14), 0);
            connGrid.CellFormatting += (s, e) =>
            {
                if (e.ColumnIndex != 3 || e.Value == null) return;
                string v = e.Value.ToString();
                e.CellStyle.ForeColor = v.StartsWith("✔") ? Theme.Success : v.StartsWith("✖") ? Theme.Danger : v.StartsWith("⚠") ? Theme.Warning : Theme.SubText;
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
            };
            var disk = new Card("備份磁碟空間") { Dock = DockStyle.Fill, Margin = new Padding(0) };
            disk.Body.Controls.Add(diskHost);
            mid.Controls.Add(cc); mid.Controls.Add(disk);

            grid.Columns.AddRange(Theme.Col("時間", 16, 150), Theme.Col("工作", 16), Theme.Col("資料庫", 16), Theme.Col("類型", 12), Theme.Col("狀態", 8, 56), Theme.Col("大小", 10), Theme.Col("訊息", 40));
            ColorStatus(grid, 4);

            Stack(Toolbar(run, check, refresh), tiles, mid, GridCard("最近執行（每次執行一筆）", grid));
        }

        public override void RefreshData()
        {
            var jobs = SettingsStore.Current.Jobs;
            bool paused = SettingsStore.Current.General.SchedulerPaused;
            int en = jobs.Count(j => j.Enabled);
            tJobs.Set(en.ToString(), "共 " + jobs.Count + " 個工作" + (paused ? "・排程已暫停" : ""), paused ? Theme.Warning : Theme.Accent);
            var next = jobs.Where(j => j.Enabled && j.NextRun.HasValue).OrderBy(j => j.NextRun).FirstOrDefault();
            tNext.Set(next == null ? "—" : next.NextRun.Value.ToString("MM/dd HH:mm"), next == null ? "沒有排定的備份" : next.Name);

            var logs = Logger.Read(DateTime.Now.AddDays(-30), DateTime.Now);
            var day = logs.Where(l => l.Time >= DateTime.Now.AddHours(-24)).ToList();
            var dayRuns = Logger.Group(day);
            int ok = dayRuns.Count(g => g.Status == LogEntry.Success), fail = dayRuns.Count(g => g.Status == LogEntry.Failed), warn = dayRuns.Count(g => g.Status == LogEntry.Warning);
            long bytes = day.Where(l => l.Status == LogEntry.Success).Sum(l => l.SizeBytes);
            tOk.Set(ok.ToString(), "備份量 " + (bytes > 0 ? Fmt.Size(bytes) : "0"));
            tFail.Set(fail + " / " + warn, fail > 0 ? "請查看備份日誌" : "狀態良好", fail > 0 ? Theme.Danger : warn > 0 ? Theme.Warning : Theme.Success);

            connGrid.Rows.Clear();
            foreach (var j in jobs)
            {
                string st;
                if (!j.ConnVerified) st = "⚠ 尚未驗證，請編輯工作並測試連線";
                else if (!Main.ConnStatus.TryGetValue(j.Id, out st)) st = "檢查中…";
                connGrid.Rows.Add(j.Name, BackupJob.EngineText(j.Engine), j.ServerText(), st);
            }

            grid.Rows.Clear();
            foreach (var g in Logger.Group(logs).Take(20))
                grid.Rows.Add(g.Start.ToString("yyyy/MM/dd HH:mm:ss"), g.Job == "" ? "(系統)" : g.Job, string.Join("、", g.Databases), g.Kind, g.Status == LogEntry.Info ? "" : g.Status, Fmt.Size(g.SizeBytes), g.Summary);
            RefreshDisks();
        }

        void RefreshDisks()
        {
            diskHost.Controls.Clear();
            var roots = new List<string>();
            foreach (var p in SettingsStore.Current.Jobs.SelectMany(j => new[] { j.TargetPath, j.SecondaryPath }).Where(p => !string.IsNullOrWhiteSpace(p) && !BackupEngine.IsUrl(p)))
            {
                try
                {
                    string r = p.StartsWith(@"\\") ? p : Path.GetPathRoot(p);
                    if (!string.IsNullOrEmpty(r) && !roots.Contains(r, StringComparer.OrdinalIgnoreCase)) roots.Add(r);
                }
                catch { }
            }
            if (roots.Count == 0)
            {
                var l = Theme.Lbl("尚未設定備份工作", 9.5f, FontStyle.Regular, Theme.SubText); l.Location = Theme.P(2, 4);
                diskHost.Controls.Add(l);
                return;
            }
            foreach (var r in roots.AsEnumerable().Reverse())
                diskHost.Controls.Add(new DiskBar(r) { Dock = DockStyle.Top });
        }
    }

    public class DiskBar : Control
    {
        readonly string root;
        string text; double ratio = -1;
        readonly Font fRoot = Theme.F(9.5f, FontStyle.Bold), fText = Theme.F(9f);
        public DiskBar(string root)
        {
            this.root = root;
            Height = TextRenderer.MeasureText("國", fRoot).Height + TextRenderer.MeasureText("國", fText).Height + Theme.S(34);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try
            {
                ulong free, total, tfree;
                if (Native.GetDiskFreeSpaceEx(root, out free, out total, out tfree) && total > 0)
                { ratio = 1 - (double)free / total; text = "剩餘 " + Fmt.Size((long)free) + " / 共 " + Fmt.Size((long)total); }
                else text = "無法讀取 (路徑不存在或無權限)";
            }
            catch (Exception ex) { text = ex.Message; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Color.White);
            int th = TextRenderer.MeasureText("國", fRoot).Height;
            // 第一行：路徑；第二行：進度條；第三行：剩餘空間 (分行顯示，長路徑也不會和文字重疊)
            TextRenderer.DrawText(g, root, fRoot, new Rectangle(0, 0, Width - 2, th), Theme.Text, TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
            var bar = new Rectangle(0, th + Theme.S(6), Width - 2, Theme.S(10));
            int t2 = TextRenderer.MeasureText("國", fText).Height;
            TextRenderer.DrawText(g, text, fText, new Rectangle(0, bar.Bottom + Theme.S(4), Width - 2, t2), Theme.SubText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (var b = new SolidBrush(Color.FromArgb(232, 236, 241))) g.FillRectangle(b, bar);
            if (ratio >= 0)
            {
                var c = ratio > 0.9 ? Theme.Danger : ratio > 0.75 ? Theme.Warning : Theme.Accent;
                using (var b = new SolidBrush(c)) g.FillRectangle(b, bar.X, bar.Y, (int)(bar.Width * ratio), bar.Height);
            }
        }
    }

    static class Native
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        public static extern bool GetDiskFreeSpaceEx(string dir, out ulong freeForUser, out ulong total, out ulong totalFree);
    }

    // ───────────────────────── 備份工作 ─────────────────────────
    public class JobsPage : Page
    {
        readonly DataGridView grid = new DataGridView();

        public JobsPage(MainForm m) : base(m, "備份工作")
        {
            var add = Theme.Btn("新增工作", true);
            var edit = Theme.Btn("編輯");
            var copy = Theme.Btn("複製");
            var del = Theme.Btn("刪除");
            var toggle = Theme.Btn("啟用 / 停用");
            var run = Theme.Btn("立即執行");
            add.Click += (s, e) => EditJob(null);
            edit.Click += (s, e) => { var j = Selected(); if (j != null) EditJob(j); };
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) { var j = Selected(); if (j != null) EditJob(j); } };
            copy.Click += (s, e) =>
            {
                var j = Selected(); if (j == null) return;
                var c = j.Clone(); c.Id = Guid.NewGuid().ToString("N"); c.Name = UniqueName(j.Name + " (複本)"); c.LastRun = null; c.LastStatus = c.LastMessage = "";
                Scheduler.Reschedule(c);
                SettingsStore.Current.Jobs.Add(c); SettingsStore.Save(); RefreshData();
            };
            del.Click += (s, e) =>
            {
                var j = Selected(); if (j == null) return;
                if (MessageBox.Show("確定要刪除備份工作「" + j.Name + "」？\n（已產生的備份檔不會被刪除）", "刪除工作", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                SettingsStore.Current.Jobs.Remove(j); SettingsStore.Save(); RefreshData();
            };
            toggle.Click += (s, e) =>
            {
                var j = Selected(); if (j == null) return;
                j.Enabled = !j.Enabled; Scheduler.Reschedule(j); SettingsStore.Save(); RefreshData();
            };
            run.Click += (s, e) =>
            {
                var j = Selected(); if (j == null) return;
                if (!j.ConnVerified) { MessageBox.Show("這個工作的連線尚未驗證。\n請按「編輯」→「1. 連線」→「測試連線」，成功後儲存再執行。", "立即執行", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                Main.RunJobs(new List<BackupJob> { j });
            };

            grid.Columns.AddRange(Theme.Col("狀態", 7, 64), Theme.Col("工作名稱", 15), Theme.Col("伺服器", 14), Theme.Col("類型", 10), Theme.Col("資料庫", 14), Theme.Col("排程", 16), Theme.Col("下次執行", 11, 120), Theme.Col("上次結果", 15));
            grid.CellFormatting += (s, e) =>
            {
                if (e.Value == null) return;
                var j = grid.Rows[e.RowIndex].Tag as BackupJob;
                if (e.ColumnIndex == 0)
                {
                    string v = e.Value.ToString();
                    e.CellStyle.ForeColor = v == "啟用" ? Theme.Success : v.StartsWith("⚠") ? Theme.Warning : Theme.SubText;
                    e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                }
                if (e.ColumnIndex == 7 && j != null && j.LastStatus != "") { e.CellStyle.ForeColor = Theme.StatusColor(j.LastStatus); e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor; }
            };
            var hint = Theme.Lbl("每個工作有自己的伺服器連線，必須測試連線成功才會執行。交易記錄備份需要 FULL 復原模式。", 9f, FontStyle.Regular, Theme.SubText);
            hint.AutoSize = false; hint.Height = TextRenderer.MeasureText("國", hint.Font).Height + Theme.S(4);
            Stack(Toolbar(add, edit, copy, del, toggle, run), hint, GridCard("備份工作清單", grid));
        }

        BackupJob Selected()
        {
            if (grid.CurrentRow == null) { MessageBox.Show("請先選擇一個工作。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return null; }
            return grid.CurrentRow.Tag as BackupJob;
        }

        public static string UniqueName(string n)
        {
            string name = n; int i = 2;
            while (SettingsStore.Current.Jobs.Any(j => j.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = n + " " + i++;
            return name;
        }

        void EditJob(BackupJob job)
        {
            bool isNew = job == null;
            BackupJob work;
            if (isNew)
            {
                // 新工作：伺服器名稱保持空白，由使用者自己輸入
                work = new BackupJob { Name = UniqueName("每日完整備份"), ConnSet = true };
                work.SqlConn = new ConnectionSettings { Server = "" };
            }
            else work = job.Clone();
            using (var f = new JobEditForm(work, job))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
            }
            Scheduler.Reschedule(work);
            if (isNew) SettingsStore.Current.Jobs.Add(work);
            else
            {
                int idx = SettingsStore.Current.Jobs.IndexOf(job);
                if (idx >= 0) SettingsStore.Current.Jobs[idx] = work;
            }
            SettingsStore.Save();
            RefreshData();
            Main.CheckConnections();
        }

        public override void RefreshData()
        {
            string selId = grid.CurrentRow != null && grid.CurrentRow.Tag is BackupJob ? ((BackupJob)grid.CurrentRow.Tag).Id : null;
            grid.Rows.Clear();
            foreach (var j in SettingsStore.Current.Jobs)
            {
                string st = !j.ConnVerified ? "⚠ 未驗證" : j.Enabled ? "啟用" : "停用";
                int r = grid.Rows.Add(st, j.Name, BackupJob.EngineText(j.Engine) + "  " + j.ServerText(), BackupJob.KindText(j.Kind), j.DatabaseText(), j.ScheduleText(),
                    j.Enabled ? Fmt.Time(j.NextRun) : "—", j.LastStatus == "" ? "" : Fmt.Time(j.LastRun) + "  " + j.LastStatus + "・" + j.LastMessage);
                grid.Rows[r].Tag = j;
                if (j.Id == selId) grid.CurrentCell = grid.Rows[r].Cells[1];
            }
        }
    }

    // ───────────────────────── 還原 ─────────────────────────
    public class RestorePage : Page
    {
        class ConnItem
        {
            public string Label; public DbEngine Engine; public ConnectionSettings Sql; public ToolConnection Tool;
            public override string ToString() { return Label; }
        }

        readonly ComboBox cbConn = Combo(380);
        readonly TextBox txtFolder = Box(300);
        readonly ComboBox cbDb = Combo(200);
        readonly DataGridView grid = new DataGridView();
        readonly Label lblChain = new Label { AutoSize = false, UseMnemonic = false };
        readonly TextBox txtTarget = Box(260);
        CheckBox chkOverwrite, chkSafety, chkKill, chkStopAt, chkLeave, chkTail;
        readonly DateTimePicker dtStop = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd HH:mm:ss", Enabled = false };
        readonly Button btnRestore;
        List<BackupItem> items = new List<BackupItem>();
        List<BackupItem> chain = new List<BackupItem>();
        readonly List<ConnItem> extraConns = new List<ConnItem>();

        public RestorePage(MainForm m) : base(m, "還原資料庫")
        {
            // 1. 來源
            var src = new Card("1. 選擇還原點");
            var browse = Theme.Btn("瀏覽…");
            var scan = Theme.Btn("掃描", true);
            var single = Theme.Btn("選擇單一備份檔…");
            var top = Toolbar(Theme.Lbl("備份資料夾"), txtFolder, browse, scan, single, Theme.Lbl("資料庫"), cbDb);
            top.Dock = DockStyle.Top; top.BackColor = Color.White; top.Padding = new Padding(0, Theme.S(4), 0, Theme.S(4));
            grid.Columns.AddRange(Theme.Col("資料庫", 16), Theme.Col("引擎", 11), Theme.Col("類型", 13), Theme.Col("備份時間", 16, 150), Theme.Col("大小", 9), Theme.Col("檔案", 40));
            Theme.StyleGrid(grid);
            grid.Dock = DockStyle.Fill;
            src.Body.Controls.Add(grid); src.Body.Controls.Add(top);

            browse.Click += (s, e) => { var p = Browse(txtFolder.Text); if (p != null) { txtFolder.Text = p; Scan(); } };
            scan.Click += (s, e) => Scan();
            single.Click += (s, e) => PickSingle();
            cbDb.SelectedIndexChanged += (s, e) => FillGrid();
            grid.SelectionChanged += (s, e) => BuildChain();

            // 2. 設定
            var opt = new Card("2. 還原設定");
            var fl = new FieldLayout(opt, 18, opt.ContentTop, 130);
            var other = Theme.Btn("其他伺服器…");
            fl.Row("還原到伺服器", cbConn, 380);
            fl.Beside(cbConn, other);
            lblChain.Font = Theme.F(9.5f); lblChain.ForeColor = Theme.Text;
            fl.Row("還原鏈", lblChain, 820);
            lblChain.Text = "請先掃描備份資料夾，再從上方表格選擇要還原到的備份點"; lblChain.ForeColor = Theme.SubText;
            lblChain.Height = TextRenderer.MeasureText("國", lblChain.Font).Height + Theme.S(4);
            fl.Row("還原為資料庫", txtTarget, 260, "可改成新名稱 (例如 MyDB_TEST) 還原成另一個資料庫，不影響原資料庫");
            int y0 = fl.Y;
            chkOverwrite = fl.Check("覆寫現有資料庫 (WITH REPLACE)", false);
            chkSafety = fl.Check("覆寫前先備份現有資料庫 (建議)", true);
            chkKill = fl.Check("強制中斷其他使用者連線", true);
            int yLeftEnd = fl.Y;
            int col2 = Math.Max(chkOverwrite.Left + chkOverwrite.PreferredSize.Width, Math.Max(chkSafety.Left + chkSafety.PreferredSize.Width, chkKill.Left + chkKill.PreferredSize.Width)) + Theme.S(40);
            fl.X = col2; fl.Y = y0;
            chkStopAt = fl.Check("還原到指定時間點", false);
            dtStop.Font = Theme.F(9.5f); dtStop.Width = Theme.S(200);
            opt.Controls.Add(dtStop);
            dtStop.Location = new Point(chkStopAt.Left + chkStopAt.PreferredSize.Width + Theme.S(10), chkStopAt.Top + (chkStopAt.PreferredSize.Height - dtStop.PreferredSize.Height) / 2);
            chkLeave = fl.Check("還原後保持 RESTORING (之後還要套用記錄)", false);
            chkTail = fl.Check("先做尾端記錄備份並一起還原 (Tail-log，還原到最新)", false);
            fl.X = Theme.S(18); fl.Y = Math.Max(yLeftEnd, fl.Y);
            btnRestore = Theme.Btn("開始還原", true, 140);
            Theme.Danger_(btnRestore);
            fl.Buttons(btnRestore);
            opt.Height = fl.Y + Theme.S(4);

            chkStopAt.CheckedChanged += (s, e) => dtStop.Enabled = chkStopAt.Checked;
            txtTarget.TextChanged += (s, e) => UpdateButton();
            btnRestore.Click += (s, e) => DoRestore();
            other.Click += (s, e) =>
            {
                var sel = CurrentItem();
                using (var f = new ConnectionForm(sel != null ? sel.Engine : DbEngine.SqlServer))
                {
                    if (f.ShowDialog(this) != DialogResult.OK) return;
                    var e2 = f.Editor.Engine;
                    var ci = new ConnItem { Engine = e2, Sql = f.Editor.SqlConn, Tool = f.Editor.ToolConn };
                    ci.Label = BackupJob.EngineText(e2) + "  " + (e2 == DbEngine.SqlServer ? ci.Sql.Server : ci.Tool.Host + ":" + ci.Tool.Port) + "（自訂，已驗證）";
                    extraConns.Add(ci);
                    FillConns(ci);
                }
            };

            opt.Dock = DockStyle.Bottom;
            src.Dock = DockStyle.Fill;
            Controls.Add(src);
            Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = Theme.S(16) });
            Controls.Add(opt);
            UpdateButton();
        }

        void FillConns(ConnItem select = null)
        {
            var cur = select ?? cbConn.SelectedItem as ConnItem;
            cbConn.Items.Clear();
            var seen = new HashSet<string>();
            foreach (var j in SettingsStore.Current.Jobs.Where(j => j.ConnVerified))
            {
                if (!seen.Add(j.ConnKey)) continue;
                cbConn.Items.Add(new ConnItem { Engine = j.Engine, Sql = j.SqlConn, Tool = j.ToolConn, Label = BackupJob.EngineText(j.Engine) + "  " + j.ServerText() + "（" + j.Name + "）" });
            }
            foreach (var c in extraConns) cbConn.Items.Add(c);
            if (cur != null)
            {
                foreach (ConnItem c in cbConn.Items) if (c == cur || c.Label == cur.Label) { cbConn.SelectedItem = c; return; }
            }
            if (cbConn.Items.Count > 0) cbConn.SelectedIndex = 0;
        }

        ConnItem Conn { get { return cbConn.SelectedItem as ConnItem; } }
        BackupItem CurrentItem() { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as BackupItem; }

        public override void RefreshData()
        {
            FillConns();
            if (txtFolder.Text == "")
            {
                var j = SettingsStore.Current.Jobs.FirstOrDefault();
                txtFolder.Text = j != null ? j.TargetPath : "";
            }
        }

        void Scan()
        {
            string folder = txtFolder.Text.Trim();
            if (!Directory.Exists(folder)) { MessageBox.Show("找不到資料夾：" + folder, "還原", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var c = Conn;
            var sqlConn = c != null && c.Engine == DbEngine.SqlServer ? c.Sql : null;
            Cursor = Cursors.WaitCursor;
            Main.SetStatus("掃描備份檔…", -1);
            Task.Run(() =>
            {
                try
                {
                    var list = RestoreEngine.Scan(folder, sqlConn, msg => Main.SetStatus(msg, -1));
                    UI(() => { items = list; FillDbCombo(); Cursor = Cursors.Default; Main.SetStatus("找到 " + list.Count + " 個備份", 0); });
                }
                catch (Exception ex) { UI(() => { Cursor = Cursors.Default; Main.SetStatus("就緒", 0); MessageBox.Show(ex.Message, "掃描失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }); }
            });
        }

        void PickSingle()
        {
            using (var d = new OpenFileDialog { Filter = "備份檔 (*.bak;*.trn;*.zip;*.bacpac;*.sql;*.gz;*.dump)|*.bak;*.trn;*.zip;*.bacpac;*.sql;*.gz;*.dump|所有檔案 (*.*)|*.*", Title = "選擇備份檔" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                string f = d.FileName;
                var c = Conn;
                var sqlConn = c != null && c.Engine == DbEngine.SqlServer ? c.Sql : null;
                Cursor = Cursors.WaitCursor;
                Task.Run(() =>
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(f);
                        var all = RestoreEngine.Scan(dir, sqlConn, null);
                        var mine = all.Where(x => x.AllFiles.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
                        UI(() =>
                        {
                            Cursor = Cursors.Default;
                            if (mine.Count == 0) { MessageBox.Show("無法辨識這個備份檔。\n（非本程式產生的 .bak / .trn 需要先選擇 SQL Server 連線才能讀取檔頭）", "還原", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                            txtFolder.Text = dir;
                            items = all;
                            FillDbCombo(mine[0].Database);
                            foreach (DataGridViewRow r in grid.Rows)
                                if (((BackupItem)r.Tag).AllFiles.Contains(f, StringComparer.OrdinalIgnoreCase)) { grid.CurrentCell = r.Cells[0]; break; }
                        });
                    }
                    catch (Exception ex) { UI(() => { Cursor = Cursors.Default; MessageBox.Show(ex.Message, "讀取失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }); }
                });
            }
        }

        void FillDbCombo(string select = null)
        {
            string cur = select ?? cbDb.SelectedItem as string;
            cbDb.Items.Clear();
            foreach (var n in items.Select(x => x.Database).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)) cbDb.Items.Add(n);
            if (cbDb.Items.Count == 0) { grid.Rows.Clear(); BuildChain(); return; }
            int idx = cur == null ? -1 : cbDb.Items.IndexOf(cur);
            cbDb.SelectedIndex = idx >= 0 ? idx : 0;
            FillGrid();
        }

        void FillGrid()
        {
            grid.Rows.Clear();
            string db = cbDb.SelectedItem as string;
            foreach (var it in items.Where(x => x.Database.Equals(db ?? "", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Time))
            {
                int r = grid.Rows.Add(it.Database, BackupJob.EngineText(it.Engine), it.KindText + (it.IsZip ? " (ZIP)" : "") + (it.Parts.Count > 1 ? " ×" + it.Parts.Count : ""), it.Time.ToString("yyyy/MM/dd HH:mm:ss"), Fmt.Size(it.Size), it.File);
                grid.Rows[r].Tag = it;
            }
            if (db != null) txtTarget.Text = db;
            BuildChain();
        }

        void BuildChain()
        {
            chain.Clear();
            var sel = CurrentItem();
            if (sel == null) { lblChain.Text = "請在上方選擇要還原到的備份點"; lblChain.ForeColor = Theme.SubText; UpdateButton(); return; }
            try
            {
                chain = RestoreEngine.BuildChain(items, sel);
                lblChain.Text = string.Join("  →  ", chain.Select(c => c.KindText + " " + c.Time.ToString("MM/dd HH:mm"))) + "   （共 " + chain.Count + " 個檔案）";
                lblChain.ForeColor = Theme.Text;
            }
            catch (Exception ex) { lblChain.Text = ex.Message; lblChain.ForeColor = Theme.Danger; }
            bool native = sel.Engine == DbEngine.SqlServer && sel.Kind != BackupKind.Bacpac;
            bool isLog = native && sel.Kind == BackupKind.Log;
            chkLeave.Enabled = chkTail.Enabled = native;
            if (!native) { chkLeave.Checked = false; chkTail.Checked = false; }
            if (sel.Kind == BackupKind.Globals) txtTarget.Text = "postgres";
            txtTarget.Enabled = sel.Kind != BackupKind.Globals;
            chkStopAt.Enabled = isLog;
            if (!isLog) chkStopAt.Checked = false;
            if (isLog) dtStop.Value = sel.Time;
            // 自動切到同類型的連線
            var c0 = Conn;
            if (c0 == null || c0.Engine != sel.Engine)
                foreach (ConnItem ci in cbConn.Items) if (ci.Engine == sel.Engine) { cbConn.SelectedItem = ci; break; }
            UpdateButton();
        }

        void UpdateButton()
        {
            if (btnRestore != null) btnRestore.Enabled = chain.Count > 0 && txtTarget.Text.Trim() != "";
        }

        void DoRestore()
        {
            string db = txtTarget.Text.Trim();
            var c = Conn;
            var eng = chain[0].Engine;
            if (c == null || c.Engine != eng)
            {
                MessageBox.Show("請選擇一個 " + BackupJob.EngineText(eng) + " 的伺服器連線。\n可以按「其他伺服器…」新增並測試連線。", "還原", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            bool exists;
            try
            {
                if (chain[0].Kind == BackupKind.Globals) exists = false;
                else exists = eng == DbEngine.SqlServer ? SqlOps.DatabaseExists(c.Sql, db) : eng == DbEngine.MySql ? MySqlTool.Exists(c.Tool, db) : PgTool.Exists(c.Tool, db);
            }
            catch (Exception ex) { MessageBox.Show("無法連線 " + BackupJob.EngineText(eng) + "：" + ex.Message, "還原", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (chkTail.Checked && (!exists || !db.Equals(chain[0].Database, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("尾端記錄備份只能用在「覆寫同名的現有資料庫」。", "還原", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (exists && !chkOverwrite.Checked)
            {
                MessageBox.Show("資料庫「" + db + "」已存在。\n\n若要覆寫，請勾選「覆寫現有資料庫」；\n或改成新的資料庫名稱，還原成另一個資料庫。", "還原", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine("伺服器：" + c.Label);
            sb.AppendLine("即將還原資料庫：" + db + (exists ? "（將覆寫現有資料！）" : "（新資料庫）"));
            sb.AppendLine();
            foreach (var x in chain) sb.AppendLine("  " + x.KindText + "  " + x.Time.ToString("yyyy/MM/dd HH:mm:ss") + "  " + Path.GetFileName(x.File));
            if (chkStopAt.Checked) sb.AppendLine("\n停在時間點：" + dtStop.Value.ToString("yyyy/MM/dd HH:mm:ss"));
            if (exists && chkSafety.Checked) sb.AppendLine("\n覆寫前會先備份現有資料庫。");
            if (exists && chkKill.Checked) sb.AppendLine("目前連到此資料庫的使用者會被中斷。");
            if (exists && chkTail.Checked) sb.AppendLine("會先備份尾端交易記錄，並套用到最後（還原到故障前最新狀態）。");
            using (var cf = new ConfirmForm("確認還原", sb.ToString(), exists ? db : null))
                if (cf.ShowDialog(this) != DialogResult.OK) return;

            var plan = new RestorePlan
            {
                Chain = chain.ToList(), TargetDb = db, Overwrite = chkOverwrite.Checked, SafetyBackup = chkSafety.Checked,
                KillConnections = chkKill.Checked, LeaveRestoring = chkLeave.Checked, TailLog = chkTail.Checked,
                StopAt = chkStopAt.Checked ? (DateTime?)dtStop.Value : null,
                SafetyBackupDir = txtFolder.Text.Trim(), SqlConn = c.Sql, ToolConn = c.Tool
            };
            // 備份檔在 NAS：沿用備份到同一個分享的工作所設定的網路帳號
            var share = NetPath.IsNetwork(chain[0].File) ? NetShare.ShareRoot(NetPath.ToUnc(chain[0].File)) : null;
            if (share != null)
            {
                var src = SettingsStore.Current.Jobs.FirstOrDefault(j => new[] { j.TargetPath, j.SecondaryPath }.Any(p => !string.IsNullOrWhiteSpace(p) && NetPath.IsNetwork(p) && NetShare.ShareRoot(NetPath.ToUnc(p)).Equals(share, StringComparison.OrdinalIgnoreCase)));
                if (src != null) { plan.NetUser = src.NetUser; plan.NetPassword = src.NetPassword; }
            }
            btnRestore.Enabled = false;
            Task.Run(() =>
            {
                try
                {
                    RestoreEngine.Execute(plan);
                    UI(() => { UpdateButton(); Main.Notify("還原完成", db + " 已還原", ToolTipIcon.Info); MessageBox.Show("資料庫「" + db + "」還原完成。", "還原", MessageBoxButtons.OK, MessageBoxIcon.Information); });
                }
                catch (Exception ex)
                {
                    UI(() => { UpdateButton(); MessageBox.Show("還原失敗：\n\n" + ex.Message, "還原", MessageBoxButtons.OK, MessageBoxIcon.Error); });
                }
            });
        }
    }

    /// <summary>危險操作確認視窗：覆寫既有資料庫時，需輸入資料庫名稱。</summary>
    public class ConfirmForm : Form
    {
        public ConfirmForm(string title, string message, string requireText)
        {
            Text = title; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = Theme.F(9.5f);
            int w = Theme.S(560);
            var txt = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Text = message.Replace("\r\n", "\n").Replace("\n", "\r\n"), Location = Theme.P(20, 20), Size = new Size(w - Theme.S(40), Theme.S(220)), ScrollBars = ScrollBars.Vertical, Font = Theme.F(9.5f) };
            Controls.Add(txt);
            int y = txt.Bottom + Theme.S(14);
            var ok = Theme.Btn("確定還原", true, 110); Theme.Danger_(ok);
            var cancel = Theme.Btn("取消", false, 90);
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok; CancelButton = cancel;
            if (requireText != null)
            {
                var l = Theme.Lbl("請輸入資料庫名稱「" + requireText + "」以確認覆寫：", 9.5f, FontStyle.Bold, Theme.Danger);
                l.Location = new Point(Theme.S(20), y);
                var input = new TextBox { Location = new Point(Theme.S(20), y + l.PreferredHeight + Theme.S(8)), Width = Theme.S(300), Font = Theme.F(10f) };
                Controls.Add(l); Controls.Add(input);
                y = input.Bottom + Theme.S(16);
                ok.Enabled = false;
                input.TextChanged += (s, e) => ok.Enabled = input.Text == requireText;
            }
            cancel.Location = new Point(w - cancel.Width - Theme.S(20), y);
            ok.Location = new Point(cancel.Left - ok.Width - Theme.S(10), y);
            Controls.Add(ok); Controls.Add(cancel);
            ClientSize = new Size(w, cancel.Bottom + Theme.S(16));
        }
    }

    // ───────────────────────── 備份日誌 ─────────────────────────
    /// <summary>
    /// 上：每次執行一筆 (同一個工作的同一次備份 / 還原合在一起)；下：該次執行依時間先後的明細。
    /// 進入頁面時不顯示，按「查詢」後才載入。
    /// </summary>
    public class LogPage : Page
    {
        readonly DateTimePicker dtFrom = Date(130), dtTo = Date(130);
        readonly ComboBox cbStatus = Combo(100);
        readonly ComboBox cbJob = Combo(180);
        readonly TextBox txtSearch = Box(160);
        readonly DataGridView gridRuns = new DataGridView(), gridDetail = new DataGridView();
        readonly Label lblCount = new Label { AutoSize = true, UseMnemonic = false };
        readonly Label lblEmpty = new Label { AutoSize = false, UseMnemonic = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
        List<LogGroup> current = new List<LogGroup>();
        bool queried;

        public LogPage(MainForm m) : base(m, "備份日誌")
        {
            dtFrom.Value = DateTime.Today.AddDays(-7); dtTo.Value = DateTime.Today;
            cbStatus.Items.AddRange(new object[] { "全部", LogEntry.Success, LogEntry.Failed, LogEntry.Warning });
            cbStatus.SelectedIndex = 0;
            var query = Theme.Btn("查詢", true);
            var export = Theme.Btn("匯出 CSV");
            var open = Theme.Btn("開啟日誌資料夾");
            var clear = Theme.Btn("全部清除");
            clear.ForeColor = Theme.Danger; clear.FlatAppearance.BorderColor = Color.FromArgb(240, 190, 184);
            clear.Click += (s, e) =>
            {
                if (MessageBox.Show("確定要清除「全部」備份日誌嗎？\n\n所有日期的紀錄都會刪除，無法復原。\n（不會刪除備份檔）", "全部清除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                int n = Logger.ClearAll();
                queried = false; RefreshData();
                Main.SetStatus("已清除 " + n + " 個日誌檔", 0);
            };
            query.Click += (s, e) => { queried = true; Query(); };
            txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; queried = true; Query(); } };
            export.Click += (s, e) => Export();
            open.Click += (s, e) => Process.Start("explorer.exe", AppPaths.LogDir);

            var bar = Toolbar(Theme.Lbl("日期"), dtFrom, Theme.Lbl("至"), dtTo, Theme.Lbl("工作"), cbJob, Theme.Lbl("結果"), cbStatus, Theme.Lbl("關鍵字"), txtSearch, query, export, open, clear);
            lblCount.Font = Theme.F(9f); lblCount.ForeColor = Theme.SubText;
            var countHost = new Panel { Height = TextRenderer.MeasureText("國", lblCount.Font).Height + Theme.S(4) };
            countHost.Controls.Add(lblCount);

            gridRuns.Columns.AddRange(Theme.Col("開始時間", 15, 150), Theme.Col("工作", 16), Theme.Col("類型", 9), Theme.Col("結果", 6, 50), Theme.Col("資料庫", 10), Theme.Col("備份量", 8), Theme.Col("耗時", 7), Theme.Col("摘要", 40));
            ColorStatus(gridRuns, 3);
            gridDetail.Columns.AddRange(Theme.Col("時間", 9, 80), Theme.Col("資料庫", 9), Theme.Col("狀態", 6, 50), Theme.Col("大小", 8), Theme.Col("耗時", 7), Theme.Col("內容", 50), Theme.Col("檔案", 22));
            ColorStatus(gridDetail, 2);
            gridRuns.SelectionChanged += (s, e) => ShowDetail();
            gridDetail.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var l = gridDetail.Rows[e.RowIndex].Tag as LogEntry;
                if (l == null) return;
                MessageBox.Show(string.Format("時間：{0:yyyy/MM/dd HH:mm:ss}\n工作：{1}\n資料庫：{2}\n類型：{3}\n狀態：{4}\n檔案：{5}\n大小：{6}\n耗時：{7}\n\n{8}",
                    l.Time, l.Job, l.Database, l.Kind, l.Status, l.File, Fmt.Size(l.SizeBytes), Fmt.Duration(l.DurationSec), l.Message), "日誌明細");
            };

            var runsCard = GridCard("執行紀錄（每次執行一筆，點選查看明細）", gridRuns);
            lblEmpty.Font = Theme.F(10f); lblEmpty.ForeColor = Theme.SubText; lblEmpty.BackColor = Color.White;
            lblEmpty.Text = "請設定日期、工作或結果等條件後，按「查詢」顯示日誌";
            gridRuns.Paint += (s, e) =>
            {
                if (gridRuns.Rows.Count > 0 || string.IsNullOrEmpty(lblEmpty.Text)) return;
                int top = gridRuns.ColumnHeadersHeight;
                var r = new Rectangle(0, top, gridRuns.ClientSize.Width, Math.Max(Theme.S(40), gridRuns.ClientSize.Height - top));
                TextRenderer.DrawText(e.Graphics, lblEmpty.Text, lblEmpty.Font, r, Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            };
            var detailCard = GridCard("本次執行明細（依時間先後）", gridDetail);
            var split = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0) };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            runsCard.Dock = DockStyle.Fill; detailCard.Dock = DockStyle.Fill;
            runsCard.Margin = new Padding(0, 0, 0, Theme.S(12)); detailCard.Margin = new Padding(0);
            split.Controls.Add(runsCard, 0, 0); split.Controls.Add(detailCard, 0, 1);
            Stack(bar, countHost, split);
        }

        /// <summary>切換到本頁或有新日誌時：還沒查詢過就保持空白；查詢過就用相同條件重新整理。</summary>
        public override void RefreshData()
        {
            string sel = cbJob.SelectedItem as string;
            cbJob.Items.Clear(); cbJob.Items.Add("全部");
            foreach (var j in SettingsStore.Current.Jobs.Select(x => x.Name).Concat(new[] { "還原" }).Distinct()) cbJob.Items.Add(j);
            int ix = sel == null ? 0 : cbJob.Items.IndexOf(sel);
            cbJob.SelectedIndex = ix >= 0 ? ix : 0;
            if (queried) Query();
            else
            {
                current = new List<LogGroup>();
                gridRuns.Rows.Clear(); gridDetail.Rows.Clear();
                lblEmpty.Text = "請設定日期、工作或結果等條件後，按「查詢」顯示日誌";
                lblCount.Text = ""; gridRuns.Invalidate();
            }
        }

        /// <summary>等同按下「查詢」(測試用)。</summary>
        public void RunQuery() { queried = true; Query(); }
        public int RunCount { get { return current.Count; } }
        public bool HasRows { get { return gridRuns.Rows.Count > 0; } }

        void Query()
        {
            var list = Logger.Read(dtFrom.Value.Date, dtTo.Value.Date.AddDays(1).AddSeconds(-1));
            var groups = Logger.Group(list);
            string st = cbStatus.SelectedIndex > 0 ? (string)cbStatus.SelectedItem : null;
            string job = cbJob.SelectedIndex > 0 ? (string)cbJob.SelectedItem : null;
            string q = txtSearch.Text.Trim();
            current = groups.Where(g => (st == null || g.Status == st) && (job == null || g.Job == job) &&
                (q == "" || g.Entries.Any(l => (l.Job + l.Database + l.Message + l.File + l.Kind).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))).ToList();
            string selId = gridRuns.CurrentRow != null && gridRuns.CurrentRow.Tag is LogGroup ? ((LogGroup)gridRuns.CurrentRow.Tag).Id : null;
            gridRuns.SuspendLayout();
            gridRuns.Rows.Clear();
            foreach (var g in current.Take(3000))
            {
                int r = gridRuns.Rows.Add(g.Start.ToString("yyyy/MM/dd HH:mm:ss"), g.Job == "" ? "(系統)" : g.Job, g.Kind, g.Status == LogEntry.Info ? "" : g.Status,
                    string.Join("、", g.Databases), Fmt.Size(g.SizeBytes), Fmt.Duration(g.DurationSec), g.Summary);
                gridRuns.Rows[r].Tag = g;
                if (g.Id == selId) gridRuns.CurrentCell = gridRuns.Rows[r].Cells[0];
            }
            gridRuns.ResumeLayout();
            lblEmpty.Text = current.Count == 0 ? "沒有符合條件的日誌" : "";
            gridRuns.Invalidate();
            lblCount.Text = string.Format("共 {0} 次執行　成功 {1}　失敗 {2}　警告 {3}（日誌 {4} 筆）", current.Count, current.Count(g => g.Status == LogEntry.Success),
                current.Count(g => g.Status == LogEntry.Failed), current.Count(g => g.Status == LogEntry.Warning), current.Sum(g => g.Entries.Count));
            ShowDetail();
        }

        void ShowDetail()
        {
            gridDetail.Rows.Clear();
            var g = gridRuns.CurrentRow == null ? null : gridRuns.CurrentRow.Tag as LogGroup;
            if (g == null) return;
            foreach (var l in g.Entries)
            {
                int r = gridDetail.Rows.Add(l.Time.ToString("HH:mm:ss"), l.Database, l.Status, Fmt.Size(l.SizeBytes), Fmt.Duration(l.DurationSec), l.Message, l.File);
                gridDetail.Rows[r].Tag = l;
            }
        }

        void Export()
        {
            if (current.Count == 0) { MessageBox.Show("請先查詢，再匯出查詢結果。", "匯出 CSV", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (var d = new SaveFileDialog { Filter = "CSV 檔 (*.csv)|*.csv", FileName = "備份日誌_" + DateTime.Now.ToString("yyyyMMdd") + ".csv" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                Func<string, string> c = s => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
                var sb = new StringBuilder("執行開始,工作,執行結果,時間,資料庫,類型,狀態,檔案,大小(Bytes),耗時(秒),內容\r\n");
                foreach (var g in current)
                    foreach (var l in g.Entries)
                        sb.Append(string.Join(",", c(g.Start.ToString("yyyy/MM/dd HH:mm:ss")), c(g.Job), c(g.Status), c(l.Time.ToString("yyyy/MM/dd HH:mm:ss")), c(l.Database), c(l.Kind), c(l.Status), c(l.File), l.SizeBytes, l.DurationSec.ToString("0.0"), c(l.Message))).Append("\r\n");
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                Main.SetStatus("已匯出 " + current.Count + " 次執行到 " + d.FileName, 0);
            }
        }
    }

    // ───────────────────────── 通知設定 ─────────────────────────
    public class NotifyPage : Page
    {
        CheckBox chkEnabled, chkSsl, chkSuccess, chkFail;
        readonly TextBox txtHost = new TextBox(), txtUser = new TextBox(), txtFrom = new TextBox(), txtTo = new TextBox();
        readonly PasswordBox txtPwd = new PasswordBox();
        readonly NumericUpDown numPort = new NumericUpDown { Minimum = 1, Maximum = 65535 };

        public NotifyPage(MainForm m) : base(m, "通知設定")
        {
            var stack = new ScrollStack();
            var card = new Card("Email 通知");
            var fl = new FieldLayout(card, 18, card.ContentTop, 140);
            chkEnabled = fl.Check("啟用 Email 通知", false);
            fl.Row("SMTP 伺服器", txtHost, 260, "例如 smtp.gmail.com、smtp.office365.com");
            fl.Row("連接埠", numPort, 100, "587 (STARTTLS) / 465 / 25");
            chkSsl = fl.Check("使用 SSL / TLS 加密", true);
            fl.Row("SMTP 帳號", txtUser, 260);
            fl.Row("SMTP 密碼", txtPwd, 260, "Gmail 請使用「應用程式密碼」");
            fl.Row("寄件者", txtFrom, 260, "空白 = 使用 SMTP 帳號");
            fl.Row("收件者", txtTo, 400, "多人用 ; 分隔");
            fl.Section("何時寄送");
            chkFail = fl.Check("備份失敗或有錯誤時", true);
            chkSuccess = fl.Check("備份成功時也寄送", false);
            var test = Theme.Btn("寄送測試信"); var save = Theme.Btn("儲存", true);
            fl.Buttons(save, test);
            stack.AddCard(card, fl.Y);
            save.Click += (s, e) => { Collect(); SettingsStore.Save(); txtPwd.SetSaved(SettingsStore.Current.Mail.Password); Main.SetStatus("通知設定已儲存", 0); };
            test.Click += (s, e) =>
            {
                Collect();
                Cursor = Cursors.WaitCursor;
                Task.Run(() =>
                {
                    try
                    {
                        Mailer.Send("[測試] SQL 備份管理通知 - " + Environment.MachineName, "<p>這是一封測試信。若您收到此信，表示 Email 通知設定正確。</p><p>主機：" + Environment.MachineName + "<br/>時間：" + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "</p>");
                        UI(() => { Cursor = Cursors.Default; MessageBox.Show("測試信已寄出，請檢查收件匣。", "通知", MessageBoxButtons.OK, MessageBoxIcon.Information); });
                    }
                    catch (Exception ex) { UI(() => { Cursor = Cursors.Default; MessageBox.Show("寄送失敗：\n" + ex.Message, "通知", MessageBoxButtons.OK, MessageBoxIcon.Error); }); }
                });
            };
            Controls.Add(stack);
        }

        void Collect()
        {
            var mm = SettingsStore.Current.Mail;
            mm.Enabled = chkEnabled.Checked; mm.Host = txtHost.Text.Trim(); mm.Port = (int)numPort.Value; mm.EnableSsl = chkSsl.Checked;
            mm.User = txtUser.Text.Trim(); mm.Password = txtPwd.Text; mm.From = txtFrom.Text.Trim(); mm.To = txtTo.Text.Trim();
            mm.NotifyOnSuccess = chkSuccess.Checked; mm.NotifyOnFailure = chkFail.Checked;
        }

        public override void RefreshData()
        {
            var mm = SettingsStore.Current.Mail;
            chkEnabled.Checked = mm.Enabled; txtHost.Text = mm.Host; numPort.Value = Math.Max(1, Math.Min(65535, mm.Port)); chkSsl.Checked = mm.EnableSsl;
            txtUser.Text = mm.User; txtPwd.SetSaved(mm.Password); txtFrom.Text = mm.From; txtTo.Text = mm.To;
            chkSuccess.Checked = mm.NotifyOnSuccess; chkFail.Checked = mm.NotifyOnFailure;
        }
    }

    // ───────────────────────── 系統設定 ─────────────────────────
    public class SystemPage : Page
    {
        CheckBox chkStart, chkMin, chkClose, chkMissed, chkBalloon;
        readonly NumericUpDown numLog = new NumericUpDown { Minimum = 0, Maximum = 3650 };
        readonly ComboBox cbFont = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        static readonly float[] Sizes = { 9f, 10f, 11f, 12f, 13f, 14f, 16f };

        public SystemPage(MainForm m) : base(m, "系統設定")
        {
            var stack = new ScrollStack();

            // 外觀
            var look = new Card("外觀");
            var fx = new FieldLayout(look, 18, look.ContentTop, 140);
            foreach (var s in Sizes) cbFont.Items.Add(s + " pt" + (s == 10f ? "（預設）" : ""));
            fx.Row("字體大小", cbFont, 160, "整個介面（含間距、列高）會等比例放大縮小，文字不會重疊");
            var apply = Theme.Btn("套用字體", true);
            fx.Buttons(apply);
            apply.Click += (s, e) =>
            {
                float size = Sizes[Math.Max(0, cbFont.SelectedIndex)];
                if (Math.Abs(size - SettingsStore.Current.General.FontSize) < 0.01) return;
                SettingsStore.Current.General.FontSize = size;
                SettingsStore.Save();
                Main.RebuildUi();
            };
            stack.AddCard(look, fx.Y);

            // 程式行為
            var card = new Card("程式行為");
            var fl = new FieldLayout(card, 18, card.ContentTop, 140);
            chkStart = fl.Check("Windows 登入後自動啟動", false);
            chkMin = fl.Check("啟動時直接縮小到右下角系統匣", false);
            chkClose = fl.Check("按視窗關閉鈕 (X) 時縮小到系統匣，而不是結束程式", true);
            chkMissed = fl.Check("程式未執行而錯過排程時，啟動後自動補做備份", true);
            chkBalloon = fl.Check("備份完成時顯示右下角通知", true);
            fl.Row("日誌保留天數", numLog, 100, "0 = 永久保留");
            var save = Theme.Btn("儲存", true);
            fl.Buttons(save);
            stack.AddCard(card, fl.Y);

            // 工作排程器
            var ts = new Card("搭配 Windows 工作排程器（建議）");
            var ft = new FieldLayout(ts, 18, ts.ContentTop, 140);
            ft.Note("Server 重新開機後若沒有人登入，本程式不會啟動。建議在「工作排程器」另外建立一個保險工作，" +
                    "設定「不論使用者登入與否均執行」，執行下列命令（無畫面、執行完即結束，結果一樣寫入日誌）：", 780);
            var cmd = new TextBox { ReadOnly = true, Font = new Font("Consolas", 10f * Theme.SettingsFont / Theme.BaseSize), BackColor = Color.FromArgb(246, 248, 250), Text = "\"" + Application.ExecutablePath + "\" --run \"工作名稱\"" };
            ft.Row(null, cmd, 640);
            var copy = ft.Beside(cmd, Theme.Btn("複製命令"));
            copy.Click += (s, e) => { Clipboard.SetText(cmd.Text); Main.SetStatus("已複製到剪貼簿", 0); };
            ft.Note("其他參數：--run-all 執行所有啟用的工作；--minimized 啟動後縮小到系統匣。", 780);
            stack.AddCard(ts, ft.Y);

            // 資料位置
            var dl = new Card("資料位置");
            var fd = new FieldLayout(dl, 18, dl.ContentTop, 140);
            fd.Note("設定檔與日誌：" + AppPaths.DataDir, 780, Theme.Text);
            var open = Theme.Btn("開啟資料夾");
            fd.Buttons(open);
            open.Click += (s, e) => Process.Start("explorer.exe", AppPaths.DataDir);
            fd.Note("SQL 備份管理  版本 " + Application.ProductVersion + "　|　.NET Framework 4.8　|　SQL Server 2008 R2 ~ 2022（含 Express）・MySQL / MariaDB・PostgreSQL", 780);
            stack.AddCard(dl, fd.Y);

            save.Click += (s, e) =>
            {
                var g = SettingsStore.Current.General;
                g.StartWithWindows = chkStart.Checked; g.StartMinimized = chkMin.Checked; g.MinimizeToTrayOnClose = chkClose.Checked;
                g.RunMissedJobs = chkMissed.Checked; g.ShowBalloon = chkBalloon.Checked; g.LogRetentionDays = (int)numLog.Value;
                try { AutoStart.Apply(g.StartWithWindows); }
                catch (Exception ex) { MessageBox.Show("設定開機啟動失敗：" + ex.Message, "系統設定", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                SettingsStore.Save();
                Main.SetStatus("系統設定已儲存", 0);
            };
            Controls.Add(stack);
        }

        public override void RefreshData()
        {
            var g = SettingsStore.Current.General;
            int idx = Array.FindIndex(Sizes, x => Math.Abs(x - g.FontSize) < 0.01);
            cbFont.SelectedIndex = idx >= 0 ? idx : 1;
            chkStart.Checked = g.StartWithWindows; chkMin.Checked = g.StartMinimized; chkClose.Checked = g.MinimizeToTrayOnClose;
            chkMissed.Checked = g.RunMissedJobs; chkBalloon.Checked = g.ShowBalloon; numLog.Value = Math.Max(0, Math.Min(3650, g.LogRetentionDays));
        }
    }
}
