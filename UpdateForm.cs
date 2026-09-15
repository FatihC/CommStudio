using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed class UpdateForm : Form
    {
        private readonly AvailableUpdate update;
        private readonly Label status;
        private readonly ProgressBar progress;
        private readonly Button install;
        private readonly Button cancel;
        private CancellationTokenSource cancellation;
        internal string PreparedDirectory { get; private set; }

        internal UpdateForm(AvailableUpdate available, bool dark)
        {
            update = available;
            Text = "CommStudio güncellemesi";
            Name = "updateForm";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10F);
            ClientSize = new Size(550, 280);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(245, 247, 250);
            ForeColor = dark ? Color.FromArgb(236, 239, 244) : Color.FromArgb(30, 42, 58);
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            Label heading = new Label
            {
                Dock = DockStyle.Fill, Text = "Yeni sürüm: " + update.Version.ToString(3)
                    + "   (mevcut: " + UpdateService.CurrentVersion.ToString(3) + ")\n\n"
                    + "İndirilecek dosya: " + (update.Asset.Size / 1048576.0).ToString("0.0") + " MB",
                AutoSize = false
            };
            status = new Label
            {
                Dock = DockStyle.Fill, Text = "Güncelleme sonrası uygulama yeniden açılacak.\nAktif bağlantılar kapanacak; ayarlarınız korunacak.", AutoSize = false
            };
            progress = new ProgressBar { Dock = DockStyle.Fill, Visible = false, Margin = new Padding(0, 2, 0, 6) };
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty
            };
            cancel = new Button { Text = "Daha sonra", AutoSize = true, Height = 34, Padding = new Padding(8, 3, 8, 3) };
            install = new Button { Text = "Güncelle ve yeniden başlat", AutoSize = true, Height = 34, Padding = new Padding(8, 3, 8, 3) };
            foreach (Button button in new[] { cancel, install })
            {
                button.BackColor = dark ? Color.FromArgb(54, 54, 54) : Color.White;
                button.ForeColor = ForeColor;
                button.FlatStyle = FlatStyle.Flat;
                buttons.Controls.Add(button);
            }
            cancel.Click += delegate { Close(); };
            install.Click += InstallClicked;
            CancelButton = cancel;
            AcceptButton = install;
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(status, 0, 1);
            layout.Controls.Add(progress, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (cancellation == null) return;
                e.Cancel = true;
                cancellation.Cancel();
                status.Text = "İndirme iptal ediliyor…";
            };
        }

        private async void InstallClicked(object sender, EventArgs e)
        {
            install.Enabled = false;
            cancel.Text = "İptal";
            progress.Visible = true;
            progress.Value = 0;
            status.Text = "Yeni sürüm indiriliyor…";
            bool completed = false;
            bool cancelled = false;
            using (cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
            {
                try
                {
                    PreparedDirectory = await UpdateService.PrepareAsync(update, Application.ExecutablePath,
                        new Progress<int>(delegate(int percent)
                        {
                            if (IsDisposed) return;
                            progress.Value = Math.Max(0, Math.Min(100, percent));
                            status.Text = percent >= 100 ? "Dosya doğrulanıyor…" : "Yeni sürüm indiriliyor… %" + percent;
                        }), cancellation.Token);
                    if (cancellation.IsCancellationRequested)
                    {
                        UpdateService.Cleanup(PreparedDirectory);
                        PreparedDirectory = null;
                        cancelled = true;
                    }
                    else completed = true;
                }
                catch (Exception error)
                {
                    cancelled = cancellation.IsCancellationRequested;
                    if (!cancelled)
                    {
                        status.Text = "Güncelleme indirilemedi. Mevcut sürümü kullanabilirsiniz.";
                        MessageBox.Show(this, error.Message, "Güncelleme", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            cancellation = null;
            if (completed) { DialogResult = DialogResult.OK; Close(); }
            else if (cancelled) Close();
            else { install.Enabled = true; cancel.Text = "Kapat"; }
        }
    }
}
