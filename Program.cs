using System;
using System.Windows.Forms;

namespace CommStudio
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            EmbeddedDependencies.Initialize();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
