using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace SqlBackupManager
{
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

        public static Font F(float size, FontStyle style = FontStyle.Regular) { return new Font(Family, size, style); }
        public static Font IconFont(float size) { return new Font("Segoe MDL2 Assets", size); }

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
                Height = 32,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Text,
                Margin = new Padding(0, 0, 8, 0)
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(240, 244, 249);
            b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(0, 70, 135) : Color.FromArgb(228, 234, 242);
            b.Width = width > 0 ? width : Math.Max(84, TextRenderer.MeasureText(text, b.Font).Width + 28);
            return b;
        }

        public static Label Lbl(string text, float size = 9.5f, FontStyle st = FontStyle.Regular, Color? color = null)
        {
            return new Label { Text = text, AutoSize = true, Font = F(size, st), ForeColor = color ?? Text, BackColor = Color.Transparent };
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
            g.ColumnHeadersHeight = 36;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 249, 251);
            g.ColumnHeadersDefaultCellStyle.ForeColor = SubText;
            g.ColumnHeadersDefaultCellStyle.Font = F(9f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(247, 249, 251);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
            g.DefaultCellStyle.Font = F(9.5f);
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 238, 252);
            g.DefaultCellStyle.SelectionForeColor = Text;
            g.DefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
            g.RowTemplate.Height = 32;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            typeof(DataGridView).InvokeMember("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, g, new object[] { true });
        }

        public static DataGridViewTextBoxColumn Col(string header, float weight, int minWidth = 60)
        {
            return new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = weight, MinimumWidth = minWidth, SortMode = DataGridViewColumnSortMode.NotSortable };
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

    /// <summary>白底卡片，含標題與細框。</summary>
    public class Card : Panel
    {
        public string Title;
        public Card(string title = null)
        {
            Title = title;
            BackColor = Theme.Card;
            Padding = new Padding(18, string.IsNullOrEmpty(title) ? 16 : 50, 18, 16);
            Theme.DoubleBuffer(this);
            ResizeRedraw = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (!string.IsNullOrEmpty(Title))
            {
                using (var f = Theme.F(11f, FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, Title, f, new Point(16, 15), Theme.Text);
                using (var p = new Pen(Color.FromArgb(238, 240, 244))) e.Graphics.DrawLine(p, 1, 42, Width - 2, 42);
            }
        }
    }

    /// <summary>總覽頁的數字卡片。</summary>
    public class StatTile : Panel
    {
        public string Caption = "", Value = "", Sub = "";
        public Color Stripe = Theme.Accent;
        public StatTile() { BackColor = Color.White; Theme.DoubleBuffer(this); ResizeRedraw = true; Height = 104; }
        public void Set(string value, string sub, Color? stripe = null)
        {
            Value = value; Sub = sub; if (stripe.HasValue) Stripe = stripe.Value; Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(Stripe)) g.FillRectangle(b, 0, 0, 4, Height);
            using (var f = Theme.F(9.5f)) TextRenderer.DrawText(g, Caption, f, new Point(18, 14), Theme.SubText);
            using (var f = Theme.F(18f, FontStyle.Bold)) TextRenderer.DrawText(g, Value, f, new Rectangle(16, 36, Width - 24, 36), Theme.Text, TextFormatFlags.EndEllipsis);
            using (var f = Theme.F(9f)) TextRenderer.DrawText(g, Sub, f, new Rectangle(18, 76, Width - 24, 20), Theme.SubText, TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>左側選單項目。</summary>
    public class NavItem : Control
    {
        public string Glyph;
        public bool Selected;
        bool hover;
        public NavItem(string text, string glyph)
        {
            Text = text; Glyph = glyph; Height = 46; Dock = DockStyle.Top; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Selected ? Theme.SidebarActive : hover ? Theme.SidebarHover : Theme.Sidebar);
            if (Selected) using (var b = new SolidBrush(Color.FromArgb(76, 160, 255))) g.FillRectangle(b, 0, 8, 4, Height - 16);
            var col = Selected ? Color.White : Theme.SidebarText;
            int x = 22;
            if (Theme.HasIconFont && !string.IsNullOrEmpty(Glyph))
            {
                using (var f = Theme.IconFont(12f)) TextRenderer.DrawText(g, Glyph, f, new Rectangle(x, 0, 24, Height), col, TextFormatFlags.VerticalCenter);
                x += 32;
            }
            using (var f = Theme.F(10.5f, Selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, Text, f, new Rectangle(x, 0, Width - x, Height), col, TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>簡單的表單排版：左標籤、右欄位，一列一列往下排。</summary>
    public class FieldLayout
    {
        readonly Control host;
        public int X, Y, LabelWidth, RowHeight = 40;
        public FieldLayout(Control host, int x, int y, int labelWidth = 140) { this.host = host; X = x; Y = y; LabelWidth = labelWidth; }

        public T Row<T>(string label, T ctl, int width = 320, string hint = null) where T : Control
        {
            if (label != null)
            {
                var l = Theme.Lbl(label);
                l.Location = new Point(X, Y + 6);
                host.Controls.Add(l);
            }
            ctl.Location = new Point(X + (label == null ? 0 : LabelWidth), Y + 2);
            if (width > 0) ctl.Width = width;
            if (ctl.Font == Control.DefaultFont || ctl is TextBox || ctl is ComboBox || ctl is NumericUpDown || ctl is DateTimePicker) ctl.Font = Theme.F(9.5f);
            host.Controls.Add(ctl);
            if (hint != null)
            {
                var h = Theme.Lbl(hint, 8.5f, FontStyle.Regular, Theme.SubText);
                h.Location = new Point(ctl.Right + 10, Y + 7);
                host.Controls.Add(h);
            }
            Y += RowHeight;
            return ctl;
        }

        public Label Note(string text, int width = 560, Color? color = null)
        {
            var l = new Label { Text = text, Font = Theme.F(9f), ForeColor = color ?? Theme.SubText, Location = new Point(X, Y), Width = width, BackColor = Color.Transparent };
            l.Height = TextRenderer.MeasureText(text, l.Font, new Size(width, 0), TextFormatFlags.WordBreak).Height + 6;
            host.Controls.Add(l);
            Y += l.Height + 6;
            return l;
        }

        public Label Section(string text)
        {
            Y += 6;
            var l = Theme.Lbl(text, 10.5f, FontStyle.Bold);
            l.Location = new Point(X, Y);
            host.Controls.Add(l);
            Y += 34;
            return l;
        }

        public CheckBox Check(string text, bool value, int indent = 0)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Font = Theme.F(9.5f), Location = new Point(X + indent, Y + 4), BackColor = Color.Transparent };
            host.Controls.Add(c);
            Y += 32;
            return c;
        }

        public void Buttons(params Button[] bs)
        {
            int x = X;
            foreach (var b in bs) { b.Location = new Point(x, Y + 6); host.Controls.Add(b); x += b.Width + 10; }
            Y += 48;
        }
    }
}
