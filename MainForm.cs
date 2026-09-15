using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed partial class MainForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

        private const float DefaultLogFontSize = 9.5F;
        private const float MinimumLogFontSize = 7F;
        private const float MaximumLogFontSize = 24F;

        private readonly AppSettings settings;
        private readonly List<LogEntry> logEntries = new List<LogEntry>();
        private readonly List<CommandRowControl> commandRows = new List<CommandRowControl>();
        private readonly MemoryStream pendingIncoming = new MemoryStream();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

        private TextBox hostTextBox;
        private NumericUpDown portInput;
        private Button connectButton;
        private Button themeButton;
        private Label statusLabel;
        private LogRichTextBox logView;
        private CheckBox wordWrapCheckBox;
        private SplitContainer contentSplitter;
        private FlowLayoutPanel commandRowsPanel;
        private ToolStripMenuItem asciiMenuItem;
        private ToolStripMenuItem hexMenuItem;
        private ToolStripMenuItem fontSizeMenuItem;
        private ToolStripMenuItem smallerFontMenuItem;
        private ToolStripMenuItem largerFontMenuItem;
        private System.Windows.Forms.Timer incomingIdleTimer;
        private DateTime lastIncomingAt;
        private DisplayMode displayMode;
        private float logFontSize;
        private Font logFont;
        private bool isDarkMode;

        private TcpClient client;
        private NetworkStream networkStream;
        private CancellationTokenSource receiveCancellation;
        private bool isConnecting;
        private bool isClosing;
        private bool commandRowsResizePending;
        private bool commandRowsResizeInProgress;
        private RowStyle connectionPanelRow;
        private MqttProfilesControl mqttProfiles;
        private MqttWorkspaceControl mqttWorkspace;
        private bool mqttClosePending;

        public MainForm()
        {
            EmbeddedDependencies.Initialize();
            settings = SettingsStore.Load();
            displayMode = string.Equals(settings.DisplayMode, "Hex", StringComparison.OrdinalIgnoreCase)
                ? DisplayMode.Hex
                : DisplayMode.Ascii;
            logFontSize = settings.LogFontSize;
            isDarkMode = settings.IsDarkMode;

            InitializeWindow();
            BuildInterface();
            RestoreSettings();
            ApplyTheme();
            AddNotice(LogDirection.System, "Ready. Choose TCP or Serial, set the connection details, then connect.");
        }

        private void InitializeWindow()
        {
            Text = "CommStudio";
            Icon associatedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (associatedIcon != null)
            {
                Icon = associatedIcon;
            }
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(244, 246, 249);
            MinimumSize = new Size(760, 540);
            StartPosition = FormStartPosition.Manual;
            Size = new Size(settings.WindowWidth, settings.WindowHeight);

            Rectangle desired = new Rectangle(settings.WindowX, settings.WindowY, settings.WindowWidth, settings.WindowHeight);
            bool visible = false;
            if (settings.HasWindowPosition)
            {
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (screen.WorkingArea.IntersectsWith(desired))
                    {
                        visible = true;
                        break;
                    }
                }
            }

            if (visible)
            {
                Location = new Point(settings.WindowX, settings.WindowY);
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
            }

            if (settings.IsMaximized)
            {
                WindowState = FormWindowState.Maximized;
            }

            FormClosing += MainFormOnFormClosing;
            Shown += delegate { RestoreSplitterPosition(); };
            SizeChanged += delegate { ScheduleCommandRowsResize(); };
            ResizeEnd += delegate { ScheduleCommandRowsResize(); };
            HandleCreated += delegate { ApplyNativeTitleBarTheme(); };
        }

        private void BuildInterface()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(14);
            root.ColumnCount = 1;
            root.RowCount = 2;
            connectionPanelRow = new RowStyle(SizeType.Absolute, 106F);
            root.RowStyles.Add(connectionPanelRow);
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Tag = "Root";
            Controls.Add(root);

            root.Controls.Add(BuildConnectionPanel(), 0, 0);

            contentSplitter = new SplitContainer();
            contentSplitter.Dock = DockStyle.Fill;
            contentSplitter.Orientation = Orientation.Horizontal;
            contentSplitter.BorderStyle = BorderStyle.None;
            contentSplitter.IsSplitterFixed = false;
            contentSplitter.FixedPanel = FixedPanel.Panel2;
            contentSplitter.SplitterWidth = 8;
            contentSplitter.Panel1MinSize = 120;
            contentSplitter.Panel2MinSize = 120;
            contentSplitter.BackColor = Color.FromArgb(214, 219, 226);
            contentSplitter.Cursor = Cursors.HSplit;
            contentSplitter.Panel1.Cursor = Cursors.Default;
            contentSplitter.Panel2.Cursor = Cursors.Default;
            contentSplitter.Panel1.Controls.Add(BuildLogPanel());
            contentSplitter.Panel2.Controls.Add(BuildCommandsPanel());
            contentSplitter.Panel2.Resize += delegate { ScheduleCommandRowsResize(); };
            contentSplitter.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (!isDarkMode)
                {
                    Rectangle divider = contentSplitter.SplitterRectangle;
                    using (Pen pen = new Pen(Color.FromArgb(200, 200, 200)))
                    {
                        int y = divider.Top + divider.Height / 2;
                        e.Graphics.DrawLine(pen, divider.Left, y, divider.Right, y);
                    }
                }
            };
            Panel workspace = new Panel { Dock = DockStyle.Fill, Margin = new Padding(3) };
            workspace.Controls.Add(contentSplitter);
            mqttProfiles = new MqttProfilesControl();
            mqttWorkspace = new MqttWorkspaceControl(mqttProfiles) { Visible = false, LogDisplayFormat = settings.MqttLogFormat };
            mqttWorkspace.StateChanged += UpdateConnectionUi;
            workspace.Controls.Add(mqttWorkspace);
            root.Controls.Add(workspace, 0, 1);

            incomingIdleTimer = new System.Windows.Forms.Timer();
            incomingIdleTimer.Interval = 100;
            incomingIdleTimer.Tick += delegate
            {
                if (pendingIncoming.Length > 0 && DateTime.UtcNow - lastIncomingAt >= TimeSpan.FromSeconds(1))
                {
                    FlushPendingIncoming();
                }
            };
            incomingIdleTimer.Start();
        }

        private Control BuildLogPanel()
        {
            Panel card = CreateCard();
            card.Margin = Padding.Empty;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(12, 8, 12, 12);
            layout.RowCount = 2;
            layout.ColumnCount = 1;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 4;
            header.RowCount = 1;
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));

            Label label = new Label();
            label.Text = "COMMUNICATION LOG";
            label.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
            label.ForeColor = Color.FromArgb(84, 94, 108);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;

            wordWrapCheckBox = new CheckBox();
            wordWrapCheckBox.Text = "Word wrap";
            wordWrapCheckBox.Dock = DockStyle.Fill;
            wordWrapCheckBox.Margin = new Padding(4, 0, 4, 2);
            wordWrapCheckBox.Checked = settings.LogWordWrap;

            Button copyButton = CreateHeaderButton("Copy all");
            copyButton.Click += delegate { CopyAllLog(); };
            Button clearButton = CreateHeaderButton("Clear");
            clearButton.Click += delegate { ClearLog(); };
            header.Controls.Add(label, 0, 0);
            header.Controls.Add(wordWrapCheckBox, 1, 0);
            header.Controls.Add(copyButton, 2, 0);
            header.Controls.Add(clearButton, 3, 0);

            logView = new LogRichTextBox();
            logView.Dock = DockStyle.Fill;
            logView.ReadOnly = true;
            logView.BackColor = Color.FromArgb(63, 63, 63);
            logView.ForeColor = Color.FromArgb(224, 224, 224);
            logView.BorderStyle = BorderStyle.None;
            logFont = new Font("Consolas", logFontSize);
            logView.Font = logFont;
            logView.WordWrap = settings.LogWordWrap;
            logView.DetectUrls = false;
            logView.HideSelection = false;
            logView.ContextMenuStrip = BuildLogContextMenu();
            logView.ZoomInRequested += delegate { AdjustLogFontSize(1F); };
            logView.ZoomOutRequested += delegate { AdjustLogFontSize(-1F); };
            logView.ZoomResetRequested += delegate { SetLogFontSize(DefaultLogFontSize); };

            layout.Controls.Add(header, 0, 0);
            Panel logFrame = new Panel();
            logFrame.Dock = DockStyle.Fill;
            logFrame.Margin = logView.Margin;
            logFrame.Tag = "InputFrame";
            logFrame.Controls.Add(logView);
            layout.Controls.Add(logFrame, 0, 1);
            card.Controls.Add(layout);
            return card;
        }

        private Control BuildCommandsPanel()
        {
            Panel card = CreateCard();

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(12, 8, 12, 10);
            layout.RowCount = 2;
            layout.ColumnCount = 1;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 2;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128F));

            Label title = new Label();
            title.Text = "COMMANDS   ·   Enter sends   ·   Shift+Enter adds a new line";
            title.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(84, 94, 108);
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;

            Button addButton = CreateHeaderButton("＋ Add command");
            addButton.ForeColor = Color.FromArgb(32, 111, 214);
            addButton.Tag = "AccentText";
            addButton.Click += delegate { AddCommandRow(null, true); };
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(addButton, 1, 0);

            commandRowsPanel = new FlowLayoutPanel();
            commandRowsPanel.Dock = DockStyle.Fill;
            commandRowsPanel.FlowDirection = FlowDirection.TopDown;
            commandRowsPanel.WrapContents = false;
            commandRowsPanel.AutoScroll = true;
            commandRowsPanel.HorizontalScroll.Enabled = false;
            commandRowsPanel.HorizontalScroll.Visible = false;
            commandRowsPanel.BackColor = Color.White;
            commandRowsPanel.Resize += delegate { ScheduleCommandRowsResize(); };

            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(commandRowsPanel, 0, 1);
            card.Controls.Add(layout);
            return card;
        }

        private ContextMenuStrip BuildLogContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem viewMenu = new ToolStripMenuItem("View as");
            asciiMenuItem = new ToolStripMenuItem("ASCII");
            hexMenuItem = new ToolStripMenuItem("Hex");
            asciiMenuItem.Click += delegate { SetDisplayMode(DisplayMode.Ascii); };
            hexMenuItem.Click += delegate { SetDisplayMode(DisplayMode.Hex); };
            viewMenu.DropDownItems.Add(asciiMenuItem);
            viewMenu.DropDownItems.Add(hexMenuItem);

            ToolStripMenuItem wordWrapMenuItem = new ToolStripMenuItem("Word wrap");
            wordWrapMenuItem.Name = "wordWrapMenuItem";
            wordWrapMenuItem.CheckOnClick = true;
            wordWrapMenuItem.Checked = logView.WordWrap;
            wordWrapMenuItem.CheckedChanged += delegate
            {
                // RichEdit reflows existing text without changing message contents or line endings.
                logView.WordWrap = wordWrapMenuItem.Checked;
                wordWrapCheckBox.Checked = wordWrapMenuItem.Checked;
            };
            wordWrapCheckBox.CheckedChanged += delegate
            {
                wordWrapMenuItem.Checked = wordWrapCheckBox.Checked;
            };

            fontSizeMenuItem = new ToolStripMenuItem();
            smallerFontMenuItem = new ToolStripMenuItem("Smaller    Ctrl+-");
            ToolStripMenuItem defaultFontMenuItem = new ToolStripMenuItem("Default (9.5 pt)    Ctrl+0");
            largerFontMenuItem = new ToolStripMenuItem("Larger    Ctrl++");
            smallerFontMenuItem.Click += delegate { AdjustLogFontSize(-1F); };
            defaultFontMenuItem.Click += delegate { SetLogFontSize(DefaultLogFontSize); };
            largerFontMenuItem.Click += delegate { AdjustLogFontSize(1F); };
            fontSizeMenuItem.DropDownItems.Add(smallerFontMenuItem);
            fontSizeMenuItem.DropDownItems.Add(defaultFontMenuItem);
            fontSizeMenuItem.DropDownItems.Add(largerFontMenuItem);

            ToolStripMenuItem copySelection = new ToolStripMenuItem("Copy selection");
            copySelection.Click += delegate
            {
                if (!string.IsNullOrEmpty(logView.SelectedText)) Clipboard.SetText(logView.SelectedText);
            };
            ToolStripMenuItem copyAll = new ToolStripMenuItem("Copy all");
            copyAll.Click += delegate { CopyAllLog(); };
            ToolStripMenuItem clear = new ToolStripMenuItem("Clear");
            clear.Click += delegate { ClearLog(); };

            menu.Items.Add(viewMenu);
            menu.Items.Add(wordWrapMenuItem);
            menu.Items.Add(fontSizeMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(copySelection);
            menu.Items.Add(copyAll);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(clear);
            menu.Opening += delegate
            {
                asciiMenuItem.Checked = displayMode == DisplayMode.Ascii;
                hexMenuItem.Checked = displayMode == DisplayMode.Hex;
                fontSizeMenuItem.Text = "Font size (" + logFontSize.ToString("0.#") + " pt)";
                smallerFontMenuItem.Enabled = logFontSize > MinimumLogFontSize;
                largerFontMenuItem.Enabled = logFontSize < MaximumLogFontSize;
                copySelection.Enabled = !string.IsNullOrEmpty(logView.SelectedText);
                copyAll.Enabled = logView.TextLength > 0;
                clear.Enabled = logEntries.Count > 0;
            };
            return menu;
        }

        private void RestoreSettings()
        {
            hostTextBox.Text = settings.Host;
            portInput.Value = settings.Port;
            RestoreSerialSettings();
            foreach (CommandSetting command in settings.Commands)
            {
                AddCommandRow(command, false);
            }

            SetDisplayMode(displayMode);
            UpdateConnectionUi();
        }

        private void RestoreSplitterPosition()
        {
            if (contentSplitter == null || contentSplitter.Height <= 0)
            {
                return;
            }

            int maximumPanel2Height = contentSplitter.Height - contentSplitter.Panel1MinSize - contentSplitter.SplitterWidth;
            int desiredPanel2Height = Math.Max(contentSplitter.Panel2MinSize,
                Math.Min(maximumPanel2Height, settings.CommandPanelHeight));
            contentSplitter.SplitterDistance = contentSplitter.Height - desiredPanel2Height - contentSplitter.SplitterWidth;
        }

        private void AddCommandRow(CommandSetting setting, bool focus)
        {
            CommandRowControl row = new CommandRowControl();
            if (setting != null)
            {
                row.CommandText = setting.Text;
                row.IsHex = setting.IsHex;
            }

            row.SendRequested += async delegate { await SendCommandAsync(row); };
            row.RemoveRequested += delegate
            {
                commandRows.Remove(row);
                commandRowsPanel.Controls.Remove(row);
                row.Dispose();
                ResizeCommandRows();
            };

            commandRows.Add(row);
            commandRowsPanel.Controls.Add(row);
            row.SetSendEnabled(IsConnected);
            row.ApplyTheme(isDarkMode);
            ResizeCommandRows();
            if (focus) row.FocusCommand();
        }

        private void ResizeCommandRows()
        {
            if (commandRowsPanel == null || commandRowsPanel.IsDisposed || commandRowsResizeInProgress)
            {
                return;
            }

            commandRowsResizeInProgress = true;
            int verticalScrollPosition = Math.Max(0, -commandRowsPanel.AutoScrollPosition.Y);
            try
            {
                commandRowsPanel.SuspendLayout();
                int width = Math.Max(440,
                    commandRowsPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
                foreach (CommandRowControl row in commandRows)
                {
                    row.Width = width;
                }

                commandRowsPanel.AutoScrollMinSize = Size.Empty;
                commandRowsPanel.ResumeLayout(true);
                commandRowsPanel.PerformLayout();
                commandRowsPanel.AutoScrollPosition = new Point(0, verticalScrollPosition);
                commandRowsPanel.HorizontalScroll.Enabled = false;
                commandRowsPanel.HorizontalScroll.Visible = false;
            }
            finally
            {
                commandRowsResizeInProgress = false;
            }
        }

        private void ScheduleCommandRowsResize()
        {
            if (isClosing || commandRowsPanel == null || commandRowsPanel.IsDisposed)
            {
                return;
            }

            if (!IsHandleCreated)
            {
                ResizeCommandRows();
                return;
            }

            if (commandRowsResizePending)
            {
                return;
            }

            commandRowsResizePending = true;
            BeginInvoke(new MethodInvoker(delegate
            {
                commandRowsResizePending = false;
                ResizeCommandRows();
            }));
        }

        private async Task ToggleConnectionAsync()
        {
            if (IsMqttSelected) { await mqttWorkspace.ToggleConnectionAsync(); return; }
            if (IsConnected)
            {
                Disconnect("Disconnected by user.", false);
                return;
            }

            if (isConnecting) return;
            if (IsSerialSelected)
            {
                await ConnectSerialAsync();
                return;
            }
            string host = hostTextBox.Text.Trim();
            if (host.Length == 0)
            {
                AddNotice(LogDirection.Error, "Host cannot be empty.");
                hostTextBox.Focus();
                return;
            }

            isConnecting = true;
            UpdateConnectionUi();
            AddNotice(LogDirection.System, "Connecting to " + host + ":" + portInput.Value + " …");

            TcpClient pendingClient = new TcpClient();
            try
            {
                Task connectTask = pendingClient.ConnectAsync(host, (int)portInput.Value);
                Task completed = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(10)));
                if (completed != connectTask)
                {
                    pendingClient.Close();
                    throw new TimeoutException("The connection attempt timed out after 10 seconds.");
                }

                await connectTask;
                if (isClosing) { pendingClient.Close(); return; }
                client = pendingClient;
                networkStream = client.GetStream();
                receiveCancellation = new CancellationTokenSource();
                AddNotice(LogDirection.System, "Connected to " + host + ":" + portInput.Value + ".");
                ReceiveLoopAsync(client, networkStream, receiveCancellation.Token);
            }
            catch (Exception exception)
            {
                pendingClient.Close();
                if (!isClosing) AddNotice(LogDirection.Error, "Connection failed: " + FriendlyMessage(exception));
            }
            finally
            {
                isConnecting = false;
                if (!isClosing) UpdateConnectionUi();
            }
        }

        private async void ReceiveLoopAsync(TcpClient activeClient, NetworkStream activeStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int received = await activeStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (isClosing || cancellationToken.IsCancellationRequested || client != activeClient) return;
                    if (received == 0)
                    {
                        if (!isClosing && client == activeClient)
                        {
                            Disconnect("Remote host closed the connection.", false);
                        }
                        return;
                    }

                    byte[] copy = new byte[received];
                    Buffer.BlockCopy(buffer, 0, copy, 0, received);
                    AppendIncoming(copy);
                }
            }
            catch (Exception exception)
            {
                if (!isClosing && !cancellationToken.IsCancellationRequested && client == activeClient)
                {
                    Disconnect("Connection lost: " + FriendlyMessage(exception), true);
                }
            }
        }

        private void AppendIncoming(byte[] data)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<byte[]>(AppendIncoming), data);
                return;
            }

            pendingIncoming.Write(data, 0, data.Length);
            lastIncomingAt = DateTime.UtcNow;
            // An IEC identification line must be visible promptly so a manual ACK can meet the meter's deadline.
            if (IsSerialSelected && autoIecBaudInput.Checked && pendingIncoming.Length <= 256 &&
                IsCompleteIecIdentification(pendingIncoming.ToArray()))
                FlushPendingIncoming();
        }

        private static bool IsCompleteIecIdentification(byte[] data)
        {
            if (data.Length < 7 || data[0] != '/' || data[4] < '0' || data[4] > '6' ||
                data[data.Length - 2] != 0x0D || data[data.Length - 1] != 0x0A) return false;
            for (int i = 1; i < 4; i++)
                if (!((data[i] >= 'A' && data[i] <= 'Z') || (data[i] >= 'a' && data[i] <= 'z'))) return false;
            return true;
        }

        private void FlushPendingIncoming()
        {
            if (pendingIncoming.Length == 0) return;
            byte[] data = pendingIncoming.ToArray();
            pendingIncoming.SetLength(0);
            AddEntry(LogEntry.Message(LogDirection.Received, data));
        }

        private async Task SendCommandAsync(CommandRowControl row)
        {
            if (!IsConnected)
            {
                AddNotice(LogDirection.Error, "Not connected. Connect before sending a command.");
                return;
            }

            byte[] data;
            try
            {
                data = row.IsHex ? ByteCodec.ParseHexCommand(row.CommandText) : ByteCodec.ParseAsciiCommand(row.CommandText);
            }
            catch (FormatException exception)
            {
                AddNotice(LogDirection.Error, "Command was not sent: " + exception.Message.Trim());
                return;
            }

            if (data.Length == 0)
            {
                AddNotice(LogDirection.Error, "Command was not sent because it is empty.");
                return;
            }

            CancellationTokenSource activeSession = receiveCancellation;
            bool autoBaud = autoIecBaudInput.Checked;
            int switchDelayMs = (int)iecBaudDelayInput.Value;
            await sendLock.WaitAsync();
            try
            {
                if (activeSession == null || activeSession != receiveCancellation) return;
                NetworkStream activeStream = networkStream;
                System.IO.Ports.SerialPort activePort = serialPort;
                if (activePort != null)
                {
                    int targetBaud = autoBaud ? SerialBaudControl.GetIecTargetBaud(data) : 0;
                    if (targetBaud > 0 && targetBaud != activePort.BaudRate)
                    {
                        int previousBaud = activePort.BaudRate;
                        System.Diagnostics.Stopwatch switchTime = System.Diagnostics.Stopwatch.StartNew();
                        AddNotice(LogDirection.System, "Sending IEC ACK at " + previousBaud + " baud; switch delay at least " + switchDelayMs + " ms from transmit start.");
                        await SerialBaudControl.SendAndChangeAsync(new SerialBaudPort(activePort), data, targetBaud,
                            activeSession.Token, 5000, switchDelayMs);
                        if (isClosing || activeSession != receiveCancellation) return;
                        AddEntry(LogEntry.Message(LogDirection.Sent, data));
                        baudRateInput.Text = targetBaud.ToString();
                        AddNotice(LogDirection.System, "Serial baud changed from " + previousBaud + " to " + targetBaud +
                            " after " + switchTime.ElapsedMilliseconds + " ms. Waiting for meter response.");
                        return;
                    }
                    await Task.Run(delegate { activePort.Write(data, 0, data.Length); });
                }
                else
                {
                    if (activeStream == null) throw new IOException("The connection is no longer available.");
                    await activeStream.WriteAsync(data, 0, data.Length);
                    await activeStream.FlushAsync();
                }
                if (isClosing || activeSession != receiveCancellation) return;
                AddEntry(LogEntry.Message(LogDirection.Sent, data));
            }
            catch (Exception exception)
            {
                if (!isClosing && activeSession == receiveCancellation)
                {
                    AddNotice(LogDirection.Error, "Send failed: " + FriendlyMessage(exception));
                    Disconnect("Connection closed after the send error.", false);
                }
            }
            finally
            {
                sendLock.Release();
            }
        }

        private void Disconnect(string message, bool isError)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, bool>(Disconnect), message, isError);
                return;
            }

            CancellationTokenSource cancellation = receiveCancellation;
            receiveCancellation = null;
            if (cancellation != null)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }

            NetworkStream stream = networkStream;
            networkStream = null;
            if (stream != null) stream.Close();

            TcpClient activeClient = client;
            client = null;
            if (activeClient != null) activeClient.Close();

            System.IO.Ports.SerialPort activePort = serialPort;
            serialPort = null;
            if (activePort != null)
            {
                try
                {
                    // Disconnect aborts pending output, including a flow-controlled baud-change flush.
                    try { activePort.DiscardOutBuffer(); } catch { }
                    activePort.Dispose();
                }
                catch (Exception exception)
                {
                    if (!isClosing) AddNotice(LogDirection.Error, "Could not close serial port: " + FriendlyMessage(exception));
                }
            }

            if (serialStartingBaud.HasValue)
            {
                baudRateInput.Text = serialStartingBaud.Value.ToString();
                serialStartingBaud = null;
            }
            FlushPendingIncoming();
            UpdateConnectionUi();
            if (!string.IsNullOrEmpty(message))
            {
                AddNotice(isError ? LogDirection.Error : LogDirection.System, message);
            }
        }

        private bool IsConnected
        {
            get { return mqttWorkspace != null && mqttWorkspace.IsConnected || serialPort != null && serialPort.IsOpen || client != null && client.Connected && networkStream != null; }
        }

        private void UpdateConnectionUi()
        {
            if (connectButton == null) return;
            if (IsMqttSelected && mqttWorkspace != null)
            {
                connectButton.Text = mqttWorkspace.IsActive ? "Disconnect" : "Connect";
                connectButton.Enabled = true;
                connectButton.BackColor = mqttWorkspace.IsActive ? Color.FromArgb(202, 61, 55) : Color.FromArgb(32, 111, 214);
                statusLabel.Text = mqttWorkspace.IsConnected ? "●  Connected" : mqttWorkspace.IsReconnecting ? "●  Reconnecting" : mqttWorkspace.IsActive ? "●  Connecting" : "●  Disconnected";
                statusLabel.ForeColor = mqttWorkspace.IsConnected
                    ? (isDarkMode ? Color.FromArgb(166, 220, 166) : Color.FromArgb(31, 154, 95))
                    : (isDarkMode ? Color.FromArgb(200, 200, 200) : Color.FromArgb(118, 126, 138));
            }
            else if (isConnecting)
            {
                connectButton.Text = "Connecting…";
                connectButton.Enabled = false;
                statusLabel.Text = "●  Connecting";
                statusLabel.ForeColor = isDarkMode ? Color.FromArgb(230, 190, 110) : Color.FromArgb(198, 133, 17);
            }
            else if (IsConnected)
            {
                connectButton.Text = "Disconnect";
                connectButton.Enabled = true;
                connectButton.BackColor = Color.FromArgb(202, 61, 55);
                statusLabel.Text = "●  Connected";
                statusLabel.ForeColor = isDarkMode ? Color.FromArgb(166, 220, 166) : Color.FromArgb(31, 154, 95);
            }
            else
            {
                connectButton.Text = "Connect";
                connectButton.Enabled = true;
                connectButton.BackColor = Color.FromArgb(32, 111, 214);
                statusLabel.Text = "●  Disconnected";
                statusLabel.ForeColor = isDarkMode ? Color.FromArgb(200, 200, 200) : Color.FromArgb(118, 126, 138);
            }

            hostTextBox.Enabled = !IsConnected && !isConnecting;
            portInput.Enabled = !IsConnected && !isConnecting;
            if (connectionTabs != null)
            {
                tcpConnectionFields.Visible = !IsSerialSelected && !IsMqttSelected;
                serialConnectionFields.Visible = IsSerialSelected;
                connectionPanelRow.Height = IsMqttSelected ? 60F : IsSerialSelected ? 142F : 106F;
                connectButton.Visible = true;
                if (mqttWorkspace != null)
                {
                    contentSplitter.Visible = !IsMqttSelected;
                    mqttWorkspace.Visible = IsMqttSelected;
                }
                tcpConnectionFields.Enabled = !IsConnected && !isConnecting;
                serialConnectionFields.Enabled = !isConnecting;
                UpdateSerialConnectionControls();
                UpdateSerialRtsUi();
            }
            foreach (CommandRowControl row in commandRows)
            {
                row.SetSendEnabled(IsConnected);
            }
        }

        private void AddNotice(LogDirection direction, string text)
        {
            AddEntry(LogEntry.Notice(direction, text));
        }

        private void AddEntry(LogEntry entry)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<LogEntry>(AddEntry), entry);
                return;
            }

            logEntries.Add(entry);
            AppendRenderedEntry(entry);
        }

        private void AppendRenderedEntry(LogEntry entry)
        {
            string direction;
            Color color;
            switch (entry.Direction)
            {
                case LogDirection.Sent:
                    direction = "TX ";
                    color = isDarkMode
                        ? Color.FromArgb(144, 202, 249)
                        : Color.FromArgb(11, 99, 206);
                    break;
                case LogDirection.Received:
                    direction = "RX ";
                    color = isDarkMode
                        ? Color.FromArgb(166, 220, 166)
                        : Color.FromArgb(20, 122, 75);
                    break;
                case LogDirection.Error:
                    direction = "ERR";
                    color = isDarkMode
                        ? Color.FromArgb(255, 160, 154)
                        : Color.FromArgb(201, 54, 47);
                    break;
                default:
                    direction = "SYS";
                    color = isDarkMode
                        ? Color.FromArgb(210, 210, 210)
                        : Color.FromArgb(95, 95, 95);
                    break;
            }

            string payload = entry.Data == null
                ? entry.Text
                : (displayMode == DisplayMode.Hex ? ByteCodec.ToHex(entry.Data) : ByteCodec.ToAscii(entry.Data));
            string line = "[" + entry.Timestamp.ToString("HH:mm:ss.fff") + "] " + direction + "  " + payload + Environment.NewLine;
            logView.SelectionStart = logView.TextLength;
            logView.SelectionLength = 0;
            logView.SelectionColor = color;
            logView.AppendText(line);
            logView.SelectionColor = logView.ForeColor;
            logView.SelectionStart = logView.TextLength;
            logView.ScrollToCaret();
        }

        private void SetDisplayMode(DisplayMode mode)
        {
            displayMode = mode;
            if (asciiMenuItem != null) asciiMenuItem.Checked = mode == DisplayMode.Ascii;
            if (hexMenuItem != null) hexMenuItem.Checked = mode == DisplayMode.Hex;
            RerenderLog();
        }

        private void RerenderLog()
        {
            if (logView == null) return;
            logView.Clear();
            foreach (LogEntry entry in logEntries)
            {
                AppendRenderedEntry(entry);
            }
        }

        private void AdjustLogFontSize(float change)
        {
            SetLogFontSize(logFontSize + change);
        }

        private void SetLogFontSize(float size)
        {
            float clamped = Math.Max(MinimumLogFontSize, Math.Min(MaximumLogFontSize, size));
            if (Math.Abs(clamped - logFontSize) < 0.01F && logFont != null)
            {
                return;
            }

            logFontSize = clamped;
            Font replacement = new Font("Consolas", logFontSize);
            logView.Font = replacement;
            if (logFont != null)
            {
                logFont.Dispose();
            }

            logFont = replacement;
        }

        private void SetDarkMode(bool darkMode)
        {
            isDarkMode = darkMode;
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            // Neutral grays match the readable editor surfaces in Notepad++ dark mode.
            Color windowBackground = isDarkMode
                ? Color.FromArgb(32, 32, 32)
                : Color.FromArgb(240, 240, 240);
            Color surface = isDarkMode
                ? Color.FromArgb(48, 48, 48)
                : windowBackground;
            Color input = isDarkMode
                ? Color.FromArgb(63, 63, 63)
                : Color.White;
            Color primaryText = isDarkMode
                ? Color.FromArgb(224, 224, 224)
                : Color.FromArgb(40, 40, 40);
            Color secondaryText = isDarkMode
                ? Color.FromArgb(200, 200, 200)
                : Color.FromArgb(80, 80, 80);

            BackColor = windowBackground;
            ApplyThemeToControl(this, windowBackground, surface, input, primaryText, secondaryText);

            contentSplitter.BackColor = isDarkMode
                ? Color.FromArgb(32, 32, 32)
                : windowBackground;
            logView.BackColor = isDarkMode
                ? Color.FromArgb(63, 63, 63)
                : Color.White;
            logView.ForeColor = isDarkMode
                ? Color.FromArgb(224, 224, 224)
                : Color.FromArgb(40, 40, 40);

            foreach (CommandRowControl row in commandRows)
            {
                row.ApplyTheme(isDarkMode);
            }

            themeButton.Text = isDarkMode ? "Light mode" : "Dark mode";
            themeButton.BackColor = isDarkMode
                ? Color.FromArgb(72, 72, 72)
                : Color.FromArgb(224, 224, 224);
            themeButton.ForeColor = isDarkMode
                ? Color.FromArgb(224, 224, 224)
                : Color.FromArgb(60, 60, 60);

            ApplyContextMenuTheme(surface, primaryText);
            ApplyApplicationMenuTheme(surface, primaryText);
            UpdateConnectionUi();
            ApplyNativeTitleBarTheme();
            RerenderLog();
            Invalidate(true);
        }

        private void ApplyThemeToControl(Control control, Color windowBackground, Color surface,
            Color input, Color primaryText, Color secondaryText)
        {
            MqttWorkspaceControl mqttControl = control as MqttWorkspaceControl;
            if (mqttControl != null) { mqttControl.ApplyTheme(isDarkMode); return; }
            MqttProfilesControl profilesControl = control as MqttProfilesControl;
            if (profilesControl != null)
            {
                profilesControl.ApplyTheme(isDarkMode);
                return;
            }
            if (control == logView)
            {
                return;
            }

            CommandRowControl commandRow = control as CommandRowControl;
            if (commandRow != null)
            {
                commandRow.ApplyTheme(isDarkMode);
                return;
            }

            string tag = control.Tag as string;
            if (string.Equals(tag, "Card", StringComparison.Ordinal))
            {
                ((Panel)control).BorderStyle = isDarkMode ? BorderStyle.FixedSingle : BorderStyle.None;
            }

            if (string.Equals(tag, "Root", StringComparison.Ordinal))
            {
                control.BackColor = windowBackground;
            }
            else if (string.Equals(tag, "InputFrame", StringComparison.Ordinal))
            {
                control.BackColor = isDarkMode ? surface : Color.FromArgb(200, 200, 200);
                control.Padding = isDarkMode ? Padding.Empty : new Padding(1);
            }
            else if (control == contentSplitter)
            {
                control.BackColor = isDarkMode
                    ? Color.FromArgb(32, 32, 32)
                    : windowBackground;
            }
            else if (control is TextBox || control is NumericUpDown || control is ComboBox)
            {
                control.BackColor = input;
                control.ForeColor = primaryText;
            }
            else if (control is Button)
            {
                Button button = (Button)control;
                if (button != connectButton && button != themeButton)
                {
                    button.BackColor = surface;
                    button.ForeColor = string.Equals(tag, "AccentText", StringComparison.Ordinal)
                        ? (isDarkMode ? Color.FromArgb(144, 202, 249) : Color.FromArgb(32, 111, 214))
                        : secondaryText;
                }
            }
            else if (control is Label)
            {
                control.BackColor = surface;
                if (control != statusLabel)
                {
                    control.ForeColor = control.Font.Size >= 12F ? primaryText : secondaryText;
                }
            }
            else if (control is CheckBox)
            {
                control.BackColor = surface;
                control.ForeColor = primaryText;
            }
            else if (control != this)
            {
                control.BackColor = surface;
                control.ForeColor = primaryText;
            }

            foreach (Control child in control.Controls)
            {
                ApplyThemeToControl(child, windowBackground, surface, input, primaryText, secondaryText);
            }
        }

        private void ApplyContextMenuTheme(Color surface, Color primaryText)
        {
            ContextMenuStrip menu = logView.ContextMenuStrip;
            if (menu == null)
            {
                return;
            }

            menu.BackColor = surface;
            menu.ForeColor = primaryText;
            menu.Renderer = isDarkMode
                ? (ToolStripRenderer)new ToolStripProfessionalRenderer(new DarkMenuColorTable())
                : new ToolStripProfessionalRenderer();
            ApplyMenuItemsTheme(menu.Items, surface, primaryText);
        }

        private static void ApplyMenuItemsTheme(ToolStripItemCollection items, Color surface, Color primaryText)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = surface;
                item.ForeColor = primaryText;
                ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
                if (dropDown != null)
                {
                    dropDown.DropDown.BackColor = surface;
                    dropDown.DropDown.ForeColor = primaryText;
                    ApplyMenuItemsTheme(dropDown.DropDownItems, surface, primaryText);
                }
            }
        }

        private void ApplyNativeTitleBarTheme()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            try
            {
                int enabled = isDarkMode ? 1 : 0;
                int result = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
                if (result != 0)
                {
                    DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private void CopyAllLog()
        {
            if (!string.IsNullOrEmpty(logView.Text)) Clipboard.SetText(logView.Text);
        }

        private void ClearLog()
        {
            logEntries.Clear();
            logView.Clear();
        }

        private async void ConnectionInputOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                if (!isConnecting) await ToggleConnectionAsync();
            }
        }

        private async void MainFormOnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (mqttClosePending) { e.Cancel = true; return; }
            if (!isClosing && mqttWorkspace != null && mqttWorkspace.IsActive)
            {
                e.Cancel = true;
                mqttClosePending = true;
                await mqttWorkspace.DisconnectAsync();
                isClosing = true;
                mqttClosePending = false;
                Close();
                return;
            }
            isClosing = true;
            SaveSettings();
            Disconnect(null, false);
            incomingIdleTimer.Stop();
        }

        private void SaveSettings()
        {
            settings.Host = hostTextBox.Text.Trim();
            settings.Port = (int)portInput.Value;
            SaveSerialSettings();
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            settings.WindowWidth = bounds.Width;
            settings.WindowHeight = bounds.Height;
            settings.WindowX = bounds.X;
            settings.WindowY = bounds.Y;
            settings.HasWindowPosition = true;
            settings.IsMaximized = WindowState == FormWindowState.Maximized;
            settings.DisplayMode = displayMode.ToString();
            settings.LogFontSize = logFontSize;
            settings.LogWordWrap = logView.WordWrap;
            settings.MqttLogFormat = mqttWorkspace.LogDisplayFormat;
            settings.CommandPanelHeight = contentSplitter.Panel2.Height;
            settings.IsDarkMode = isDarkMode;
            settings.Commands.Clear();
            foreach (CommandRowControl row in commandRows)
            {
                settings.Commands.Add(new CommandSetting { Text = row.CommandText, IsHex = row.IsHex });
            }

            try
            {
                SettingsStore.Save(settings);
            }
            catch (Exception exception)
            {
                MessageBox.Show("CommStudio could not save its settings.\n\n" + FriendlyMessage(exception),
                    "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string FriendlyMessage(Exception exception)
        {
            AggregateException aggregate = exception as AggregateException;
            if (aggregate != null && aggregate.InnerExceptions.Count == 1)
            {
                exception = aggregate.InnerExceptions[0];
            }

            return exception.Message;
        }

        private static Panel CreateCard()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.White;
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.Tag = "Card";
            return panel;
        }

        private static Label CreateSmallLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold);
            label.ForeColor = Color.FromArgb(105, 114, 126);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.BottomLeft;
            return label;
        }

        private static Button CreateHeaderButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(76, 88, 103);
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(4, 0, 0, 2);
            button.Tag = "HeaderButton";
            return button;
        }

        private sealed class DarkMenuColorTable : ProfessionalColorTable
        {
            private static readonly Color Surface = Color.FromArgb(48, 48, 48);
            private static readonly Color Selected = Color.FromArgb(80, 80, 80);
            private static readonly Color Border = Color.FromArgb(104, 104, 104);

            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color MenuItemSelected { get { return Selected; } }
            public override Color MenuItemBorder { get { return Border; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Surface; } }
            public override Color CheckBackground { get { return Selected; } }
            public override Color CheckSelectedBackground { get { return Selected; } }
            public override Color CheckPressedBackground { get { return Selected; } }
        }
    }
}
