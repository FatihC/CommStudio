using System;
using System.Collections;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class NetworkIntegrationTests
{
    private static int exitCode;

    private static void Main()
    {
        Thread thread = new Thread(Run);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Environment.Exit(exitCode);
    }

    private static void Run()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        TcpClient serverClient = null;
        try
        {
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

            Application.EnableVisualStyles();
            Assembly app = Assembly.LoadFrom(Environment.GetEnvironmentVariable("COMMSTUDIO_TEST_APP") ?? "dist\\CommStudio.exe");
            Type formType = app.GetType("CommStudio.MainForm", true);
            using (Form form = (Form)Activator.CreateInstance(formType, true))
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2000, -2000);
                form.Show();
                Application.DoEvents();
                ((TabControl)GetField(formType, form, "connectionTabs")).SelectedIndex = 0;

                TextBox host = (TextBox)GetField(formType, form, "hostTextBox");
                NumericUpDown portInput = (NumericUpDown)GetField(formType, form, "portInput");
                host.Text = "127.0.0.1";
                portInput.Value = port;
                TabControl tabs = (TabControl)GetField(formType, form, "connectionTabs");
                tabs.SelectedIndex = 0;

                Task connectTask = (Task)Invoke(formType, form, "ToggleConnectionAsync", null);
                PumpUntil(connectTask, TimeSpan.FromSeconds(5));
                PumpUntil(acceptTask, TimeSpan.FromSeconds(5));
                serverClient = acceptTask.Result;
                tabs.SelectedIndex = 1;
                Assert(tabs.SelectedIndex == 0, "transport cannot change during an active connection");

                IList rows = (IList)GetField(formType, form, "commandRows");
                object firstRow = rows[0];
                firstRow.GetType().GetProperty("CommandText").SetValue(firstRow, "[ACK][CR]", null);
                firstRow.GetType().GetProperty("IsHex").SetValue(firstRow, false, null);

                Task sendTask = (Task)Invoke(formType, form, "SendCommandAsync", new object[] { firstRow });
                PumpUntil(sendTask, TimeSpan.FromSeconds(5));

                NetworkStream serverStream = serverClient.GetStream();
                serverStream.ReadTimeout = 3000;
                byte[] sent = new byte[2];
                int read = serverStream.Read(sent, 0, sent.Length);
                Assert(read == 2 && sent[0] == 0x06 && sent[1] == 0x0D, "ASCII token bytes sent over TCP");

                serverStream.Write(new byte[] { 0x41 }, 0, 1);
                PumpFor(TimeSpan.FromMilliseconds(300));
                serverStream.Write(new byte[] { 0x06, 0x0D }, 0, 2);
                PumpFor(TimeSpan.FromMilliseconds(1250));

                IList entries = (IList)GetField(formType, form, "logEntries");
                int receivedEntries = 0;
                byte[] receivedData = null;
                foreach (object entry in entries)
                {
                    if (entry.GetType().GetProperty("Direction").GetValue(entry, null).ToString() == "Received")
                    {
                        receivedEntries++;
                        receivedData = (byte[])entry.GetType().GetProperty("Data").GetValue(entry, null);
                    }
                }

                Assert(receivedEntries == 1, "fragmented incoming bytes grouped into one message");
                Assert(receivedData != null && receivedData.Length == 3 && receivedData[0] == 0x41 &&
                    receivedData[1] == 0x06 && receivedData[2] == 0x0D, "grouped incoming bytes preserved");

                Invoke(formType, form, "Disconnect", new object[] { null, false });
                form.Hide();
            }

            Console.WriteLine("TCP loopback integration test passed.");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            Console.Error.WriteLine("FAIL TCP integration test: " + Unwrap(exception));
        }
        finally
        {
            if (serverClient != null) serverClient.Close();
            listener.Stop();
        }
    }

    private static object GetField(Type type, object instance, string name)
    {
        return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }

    private static object Invoke(Type type, object instance, string name, object[] arguments)
    {
        return type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, arguments);
    }

    private static void PumpUntil(Task task, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        if (!task.IsCompleted) throw new TimeoutException("An asynchronous test operation timed out.");
        if (task.IsFaulted) throw task.Exception;
    }

    private static void PumpFor(TimeSpan duration)
    {
        DateTime deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Failed assertion: " + name);
    }

    private static Exception Unwrap(Exception exception)
    {
        TargetInvocationException invocation = exception as TargetInvocationException;
        return invocation != null && invocation.InnerException != null ? invocation.InnerException : exception;
    }
}
