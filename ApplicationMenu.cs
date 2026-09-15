using System;
using System.Drawing;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed partial class MainForm
    {
        private Button applicationMenuButton;
        private ContextMenuStrip applicationMenu;

        private Button BuildApplicationMenuButton()
        {
            applicationMenu = new ContextMenuStrip
            {
                Name = "applicationMenu", ShowImageMargin = false,
                Font = new Font("Segoe UI", 9.5F), Padding = new Padding(5)
            };
            ToolStripMenuItem about = new ToolStripMenuItem("&Hakkında")
            {
                Name = "aboutMenuItem", Padding = new Padding(12, 7, 28, 7)
            };
            about.Click += delegate
            {
                using (AboutForm dialog = new AboutForm(isDarkMode)) dialog.ShowDialog(this);
            };
            applicationMenu.Items.Add(about);
            updateMenuItem = new ToolStripMenuItem("Güncellemeleri kontrol et")
            {
                Name = "updateMenuItem", Padding = new Padding(12, 7, 28, 7)
            };
            updateMenuItem.Click += async delegate { await CheckForUpdatesAsync(false); };
            applicationMenu.Items.Add(updateMenuItem);

            applicationMenuButton = CreateHeaderButton("");
            applicationMenuButton.Name = "applicationMenuButton";
            applicationMenuButton.AccessibleName = "Uygulama menüsü";
            applicationMenuButton.AccessibleDescription = "Hakkında ve güncelleme menüsünü açar.";
            applicationMenuButton.Margin = new Padding(0, 2, 2, 2);
            applicationMenuButton.Paint += delegate(object sender, PaintEventArgs e)
            {
                float scale = e.Graphics.DpiX / 96F;
                float left = (applicationMenuButton.ClientSize.Width - 16F * scale) / 2F;
                float middle = applicationMenuButton.ClientSize.Height / 2F;
                using (Pen pen = new Pen(applicationMenuButton.ForeColor, 1.6F * scale))
                {
                    for (int i = -1; i <= 1; i++)
                        e.Graphics.DrawLine(pen, left, middle + i * 5F * scale,
                            left + 16F * scale, middle + i * 5F * scale);
                }
            };
            applicationMenuButton.Click += delegate { ShowApplicationMenu(); };
            applicationMenuButton.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Down) return;
                e.Handled = true;
                e.SuppressKeyPress = true;
                ShowApplicationMenu();
            };
            applicationMenuButton.Disposed += delegate { applicationMenu.Dispose(); };
            return applicationMenuButton;
        }

        private void ShowApplicationMenu()
        {
            applicationMenu.Show(applicationMenuButton, new Point(0, applicationMenuButton.Height + 3));
            applicationMenu.Items[0].Select();
        }

        private void ApplyApplicationMenuTheme(Color surface, Color primaryText)
        {
            applicationMenu.BackColor = surface;
            applicationMenu.ForeColor = primaryText;
            applicationMenu.Renderer = isDarkMode
                ? (ToolStripRenderer)new ToolStripProfessionalRenderer(new DarkMenuColorTable())
                : new ToolStripProfessionalRenderer();
            ApplyMenuItemsTheme(applicationMenu.Items, surface, primaryText);
            applicationMenuButton.ForeColor = primaryText;
            applicationMenuButton.FlatAppearance.MouseOverBackColor = isDarkMode
                ? Color.FromArgb(68, 68, 68) : Color.FromArgb(220, 226, 234);
            applicationMenuButton.FlatAppearance.MouseDownBackColor = isDarkMode
                ? Color.FromArgb(80, 80, 80) : Color.FromArgb(204, 216, 232);
        }
    }
}
