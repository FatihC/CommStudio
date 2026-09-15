using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class UiSmokeTests
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
        try
        {
            Application.EnableVisualStyles();
            Assembly app = Assembly.LoadFrom(Environment.GetEnvironmentVariable("COMMSTUDIO_TEST_APP") ?? "dist\\CommStudio.exe");
            Type formType = app.GetType("CommStudio.MainForm", true);
            using (Form form = (Form)Activator.CreateInstance(formType, true))
            {
                form.Size = new Size(1000, 720);
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2000, -2000);
                form.Show();
                Application.DoEvents();
                ((TabControl)formType.GetField("connectionTabs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form)).SelectedIndex = 0;
                Assert(form.Text == "CommStudio", "window title");
                Assert(form.Icon != null && form.Icon.Width >= 16, "embedded application icon is loaded by the window");
                Assert(form.MinimumSize.Width >= 760, "minimum window width");

                FieldInfo splitterField = formType.GetField("contentSplitter", BindingFlags.Instance | BindingFlags.NonPublic);
                SplitContainer splitter = (SplitContainer)splitterField.GetValue(form);
                Assert(splitter.Orientation == Orientation.Horizontal, "horizontal log/commands splitter");
                Assert(!splitter.IsSplitterFixed, "log/commands splitter is draggable");
                int originalDistance = splitter.SplitterDistance;
                int movedDistance = originalDistance + 20 <= splitter.Height - splitter.Panel2MinSize - splitter.SplitterWidth
                    ? originalDistance + 20
                    : originalDistance - 20;
                splitter.SplitterDistance = movedDistance;
                Assert(splitter.SplitterDistance == movedDistance, "splitter moves vertically");

                FieldInfo rowsField = formType.GetField("commandRows", BindingFlags.Instance | BindingFlags.NonPublic);
                IList rows = (IList)rowsField.GetValue(form);
                Assert(rows.Count >= 3, "default/restored command rows");
                foreach (object row in rows)
                {
                    row.GetType().GetProperty("CommandText").SetValue(row, string.Empty, null);
                    row.GetType().GetProperty("IsHex").SetValue(row, false, null);
                }
                FieldInfo commandPanelField = formType.GetField("commandRowsPanel", BindingFlags.Instance | BindingFlags.NonPublic);
                FlowLayoutPanel commandPanel = (FlowLayoutPanel)commandPanelField.GetValue(form);
                formType.GetMethod("ResizeCommandRows", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                Application.DoEvents();
                Assert(!commandPanel.HorizontalScroll.Visible, "command panel has no horizontal scrollbar");

                form.WindowState = FormWindowState.Maximized;
                PumpFor(TimeSpan.FromMilliseconds(150));
                form.WindowState = FormWindowState.Normal;
                PumpFor(TimeSpan.FromMilliseconds(250));
                Assert(!commandPanel.HorizontalScroll.Visible, "no horizontal scrollbar after maximize and restore");
                foreach (Control rowControl in commandPanel.Controls)
                {
                    Assert(rowControl.Width <= commandPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth,
                        "command row is resized after window restore");
                }

                FieldInfo logField = formType.GetField("logView", BindingFlags.Instance | BindingFlags.NonPublic);
                RichTextBox log = (RichTextBox)logField.GetValue(form);
                Assert(log.Text.Contains("Ready."), "startup log entry");

                Type displayModeType = app.GetType("CommStudio.DisplayMode", true);
                Type directionType = app.GetType("CommStudio.LogDirection", true);
                Type logEntryType = app.GetType("CommStudio.LogEntry", true);
                MethodInfo setDisplayMode = formType.GetMethod("SetDisplayMode", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo addEntry = formType.GetMethod("AddEntry", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo createMessage = logEntryType.GetMethod("Message", BindingFlags.Static | BindingFlags.Public);

                object asciiMode = Enum.Parse(displayModeType, "Ascii");
                object hexMode = Enum.Parse(displayModeType, "Hex");
                object received = Enum.Parse(directionType, "Received");
                setDisplayMode.Invoke(form, new object[] { asciiMode });
                object message = createMessage.Invoke(null, new object[] { received, new byte[] { 0x41, 0x06, 0x0D } });
                addEntry.Invoke(form, new object[] { message });
                Assert(log.Text.Contains("A[ACK][CR]"), "ASCII history rendering");
                setDisplayMode.Invoke(form, new object[] { hexMode });
                Assert(log.Text.Contains("41 06 0D"), "old history re-rendered as Hex");

                ToolStripMenuItem wrapItem = (ToolStripMenuItem)log.ContextMenuStrip.Items["wordWrapMenuItem"];
                Assert(wrapItem != null, "word wrap is available in the log menu");
                CheckBox wrapCheckBox = (CheckBox)formType.GetField("wordWrapCheckBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Assert(wrapCheckBox.Visible && wrapCheckBox.Checked == wrapItem.Checked,
                    "header word wrap checkbox reflects the restored setting");
                if (wrapItem.Checked) wrapItem.PerformClick();
                byte[] longPayload = new byte[600];
                for (int i = 0; i < longPayload.Length; i++) longPayload[i] = 0x41;
                object longMessage = createMessage.Invoke(null, new object[] { received, longPayload });
                addEntry.Invoke(form, new object[] { longMessage });
                foreach (object mode in new object[] { asciiMode, hexMode })
                {
                    setDisplayMode.Invoke(form, new object[] { mode });
                    string originalText = log.Text;
                    int lastCharacter = log.TextLength - 1;
                    int unwrappedLine = log.GetLineFromCharIndex(lastCharacter);
                    log.Select(lastCharacter - 10, 5);
                    string originalSelection = log.SelectedText;
                    Color originalColor = log.SelectionColor;

                    if (mode.Equals(asciiMode)) wrapCheckBox.Checked = true;
                    else wrapItem.PerformClick();
                    Assert(wrapItem.Checked && wrapCheckBox.Checked && log.GetLineFromCharIndex(lastCharacter) > unwrappedLine,
                        "clicking word wrap immediately reflows existing " + mode + " history");
                    Assert(log.Text == originalText && log.SelectedText == originalSelection &&
                        log.SelectionColor == originalColor, "wrapping preserves text, selection and message color");

                    addEntry.Invoke(form, new object[] { longMessage });
                    string textWithNewMessage = log.Text;
                    Assert(log.GetLineFromCharIndex(log.TextLength - 1) > unwrappedLine + 1,
                        "new messages also wrap");
                    if (mode.Equals(asciiMode)) wrapItem.PerformClick();
                    else wrapCheckBox.Checked = false;
                    Assert(!wrapItem.Checked && !wrapCheckBox.Checked && log.GetLineFromCharIndex(log.TextLength - 1) == unwrappedLine + 1,
                        "unchecking word wrap restores one visual line per message");
                    Assert(log.Text == textWithNewMessage && log.Text.StartsWith(originalText, StringComparison.Ordinal),
                        "wrap toggles never insert or remove real line endings");
                }

                MethodInfo setLogFontSize = formType.GetMethod("SetLogFontSize", BindingFlags.Instance | BindingFlags.NonPublic);
                setLogFontSize.Invoke(form, new object[] { 14F });
                Assert(Math.Abs(log.Font.Size - 14F) < 0.1F, "log font size changes immediately");
                setLogFontSize.Invoke(form, new object[] { 99F });
                Assert(Math.Abs(log.Font.Size - 24F) < 0.1F, "log font size maximum is enforced");

                MethodInfo setDarkMode = formType.GetMethod("SetDarkMode", BindingFlags.Instance | BindingFlags.NonPublic);
                setDarkMode.Invoke(form, new object[] { true });
                Assert(form.BackColor.R < 40, "dark mode applies to the window");
                Assert(log.BackColor.R >= 48 && log.BackColor.R < 80 &&
                    log.BackColor.R == log.BackColor.G && log.BackColor.G == log.BackColor.B &&
                    log.ForeColor.R > 200, "dark mode uses a readable neutral gray log");
                FieldInfo hostField = formType.GetField("hostTextBox", BindingFlags.Instance | BindingFlags.NonPublic);
                TextBox hostInput = (TextBox)hostField.GetValue(form);
                Assert(hostInput.BackColor == log.BackColor && hostInput.ForeColor.R > 200,
                    "dark connection inputs use the same readable editor surface");
                Assert(((Control)rows[0]).BackColor.R < log.BackColor.R,
                    "dark command rows frame the lighter editor surface");
                FieldInfo sendButtonField = rows[0].GetType().GetField("sendButton", BindingFlags.Instance | BindingFlags.NonPublic);
                Button rowSendButton = (Button)sendButtonField.GetValue(rows[0]);
                Assert(rowSendButton.ForeColor.R > 150, "disconnected Send text remains legible in dark mode");
                FieldInfo themeButtonField = formType.GetField("themeButton", BindingFlags.Instance | BindingFlags.NonPublic);
                Button themeButton = (Button)themeButtonField.GetValue(form);
                Assert(themeButton.Text == "Light mode", "theme button shows the available mode");
                setDarkMode.Invoke(form, new object[] { false });
                Assert(form.BackColor.R > 200 && hostInput.BackColor.R > 200, "light mode can be restored");
                Assert(log.BackColor.R > 240 && log.ForeColor.R < 80, "light mode uses a light log with dark text");
                foreach (bool darkPreview in new bool[] { false, true })
                {
                    setDarkMode.Invoke(form, new object[] { darkPreview });
                    CheckApplicationMenu(form, formType, darkPreview);
                    using (Bitmap preview = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(preview, new Rectangle(Point.Empty, form.Size));
                        preview.Save(darkPreview ? "tests\\bin\\ui-preview.png" : "tests\\bin\\ui-preview-light.png",
                            ImageFormat.Png);
                    }
                }

                TabControl tabs = (TabControl)formType.GetField("connectionTabs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Assert(tabs.TabCount == 3, "TCP, Serial and MQTT tabs are available");
                Button connect = (Button)formType.GetField("connectButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Assert(tabs.Parent == connect.Parent && tabs.Bounds.Bottom <= tabs.Parent.ClientSize.Height,
                    "connection tabs share the action header without overflowing it");
                tabs.SelectedIndex = 0;
                Assert(hostInput.Visible, "TCP tab displays connection inputs");
                tabs.SelectedIndex = 1;
                Control serialInput = (Control)formType.GetField("serialPortInput", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Assert(serialInput.Visible && !hostInput.Visible, "Serial tab replaces TCP inputs with serial settings");
                foreach (Size size in new Size[] { new Size(1000, 720), form.MinimumSize })
                {
                    form.Size = size;
                    PumpFor(TimeSpan.FromMilliseconds(100));
                    Assert(themeButton.Right <= themeButton.Parent.ClientSize.Width,
                        "header actions fit at minimum width with the application menu");
                    foreach (bool darkPreview in new bool[] { false, true })
                    {
                        setDarkMode.Invoke(form, new object[] { darkPreview });
                        using (Bitmap preview = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(preview, new Rectangle(Point.Empty, form.Size));
                            preview.Save("tests\\bin\\serial-" + size.Width + (darkPreview ? "-dark.png" : "-light.png"), ImageFormat.Png);
                        }
                    }
                }

                tabs.SelectedIndex = 2;
                Control mqtt = (Control)formType.GetField("mqttProfiles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Assert(mqtt.Visible && !splitter.Visible && connect.Visible, "MQTT shows connection settings and an available Connect action");
                foreach (Size size in new Size[] { new Size(1000, 720), form.MinimumSize })
                {
                    form.Size = size;
                    PumpFor(TimeSpan.FromMilliseconds(100));
                    Assert(tabs.GetTabRect(2).Right <= tabs.ClientSize.Width, "all three tabs fit the header");
                    foreach (bool darkPreview in new bool[] { false, true })
                    {
                        setDarkMode.Invoke(form, new object[] { darkPreview });
                        using (Bitmap preview = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(preview, new Rectangle(Point.Empty, form.Size));
                            preview.Save("tests\\bin\\mqtt-" + size.Width + (darkPreview ? "-dark.png" : "-light.png"), ImageFormat.Png);
                        }
                    }
                }
                tabs.SelectedIndex = 0;
                Assert(splitter.Visible && hostInput.Visible && connect.Visible, "returning from MQTT restores the TCP workspace");
                form.Hide();
            }

            Console.WriteLine("UI startup smoke test passed.");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            Console.Error.WriteLine("FAIL UI smoke test: " + exception);
        }
    }

    private static void CheckApplicationMenu(Form owner, Type formType, bool dark)
    {
        Button button = (Button)formType.GetField("applicationMenuButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        ContextMenuStrip menu = (ContextMenuStrip)formType.GetField("applicationMenu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        Assert(button.Visible && button.Width >= 28, "compact application menu button is visible");
        button.PerformClick();
        Application.DoEvents();
        Assert(menu.Visible && menu.Items.Count == 2 && menu.Items[0].Name == "aboutMenuItem"
            && menu.Items[1].Name == "updateMenuItem" && menu.Items[1].Enabled,
            "hamburger opens About and update checks");
        Assert(dark ? menu.BackColor.R < 80 : menu.BackColor.R > 200, "application menu follows theme");
        menu.Close();
        Exception failure = null;
        bool opened = false;
        using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 })
        {
            timer.Tick += delegate
            {
                Form dialog = null;
                foreach (Form candidate in Application.OpenForms)
                    if (candidate.Name == "aboutForm") { dialog = candidate; break; }
                if (dialog != null && !dialog.Visible) return;
                timer.Stop();
                try
                {
                    Assert(dialog != null && dialog.Modal && dialog.Owner == owner, "About opens as an owned modal dialog");
                    opened = true;
                    Assert(dark ? dialog.BackColor.R < 80 : dialog.BackColor.R > 200, "About follows theme");
                    Assert(dialog.Controls.Find("developerLabel", true)[0].Text == "Fatih Coşkun", "developer credit");
                    Assert(dialog.Controls.Find("versionLabel", true)[0].Text.Contains(formType.Assembly.GetName().Version.ToString(3)), "version comes from assembly");
                    LinkLabel link = (LinkLabel)dialog.Controls.Find("repositoryLink", true)[0];
                    Assert((string)link.Links[0].LinkData == "https://github.com/FatihC/CommStudio", "repository link target");
                    using (Bitmap preview = new Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.DrawToBitmap(preview, new Rectangle(Point.Empty, dialog.Size));
                        preview.Save("tests\\bin\\about-" + (dark ? "dark" : "light") + ".png", ImageFormat.Png);
                    }
                    typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(dialog, new object[] { dark ? Keys.Escape : Keys.Enter });
                    Assert(dialog.DialogResult != DialogResult.None, "Enter and Escape dismiss About");
                }
                catch (Exception exception) { failure = exception; }
                finally { if (dialog != null && dialog.Visible) dialog.Close(); }
            };
            timer.Start();
            ((ToolStripMenuItem)menu.Items[0]).PerformClick();
        }
        if (failure != null) throw failure;
        Assert(opened && owner.Visible, "main workspace remains open after About closes");
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Failed assertion: " + name);
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
}
