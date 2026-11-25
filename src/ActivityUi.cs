using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SqlBackupManager
{
    /// <summary>扁平進度條 (可顯示文字)。</summary>
    public class ProgBar : Control
    {
        int value;
        public Color Fill = Theme.Accent;
        public bool Marquee;
        int marq;
        readonly Timer t = new Timer { Interval = 40 };
        public ProgBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(10);
            t.Tick += (s, e) => { if (Marquee) { marq = (marq + Theme.S(6)) % Math.Max(1, Width + Width / 3); Invalidate(); } };
            t.Start();
        }
        public int Value { get { return value; } set { int v = Math.Max(0, Math.Min(100, value)); if (v != this.value) { this.value = v; Invalidate(); } } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            using (var b = new SolidBrush(Color.FromArgb(224, 230, 238))) g.FillRectangle(b, 0, 0, Width, Height);
            using (var b = new SolidBrush(Fill))
            {
                if (Marquee) { int w = Width / 3; g.FillRectangle(b, marq - w, 0, w, Height); }
                else g.FillRectangle(b, 0, 0, (int)(Width * value / 100.0), Height);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) t.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>主視窗上方的「正在執行」狀態列；每一頁都看得到，點一下開啟明細。</summary>
    public class ActivityBanner : Panel
    {
        readonly Label lbl = new Label { AutoSize = false, UseMnemonic = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand };
        readonly ToolTip tip = new ToolTip();
        readonly Label pct = new Label { AutoSize = false, UseMnemonic = false, TextAlign = ContentAlignment.MiddleRight, Cursor = Cursors.Hand };
        readonly ProgBar bar = new ProgBar { Cursor = Cursors.Hand };
        readonly Button btn = Theme.Btn("查看明細");
        readonly Timer t = new Timer { Interval = 500 };
        bool wasRunning;
        DateTime hideAt = DateTime.MinValue;
        public event Action OpenDetails;

        public ActivityBanner()
        {
            Dock = DockStyle.Top; Visible = false; Cursor = Cursors.Hand;
            lbl.Font = Theme.F(10f, FontStyle.Bold); pct.Font = Theme.F(10f, FontStyle.Bold);
            int lh = TextRenderer.MeasureText("國", lbl.Font).Height;
            Height = Math.Max(btn.Height + Theme.S(14), lh + Theme.S(24)) + Theme.S(10);
            Controls.AddRange(new Control[] { lbl, pct, bar, btn });
            foreach (Control c in new Control[] { this, lbl, pct, bar }) c.Click += (s, e) => Raise();
            btn.Click += (s, e) => Raise();
            Resize += (s, e) => Lay();
            t.Tick += (s, e) => Tick();
            t.Start();
            Lay();
        }

        void Raise() { var h = OpenDetails; if (h != null) h(); }

        void Lay()
        {
            int pad = Theme.S(24), h = Height - Theme.S(10);
            btn.Location = new Point(Width - pad - btn.Width, (h - btn.Height) / 2);
            // 文字優先：剩餘寬度不夠時縮短進度條 (最短 60)
            int free = btn.Left - pad - Theme.S(12) - Theme.S(56) - Theme.S(24);
            int barW = Math.Max(Theme.S(60), Math.Min(Theme.S(220), free - Theme.S(380)));
            pct.Size = new Size(Theme.S(56), h); pct.Location = new Point(btn.Left - Theme.S(12) - pct.Width, 0);
            bar.Size = new Size(barW, Theme.S(10)); bar.Location = new Point(pct.Left - Theme.S(8) - barW, (h - bar.Height) / 2);
            // 單行顯示，太長用「…」省略 (不折行)
            int lh = TextRenderer.MeasureText("國", lbl.Font).Height + Theme.S(2);
            lbl.Size = new Size(Math.Max(10, bar.Left - pad - Theme.S(16)), lh);
            lbl.Location = new Point(pad, (h - lh) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, Height - Theme.S(10) - 1, Width, Height - Theme.S(10) - 1);
            using (var b = new SolidBrush(Theme.Page)) e.Graphics.FillRectangle(b, 0, Height - Theme.S(10), Width, Theme.S(10));
        }

        void Tick()
        {
            if (Activity.Running)
            {
                wasRunning = true;
                BackColor = Color.FromArgb(232, 242, 253);
                lbl.ForeColor = Theme.Accent; pct.ForeColor = Theme.Accent; bar.Fill = Theme.Accent;
                lbl.Text = "● 執行中　" + Activity.Title + "　｜　" + Activity.CurrentText + (Activity.UnitText != "" ? "　（" + Activity.UnitText + "）" : "");
                tip.SetToolTip(lbl, lbl.Text);
                int o = Activity.Overall;
                bar.Marquee = false; bar.Value = o; pct.Text = o + "%";
                if (!Visible) Visible = true;
            }
            else if (wasRunning)
            {
                // 執行完畢：顯示結果 20 秒
                wasRunning = false;
                hideAt = DateTime.Now.AddSeconds(20);
                bool ok = Activity.Result != LogEntry.Failed;
                BackColor = ok ? Color.FromArgb(232, 246, 234) : Color.FromArgb(253, 236, 234);
                var c = ok ? Theme.Success : Theme.Danger;
                lbl.ForeColor = pct.ForeColor = c; bar.Fill = c;
                lbl.Text = (ok ? "✔ 完成　" : "✖ 有錯誤　") + Activity.Title + "　｜　" + Activity.Summary + "　（" + Activity.End.ToString("HH:mm:ss") + "，耗時 " + Fmt.Duration((Activity.End - Activity.Start).TotalSeconds) + "）";
                bar.Value = 100; pct.Text = "100%";
                Visible = true;
            }
            else if (Visible && DateTime.Now > hideAt) Visible = false;
        }

        protected override void Dispose(bool disposing) { if (disposing) t.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>執行明細：整體進度、目前步驟進度、每一個步驟的時間與狀態。</summary>
    public class ActivityForm : Form
    {
        readonly Label lblTitle = Theme.Lbl("", 13f, FontStyle.Bold);
        readonly Label lblOverall = Theme.Lbl("", 10f, FontStyle.Bold);
        readonly Label lblCurrent = new Label { AutoSize = false, UseMnemonic = false, AutoEllipsis = true };
        readonly Label lblTime = Theme.Lbl("", 9f, FontStyle.Regular, Theme.SubText);
        readonly ProgBar barAll = new ProgBar(), barCur = new ProgBar();
        readonly DataGridView grid = new DataGridView();
        readonly Timer t = new Timer { Interval = 500 };
        int lastCount = -1;

        public ActivityForm()
        {
            Text = "執行明細"; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            BackColor = Color.White; Font = Theme.F(9.5f); Icon = Theme.MakeIcon(Color.Empty);
            ClientSize = Theme.S(760, 560); MinimumSize = Theme.S(560, 420);

            var top = new Panel { Dock = DockStyle.Top, BackColor = Color.White, Padding = new Padding(Theme.S(20), Theme.S(16), Theme.S(20), Theme.S(8)) };
            lblCurrent.Font = Theme.F(9.5f);
            int y = Theme.S(16), x = Theme.S(20);
            lblTitle.Location = new Point(x, y); top.Controls.Add(lblTitle); y += TextRenderer.MeasureText("國", lblTitle.Font).Height + Theme.S(12);
            var l1 = Theme.Lbl("整體進度"); l1.Location = new Point(x, y); top.Controls.Add(l1);
            lblOverall.Location = new Point(x + Theme.S(90), y); top.Controls.Add(lblOverall);
            y += TextRenderer.MeasureText("國", lblOverall.Font).Height + Theme.S(6);
            barAll.Location = new Point(x, y); barAll.Height = Theme.S(12); top.Controls.Add(barAll); y += barAll.Height + Theme.S(16);
            var l2 = Theme.Lbl("目前步驟"); l2.Location = new Point(x, y); top.Controls.Add(l2);
            lblCurrent.Location = new Point(x + Theme.S(90), y); lblCurrent.Height = TextRenderer.MeasureText("國", lblCurrent.Font).Height + Theme.S(2); top.Controls.Add(lblCurrent);
            y += lblCurrent.Height + Theme.S(6);
            barCur.Location = new Point(x, y); barCur.Height = Theme.S(8); top.Controls.Add(barCur); y += barCur.Height + Theme.S(12);
            lblTime.Location = new Point(x, y); top.Controls.Add(lblTime); y += TextRenderer.MeasureText("國", lblTime.Font).Height + Theme.S(10);
            top.Height = y;
            top.Resize += (s, e) => { int w = top.Width - Theme.S(40); barAll.Width = w; barCur.Width = w; lblCurrent.Width = w - Theme.S(90); };

            var card = new Card("步驟明細") { Dock = DockStyle.Fill };
            grid.Columns.AddRange(Theme.Col("時間", 14, 90), Theme.Col("步驟", 66), Theme.Col("狀態", 20, 90));
            Theme.StyleGrid(grid); grid.Dock = DockStyle.Fill;
            card.Body.Controls.Add(grid);
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(20), 0, Theme.S(20), Theme.S(16)), BackColor = Color.White };
            host.Controls.Add(card);
            Controls.Add(host); Controls.Add(top);

            grid.CellFormatting += (s, e) =>
            {
                if (e.ColumnIndex != 2 || e.Value == null) return;
                string v = e.Value.ToString();
                e.CellStyle.ForeColor = v.StartsWith("✔") ? Theme.Success : v.StartsWith("✖") ? Theme.Danger : Theme.Accent;
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
            };
            t.Tick += (s, e) => Refresh2();
            t.Start();
            Shown += (s, e) => Refresh2();
        }

        void Refresh2()
        {
            var steps = Activity.Snapshot();
            if (Activity.Title == "") { lblTitle.Text = "目前沒有執行中的作業"; return; }
            bool run = Activity.Running;
            bool failed = !run && Activity.Result == LogEntry.Failed;
            lblTitle.Text = (run ? "執行中：" : failed ? "已結束（有錯誤）：" : "已完成：") + Activity.Title;
            lblTitle.ForeColor = run ? Theme.Text : failed ? Theme.Danger : Theme.Success;
            int o = Activity.Overall;
            barAll.Value = o; barAll.Fill = failed ? Theme.Danger : run ? Theme.Accent : Theme.Success;
            lblOverall.Text = o + "%" + (run ? "　" + Activity.UnitText : "");
            var cur = steps.LastOrDefault();
            lblCurrent.Text = cur == null ? "" : cur.Text + (cur.Percent >= 0 && !cur.Done ? "　" + cur.Percent + "%" : "");
            barCur.Marquee = run && cur != null && cur.Percent < 0;
            barCur.Value = cur == null ? 0 : cur.Done ? 100 : Math.Max(0, cur.Percent);
            var end = run ? DateTime.Now : Activity.End;
            lblTime.Text = "開始 " + Activity.Start.ToString("HH:mm:ss") + "　已耗時 " + Fmt.Duration(Math.Max(0.1, (end - Activity.Start).TotalSeconds)) + (run ? "" : "　結束 " + Activity.End.ToString("HH:mm:ss"));

            // 只在步驟數改變時重建，其餘只更新最後一列
            if (steps.Count != lastCount)
            {
                grid.Rows.Clear();
                foreach (var s in steps) grid.Rows.Add(s.Time.ToString("HH:mm:ss"), s.Text, StatusOf(s, run, failed));
                lastCount = steps.Count;
                if (grid.Rows.Count > 0) grid.FirstDisplayedScrollingRowIndex = grid.Rows.Count - 1;
            }
            else if (steps.Count > 0)
            {
                grid.Rows[steps.Count - 1].Cells[2].Value = StatusOf(steps.Last(), run, failed);
                for (int i = 0; i < steps.Count - 1; i++) grid.Rows[i].Cells[2].Value = StatusOf(steps[i], true, false);
            }
        }

        static string StatusOf(ActivityStep s, bool run, bool failed)
        {
            if (!s.Done) return s.Percent >= 0 ? "進行中 " + s.Percent + "%" : "進行中…";
            if (s.Text.StartsWith("結束（有錯誤）")) return "✖ 有錯誤";
            if (s.Text.StartsWith("✖")) return "✖ 失敗";
            return "✔ 完成";
        }

        protected override void Dispose(bool disposing) { if (disposing) t.Dispose(); base.Dispose(disposing); }
    }
}
