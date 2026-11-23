using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlBackupManager
{
    public class JobEditForm : Form
    {
        readonly BackupJob job;
        readonly BackupJob original;

        TextBox txtName = new TextBox();
        ComboBox cbEngine = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        ComboBox cbKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        Label lblKindHint = new Label { AutoSize = false };
        CheckBox chkEnabled;
        RadioButton rbAll = new RadioButton { Text = "全部使用者資料庫（之後新增的資料庫也會自動備份）", AutoSize = true };
        RadioButton rbPick = new RadioButton { Text = "指定資料庫", AutoSize = true };
        CheckBox chkSys;
        CheckedListBox lstDb = new CheckedListBox { CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
        Label lblDbStatus = new Label { AutoSize = true };

        TextBox txtPath = new TextBox(), txtSecondary = new TextBox(), txtMirror = new TextBox(), txtCert = new TextBox();
        CheckBox chkSub, chkVerify, chkChecksum, chkCompress, chkZip, chkCopyOnly, chkFallback, chkSingleTx, chkRoutines, chkPgCustom, chkGzip;
        NumericUpDown numRetention = new NumericUpDown { Minimum = 0, Maximum = 3650 };
        NumericUpDown numStripes = new NumericUpDown { Minimum = 1, Maximum = 16 };
        ComboBox cbAlgo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        Panel pnlSqlAdv = new Panel { BackColor = Color.White }, pnlDumpAdv = new Panel { BackColor = Color.White };

        ComboBox cbSchedule = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        DateTimePicker dtStart = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd" };
        DateTimePicker dtTime = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
        DateTimePicker dtEnd = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
        Panel pnlWeek = new Panel { BackColor = Color.White, Height = 30 }, pnlMonth = new Panel { BackColor = Color.White, Height = 34 }, pnlInterval = new Panel { BackColor = Color.White, Height = 80 };
        CheckBox[] chkDays = new CheckBox[7];
        ComboBox cbMonthDay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        NumericUpDown numInterval = new NumericUpDown { Minimum = 5, Maximum = 1440, Increment = 5 };
        Label lblPreview = new Label { AutoSize = true };
        List<string> preChecked;

        public JobEditForm(BackupJob work, BackupJob original)
        {
            job = work; this.original = original;
            Text = original == null ? "新增備份工作" : "編輯備份工作 - " + original.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = Theme.F(9.5f);
            ClientSize = new Size(820, 660);
            Icon = Theme.MakeIcon(Color.Empty);

            var tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.F(10f), Padding = new Point(18, 6) };
            var t1 = new TabPage("一般") { BackColor = Color.White };
            var t2 = new TabPage("存放位置與選項") { BackColor = Color.White, AutoScroll = true };
            var t3 = new TabPage("排程") { BackColor = Color.White };
            tabs.TabPages.AddRange(new[] { t1, t2, t3 });

            // ── 一般
            var f1 = new FieldLayout(t1, 20, 18, 120);
            f1.Row("工作名稱", txtName, 360);
            f1.Row("資料庫類型", cbEngine, 220);
            f1.Row("備份類型", cbKind, 220);
            lblKindHint.Font = Theme.F(9f); lblKindHint.ForeColor = Theme.SubText; lblKindHint.Size = new Size(640, 38);
            f1.Y -= 6; f1.Row(null, lblKindHint, 640); f1.Y -= 4;
            chkEnabled = f1.Check("啟用此工作 (依排程自動執行)", true);
            f1.Section("要備份的資料庫");
            rbAll.Font = rbPick.Font = Theme.F(9.5f);
            rbAll.Location = new Point(20, f1.Y); t1.Controls.Add(rbAll); f1.Y += 30;
            chkSys = f1.Check("包含系統資料庫", false, 24);
            rbPick.Location = new Point(20, f1.Y); t1.Controls.Add(rbPick); f1.Y += 30;
            lstDb.Font = Theme.F(9.5f); lstDb.Location = new Point(44, f1.Y); lstDb.Size = new Size(520, 150); t1.Controls.Add(lstDb);
            var reload = Theme.Btn("重新載入"); reload.Location = new Point(576, f1.Y); t1.Controls.Add(reload);
            lblDbStatus.Font = Theme.F(9f); lblDbStatus.ForeColor = Theme.SubText; lblDbStatus.Location = new Point(576, f1.Y + 40); t1.Controls.Add(lblDbStatus);
            reload.Click += (s, e) => LoadDatabases();

            // ── 存放與選項
            var f2 = new FieldLayout(t2, 20, 18, 130);
            f2.Row("備份資料夾", txtPath, 440);
            var b1 = Theme.Btn("瀏覽…"); b1.Location = new Point(txtPath.Right + 8, txtPath.Top - 3); t2.Controls.Add(b1);
            f2.Note("可填本機路徑、網路路徑 (\\\\NAS\\Backup)；SQL Server 2012 SP1 CU2 以上也可填 Azure Blob 網址 (https://帳戶.blob.core.windows.net/容器，需先建立 Credential)。", 640);
            chkSub = f2.Check("每個資料庫建立子資料夾", true, 130);
            f2.Row("第二備份位置", txtSecondary, 440);
            var b2 = Theme.Btn("瀏覽…"); b2.Location = new Point(txtSecondary.Right + 8, txtSecondary.Top - 3); t2.Controls.Add(b2);
            f2.Y -= 8; f2.Note("選填。備份完成後由本程式複製一份過去（異地備份），不需要 SQL 服務帳戶的權限。", 640);
            f2.Row("保留天數", numRetention, 80, "超過天數的舊備份自動刪除；0 = 永久保留");
            f2.Section("備份選項");
            int yOpt = f2.Y;
            chkVerify = f2.Check("備份後驗證備份檔", true);
            chkChecksum = f2.Check("計算總和檢查碼 (CHECKSUM)", true);
            chkCompress = f2.Check("原生備份壓縮 (Standard 以上)", true);
            chkZip = f2.Check("備份後壓縮成 ZIP (Express 適用)", false);
            int yEnd = f2.Y;
            f2.X = 380; f2.Y = yOpt;
            chkCopyOnly = f2.Check("僅複製備份 (COPY_ONLY，不影響備份鏈)", false);
            chkFallback = f2.Check("找不到基底備份時自動改做完整備份", true);
            f2.X = 20; f2.Y = Math.Max(yEnd, f2.Y);

            // SQL Server 進階
            pnlSqlAdv.Location = new Point(0, f2.Y); pnlSqlAdv.Size = new Size(780, 210); t2.Controls.Add(pnlSqlAdv);
            var fa = new FieldLayout(pnlSqlAdv, 20, 0, 130);
            fa.Section("SQL Server 進階");
            fa.Row("分割檔案數", numStripes, 70, "Striped：同時寫入多個檔案，加快大型資料庫備份");
            fa.Row("鏡像備份路徑", txtMirror, 440, null);
            fa.Y -= 10; fa.Note("Mirror：同時寫一份完全相同的備份到另一個位置（僅 Enterprise / Developer 版）。", 640);
            fa.Row("加密憑證", txtCert, 220);
            cbAlgo.Items.AddRange(new object[] { "AES_256", "AES_192", "AES_128", "TRIPLE_DES_3KEY" });
            cbAlgo.Font = Theme.F(9.5f); cbAlgo.Location = new Point(txtCert.Right + 10, txtCert.Top); cbAlgo.Width = 150; pnlSqlAdv.Controls.Add(cbAlgo);
            fa.Y -= 10; fa.Note("填入 master 中的伺服器憑證名稱即啟用備份加密（Standard 以上，2014+）。請務必另外備份憑證與私密金鑰，否則無法還原！", 640, Theme.Warning);

            // MySQL / PostgreSQL 進階
            pnlDumpAdv.Location = new Point(0, f2.Y); pnlDumpAdv.Size = new Size(780, 180); t2.Controls.Add(pnlDumpAdv);
            var fd = new FieldLayout(pnlDumpAdv, 20, 0, 130);
            fd.Section("Dump 選項");
            chkSingleTx = fd.Check("--single-transaction (InnoDB 線上備份不鎖表)", true);
            chkRoutines = fd.Check("包含預存程序、函數、觸發程序、事件", true);
            chkPgCustom = fd.Check("PostgreSQL 使用 custom 格式 (-Fc，可選擇性還原，已壓縮)", true);
            chkGzip = fd.Check("純 SQL 輸出以 gzip 壓縮 (.sql.gz)", true);

            b1.Click += (s, e) => { var p = BrowseFolder(txtPath.Text); if (p != null) txtPath.Text = p; };
            b2.Click += (s, e) => { var p = BrowseFolder(txtSecondary.Text); if (p != null) txtSecondary.Text = p; };

            // ── 排程
            var f3 = new FieldLayout(t3, 20, 18, 120);
            cbSchedule.Items.AddRange(new object[] { "單次", "每天", "每週", "每月", "固定間隔" });
            f3.Row("排程方式", cbSchedule, 160);
            f3.Row("開始日期", dtStart, 140);
            f3.Row("執行時間", dtTime, 100);
            int ySched = f3.Y;
            string[] dn = { "日", "一", "二", "三", "四", "五", "六" };
            var wl = Theme.Lbl("星期"); wl.Location = new Point(0, 4); pnlWeek.Controls.Add(wl);
            for (int i = 0; i < 7; i++)
            {
                int d = (i + 1) % 7; // 一 ~ 日
                chkDays[d] = new CheckBox { Text = dn[d], AutoSize = true, Font = Theme.F(9.5f), Location = new Point(120 + i * 58, 2) };
                pnlWeek.Controls.Add(chkDays[d]);
                chkDays[d].CheckedChanged += (s, e) => UpdatePreview();
            }
            var ml = Theme.Lbl("每月日期"); ml.Location = new Point(0, 6); pnlMonth.Controls.Add(ml);
            for (int i = 1; i <= 31; i++) cbMonthDay.Items.Add(i + " 日");
            cbMonthDay.Items.Add("最後一天");
            cbMonthDay.Font = Theme.F(9.5f); cbMonthDay.Location = new Point(120, 2); cbMonthDay.Width = 120; pnlMonth.Controls.Add(cbMonthDay);
            var il = Theme.Lbl("間隔 (分鐘)"); il.Location = new Point(0, 6); pnlInterval.Controls.Add(il);
            numInterval.Font = Theme.F(9.5f); numInterval.Location = new Point(120, 2); numInterval.Width = 90; pnlInterval.Controls.Add(numInterval);
            var ih = Theme.Lbl("例如交易記錄每 15 / 30 / 60 分鐘", 8.5f, FontStyle.Regular, Theme.SubText); ih.Location = new Point(220, 6); pnlInterval.Controls.Add(ih);
            var el = Theme.Lbl("結束時間"); el.Location = new Point(0, 46); pnlInterval.Controls.Add(el);
            dtEnd.Font = Theme.F(9.5f); dtEnd.Location = new Point(120, 42); dtEnd.Width = 100; pnlInterval.Controls.Add(dtEnd);
            var eh = Theme.Lbl("與執行時間相同 = 全天執行", 8.5f, FontStyle.Regular, Theme.SubText); eh.Location = new Point(230, 46); pnlInterval.Controls.Add(eh);
            foreach (var p in new Panel[] { pnlWeek, pnlMonth, pnlInterval }) { p.Location = new Point(20, ySched); p.Width = 720; t3.Controls.Add(p); }
            var prevCard = new Panel { BackColor = Color.FromArgb(240, 246, 253), Location = new Point(20, ySched + 100), Size = new Size(740, 46) };
            lblPreview.Font = Theme.F(10f, FontStyle.Bold); lblPreview.ForeColor = Theme.Accent; lblPreview.Location = new Point(14, 13);
            prevCard.Controls.Add(lblPreview); t3.Controls.Add(prevCard);
            var schedNote = new Label { Font = Theme.F(9f), ForeColor = Theme.SubText, Location = new Point(20, ySched + 160), Size = new Size(740, 80),
                Text = "建議排程：\n• 每週日 01:00 完整備份 + 每天 01:00 差異備份 + 每 30 分鐘交易記錄備份（FULL 復原模式）\n• 小型資料庫：每天完整備份即可\n• MySQL / PostgreSQL：每天 dump 一次" };
            t3.Controls.Add(schedNote);

            foreach (Control c in new Control[] { cbSchedule, dtStart, dtTime, dtEnd, cbMonthDay, numInterval })
            {
                if (c is ComboBox) ((ComboBox)c).SelectedIndexChanged += (s, e) => UpdateSchedulePanels();
                else if (c is DateTimePicker) ((DateTimePicker)c).ValueChanged += (s, e) => UpdatePreview();
                else ((NumericUpDown)c).ValueChanged += (s, e) => UpdatePreview();
            }

            // ── 底部按鈕
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, Width = ClientSize.Width, BackColor = Color.FromArgb(247, 249, 251) };
            var ok = Theme.Btn("儲存", true, 100); var cancel = Theme.Btn("取消", false, 90);
            ok.Anchor = cancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            ok.Location = new Point(ClientSize.Width - 210, 14); cancel.Location = new Point(ClientSize.Width - 104, 14);
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            ok.Click += (s, e) => Save();
            bottom.Controls.Add(ok); bottom.Controls.Add(cancel);
            var hint = Theme.Lbl("排程時間到時，程式需在執行中（可縮小在右下角）", 9f, FontStyle.Regular, Theme.SubText); hint.Location = new Point(20, 22); bottom.Controls.Add(hint);

            Controls.Add(tabs);
            Controls.Add(bottom);

            foreach (DbEngine e in Enum.GetValues(typeof(DbEngine))) cbEngine.Items.Add(BackupJob.EngineText(e));
            cbEngine.SelectedIndexChanged += (s, e) =>
            {
                if (loaded) { lstDb.Items.Clear(); preChecked = new List<string>(); }
                FillKinds(); UpdateEngineUi(); LoadDatabases();
            };
            cbKind.SelectedIndexChanged += (s, e) => UpdateEngineUi();
            rbAll.CheckedChanged += (s, e) => { lstDb.Enabled = rbPick.Checked; chkSys.Enabled = rbAll.Checked; };

            LoadFromJob();
            loaded = true;
        }

        bool loaded;

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
            chkSingleTx.Enabled = Engine == DbEngine.MySql;
            chkPgCustom.Enabled = Engine == DbEngine.PostgreSql;
            chkRoutines.Enabled = Engine == DbEngine.MySql;
            bool globals = k == BackupKind.Globals;
            rbAll.Enabled = rbPick.Enabled = lstDb.Enabled = !globals;
            if (!globals) lstDb.Enabled = rbPick.Checked;
            string h;
            switch (k)
            {
                case BackupKind.Full: h = "備份整個資料庫，是所有還原的基礎。"; break;
                case BackupKind.Differential: h = "只備份上次完整備份後變動的資料，檔案小、速度快；還原需要「完整 + 最近一次差異」。"; break;
                case BackupKind.Log: h = "備份交易記錄，可還原到任一時間點。資料庫需為 FULL 復原模式，建議每 15~60 分鐘一次。"; break;
                case BackupKind.Partial: h = "只備份主要檔案群組與所有可讀寫檔案群組（略過唯讀檔案群組），適合有大量唯讀歷史資料的資料庫。"; break;
                case BackupKind.PartialDiff: h = "部分備份的差異版本，基底為最近一次部分備份。"; break;
                case BackupKind.Bacpac: h = "用 SqlPackage 匯出結構 + 資料，可搬到其他版本或 Azure SQL。需安裝 SqlPackage；大型資料庫較慢。"; break;
                case BackupKind.Dump: h = Engine == DbEngine.MySql ? "用 mysqldump 匯出 SQL 檔 (需安裝 MySQL / MariaDB 用戶端工具)。" : "用 pg_dump 匯出 (需安裝 PostgreSQL 用戶端工具)。"; break;
                default: h = "用 pg_dumpall --globals-only 備份角色 (帳號)、密碼與表格空間，搭配各資料庫的 pg_dump 使用。"; break;
            }
            lblKindHint.Text = h;
        }

        void LoadDatabases()
        {
            var eng = Engine;
            if (preChecked == null) preChecked = new List<string>(job.Databases);
            else preChecked = lstDb.CheckedItems.Cast<object>().Select(o => o.ToString().Split(new[] { "    " }, StringSplitOptions.None)[0]).Union(preChecked).ToList();
            lblDbStatus.Text = "載入中…";
            Task.Run(() =>
            {
                try
                {
                    var dbs = eng == DbEngine.SqlServer ? SqlOps.GetDatabases(SettingsStore.Current.Connection) : eng == DbEngine.MySql ? MySqlTool.Databases() : PgTool.Databases();
                    BeginInvoke((Action)(() =>
                    {
                        if (Engine != eng) return;
                        lstDb.Items.Clear();
                        foreach (var d in dbs)
                        {
                            string extra = eng == DbEngine.SqlServer ? "    (" + d.RecoveryModel + "，" + d.SizeMB.ToString("0") + " MB" + (d.IsSystem ? "，系統" : "") + ")" : d.IsSystem ? "    (系統)" : "";
                            lstDb.Items.Add(d.Name + extra, preChecked.Contains(d.Name, StringComparer.OrdinalIgnoreCase));
                        }
                        foreach (var n in preChecked.Where(n => !dbs.Any(d => d.Name.Equals(n, StringComparison.OrdinalIgnoreCase))))
                            lstDb.Items.Add(n + "    (找不到)", true);
                        lblDbStatus.Text = "共 " + dbs.Count + " 個";
                    }));
                }
                catch (Exception ex)
                {
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            lstDb.Items.Clear();
                            foreach (var n in preChecked) lstDb.Items.Add(n, true);
                            lblDbStatus.Text = "無法連線";
                            new ToolTip().SetToolTip(lblDbStatus, ex.Message);
                        }));
                    }
                    catch { }
                }
            });
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
            j.AllUserDatabases = rbAll.Checked; j.IncludeSystemDatabases = chkSys.Checked;
            j.Databases = lstDb.CheckedItems.Cast<object>().Select(o => o.ToString().Split(new[] { "    " }, StringSplitOptions.None)[0].Trim()).ToList();
            j.TargetPath = txtPath.Text.Trim(); j.SecondaryPath = txtSecondary.Text.Trim(); j.SubFolderPerDatabase = chkSub.Checked;
            j.RetentionDays = (int)numRetention.Value;
            j.Verify = chkVerify.Checked; j.Checksum = chkChecksum.Checked; j.Compression = chkCompress.Checked; j.ZipAfterBackup = chkZip.Checked;
            j.CopyOnly = chkCopyOnly.Checked; j.FallbackToFull = chkFallback.Checked;
            j.StripeCount = (int)numStripes.Value; j.MirrorPath = txtMirror.Text.Trim(); j.EncryptCertificate = txtCert.Text.Trim();
            j.EncryptAlgorithm = cbAlgo.SelectedItem as string ?? "AES_256";
            j.SingleTransaction = chkSingleTx.Checked; j.DumpRoutines = chkRoutines.Checked; j.PgCustomFormat = chkPgCustom.Checked; j.GzipDump = chkGzip.Checked;
            j.Schedule = (ScheduleKind)Math.Max(0, cbSchedule.SelectedIndex);
            j.StartDate = dtStart.Value.Date;
            j.TimeOfDayMinutes = dtTime.Value.Hour * 60 + dtTime.Value.Minute;
            j.IntervalEndMinutes = dtEnd.Value.Hour * 60 + dtEnd.Value.Minute;
            j.IntervalMinutes = (int)numInterval.Value;
            j.MonthDay = cbMonthDay.SelectedIndex == 31 ? 0 : cbMonthDay.SelectedIndex + 1;
            int mask = 0; for (int d = 0; d < 7; d++) if (chkDays[d].Checked) mask |= 1 << d;
            j.WeekDaysMask = mask;
        }

        void UpdatePreview()
        {
            var tmp = job.Clone();
            try { ToJob(tmp); } catch { return; }
            var n = ScheduleCalc.Next(tmp, DateTime.Now);
            lblPreview.Text = tmp.ScheduleText() + "　→　下次執行：" + (n.HasValue ? n.Value.ToString("yyyy/MM/dd (ddd) HH:mm") : "無 (時間已過或未選擇星期)");
        }

        void LoadFromJob()
        {
            var j = job;
            txtName.Text = j.Name;
            cbEngine.SelectedIndex = (int)j.Engine;   // 會觸發 FillKinds + LoadDatabases
            if (cbKind.Items.Count == 0) FillKinds();
            int ki = Array.IndexOf(BackupJob.KindsFor(j.Engine), j.Kind);
            cbKind.SelectedIndex = ki >= 0 ? ki : 0;
            chkEnabled.Checked = j.Enabled;
            rbAll.Checked = j.AllUserDatabases; rbPick.Checked = !j.AllUserDatabases; chkSys.Checked = j.IncludeSystemDatabases;
            lstDb.Enabled = rbPick.Checked; chkSys.Enabled = rbAll.Checked;
            txtPath.Text = j.TargetPath; txtSecondary.Text = j.SecondaryPath; chkSub.Checked = j.SubFolderPerDatabase;
            numRetention.Value = Math.Max(0, Math.Min(3650, j.RetentionDays));
            chkVerify.Checked = j.Verify; chkChecksum.Checked = j.Checksum; chkCompress.Checked = j.Compression; chkZip.Checked = j.ZipAfterBackup;
            chkCopyOnly.Checked = j.CopyOnly; chkFallback.Checked = j.FallbackToFull;
            numStripes.Value = Math.Max(1, Math.Min(16, j.StripeCount)); txtMirror.Text = j.MirrorPath; txtCert.Text = j.EncryptCertificate;
            cbAlgo.SelectedItem = string.IsNullOrEmpty(j.EncryptAlgorithm) ? "AES_256" : j.EncryptAlgorithm;
            if (cbAlgo.SelectedIndex < 0) cbAlgo.SelectedIndex = 0;
            chkSingleTx.Checked = j.SingleTransaction; chkRoutines.Checked = j.DumpRoutines; chkPgCustom.Checked = j.PgCustomFormat; chkGzip.Checked = j.GzipDump;
            cbSchedule.SelectedIndex = (int)j.Schedule;
            dtStart.Value = j.StartDate < dtStart.MinDate ? DateTime.Today : j.StartDate;
            dtTime.Value = DateTime.Today.AddMinutes(j.TimeOfDayMinutes);
            dtEnd.Value = DateTime.Today.AddMinutes(j.IntervalEndMinutes);
            numInterval.Value = Math.Max(5, Math.Min(1440, j.IntervalMinutes));
            cbMonthDay.SelectedIndex = j.MonthDay <= 0 ? 31 : Math.Min(31, j.MonthDay) - 1;
            for (int d = 0; d < 7; d++) chkDays[d].Checked = (j.WeekDaysMask & (1 << d)) != 0;
            UpdateEngineUi();
            UpdateSchedulePanels();
        }

        void Save()
        {
            string err = null;
            string name = txtName.Text.Trim();
            if (name == "") err = "請輸入工作名稱。";
            else if (SettingsStore.Current.Jobs.Any(x => x != original && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) err = "已有相同名稱的工作，請換一個名稱（命令列 --run 會用名稱找工作）。";
            else if (txtPath.Text.Trim() == "") err = "請輸入備份資料夾。";
            else if (BackupEngine.IsUrl(txtPath.Text.Trim()) && (Engine != DbEngine.SqlServer || Kind == BackupKind.Bacpac)) err = "只有 SQL Server 原生備份可以備份到 URL。";
            else if (rbPick.Checked && Kind != BackupKind.Globals && lstDb.CheckedItems.Count == 0) err = "請至少勾選一個資料庫，或改選「全部使用者資料庫」。";
            else if (cbSchedule.SelectedIndex == (int)ScheduleKind.Weekly && !chkDays.Any(c => c.Checked)) err = "每週排程請至少勾選一天。";
            if (err != null) { MessageBox.Show(err, "備份工作", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            ToJob(job);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
