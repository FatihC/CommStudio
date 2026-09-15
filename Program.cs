using System;
using System.Windows.Forms;

namespace CommStudio
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--apply-update")
            {
                UpdateService.RunHelper(args[1]);
                return;
            }
            EmbeddedDependencies.Initialize();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm form = new MainForm())
            {
                form.Shown += async delegate
                {
                    if (args.Length == 2 && args[0] == "--cleanup-update")
                        UpdateService.CleanupAfterRestart(args[1]);
                    await form.CheckForUpdatesAsync(true);
                };
                Application.Run(form);
            }
            try { UpdateService.LaunchPending(); }
            catch (Exception error)
            {
                MessageBox.Show("Güncelleme başlatılamadı. Mevcut EXE korunuyor; uygulamayı tekrar açabilirsiniz.\n\n"
                    + error.Message, "CommStudio güncellemesi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
