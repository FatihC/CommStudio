using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed partial class MainForm
    {
        private ToolStripMenuItem updateMenuItem;
        private bool checkingForUpdate;
        private DateTime lastUpdateCheck;
        private AvailableUpdate availableUpdate;

        internal async Task CheckForUpdatesAsync(bool automatic)
        {
            if (checkingForUpdate || isClosing || mqttClosePending) return;
            checkingForUpdate = true;
            updateMenuItem.Enabled = false;
            updateMenuItem.Text = "Güncelleme kontrol ediliyor…";
            try
            {
                if (DateTime.UtcNow - lastUpdateCheck > TimeSpan.FromMinutes(1))
                {
                    using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                    {
                        FormClosedEventHandler closed = delegate { cancellation.Cancel(); };
                        FormClosed += closed;
                        try { availableUpdate = await UpdateService.CheckAsync(cancellation.Token); }
                        finally { FormClosed -= closed; }
                    }
                    lastUpdateCheck = DateTime.UtcNow;
                }
                if (IsDisposed || isClosing || mqttClosePending) return;
                if (availableUpdate == null)
                {
                    if (!automatic) MessageBox.Show(this, "Yeni bir kararlı sürüm bulunamadı.\nMevcut sürüm: "
                        + UpdateService.CurrentVersion.ToString(3), "Güncelleme", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (UpdateForm dialog = new UpdateForm(availableUpdate, isDarkMode))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    UpdateService.PendingDirectory = dialog.PreparedDirectory;
                    // The existing close handler saves settings and awaits MQTT disconnect.
                    // Program launches the updater only after the main window has closed.
                    Close();
                }
            }
            catch (Exception error)
            {
                if (!IsDisposed && !isClosing && !automatic)
                    MessageBox.Show(this, "Güncellemeler kontrol edilemedi. İnternet bağlantınızı kontrol edip tekrar deneyin.\n\n"
                        + error.Message, "Güncelleme", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                checkingForUpdate = false;
                if (!IsDisposed)
                {
                    updateMenuItem.Enabled = true;
                    updateMenuItem.Text = availableUpdate == null ? "Güncellemeleri kontrol et"
                        : "Güncelle: " + availableUpdate.Version.ToString(3);
                }
            }
        }
    }
}
