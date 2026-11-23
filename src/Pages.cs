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
            Dock = DockStyle.Fill; BackColor = Theme.Page; Padding = new Padding(24, 20, 24, 20); Visible = false;
            Theme.DoubleBuffer(this);
        }

        public virtual void RefreshData() { }

        /// <summary>由上而下排列；最後一個元素填滿剩餘空間 (傳 null 則不填滿)。</summary>
        protected void Stack(params Control[] items)
        {
            int last = items.Length - 1;
            if (items[last] != null) { items[last].Dock = DockStyle.Fill; Controls.Add(items[last]); }
            for (int i = last - 1; i >= 0; i--)
            {
                Controls.Add(new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Color.Transparent });
                items[i].Dock = DockStyle.Top;
                Controls.Add(items[i]);
            }
        }

        protected static FlowLayoutPanel Toolbar(params Control[] items)
        {
            var f = new FlowLayoutPanel { Height = 34, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0), Margin = new Padding(0) };
            f.Controls.AddRange(items);
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
            c.Controls.Add(grid);
            return c;
        }

        protected static string Browse(string current)
        {
            using (var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(current) ? current : "", ShowNewFolderButton = true })
                return d.ShowDialog() == DialogResult.OK ? d.SelectedPath : null;
        }

        protected static void ColorStatus(DataGridView g, int col)
        {
            g.CellFormatting += (s, e) =>
            {
                if (e.ColumnIndex != col || e.Value == null) return;
                e.CellStyle.ForeColor = Theme.StatusColor(e.Value.ToString());
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                e.CellStyle.Font = Theme.F(9.5f, FontStyle.Bold);
            };
        }
    }

    // ───────────────────────── 總覽 ─────────────────────────
    public class DashboardPage : Page
    {
        StatTile tJobs = new StatTile { Caption = "啟用中的備份工作" };
        StatTile tNext = new StatTile { Caption = "下次備份" };
        StatTile tOk = new StatTile { Caption = "近 24 小時成功", Stripe = Theme.Success };
        StatTile tFail = new StatTile { Caption = "近 24 小時失敗 / 警告", Stripe = Theme.Danger };
        Label[] infoValues = new Label[5];
        Panel diskHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true };
        DataGridView grid = new DataGridView();

        public DashboardPage(MainForm m) : base(m, "總覽")
        {
            var run = Theme.Btn("立即執行全部工作", true);
            run.Click += (s, e) => Main.RunJobs(SettingsStore.Current.Jobs.Where(j => j.Enabled).ToList());
            var refresh = Theme.Btn("重新整理");
            refresh.Click += (s, e) => RefreshData();

            var tiles = new TableLayoutPanel { Height = 104, ColumnCount = 4, RowCount = 1, BackColor = Color.Transparent };
            for (int i = 0; i < 4; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            foreach (var t in new[] { tJobs, tNext, tOk, tFail }) { t.Dock = DockStyle.Fill; t.Margin = new Padding(0, 0, t == tFail ? 0 : 14, 0); tiles.Controls.Add(t); }

            var mid = new TableLayoutPanel { Height = 220, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            var server = new Card("SQL Server 資訊") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
            string[] keys = { "執行個體", "版本", "服務帳戶", "資料庫", "預設備份路徑" };
            for (int i = 0; i < keys.Length; i++)
            {
                var k = Theme.Lbl(keys[i], 9.5f, FontStyle.Regular, Theme.SubText); k.Location = new Point(18, 56 + i * 30);
                var v = new Label { Font = Theme.F(9.5f), ForeColor = Theme.Text, Location = new Point(130, 56 + i * 30), AutoEllipsis = true, Height = 22, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right, Text = "—" };
                v.Width = 300;
                server.Controls.Add(k); server.Controls.Add(v);
                infoValues[i] = v;
            }
            server.Resize += (s, e) => { foreach (var v in infoValues) v.Width = Math.Max(100, server.Width - 150); };
            var disk = new Card("備份磁碟空間") { Dock = DockStyle.Fill, Margin = new Padding(0) };
            disk.Controls.Add(diskHost);
            mid.Controls.Add(server); mid.Controls.Add(disk);

            grid.Columns.AddRange(Theme.Col("時間", 16, 155), Theme.Col("工作", 16), Theme.Col("資料庫", 16), Theme.Col("類型", 12), Theme.Col("狀態", 8), Theme.Col("大小", 10), Theme.Col("訊息", 40));
            ColorStatus(grid, 4);

            Stack(Toolbar(run, refresh), tiles, mid, GridCard("最近活動", grid));
        }

        public override void RefreshData()
        {
            var jobs = SettingsStore.Current.Jobs;
            int en = jobs.Count(j => j.Enabled);
            tJobs.Set(en.ToString(), "共 " + jobs.Count + " 個工作" + (SettingsStore.Current.General.SchedulerPaused ? "・排程已暫停" : ""), SettingsStore.Current.General.SchedulerPaused ? Theme.Warning : Theme.Accent);
            var next = jobs.Where(j => j.Enabled && j.NextRun.HasValue).OrderBy(j => j.NextRun).FirstOrDefault();
            tNext.Set(next == null ? "—" : next.NextRun.Value.ToString("MM/dd HH:mm"), next == null ? "沒有排定的備份" : next.Name);

            var logs = Logger.Read(DateTime.Now.AddDays(-30), DateTime.Now);
            var day = logs.Where(l => l.Time >= DateTime.Now.AddHours(-24)).ToList();
            int ok = day.Count(l => l.Status == LogEntry.Success), fail = day.Count(l => l.Status == LogEntry.Failed), warn = day.Count(l => l.Status == LogEntry.Warning);
            long bytes = day.Where(l => l.Status == LogEntry.Success).Sum(l => l.SizeBytes);
            tOk.Set(ok.ToString(), "備份量 " + (bytes > 0 ? Fmt.Size(bytes) : "0"));
            tFail.Set(fail + " / " + warn, fail > 0 ? "請查看備份日誌" : "狀態良好", fail > 0 ? Theme.Danger : warn > 0 ? Theme.Warning : Theme.Success);

            grid.Rows.Clear();
            foreach (var l in logs.Take(30))
                grid.Rows.Add(l.Time.ToString("yyyy/MM/dd HH:mm:ss"), l.Job, l.Database, l.Kind, l.Status, Fmt.Size(l.SizeBytes), l.Message);

            RefreshDisks();
            var si = Main.Server;
            if (si != null)
            {
                infoValues[0].Text = SettingsStore.Current.Connection.Server + "  (" + si.ServerName + ")";
                infoValues[1].Text = si.Summary;
                infoValues[2].Text = si.ServiceAccount;
                infoValues[3].Text = Main.DatabaseCount + " 個";
                infoValues[4].Text = si.DefaultBackupPath;
            }
            else
            {
                infoValues[0].Text = SettingsStore.Current.Connection.Server;
                infoValues[1].Text = Main.ServerError ?? "連線中…";
                for (int i = 2; i < 5; i++) infoValues[i].Text = "—";
            }
        }

        void RefreshDisks()
        {
            diskHost.Controls.Clear();
            var paths = SettingsStore.Current.Jobs.SelectMany(j => new[] { j.TargetPath, j.SecondaryPath })
                .Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            var roots = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    string r = p.StartsWith(@"\\") ? p : Path.GetPathRoot(p);
                    if (!roots.Contains(r, StringComparer.OrdinalIgnoreCase)) roots.Add(r);
                }
                catch { }
            }
            if (roots.Count == 0)
            {
                var l = Theme.Lbl("尚未設定備份工作", 9.5f, FontStyle.Regular, Theme.SubText); l.Location = new Point(2, 4);
                diskHost.Controls.Add(l);
                return;
            }
            foreach (var r in roots.AsEnumerable().Reverse())
                diskHost.Controls.Add(new DiskBar(r) { Dock = DockStyle.Top });
        }
    }

    public class DiskBar : Control
    {
        string root, text; double ratio = -1;
        public DiskBar(string root)
        {
            this.root = root; Height = 52;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try
            {
                if (root.StartsWith(@"\\"))
                {
                    ulong free, total, tfree;
                    if (Native.GetDiskFreeSpaceEx(root, out free, out total, out tfree) && total > 0)
                    { ratio = 1 - (double)free / total; text = "剩餘 " + Fmt.Size((long)free) + " / 共 " + Fmt.Size((long)total); }
                    else text = "無法讀取 (路徑不存在或無權限)";
                }
                else
                {
                    var d = new DriveInfo(root);
                    if (d.IsReady) { ratio = 1 - (double)d.AvailableFreeSpace / d.TotalSize; text = "剩餘 " + Fmt.Size(d.AvailableFreeSpace) + " / 共 " + Fmt.Size(d.TotalSize); }
                    else text = "磁碟未就緒";
                }
            }
            catch (Exception ex) { text = ex.Message; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Color.White);
            using (var f = Theme.F(9.5f, FontStyle.Bold)) TextRenderer.DrawText(g, root, f, new Rectangle(0, 2, Width / 2, 20), Theme.Text, TextFormatFlags.EndEllipsis);
            using (var f = Theme.F(9f)) TextRenderer.DrawText(g, text, f, new Rectangle(Width / 2 - 40, 2, Width / 2 + 40, 20), Theme.SubText, TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
            var bar = new Rectangle(0, 26, Width - 2, 10);
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
        DataGridView grid = new DataGridView();

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
            run.Click += (s, e) => { var j = Selected(); if (j != null) Main.RunJobs(new List<BackupJob> { j }); };

            grid.Columns.AddRange(Theme.Col("狀態", 7, 56), Theme.Col("工作名稱", 16), Theme.Col("類型", 10), Theme.Col("資料庫", 18), Theme.Col("排程", 18), Theme.Col("下次執行", 12, 120), Theme.Col("上次執行", 12, 120), Theme.Col("上次結果", 16));
            grid.CellFormatting += (s, e) =>
            {
                if (e.Value == null) return;
                if (e.ColumnIndex == 0) { e.CellStyle.ForeColor = e.Value.ToString() == "啟用" ? Theme.Success : Theme.SubText; e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor; }
                if (e.ColumnIndex == 7)
                {
                    var j = grid.Rows[e.RowIndex].Tag as BackupJob;
                    if (j != null && j.LastStatus != "") { e.CellStyle.ForeColor = Theme.StatusColor(j.LastStatus); e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor; }
                }
            };
            var hint = new Label { Text = "提示：交易記錄備份需要資料庫為 FULL 復原模式。常見組合：每週日完整備份 + 每天差異備份 + 每小時交易記錄備份。", Font = Theme.F(9f), ForeColor = Theme.SubText, Height = 20 };
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
            var work = isNew ? new BackupJob { Name = UniqueName("每日完整備份") } : job.Clone();
            if (isNew && Main.Server != null && !string.IsNullOrEmpty(Main.Server.DefaultBackupPath)) work.TargetPath = Main.Server.DefaultBackupPath;
            if (isNew && Main.Server != null && !Main.Server.SupportsCompression) { work.Compression = false; }
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
        }

        public override void RefreshData()
        {
            string selId = grid.CurrentRow != null && grid.CurrentRow.Tag is BackupJob ? ((BackupJob)grid.CurrentRow.Tag).Id : null;
            grid.Rows.Clear();
            foreach (var j in SettingsStore.Current.Jobs)
            {
                int r = grid.Rows.Add(j.Enabled ? "啟用" : "停用", j.Name, BackupJob.KindText(j.Kind), j.DatabaseText(), j.ScheduleText(),
                    j.Enabled ? Fmt.Time(j.NextRun) : "—", Fmt.Time(j.LastRun), j.LastStatus == "" ? "" : j.LastStatus + "・" + j.LastMessage);
                grid.Rows[r].Tag = j;
                if (j.Id == selId) grid.CurrentCell = grid.Rows[r].Cells[1];
            }
        }
    }

    // ───────────────────────── 還原 ─────────────────────────
    public class RestorePage : Page
    {
        TextBox txtFolder = new TextBox();
        ComboBox cbDb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        DataGridView grid = new DataGridView();
        Label lblChain = new Label { AutoSize = false };
        TextBox txtTarget = new TextBox();
        CheckBox chkOverwrite, chkSafety, chkKill, chkStopAt, chkLeave, chkTail;
        DateTimePicker dtStop = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd HH:mm:ss", Width = 190, Enabled = false };
        Button btnRestore;
        List<BackupItem> items = new List<BackupItem>();
        List<BackupItem> chain = new List<BackupItem>();

        public RestorePage(MainForm m) : base(m, "還原資料庫")
        {
            // 來源
            var src = new Card("1. 選擇還原點");
            var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.White };
            var l1 = Theme.Lbl("備份資料夾"); l1.Location = new Point(0, 8);
            txtFolder.Font = Theme.F(9.5f); txtFolder.Location = new Point(86, 4); txtFolder.Width = 300;
            var browse = Theme.Btn("瀏覽…"); browse.Location = new Point(394, 1);
            var scan = Theme.Btn("掃描", true); scan.Location = new Point(browse.Right + 8, 1);
            var single = Theme.Btn("選擇單一備份檔…"); single.Location = new Point(scan.Right + 8, 1);
            var l2 = Theme.Lbl("資料庫"); l2.Location = new Point(single.Right + 24, 8);
            cbDb.Font = Theme.F(9.5f); cbDb.Location = new Point(l2.Left + 60, 4); cbDb.Width = 200;
            top.Controls.AddRange(new Control[] { l1, txtFolder, browse, scan, single, l2, cbDb });
            grid.Columns.AddRange(Theme.Col("資料庫", 16), Theme.Col("引擎", 11), Theme.Col("類型", 13), Theme.Col("備份時間", 16, 140), Theme.Col("大小", 9), Theme.Col("檔案", 40));
            Theme.StyleGrid(grid);
            grid.Dock = DockStyle.Fill;
            src.Controls.Add(grid); src.Controls.Add(top);

            browse.Click += (s, e) => { var p = Browse(txtFolder.Text); if (p != null) { txtFolder.Text = p; Scan(); } };
            scan.Click += (s, e) => Scan();
            single.Click += (s, e) => PickSingle();
            cbDb.SelectedIndexChanged += (s, e) => FillGrid();
            grid.SelectionChanged += (s, e) => BuildChain();

            // 設定
            var opt = new Card("2. 還原設定") { Height = 300 };
            var fl = new FieldLayout(opt, 18, 54, 120) { RowHeight = 34 };
            lblChain.Font = Theme.F(9.5f); lblChain.ForeColor = Theme.Text; lblChain.Height = 40;
            fl.Row("還原鏈", lblChain, 820);
            fl.Y += 6;
            fl.Row("還原為資料庫", txtTarget, 260, "可改成新名稱 (例如 MyDB_TEST) 還原成另一個資料庫，不影響原資料庫");
            int y0 = fl.Y;
            chkOverwrite = fl.Check("覆寫現有資料庫 (WITH REPLACE)", false);
            chkSafety = fl.Check("覆寫前先備份現有資料庫 (建議)", true);
            chkKill = fl.Check("強制中斷其他使用者連線", true);
            fl.X = 400; fl.Y = y0;
            chkStopAt = fl.Check("還原到指定時間點", false);
            dtStop.Font = Theme.F(9.5f); dtStop.Location = new Point(fl.X + 150, fl.Y - 31); opt.Controls.Add(dtStop);
            chkLeave = fl.Check("還原後保持 RESTORING (之後還要套用記錄)", false);
            chkTail = fl.Check("先做尾端記錄備份並一起還原 (Tail-log，還原到最新)", false);
            chkStopAt.CheckedChanged += (s, e) => dtStop.Enabled = chkStopAt.Checked;
            txtTarget.TextChanged += (s, e) => UpdateButton();
            btnRestore = Theme.Btn("開始還原", true, 140);
            btnRestore.BackColor = Theme.Danger; btnRestore.FlatAppearance.BorderColor = Theme.Danger; btnRestore.FlatAppearance.MouseOverBackColor = Color.FromArgb(170, 36, 22);
            btnRestore.Location = new Point(18, y0 + 104);
            btnRestore.Click += (s, e) => DoRestore();
            opt.Controls.Add(btnRestore);

            opt.Dock = DockStyle.Bottom;
            Controls.Add(src); src.Dock = DockStyle.Fill;
            Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 16 });
            Controls.Add(opt);
            UpdateButton();
        }

        public override void RefreshData()
        {
            if (txtFolder.Text == "")
            {
                var j = SettingsStore.Current.Jobs.FirstOrDefault();
                txtFolder.Text = j != null ? j.TargetPath : (Main.Server != null ? Main.Server.DefaultBackupPath : "");
            }
        }

        void Scan()
        {
            string folder = txtFolder.Text.Trim();
            if (!Directory.Exists(folder)) { MessageBox.Show("找不到資料夾：" + folder, "還原", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Cursor = Cursors.WaitCursor;
            Main.SetStatus("掃描備份檔…", -1);
            Task.Run(() =>
            {
                try
                {
                    var list = RestoreEngine.Scan(folder, msg => Main.SetStatus(msg, -1));
                    UI(() => { items = list; FillDbCombo(); Cursor = Cursors.Default; Main.SetStatus("找到 " + list.Count + " 個備份", 0); });
                }
                catch (Exception ex) { UI(() => { Cursor = Cursors.Default; Main.SetStatus("就緒", 0); MessageBox.Show(ex.Message, "掃描失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }); }
            });
        }

        void PickSingle()
        {
            using (var d = new OpenFileDialog { Filter = "SQL 備份檔 (*.bak;*.trn;*.zip)|*.bak;*.trn;*.zip|所有檔案 (*.*)|*.*", Title = "選擇備份檔" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                string f = d.FileName;
                Cursor = Cursors.WaitCursor;
                Task.Run(() =>
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(f);
                        var all = RestoreEngine.Scan(dir, null);
                        var mine = all.Where(x => x.File.Equals(f, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (mine.Count == 0 && !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) mine = RestoreEngine.FromHeaders(f);
                        UI(() =>
                        {
                            Cursor = Cursors.Default;
                            if (mine.Count == 0) { MessageBox.Show("無法辨識這個備份檔。", "還原", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                            txtFolder.Text = dir;
                            items = all.Count > 0 ? all : mine;
                            foreach (var x in mine) if (!items.Contains(x)) items.Add(x);
                            FillDbCombo(mine[0].Database);
                            foreach (DataGridViewRow r in grid.Rows)
                                if (((BackupItem)r.Tag).File.Equals(f, StringComparison.OrdinalIgnoreCase)) { grid.CurrentCell = r.Cells[0]; break; }
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
            var sel = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as BackupItem;
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
            UpdateButton();
        }

        void UpdateButton()
        {
            if (btnRestore != null) btnRestore.Enabled = chain.Count > 0 && txtTarget.Text.Trim() != "";
        }

        void DoRestore()
        {
            string db = txtTarget.Text.Trim();
            bool exists;
            var eng = chain[0].Engine;
            try
            {
                if (chain[0].Kind == BackupKind.Globals) exists = false;
                else exists = eng == DbEngine.SqlServer ? SqlOps.DatabaseExists(SettingsStore.Current.Connection, db) : eng == DbEngine.MySql ? MySqlTool.Exists(db) : PgTool.Exists(db);
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
            sb.AppendLine("即將還原資料庫：" + db + (exists ? "（將覆寫現有資料！）" : "（新資料庫）"));
            sb.AppendLine();
            foreach (var c in chain) sb.AppendLine("  " + c.KindText + "  " + c.Time.ToString("yyyy/MM/dd HH:mm:ss") + "  " + Path.GetFileName(c.File));
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
                SafetyBackupDir = txtFolder.Text.Trim()
            };
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
            ClientSize = new Size(560, 380);
            var txt = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Text = message.Replace("\n", "\r\n").Replace("\r\r", "\r"), Location = new Point(20, 20), Size = new Size(520, 220), ScrollBars = ScrollBars.Vertical, Font = Theme.F(9.5f) };
            Controls.Add(txt);
            var ok = Theme.Btn("確定還原", true, 110); ok.BackColor = Theme.Danger; ok.FlatAppearance.BorderColor = Theme.Danger;
            var cancel = Theme.Btn("取消", false, 90);
            ok.Location = new Point(330, 330); cancel.Location = new Point(450, 330);
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok; CancelButton = cancel;
            Controls.Add(ok); Controls.Add(cancel);
            if (requireText != null)
            {
                var l = Theme.Lbl("請輸入資料庫名稱「" + requireText + "」以確認覆寫：", 9.5f, FontStyle.Bold, Theme.Danger);
                l.Location = new Point(20, 256);
                var input = new TextBox { Location = new Point(20, 284), Width = 300, Font = Theme.F(10f) };
                Controls.Add(l); Controls.Add(input);
                ok.Enabled = false;
                input.TextChanged += (s, e) => ok.Enabled = input.Text == requireText;
            }
        }
    }

    // ───────────────────────── 備份日誌 ─────────────────────────
    public class LogPage : Page
    {
        DateTimePicker dtFrom = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd", Width = 120 };
        DateTimePicker dtTo = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd", Width = 120 };
        ComboBox cbStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        TextBox txtSearch = new TextBox { Width = 180 };
        DataGridView grid = new DataGridView();
        Label lblCount = new Label { AutoSize = true };
        List<LogEntry> current = new List<LogEntry>();

        public LogPage(MainForm m) : base(m, "備份日誌")
        {
            dtFrom.Value = DateTime.Today.AddDays(-7); dtTo.Value = DateTime.Today;
            cbStatus.Items.AddRange(new object[] { "全部", LogEntry.Success, LogEntry.Failed, LogEntry.Warning, LogEntry.Info });
            cbStatus.SelectedIndex = 0;
            var query = Theme.Btn("查詢", true);
            var export = Theme.Btn("匯出 CSV");
            var open = Theme.Btn("開啟日誌資料夾");
            query.Click += (s, e) => RefreshData();
            txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RefreshData(); } };
            export.Click += (s, e) => Export();
            open.Click += (s, e) => Process.Start("explorer.exe", AppPaths.LogDir);

            var bar = new Card { Height = 64 };
            int x = 18;
            Action<Control> place = c => { c.Font = Theme.F(9.5f); c.Location = new Point(x, c is Label ? 22 : c is Button ? 15 : 18); bar.Controls.Add(c); x += c.Width + (c is Label ? 4 : 14); };
            foreach (var c in new Control[] { Fixed("日期"), dtFrom, Fixed("至"), dtTo, Fixed("狀態"), cbStatus, Fixed("關鍵字"), txtSearch, query, export, open }) place(c);
            lblCount.Font = Theme.F(9f); lblCount.ForeColor = Theme.SubText;

            grid.Columns.AddRange(Theme.Col("時間", 13, 155), Theme.Col("工作", 12), Theme.Col("資料庫", 11), Theme.Col("類型", 9), Theme.Col("狀態", 6, 56), Theme.Col("大小", 7), Theme.Col("耗時", 7), Theme.Col("訊息", 25), Theme.Col("檔案", 20));
            ColorStatus(grid, 4);
            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var l = grid.Rows[e.RowIndex].Tag as LogEntry;
                if (l == null) return;
                MessageBox.Show(string.Format("時間：{0:yyyy/MM/dd HH:mm:ss}\n工作：{1}\n資料庫：{2}\n類型：{3}\n狀態：{4}\n檔案：{5}\n大小：{6}\n耗時：{7}\n\n{8}",
                    l.Time, l.Job, l.Database, l.Kind, l.Status, l.File, Fmt.Size(l.SizeBytes), Fmt.Duration(l.DurationSec), l.Message), "日誌明細");
            };
            Stack(bar, lblCount, GridCard(null, grid));
        }

        static Label Fixed(string t) { var l = Theme.Lbl(t); l.AutoSize = false; l.Width = TextRenderer.MeasureText(t, Theme.F(9.5f)).Width + 2; l.Height = 22; return l; }

        public override void RefreshData()
        {
            var list = Logger.Read(dtFrom.Value.Date, dtTo.Value.Date.AddDays(1).AddSeconds(-1));
            string st = cbStatus.SelectedIndex > 0 ? (string)cbStatus.SelectedItem : null;
            string q = txtSearch.Text.Trim();
            current = list.Where(l => (st == null || l.Status == st) &&
                (q == "" || (l.Job + l.Database + l.Message + l.File + l.Kind).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            grid.SuspendLayout();
            grid.Rows.Clear();
            foreach (var l in current.Take(5000))
            {
                int r = grid.Rows.Add(l.Time.ToString("yyyy/MM/dd HH:mm:ss"), l.Job, l.Database, l.Kind, l.Status, Fmt.Size(l.SizeBytes), Fmt.Duration(l.DurationSec), l.Message, l.File);
                grid.Rows[r].Tag = l;
            }
            grid.ResumeLayout();
            lblCount.Text = string.Format("共 {0} 筆　成功 {1}　失敗 {2}　警告 {3}{4}", current.Count, current.Count(l => l.Status == LogEntry.Success),
                current.Count(l => l.Status == LogEntry.Failed), current.Count(l => l.Status == LogEntry.Warning), current.Count > 5000 ? "（僅顯示前 5000 筆）" : "");
        }

        void Export()
        {
            using (var d = new SaveFileDialog { Filter = "CSV 檔 (*.csv)|*.csv", FileName = "備份日誌_" + DateTime.Now.ToString("yyyyMMdd") + ".csv" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                Func<string, string> c = s => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
                var sb = new StringBuilder("時間,工作,資料庫,類型,狀態,檔案,大小(Bytes),耗時(秒),訊息\r\n");
                foreach (var l in current)
                    sb.Append(string.Join(",", c(l.Time.ToString("yyyy/MM/dd HH:mm:ss")), c(l.Job), c(l.Database), c(l.Kind), c(l.Status), c(l.File), l.SizeBytes, l.DurationSec.ToString("0.0"), c(l.Message))).Append("\r\n");
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                Main.SetStatus("已匯出 " + current.Count + " 筆到 " + d.FileName, 0);
            }
        }
    }

    // ───────────────────────── 連線設定 ─────────────────────────
    public class ConnectionPage : Page
    {
        ComboBox cbServer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        RadioButton rbWin = new RadioButton { Text = "Windows 驗證", AutoSize = true };
        RadioButton rbSql = new RadioButton { Text = "SQL Server 驗證", AutoSize = true };
        TextBox txtUser = new TextBox();
        TextBox txtPwd = new TextBox { UseSystemPasswordChar = true };
        NumericUpDown numTimeout = new NumericUpDown { Minimum = 5, Maximum = 120 };
        Label lblResult = new Label { AutoSize = false };
        TextBox txtPkg = new TextBox();
        TextBox[] tHost = { new TextBox(), new TextBox() }, tUser = { new TextBox(), new TextBox() }, tPwd = { new TextBox { UseSystemPasswordChar = true }, new TextBox { UseSystemPasswordChar = true } }, tBin = { new TextBox(), new TextBox() };
        NumericUpDown[] tPort = { new NumericUpDown { Minimum = 1, Maximum = 65535 }, new NumericUpDown { Minimum = 1, Maximum = 65535 } };
        Label[] tResult = { new Label { AutoSize = false }, new Label { AutoSize = false } };

        public ConnectionPage(MainForm m) : base(m, "連線設定")
        {
            var card = new Card("SQL Server 連線") { AutoScroll = true };
            var fl = new FieldLayout(card, 18, 60);
            fl.Row("伺服器執行個體", cbServer, 300);
            var detect = Theme.Btn("偵測本機執行個體"); detect.Location = new Point(cbServer.Right + 10, cbServer.Top - 2); card.Controls.Add(detect);
            var authPanel = new FlowLayoutPanel { Height = 28, BackColor = Color.White, WrapContents = false };
            rbWin.Font = rbSql.Font = Theme.F(9.5f); authPanel.Controls.Add(rbWin); authPanel.Controls.Add(rbSql);
            fl.Row("驗證方式", authPanel, 320);
            fl.Row("帳號", txtUser, 220);
            fl.Row("密碼", txtPwd, 220, "以 DPAPI 加密儲存於本機");
            fl.Row("連線逾時 (秒)", numTimeout, 80);
            fl.Row("SqlPackage 位置", txtPkg, 420, "BACPAC 用，空白 = 自動尋找");
            var test = Theme.Btn("測試連線"); var save = Theme.Btn("儲存", true);
            fl.Buttons(save, test);
            lblResult.Font = Theme.F(9.5f); lblResult.Size = new Size(760, 60);
            fl.Row(null, lblResult, 760);
            fl.Y += 30;
            fl.Section("注意事項");
            fl.Note("• SQL Server Express 版沒有 SQL Agent，本程式就是用來取代 Agent 的排程備份功能，必須保持執行（可縮小到右下角）。\n" +
                    "• 備份檔是由「SQL Server 服務帳戶」寫入磁碟，不是目前登入的 Windows 帳號。請確認服務帳戶對備份資料夾有「修改」權限。\n" +
                    "• 備份到網路路徑 (\\\\NAS\\Backup) 時，服務帳戶需改用網域帳戶，或改用「第二備份位置」由本程式複製過去。\n" +
                    "• 使用的帳號需具備 sysadmin，或至少 db_backupoperator 權限；還原則需要 dbcreator / sysadmin。", 780);

            for (int i = 0; i < 2; i++)
            {
                int k = i;
                fl.Y += 10;
                fl.Section(i == 0 ? "MySQL / MariaDB（選用）" : "PostgreSQL（選用）");
                fl.Row("主機", tHost[i], 220);
                fl.Row("連接埠", tPort[i], 90);
                fl.Row("帳號", tUser[i], 220);
                fl.Row("密碼", tPwd[i], 220, "以 DPAPI 加密儲存於本機");
                fl.Row("工具資料夾", tBin[i], 420, i == 0 ? "mysqldump.exe 所在資料夾，空白 = 自動尋找" : "pg_dump.exe 所在資料夾，空白 = 自動尋找");
                var tb = Theme.Btn("測試連線"); var sb2 = Theme.Btn("儲存", true);
                fl.Buttons(sb2, tb);
                tResult[i].Font = Theme.F(9.5f); fl.Row(null, tResult[i], 760); tResult[i].Height = 44;
                tb.Click += (s, e) => TestTool(k);
                sb2.Click += (s, e) => { CollectTool(k); SettingsStore.Save(); Main.SetStatus("連線設定已儲存", 0); };
            }

            rbWin.CheckedChanged += (s, e) => { txtUser.Enabled = txtPwd.Enabled = rbSql.Checked; };
            detect.Click += (s, e) =>
            {
                var list = SqlOps.DetectLocalInstances();
                cbServer.Items.Clear(); foreach (var i in list) cbServer.Items.Add(i);
                if (list.Count > 0) { cbServer.DroppedDown = true; }
                else MessageBox.Show("本機找不到 SQL Server 執行個體，請手動輸入 (例如 SERVER\\SQLEXPRESS)。", "偵測", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            test.Click += (s, e) => Test(Collect());
            save.Click += (s, e) =>
            {
                SettingsStore.Current.Connection = Collect();
                SettingsStore.Save();
                Main.SetStatus("連線設定已儲存", 0);
                Main.RefreshServerInfo();
            };
            Stack(card);
        }

        ConnectionSettings Collect()
        {
            var c = new ConnectionSettings { Server = cbServer.Text.Trim(), UseWindowsAuth = rbWin.Checked, User = txtUser.Text.Trim(), ConnectTimeoutSec = (int)numTimeout.Value, SqlPackagePath = txtPkg.Text.Trim() };
            c.Password = txtPwd.Text;
            return c;
        }

        void Test(ConnectionSettings c)
        {
            lblResult.ForeColor = Theme.SubText; lblResult.Text = "連線中…";
            Task.Run(() =>
            {
                try
                {
                    var si = SqlOps.GetServerInfo(c);
                    var dbs = SqlOps.GetDatabases(c);
                    UI(() =>
                    {
                        lblResult.ForeColor = Theme.Success;
                        lblResult.Text = "✔ 連線成功\n" + si.Summary + (si.IsExpress ? "（Express 版：不支援原生壓縮，可用 ZIP 壓縮）" : "") +
                            "\n資料庫 " + dbs.Count + " 個；服務帳戶：" + si.ServiceAccount;
                    });
                }
                catch (Exception ex) { UI(() => { lblResult.ForeColor = Theme.Danger; lblResult.Text = "✖ 連線失敗\n" + ex.Message; }); }
            });
        }

        public override void RefreshData()
        {
            var c = SettingsStore.Current.Connection;
            cbServer.Text = c.Server; rbWin.Checked = c.UseWindowsAuth; rbSql.Checked = !c.UseWindowsAuth;
            txtUser.Text = c.User; txtPwd.Text = c.Password; numTimeout.Value = Math.Max(5, Math.Min(120, c.ConnectTimeoutSec));
            txtUser.Enabled = txtPwd.Enabled = !c.UseWindowsAuth;
            txtPkg.Text = c.SqlPackagePath;
            var ts = new[] { SettingsStore.Current.MySql, SettingsStore.Current.PgSql };
            for (int i = 0; i < 2; i++)
            {
                tHost[i].Text = ts[i].Host; tPort[i].Value = Math.Max(1, Math.Min(65535, ts[i].Port)); tUser[i].Text = ts[i].User; tPwd[i].Text = ts[i].Password; tBin[i].Text = ts[i].BinDir;
            }
        }

        void CollectTool(int i)
        {
            var t = new ToolConnection { Host = tHost[i].Text.Trim(), Port = (int)tPort[i].Value, User = tUser[i].Text.Trim(), BinDir = tBin[i].Text.Trim() };
            t.Password = tPwd[i].Text;
            if (i == 0) SettingsStore.Current.MySql = t; else SettingsStore.Current.PgSql = t;
        }

        void TestTool(int i)
        {
            CollectTool(i);
            tResult[i].ForeColor = Theme.SubText; tResult[i].Text = "連線中…";
            Task.Run(() =>
            {
                try
                {
                    string v = i == 0 ? MySqlTool.Version() : PgTool.Version();
                    int n = (i == 0 ? MySqlTool.Databases() : PgTool.Databases()).Count;
                    string exe = i == 0 ? MySqlTool.Exe("mysqldump.exe") : PgTool.Exe("pg_dump.exe");
                    UI(() => { tResult[i].ForeColor = Theme.Success; tResult[i].Text = "✔ 連線成功：" + v + "，資料庫 " + n + " 個\n工具：" + exe; });
                }
                catch (Exception ex) { UI(() => { tResult[i].ForeColor = Theme.Danger; tResult[i].Text = "✖ " + ex.Message; }); }
            });
        }
    }

    // ───────────────────────── 通知設定 ─────────────────────────
    public class NotifyPage : Page
    {
        CheckBox chkEnabled;
        TextBox txtHost = new TextBox(), txtUser = new TextBox(), txtPwd = new TextBox { UseSystemPasswordChar = true }, txtFrom = new TextBox(), txtTo = new TextBox();
        NumericUpDown numPort = new NumericUpDown { Minimum = 1, Maximum = 65535 };
        CheckBox chkSsl, chkSuccess, chkFail;

        public NotifyPage(MainForm m) : base(m, "通知設定")
        {
            var card = new Card("Email 通知") { AutoScroll = true };
            var fl = new FieldLayout(card, 18, 60);
            chkEnabled = fl.Check("啟用 Email 通知", false);
            fl.Y += 4;
            fl.Row("SMTP 伺服器", txtHost, 260, "例如 smtp.gmail.com、smtp.office365.com");
            fl.Row("連接埠", numPort, 90, "587 (STARTTLS) / 465 / 25");
            chkSsl = fl.Check("使用 SSL / TLS 加密", true, 140);
            fl.Row("SMTP 帳號", txtUser, 260);
            fl.Row("SMTP 密碼", txtPwd, 260, "Gmail 請使用「應用程式密碼」");
            fl.Row("寄件者", txtFrom, 260, "空白 = 使用 SMTP 帳號");
            fl.Row("收件者", txtTo, 400, "多人用 ; 分隔");
            fl.Section("何時寄送");
            chkFail = fl.Check("備份失敗或有錯誤時", true);
            chkSuccess = fl.Check("備份成功時也寄送", false);
            var test = Theme.Btn("寄送測試信"); var save = Theme.Btn("儲存", true);
            fl.Y += 8;
            fl.Buttons(save, test);
            save.Click += (s, e) => { Collect(); SettingsStore.Save(); Main.SetStatus("通知設定已儲存", 0); };
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
            Stack(card);
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
            txtUser.Text = mm.User; txtPwd.Text = mm.Password; txtFrom.Text = mm.From; txtTo.Text = mm.To;
            chkSuccess.Checked = mm.NotifyOnSuccess; chkFail.Checked = mm.NotifyOnFailure;
        }
    }

    // ───────────────────────── 系統設定 ─────────────────────────
    public class SystemPage : Page
    {
        CheckBox chkStart, chkMin, chkClose, chkMissed, chkBalloon;
        NumericUpDown numLog = new NumericUpDown { Minimum = 0, Maximum = 3650 };

        public SystemPage(MainForm m) : base(m, "系統設定")
        {
            var card = new Card("程式行為") { AutoScroll = true };
            var fl = new FieldLayout(card, 18, 60);
            chkStart = fl.Check("Windows 登入後自動啟動", false);
            chkMin = fl.Check("啟動時直接縮小到右下角系統匣", false);
            chkClose = fl.Check("按視窗關閉鈕 (X) 時縮小到系統匣，而不是結束程式", true);
            chkMissed = fl.Check("程式未執行而錯過排程時，啟動後自動補做備份", true);
            chkBalloon = fl.Check("備份完成時顯示右下角通知", true);
            fl.Y += 6;
            fl.Row("日誌保留天數", numLog, 90, "0 = 永久保留");
            var save = Theme.Btn("儲存", true);
            fl.Buttons(save);

            fl.Section("搭配 Windows 工作排程器（建議）");
            fl.Note("Server 重新開機後若沒有人登入，本程式不會啟動。建議在「工作排程器」另外建立一個保險工作，" +
                    "設定「不論使用者登入與否均執行」，執行下列命令（無畫面、執行完即結束，結果一樣寫入日誌）：", 780);
            var cmd = new TextBox { ReadOnly = true, Font = new Font("Consolas", 10f), BackColor = Color.FromArgb(246, 248, 250), Text = "\"" + Application.ExecutablePath + "\" --run \"工作名稱\"" };
            fl.Row(null, cmd, 640);
            var copy = Theme.Btn("複製命令"); copy.Location = new Point(cmd.Right + 10, cmd.Top - 3); card.Controls.Add(copy);
            copy.Click += (s, e) => { Clipboard.SetText(cmd.Text); Main.SetStatus("已複製到剪貼簿", 0); };
            fl.Note("其他參數：--run-all 執行所有啟用的工作；--minimized 啟動後縮小到系統匣。", 780);

            fl.Section("資料位置");
            fl.Note("設定檔與日誌：" + AppPaths.DataDir, 780, Theme.Text);
            var open = Theme.Btn("開啟資料夾");
            fl.Buttons(open);
            open.Click += (s, e) => Process.Start("explorer.exe", AppPaths.DataDir);
            fl.Note("SQL 備份管理  版本 " + Application.ProductVersion + "　|　.NET Framework 4.8　|　SQL Server 2008 R2 ~ 2022（含 Express）・MySQL / MariaDB・PostgreSQL", 780);

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
            Stack(card);
        }

        public override void RefreshData()
        {
            var g = SettingsStore.Current.General;
            chkStart.Checked = g.StartWithWindows; chkMin.Checked = g.StartMinimized; chkClose.Checked = g.MinimizeToTrayOnClose;
            chkMissed.Checked = g.RunMissedJobs; chkBalloon.Checked = g.ShowBalloon; numLog.Value = Math.Max(0, Math.Min(3650, g.LogRetentionDays));
        }
    }
}
