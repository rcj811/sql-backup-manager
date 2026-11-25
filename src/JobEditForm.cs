using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SqlBackupManager
{
    /// <summary>
    /// 新增 / 編輯備份工作。
    /// 第一頁是「連線」：每個工作有自己的伺服器與帳號，必須測試連線成功才能儲存。
    /// </summary>
    public class JobEditForm : Form
    {
        readonly BackupJob job;
        readonly BackupJob original;

        readonly TabControl tabs = new TabControl();
        readonly ComboBox cbEngine = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ConnectionEditor conn = new ConnectionEditor();

        readonly TextBox txtName = new TextBox();
        readonly ComboBox cbKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly Label lblKindHint = new Label { AutoSize = false, UseMnemonic = false };
        CheckBox chkEnabled, chkSys;
        RadioButton rbAll, rbPick;
        readonly CheckedListBox lstDb = new CheckedListBox { CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
        readonly Label lblDbStatus = new Label { AutoSize = true, UseMnemonic = false };

        readonly TextBox txtPath = new TextBox(), txtSecondary = new TextBox(), txtMirror = new TextBox(), txtCert = new TextBox();
        readonly Panel pnlNet = new Panel { BackColor = Color.FromArgb(247, 250, 255), Visible = false }, pnlRest = new Panel { BackColor = Color.White };
        readonly TextBox txtNetUser = new TextBox(), txtNetPwd = new TextBox { UseSystemPasswordChar = true };
        readonly Label lblNetInfo = new Label { AutoSize = false, UseMnemonic = false }, lblNetResult = new Label { AutoSize = false, UseMnemonic = false };
        int restTopNoNet, netHostTop;
        readonly Panel pnlHost = new Panel { BackColor = Color.FromArgb(247, 250, 255) }, pnlNetBottom = new Panel { BackColor = Color.FromArgb(247, 250, 255) };
        readonly TextBox txtHostLocal = new TextBox(), txtHostUnc = new TextBox(), txtHostUser = new TextBox(), txtHostPwd = new TextBox { UseSystemPasswordChar = true };
        readonly Label lblHostInfo = new Label { AutoSize = false, UseMnemonic = false };
        bool hostUncEdited, hostUncAuto;
        RadioButton rbNetUse, rbRelay;

        void UpdateRelayFields()
        {
            bool r = rbRelay.Checked;
            txtHostLocal.Enabled = txtHostUnc.Enabled = txtHostUser.Enabled = txtHostPwd.Enabled = r;
        }

        /// <summary>需要 SQL 主機中轉：SQL Server 原生備份、SQL 在別台電腦、而且有網路路徑。</summary>
        bool NeedRelay { get { return Engine == DbEngine.SqlServer && Kind != BackupKind.Bacpac && NetPaths().Count > 0 && !NetPath.IsLocalServer(conn.SqlConn.Server); } }
        bool netVerified, netLoading;
        string netVerifiedFor = "";
        CheckBox chkSub, chkVerify, chkChecksum, chkCompress, chkZip, chkCopyOnly, chkFallback, chkSingleTx, chkRoutines, chkPgCustom, chkGzip;
        readonly NumericUpDown numRetention = new NumericUpDown { Minimum = 0, Maximum = 3650 };
        readonly NumericUpDown numStripes = new NumericUpDown { Minimum = 1, Maximum = 16 };
        readonly ComboBox cbAlgo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly Panel pnlSqlAdv = new Panel { BackColor = Color.White }, pnlDumpAdv = new Panel { BackColor = Color.White };

        readonly ComboBox cbSchedule = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly DateTimePicker dtStart = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd" };
        readonly DateTimePicker dtTime = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
        readonly DateTimePicker dtEnd = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
        readonly Panel pnlWeek = new Panel { BackColor = Color.White }, pnlMonth = new Panel { BackColor = Color.White }, pnlInterval = new Panel { BackColor = Color.White };
        readonly CheckBox[] chkDays = new CheckBox[7];
        readonly ComboBox cbMonthDay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly NumericUpDown numInterval = new NumericUpDown { Minimum = 5, Maximum = 1440, Increment = 5 };
        readonly Label lblPreview = new Label { AutoSize = true, UseMnemonic = false };
        readonly Label lblVerify = new Label { AutoSize = true, UseMnemonic = false };
        readonly Button btnOk;
        List<string> preChecked;
        bool loaded;
        Button btnDefaultPath;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, string lp);

        /// <summary>文字框空白時顯示的灰色提示字。</summary>
        static void Cue(TextBox t, string text)
        {
            EventHandler h = (s, e) => { try { SendMessage(t.Handle, 0x1501, (IntPtr)1, text); } catch { } };
            if (t.IsHandleCreated) h(t, EventArgs.Empty); else t.HandleCreated += h;
        }

        void SetPath(string p)
        {
            txtPath.Text = p;
        }

        public JobEditForm(BackupJob work, BackupJob original)
        {
            job = work; this.original = original;
            Text = original == null ? "新增備份工作" : "編輯備份工作 - " + original.Name;
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = Theme.F(9.5f);
            Icon = Theme.MakeIcon(Color.Empty);
            ClientSize = Theme.S(860, 680);
            MinimumSize = Theme.S(760, 560);

            tabs.Dock = DockStyle.Fill; tabs.Font = Theme.F(10f); tabs.Padding = new Point(Theme.S(16), Theme.S(6));
            var tConn = Tab("1. 連線"); var tGen = Tab("2. 備份內容"); var tOpt = Tab("3. 存放位置與選項"); var tSch = Tab("4. 排程");
            tabs.TabPages.AddRange(new[] { tConn, tGen, tOpt, tSch });

            // ── 1. 連線
            var f0 = new FieldLayout(tConn, 20, Theme.S(18), 140);
            f0.Row("工作名稱", txtName, 360);
            foreach (DbEngine e in Enum.GetValues(typeof(DbEngine))) cbEngine.Items.Add(BackupJob.EngineText(e));
            f0.Row("資料庫類型", cbEngine, 220);
            f0.Section("伺服器連線");
            f0.Note("每個工作可以連到不同的伺服器。請填好後按「測試連線」，成功後才能儲存，排程才會執行備份。", 720);
            conn.Location = new Point(f0.X, f0.Y); conn.Width = Theme.S(760);
            tConn.Controls.Add(conn);

            // ── 2. 備份內容
            var f1 = new FieldLayout(tGen, 20, Theme.S(18), 140);
            f1.Row("備份類型", cbKind, 240);
            lblKindHint.Font = Theme.F(9f); lblKindHint.ForeColor = Theme.SubText;
            f1.Row(null, lblKindHint, 720);
            lblKindHint.Height = TextRenderer.MeasureText("國\n國", lblKindHint.Font).Height + Theme.S(4);
            f1.Y = lblKindHint.Bottom + Theme.S(8);
            chkEnabled = f1.Check("啟用此工作 (依排程自動執行)", true);
            f1.Section("要備份的資料庫");
            rbAll = f1.Radio("全部使用者資料庫（之後新增的資料庫也會自動備份）", true);
            chkSys = f1.Check("包含系統資料庫", false, 24);
            rbPick = f1.Radio("指定資料庫", false);
            lstDb.Font = Theme.F(9.5f); lstDb.Location = new Point(f1.X + Theme.S(24), f1.Y); lstDb.Size = Theme.S(520, 180);
            tGen.Controls.Add(lstDb);
            var reload = Theme.Btn("重新載入清單"); reload.Location = new Point(lstDb.Right + Theme.S(12), lstDb.Top); tGen.Controls.Add(reload);
            lblDbStatus.Font = Theme.F(9f); lblDbStatus.ForeColor = Theme.SubText; lblDbStatus.Location = new Point(reload.Left, reload.Bottom + Theme.S(10)); tGen.Controls.Add(lblDbStatus);
            reload.Click += (s, e) => conn.Test();

            // ── 3. 存放與選項
            var f2 = new FieldLayout(tOpt, 20, Theme.S(18), 140);
            f2.Row("備份資料夾", txtPath, 440);
            var b1 = f2.Beside(txtPath, Theme.Btn("瀏覽…"));
            btnDefaultPath = f2.Beside(b1, Theme.Btn("用伺服器預設"));
            btnDefaultPath.Enabled = false;
            btnDefaultPath.Click += (s, e) => { if (!string.IsNullOrEmpty(conn.DefaultBackupDir)) txtPath.Text = conn.DefaultBackupDir; };
            Cue(txtPath, "例如 D:\\SQLBackup 或 \\\\NAS\\Backup");
            Cue(txtSecondary, "選填，例如 \\\\NAS\\Backup");
            f2.Note("本機路徑、網路路徑 (\\\\NAS\\Backup 或對應的磁碟機 Z:)，或 Azure Blob 網址。網路路徑會自動偵測並要求驗證帳號。", 720);
            f2.Row("第二備份位置", txtSecondary, 440);
            var b2 = f2.Beside(txtSecondary, Theme.Btn("瀏覽…"));
            f2.Note("選填。備份完成後由本程式複製一份過去（異地備份）。", 720);

            // 網路路徑區 (偵測到網路路徑才顯示)
            pnlNet.Location = new Point(0, f2.Y); pnlNet.Width = Theme.S(820);
            var fn0 = new FieldLayout(pnlNet, 20, 0, 140);
            fn0.Section("網路位置驗證");
            lblNetInfo.Font = Theme.F(9f); lblNetInfo.ForeColor = Theme.SubText;
            fn0.Row(null, lblNetInfo, 720);
            lblNetInfo.Height = TextRenderer.MeasureText("國\n國\n國\n國", lblNetInfo.Font).Height + Theme.S(4);
            fn0.Y = lblNetInfo.Bottom + Theme.S(6);
            fn0.Row("網路帳號", txtNetUser, 260, "例如 NAS\\admin；空白 = 目前登入的帳號");
            fn0.Row("網路密碼", txtNetPwd, 260, "以 DPAPI 加密儲存於本機");
            netHostTop = fn0.Y;

            // SQL 主機中轉 (SQL Server 在別台電腦時才顯示)
            pnlHost.Location = new Point(0, netHostTop); pnlHost.Width = Theme.S(820);
            var fh = new FieldLayout(pnlHost, 20, 0, 160);
            fh.Section("SQL Server 在另一台電腦");
            lblHostInfo.Font = Theme.F(9f); lblHostInfo.ForeColor = Theme.SubText;
            fh.Row(null, lblHostInfo, 720);
            lblHostInfo.Height = TextRenderer.MeasureText("國\n國", lblHostInfo.Font).Height + Theme.S(4);
            fh.Y = lblHostInfo.Bottom + Theme.S(6);
            rbNetUse = fh.Radio("由 SQL Server 用上面的網路帳號直接連線 NAS（建議；SQL 帳號需有 sysadmin 權限）", true);
            rbRelay = fh.Radio("SQL 主機中轉：SQL Server 先備份到它本機，本程式再用下列帳號取回送到 NAS", false);
            fh.Row("SQL 主機暫存資料夾", txtHostLocal, 300, "SQL Server 那台電腦上的路徑");
            fh.Row("本程式存取路徑", txtHostUnc, 420);
            fh.Row("SQL 主機帳號", txtHostUser, 260, "該電腦的 Windows 帳號，例如 chin-sys\\Administrator");
            fh.Row("SQL 主機密碼", txtHostPwd, 260, "以 DPAPI 加密儲存於本機");
            pnlHost.Height = fh.Y;
            rbNetUse.CheckedChanged += (s, e) => { UpdateRelayFields(); InvalidateNet(); };
            pnlNet.Controls.Add(pnlHost);

            pnlNetBottom.Width = Theme.S(820);
            var fb = new FieldLayout(pnlNetBottom, 20, 0, 140);
            var btnNet = Theme.Btn("驗證網路路徑", true);
            fb.Buttons(btnNet);
            fb.Y -= Theme.S(8);
            lblNetResult.Font = Theme.F(9.5f);
            fb.Row(null, lblNetResult, 720);
            lblNetResult.Height = TextRenderer.MeasureText("國\n國", lblNetResult.Font).Height + Theme.S(4);
            pnlNetBottom.Height = lblNetResult.Bottom + Theme.S(12);
            pnlNet.Controls.Add(pnlNetBottom);
            tOpt.Controls.Add(pnlNet);
            txtHostLocal.TextChanged += (s, e) =>
            {
                // 存取路徑跟著暫存資料夾自動產生 (使用者自己改過就不再覆蓋)
                if (!hostUncEdited) { hostUncAuto = true; txtHostUnc.Text = NetPath.AdminShare(NetPath.HostOf(conn.SqlConn.Server), txtHostLocal.Text.Trim()); hostUncAuto = false; }
                InvalidateNet();
            };
            txtHostUnc.TextChanged += (s, e) => { if (!hostUncAuto) hostUncEdited = true; InvalidateNet(); };
            txtHostUser.TextChanged += (s, e) => InvalidateNet();
            txtHostPwd.TextChanged += (s, e) => InvalidateNet();
            tabs.SelectedIndexChanged += (s, e) => OnPathsChanged();
            btnNet.Click += (s, e) => TestNetwork();

            // 其餘選項 (位置隨網路區顯示/隱藏而上下移動)
            pnlRest.Width = Theme.S(820);
            var fr = new FieldLayout(pnlRest, 20, 0, 140);
            chkSub = fr.Check("每個資料庫建立子資料夾", true, 0);
            fr.Row("保留天數", numRetention, 90, "超過天數的舊備份自動刪除；0 = 永久保留");
            fr.Section("備份選項");
            chkVerify = fr.Check("備份後驗證備份檔", true);
            chkChecksum = fr.Check("計算總和檢查碼 (CHECKSUM)", true);
            chkCompress = fr.Check("原生備份壓縮 (Standard 以上)", true);
            chkZip = fr.Check("備份後壓縮成 ZIP (Express 適用)", false);
            chkCopyOnly = fr.Check("僅複製備份 (COPY_ONLY，不影響備份鏈)", false);
            chkFallback = fr.Check("找不到基底備份時自動改做完整備份", true);
            int advTop = fr.Y;

            var fa = new FieldLayout(pnlSqlAdv, 20, 0, 140);
            fa.Section("SQL Server 進階");
            fa.Row("分割檔案數", numStripes, 80, "Striped：同時寫入多個檔案，加快大型資料庫備份");
            fa.Row("鏡像備份路徑", txtMirror, 440);
            fa.Note("Mirror：同時寫一份相同的備份到另一個位置（僅 Enterprise / Developer 版）。", 720);
            fa.Row("加密憑證", txtCert, 240);
            cbAlgo.Items.AddRange(new object[] { "AES_256", "AES_192", "AES_128", "TRIPLE_DES_3KEY" });
            cbAlgo.Font = Theme.F(9.5f); cbAlgo.Width = Theme.S(160); fa.Beside(txtCert, cbAlgo);
            fa.Note("填入 master 中的伺服器憑證名稱即啟用加密（Standard 以上，2014+）。請另外備份憑證與私密金鑰，否則無法還原！", 720, Theme.Warning);
            pnlSqlAdv.Location = new Point(0, advTop); pnlSqlAdv.Size = new Size(Theme.S(820), fa.Y); pnlRest.Controls.Add(pnlSqlAdv);

            var fd = new FieldLayout(pnlDumpAdv, 20, 0, 140);
            fd.Section("Dump 選項");
            chkSingleTx = fd.Check("--single-transaction (InnoDB 線上備份不鎖表)", true);
            chkRoutines = fd.Check("包含預存程序、函數、觸發程序、事件", true);
            chkPgCustom = fd.Check("PostgreSQL 使用 custom 格式 (-Fc，可選擇性還原，已壓縮)", true);
            chkGzip = fd.Check("純 SQL 輸出以 gzip 壓縮 (.sql.gz)", true);
            pnlDumpAdv.Location = new Point(0, advTop); pnlDumpAdv.Size = new Size(Theme.S(820), fd.Y); pnlRest.Controls.Add(pnlDumpAdv);
            pnlRest.Height = advTop + Math.Max(pnlSqlAdv.Height, pnlDumpAdv.Height) + Theme.S(10);
            tOpt.Controls.Add(pnlRest);
            restTopNoNet = f2.Y;

            b1.Click += (s, e) => { var p = BrowseFolder(txtPath.Text); if (p != null) txtPath.Text = p; };
            b2.Click += (s, e) => { var p = BrowseFolder(txtSecondary.Text); if (p != null) txtSecondary.Text = p; };
            txtPath.TextChanged += (s, e) => OnPathsChanged();
            txtSecondary.TextChanged += (s, e) => OnPathsChanged();
            txtNetUser.TextChanged += (s, e) => InvalidateNet();
            txtNetPwd.TextChanged += (s, e) => InvalidateNet();


            // ── 4. 排程
            var f3 = new FieldLayout(tSch, 20, Theme.S(18), 140);
            cbSchedule.Items.AddRange(new object[] { "單次", "每天", "每週", "每月", "固定間隔" });
            f3.Row("排程方式", cbSchedule, 170);
            f3.Row("開始日期", dtStart, 150);
            f3.Row("執行時間", dtTime, 110);
            int ySched = f3.Y;
            string[] dn = { "日", "一", "二", "三", "四", "五", "六" };
            var days = new List<Control>();
            for (int i = 0; i < 7; i++)
            {
                int d = (i + 1) % 7; // 一 ~ 日
                chkDays[d] = new CheckBox { Text = dn[d], AutoSize = true, Font = Theme.F(9.5f), UseMnemonic = false };
                chkDays[d].CheckedChanged += (s, e) => UpdatePreview();
                days.Add(chkDays[d]);
            }
            var wl = Theme.Lbl("星期"); wl.Location = new Point(0, Theme.S(8)); pnlWeek.Controls.Add(wl);
            var fw = new FieldLayout(pnlWeek, 0, 0, 140);
            fw.X = Math.Max(Theme.S(140), TextRenderer.MeasureText("星期", wl.Font).Width + Theme.S(14));
            fw.Inline(0, days.ToArray());
            pnlWeek.Height = fw.Y;
            var fm = new FieldLayout(pnlMonth, 0, 0, 140);
            for (int i = 1; i <= 31; i++) cbMonthDay.Items.Add(i + " 日");
            cbMonthDay.Items.Add("最後一天");
            fm.Row("每月日期", cbMonthDay, 130);
            pnlMonth.Height = fm.Y;
            var fi = new FieldLayout(pnlInterval, 0, 0, 140);
            fi.Row("間隔 (分鐘)", numInterval, 100, "例如交易記錄每 15 / 30 / 60 分鐘");
            fi.Row("結束時間", dtEnd, 110, "與執行時間相同 = 全天執行");
            pnlInterval.Height = fi.Y;
            int panelsH = Math.Max(pnlWeek.Height, Math.Max(pnlMonth.Height, pnlInterval.Height));
            foreach (var p in new Panel[] { pnlWeek, pnlMonth, pnlInterval }) { p.Location = new Point(f3.X, ySched); p.Width = Theme.S(760); tSch.Controls.Add(p); }
            var prev = new Panel { BackColor = Color.FromArgb(240, 246, 253), Location = new Point(f3.X, ySched + panelsH + Theme.S(12)), Width = Theme.S(760) };
            lblPreview.Font = Theme.F(10f, FontStyle.Bold); lblPreview.ForeColor = Theme.Accent; lblPreview.Location = Theme.P(14, 12);
            prev.Height = TextRenderer.MeasureText("國", lblPreview.Font).Height + Theme.S(24);
            prev.Controls.Add(lblPreview); tSch.Controls.Add(prev);
            var fn = new FieldLayout(tSch, 20, prev.Bottom + Theme.S(14), 140);
            fn.Note("建議排程：\n• 每週日 01:00 完整備份 + 每天 01:00 差異備份 + 每 30 分鐘交易記錄備份（FULL 復原模式）\n• 小型資料庫：每天完整備份即可\n• MySQL / PostgreSQL：每天 dump 一次", 740);

            cbSchedule.SelectedIndexChanged += (s, e) => UpdateSchedulePanels();
            cbMonthDay.SelectedIndexChanged += (s, e) => UpdatePreview();
            foreach (var d in new[] { dtStart, dtTime, dtEnd }) d.ValueChanged += (s, e) => UpdatePreview();
            numInterval.ValueChanged += (s, e) => UpdatePreview();

            // ── 底部
            var bottom = new Panel { Dock = DockStyle.Bottom, BackColor = Color.FromArgb(247, 249, 251) };
            btnOk = Theme.Btn("儲存", true, 100);
            var cancel = Theme.Btn("取消", false, 90);
            bottom.Height = btnOk.Height + Theme.S(28);
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            btnOk.Click += (s, e) => Save();
            lblVerify.Font = Theme.F(9.5f);
            bottom.Controls.Add(btnOk); bottom.Controls.Add(cancel); bottom.Controls.Add(lblVerify);
            bottom.Resize += (s, e) =>
            {
                cancel.Location = new Point(bottom.Width - cancel.Width - Theme.S(20), Theme.S(14));
                btnOk.Location = new Point(cancel.Left - btnOk.Width - Theme.S(10), Theme.S(14));
                lblVerify.Location = new Point(Theme.S(20), (bottom.Height - lblVerify.Height) / 2);
            };

            Controls.Add(tabs);
            Controls.Add(bottom);

            cbEngine.SelectedIndexChanged += (s, e) =>
            {
                conn.SetEngine((DbEngine)cbEngine.SelectedIndex);
                if (loaded) { lstDb.Items.Clear(); preChecked = new List<string>(); }
                FillKinds(); UpdateEngineUi();
            };
            cbKind.SelectedIndexChanged += (s, e) => UpdateEngineUi();
            rbAll.CheckedChanged += (s, e) => { lstDb.Enabled = rbPick.Checked; chkSys.Enabled = rbAll.Checked; };
            conn.VerifiedChanged += () =>
            {
                UpdateVerifyLabel();
                if (!conn.Verified) return;
                FillDatabases(conn.Databases);
                bool hasDef = Engine == DbEngine.SqlServer && !string.IsNullOrEmpty(conn.DefaultBackupDir);
                btnDefaultPath.Enabled = hasDef;
            };

            LoadFromJob();
            loaded = true;
            Shown += (s, e) => conn.Test();   // 開啟時自動驗證一次並載入資料庫清單
        }

        TabPage Tab(string t) { return new TabPage(t) { BackColor = Color.White, AutoScroll = true, Padding = new Padding(Theme.S(4)) }; }

        static string BrowseFolder(string cur)
        {
            using (var d = new FolderBrowserDialog { SelectedPath = System.IO.Directory.Exists(cur) ? cur : "", ShowNewFolderButton = true })
                return d.ShowDialog() == DialogResult.OK ? d.SelectedPath : null;
        }

        DbEngine Engine { get { return (DbEngine)Math.Max(0, cbEngine.SelectedIndex); } }

        BackupKind Kind
        {
            get
            {
                var ks = BackupJob.KindsFor(Engine);
                return ks[Math.Max(0, Math.Min(ks.Length - 1, cbKind.SelectedIndex))];
            }
        }

        void UpdateVerifyLabel()
        {
            lblVerify.ForeColor = conn.Verified ? Theme.Success : Theme.Warning;
            lblVerify.Text = conn.Verified ? "✔ 連線已驗證" : "⚠ 連線尚未驗證（請到「1. 連線」按測試連線）";
        }

        /// <summary>目前填寫的路徑中，屬於網路位置的 (已把 Z: 轉成 UNC)。</summary>
        List<string> NetPaths()
        {
            var list = new List<string>();
            foreach (var p in new[] { txtPath.Text.Trim(), txtSecondary.Text.Trim() })
                if (p != "" && !BackupEngine.IsUrl(p) && NetPath.IsNetwork(p)) list.Add(NetPath.ToUnc(p));
            return list;
        }

        string NetKey()
        {
            string k = string.Join("|", NetPaths()).ToLowerInvariant() + "|" + txtNetUser.Text.Trim().ToLowerInvariant() + "|" + txtNetPwd.Text;
            if (NeedRelay) k += "|mode" + (rbRelay.Checked ? 1 : 0) + "|relay|" + conn.SqlConn.Server.ToLowerInvariant() + "|" + txtHostLocal.Text.Trim().ToLowerInvariant() + "|" + txtHostUnc.Text.Trim().ToLowerInvariant() + "|" + txtHostUser.Text.Trim().ToLowerInvariant() + "|" + txtHostPwd.Text;
            return k;
        }

        void OnPathsChanged()
        {
            var nets = NetPaths();
            bool show = nets.Count > 0;
            bool relay = show && NeedRelay;
            pnlHost.Visible = relay;
            pnlNetBottom.Top = relay ? pnlHost.Bottom : netHostTop;
            pnlNet.Height = pnlNetBottom.Bottom;
            if (relay)
            {
                string host = NetPath.HostOf(conn.SqlConn.Server);
                lblHostInfo.Text = "SQL Server 在 " + host + "，備份檔由 " + host + " 的 SQL 服務帳戶寫入，它預設無法登入 NAS（作業系統錯誤 1326 / 5）。\n請選擇處理方式（主機中轉使用 C$ 系統分享時，帳號需為 " + host + " 的系統管理員）：";
                if (txtHostLocal.Text.Trim() == "")
                {
                    string def = !string.IsNullOrEmpty(conn.DefaultBackupDir) ? conn.DefaultBackupDir.Substring(0, 2) + "\\SqlBackupStage" : "C:\\SqlBackupStage";
                    txtHostLocal.Text = def;
                }
            }
            pnlNet.Visible = show;
            pnlRest.Top = show ? pnlNet.Bottom + Theme.S(6) : restTopNoNet;
            if (show)
            {
                var sb = new System.Text.StringBuilder("偵測到網路路徑：");
                foreach (var p in new[] { txtPath.Text.Trim(), txtSecondary.Text.Trim() })
                {
                    if (p == "" || !NetPath.IsNetwork(p)) continue;
                    string unc = NetPath.ToUnc(p);
                    sb.Append("\n  • ").Append(unc).Append(unc != p ? "（" + p.Substring(0, 2) + " 已自動轉換成網路路徑）" : "");
                }
                sb.Append("\n備份時會先寫到 SQL Server 本機，再用下面的帳號複製過去。");
                lblNetInfo.Text = sb.ToString();
            }
            InvalidateNet();
        }

        void InvalidateNet()
        {
            if (netLoading) return;
            bool ok = netVerified && netVerifiedFor == NetKey();
            lblNetResult.ForeColor = ok ? Theme.Success : Theme.Warning;
            lblNetResult.Text = ok ? "✔ 網路路徑已驗證" : "尚未驗證：請按「驗證網路路徑」，成功後才能儲存";
        }

        void TestNetwork()
        {
            var paths = NetPaths();
            if (paths.Count == 0) return;
            string user = txtNetUser.Text.Trim(), pwd = txtNetPwd.Text, key = NetKey();
            bool relay = NeedRelay;
            var sqlConn = conn.SqlConn;
            string hostLocal = txtHostLocal.Text.Trim(), hostUnc = txtHostUnc.Text.Trim(), hostUser = txtHostUser.Text.Trim(), hostPwd = txtHostPwd.Text;
            int relayMode = rbRelay.Checked ? 1 : 0;
            if (relay && relayMode == 1 && (hostLocal == "" || hostUnc == "")) { lblNetResult.ForeColor = Theme.Danger; lblNetResult.Text = "✖ 請填寫 SQL 主機暫存資料夾與本程式存取路徑"; return; }
            lblNetResult.ForeColor = Theme.SubText; lblNetResult.Text = "驗證中…";
            System.Threading.Tasks.Task.Run(() =>
            {
                string msg = null;
                try
                {
                    foreach (var p in paths) NetShare.TestWrite(p, user, pwd);
                    if (relay && relayMode == 1) NetShare.TestRelay(sqlConn, hostLocal, hostUnc, hostUser, hostPwd);
                    if (relay && relayMode == 0) SqlNetUse.Test(sqlConn, paths[0], user, pwd);
                }
                catch (Exception ex) { msg = ex.Message; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        netVerified = msg == null; netVerifiedFor = netVerified ? key : "";
                        lblNetResult.ForeColor = netVerified ? Theme.Success : Theme.Danger;
                        lblNetResult.Text = netVerified ? "✔ 驗證成功：可以寫入 " + string.Join("、", paths) + (relay ? (relayMode == 1 ? "；SQL 主機中轉正常" : "；SQL Server 已實際寫入 NAS 測試成功") : "") : "✖ 驗證失敗：" + msg;
                    }));
                }
                catch { }
            });
        }

        void FillKinds()
        {
            var cur = cbKind.SelectedIndex >= 0 ? Kind : job.Kind;
            cbKind.Items.Clear();
            var ks = BackupJob.KindsFor(Engine);
            foreach (var k in ks) cbKind.Items.Add(BackupJob.KindText(k));
            int idx = Array.IndexOf(ks, cur);
            cbKind.SelectedIndex = idx >= 0 ? idx : 0;
        }

        void UpdateEngineUi()
        {
            bool ss = Engine == DbEngine.SqlServer;
            var k = Kind;
            bool native = ss && k != BackupKind.Bacpac;
            pnlSqlAdv.Visible = native;
            pnlDumpAdv.Visible = !ss;
            chkChecksum.Enabled = chkCompress.Enabled = chkZip.Enabled = chkCopyOnly.Enabled = chkFallback.Enabled = native;
            chkSingleTx.Enabled = chkRoutines.Enabled = Engine == DbEngine.MySql;
            chkPgCustom.Enabled = Engine == DbEngine.PostgreSql;
            bool globals = k == BackupKind.Globals;
            rbAll.Enabled = rbPick.Enabled = !globals;
            lstDb.Enabled = !globals && rbPick.Checked;
            string h;
            switch (k)
            {
                case BackupKind.Full: h = "備份整個資料庫，是所有還原的基礎。"; break;
                case BackupKind.Differential: h = "只備份上次完整備份後變動的資料，檔案小、速度快；還原需要「完整 + 最近一次差異」。"; break;
                case BackupKind.Log: h = "備份交易記錄，可還原到任一時間點。資料庫需為 FULL 復原模式，建議每 15~60 分鐘一次。"; break;
                case BackupKind.Partial: h = "只備份主要與可讀寫檔案群組（略過唯讀檔案群組），適合有大量唯讀歷史資料的資料庫。"; break;
                case BackupKind.PartialDiff: h = "部分備份的差異版本，基底為最近一次部分備份。"; break;
                case BackupKind.Bacpac: h = "用 SqlPackage 匯出結構 + 資料，可搬到其他版本或 Azure SQL。需安裝 SqlPackage；大型資料庫較慢。"; break;
                case BackupKind.Dump: h = Engine == DbEngine.MySql ? "用 mysqldump 匯出 SQL 檔（需安裝 MySQL / MariaDB 用戶端工具）。" : "用 pg_dump 匯出（需安裝 PostgreSQL 用戶端工具）。"; break;
                default: h = "用 pg_dumpall --globals-only 備份角色（帳號）、密碼與表格空間，搭配各資料庫的 pg_dump 使用。"; break;
            }
            lblKindHint.Text = h;
        }

        static string NameOf(object item) { return item.ToString().Split(new[] { "    " }, StringSplitOptions.None)[0].Trim(); }

        void FillDatabases(List<DbInfo> dbs)
        {
            if (preChecked == null) preChecked = new List<string>(job.Databases);
            preChecked = lstDb.CheckedItems.Cast<object>().Select(NameOf).Union(preChecked).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            lstDb.Items.Clear();
            foreach (var d in dbs)
            {
                string extra = Engine == DbEngine.SqlServer ? "    (" + d.RecoveryModel + "，" + d.SizeMB.ToString("0") + " MB" + (d.IsSystem ? "，系統" : "") + ")" : d.IsSystem ? "    (系統)" : "";
                lstDb.Items.Add(d.Name + extra, preChecked.Contains(d.Name, StringComparer.OrdinalIgnoreCase));
            }
            foreach (var n in preChecked.Where(n => !dbs.Any(d => d.Name.Equals(n, StringComparison.OrdinalIgnoreCase))))
                lstDb.Items.Add(n + "    (找不到)", true);
            lblDbStatus.Text = "共 " + dbs.Count + " 個資料庫";
        }

        void UpdateSchedulePanels()
        {
            var k = (ScheduleKind)Math.Max(0, cbSchedule.SelectedIndex);
            pnlWeek.Visible = k == ScheduleKind.Weekly;
            pnlMonth.Visible = k == ScheduleKind.Monthly;
            pnlInterval.Visible = k == ScheduleKind.Interval;
            UpdatePreview();
        }

        void ToJob(BackupJob j)
        {
            j.Name = txtName.Text.Trim();
            j.Engine = Engine; j.Kind = Kind; j.Enabled = chkEnabled.Checked;
            j.SqlConn = conn.SqlConn; j.ToolConn = conn.ToolConn; j.ConnSet = true;
            j.ConnVerified = conn.Verified; if (conn.Verified) j.ConnVerifiedAt = DateTime.Now;
            j.AllUserDatabases = rbAll.Checked; j.IncludeSystemDatabases = chkSys.Checked;
            j.Databases = lstDb.CheckedItems.Cast<object>().Select(NameOf).ToList();
            j.TargetPath = txtPath.Text.Trim(); j.SecondaryPath = txtSecondary.Text.Trim(); j.SubFolderPerDatabase = chkSub.Checked; j.StageNetwork = true;
            j.NetUser = txtNetUser.Text.Trim(); j.NetPassword = txtNetPwd.Text;
            j.SqlConn.HostStageLocal = txtHostLocal.Text.Trim(); j.SqlConn.HostStageUnc = txtHostUnc.Text.Trim();
            j.SqlConn.HostUser = txtHostUser.Text.Trim(); j.SqlConn.HostPassword = txtHostPwd.Text;
            j.SqlConn.RemoteNetMode = rbRelay.Checked ? 1 : 0; j.NetVerified = NetPaths().Count == 0 || netVerified;
            j.RetentionDays = (int)numRetention.Value;
            j.Verify = chkVerify.Checked; j.Checksum = chkChecksum.Checked; j.Compression = chkCompress.Checked; j.ZipAfterBackup = chkZip.Checked;
            j.CopyOnly = chkCopyOnly.Checked; j.FallbackToFull = chkFallback.Checked;
            j.StripeCount = (int)numStripes.Value; j.MirrorPath = txtMirror.Text.Trim(); j.EncryptCertificate = txtCert.Text.Trim();
            j.EncryptAlgorithm = cbAlgo.SelectedItem as string ?? "AES_256";
            j.SingleTransaction = chkSingleTx.Checked; j.DumpRoutines = chkRoutines.Checked; j.PgCustomFormat = chkPgCustom.Checked; j.GzipDump = chkGzip.Checked;
            ApplySchedule(j);
        }

        void ApplySchedule(BackupJob j)
        {
            j.Schedule = (ScheduleKind)Math.Max(0, cbSchedule.SelectedIndex);
            j.StartDate = dtStart.Value.Date;
            j.TimeOfDayMinutes = dtTime.Value.Hour * 60 + dtTime.Value.Minute;
            j.IntervalEndMinutes = dtEnd.Value.Hour * 60 + dtEnd.Value.Minute;
            j.IntervalMinutes = (int)numInterval.Value;
            j.MonthDay = cbMonthDay.SelectedIndex == 31 ? 0 : cbMonthDay.SelectedIndex + 1;
            int mask = 0; for (int d = 0; d < 7; d++) if (chkDays[d] != null && chkDays[d].Checked) mask |= 1 << d;
            j.WeekDaysMask = mask;
        }

        void UpdatePreview()
        {
            if (cbSchedule.SelectedIndex < 0) return;
            var tmp = job.Clone();
            ApplySchedule(tmp);
            var n = ScheduleCalc.Next(tmp, DateTime.Now);
            lblPreview.Text = tmp.ScheduleText() + "　→　下次執行：" + (n.HasValue ? n.Value.ToString("yyyy/MM/dd (ddd) HH:mm") : "無 (時間已過或未選擇星期)");
        }

        void LoadFromJob()
        {
            var j = job;
            txtName.Text = j.Name;
            conn.Load(j.Engine, j.SqlConn, j.ToolConn, j.ConnVerified);
            cbEngine.SelectedIndex = (int)j.Engine;
            if (cbKind.Items.Count == 0) FillKinds();
            int ki = Array.IndexOf(BackupJob.KindsFor(j.Engine), j.Kind);
            cbKind.SelectedIndex = ki >= 0 ? ki : 0;
            chkEnabled.Checked = j.Enabled;
            rbAll.Checked = j.AllUserDatabases; rbPick.Checked = !j.AllUserDatabases; chkSys.Checked = j.IncludeSystemDatabases;
            lstDb.Enabled = rbPick.Checked; chkSys.Enabled = rbAll.Checked;
            preChecked = new List<string>(j.Databases);
            foreach (var n in j.Databases) lstDb.Items.Add(n, true);
            lblDbStatus.Text = "測試連線後載入清單";
            txtPath.Text = (j.TargetPath ?? "").Trim();
            netLoading = true;
            txtNetUser.Text = j.NetUser; txtNetPwd.Text = j.NetPassword;
            hostUncAuto = true; txtHostLocal.Text = j.SqlConn.HostStageLocal; txtHostUnc.Text = j.SqlConn.HostStageUnc; hostUncAuto = false;
            hostUncEdited = j.SqlConn.HostStageUnc != "" && j.SqlConn.HostStageUnc != NetPath.AdminShare(NetPath.HostOf(j.SqlConn.Server), j.SqlConn.HostStageLocal);
            txtHostUser.Text = j.SqlConn.HostUser; txtHostPwd.Text = j.SqlConn.HostPassword;
            rbRelay.Checked = j.SqlConn.RemoteNetMode == 1; rbNetUse.Checked = !rbRelay.Checked; UpdateRelayFields();
            txtSecondary.Text = j.SecondaryPath;
            netLoading = false;
            netVerified = j.NetVerified && NetPaths().Count > 0;
            netVerifiedFor = netVerified ? NetKey() : "";
            OnPathsChanged();
            chkSub.Checked = j.SubFolderPerDatabase;
            numRetention.Value = Math.Max(0, Math.Min(3650, j.RetentionDays));
            chkVerify.Checked = j.Verify; chkChecksum.Checked = j.Checksum; chkCompress.Checked = j.Compression; chkZip.Checked = j.ZipAfterBackup;
            chkCopyOnly.Checked = j.CopyOnly; chkFallback.Checked = j.FallbackToFull;
            numStripes.Value = Math.Max(1, Math.Min(16, j.StripeCount)); txtMirror.Text = j.MirrorPath; txtCert.Text = j.EncryptCertificate;
            cbAlgo.SelectedItem = string.IsNullOrEmpty(j.EncryptAlgorithm) ? "AES_256" : j.EncryptAlgorithm;
            if (cbAlgo.SelectedIndex < 0) cbAlgo.SelectedIndex = 0;
            chkSingleTx.Checked = j.SingleTransaction; chkRoutines.Checked = j.DumpRoutines; chkPgCustom.Checked = j.PgCustomFormat; chkGzip.Checked = j.GzipDump;
            dtStart.Value = j.StartDate < dtStart.MinDate ? DateTime.Today : j.StartDate;
            dtTime.Value = DateTime.Today.AddMinutes(j.TimeOfDayMinutes);
            dtEnd.Value = DateTime.Today.AddMinutes(j.IntervalEndMinutes);
            numInterval.Value = Math.Max(5, Math.Min(1440, j.IntervalMinutes));
            cbMonthDay.SelectedIndex = j.MonthDay <= 0 ? 31 : Math.Min(31, j.MonthDay) - 1;
            for (int d = 0; d < 7; d++) chkDays[d].Checked = (j.WeekDaysMask & (1 << d)) != 0;
            cbSchedule.SelectedIndex = (int)j.Schedule;
            UpdateEngineUi();
            UpdateSchedulePanels();
            UpdateVerifyLabel();
        }

        void Save()
        {
            string err = null; int tab = -1;
            string name = txtName.Text.Trim();
            if (name == "") { err = "請輸入工作名稱。"; tab = 0; }
            else if (SettingsStore.Current.Jobs.Any(x => x != original && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { err = "已有相同名稱的工作，請換一個名稱（命令列 --run 會用名稱找工作）。"; tab = 0; }
            else if (!conn.Verified) { err = "連線尚未驗證成功。\n請在「1. 連線」頁按「測試連線」，成功後才能儲存。"; tab = 0; }
            else if (txtPath.Text.Trim() == "") { err = "請指定備份資料夾（可按「瀏覽…」選擇，或按「用伺服器預設」）。"; tab = 2; }

            else if (BackupEngine.IsUrl(txtPath.Text.Trim()) && (Engine != DbEngine.SqlServer || Kind == BackupKind.Bacpac)) { err = "只有 SQL Server 原生備份可以備份到 URL。"; tab = 2; }
            else if (NetPaths().Count > 0 && !(netVerified && netVerifiedFor == NetKey())) { err = "偵測到網路路徑，請在「3. 存放位置與選項」輸入網路帳號密碼，並按「驗證網路路徑」成功後才能儲存。"; tab = 2; }
            else if (rbPick.Checked && Kind != BackupKind.Globals && lstDb.CheckedItems.Count == 0) { err = "請至少勾選一個資料庫，或改選「全部使用者資料庫」。"; tab = 1; }
            else if (cbSchedule.SelectedIndex == (int)ScheduleKind.Weekly && !chkDays.Any(c => c.Checked)) { err = "每週排程請至少勾選一天。"; tab = 3; }
            if (err != null)
            {
                if (tab >= 0) tabs.SelectedIndex = tab;
                MessageBox.Show(err, "備份工作", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!PathHelper.LooksUsable(txtPath.Text))
            {
                tabs.SelectedIndex = 2;
                if (MessageBox.Show("這台電腦找不到備份資料夾的磁碟機：\n" + txtPath.Text.Trim() +
                    "\n\n如果 SQL Server 在另一台電腦，而這是那台電腦上的路徑，可以按「是」繼續；\n否則請按「否」修改路徑。",
                    "備份資料夾", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            ToJob(job);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
