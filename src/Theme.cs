using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace SqlBackupManager
{
    /// <summary>
    /// 介面樣式。所有尺寸都經過 S() 依字體大小等比縮放，
    /// 所以在「系統設定」調大字體時，間距、列高、欄寬會一起放大，文字不會互相重疊。
    /// </summary>
    public static class Theme
    {
        public static readonly Color Sidebar = Color.FromArgb(24, 33, 48);
        public static readonly Color SidebarHover = Color.FromArgb(36, 48, 68);
        public static readonly Color SidebarActive = Color.FromArgb(42, 57, 82);
        public static readonly Color SidebarText = Color.FromArgb(196, 205, 218);
        public static readonly Color Accent = Color.FromArgb(0, 103, 192);
        public static readonly Color AccentHover = Color.FromArgb(0, 86, 160);
        public static readonly Color Page = Color.FromArgb(243, 245, 248);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(222, 226, 232);
        public static readonly Color Text = Color.FromArgb(32, 36, 44);
        public static readonly Color SubText = Color.FromArgb(104, 112, 124);
        public static readonly Color Success = Color.FromArgb(16, 124, 16);
        public static readonly Color Danger = Color.FromArgb(196, 43, 28);
        public static readonly Color Warning = Color.FromArgb(202, 120, 0);
        public static readonly Color InfoC = Color.FromArgb(0, 103, 192);

        public const float BaseSize = 9.5f;
        static float k = 1f;

        /// <summary>縮放比例 = 使用者字體大小 / 9.5pt (另外乘上字型本身的行高差異)。</summary>
        public static float K { get { return k; } }

        public static void Init(float fontSize)
        {
            fontSize = Math.Max(8f, Math.Min(18f, fontSize <= 0 ? 10f : fontSize));
            SettingsFont = fontSize;
            k = fontSize / BaseSize;
            // 中文字型 (微軟正黑體) 行高比 Segoe UI 高，依實際量測再補償
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font(Family, fontSize))
            {
                float h = TextRenderer.MeasureText(g, "國Ag", f).Height;
                float expected = 17f * k;   // 9.5pt Segoe UI 在 96 DPI 約 17px
                if (h > expected) k *= h / expected;
            }
        }

        public static int S(int px) { return (int)Math.Round(px * k); }
        public static Size S(int w, int h) { return new Size(S(w), S(h)); }
        public static Point P(int x, int y) { return new Point(S(x), S(y)); }

        static string family;
        public static string Family
        {
            get
            {
                if (family == null)
                {
                    family = "Segoe UI";
                    foreach (var f in FontFamily.Families)
                        if (f.Name == "Microsoft JhengHei UI") { family = f.Name; break; }
                }
                return family;
            }
        }

        static bool? hasIcons;
        public static bool HasIconFont
        {
            get
            {
                if (!hasIcons.HasValue)
                {
                    hasIcons = false;
                    foreach (var f in FontFamily.Families) if (f.Name == "Segoe MDL2 Assets") { hasIcons = true; break; }
                }
                return hasIcons.Value;
            }
        }

        /// <summary>字體：以 9.5pt 為基準等比放大。</summary>
        public static Font F(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(Family, size * (SettingsFont / BaseSize), style);
        }
        public static float SettingsFont = 10f;

        public static Font IconFont(float size) { return new Font("Segoe MDL2 Assets", size * (SettingsFont / BaseSize)); }

        public static Color StatusColor(string s)
        {
            if (s == LogEntry.Success) return Success;
            if (s == LogEntry.Failed) return Danger;
            if (s == LogEntry.Warning) return Warning;
            return InfoC;
        }

        public static Button Btn(string text, bool primary = false, int width = 0)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = F(9.5f),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Text,
                Margin = new Padding(0, 0, S(8), 0)
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(240, 244, 249);
            b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(0, 70, 135) : Color.FromArgb(228, 234, 242);
            var sz = TextRenderer.MeasureText(text, b.Font);
            b.Height = Math.Max(S(32), sz.Height + S(14));
            b.Width = Math.Max(width > 0 ? S(width) : S(84), sz.Width + S(28));
            return b;
        }

        public static void Danger_(Button b)
        {
            b.BackColor = Danger; b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Danger; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(170, 36, 22);
        }

        public static Label Lbl(string text, float size = 9.5f, FontStyle st = FontStyle.Regular, Color? color = null)
        {
            return new Label { Text = text, AutoSize = true, Font = F(size, st), ForeColor = color ?? Text, BackColor = Color.Transparent, UseMnemonic = false };
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BorderStyle = BorderStyle.None;
            g.BackgroundColor = Color.White;
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.ReadOnly = true;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.GridColor = Color.FromArgb(235, 238, 242);
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 249, 251);
            g.ColumnHeadersDefaultCellStyle.ForeColor = SubText;
            g.ColumnHeadersDefaultCellStyle.Font = F(9f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(247, 249, 251);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(S(6), 0, 0, 0);
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.DefaultCellStyle.Font = F(9.5f);
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 238, 252);
            g.DefaultCellStyle.SelectionForeColor = Text;
            g.DefaultCellStyle.Padding = new Padding(S(6), 0, 0, 0);
            g.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            int lh = TextRenderer.MeasureText("國Ag", g.DefaultCellStyle.Font).Height;
            g.ColumnHeadersHeight = Math.Max(S(36), lh + S(16));
            g.RowTemplate.Height = Math.Max(S(32), lh + S(14));
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.ShowCellToolTips = true;
            typeof(DataGridView).InvokeMember("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, g, new object[] { true });
        }

        public static DataGridViewTextBoxColumn Col(string header, float weight, int minWidth = 60)
        {
            return new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = weight, MinimumWidth = S(minWidth), SortMode = DataGridViewColumnSortMode.NotSortable };
        }

        public static void DoubleBuffer(Control c)
        {
            typeof(Control).InvokeMember("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, c, new object[] { true });
        }

        /// <summary>產生程式圖示 (藍底白色資料庫圖案)。</summary>
        public static Icon MakeIcon(Color badge)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = RoundRect(new Rectangle(1, 1, 30, 30), 7))
                    using (var br = new SolidBrush(Accent)) g.FillPath(br, path);
                    using (var w = new SolidBrush(Color.White))
                    using (var p = new Pen(Accent, 1.6f))
                    {
                        g.FillRectangle(w, 9, 9, 14, 14);
                        g.FillEllipse(w, 9, 19, 14, 6);
                        g.FillEllipse(w, 9, 6, 14, 6);
                        g.DrawEllipse(p, 9, 6, 14, 6);
                        g.DrawArc(p, 9, 11, 14, 6, 0, 180);
                        g.DrawArc(p, 9, 15, 14, 6, 0, 180);
                    }
                    if (badge != Color.Empty)
                        using (var b = new SolidBrush(badge)) { g.FillEllipse(Brushes.White, 19, 19, 13, 13); g.FillEllipse(b, 21, 21, 9, 9); }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>
    /// 白底卡片。標題是真正的 Label 控制項 (不是畫上去的)，卡片本身不捲動，
    /// 需要捲動時由外層頁面捲動，避免標題殘影重疊。
    /// </summary>
    public class Card : Panel
    {
        public readonly Label TitleLabel;
        public int ContentTop { get; private set; }
        Panel body;

        /// <summary>放表格、工具列等 Dock 內容的區域 (在標題下方，有內距)。</summary>
        public Panel Body
        {
            get
            {
                if (body == null)
                {
                    body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(Theme.S(14), Theme.S(8), Theme.S(14), Theme.S(12)) };
                    Controls.Add(body);
                    body.BringToFront();
                }
                return body;
            }
        }

        public Card(string title = null)
        {
            BackColor = Theme.Card;
            Theme.DoubleBuffer(this);
            ResizeRedraw = true;
            if (!string.IsNullOrEmpty(title))
            {
                TitleLabel = Theme.Lbl(title, 11f, FontStyle.Bold);
                TitleLabel.AutoSize = false;
                TitleLabel.Dock = DockStyle.Top;
                TitleLabel.TextAlign = ContentAlignment.MiddleLeft;
                TitleLabel.Padding = new Padding(Theme.S(17), 0, 0, 0);
                TitleLabel.Height = TextRenderer.MeasureText("國", TitleLabel.Font).Height + Theme.S(22);
                TitleLabel.BackColor = Color.White;
                var line = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(232, 235, 240) };
                Controls.Add(line);
                Controls.Add(TitleLabel);
                ContentTop = TitleLabel.Height + 1 + Theme.S(14);
            }
            else ContentTop = Theme.S(14);
            Padding = new Padding(1);   // 留 1px 給外框
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>總覽頁的數字卡片 (位置依字高計算)。</summary>
    public class StatTile : Panel
    {
        public string Caption = "", Value = "", Sub = "";
        public Color Stripe = Theme.Accent;
        readonly Font fCap = Theme.F(9.5f), fVal = Theme.F(18f, FontStyle.Bold), fSub = Theme.F(9f);
        public StatTile()
        {
            BackColor = Color.White; Theme.DoubleBuffer(this); ResizeRedraw = true;
            Height = TextRenderer.MeasureText("國", fCap).Height + TextRenderer.MeasureText("0", fVal).Height + TextRenderer.MeasureText("國", fSub).Height + Theme.S(36);
        }
        public void Set(string value, string sub, Color? stripe = null)
        {
            Value = value; Sub = sub; if (stripe.HasValue) Stripe = stripe.Value; Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(Stripe)) g.FillRectangle(b, 0, 0, Theme.S(4), Height);
            int x = Theme.S(18), y = Theme.S(12), w = Width - x - Theme.S(8);
            int h1 = TextRenderer.MeasureText("國", fCap).Height, h2 = TextRenderer.MeasureText("0", fVal).Height, h3 = TextRenderer.MeasureText("國", fSub).Height;
            TextRenderer.DrawText(g, Caption, fCap, new Rectangle(x, y, w, h1), Theme.SubText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            y += h1 + Theme.S(4);
            TextRenderer.DrawText(g, Value, fVal, new Rectangle(x - Theme.S(2), y, w, h2), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            y += h2 + Theme.S(4);
            TextRenderer.DrawText(g, Sub, fSub, new Rectangle(x, y, w, h3), Theme.SubText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>左側選單項目。</summary>
    public class NavItem : Control
    {
        public string Glyph;
        public bool Selected;
        bool hover;
        readonly Font fText = Theme.F(10.5f), fBold = Theme.F(10.5f, FontStyle.Bold);
        public NavItem(string text, string glyph)
        {
            Text = text; Glyph = glyph; Dock = DockStyle.Top; Cursor = Cursors.Hand;
            Height = Math.Max(Theme.S(46), TextRenderer.MeasureText("國", fText).Height + Theme.S(24));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Selected ? Theme.SidebarActive : hover ? Theme.SidebarHover : Theme.Sidebar);
            if (Selected) using (var b = new SolidBrush(Color.FromArgb(76, 160, 255))) g.FillRectangle(b, 0, Theme.S(8), Theme.S(4), Height - Theme.S(16));
            var col = Selected ? Color.White : Theme.SidebarText;
            int x = Theme.S(22);
            if (Theme.HasIconFont && !string.IsNullOrEmpty(Glyph))
            {
                using (var f = Theme.IconFont(12f)) TextRenderer.DrawText(g, Glyph, f, new Rectangle(x, 0, Theme.S(26), Height), col, TextFormatFlags.VerticalCenter);
                x += Theme.S(34);
            }
            TextRenderer.DrawText(g, Text, Selected ? fBold : fText, new Rectangle(x, 0, Width - x, Height), col, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// 表單排版：左標籤、右欄位，一列一列往下排。
    /// 所有尺寸用 S() 縮放；標籤比預留寬度長時，欄位自動往右移，列高依實際控制項高度決定。
    /// </summary>
    public class FieldLayout
    {
        readonly Control host;
        public int X, Y, LabelWidth;
        public int Gap = Theme.S(12);

        /// <param name="x">未縮放的 X</param>
        /// <param name="y">已縮放的 Y (通常是 Card.ContentTop)</param>
        public FieldLayout(Control host, int x, int y, int labelWidth = 140)
        {
            this.host = host; X = Theme.S(x); Y = y; LabelWidth = Theme.S(labelWidth);
        }

        int MinRow { get { return Theme.S(40); } }

        public T Row<T>(string label, T ctl, int width = 320, string hint = null) where T : Control
        {
            if (ctl is TextBox || ctl is ComboBox || ctl is NumericUpDown || ctl is DateTimePicker || ctl is CheckBox || ctl is RadioButton || ctl is Label)
                if (ctl.Font == Control.DefaultFont || ctl.Font == null) ctl.Font = Theme.F(9.5f);
            int cx = X;
            Label l = null;
            if (label != null)
            {
                l = Theme.Lbl(label);
                cx = X + Math.Max(LabelWidth, TextRenderer.MeasureText(label, l.Font).Width + Theme.S(14));
            }
            if (width > 0) ctl.Width = Theme.S(width);
            host.Controls.Add(ctl);
            int ch = ctl.Height;
            if (ctl is TextBox || ctl is ComboBox || ctl is NumericUpDown || ctl is DateTimePicker) ch = ctl.PreferredSize.Height;
            int rowH = Math.Max(MinRow, ch + Theme.S(12));
            ctl.Location = new Point(cx, Y + (rowH - ch) / 2);
            if (l != null)
            {
                int lh = TextRenderer.MeasureText(label, l.Font).Height;
                l.Location = new Point(X, Y + (rowH - lh) / 2);
                host.Controls.Add(l);
            }
            if (hint != null)
            {
                var h = Theme.Lbl(hint, 8.5f, FontStyle.Regular, Theme.SubText);
                int hh = TextRenderer.MeasureText(hint, h.Font).Height;
                h.Location = new Point(ctl.Right + Theme.S(10), Y + (rowH - hh) / 2);
                host.Controls.Add(h);
            }
            Y += rowH;
            return ctl;
        }

        /// <summary>一列：左標籤 + 多個控制項水平排列 (例如兩個選項按鈕)。</summary>
        public void RowMany(string label, params Control[] cs)
        {
            var l = Theme.Lbl(label);
            int cx = X + Math.Max(LabelWidth, TextRenderer.MeasureText(label, l.Font).Width + Theme.S(14));
            int h = 0;
            foreach (var c in cs) { if (c.Font == Control.DefaultFont) c.Font = Theme.F(9.5f); h = Math.Max(h, c.PreferredSize.Height); }
            int rowH = Math.Max(MinRow, h + Theme.S(12));
            foreach (var c in cs)
            {
                host.Controls.Add(c);
                var ps = c.PreferredSize;
                c.Location = new Point(cx, Y + (rowH - ps.Height) / 2);
                cx += ps.Width + Theme.S(18);
            }
            int lh = TextRenderer.MeasureText(label, l.Font).Height;
            l.Location = new Point(X, Y + (rowH - lh) / 2);
            host.Controls.Add(l);
            Y += rowH;
        }

        /// <summary>放在上一列欄位右邊 (例如「瀏覽…」按鈕)。</summary>
        public T Beside<T>(Control prev, T ctl) where T : Control
        {
            host.Controls.Add(ctl);
            ctl.Location = new Point(prev.Right + Theme.S(8), prev.Top + (prev.Height - ctl.Height) / 2);
            return ctl;
        }

        public Label Note(string text, int width = 560, Color? color = null)
        {
            var l = new Label { Text = text, Font = Theme.F(9f), ForeColor = color ?? Theme.SubText, Location = new Point(X, Y), Width = Theme.S(width), BackColor = Color.Transparent, UseMnemonic = false };
            l.Height = TextRenderer.MeasureText(text, l.Font, new Size(l.Width, 0), TextFormatFlags.WordBreak).Height + Theme.S(4);
            host.Controls.Add(l);
            Y += l.Height + Theme.S(8);
            return l;
        }

        public Label Section(string text)
        {
            Y += Theme.S(8);
            var l = Theme.Lbl(text, 10.5f, FontStyle.Bold);
            l.Location = new Point(X, Y);
            host.Controls.Add(l);
            Y += TextRenderer.MeasureText(text, l.Font).Height + Theme.S(12);
            return l;
        }

        public CheckBox Check(string text, bool value, int indent = 0)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Font = Theme.F(9.5f), Location = new Point(X + Theme.S(indent), Y + Theme.S(4)), BackColor = Color.Transparent, UseMnemonic = false };
            host.Controls.Add(c);
            Y += Math.Max(Theme.S(32), c.PreferredSize.Height + Theme.S(10));
            return c;
        }

        public RadioButton Radio(string text, bool value, int indent = 0)
        {
            var c = new RadioButton { Text = text, Checked = value, AutoSize = true, Font = Theme.F(9.5f), Location = new Point(X + Theme.S(indent), Y + Theme.S(4)), BackColor = Color.Transparent, UseMnemonic = false };
            host.Controls.Add(c);
            Y += Math.Max(Theme.S(32), c.PreferredSize.Height + Theme.S(10));
            return c;
        }

        public void Buttons(params Button[] bs)
        {
            int x = X, h = 0;
            foreach (var b in bs) { b.Location = new Point(x, Y + Theme.S(6)); host.Controls.Add(b); x += b.Width + Theme.S(10); h = Math.Max(h, b.Height); }
            Y += h + Theme.S(18);
        }

        /// <summary>一列多個控制項 (checkbox 等) 水平排列。</summary>
        public void Inline(int indent, params Control[] cs)
        {
            int x = X + Theme.S(indent), h = 0;
            foreach (var c in cs)
            {
                if (c.Font == Control.DefaultFont) c.Font = Theme.F(9.5f);
                c.Location = new Point(x, Y + Theme.S(4)); host.Controls.Add(c);
                int w = c.AutoSize ? c.PreferredSize.Width : c.Width;
                x += w + Theme.S(14); h = Math.Max(h, c.AutoSize ? c.PreferredSize.Height : c.Height);
            }
            Y += Math.Max(Theme.S(34), h + Theme.S(12));
        }
    }

    /// <summary>可捲動的表單頁：多張卡片由上往下排，由頁面本身捲動。</summary>
    public class ScrollStack : FlowLayoutPanel
    {
        public ScrollStack()
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoScroll = true;
            BackColor = Theme.Page;
            Dock = DockStyle.Fill;
            Padding = new Padding(0, 0, 0, Theme.S(10));
        }
        public void AddCard(Card c, int contentBottom)
        {
            c.Height = contentBottom + Theme.S(16);
            c.Margin = new Padding(0, 0, 0, Theme.S(16));
            Controls.Add(c);
            FitWidths();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); FitWidths(); }
        void FitWidths()
        {
            int w = ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2;
            foreach (Control c in Controls) c.Width = Math.Max(Theme.S(400), w);
        }
    }
}

namespace SqlBackupManager
{
    /// <summary>
    /// 密碼欄位 + 「顯示」按鈕。
    /// 新輸入的密碼可以按「顯示」確認；從設定檔載入的「已儲存密碼」不能顯示，
    /// 要重新輸入 (一打字就會清掉舊密碼) 之後才能顯示。
    /// </summary>
    public class PasswordBox : Panel
    {
        readonly TextBox tb = new TextBox { UseSystemPasswordChar = true };
        readonly Button eye = new Button { FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, BackColor = System.Drawing.Color.White, Cursor = Cursors.Hand, TabStop = false };
        readonly ToolTip tip = new ToolTip();
        bool locked, shown;

        public PasswordBox()
        {
            BackColor = System.Drawing.Color.Transparent;
            tb.Font = Theme.F(9.5f);
            eye.Font = Theme.F(9f);
            eye.FlatAppearance.BorderColor = Theme.Border;
            eye.Text = "顯示";
            Height = tb.PreferredHeight;
            eye.Size = new System.Drawing.Size(TextRenderer.MeasureText("隱藏", eye.Font).Width + Theme.S(18), Height);
            Controls.Add(tb); Controls.Add(eye);
            Resize += (s, e) => Lay();
            tb.TextChanged += (s, e) => OnTextChanged(EventArgs.Empty);
            eye.Click += (s, e) => { if (locked) return; shown = !shown; tb.UseSystemPasswordChar = !shown; eye.Text = shown ? "隱藏" : "顯示"; tb.Focus(); tb.SelectionStart = tb.TextLength; };
            // 已儲存的密碼：一開始輸入 (或刪除、貼上) 就清掉，改成新密碼
            tb.KeyDown += (s, e) =>
            {
                if (!locked) return;
                bool edit = e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete || (e.Control && e.KeyCode == Keys.V);
                if (edit) { Unlock(); if (e.KeyCode != Keys.V) e.SuppressKeyPress = true; }
            };
            tb.KeyPress += (s, e) => { if (locked && !char.IsControl(e.KeyChar)) Unlock(); };
            UpdateEye();
            Lay();
        }

        void Unlock() { locked = false; tb.Text = ""; UpdateEye(); }

        /// <summary>目前是「已儲存、不能顯示」的密碼。</summary>
        public bool Locked { get { return locked; } }
        public bool CanReveal { get { return eye.Enabled; } }

        void Lay()
        {
            eye.Height = Height;
            eye.Location = new System.Drawing.Point(Width - eye.Width, 0);
            tb.Location = new System.Drawing.Point(0, 0);
            tb.Width = Math.Max(Theme.S(40), Width - eye.Width - Theme.S(4));
        }

        void UpdateEye()
        {
            if (locked) { shown = false; tb.UseSystemPasswordChar = true; eye.Text = "顯示"; }
            eye.Enabled = !locked && Enabled;
            eye.ForeColor = locked ? Theme.SubText : Theme.Text;
            tip.SetToolTip(eye, locked ? "已儲存的密碼無法顯示；重新輸入後可以按「顯示」確認" : "顯示 / 隱藏密碼");
            tip.SetToolTip(tb, locked ? "已儲存的密碼（重新輸入會取代）" : "");
        }

        /// <summary>載入已儲存的密碼 (不能顯示)。</summary>
        public void SetSaved(string password)
        {
            tb.Text = password ?? "";
            locked = tb.Text != "";
            UpdateEye();
        }

        public override string Text { get { return tb.Text; } set { tb.Text = value ?? ""; } }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); UpdateEye(); }
    }
}
