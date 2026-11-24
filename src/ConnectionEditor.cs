using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlBackupManager
{
    /// <summary>
    /// 連線設定 + 測試連線。用在「新增/編輯備份工作」與「還原到其他伺服器」。
    /// 修改任何欄位都會讓驗證失效，必須重新測試成功才算 Verified。
    /// </summary>
    public class ConnectionEditor : Panel
    {
        public DbEngine Engine { get; private set; }
        public bool Verified { get; private set; }
        public List<DbInfo> Databases = new List<DbInfo>();
        public event Action VerifiedChanged;

        readonly Panel pSql = new Panel { BackColor = Color.White }, pTool = new Panel { BackColor = Color.White };
        readonly ComboBox cbServer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        readonly RadioButton rbWin = new RadioButton { Text = "Windows 驗證", AutoSize = true, UseMnemonic = false };
        readonly RadioButton rbSql = new RadioButton { Text = "SQL Server 驗證", AutoSize = true, UseMnemonic = false };
        readonly TextBox txtUser = new TextBox(), txtPwd = new TextBox { UseSystemPasswordChar = true }, txtPkg = new TextBox();
        readonly NumericUpDown numTimeout = new NumericUpDown { Minimum = 5, Maximum = 120 };
        readonly TextBox tHost = new TextBox(), tUser = new TextBox(), tPwd = new TextBox { UseSystemPasswordChar = true }, tBin = new TextBox();
        readonly NumericUpDown tPort = new NumericUpDown { Minimum = 1, Maximum = 65535 };
        readonly Label lblResult = new Label { AutoSize = false, UseMnemonic = false };
        readonly Button btnTest;
        bool loading;

        public ConnectionEditor()
        {
            BackColor = Color.White;
            foreach (var c in new Control[] { rbWin, rbSql }) c.Font = Theme.F(9.5f);

            // SQL Server
            var f = new FieldLayout(pSql, 0, 0, 140);
            f.Row("伺服器執行個體", cbServer, 280);
            var detect = f.Beside(cbServer, Theme.Btn("偵測本機"));
            f.RowMany("驗證方式", rbWin, rbSql);
            f.Row("帳號", txtUser, 220);
            f.Row("密碼", txtPwd, 220, "以 DPAPI 加密儲存於本機");
            f.Row("連線逾時 (秒)", numTimeout, 80);
            f.Row("SqlPackage 位置", txtPkg, 360, "BACPAC 才需要，空白 = 自動尋找");
            pSql.Height = f.Y;

            // MySQL / PostgreSQL
            var g = new FieldLayout(pTool, 0, 0, 140);
            g.Row("主機", tHost, 240, "例如 localhost、192.168.1.10");
            g.Row("連接埠", tPort, 100);
            g.Row("帳號", tUser, 220);
            g.Row("密碼", tPwd, 220, "以 DPAPI 加密儲存於本機");
            g.Row("工具資料夾", tBin, 360, "mysqldump / pg_dump 所在資料夾，空白 = 自動尋找");
            pTool.Height = g.Y;

            btnTest = Theme.Btn("測試連線", true, 120);
            lblResult.Font = Theme.F(9.5f);

            Controls.Add(pSql); Controls.Add(pTool); Controls.Add(btnTest); Controls.Add(lblResult);

            detect.Click += (s, e) =>
            {
                var list = SqlOps.DetectLocalInstances();
                cbServer.Items.Clear(); foreach (var i in list) cbServer.Items.Add(i);
                if (list.Count > 0) cbServer.DroppedDown = true;
                else MessageBox.Show("本機找不到 SQL Server 執行個體，請手動輸入，例如：\nSERVER\\SQLEXPRESS、192.168.1.10,1433", "偵測", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            rbWin.CheckedChanged += (s, e) => { txtUser.Enabled = txtPwd.Enabled = rbSql.Checked; Invalidate_(); };
            foreach (Control c in new Control[] { cbServer, txtUser, txtPwd, txtPkg, tHost, tUser, tPwd, tBin })
                c.TextChanged += (s, e) => Invalidate_();
            numTimeout.ValueChanged += (s, e) => Invalidate_();
            tPort.ValueChanged += (s, e) => Invalidate_();
            btnTest.Click += (s, e) => Test();
            Resize += (s, e) => DoLayout();
        }

        void DoLayout()
        {
            var p = Engine == DbEngine.SqlServer ? pSql : pTool;
            pSql.Visible = Engine == DbEngine.SqlServer; pTool.Visible = !pSql.Visible;
            p.Location = new Point(0, 0); p.Width = Width;
            btnTest.Location = new Point(0, p.Bottom + Theme.S(6));
            lblResult.Location = new Point(0, btnTest.Bottom + Theme.S(10));
            lblResult.Width = Math.Max(Theme.S(300), Width - Theme.S(4));
            lblResult.Height = TextRenderer.MeasureText(string.IsNullOrEmpty(lblResult.Text) ? "國" : lblResult.Text, lblResult.Font, new Size(lblResult.Width, 0), TextFormatFlags.WordBreak).Height + Theme.S(6);
            Height = lblResult.Bottom + Theme.S(4);
        }

        public int PreferredHeightFor(DbEngine e)
        {
            var p = e == DbEngine.SqlServer ? pSql : pTool;
            return p.Height + btnTest.Height + Theme.S(16) + Theme.S(70);
        }

        void Invalidate_()
        {
            if (loading) return;
            if (Verified) { Verified = false; RaiseVerified(); }
            SetResult("尚未驗證：修改連線後請按「測試連線」", Theme.Warning);
        }

        void RaiseVerified() { var h = VerifiedChanged; if (h != null) h(); }

        void SetResult(string text, Color c)
        {
            lblResult.ForeColor = c; lblResult.Text = text; DoLayout();
        }

        public void Load(DbEngine e, ConnectionSettings sql, ToolConnection tool, bool verified)
        {
            loading = true;
            Engine = e;
            cbServer.Text = sql.Server; rbWin.Checked = sql.UseWindowsAuth; rbSql.Checked = !sql.UseWindowsAuth;
            txtUser.Text = sql.User; txtPwd.Text = sql.Password; numTimeout.Value = Math.Max(5, Math.Min(120, sql.ConnectTimeoutSec)); txtPkg.Text = sql.SqlPackagePath;
            txtUser.Enabled = txtPwd.Enabled = !sql.UseWindowsAuth;
            tHost.Text = tool.Host; tPort.Value = Math.Max(1, Math.Min(65535, tool.Port <= 0 ? DefaultPort(e) : tool.Port)); tUser.Text = tool.User; tPwd.Text = tool.Password; tBin.Text = tool.BinDir;
            loading = false;
            Verified = verified;
            if (verified) SetResult("✔ 已驗證", Theme.Success);
            else SetResult("尚未驗證：請按「測試連線」，成功後才能儲存與備份", Theme.Warning);
            DoLayout();
        }

        static int DefaultPort(DbEngine e) { return e == DbEngine.PostgreSql ? 5432 : 3306; }

        /// <summary>切換資料庫類型 (MySQL ↔ PostgreSQL 會換成預設連接埠與帳號)。</summary>
        public void SetEngine(DbEngine e)
        {
            if (e == Engine) return;
            loading = true;
            if (e != DbEngine.SqlServer && Engine != e)
            {
                if (tPort.Value == 3306 || tPort.Value == 5432) tPort.Value = DefaultPort(e);
                if (tUser.Text == "" || tUser.Text == "root" || tUser.Text == "postgres") tUser.Text = e == DbEngine.MySql ? "root" : "postgres";
            }
            loading = false;
            Engine = e;
            Verified = false; RaiseVerified();
            SetResult("尚未驗證：請按「測試連線」", Theme.Warning);
            DoLayout();
        }

        public ConnectionSettings SqlConn
        {
            get
            {
                var c = new ConnectionSettings { Server = cbServer.Text.Trim(), UseWindowsAuth = rbWin.Checked, User = txtUser.Text.Trim(), ConnectTimeoutSec = (int)numTimeout.Value, SqlPackagePath = txtPkg.Text.Trim() };
                c.Password = txtPwd.Text;
                return c;
            }
        }

        public ToolConnection ToolConn
        {
            get
            {
                var t = new ToolConnection { Host = tHost.Text.Trim(), Port = (int)tPort.Value, User = tUser.Text.Trim(), BinDir = tBin.Text.Trim() };
                t.Password = tPwd.Text;
                return t;
            }
        }

        public void Test()
        {
            var e = Engine; var sql = SqlConn; var tool = ToolConn;
            if (e == DbEngine.SqlServer ? sql.Server == "" : tool.Host == "") { SetResult("請先輸入伺服器", Theme.Danger); return; }
            btnTest.Enabled = false;
            SetResult("連線中…", Theme.SubText);
            Task.Run(() =>
            {
                try
                {
                    List<DbInfo> dbs;
                    string info = ConnTest.Test(e, sql, tool, out dbs);
                    UI(() => { btnTest.Enabled = true; Databases = dbs; Verified = true; SetResult("✔ 連線成功　" + info, Theme.Success); RaiseVerified(); });
                }
                catch (Exception ex)
                {
                    UI(() => { btnTest.Enabled = true; Verified = false; SetResult("✖ 連線失敗：" + ex.Message, Theme.Danger); RaiseVerified(); });
                }
            });
        }

        void UI(Action a) { if (IsDisposed || !IsHandleCreated) return; try { BeginInvoke(a); } catch { } }
    }

    /// <summary>還原頁「其他伺服器…」用的連線對話框。</summary>
    public class ConnectionForm : Form
    {
        public readonly ConnectionEditor Editor = new ConnectionEditor();
        readonly ComboBox cbEngine = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

        public ConnectionForm(DbEngine engine)
        {
            Text = "還原到哪一台伺服器";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = Theme.F(9.5f);
            Icon = Theme.MakeIcon(Color.Empty);
            var f = new FieldLayout(this, 20, Theme.S(16), 140);
            foreach (DbEngine e in Enum.GetValues(typeof(DbEngine))) cbEngine.Items.Add(BackupJob.EngineText(e));
            f.Row("資料庫類型", cbEngine, 220);
            Editor.Location = new Point(Theme.S(20), f.Y); Editor.Width = Theme.S(720);
            Controls.Add(Editor);
            var tmpSql = new ConnectionSettings();
            var tmpTool = new ToolConnection { Port = engine == DbEngine.PostgreSql ? 5432 : 3306, User = engine == DbEngine.PostgreSql ? "postgres" : "root" };
            Editor.Load(engine, tmpSql, tmpTool, false);
            cbEngine.SelectedIndex = (int)engine;
            cbEngine.SelectedIndexChanged += (s, e) => Editor.SetEngine((DbEngine)cbEngine.SelectedIndex);

            var ok = Theme.Btn("使用此連線", true); var cancel = Theme.Btn("取消");
            ok.Enabled = false;
            Editor.VerifiedChanged += () => ok.Enabled = Editor.Verified;
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok); Controls.Add(cancel);
            Action place = () =>
            {
                int y = Editor.Bottom + Theme.S(16);
                cancel.Location = new Point(Editor.Right - cancel.Width, y);
                ok.Location = new Point(cancel.Left - ok.Width - Theme.S(10), y);
                ClientSize = new Size(Editor.Right + Theme.S(20), cancel.Bottom + Theme.S(16));
            };
            Editor.Resize += (s, e) => place();
            place();
        }
    }
}
