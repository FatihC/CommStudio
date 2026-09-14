using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CommStudio
{
    internal enum MqttPayloadFormat { PlainText, Json, Hex }

    internal sealed class MqttLogEntry
    {
        private readonly byte[] payload;
        private List<MqttFilterField> filterFields;
        public IList<MqttFilterField> FilterFields { get { return filterFields ?? (filterFields = MqttFilter.ReadFields(RawText)); } }
        public readonly string Direction;
        public readonly string Topic;
        public readonly DateTime Timestamp;
        public readonly string Id;
        public string PayloadBase64 { get { return Convert.ToBase64String(payload); } }
        public readonly int Qos;
        public readonly bool Retain;
        public MqttPayloadFormat Format;
        public bool IsMessage { get { return Direction == "RX" || Direction == "TX"; } }
        public string RawText { get { return Encoding.UTF8.GetString(payload); } }
        public int ByteCount { get { return payload.Length; } }
        public string DisplayText { get; private set; }
        public string Notice { get; private set; }
        public string Metadata { get { return Timestamp.ToString("HH:mm:ss.fff") + "   ·   " + ByteCount + " B   ·   QoS " + Qos + (Retain ? "   ·   Retain" : ""); } }
        internal Rectangle Bounds, PayloadBounds, TopicBounds, ScrollBounds;
        internal int PayloadWidth, HorizontalOffset;
        internal int SelectionStart, SelectionLength;
        internal string SelectionText = "";

        public MqttLogEntry(string direction, string topic, byte[] bytes, int qos, bool retain)
            : this(direction, topic, bytes, qos, retain, DateTime.Now, Guid.NewGuid().ToString("N")) { }
        internal MqttLogEntry(string direction, string topic, byte[] bytes, int qos, bool retain, DateTime timestamp, string id)
        {
            Timestamp = timestamp; Id = id;
            Direction = direction; Topic = topic ?? ""; Qos = qos; Retain = retain;
            payload = bytes == null ? new byte[0] : (byte[])bytes.Clone();
        }

        public void SetFormat(MqttPayloadFormat format)
        {
            string previousText = DisplayText;
            Format = format;
            Notice = "";
            if (IsMessage && format == MqttPayloadFormat.Hex)
                DisplayText = BitConverter.ToString(payload).Replace('-', ' ');
            else
            {
                string text = RawText;
                if (IsMessage && format == MqttPayloadFormat.Json)
                {
                    try
                    {
                        text = new UTF8Encoding(false, true).GetString(payload);
                        if (JsonSyntaxValidator.Validate(text) == null) text = JsonTextFormatter.Beautify(text);
                        else Notice = "Geçerli JSON değil · Plain Text gösteriliyor";
                    }
                    catch (DecoderFallbackException) { Notice = "UTF-8 metin değil · Baytları Hex ile inceleyin"; }
                }
                // Native text rendering must not hide content after an embedded NUL.
                StringBuilder visible = new StringBuilder();
                foreach (char c in text)
                    if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t') visible.Append("\\x" + ((int)c).ToString("X2"));
                    else visible.Append(c);
                DisplayText = visible.ToString();
            }
            if (previousText != DisplayText) { SelectionStart = SelectionLength = 0; SelectionText = ""; }
        }

    }

    // A single scrolling surface keeps long sessions from creating a native window for each message.
    internal sealed class MqttLogControl : ScrollableControl
    {
        private readonly List<MqttLogEntry> entries = new List<MqttLogEntry>();
        private readonly List<MqttLogEntry> visibleEntries = new List<MqttLogEntry>();
        private string filter = "";
        private Func<MqttLogEntry, bool> matches = delegate { return true; };
        public Func<string, MqttLogEntry, bool> SaveChange;
        private readonly Font payloadFont = new Font("Consolas", 9.5F);
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem copySelection, copyPayload, copyTopic, delete, formats;
        private readonly Dictionary<MqttLogEntry, HScrollBar> scrollBars = new Dictionary<MqttLogEntry, HScrollBar>();
        private sealed class PayloadView : Panel
        {
            public readonly MqttPayloadTextBox Editor;
            public PayloadView(Action<int> scroll) { Editor = new MqttPayloadTextBox(scroll); Controls.Add(Editor); }
        }
        private readonly Dictionary<MqttLogEntry, PayloadView> payloadViews = new Dictionary<MqttLogEntry, PayloadView>();
        private bool syncingPayloadViews;
        private MqttLogEntry selected;
        private bool dark, layingOut, syncingScrollBars, wordWrap;
        private MqttPayloadFormat format;
        private const TextFormatFlags Wrapped = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.ExpandTabs;
        public IList<MqttLogEntry> Entries { get { return entries.AsReadOnly(); } }
        public IList<MqttLogEntry> VisibleEntries { get { return visibleEntries.AsReadOnly(); } }
        public MqttPayloadFormat DisplayFormat { get { return format; } }
        public bool WordWrap { get { return wordWrap; } set { wordWrap = value; ArrangeEntries(false); } }

        public MqttLogControl()
        {
            Dock = DockStyle.Fill; AutoScroll = true; TabStop = true;
            AccessibleName = "MQTT gelen ve giden mesajlar";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            copySelection = new ToolStripMenuItem("Copy Selection", null, delegate { if (selected != null) Copy(selected.SelectionText); });
            copyPayload = new ToolStripMenuItem("Copy Payload", null, delegate { if (selected != null) Copy(selected.IsMessage && selected.Format == MqttPayloadFormat.Hex ? selected.DisplayText : selected.RawText); });
            copyTopic = new ToolStripMenuItem("Copy Topic", null, delegate { if (selected != null) Copy(selected.Topic); });
            delete = new ToolStripMenuItem("Delete", null, delegate { DeleteEntry(selected); });
            formats = new ToolStripMenuItem("Biçim");
            foreach (MqttPayloadFormat choice in Enum.GetValues(typeof(MqttPayloadFormat)))
            {
                MqttPayloadFormat value = choice;
                ToolStripMenuItem item = new ToolStripMenuItem(FormatName(value), null, delegate { SetEntryFormat(selected, value); });
                item.Tag = value; formats.DropDownItems.Add(item);
            }
            menu.Items.AddRange(new ToolStripItem[] { copySelection, copyPayload, copyTopic, delete, new ToolStripSeparator(), formats });
            menu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                e.Cancel = selected == null;
                copySelection.Enabled = selected != null && selected.SelectionText.Length > 0;
                copyTopic.Enabled = selected != null && selected.IsMessage;
                formats.Enabled = selected != null && selected.IsMessage;
                foreach (ToolStripMenuItem item in formats.DropDownItems)
                    item.Checked = selected != null && selected.Format == (MqttPayloadFormat)item.Tag;
            };
            ApplyTheme(false);
        }

        public static string FormatName(MqttPayloadFormat value)
        { return value == MqttPayloadFormat.PlainText ? "Plain Text" : value == MqttPayloadFormat.Json ? "JSON" : "Hex"; }
        public void AddEntry(string direction, string topic, byte[] payload, int qos, bool retain)
        { AddEntry(new MqttLogEntry(direction, topic, payload, qos, retain)); }
        public void AddEntry(MqttLogEntry entry)
        { AddEntry(entry, true); }
        public void AddEntry(MqttLogEntry entry, bool persist)
        {
            bool follow = entries.Count == 0 || -AutoScrollPosition.Y + ClientSize.Height >= AutoScrollMinSize.Height - 24;
            if (persist && SaveChange != null) SaveChange("add", entry);
            entry.SetFormat(format); entries.Add(entry); ArrangeEntries(follow);
        }
        public void LoadEntries(IEnumerable<MqttLogEntry> restored)
        {
            selected = null; entries.Clear();
            foreach (MqttLogEntry entry in restored) { entry.SetFormat(format); entries.Add(entry); }
            ArrangeEntries(true);
        }
        public void SetFilter(string value)
        {
            string query = (value ?? "").Trim();
            Func<MqttLogEntry, bool> compiled = MqttFilter.Compile(query);
            filter = query; matches = compiled; selected = null; ArrangeEntries(false); ScrollTo(0);
        }
        private bool MatchesFilter(MqttLogEntry entry)
        { return matches(entry); }
        public void SetDisplayFormat(MqttPayloadFormat value)
        {
            format = value;
            foreach (MqttLogEntry entry in entries) entry.SetFormat(value);
            ArrangeEntries(false);
        }
        public void SetEntryFormat(MqttLogEntry entry, MqttPayloadFormat value)
        { if (entry == null || !entries.Contains(entry) || !entry.IsMessage) return; entry.SetFormat(value); ArrangeEntries(false); }
        public void DeleteEntry(MqttLogEntry entry)
        { if (entry == null || !entries.Contains(entry) || (SaveChange != null && !SaveChange("delete", entry))) return; entries.Remove(entry); if (selected == entry) selected = null; ArrangeEntries(false); }
        public void Clear() { if (SaveChange != null && !SaveChange("clear", null)) return; selected = null; entries.Clear(); ArrangeEntries(false); }
        public void ApplyTheme(bool value)
        {
            dark = value;
            BackColor = dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(248, 249, 251);
            ForeColor = dark ? Color.FromArgb(228, 231, 235) : Color.FromArgb(45, 58, 72);
            menu.BackColor = dark ? Color.FromArgb(63, 63, 63) : Color.White;
            menu.ForeColor = ForeColor;
            SyncPayloadViews();
            Invalidate();
        }
        private void Copy(string text)
        {
            try { if (text.Length == 0) Clipboard.Clear(); else Clipboard.SetText(text); }
            catch (ExternalException) { MessageBox.Show(this, "Pano şu anda kullanılıyor. Tekrar deneyin.", "Copy", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); ArrangeEntries(false); }
        private void ArrangeEntries(bool follow)
        {
            if (layingOut || entries == null) return;
            layingOut = true;
            try
            {
                int scroll = -AutoScrollPosition.Y;
                follow = follow || (AutoScrollMinSize.Height > 0 && scroll + ClientSize.Height >= AutoScrollMinSize.Height - 24);
                int viewportWidth = Math.Max(120, Width - SystemInformation.VerticalScrollBarWidth);
                int width = viewportWidth - 24;
                int maximumCardWidth = Math.Max(60, (int)(viewportWidth * .70));
                int y = 12;
                visibleEntries.Clear();
                foreach (MqttLogEntry entry in entries)
                {
                    if (!MatchesFilter(entry)) { entry.Bounds = Rectangle.Empty; entry.ScrollBounds = Rectangle.Empty; continue; }
                    visibleEntries.Add(entry);
                    string payloadText = entry.DisplayText.Length == 0 ? "(boş payload)" : entry.DisplayText;
                    TextFormatFlags unwrapped = TextFormatFlags.NoPrefix | TextFormatFlags.ExpandTabs;
                    Size naturalContent = Measure(payloadText, entry.IsMessage ? payloadFont : Font, 1000000, unwrapped);
                    int w = width;
                    if (entry.IsMessage)
                    {
                        int naturalWidth = naturalContent.Width;
                        foreach (string text in new[] { entry.Topic, entry.Metadata, entry.Notice })
                            naturalWidth = Math.Max(naturalWidth, Measure(text, Font, 1000000, unwrapped).Width);
                        w = Math.Min(maximumCardWidth, naturalWidth + 24);
                    }
                    int topicHeight = entry.IsMessage ? Measure(entry.Topic, Font, w - 24, Wrapped).Height : 0;
                    Size content = wordWrap ? Measure(payloadText, entry.IsMessage ? payloadFont : Font, w - 24, Wrapped) : naturalContent;
                    int x = entry.Direction == "TX" ? 12 + Math.Max(0, width - w) : 12;
                    if (entry.IsMessage)
                    {
                        entry.TopicBounds = new Rectangle(x + 12, y + 12, w - 24, topicHeight);
                        int payloadY = entry.TopicBounds.Bottom + 10;
                        entry.PayloadBounds = new Rectangle(x + 12, payloadY, w - 24, content.Height);
                        entry.PayloadWidth = content.Width;
                        entry.HorizontalOffset = wordWrap ? 0 : Math.Min(entry.HorizontalOffset, Math.Max(0, content.Width - entry.PayloadBounds.Width));
                        int scrollHeight = !wordWrap && content.Width > entry.PayloadBounds.Width ? SystemInformation.HorizontalScrollBarHeight + 6 : 0;
                        entry.ScrollBounds = scrollHeight == 0 ? Rectangle.Empty : new Rectangle(x + 12, entry.PayloadBounds.Bottom + 6, w - 24, SystemInformation.HorizontalScrollBarHeight);
                        int noteHeight = entry.Notice.Length == 0 ? 0 : Measure(entry.Notice, Font, w - 24, Wrapped).Height + 6;
                        entry.Bounds = new Rectangle(x, y, w, payloadY - y + content.Height + scrollHeight + Measure(entry.Metadata, Font, w - 24, Wrapped).Height + 24 + noteHeight);
                    }
                    else
                    {
                        string notice = entry.Timestamp.ToString("HH:mm:ss.fff") + "  ·  " + entry.Direction + "  " + entry.Topic + "  " + entry.DisplayText;
                        entry.Bounds = new Rectangle(x, y, w, Measure(notice, Font, w - 20, Wrapped).Height + 16);
                    }
                    y = entry.Bounds.Bottom + (entry.IsMessage ? 14 : 5);
                }
                AutoScrollMinSize = new Size(0, visibleEntries.Count == 0 ? 0 : y);
                if (selected != null && !visibleEntries.Contains(selected)) selected = null;
                AutoScrollPosition = new Point(0, follow ? Math.Max(0, y - ClientSize.Height) : scroll);
            }
            finally { layingOut = false; }
            SyncScrollBars();
            Invalidate();
        }
        private static Size Measure(string text, Font font, int width, TextFormatFlags flags)
        { return TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue), flags); }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); SyncScrollBars(); Invalidate(); }

        private void SyncScrollBars()
        {
            if (layingOut || syncingScrollBars || scrollBars == null || IsDisposed) return;
            syncingScrollBars = true;
            try
            {
                // Keep native scrollbars only for visible cards; offsets belong to the
                // entries and survive scrolling offscreen, resizing and view changes.
                HashSet<MqttLogEntry> visible = new HashSet<MqttLogEntry>();
                foreach (MqttLogEntry entry in new List<MqttLogEntry>(visibleEntries))
                {
                    if (entry.ScrollBounds.IsEmpty) continue;
                    Rectangle bounds = Offset(entry.ScrollBounds);
                    if (!bounds.IntersectsWith(ClientRectangle)) continue;
                    visible.Add(entry);
                    HScrollBar bar;
                    if (!scrollBars.TryGetValue(entry, out bar))
                    {
                        MqttLogEntry target = entry;
                        bar = new HScrollBar { TabStop = true, AccessibleName = "Mesajı yatay kaydır: " + entry.Topic };
                        bar.ValueChanged += delegate(object sender, EventArgs e)
                        {
                            target.HorizontalOffset = ((HScrollBar)sender).Value;
                            if (!syncingScrollBars) SyncPayloadViews();
                            Invalidate(Offset(target.PayloadBounds));
                        };
                        scrollBars.Add(entry, bar);
                        Controls.Add(bar);
                    }
                    int offset = entry.HorizontalOffset;
                    bar.Bounds = bounds;
                    bar.Minimum = 0;
                    bar.Maximum = Math.Max(0, entry.PayloadWidth - 1);
                    bar.LargeChange = entry.PayloadBounds.Width;
                    bar.SmallChange = 32;
                    bar.Value = Math.Min(offset, Math.Max(0, bar.Maximum - bar.LargeChange + 1));
                }
                foreach (MqttLogEntry entry in new List<MqttLogEntry>(scrollBars.Keys))
                    if (!visible.Contains(entry))
                    { HScrollBar bar = scrollBars[entry]; scrollBars.Remove(entry); Controls.Remove(bar); bar.Dispose(); }
            }
            finally { syncingScrollBars = false; }
            SyncPayloadViews();
        }

        private Color CardColor(MqttLogEntry entry)
        {
            return dark ? (entry.Direction == "TX" ? Color.FromArgb(48, 67, 62) : Color.FromArgb(48, 60, 76)) :
                (entry.Direction == "TX" ? Color.FromArgb(233, 246, 239) : Color.FromArgb(235, 243, 253));
        }
        private void SyncPayloadViews()
        {
            if (layingOut || syncingPayloadViews || payloadViews == null || IsDisposed || Disposing) return;
            syncingPayloadViews = true;
            try
            {
                HashSet<MqttLogEntry> visible = new HashSet<MqttLogEntry>();
                foreach (MqttLogEntry entry in new List<MqttLogEntry>(visibleEntries))
                {
                    if (!entry.IsMessage) continue;
                    Rectangle payload = Offset(entry.PayloadBounds);
                    Rectangle viewport = Rectangle.Intersect(payload, ClientRectangle);
                    if (viewport.Width <= 0 || viewport.Height <= 0) continue;
                    visible.Add(entry);
                    PayloadView view;
                    if (!payloadViews.TryGetValue(entry, out view))
                    {
                        MqttLogEntry target = entry;
                        view = new PayloadView(delegate(int delta)
                        {
                            int lines = SystemInformation.MouseWheelScrollLines;
                            ScrollTo(-AutoScrollPosition.Y - delta / 120 * (lines < 0 ? ClientSize.Height : Math.Max(1, lines) * Font.Height));
                        });
                        MqttPayloadTextBox editor = view.Editor;
                        editor.Font = payloadFont; editor.ContextMenuStrip = menu;
                        editor.AccessibleName = "MQTT mesaj içeriği: " + entry.Topic;
                        editor.MouseDown += delegate { selected = target; Invalidate(); };
                        editor.Enter += delegate { selected = target; Invalidate(); };
                        editor.SelectionChanged += delegate
                        {
                            if (syncingPayloadViews) return;
                            target.SelectionStart = editor.SelectionStart; target.SelectionLength = editor.SelectionLength; target.SelectionText = editor.SelectedText;
                        };
                        editor.HScroll += delegate
                        {
                            if (syncingPayloadViews) return;
                            target.HorizontalOffset = Math.Min(editor.HorizontalPosition, Math.Max(0, target.PayloadWidth - target.PayloadBounds.Width));
                            HScrollBar bar;
                            if (scrollBars.TryGetValue(target, out bar)) bar.Value = target.HorizontalOffset;
                        };
                        view.Bounds = viewport;
                        payloadViews.Add(entry, view); Controls.Add(view);
                    }
                    MqttPayloadTextBox text = view.Editor;
                    view.Bounds = viewport;
                    view.BackColor = text.BackColor = CardColor(entry); text.ForeColor = ForeColor;
                    string content = entry.DisplayText.Length == 0 ? "(boş payload)" : entry.DisplayText;
                    if (text.Text.Replace("\r\n", "\n") != content.Replace("\r\n", "\n")) text.Text = content;
                    text.WordWrap = wordWrap;
                    text.Bounds = new Rectangle(payload.X - viewport.X, payload.Y - viewport.Y, payload.Width, payload.Height + 4);
                    int start = Math.Min(entry.SelectionStart, text.TextLength), length = Math.Min(entry.SelectionLength, Math.Max(0, text.TextLength - start));
                    if (text.SelectionStart != start || text.SelectionLength != length) text.Select(start, length);
                    if (text.HorizontalPosition != entry.HorizontalOffset) text.HorizontalPosition = entry.HorizontalOffset;
                }
                foreach (MqttLogEntry entry in new List<MqttLogEntry>(payloadViews.Keys))
                    if (!visible.Contains(entry)) { PayloadView view = payloadViews[entry]; payloadViews.Remove(entry); Controls.Remove(view); view.Dispose(); }
            }
            finally { syncingPayloadViews = false; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            SyncScrollBars();
            if (visibleEntries.Count == 0)
            {
                TextRenderer.DrawText(e.Graphics, filter.Length == 0 ? "MQTT mesajları burada görünecek" : "Filtreye uyan mesaj bulunamadı", Font, ClientRectangle,
                    dark ? Color.Silver : Color.SlateGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }
            // TextRenderer uses device coordinates, so offset rectangles explicitly.
            foreach (MqttLogEntry entry in visibleEntries)
            {
                Rectangle bounds = Offset(entry.Bounds);
                if (!bounds.IntersectsWith(e.ClipRectangle)) continue;
                Color muted = dark ? Color.FromArgb(176, 186, 196) : Color.FromArgb(106, 121, 137);
                if (!entry.IsMessage)
                {
                    string text = entry.Timestamp.ToString("HH:mm:ss.fff") + "  ·  " + entry.Direction + "  " + entry.Topic + "  " + entry.DisplayText;
                    TextRenderer.DrawText(e.Graphics, text, Font, Rectangle.Inflate(bounds, -10, -8),
                        entry.Direction == "ERR" ? (dark ? Color.LightSalmon : Color.FromArgb(166, 90, 64)) : muted, Wrapped);
                    continue;
                }
                bool outgoing = entry.Direction == "TX";
                Color fill = CardColor(entry);
                Color accent = outgoing ? Color.FromArgb(126, 184, 154) : Color.FromArgb(138, 175, 217);
                using (GraphicsPath path = Rounded(bounds, 9))
                using (SolidBrush brush = new SolidBrush(fill))
                using (Pen border = new Pen(entry == selected ? accent : BackColor))
                { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(border, path); }
                TextRenderer.DrawText(e.Graphics, entry.Topic, Font, Offset(entry.TopicBounds), ForeColor, Wrapped);
                Rectangle payloadViewport = Offset(entry.PayloadBounds);
                int footerY = (entry.ScrollBounds.IsEmpty ? payloadViewport.Bottom : Offset(entry.ScrollBounds).Bottom) + 10;
                int footerHeight = Measure(entry.Metadata, Font, bounds.Width - 24, Wrapped).Height;
                TextRenderer.DrawText(e.Graphics, entry.Metadata, Font,
                    new Rectangle(bounds.X + 12, footerY, bounds.Width - 24, footerHeight), muted, Wrapped);
                if (entry.Notice.Length != 0)
                    TextRenderer.DrawText(e.Graphics, entry.Notice, Font, new Rectangle(bounds.X + 12, footerY + footerHeight + 6, bounds.Width - 24, bounds.Bottom - footerY - footerHeight - 6), muted, Wrapped);
            }
        }
        private Rectangle Offset(Rectangle bounds) { bounds.Offset(AutoScrollPosition); return bounds; }
        private static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath(); int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus(); selected = null;
            Point location = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
            foreach (MqttLogEntry entry in visibleEntries) if (entry.Bounds.Contains(location)) { selected = entry; break; }
            Invalidate();
            if (e.Button == MouseButtons.Right && selected != null) menu.Show(this, e.Location);
        }
        protected override bool IsInputKey(Keys keyData)
        { Keys key = keyData & Keys.KeyCode; return key == Keys.Up || key == Keys.Down || IsNavigationKey(key) || base.IsInputKey(keyData); }
        private static bool IsNavigationKey(Keys key)
        { return key == Keys.PageUp || key == Keys.PageDown || key == Keys.Home || key == Keys.End; }
        private void ScrollTo(int position)
        {
            AutoScrollPosition = new Point(0, Math.Max(0, Math.Min(position, AutoScrollMinSize.Height - ClientSize.Height)));
            SyncScrollBars(); Invalidate();
        }
        private void Navigate(Keys key)
        {
            int position = -AutoScrollPosition.Y;
            int page = Math.Max(1, ClientSize.Height - 24);
            ScrollTo(key == Keys.Home ? 0 : key == Keys.End ? AutoScrollMinSize.Height : position + (key == Keys.PageDown ? page : -page));
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (IsNavigationKey(key) && (keyData & (Keys.Alt | Keys.Shift)) == 0) { Navigate(key); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (IsNavigationKey(e.KeyCode)) { Navigate(e.KeyCode); e.Handled = e.SuppressKeyPress = true; return; }
            if ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) && visibleEntries.Count > 0)
            {
                int index = selected == null ? -1 : visibleEntries.IndexOf(selected);
                selected = visibleEntries[Math.Max(0, Math.Min(visibleEntries.Count - 1, index + (e.KeyCode == Keys.Down ? 1 : -1)))];
                AutoScrollPosition = new Point(0, selected.Bounds.Top); SyncScrollBars(); Invalidate(); e.Handled = true;
            }
            if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            { if (selected != null) menu.Show(this, Offset(selected.Bounds).Location); e.Handled = true; }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { menu.Dispose(); payloadFont.Dispose(); } base.Dispose(disposing); }
    }
}
