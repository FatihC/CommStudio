using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed class MqttProfilesControl : UserControl
    {
        private readonly string storePath;
        private List<MqttProfile> profiles;
        private readonly Dictionary<string, Control> fields = new Dictionary<string, Control>();
        private readonly Dictionary<string, Draft> drafts = new Dictionary<string, Draft>();
        private ComboBox profileSelector;
        private DataGridView userProperties;
        private Label feedback;
        private Button saveButton;
        private Button deleteButton;
        private bool sessionActive;
        private List<string> newSubscriptions = new List<string>();
        public event Action ProfileChanged;
        private Panel scrollPanel;
        private string currentKey;
        private bool loading;
        private bool loadFailed;
        private TableLayoutPanel generalSection;
        private TableLayoutPanel formBody;
        private TableLayoutPanel rightLayout;
        private readonly List<Button> sectionHeadings = new List<Button>();
        private readonly List<TableLayoutPanel> sections = new List<TableLayoutPanel>();

        private sealed class Draft
        {
            public Dictionary<string, object> Values = new Dictionary<string, object>();
            public List<MqttUserProperty> Properties;
        }

        public MqttProfilesControl() : this(MqttProfileStore.DefaultPath) { }

        internal MqttProfilesControl(string path)
        {
            storePath = path;
            Dock = DockStyle.Fill;
            BuildForm();
            try { profiles = MqttProfileStore.Load(storePath); }
            catch (Exception)
            {
                profiles = new List<MqttProfile>();
                loadFailed = true;
                feedback.Text = "MQTT kayıtları okunamadı. Mevcut dosyanın üzerine yazılmayacak.";
                saveButton.Enabled = false;
            }
            RefreshProfiles(null);
        }

        private void BuildForm()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(10, 0, 10, 10) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);
            TableLayoutPanel sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
                RowCount = 4, Padding = new Padding(0, 8, 16, 0), Margin = Padding.Empty };
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebar.Controls.Add(new Label { Text = "BAĞLANTILAR", Dock = DockStyle.Fill }, 0, 0);
            profileSelector = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, AccessibleName = "Kayıtlı MQTT bağlantıları", DropDownWidth = 280 };
            profileSelector.SelectedIndexChanged += delegate { SelectProfile(); };
            sidebar.Controls.Add(profileSelector, 0, 1);
            sidebar.Controls.Add(new Label { Text = "Bir bağlantı seçin veya Yeni Ekle ile başlayın.\n\nDeğişiklikleri Kaydet ile saklayın.",
                Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) }, 0, 2);
            root.Controls.Add(sidebar, 0, 0);

            TableLayoutPanel right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Margin = Padding.Empty };
            rightLayout = right;
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            TableLayoutPanel toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            toolbar.Controls.Add(new Label { Text = "MQTT bağlantı ayarları", Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            saveButton = new Button { Text = "Kaydet", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand, Margin = new Padding(4, 3, 4, 3) };
            saveButton.FlatAppearance.BorderSize = 0;
            saveButton.Click += delegate { SaveProfile(); };
            deleteButton = new Button { Text = "Sil", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand, Margin = new Padding(4, 3, 4, 3), AccessibleName = "Bağlantıyı sil" };
            deleteButton.FlatAppearance.BorderSize = 0;
            deleteButton.Click += delegate { DeleteProfile(); };
            foreach (Button button in new[] { saveButton, deleteButton })
                button.Paint += delegate(object sender, PaintEventArgs e)
                {
                    Button control = (Button)sender;
                    if (control.Enabled) return;
                    e.Graphics.Clear(control.BackColor);
                    TextRenderer.DrawText(e.Graphics, control.Text, control.Font, control.ClientRectangle,
                        control.BackColor.GetBrightness() < 0.5F ? Color.FromArgb(190, 190, 190) : Color.FromArgb(110, 110, 110),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
            toolbar.Controls.Add(deleteButton, 1, 0);
            toolbar.Controls.Add(saveButton, 2, 0);
            right.Controls.Add(toolbar, 0, 0);
            scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(0, 0, 8, 0) };
            formBody = body;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scrollPanel.Controls.Add(body);
            right.Controls.Add(scrollPanel, 0, 1);
            feedback = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Text = "Profiller bu bilgisayarda saklanır. Üstteki Connect ile formdaki ayarları kullanarak bağlanabilirsiniz." };
            right.Controls.Add(feedback, 0, 2);
            root.Controls.Add(right, 1, 0);

            TableLayoutPanel general = AddSection(body, "General", false);
            generalSection = general;
            AddText(general, "Name *", "Name");
            AddChoice(general, "Transport", "Scheme", "mqtt://", "mqtts://", "ws://", "wss://");
            AddText(general, "Host *", "Host");
            AddNumber(general, "Port *", "Port", 1, 65535);
            AddText(general, "Client ID", "ClientId");
            AddText(general, "Username", "Username");
            TextBox password = AddText(general, "Password", "Password");
            password.UseSystemPasswordChar = true;
            AddCheck(general, "SSL/TLS", "UseTls");
            ((ComboBox)fields["Scheme"]).SelectedIndexChanged += delegate
            {
                if (loading) return;
                string scheme = fields["Scheme"].Text;
                ((CheckBox)fields["UseTls"]).Checked = scheme == "mqtts://" || scheme == "wss://";
            };
            ((CheckBox)fields["UseTls"]).CheckedChanged += delegate
            {
                if (loading) return;
                bool websocket = fields["Scheme"].Text.StartsWith("ws", StringComparison.Ordinal);
                fields["Scheme"].Text = ((CheckBox)fields["UseTls"]).Checked
                    ? (websocket ? "wss://" : "mqtts://") : (websocket ? "ws://" : "mqtt://");
            };

            TableLayoutPanel advanced = AddSection(body, "Advanced", true);
            AddChoice(advanced, "MQTT Version", "Version", "5.0", "3.1.1");
            AddNumber(advanced, "Connect timeout (s)", "ConnectTimeout", 1, 3600);
            AddNumber(advanced, "Keep alive (s)", "KeepAlive", 0, 65535);
            AddCheck(advanced, "Auto reconnect", "AutoReconnect");
            AddNumber(advanced, "Reconnect period (ms)", "ReconnectPeriod", 1, int.MaxValue);
            AddCheck(advanced, "Clean start / session", "CleanStart");
            AddNumber(advanced, "Session expiry (s)", "SessionExpiryInterval", 0, uint.MaxValue);
            AddText(advanced, "Receive maximum", "ReceiveMaximum");
            AddText(advanced, "Maximum packet size", "MaximumPacketSize");
            AddText(advanced, "Topic alias maximum", "TopicAliasMaximum");
            AddCheck(advanced, "Request response info", "RequestResponseInfo");
            AddCheck(advanced, "Request problem info", "RequestProblemInfo");
            AddText(advanced, "Authentication method", "AuthenticationMethod");
            AddFullRow(advanced, new Label { Text = "İsteğe bağlı sayısal alanlar boş bırakılabilir. MQTT 5 alanları seçilen profilde korunur.",
                Dock = DockStyle.Fill }, 40);

            TableLayoutPanel properties = AddSection(body, "User Properties", true);
            userProperties = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false,
                AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersWidth = 28,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                AccessibleName = "MQTT user properties" };
            userProperties.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Key" });
            userProperties.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value" });
            AddFullRow(properties, userProperties, 140);

            TableLayoutPanel will = AddSection(body, "Last Will and Testament", true);
            AddText(will, "Last-Will topic", "WillTopic");
            AddNumber(will, "Last-Will QoS", "WillQos", 0, 2);
            AddCheck(will, "Last-Will retain", "WillRetain");
            TextBox payload = AddText(will, "Last-Will payload", "WillPayload");
            payload.Multiline = true;
            payload.AcceptsReturn = true;
            payload.ScrollBars = ScrollBars.Vertical;
            will.RowStyles[will.RowCount - 1].Height = 110;
            AddChoice(will, "Payload editor format", "WillPayloadFormat", "Plaintext", "JSON");
            AddCheck(will, "Payload format indicator", "PayloadFormatIndicator");
            AddNumber(will, "Will delay (s)", "WillDelayInterval", 0, uint.MaxValue);

            foreach (Control input in fields.Values)
            {
                input.TextChanged += delegate { MarkDraft(); };
                CheckBox check = input as CheckBox;
                if (check != null) check.CheckedChanged += delegate { MarkDraft(); };
            }
            userProperties.CellValueChanged += delegate { MarkDraft(); };
            userProperties.RowsRemoved += delegate { MarkDraft(); };
        }

        private TableLayoutPanel AddSection(TableLayoutPanel body, string title, bool collapsible)
        {
            Button heading = new Button { Text = (collapsible ? "▾  " : "") + title, FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            heading.FlatAppearance.BorderSize = 0;
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            body.Controls.Add(heading, 0, body.RowCount++);
            TableLayoutPanel section = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164));
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.Controls.Add(section, 0, body.RowCount++);
            sections.Add(section);
            sectionHeadings.Add(heading);
            if (collapsible) heading.Click += delegate
            {
                section.Visible = !section.Visible;
                heading.Text = (section.Visible ? "▾  " : "▸  ") + title;
            };
            return section;
        }

        public MqttProfile GetConnectionProfile() { return ReadProfile(); }
        public string SelectedProfileId
        { get { MqttProfile selected = profileSelector.SelectedItem as MqttProfile; return selected == null ? null : selected.Id; } }
        public MqttProfile GetPersistentConnectionProfile()
        {
            MqttProfile profile = ReadProfile();
            if (SelectedProfileId == null)
            {
                if (loadFailed) throw new InvalidOperationException("Bağlantı kaydedilemediği için geçmiş başlatılamadı.");
                PersistProfile(profile);
            }
            return profile;
        }

        public string[] GetSelectedSubscriptions()
        {
            MqttProfile selected = profileSelector.SelectedItem as MqttProfile;
            return (selected == null ? newSubscriptions : selected.Subscriptions).ToArray();
        }

        public void UpdateSubscriptions(string[] topics)
        {
            MqttProfile selected = profileSelector.SelectedItem as MqttProfile;
            if (selected == null) { newSubscriptions = new List<string>(topics); return; }
            if (loadFailed) throw new InvalidOperationException("Bağlantı kayıtları okunamadığı için topic listesi kaydedilemedi.");
            List<MqttProfile> updated = new List<MqttProfile>(profiles);
            MqttProfile copy = selected.Copy();
            copy.Subscriptions = new List<string>(topics);
            updated[updated.FindIndex(delegate(MqttProfile item) { return item.Id == selected.Id; })] = copy;
            MqttProfileStore.Save(storePath, updated);
            // Keep the selected object and form draft intact; only subscriptions are saved here.
            selected.Subscriptions = copy.Subscriptions;
        }

        public void ScrollToTop() { scrollPanel.AutoScrollPosition = Point.Empty; }

        public void SetConnectionCollapsed(bool collapsed)
        {
            // Leave one complete input row visible beneath the toolbar at the 100px height.
            rightLayout.RowStyles[0].Height = collapsed ? 36 : 38;
            rightLayout.RowStyles[2].Height = collapsed ? 18 : 24;
            if (collapsed) ScrollToTop();
        }

        public void UseCompactLayout()
        {
            rightLayout.RowStyles[0].Height = 38;
            rightLayout.RowStyles[2].Height = 24;
            sectionHeadings[0].Visible = false;
            formBody.RowStyles[0].Height = 0;
            generalSection.SuspendLayout();
            List<Control> labels = new List<Control>();
            foreach (Control child in generalSection.Controls) if (child is Label) labels.Add(child);
            foreach (Control label in labels) { generalSection.Controls.Remove(label); label.Dispose(); }
            generalSection.Controls.Clear();
            generalSection.ColumnCount = 4;
            generalSection.ColumnStyles.Clear();
            generalSection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
            generalSection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            generalSection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            generalSection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            generalSection.RowStyles.Clear();
            generalSection.RowCount = 4;
            string[] names = { "Name", "ClientId", "Host", "Port", "Username", "Password", "Scheme", "UseTls" };
            string[] captions = { "Name *", "Client ID", "Host *", "Port *", "Username", "Password", "Transport", "SSL/TLS" };
            for (int row = 0; row < 4; row++) generalSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            for (int i = 0; i < names.Length; i++)
            {
                int column = (i % 2) * 2;
                generalSection.Controls.Add(new Label { Text = captions[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, column, i / 2);
                Control input = fields[names[i]];
                input.Margin = new Padding(3, 3, 5, 3);
                generalSection.Controls.Add(input, column + 1, i / 2);
            }
            generalSection.ResumeLayout(true);
            for (int i = 1; i < sections.Count; i++)
            { sections[i].Visible = false; sectionHeadings[i].Text = sectionHeadings[i].Text.Replace("▾", "▸"); }
        }

        public void SetSessionActive(bool active)
        {
            sessionActive = active;
            profileSelector.Enabled = !active;
            foreach (Control input in fields.Values) input.Enabled = !active;
            userProperties.Enabled = !active;
            saveButton.Enabled = !active && !loadFailed;
            deleteButton.Enabled = !active && !loadFailed && profileSelector.SelectedItem is MqttProfile;
        }

        private void AddField(TableLayoutPanel section, string label, string property, Control input)
        {
            int row = section.RowCount++;
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            section.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(3, 5, 5, 4);
            input.AccessibleName = label;
            section.Controls.Add(input, 1, row);
            fields.Add(property, input);
        }

        private TextBox AddText(TableLayoutPanel section, string label, string property)
        {
            TextBox input = new TextBox();
            AddField(section, label, property, input);
            return input;
        }

        private void AddNumber(TableLayoutPanel section, string label, string property, decimal min, decimal max)
        { AddField(section, label, property, new NumericUpDown { Minimum = min, Maximum = max }); }
        private void AddCheck(TableLayoutPanel section, string label, string property)
        { AddField(section, label, property, new CheckBox()); }
        private void AddChoice(TableLayoutPanel section, string label, string property, params string[] choices)
        {
            ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            combo.Items.AddRange(choices);
            AddField(section, label, property, combo);
        }
        private static void AddFullRow(TableLayoutPanel section, Control input, int height)
        {
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            section.Controls.Add(input, 0, section.RowCount++);
            section.SetColumnSpan(input, 2);
        }

        private void MarkDraft()
        {
            if (!loading && !loadFailed) feedback.Text = "Kaydedilmemiş değişiklikler var.";
        }

        private void RefreshProfiles(string selectedId)
        {
            loading = true;
            profileSelector.Items.Clear();
            profileSelector.Items.Add("＋ Yeni Ekle");
            int selected = 0;
            foreach (MqttProfile profile in profiles)
            {
                int index = profileSelector.Items.Add(profile);
                if (profile.Id == selectedId) selected = index;
            }
            profileSelector.SelectedIndex = selected;
            loading = false;
            currentKey = null;
            SelectProfile();
        }

        private List<MqttUserProperty> ReadUserProperties()
        {
            userProperties.EndEdit();
            List<MqttUserProperty> values = new List<MqttUserProperty>();
            foreach (DataGridViewRow row in userProperties.Rows)
            {
                if (row.IsNewRow) continue;
                string key = Convert.ToString(row.Cells[0].Value);
                string value = Convert.ToString(row.Cells[1].Value);
                if (key.Length > 0 || value.Length > 0) values.Add(new MqttUserProperty { Key = key, Value = value });
            }
            return values;
        }

        private void SelectProfile()
        {
            if (loading) return;
            if (currentKey != null)
            {
                Draft draft = new Draft { Properties = ReadUserProperties() };
                foreach (KeyValuePair<string, Control> field in fields)
                {
                    CheckBox check = field.Value as CheckBox;
                    NumericUpDown number = field.Value as NumericUpDown;
                    draft.Values[field.Key] = check != null ? (object)check.Checked : number != null ? (object)number.Value : field.Value.Text;
                }
                drafts[currentKey] = draft;
            }
            MqttProfile selected = profileSelector.SelectedItem as MqttProfile;
            MqttProfile profile = selected ?? MqttProfile.CreateNew();
            currentKey = selected == null ? "new" : selected.Id;
            Draft existing;
            drafts.TryGetValue(currentKey, out existing);
            loading = true;
            foreach (KeyValuePair<string, Control> field in fields)
            {
                object value = existing == null ? typeof(MqttProfile).GetProperty(field.Key).GetValue(profile, null) : existing.Values[field.Key];
                CheckBox check = field.Value as CheckBox;
                NumericUpDown number = field.Value as NumericUpDown;
                if (check != null) check.Checked = Convert.ToBoolean(value);
                else if (number != null) number.Value = Math.Max(number.Minimum, Math.Min(number.Maximum, Convert.ToDecimal(value)));
                else field.Value.Text = Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            userProperties.Rows.Clear();
            foreach (MqttUserProperty property in existing == null ? profile.UserProperties : existing.Properties)
                userProperties.Rows.Add(property.Key, property.Value);
            loading = false;
            scrollPanel.AutoScrollPosition = Point.Empty;
            if (!loadFailed) feedback.Text = existing != null ? "Bu profildeki form taslağı korundu. Kaydet ile saklayabilirsiniz."
                : selected == null ? "Yeni bağlantı için Name ve Host alanlarını doldurun." : "Kayıtlı bağlantı yüklendi. Tüm alanları düzenleyebilirsiniz.";
            SetSessionActive(sessionActive);
            Action handler = ProfileChanged;
            if (handler != null) handler();
        }

        private MqttProfile ReadProfile()
        {
            MqttProfile selected = profileSelector.SelectedItem as MqttProfile;
            MqttProfile profile = selected == null ? MqttProfile.CreateNew() : selected.Copy();
            if (selected == null) profile.Subscriptions = new List<string>(newSubscriptions);
            foreach (KeyValuePair<string, Control> field in fields)
            {
                PropertyInfo property = typeof(MqttProfile).GetProperty(field.Key);
                CheckBox check = field.Value as CheckBox;
                NumericUpDown number = field.Value as NumericUpDown;
                object value;
                if (check != null) value = check.Checked;
                else if (number != null) value = Convert.ChangeType(number.Value, property.PropertyType, CultureInfo.InvariantCulture);
                else if (Nullable.GetUnderlyingType(property.PropertyType) != null)
                {
                    string text = field.Value.Text.Trim();
                    long parsed;
                    long minimum = field.Key == "TopicAliasMaximum" ? 0 : 1;
                    long maximum = field.Key == "MaximumPacketSize" ? uint.MaxValue : 65535;
                    if (text.Length == 0) value = null;
                    else if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) || parsed < minimum || parsed > maximum)
                        throw new ArgumentException(field.Value.AccessibleName + ": " + minimum + "–" + maximum + " aralığında tam sayı girin veya boş bırakın.");
                    else value = Convert.ChangeType(parsed, Nullable.GetUnderlyingType(property.PropertyType), CultureInfo.InvariantCulture);
                }
                else value = field.Value.Text;
                property.SetValue(profile, value, null);
            }
            profile.Name = profile.Name.Trim();
            profile.Host = profile.Host.Trim();
            if (profile.Name.Length == 0) { fields["Name"].Focus(); throw new ArgumentException("Name alanı zorunludur."); }
            if (profile.Host.Length == 0) { fields["Host"].Focus(); throw new ArgumentException("Host alanı zorunludur."); }
            foreach (MqttProfile saved in profiles)
                if (saved.Id != profile.Id && string.Equals(saved.Name, profile.Name, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Bu adla bir bağlantı zaten var. Farklı bir Name girin.");
            profile.UserProperties = ReadUserProperties();
            if (!string.IsNullOrEmpty(profile.WillTopic) && (profile.WillTopic.Contains("#") || profile.WillTopic.Contains("+")))
                throw new ArgumentException("Last-Will topic, + veya # joker karakterlerini içeremez.");
            return profile;
        }

        private void SaveProfile()
        {
            if (loadFailed) return;
            try
            {
                MqttProfile profile = ReadProfile();
                PersistProfile(profile);
            }
            catch (ArgumentException exception) { feedback.Text = exception.Message; }
            catch (Exception) { feedback.Text = "Bağlantı kaydedilemedi. Kayıt dosyasının yazılabilir olduğunu kontrol edin."; }
        }
        private void PersistProfile(MqttProfile profile)
        {
            List<MqttProfile> updated = new List<MqttProfile>(profiles);
            int index = updated.FindIndex(delegate(MqttProfile item) { return item.Id == profile.Id; });
            if (index < 0) updated.Add(profile); else updated[index] = profile;
            MqttProfileStore.Save(storePath, updated);
            profiles = updated;
            if (currentKey == "new") newSubscriptions.Clear();
            drafts.Remove(currentKey);
            RefreshProfiles(profile.Id);
            feedback.Text = "Bağlantı kaydedildi: " + profile.Name;
        }

        private void DeleteProfile()
        {
            MqttProfile selected = profileSelector.SelectedItem as MqttProfile;
            if (selected == null || loadFailed || sessionActive) return;
            try
            {
                List<MqttProfile> updated = new List<MqttProfile>(profiles);
                int index = updated.FindIndex(delegate(MqttProfile item) { return item.Id == selected.Id; });
                updated.RemoveAt(index);
                MqttProfileStore.Save(storePath, updated);
                profiles = updated;
                drafts.Remove(selected.Id);
                RefreshProfiles(updated.Count == 0 ? null : updated[Math.Min(index, updated.Count - 1)].Id);
                feedback.Text = "Bağlantı silindi: " + selected.Name;
            }
            catch (Exception) { feedback.Text = "Bağlantı silinemedi. Kayıt dosyasının yazılabilir olduğunu kontrol edin."; }
        }

        public void ApplyTheme(bool dark)
        {
            Color surface = dark ? Color.FromArgb(48, 48, 48) : Color.FromArgb(240, 240, 240);
            Color input = dark ? Color.FromArgb(63, 63, 63) : Color.White;
            Color text = dark ? Color.FromArgb(224, 224, 224) : Color.FromArgb(40, 40, 40);
            ThemeChildren(this, surface, input, text);
            saveButton.BackColor = Color.FromArgb(32, 111, 214);
            saveButton.ForeColor = Color.White;
            userProperties.EnableHeadersVisualStyles = false;
            userProperties.BackgroundColor = input;
            userProperties.GridColor = dark ? Color.FromArgb(104, 104, 104) : Color.FromArgb(200, 200, 200);
            userProperties.DefaultCellStyle.BackColor = input;
            userProperties.DefaultCellStyle.ForeColor = text;
            userProperties.DefaultCellStyle.SelectionBackColor = Color.FromArgb(32, 111, 214);
            userProperties.DefaultCellStyle.SelectionForeColor = Color.White;
            userProperties.ColumnHeadersDefaultCellStyle.BackColor = surface;
            userProperties.ColumnHeadersDefaultCellStyle.ForeColor = text;
            userProperties.RowHeadersDefaultCellStyle.BackColor = surface;
            userProperties.RowHeadersDefaultCellStyle.ForeColor = text;
        }

        private static void ThemeChildren(Control control, Color surface, Color input, Color text)
        {
            control.BackColor = control is TextBox || control is ComboBox || control is NumericUpDown ? input : surface;
            control.ForeColor = text;
            foreach (Control child in control.Controls) ThemeChildren(child, surface, input, text);
        }
    }
}
