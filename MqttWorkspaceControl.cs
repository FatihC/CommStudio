using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed class MqttWorkspaceControl : UserControl
    {
        private readonly MqttSession session = new MqttSession();
        private readonly MqttProfilesControl profiles;
        private readonly string messagesPath;
        private readonly string historyDirectory;
        private MqttHistoryStore history, sessionHistory;
        private string historyProfileId;
        private bool historyErrorShown;
        private Label logCaption;
        private TextBox logFilter;
        private Button filterButton;
        private ErrorProvider filterErrors;
        private List<SavedMqttMessage> messages;
        private bool messagesLoadFailed;
        private bool refreshingMessages;
        private bool transportBusy;
        private bool darkMode;
        private bool connectionCollapsed;
        private int expandedConnectionHeight;
        private SplitContainer connectionSplit;
        private SplitContainer publishSplit;
        private SplitContainer communicationSplit;
        private ListBox subscribedTopics;
        private TextBox subscriptionTopic;
        private Button subscribeButton;
        private Button unsubscribeButton;
        private MqttLogControl mqttLog;
        private ComboBox logFormat;
        private ComboBox savedMessages;
        private TextBox publishTopic;
        private JsonMessageTextBox publishMessage;
        private Button sendButton;
        private Button compressButton;
        private Button beautifyButton;
        private Button saveMessageButton;
        private Button updateMessageButton;
        private Button deleteMessageButton;
        public event Action StateChanged;
        public bool IsConnected { get { return session.IsConnected; } }
        public bool IsActive { get { return session.IsActive || transportBusy; } }
        public bool IsReconnecting { get { return session.IsReconnecting; } }
        public string LogDisplayFormat
        {
            get { return mqttLog.DisplayFormat.ToString(); }
            set { logFormat.SelectedIndex = value == "Json" ? 1 : value == "Hex" ? 2 : 0; }
        }

        public MqttWorkspaceControl(MqttProfilesControl profiles) : this(profiles, SavedMqttMessageStore.DefaultPath,
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SavedMqttMessageStore.DefaultPath), "mqtt-history")) { }
        internal MqttWorkspaceControl(MqttProfilesControl profileControl, string path)
            : this(profileControl, path, path + ".history") { }
        private MqttWorkspaceControl(MqttProfilesControl profileControl, string path, string historyPath)
        {
            profiles = profileControl;
            messagesPath = path;
            historyDirectory = historyPath;
            Dock = DockStyle.Fill;
            BuildWorkspace();
            profiles.ProfileChanged += ProfileChanged;
            mqttLog.SaveChange = SaveLogChange;
            session.MessageLogged += delegate(string direction, string topic, string text)
            {
                if (direction == "RX" || direction == "TX") return;
                MqttHistoryStore target = sessionHistory;
                MqttLogEntry entry = new MqttLogEntry(direction, topic, System.Text.Encoding.UTF8.GetBytes(text ?? ""), 0, false);
                Post(delegate { RecordSessionEntry(target, entry); });
            };
            session.PayloadLogged += delegate(string direction, string topic, byte[] bytes, int qos, bool retain)
            {
                MqttHistoryStore target = sessionHistory;
                MqttLogEntry entry = new MqttLogEntry(direction, topic, bytes, qos, retain);
                Post(delegate { RecordSessionEntry(target, entry); });
            };
            session.StateChanged += delegate { Post(UpdateState); };
            try { messages = SavedMqttMessageStore.Load(messagesPath); }
            catch (Exception)
            {
                messages = new List<SavedMqttMessage>();
                messagesLoadFailed = true;
                AppendLog("ERR", "", "Kayıtlı mesajlar okunamadı. Mevcut dosyanın üzerine yazılmayacak.");
            }
            RefreshSavedMessages(null);
            ProfileChanged();
        }

        private void ProfileChanged()
        {
            string id = profiles.SelectedProfileId;
            if (id != historyProfileId)
            {
                historyProfileId = id;
                historyErrorShown = false;
                history = id == null ? null : new MqttHistoryStore(historyDirectory, id);
                logCaption.Text = "COMMUNICATION LOG";
                mqttLog.LoadEntries(new MqttLogEntry[0]);
                if (history != null)
                    try { mqttLog.LoadEntries(history.Load()); }
                    catch (Exception exception) { ReportHistoryError(exception); }
            }
            UpdateState();
        }
        private bool SaveLogChange(string operation, MqttLogEntry entry)
        { return SaveHistory(history, operation, entry); }
        private bool SaveHistory(MqttHistoryStore target, string operation, MqttLogEntry entry)
        {
            if (target == null) return true;
            try { target.Append(operation, entry); return true; }
            catch (Exception exception) { ReportHistoryError(exception); return false; }
        }
        private void ReportHistoryError(Exception exception)
        {
            logCaption.Text = "COMMUNICATION LOG · Geçmiş kaydedilemiyor";
            logCaption.AccessibleDescription = exception.Message;
            if (!historyErrorShown)
            {
                historyErrorShown = true;
                mqttLog.AddEntry(new MqttLogEntry("ERR", "", System.Text.Encoding.UTF8.GetBytes("MQTT geçmişi kullanılamıyor: " + exception.Message), 0, false), false);
            }
        }
        private void RecordSessionEntry(MqttHistoryStore target, MqttLogEntry entry)
        {
            if (target == null || target.ProfileId == historyProfileId) mqttLog.AddEntry(entry);
            else SaveHistory(target, "add", entry);
        }

        private void BuildWorkspace()
        {
            connectionSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                Size = new Size(900, 600), SplitterWidth = 6, FixedPanel = FixedPanel.Panel1,
                Panel1MinSize = 100, Panel2MinSize = 288, SplitterDistance = 190 };
            profiles.UseCompactLayout();
            profiles.Visible = true;
            connectionSplit.Panel1.Controls.Add(profiles);
            publishSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                Size = new Size(900, 400), SplitterWidth = 6, FixedPanel = FixedPanel.Panel2,
                Panel1MinSize = 110, Panel2MinSize = 172, SplitterDistance = 214 };
            communicationSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
                Size = new Size(900, 240), SplitterWidth = 6, FixedPanel = FixedPanel.Panel1,
                Panel1MinSize = 160, Panel2MinSize = 240, SplitterDistance = 190, IsSplitterFixed = true };
            communicationSplit.Panel1.Controls.Add(BuildSubscriptions());
            communicationSplit.Panel2.Controls.Add(BuildLog());
            publishSplit.Panel1.Controls.Add(communicationSplit);
            publishSplit.Panel2.Controls.Add(BuildPublish());
            connectionSplit.Panel2.Controls.Add(publishSplit);
            connectionSplit.SizeChanged += delegate
            {
                int maximum = connectionSplit.Height - connectionSplit.SplitterWidth - connectionSplit.Panel2MinSize;
                if (maximum >= connectionSplit.Panel1MinSize && connectionSplit.SplitterDistance > maximum)
                    connectionSplit.SplitterDistance = maximum;
            };
            publishSplit.SizeChanged += delegate
            {
                int maximum = publishSplit.Height - publishSplit.SplitterWidth - publishSplit.Panel2MinSize;
                if (maximum >= publishSplit.Panel1MinSize)
                    publishSplit.SplitterDistance = Math.Max(publishSplit.Panel1MinSize, Math.Min(publishSplit.SplitterDistance, maximum));
            };
            Controls.Add(connectionSplit);
        }

        private static TableLayoutPanel CreateLayout(int rows)
        {
            TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows,
                Padding = new Padding(8, 3, 8, 5), Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return panel;
        }
        private static Button Button(string title)
        {
            Button button = new Button { Text = title, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand, Margin = new Padding(3, 1, 3, 1) };
            button.FlatAppearance.BorderSize = 0;
            button.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (button.Enabled) return;
                e.Graphics.Clear(button.BackColor);
                TextRenderer.DrawText(e.Graphics, button.Text, button.Font, button.ClientRectangle,
                    button.BackColor.GetBrightness() < 0.5F ? Color.FromArgb(170, 170, 170) : Color.FromArgb(110, 110, 110),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            return button;
        }
        private static Label Caption(string title)
        { return new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }; }

        private Control BuildSubscriptions()
        {
            TableLayoutPanel panel = CreateLayout(4);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(Caption("ABONE OLUNAN TOPIC'LER"), 0, 0);
            subscriptionTopic = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Abone olunacak topic" };
            panel.Controls.Add(subscriptionTopic, 0, 1);
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            subscribeButton = Button("Abone ol");
            unsubscribeButton = Button("Kaldır");
            subscribeButton.Click += async delegate { await RunOperation(async delegate
                { await session.SubscribeAsync(subscriptionTopic.Text); profiles.UpdateSubscriptions(session.Subscriptions); UpdateState(); }); };
            unsubscribeButton.Click += async delegate
            {
                string topic = subscribedTopics.SelectedItem as string;
                if (topic != null) await RunOperation(async delegate
                    { await session.UnsubscribeAsync(topic); profiles.UpdateSubscriptions(session.Subscriptions); UpdateState(); });
            };
            actions.Controls.Add(subscribeButton, 0, 0);
            actions.Controls.Add(unsubscribeButton, 1, 0);
            panel.Controls.Add(actions, 0, 2);
            subscribedTopics = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false,
                HorizontalScrollbar = true, AccessibleName = "Abone olunan topic listesi" };
            subscribedTopics.SelectedIndexChanged += delegate { unsubscribeButton.Enabled = IsConnected && subscribedTopics.SelectedIndex >= 0; };
            panel.Controls.Add(subscribedTopics, 0, 3);
            return panel;
        }

        private Control BuildLog()
        {
            TableLayoutPanel panel = CreateLayout(3);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            logFormat = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = Padding.Empty, DropDownWidth = 150, FlatStyle = FlatStyle.Flat, AccessibleName = "Tüm MQTT mesajlarının gösterim biçimi" };
            logFormat.Items.AddRange(new object[] { "Plain Text", "JSON", "Hex" });
            logFormat.SelectedIndex = 0;
            header.Controls.Add(logFormat, 0, 0);
            logFilter = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Topic veya mesaj içeriğinde ara", Margin = new Padding(6, 0, 20, 0) };
            filterErrors = new ErrorProvider(this) { BlinkStyle = ErrorBlinkStyle.NeverBlink };
            filterButton = Button("Filtrele");
            filterButton.Click += delegate { ApplyLogFilter(); };
            logFilter.KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.KeyCode == Keys.Enter) { ApplyLogFilter(); e.Handled = e.SuppressKeyPress = true; } };
            header.Controls.Add(logFilter, 1, 0);
            header.Controls.Add(filterButton, 2, 0);
            TableLayoutPanel title = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            title.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            logCaption = Caption("COMMUNICATION LOG");
            title.Controls.Add(logCaption, 0, 0);
            Button filterHelp = Button("?");
            filterHelp.AccessibleName = "Filtre sorgusu örnekleri";
            filterHelp.Click += delegate
            {
                MessageBox.Show(this, "İki koşul birlikte:\nserialNumber:12345 AND function:heartbeat\n\n" +
                    "Bir değeri hariç tut:\nNOT serialNumber:12345\n\n" +
                    "Sözcüklerden herhangi biri:\n12345 OR abcde\n\n" +
                    "İç içe alan:\ndevice.serialNumber:12345\n\n" +
                    "Koşulları grupla:\n(12345 OR abcde) AND function:heartbeat\n\n" +
                    "Alan:değer tam eşleşir; alan adı tek başınaysa JSON içinde her seviyede aranır. " +
                    "Sözcükler topic ve içerikte aranır. Boşlukla ayrılan sözcükler AND sayılır. " +
                    "Bir ifadeyi birlikte aramak için çift tırnak kullanın: \"hello world\".\n\n" +
                    "Büyük/küçük harf fark etmez. NOT, alanı bulunmayan mesajları da kapsar. " +
                    "Öncelik: NOT, AND, OR. Boş filtre bütün mesajları gösterir.", "Filtre örnekleri", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            title.Controls.Add(filterHelp, 1, 0);
            CheckBox wrap = new CheckBox { Text = "Word wrap", Checked = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
            Button clear = Button("Clear");
            title.Controls.Add(wrap, 2, 0);
            title.Controls.Add(clear, 3, 0);
            mqttLog = new MqttLogControl();
            logFormat.SelectedIndexChanged += delegate { mqttLog.SetDisplayFormat((MqttPayloadFormat)logFormat.SelectedIndex); };
            wrap.CheckedChanged += delegate { mqttLog.WordWrap = wrap.Checked; };
            clear.Click += delegate { mqttLog.Clear(); };
            panel.Controls.Add(title, 0, 0);
            panel.Controls.Add(header, 0, 1);
            panel.Controls.Add(mqttLog, 0, 2);
            return panel;
        }

        private void ApplyLogFilter()
        {
            try { mqttLog.SetFilter(logFilter.Text); filterErrors.SetError(logFilter, ""); }
            catch (FormatException exception) { filterErrors.SetError(logFilter, exception.Message + " Mevcut filtre korundu."); }
        }

        private Control BuildPublish()
        {
            TableLayoutPanel panel = CreateLayout(4);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(Caption("PUBLISH"), 0, 0);
            TableLayoutPanel savedRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
            savedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            savedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            savedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            savedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            savedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            savedRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            savedMessages = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, DropDownWidth = 460, AccessibleName = "Kayıtlı MQTT mesajları" };
            savedMessages.SelectedIndexChanged += delegate { SelectSavedMessage(); };
            saveMessageButton = Button("Kaydet");
            updateMessageButton = Button("Güncelle");
            deleteMessageButton = Button("Sil");
            saveMessageButton.Click += delegate
            {
                string name = AskMessageName();
                if (name != null) SaveNewMessage(name);
            };
            updateMessageButton.Click += delegate { UpdateSavedMessage(); };
            deleteMessageButton.Click += delegate { DeleteSavedMessage(); };
            savedRow.Controls.Add(Caption("Kayıtlar"), 0, 0);
            savedRow.Controls.Add(savedMessages, 1, 0);
            savedRow.Controls.Add(saveMessageButton, 2, 0);
            savedRow.Controls.Add(updateMessageButton, 3, 0);
            savedRow.Controls.Add(deleteMessageButton, 4, 0);
            panel.Controls.Add(savedRow, 0, 1);
            TableLayoutPanel topicRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            topicRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            topicRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topicRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            publishTopic = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Publish topic" };
            topicRow.Controls.Add(Caption("Topic"), 0, 0);
            topicRow.Controls.Add(publishTopic, 1, 0);
            panel.Controls.Add(topicRow, 0, 2);
            TableLayoutPanel messageRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            messageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            messageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            messageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            messageRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            publishMessage = new JsonMessageTextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, MaxLength = 0,
                ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10F), AccessibleName = "Publish mesajı" };
            sendButton = Button("Gönder");
            sendButton.Click += async delegate { await PublishAsync(); };
            compressButton = Button("Compress");
            beautifyButton = Button("Beautify");
            compressButton.Click += delegate { FormatPublishJson(false); };
            beautifyButton.Click += delegate { FormatPublishJson(true); };
            TableLayoutPanel messageActions = new TableLayoutPanel { Dock = DockStyle.Fill,
                ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            messageActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) messageActions.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            messageActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            messageActions.Controls.Add(sendButton, 0, 0);
            messageActions.Controls.Add(compressButton, 0, 1);
            messageActions.Controls.Add(beautifyButton, 0, 2);
            messageRow.Controls.Add(Caption("Mesaj"), 0, 0);
            messageRow.Controls.Add(publishMessage, 1, 0);
            messageRow.Controls.Add(messageActions, 2, 0);
            panel.Controls.Add(messageRow, 0, 3);
            return panel;
        }

        public async Task ToggleConnectionAsync()
        {
            if (session.IsActive) { await DisconnectAsync(); return; }
            if (transportBusy) return;
            transportBusy = true;
            UpdateState();
            try
            {
                MqttProfile profile = profiles.GetPersistentConnectionProfile();
                ProfileChanged();
                sessionHistory = history;
                await session.StartAsync(profile);
            }
            catch (OperationCanceledException) { AppendLog("ERR", "", "MQTT bağlantısı iptal edildi veya zaman aşımına uğradı."); }
            catch (Exception exception) { AppendLog("ERR", "", exception.Message); }
            finally { transportBusy = false; UpdateState(); }
        }

        public async Task DisconnectAsync()
        { await session.StopAsync(); AppendLog("SYS", "", "MQTT bağlantısı kapatıldı."); UpdateState(); }

        private async Task RunOperation(Func<Task> operation)
        {
            try { await operation(); }
            catch (OperationCanceledException) { AppendLog("ERR", "", "İşlem iptal edildi."); }
            catch (Exception exception) { AppendLog("ERR", "", exception.Message); }
        }

        private void UpdateState()
        {
            if (IsDisposed) return;
            UpdateConnectionPanel(IsConnected);
            profiles.SetSessionActive(IsActive);
            if (connectionCollapsed) profiles.ScrollToTop();
            subscribeButton.Enabled = IsConnected;
            sendButton.Enabled = IsConnected;
            sendButton.BackColor = IsConnected ? Color.FromArgb(32, 111, 214) :
                (darkMode ? Color.FromArgb(72, 72, 72) : Color.FromArgb(224, 224, 224));
            string selected = subscribedTopics.SelectedItem as string;
            subscribedTopics.BeginUpdate();
            subscribedTopics.Items.Clear();
            subscribedTopics.Items.AddRange(session.IsActive ? session.Subscriptions : profiles.GetSelectedSubscriptions());
            if (selected != null) subscribedTopics.SelectedItem = selected;
            subscribedTopics.EndUpdate();
            unsubscribeButton.Enabled = IsConnected && subscribedTopics.SelectedIndex >= 0;
            Action handler = StateChanged;
            if (handler != null) handler();
        }

        private bool ValidatePublishJson()
        {
            if (publishMessage.ValidateJson()) return true;
            AppendLog("ERR", "", "JSON: " + publishMessage.ValidationError.Message + " (satır " +
                publishMessage.ValidationError.Line + ", sütun " + publishMessage.ValidationError.Column + ")");
            return false;
        }

        private void FormatPublishJson(bool beautify)
        {
            if (!ValidatePublishJson()) return;
            string formatted = beautify ? JsonTextFormatter.Beautify(publishMessage.Text) : JsonTextFormatter.Compress(publishMessage.Text);
            if (formatted != publishMessage.Text)
            {
                // Replace as an edit so Ctrl+Z can restore the original formatting.
                publishMessage.ReplaceAllWithUndo(formatted);
            }
            publishMessage.Focus();
        }

        private async Task PublishAsync()
        {
            if (!ValidatePublishJson()) return;
            sendButton.Enabled = false;
            await RunOperation(delegate { return session.PublishAsync(publishTopic.Text, publishMessage.Text); });
            UpdateState();
        }

        private void UpdateConnectionPanel(bool connected)
        {
            if (connected == connectionCollapsed) return;
            if (connected)
            {
                expandedConnectionHeight = connectionSplit.SplitterDistance;
            }
            profiles.SetConnectionCollapsed(connected);
            int maximum = connectionSplit.Height - connectionSplit.SplitterWidth - connectionSplit.Panel2MinSize;
            if (maximum >= connectionSplit.Panel1MinSize)
                connectionSplit.SplitterDistance = connected ? connectionSplit.Panel1MinSize :
                    Math.Max(connectionSplit.Panel1MinSize, Math.Min(expandedConnectionHeight, maximum));
            connectionCollapsed = connected;
        }

        private void AppendLog(string direction, string topic, string text)
        {
            if (IsDisposed || mqttLog.IsDisposed) return;
            mqttLog.AddEntry(direction, topic, System.Text.Encoding.UTF8.GetBytes(text ?? ""), 0, false);
        }

        private void Post(Action action)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            { try { BeginInvoke(new Action(delegate { if (!IsDisposed) action(); })); } catch (InvalidOperationException) { } }
            else action();
        }

        private void RefreshSavedMessages(string id)
        {
            refreshingMessages = true;
            savedMessages.Items.Clear();
            savedMessages.Items.Add("Yeni mesaj");
            int selected = 0;
            foreach (SavedMqttMessage message in messages)
            { int index = savedMessages.Items.Add(message); if (message.Id == id) selected = index; }
            savedMessages.SelectedIndex = selected;
            refreshingMessages = false;
            UpdateMessageButtons();
        }
        private void SelectSavedMessage()
        {
            if (refreshingMessages) return;
            SavedMqttMessage selected = savedMessages.SelectedItem as SavedMqttMessage;
            publishTopic.Text = selected == null ? "" : selected.Topic;
            publishMessage.Text = selected == null ? "" : selected.Message;
            UpdateMessageButtons();
        }
        private void UpdateMessageButtons()
        {
            bool selected = savedMessages.SelectedItem is SavedMqttMessage;
            saveMessageButton.Enabled = !messagesLoadFailed;
            updateMessageButton.Enabled = selected && !messagesLoadFailed;
            deleteMessageButton.Enabled = selected && !messagesLoadFailed;
        }

        private string AskMessageName()
        {
            using (Form dialog = new Form { Text = "Mesajı kaydet", Size = new Size(390, 156),
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, Font = Font })
            {
                Label label = new Label { Text = "Mesaj adı", Left = 14, Top = 12, AutoSize = true };
                TextBox name = new TextBox { Left = 14, Top = 34, Width = 342, AccessibleName = "Kaydedilecek mesaj adı" };
                Button ok = new Button { Text = "Kaydet", Left = 184, Top = 72, Width = 82 };
                Button cancel = new Button { Text = "İptal", Left = 274, Top = 72, Width = 82, DialogResult = DialogResult.Cancel };
                ok.Click += delegate { if (name.Text.Trim().Length > 0) dialog.DialogResult = DialogResult.OK; else name.Focus(); };
                dialog.Controls.AddRange(new Control[] { label, name, ok, cancel });
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                ApplyColors(dialog, darkMode);
                return dialog.ShowDialog(FindForm()) == DialogResult.OK ? name.Text.Trim() : null;
            }
        }

        private void SaveNewMessage(string name)
        {
            if (messagesLoadFailed || string.IsNullOrWhiteSpace(name)) return;
            SavedMqttMessage message = new SavedMqttMessage { Id = Guid.NewGuid().ToString("N"), Name = name.Trim(),
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, Topic = publishTopic.Text, Message = publishMessage.Text };
            List<SavedMqttMessage> updated = new List<SavedMqttMessage>(messages);
            updated.Add(message);
            PersistMessages(updated, message.Id, "Mesaj kaydedildi: " + message.Name);
        }
        private void UpdateSavedMessage()
        {
            SavedMqttMessage selected = savedMessages.SelectedItem as SavedMqttMessage;
            if (selected == null || messagesLoadFailed) return;
            SavedMqttMessage updatedMessage = selected.Copy();
            updatedMessage.Topic = publishTopic.Text;
            updatedMessage.Message = publishMessage.Text;
            updatedMessage.UpdatedAtUtc = DateTime.UtcNow;
            List<SavedMqttMessage> updated = new List<SavedMqttMessage>(messages);
            updated[updated.FindIndex(delegate(SavedMqttMessage item) { return item.Id == selected.Id; })] = updatedMessage;
            PersistMessages(updated, selected.Id, "Mesaj güncellendi: " + selected.Name);
        }
        private void DeleteSavedMessage()
        {
            SavedMqttMessage selected = savedMessages.SelectedItem as SavedMqttMessage;
            if (selected == null || messagesLoadFailed) return;
            List<SavedMqttMessage> updated = new List<SavedMqttMessage>(messages);
            updated.RemoveAll(delegate(SavedMqttMessage item) { return item.Id == selected.Id; });
            PersistMessages(updated, null, "Kayıt silindi: " + selected.Name);
        }
        private void PersistMessages(List<SavedMqttMessage> updated, string id, string notice)
        {
            try
            {
                SavedMqttMessageStore.Save(messagesPath, updated);
                messages = updated;
                RefreshSavedMessages(id);
                AppendLog("SYS", "", notice);
            }
            catch (Exception) { AppendLog("ERR", "", "Mesaj kayıtları kaydedilemedi. Mevcut kayıtlar korundu."); }
        }

        public void ApplyTheme(bool dark)
        {
            darkMode = dark;
            ApplyColors(this, dark);
            profiles.ApplyTheme(dark);
            sendButton.BackColor = IsConnected ? Color.FromArgb(32, 111, 214) :
                (darkMode ? Color.FromArgb(72, 72, 72) : Color.FromArgb(224, 224, 224));
            sendButton.ForeColor = Color.White;
        }
        private static void ApplyColors(Control control, bool dark)
        {
            MqttLogControl log = control as MqttLogControl;
            if (log != null) { log.ApplyTheme(dark); return; }
            Color surface = dark ? Color.FromArgb(48, 48, 48) : Color.FromArgb(240, 240, 240);
            Color input = dark ? Color.FromArgb(63, 63, 63) : Color.White;
            control.BackColor = control is TextBoxBase || control is ComboBox || control is ListBox ? input : surface;
            control.ForeColor = dark ? Color.FromArgb(224, 224, 224) : Color.FromArgb(40, 40, 40);
            if (control is SplitContainer) control.BackColor = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(200, 200, 200);
            foreach (Control child in control.Controls) ApplyColors(child, dark);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { profiles.ProfileChanged -= ProfileChanged; session.Dispose(); if (filterErrors != null) filterErrors.Dispose(); } base.Dispose(disposing); }
    }
}
