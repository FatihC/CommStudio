using System;
using System.Drawing;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed partial class MainForm
    {
        private TabControl connectionTabs;
        private Control tcpConnectionFields;
        private Control serialConnectionFields;
        private ComboBox serialPortInput;
        private ComboBox baudRateInput;
        private ComboBox dataBitsInput;
        private ComboBox parityInput;
        private ComboBox stopBitsInput;
        private ComboBox handshakeInput;
        private CheckBox dtrInput;
        private CheckBox rtsInput;
        private SerialPort serialPort;
        private int? serialStartingBaud;
        private CheckBox autoIecBaudInput;
        private NumericUpDown iecBaudDelayInput;
        private Button applyBaudButton;
        private bool isChangingBaud;
        private readonly string[] stopBitValues = { "One", "OnePointFive", "Two" };
        private readonly string[] handshakeValues = { "None", "XOnXOff", "RequestToSend", "RequestToSendXOnXOff" };

        private bool IsSerialSelected { get { return connectionTabs.SelectedIndex == 1; } }
        private bool IsMqttSelected { get { return connectionTabs.SelectedIndex == 2; } }

        private Control BuildConnectionPanel()
        {
            Panel card = CreateCard();
            card.Margin = new Padding(0, 0, 0, 10);
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(10, 4, 10, 4);
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.Margin = Padding.Empty;
            header.ColumnCount = 6;
            header.RowCount = 1;
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 222F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94F));
            Label title = new Label { Text = "CommStudio", Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            connectButton = CreateHeaderButton("Connect");
            connectButton.ForeColor = Color.White;
            connectButton.Font = new Font("Segoe UI", 10F);
            connectButton.Click += async delegate { await ToggleConnectionAsync(); };
            themeButton = CreateHeaderButton("Dark mode");
            themeButton.Click += delegate { SetDarkMode(!isDarkMode); };
            TableLayoutPanel brand = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty,
                ColumnCount = 2, RowCount = 1 };
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32F));
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            brand.Controls.Add(BuildApplicationMenuButton(), 0, 0);
            brand.Controls.Add(title, 1, 0);
            header.Controls.Add(brand, 0, 0);
            header.Controls.Add(statusLabel, 3, 0);
            header.Controls.Add(connectButton, 4, 0);
            header.Controls.Add(themeButton, 5, 0);

            connectionTabs = new ConnectionTabControl { Dock = DockStyle.Fill, Margin = Padding.Empty,
                DrawMode = TabDrawMode.OwnerDrawFixed, ItemSize = new Size(70, 32), SizeMode = TabSizeMode.Fixed,
                AccessibleName = "Connection type" };
            connectionTabs.Selecting += delegate(object sender, TabControlCancelEventArgs e)
            {
                e.Cancel = IsConnected || isConnecting || mqttWorkspace != null && mqttWorkspace.IsActive;
            };
            Panel connectionFields = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty,
                Padding = new Padding(0, 4, 0, 0) };
            TableLayoutPanel tcpFields = CreateConnectionFields(2);
            tcpConnectionFields = tcpFields;
            tcpFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tcpFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            hostTextBox = new TextBox { Dock = DockStyle.Fill };
            portInput = new NumericUpDown { Minimum = 1, Maximum = 65535, Dock = DockStyle.Fill };
            hostTextBox.KeyDown += ConnectionInputOnKeyDown;
            portInput.KeyDown += ConnectionInputOnKeyDown;
            AddConnectionField(tcpFields, 0, "HOST", hostTextBox);
            AddConnectionField(tcpFields, 1, "PORT", portInput);
            Control serialFields = BuildSerialFields();
            serialConnectionFields = serialFields;
            serialFields.Visible = false;
            connectionFields.Controls.Add(tcpFields);
            connectionFields.Controls.Add(serialFields);
            connectionTabs.TabPages.Add(new TabPage("TCP"));
            connectionTabs.TabPages.Add(new TabPage("Serial"));
            connectionTabs.TabPages.Add(new TabPage("MQTT"));
            connectionTabs.SelectedIndexChanged += delegate
            {
                connectionTabs.Invalidate();
                UpdateConnectionUi();
            };
            header.Controls.Add(connectionTabs, 1, 0);
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(connectionFields, 0, 1);
            card.Controls.Add(layout);
            return card;
        }

        private static TableLayoutPanel CreateConnectionFields(int columns)
        {
            TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns,
                RowCount = 2, Margin = Padding.Empty };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 17F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            return panel;
        }

        private static void AddConnectionField(TableLayoutPanel panel, int column, string label, Control input)
        {
            panel.Controls.Add(CreateSmallLabel(label), column, 0);
            input.Margin = new Padding(3, 2, 5, 0);
            panel.Controls.Add(input, column, 1);
        }

        private ComboBox CreateSerialCombo(params string[] values)
        {
            ComboBox combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(values);
            if (values.Length > 0) combo.SelectedIndex = 0;
            combo.KeyDown += ConnectionInputOnKeyDown;
            return combo;
        }

        private Control BuildSerialFields()
        {
            TableLayoutPanel fields = CreateConnectionFields(7);
            float[] widths = { 24F, 15F, 8F, 11F, 8F, 21F, 13F };
            foreach (float width in widths) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
            TableLayoutPanel portField = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            portField.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            portField.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
            serialPortInput = CreateSerialCombo();
            serialPortInput.DropDownStyle = ComboBoxStyle.DropDown;
            serialPortInput.Margin = Padding.Empty;
            Button refresh = CreateHeaderButton("↻");
            refresh.AccessibleName = "Refresh serial ports";
            refresh.Margin = Padding.Empty;
            refresh.Click += delegate { RefreshSerialPorts(); };
            portField.Controls.Add(serialPortInput, 0, 0);
            portField.Controls.Add(refresh, 1, 0);
            baudRateInput = CreateSerialCombo("110", "300", "600", "1200", "2400", "4800", "9600", "14400", "19200", "38400", "57600", "115200", "230400", "460800", "921600");
            baudRateInput.DropDownStyle = ComboBoxStyle.DropDown;
            baudRateInput.KeyDown -= ConnectionInputOnKeyDown;
            baudRateInput.KeyDown += async delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                e.Handled = true;
                if (serialPort != null) await ApplySerialBaudAsync();
                else if (!isConnecting) await ToggleConnectionAsync();
            };
            dataBitsInput = CreateSerialCombo("5", "6", "7", "8");
            parityInput = CreateSerialCombo(Enum.GetNames(typeof(Parity)));
            stopBitsInput = CreateSerialCombo("1", "1.5", "2");
            handshakeInput = CreateSerialCombo("None", "XON/XOFF", "RTS/CTS", "RTS/CTS + XON/XOFF");
            handshakeInput.DropDownWidth = 200;
            FlowLayoutPanel signals = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            dtrInput = new CheckBox { Text = "DTR", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            rtsInput = new CheckBox { Text = "RTS", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            // Stack the output switches to stay usable at the minimum window width.
            signals.FlowDirection = FlowDirection.TopDown;
            signals.Controls.Add(dtrInput);
            signals.Controls.Add(rtsInput);
            handshakeInput.SelectedIndexChanged += delegate { UpdateSerialRtsUi(); };
            AddConnectionField(fields, 0, "COM PORT", portField);
            AddConnectionField(fields, 1, "BAUD", baudRateInput);
            AddConnectionField(fields, 2, "DATA", dataBitsInput);
            AddConnectionField(fields, 3, "PARITY", parityInput);
            AddConnectionField(fields, 4, "STOP", stopBitsInput);
            AddConnectionField(fields, 5, "FLOW CONTROL", handshakeInput);
            fields.Controls.Add(signals, 6, 0);
            fields.SetRowSpan(signals, 2);
            TableLayoutPanel serialLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            serialLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            serialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            TableLayoutPanel options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106F));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66F));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            autoIecBaudInput = new CheckBox { Text = "IEC 62056-21: auto baud after ACK", Dock = DockStyle.Fill,
                Margin = new Padding(3, 3, 0, 0) };
            applyBaudButton = CreateHeaderButton("Apply baud (connected)");
            applyBaudButton.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (applyBaudButton.Enabled) return;
                e.Graphics.Clear(applyBaudButton.BackColor);
                TextRenderer.DrawText(e.Graphics, applyBaudButton.Text, applyBaudButton.Font, applyBaudButton.ClientRectangle,
                    isDarkMode ? Color.FromArgb(160, 160, 160) : Color.FromArgb(110, 110, 110),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            applyBaudButton.Click += async delegate { await ApplySerialBaudAsync(); };
            options.Controls.Add(autoIecBaudInput, 0, 0);
            options.Controls.Add(new Label { Text = "Switch delay (ms)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 1, 0);
            iecBaudDelayInput = new NumericUpDown { Minimum = 0, Maximum = 1000, Increment = 10, Value = 250,
                Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 0) };
            options.Controls.Add(iecBaudDelayInput, 2, 0);
            options.Controls.Add(applyBaudButton, 3, 0);
            serialLayout.Controls.Add(fields, 0, 0);
            serialLayout.Controls.Add(options, 0, 1);
            return serialLayout;
        }

        private void UpdateSerialConnectionControls()
        {
            if (applyBaudButton == null) return;
            bool editable = !IsConnected && !isConnecting;
            serialPortInput.Parent.Enabled = editable;
            dataBitsInput.Enabled = editable;
            parityInput.Enabled = editable;
            stopBitsInput.Enabled = editable;
            handshakeInput.Enabled = editable;
            dtrInput.Enabled = editable;
            baudRateInput.Enabled = !isConnecting && !isChangingBaud;
            autoIecBaudInput.Enabled = !isConnecting;
            applyBaudButton.Enabled = serialPort != null && serialPort.IsOpen && !isChangingBaud;
        }

        private async Task ApplySerialBaudAsync()
        {
            if (serialPort == null || isChangingBaud) return;
            int targetBaud;
            if (!int.TryParse(baudRateInput.Text, out targetBaud) || targetBaud < 1 || targetBaud > 4000000)
            {
                AddNotice(LogDirection.Error, "Baud rate must be a whole number between 1 and 4000000.");
                return;
            }
            SerialPort activePort = serialPort;
            CancellationTokenSource activeSession = receiveCancellation;
            isChangingBaud = true;
            UpdateSerialConnectionControls();
            await sendLock.WaitAsync();
            try
            {
                if (isClosing || activeSession != receiveCancellation) return;
                int previousBaud = activePort.BaudRate;
                await SerialBaudControl.SendAndChangeAsync(new SerialBaudPort(activePort), null, targetBaud,
                    activeSession.Token, 5000);
                if (isClosing || activeSession != receiveCancellation) return;
                baudRateInput.Text = targetBaud.ToString();
                AddNotice(LogDirection.System, "Serial baud changed from " + previousBaud + " to " + targetBaud + " without reconnecting.");
            }
            catch (Exception exception)
            {
                if (!isClosing && activeSession == receiveCancellation)
                    Disconnect("Baud change failed: " + FriendlyMessage(exception), true);
            }
            finally
            {
                sendLock.Release();
                isChangingBaud = false;
                if (!isClosing) UpdateSerialConnectionControls();
            }
        }

        private void UpdateSerialRtsUi()
        {
            if (rtsInput == null) return;
            rtsInput.Enabled = handshakeInput.SelectedIndex < 2 && !IsConnected && !isConnecting;
            rtsInput.Text = handshakeInput.SelectedIndex >= 2 ? "RTS auto" : "RTS";
        }

        private void RefreshSerialPorts()
        {
            string selected = serialPortInput.Text;
            try
            {
                string[] ports = SerialPort.GetPortNames();
                Array.Sort(ports, delegate(string left, string right)
                {
                    int a, b;
                    return int.TryParse(left.Substring(3), out a) && int.TryParse(right.Substring(3), out b)
                        ? a.CompareTo(b) : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
                });
                serialPortInput.Items.Clear();
                serialPortInput.Items.AddRange(ports);
                serialPortInput.Text = selected.Length > 0 ? selected : ports.Length > 0 ? ports[0] : string.Empty;
            }
            catch (Exception exception)
            {
                AddNotice(LogDirection.Error, "Could not list serial ports: " + FriendlyMessage(exception));
            }
        }

        private void RestoreSerialSettings()
        {
            serialPortInput.Text = settings.SerialPortName;
            RefreshSerialPorts();
            baudRateInput.Text = settings.BaudRate.ToString();
            dataBitsInput.SelectedItem = settings.DataBits.ToString();
            parityInput.SelectedItem = settings.SerialParity;
            stopBitsInput.SelectedIndex = Array.IndexOf(stopBitValues, settings.SerialStopBits);
            handshakeInput.SelectedIndex = Array.IndexOf(handshakeValues, settings.SerialHandshake);
            dtrInput.Checked = settings.DtrEnabled;
            rtsInput.Checked = settings.RtsEnabled;
            autoIecBaudInput.Checked = settings.AutoIecBaudSwitch;
            iecBaudDelayInput.Value = settings.IecBaudSwitchDelayMs ?? 250;
            connectionTabs.SelectedIndex = settings.ConnectionType == "MQTT" ? 2 : settings.ConnectionType == "Serial" ? 1 : 0;
        }

        private void SaveSerialSettings()
        {
            settings.ConnectionType = IsMqttSelected ? "MQTT" : IsSerialSelected ? "Serial" : "TCP";
            settings.SerialPortName = serialPortInput.Text.Trim();
            int baud;
            if (serialStartingBaud.HasValue) settings.BaudRate = serialStartingBaud.Value;
            else if (int.TryParse(baudRateInput.Text, out baud) && baud > 0 && baud <= 4000000) settings.BaudRate = baud;
            settings.DataBits = int.Parse(dataBitsInput.Text);
            settings.SerialParity = parityInput.Text;
            settings.SerialStopBits = stopBitValues[stopBitsInput.SelectedIndex];
            settings.SerialHandshake = handshakeValues[handshakeInput.SelectedIndex];
            settings.DtrEnabled = dtrInput.Checked;
            settings.RtsEnabled = rtsInput.Checked;
            settings.AutoIecBaudSwitch = autoIecBaudInput.Checked;
            settings.IecBaudSwitchDelayMs = (int)iecBaudDelayInput.Value;
        }

        private SerialPort CreateConfiguredSerialPort()
        {
            string name = serialPortInput.Text.Trim();
            if (name.Length == 0) throw new ArgumentException("Select or enter a COM port, then connect. Use the refresh button after plugging in a device.");
            int baud;
            if (!int.TryParse(baudRateInput.Text, out baud) || baud < 1 || baud > 4000000)
                throw new ArgumentException("Baud rate must be a whole number between 1 and 4000000.");
            return new SerialPort(name, baud, (Parity)Enum.Parse(typeof(Parity), parityInput.Text),
                int.Parse(dataBitsInput.Text), (StopBits)Enum.Parse(typeof(StopBits), stopBitValues[stopBitsInput.SelectedIndex]))
            {
                Handshake = (Handshake)Enum.Parse(typeof(Handshake), handshakeValues[handshakeInput.SelectedIndex]),
                DtrEnable = dtrInput.Checked,
                RtsEnable = handshakeInput.SelectedIndex < 2 && rtsInput.Checked,
                ReadTimeout = 200,
                WriteTimeout = 2000,
                ParityReplace = 0
            };
        }

        private async Task ConnectSerialAsync()
        {
            SerialPort pendingPort = null;
            try
            {
                pendingPort = CreateConfiguredSerialPort();
                isConnecting = true;
                UpdateConnectionUi();
                AddNotice(LogDirection.System, "Opening " + pendingPort.PortName + " …");
                await Task.Run(delegate { pendingPort.Open(); });
                if (isClosing) { pendingPort.Dispose(); return; }
                serialPort = pendingPort;
                serialStartingBaud = pendingPort.BaudRate;
                receiveCancellation = new CancellationTokenSource();
                AddNotice(LogDirection.System, "Connected to " + pendingPort.PortName + " · " + pendingPort.BaudRate + " baud · " +
                    pendingPort.DataBits + " data bits · " + pendingPort.Parity + " parity · " + stopBitsInput.Text +
                    " stop bits · Flow: " + handshakeInput.Text + ".");
                ReceiveSerialLoopAsync(pendingPort, receiveCancellation.Token);
            }
            catch (Exception exception)
            {
                if (pendingPort != null) pendingPort.Dispose();
                if (!isClosing) AddNotice(LogDirection.Error, "Serial connection failed: " + FriendlyMessage(exception));
            }
            finally
            {
                isConnecting = false;
                if (!isClosing) UpdateConnectionUi();
            }
        }

        private async void ReceiveSerialLoopAsync(SerialPort activePort, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int count = await Task.Run(delegate
                    {
                        try { return activePort.Read(buffer, 0, buffer.Length); }
                        catch (TimeoutException) { return 0; }
                    });
                    if (isClosing || cancellationToken.IsCancellationRequested || serialPort != activePort) return;
                    if (count == 0) continue;
                    byte[] copy = new byte[count];
                    Buffer.BlockCopy(buffer, 0, copy, 0, count);
                    AppendIncoming(copy);
                }
            }
            catch (Exception exception)
            {
                if (!isClosing && !cancellationToken.IsCancellationRequested && serialPort == activePort)
                    Disconnect("Serial connection lost: " + FriendlyMessage(exception), true);
            }
        }

        private sealed class ConnectionTabControl : TabControl
        {
            public ConnectionTabControl()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Color surface = TabCount > 0 ? TabPages[0].BackColor : SystemColors.Control;
                bool dark = surface.GetBrightness() < 0.5F;
                Color border = dark ? Color.FromArgb(104, 104, 104) : Color.FromArgb(180, 180, 180);
                e.Graphics.Clear(surface);
                for (int index = 0; index < TabCount; index++)
                {
                    Rectangle bounds = GetTabRect(index);
                    bounds.Inflate(-2, -1);
                    bool selected = index == SelectedIndex;
                    if (!selected) bounds.Y += 3;
                    bounds.Height = Height - bounds.Top - 1;
                    using (Brush fill = new SolidBrush(selected ? surface :
                        (dark ? Color.FromArgb(64, 64, 64) : Color.FromArgb(224, 224, 224))))
                        e.Graphics.FillRectangle(fill, bounds);
                    using (Pen outline = new Pen(border))
                    {
                        e.Graphics.DrawLine(outline, bounds.Left, bounds.Bottom, bounds.Left, bounds.Top + 3);
                        e.Graphics.DrawLine(outline, bounds.Left, bounds.Top + 3, bounds.Left + 3, bounds.Top);
                        e.Graphics.DrawLine(outline, bounds.Left + 3, bounds.Top, bounds.Right - 3, bounds.Top);
                        e.Graphics.DrawLine(outline, bounds.Right - 3, bounds.Top, bounds.Right, bounds.Top + 3);
                        e.Graphics.DrawLine(outline, bounds.Right, bounds.Top + 3, bounds.Right, bounds.Bottom);
                        if (!selected) e.Graphics.DrawLine(outline, bounds.Left, bounds.Bottom, bounds.Right, bounds.Bottom);
                    }
                    TextRenderer.DrawText(e.Graphics, TabPages[index].Text, Font, bounds, ForeColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    if (selected)
                    {
                        using (Pen pen = new Pen(dark ? Color.FromArgb(144, 202, 249) : Color.FromArgb(32, 111, 214), 2))
                            e.Graphics.DrawLine(pen, bounds.Left + 4, bounds.Top + 1, bounds.Right - 4, bounds.Top + 1);
                        if (Focused && ShowFocusCues)
                        {
                            bounds.Inflate(-6, -6);
                            ControlPaint.DrawFocusRectangle(e.Graphics, bounds, ForeColor, surface);
                        }
                    }
                }
            }
        }
    }
}
