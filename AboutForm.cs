using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed class AboutForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

        private readonly List<Font> ownedFonts = new List<Font>();
        private readonly Color primary;
        private readonly Color secondary;
        private readonly Color accent;
        private readonly Color surface;
        private Image brandImage;

        public AboutForm(bool dark)
        {
            Text = "CommStudio Hakkında";
            Name = "aboutForm";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 448);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = MakeFont(10F, false);
            BackColor = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(245, 247, 250);
            primary = dark ? Color.FromArgb(236, 239, 244) : Color.FromArgb(30, 42, 58);
            secondary = dark ? Color.FromArgb(174, 184, 198) : Color.FromArgb(88, 103, 121);
            accent = dark ? Color.FromArgb(144, 202, 249) : Color.FromArgb(32, 111, 214);
            surface = dark ? Color.FromArgb(45, 45, 45) : Color.White;
            ForeColor = primary;

            using (Stream stream = typeof(AboutForm).Assembly.GetManifestResourceStream("CommStudio.Icon"))
            using (Icon icon = new Icon(stream, new Size(64, 64)))
            {
                Icon = (Icon)icon.Clone();
            }
            using (Stream stream = typeof(AboutForm).Assembly.GetManifestResourceStream("CommStudio.Logo"))
            using (Image source = Image.FromStream(stream)) brandImage = new Bitmap(source);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 20),
                ColumnCount = 1, RowCount = 6, Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            foreach (float height in new float[] { 86, 40, 70, 126, 36 })
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel hero = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty
            };
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            hero.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            PictureBox logo = new PictureBox
            {
                Image = brandImage, SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(64, 64), Margin = new Padding(0, 3, 20, 0), TabStop = false
            };
            hero.Controls.Add(logo, 0, 0);
            hero.SetRowSpan(logo, 2);
            hero.Controls.Add(MakeLabel("CommStudio", 25F, true, primary), 1, 0);
            Label version = MakeLabel("Sürüm " + typeof(AboutForm).Assembly.GetName().Version.ToString(3)
                + "  ·  Windows", 9.5F, false, secondary);
            version.Name = "versionLabel";
            hero.Controls.Add(version, 1, 1);
            layout.Controls.Add(hero, 0, 0);

            FlowLayoutPanel protocols = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty
            };
            foreach (string protocol in new string[] { "TCP", "SERIAL", "MQTT" })
            {
                Label badge = MakeLabel(protocol, 8F, true, accent);
                badge.Dock = DockStyle.None;
                badge.Size = new Size(72, 26);
                badge.BackColor = dark ? Color.FromArgb(39, 58, 77) : Color.FromArgb(229, 239, 253);
                badge.TextAlign = ContentAlignment.MiddleCenter;
                badge.Margin = new Padding(0, 0, 8, 0);
                protocols.Controls.Add(badge);
            }
            layout.Controls.Add(protocols, 0, 1);
            Label description = MakeLabel("Cihazlarla iletişimi sadeleştirir.\nKomut gönderin, veri akışını inceleyin ve MQTT\nmesajlarınızı tek bir çalışma alanından yönetin.", 10F, false, secondary);
            layout.Controls.Add(description, 0, 2);

            TableLayoutPanel credit = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(18, 13, 18, 12),
                Margin = new Padding(0, 0, 0, 8), ColumnCount = 1, RowCount = 3
            };
            credit.RowStyles.Add(new RowStyle(SizeType.Absolute, 23F));
            credit.RowStyles.Add(new RowStyle(SizeType.Absolute, 31F));
            credit.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            credit.Controls.Add(MakeLabel("GELİŞTİRİCİ", 8F, true, accent), 0, 0);
            Label developer = MakeLabel("Fatih Coşkun", 15F, true, primary);
            developer.Name = "developerLabel";
            credit.Controls.Add(developer, 0, 1);
            LinkLabel repository = new LinkLabel
            {
                Name = "repositoryLink", Text = "github.com/FatihC/CommStudio", Dock = DockStyle.Fill,
                Font = MakeFont(9F, false), LinkColor = accent, ActiveLinkColor = accent,
                VisitedLinkColor = accent, LinkBehavior = LinkBehavior.HoverUnderline, Margin = Padding.Empty
            };
            repository.Links.Add(0, repository.Text.Length, "https://github.com/FatihC/CommStudio");
            repository.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs e)
            {
                try { Process.Start(new ProcessStartInfo((string)e.Link.LinkData) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show(this, "Bağlantı açılamadı. Tarayıcınızda şu adresi ziyaret edebilirsiniz:\n\n"
                        + e.Link.LinkData, "GitHub deposu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            credit.Controls.Add(repository, 0, 2);
            layout.Controls.Add(credit, 0, 3);
            layout.Controls.Add(MakeLabel("© 2026 Fatih Coşkun  ·  MQTT altyapısı: MQTTnet (MIT)", 8.5F, false, secondary), 0, 4);

            Button close = new Button
            {
                Name = "closeButton", Text = "&Kapat", DialogResult = DialogResult.OK,
                Size = new Size(100, 34), Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(32, 111, 214),
                ForeColor = Color.White, Cursor = Cursors.Hand, Margin = Padding.Empty,
                UseVisualStyleBackColor = false
            };
            close.FlatAppearance.BorderSize = 0;
            close.FlatAppearance.MouseOverBackColor = Color.FromArgb(27, 96, 189);
            layout.Controls.Add(close, 0, 5);
            AcceptButton = close;
            CancelButton = close;
            Shown += delegate { close.Select(); };
            Controls.Add(layout);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 3, BackColor = accent });
            HandleCreated += delegate
            {
                try
                {
                    int enabled = dark ? 1 : 0;
                    if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
                        DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            };
        }

        private Font MakeFont(float size, bool bold)
        {
            Font font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            ownedFonts.Add(font);
            return font;
        }

        private Label MakeLabel(string text, float size, bool bold, Color color)
        {
            return new Label
            {
                Text = text, Dock = DockStyle.Fill, Font = MakeFont(size, bold),
                ForeColor = color, Margin = Padding.Empty, UseMnemonic = false
            };
        }

        protected override void Dispose(bool disposing)
        {
            Icon icon = disposing ? Icon : null;
            base.Dispose(disposing);
            if (!disposing) return;
            if (brandImage != null) { brandImage.Dispose(); brandImage = null; }
            if (icon != null) icon.Dispose();
            foreach (Font font in ownedFonts) font.Dispose();
            ownedFonts.Clear();
        }
    }
}
