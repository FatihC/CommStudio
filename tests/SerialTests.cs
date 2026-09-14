using System;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class SerialTests
{
    [STAThread]
    private static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Assembly app = Assembly.LoadFrom(Environment.GetEnvironmentVariable("COMMSTUDIO_TEST_APP") ?? "dist\\CommStudio.exe");
            Type type = app.GetType("CommStudio.MainForm", true);
            Type settingsType = app.GetType("CommStudio.AppSettings", true);
            Type storeType = app.GetType("CommStudio.SettingsStore", true);
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(settingsType);
            object oldSettings;
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"Host\":\"example.test\",\"Port\":2404}")))
                oldSettings = serializer.ReadObject(stream);
            object normalized = storeType.GetMethod("Normalize", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { oldSettings });
            Assert((string)GetProperty(normalized, "Host") == "example.test", "legacy TCP host preserved");
            Assert((string)GetProperty(normalized, "ConnectionType") == "TCP", "legacy files default to TCP");
            Assert((int)GetProperty(normalized, "BaudRate") == 9600 && (int)GetProperty(normalized, "DataBits") == 8,
                "legacy files receive serial defaults");
            Assert((string)GetProperty(normalized, "SerialStopBits") == "One", "legacy stop bits default to one");
            Assert(!(bool)GetProperty(normalized, "AutoIecBaudSwitch"), "automatic baud switching is opt-in for existing settings");
            Assert((int)GetProperty(normalized, "IecBaudSwitchDelayMs") == 250, "existing settings receive USB delay default");

            using (Form form = (Form)Activator.CreateInstance(type, true))
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2000, -2000);
                form.Show();
                Application.DoEvents();
                TabControl tabs = (TabControl)Field(form, "connectionTabs");
                tabs.SelectedIndex = 1;
                CheckBox autoBaud = (CheckBox)Field(form, "autoIecBaudInput");
                autoBaud.Checked = true;
                NumericUpDown delay = (NumericUpDown)Field(form, "iecBaudDelayInput");
                delay.Value = 350;
                MethodInfo append = type.GetMethod("AppendIncoming", BindingFlags.Instance | BindingFlags.NonPublic);
                RichTextBox log = (RichTextBox)Field(form, "logView");
                append.Invoke(form, new object[] { Encoding.ASCII.GetBytes("/LUN5<1>LUN463") });
                Assert(!log.Text.Contains("/LUN5"), "partial identification stays buffered");
                append.Invoke(form, new object[] { Encoding.ASCII.GetBytes("367853\r\n") });
                Assert(log.Text.Contains("/LUN5<1>LUN463367853[CR][LF]"), "complete IEC identification is immediately visible without idle timer");
                Assert(((MemoryStream)Field(form, "pendingIncoming")).Length == 0, "identification bytes are not duplicated later");
                autoBaud.Checked = false;
                append.Invoke(form, new object[] { Encoding.ASCII.GetBytes("/ABC5TEST\r\n") });
                Assert(!log.Text.Contains("/ABC5TEST"), "normal serial mode preserves one-second grouping");
                Invoke(form, "FlushPendingIncoming");
                autoBaud.Checked = true;
                ComboBox port = (ComboBox)Field(form, "serialPortInput");
                ComboBox baud = (ComboBox)Field(form, "baudRateInput");
                ComboBox handshake = (ComboBox)Field(form, "handshakeInput");
                CheckBox rts = (CheckBox)Field(form, "rtsInput");
                CheckBox dtr = (CheckBox)Field(form, "dtrInput");
                port.Text = "COM999999";
                baud.Text = "250000";
                ((ComboBox)Field(form, "dataBitsInput")).SelectedItem = "7";
                ((ComboBox)Field(form, "parityInput")).SelectedItem = "Even";
                ((ComboBox)Field(form, "stopBitsInput")).SelectedIndex = 2;
                dtr.Checked = true;
                rts.Checked = true;
                handshake.SelectedIndex = 2;
                Assert(!rts.Enabled, "hardware flow control disables manual RTS");
                using (SerialPort configured = (SerialPort)Invoke(form, "CreateConfiguredSerialPort"))
                {
                    Assert(configured.PortName == "COM999999" && configured.BaudRate == 250000, "custom port and baud");
                    Assert(configured.DataBits == 7 && configured.Parity == Parity.Even && configured.StopBits == StopBits.Two,
                        "framing settings applied");
                    Assert(configured.Handshake == Handshake.RequestToSend && configured.DtrEnable && !configured.RtsEnable,
                        "flow and output controls applied");
                    Assert(configured.ParityReplace == 0, "binary received bytes are not replaced");
                }
                handshake.SelectedIndex = 0;
                Assert(rts.Enabled, "manual RTS available without hardware flow control");
                Invoke(form, "SaveSerialSettings");
                object settings = Field(form, "settings");
                using (MemoryStream stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, settings);
                    stream.Position = 0;
                    object restored = serializer.ReadObject(stream);
                    Assert((string)GetProperty(restored, "ConnectionType") == "Serial" &&
                        (int)GetProperty(restored, "BaudRate") == 250000 && (bool)GetProperty(restored, "RtsEnabled"),
                        "serial preferences survive JSON round trip");
                    Assert((bool)GetProperty(restored, "AutoIecBaudSwitch"), "automatic baud preference survives JSON round trip");
                    Assert((int)GetProperty(restored, "IecBaudSwitchDelayMs") == 350, "USB switch delay survives JSON round trip");
                }
                port.Text = "";
                // Simulate the post-handshake session state without opening a physical meter port.
                type.GetField("serialStartingBaud", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, 300);
                baud.Text = "9600";
                Invoke(form, "SaveSerialSettings");
                Assert((int)GetProperty(settings, "BaudRate") == 300, "closing while connected saves the starting baud");
                Assert(baud.Text == "9600", "saving does not change the active baud display");
                type.GetMethod("Disconnect", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { null, false });
                Assert(baud.Text == "300", "disconnect restores starting baud after negotiation");
                port.Text = "COM999999";
                using (SerialPort nextSession = (SerialPort)Invoke(form, "CreateConfiguredSerialPort"))
                    Assert(nextSession.BaudRate == 300, "next session opens with the starting baud");
                baud.Text = "1200";
                Invoke(form, "SaveSerialSettings");
                Assert((int)GetProperty(settings, "BaudRate") == 1200, "user can choose a different starting baud while disconnected");
                port.Text = "";
                Pump((Task)Invoke(form, "ToggleConnectionAsync"));
                Assert(((RichTextBox)Field(form, "logView")).Text.Contains("Select or enter a COM port"), "empty port gives useful error");
                port.Text = "COM999999";
                baud.Text = "invalid";
                Pump((Task)Invoke(form, "ToggleConnectionAsync"));
                Assert(((RichTextBox)Field(form, "logView")).Text.Contains("Baud rate must"), "invalid baud gives useful error");
                baud.Text = "9600";
                Pump((Task)Invoke(form, "ToggleConnectionAsync"));
                Assert(Field(form, "serialPort") == null, "failed open does not retain a port");
                Assert(((Button)Field(form, "connectButton")).Enabled, "failed open permits retry");
                tabs.SelectedIndex = 0;
                Assert(tabs.SelectedIndex == 0, "can return to TCP after serial failure");
                form.Hide();
            }
            Console.WriteLine("Serial configuration, settings migration and failure-path tests passed. Hardware TX/RX not exercised.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static object Field(object instance, string name)
    {
        return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }
    private static object GetProperty(object instance, string name)
    {
        return instance.GetType().GetProperty(name).GetValue(instance, null);
    }
    private static object Invoke(object instance, string name)
    {
        return instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Pump(Task task)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Assert(task.IsCompleted, "connection test timed out");
        task.GetAwaiter().GetResult();
    }
}
