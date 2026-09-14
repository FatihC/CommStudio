using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class MqttPreferencesTests
{
    [STAThread]
    private static int Main()
    {
        string directory = Path.GetFullPath(Path.Combine("tests", "bin", "mqtt-preferences-" + Guid.NewGuid().ToString("N")));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            Application.EnableVisualStyles();
            Assembly app = Assembly.LoadFrom(Environment.GetEnvironmentVariable("COMMSTUDIO_TEST_APP") ?? "dist\\CommStudio.exe");
            Type store = app.GetType("CommStudio.SettingsStore", true);
            // Redirect this test process's settings store; never change the user's preferences.
            store.GetField("SettingsDirectory", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, directory);
            store.GetField("SettingsPath", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, path);
            // Surface filesystem restrictions as a test failure before a hidden form
            // could show the application's interactive save-error dialog.
            object defaults = Activator.CreateInstance(app.GetType("CommStudio.AppSettings", true));
            store.GetMethod("Save").Invoke(null, new[] { defaults });
            store.GetMethod("Save").Invoke(null, new[] { defaults });
            Type formType = app.GetType("CommStudio.MainForm", true);
            foreach (string format in new[] { "Json", "Hex", "PlainText" })
            {
                using (Form form = (Form)Activator.CreateInstance(formType, true))
                {
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-2000, -2000);
                    form.Show(); Application.DoEvents();
                    object workspace = Field(form, "mqttWorkspace");
                    ((ComboBox)Field(workspace, "logFormat")).SelectedIndex = format == "Json" ? 1 : format == "Hex" ? 2 : 0;
                    form.Close(); Application.DoEvents();
                }
                Assert(File.Exists(path), "closing writes settings");
                using (Form reopened = (Form)Activator.CreateInstance(formType, true))
                {
                    object workspace = Field(reopened, "mqttWorkspace");
                    Assert((string)workspace.GetType().GetProperty("LogDisplayFormat").GetValue(workspace, null) == format,
                        "reopened workspace restores " + format);
                    object log = Field(workspace, "mqttLog");
                    Assert(Field(log, "format").ToString() == format, "new messages inherit restored format");
                }
            }
            foreach (string json in new[] { "{}", "{\"MqttLogFormat\":\"invalid\"}" })
            {
                File.WriteAllText(path, json);
                using (Form form = (Form)Activator.CreateInstance(formType, true))
                {
                    object workspace = Field(form, "mqttWorkspace");
                    Assert((string)workspace.GetType().GetProperty("LogDisplayFormat").GetValue(workspace, null) == "PlainText",
                        "old or invalid settings fall back to Plain Text");
                }
            }
            Console.WriteLine("MQTT preferences tests passed: close/reopen restores all formats; legacy settings remain compatible.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
    private static object Field(object target, string name)
    { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
