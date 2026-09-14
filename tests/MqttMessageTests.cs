using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using MQTTnet;
using MQTTnet.Server;
using CommStudio;

internal static class MqttMessageTests
{
    [STAThread]
    private static int Main()
    {
        string path = Path.GetFullPath("tests\\bin\\mqtt-message-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            CheckPublishWordSelection();
            CheckQueries();
            CheckHistoryStore(path + ".journal-test");
            CheckLog();
            using (Form host = new Form { Size = new Size(1000, 720), ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) })
            using (MqttWorkspaceControl workspace = new MqttWorkspaceControl(new MqttProfilesControl(path + ".profiles"), path))
            {
                host.Controls.Add(workspace); host.Show(); Application.DoEvents();
                TextBox topic = (TextBox)Field(workspace, "publishTopic");
                TextBox message = (TextBox)Field(workspace, "publishMessage");
                ComboBox selector = (ComboBox)Field(workspace, "savedMessages");
                topic.Text = "test/topic"; message.Text = "Türkçe\r\n{\"value\":1}";
                Invoke(workspace, "SaveNewMessage", "First message");
                List<SavedMqttMessage> saved = SavedMqttMessageStore.Load(path);
                Assert(saved.Count == 1 && saved[0].Name == "First message" && saved[0].CreatedAtUtc.Year == DateTime.UtcNow.Year,
                    "save records name and timestamp");
                Assert(saved[0].Topic == topic.Text && saved[0].Message == message.Text, "topic and exact multiline text persist");
                string id = saved[0].Id; DateTime created = saved[0].CreatedAtUtc;
                selector.SelectedIndex = 0;
                Assert(message.Text == "", "new message clears the editor");
                selector.SelectedIndex = 1;
                Assert(topic.Text == "test/topic" && message.Text == saved[0].Message, "selecting a saved message fills publish fields");
                message.Text = "Updated"; topic.Text = "test/updated";
                Invoke(workspace, "UpdateSavedMessage");
                saved = SavedMqttMessageStore.Load(path);
                Assert(saved.Count == 1 && saved[0].Id == id && saved[0].CreatedAtUtc == created &&
                    saved[0].UpdatedAtUtc >= created && saved[0].Message == "Updated", "update preserves identity and creation date");
                Invoke(workspace, "SaveNewMessage", "Second message");
                Assert(SavedMqttMessageStore.Load(path).Count == 2, "save creates a separate reusable message");
                selector.SelectedIndex = 1;
                Invoke(workspace, "DeleteSavedMessage");
                saved = SavedMqttMessageStore.Load(path);
                Assert(saved.Count == 1 && saved[0].Name == "Second message", "delete removes only the selected record");
                Assert(message.Text == "Updated", "deleting a record preserves editor text");
                CheckJson();
                CheckPublishFormatting(workspace);
                CheckConnectionWorkspace(host, workspace, path);
                host.Size = new Size(760, 480); Application.DoEvents();
                Assert(message.RectangleToScreen(message.ClientRectangle).Bottom <= host.RectangleToScreen(host.ClientRectangle).Bottom,
                    "publish message remains on screen in a small window");
                Button beautify = (Button)Field(workspace, "beautifyButton");
                Assert(beautify.Height >= 24 && beautify.RectangleToScreen(beautify.ClientRectangle).Bottom <= host.RectangleToScreen(host.ClientRectangle).Bottom,
                    "formatting buttons remain readable and visible in a small window");
                host.Hide();
            }
            using (MqttWorkspaceControl reopened = new MqttWorkspaceControl(new MqttProfilesControl(path + ".profiles"), path))
                Assert(((ComboBox)Field(reopened, "savedMessages")).Items.Count == 2, "saved message list survives reopening");
            Console.WriteLine("MQTT message tests passed: saved messages, log formats, raw payload preservation, context actions, themes, wrapping and compact layout.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally
        {
            if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".profiles")) File.Delete(path + ".profiles");
            foreach (string directory in new[] { path + ".history", path + ".journal-test" })
                if (Directory.Exists(directory)) { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        }
    }

    private static void CheckPublishFormatting(MqttWorkspaceControl workspace)
    {
        JsonMessageTextBox editor = (JsonMessageTextBox)Field(workspace, "publishMessage");
        Button compress = (Button)Field(workspace, "compressButton");
        Button beautify = (Button)Field(workspace, "beautifyButton");
        Assert(!workspace.IsConnected && compress.Enabled && beautify.Enabled, "JSON formatting works offline");
        string compact = @"{""text"":""hello  world \""quoted\"" \u0131 \\ end"",""n"":123456789012345678901234567890,""n"":1.00e+20,""bytes"":[0,14,-2,1.50],""nested"":[{""ok"":true},null,[]]}";
        editor.Text = compact;
        beautify.PerformClick();
        string pretty = editor.Text;
        Assert(pretty.Contains(Environment.NewLine + "  \"text\": ") && pretty.Contains("[0, 14, -2, 1.50]"),
            "Beautify indents objects and keeps numeric arrays together");
        Assert(JsonSyntaxValidator.Validate(pretty) == null, "Beautify produces valid JSON");
        Assert(editor.CanUndo, "formatting can be undone");
        editor.Undo();
        Assert(editor.Text == compact, "undo restores exact original JSON");
        editor.Text = pretty;
        compress.PerformClick();
        Assert(editor.Text == compact, "Compress preserves string whitespace, escapes, duplicate keys and exact numeric tokens");
        string large = "{\"data\":\"" + new string('a', 40000) + "\"}";
        editor.Text = large;
        beautify.PerformClick();
        compress.PerformClick();
        Assert(editor.Text == large, "formatting does not truncate payloads above the native default text limit");
        foreach (Button button in new[] { compress, beautify })
        {
            editor.Text = "{\"unfinished\": ";
            button.PerformClick();
            Assert(editor.Text == "{\"unfinished\": " && editor.ValidationError != null, "invalid JSON is marked and left untouched");
        }
        editor.Text = compact;
    }

    private static void CheckPublishWordSelection()
    {
        using (Form host = new Form { Size = new Size(1000, 300), ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) })
        using (JsonMessageTextBox editor = new JsonMessageTextBox { Multiline = true, Dock = DockStyle.Fill })
        {
            host.Controls.Add(editor); host.Show(); editor.Focus(); Application.DoEvents();
            string[,] cases = {
                { "{\"serialNumber\":\"869330078350791\",\"function\":\"heartbeat\"}", "869330078350791" },
                { "{\"serialNumber\":\"12345\"}", "serialNumber" },
                { "{\"function\":\"heartbeat\"}", "heartbeat" },
                { "one,two;three", "two" },
                { "abc-def", "def" },
                { "önce (ölçüm) sonra", "ölçüm" },
                { "first second third", "second" },
                { "{\"value\":12345}", "12345" },
                { "\"e\u0301cole\"", "e\u0301cole" }
            };
            for (int i = 0; i < cases.GetLength(0); i++)
            {
                editor.Text = cases[i, 0]; editor.Select(0, 0);
                int index = editor.Text.IndexOf(cases[i, 1], StringComparison.Ordinal) + 2;
                Point point = editor.GetPositionFromCharIndex(index);
                IntPtr coordinates = new IntPtr(((point.Y + 3) << 16) | (point.X + 1));
                Invoke(editor, "WndProc", Message.Create(editor.Handle, 0x0203, new IntPtr(1), coordinates));
                Invoke(editor, "WndProc", Message.Create(editor.Handle, 0x0202, IntPtr.Zero, coordinates));
                Application.DoEvents();
                Assert(editor.SelectedText == cases[i, 1], "Publish double-click selects only the word: " + cases[i, 1] + " (actual: " + editor.SelectedText + ")");
                Assert(editor.Text == cases[i, 0], "word selection preserves the original message");
            }
            editor.Text = "";
            Invoke(editor, "WndProc", Message.Create(editor.Handle, 0x0203, new IntPtr(1), IntPtr.Zero));
            Assert(editor.SelectionLength == 0, "double-click in an empty editor is harmless");
            host.Hide();
        }
    }

    private static void CheckHistoryStore(string directory)
    {
        MqttHistoryStore store = new MqttHistoryStore(directory, "profile-one");
        Assert(store.Load().Count == 0, "new connection starts with an empty history");
        DateTime timestamp = new DateTime(2026, 9, 9, 12, 34, 56, DateTimeKind.Local);
        MqttLogEntry entry = new MqttLogEntry("RX", "/Türkçe", new byte[] { 0, 255, 128, 65 }, 2, true, timestamp, "binary-one");
        store.Append("add", entry);
        store.Append("add", new MqttLogEntry("TX", "/request", Encoding.UTF8.GetBytes("abcd\r\nnext"), 0, false));
        MqttHistoryStore reopened = new MqttHistoryStore(directory, "profile-one");
        List<MqttLogEntry> restored = reopened.Load();
        Assert(restored.Count == 2 && restored[0].Id == entry.Id && restored[0].Timestamp == timestamp && restored[0].PayloadBase64 == entry.PayloadBase64 &&
            restored[0].Topic == entry.Topic && restored[0].Qos == 2 && restored[0].Retain, "journal restores timestamps, binary payloads and metadata exactly");
        Assert(new MqttHistoryStore(directory, "profile-two").Load().Count == 0, "connection histories are isolated");
        reopened.Append("delete", restored[0]);
        Assert(new MqttHistoryStore(directory, "profile-one").Load().Count == 1, "message deletion persists");
        reopened.Append("clear", null);
        Assert(new MqttHistoryStore(directory, "profile-one").Load().Count == 0, "clear persists across reopen");
        reopened.Append("add", entry);
        File.AppendAllText(reopened.FilePath, "{\"Operation\":\"add\"", new UTF8Encoding(false));
        MqttHistoryStore recovered = new MqttHistoryStore(directory, "profile-one");
        Assert(recovered.Load().Count == 1, "incomplete final append does not discard valid history");
        recovered.Append("add", new MqttLogEntry("RX", "/after-recovery", new byte[0], 0, false));
        Assert(new MqttHistoryStore(directory, "profile-one").Load().Count == 2, "recording continues after tail recovery");
    }

    private static void CheckQueries()
    {
        MqttLogEntry heartbeat = new MqttLogEntry("RX", "/response", Encoding.UTF8.GetBytes("{\"device\":{\"serialNumber\":\"12345\"},\"function\":\"heartbeat\",\"note\":\"hello world\"}"), 0, false);
        MqttLogEntry other = new MqttLogEntry("TX", "/request/abcde", Encoding.UTF8.GetBytes("{\"device\":{\"serialNumber\":123456},\"function\":\"identification\"}"), 0, false);
        foreach (MqttLogEntry entry in new[] { heartbeat, other }) entry.SetFormat(MqttPayloadFormat.Json);
        foreach (string query in new[] { "serialNumber:12345 AND function:heartbeat", "device.serialNumber=12345 function:heartbeat", "(abcde OR 12345) AND NOT function:identification", "\"hello world\"", "topic:/response", "direction:rx" })
        {
            Func<MqttLogEntry, bool> match = MqttFilter.Compile(query);
            Assert(match(heartbeat) && !match(other), "structured query: " + query);
        }
        Assert(MqttFilter.Compile("NOT serialNumber:12345")(other) && !MqttFilter.Compile("NOT serialNumber:12345")(heartbeat), "NOT excludes exact serial number only");
        Assert(MqttFilter.Compile("12345 OR abcde")(heartbeat) && MqttFilter.Compile("12345 OR abcde")(other), "OR matches either word");
        Assert(MqttFilter.Compile("abcde OR missing AND missing")(other), "AND has priority over OR");
        Assert(!MqttFilter.Compile("serialNumber:12345")(other), "field equality never matches a longer serial number");
        MqttLogEntry escaped = new MqttLogEntry("RX", "/json", Encoding.UTF8.GetBytes("{\"devices\":[{\"serialNumber\":\"123\\u0034\\u0035\"}],\"large\":123456789012345678901234567890}"), 0, false);
        escaped.SetFormat(MqttPayloadFormat.Hex);
        Assert(MqttFilter.Compile("devices.serialNumber:12345 AND large:123456789012345678901234567890")(escaped), "nested arrays, decoded strings and large numeric IDs match independently of display format");
        MqttLogEntry plain = new MqttLogEntry("RX", "/plain", Encoding.UTF8.GetBytes("hello world AND mqtt://broker"), 0, false);
        plain.SetFormat(MqttPayloadFormat.PlainText);
        Assert(MqttFilter.Compile("\"AND\" AND \"mqtt://broker\"")(plain), "quotes search literal operators and punctuation");
        Assert(!MqttFilter.Compile("serialNumber:12345")(plain) && MqttFilter.Compile("NOT serialNumber:12345")(plain), "NOT includes absent fields");
        foreach (string invalid in new[] { "AND", "12345 OR", "function:", "NOT", "(12345", "12345)", "\"unfinished", "()", "abc AND OR def" })
        {
            bool failed = false;
            try { MqttFilter.Compile(invalid); } catch (FormatException) { failed = true; }
            Assert(failed, "invalid query reports error: " + invalid);
        }
    }

    private static void CheckJson()
    {
        foreach (string valid in new[] { "{}", "[]", "null", "true", "false", "-0", "1.2e-30", "0e+1", "1e9999",
            " {\r\n\"Türkçe\": [true, null, {\"x\": \"\\u1234\\n\\t\\\\\\\"\"}]} ", "\"text\"" })
            Assert(JsonSyntaxValidator.Validate(valid) == null, "valid JSON: " + valid);
        foreach (string invalid in new[] { "", " ", "{", "{unquoted:1}", "{\"x\":1,}", "[1,]", "[1 2]", "01", "-", "1.",
            "+1", "NaN", "Infinity", "1e", "[true false]", "{}[]", "\"\\q\"", "\"\\u12x4\"", "\"a\nb\"", "\"unfinished", "\u00a0{}" })
            Assert(JsonSyntaxValidator.Validate(invalid) != null, "invalid JSON: " + invalid);
        JsonValidationError error = JsonSyntaxValidator.Validate("{\r\n  function: 1\r\n}");
        Assert(error.Offset == 5 && error.Length == 8 && error.Line == 2 && error.Column == 3,
            "diagnostic identifies the complete unquoted key with exact line/column");
    }

    private static void CheckLog()
    {
        using (Form host = new Form { Size = new Size(860, 760), ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) })
        using (MqttLogControl log = new MqttLogControl())
        {
            host.Controls.Add(log); host.Show(); Application.DoEvents();
            string original = "{\"device\":{\"flag\":\"FCS\",\"serialNumber\":\"1234567890\"},\"function\":\"heartbeat\",\"response\":{\"signal\":30,\"deviceDate\":\"2026-09-09 14:39:32\"}}";
            log.AddEntry("SYS", "", Encoding.UTF8.GetBytes("MQTT bağlantısı kuruldu."), 0, false);
            log.AddEntry("RX", "/response", Encoding.UTF8.GetBytes(original), 1, true);
            log.AddEntry("TX", "/request/FCS1234567890", Encoding.UTF8.GetBytes("{\"function\":\"identification\",\"request\":{\"parameters\":[\"heartbeatPeriod\"]}}"), 0, false);
            MqttLogEntry received = log.Entries[1], sent = log.Entries[2];
            log.AddEntry("RX", "/tiny", Encoding.UTF8.GetBytes("1"), 0, false);
            MqttLogEntry small = log.Entries[3];
            Assert(small.Bounds.Width < (int)((log.Width - SystemInformation.VerticalScrollBarWidth) * .70) && small.ScrollBounds.IsEmpty,
                "short payload uses a smaller card without a scrollbar");
            log.DeleteEntry(small);
            log.SetDisplayFormat(MqttPayloadFormat.Json);
            Assert(received.DisplayText.Contains(Environment.NewLine) && received.RawText == original, "JSON formatting preserves raw payload");
            log.SetEntryFormat(received, MqttPayloadFormat.Hex);
            Assert(sent.Format == MqttPayloadFormat.Json && log.DisplayFormat == MqttPayloadFormat.Json, "individual format changes only one message");
            log.SetDisplayFormat(MqttPayloadFormat.PlainText);
            Assert(received.Format == MqttPayloadFormat.PlainText && sent.DisplayText == sent.RawText, "global choice resets overrides");
            byte[] binary = { 0, 255, 128, 65, 13, 10 };
            log.AddEntry("RX", "/binary", binary, 0, false); binary[1] = 0;
            MqttLogEntry bytes = log.Entries[3];
            log.SetDisplayFormat(MqttPayloadFormat.Hex);
            Assert(bytes.DisplayText == "00 FF 80 41 0D 0A", "hex preserves exact original bytes and owns a copy");
            Assert(log.Entries[0].DisplayText == "MQTT bağlantısı kuruldu.", "system events do not become hex payloads");
            log.SetEntryFormat(bytes, MqttPayloadFormat.Json);
            Assert(bytes.Notice.Length > 0, "binary JSON fallback is explicit");
            log.SetDisplayFormat(MqttPayloadFormat.Json);
            log.AddEntry("RX", "/status", Encoding.UTF8.GetBytes("ready"), 0, false);
            Assert(log.Entries[4].Format == MqttPayloadFormat.Json && log.Entries[4].DisplayText == "ready" && log.Entries[4].Notice.Length > 0, "new messages inherit global format and invalid JSON remains visible");
            log.DeleteEntry(bytes); log.DeleteEntry(log.Entries[3]);
            Assert(log.Entries.Count == 3 && log.Entries[1] == received, "delete affects only chosen entry");
            // Exercise the actual context menu's local-format and delete actions.
            typeof(MqttLogControl).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(log,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, received.Bounds.X + 5, received.Bounds.Y + 5, 0) });
            ToolStripMenuItem formats = (ToolStripMenuItem)Field(log, "formats");
            ((ToolStripMenuItem)formats.DropDownItems[2]).PerformClick();
            Assert(received.Format == MqttPayloadFormat.Hex && sent.Format == MqttPayloadFormat.Json, "context submenu targets clicked card");
            log.SetDisplayFormat(MqttPayloadFormat.Json);
            foreach (bool dark in new[] { false, true })
            {
                log.ApplyTheme(dark); log.AutoScrollPosition = Point.Empty; Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(log.Width, log.Height))
                { log.DrawToBitmap(bitmap, log.ClientRectangle); bitmap.Save("tests\\bin\\mqtt-log-" + (dark ? "dark" : "light") + ".png"); }
            }
            host.Size = new Size(260, 400); Application.DoEvents();
            Assert(log.Entries.All(x => x.Bounds.Right <= log.Width), "cards fit narrow log panel");
            Assert(received.Bounds.Width == (int)((log.Width - SystemInformation.VerticalScrollBarWidth) * .70), "card width is seventy percent of log viewport");
            log.WordWrap = false;
            log.AddEntry("RX", "/long", Encoding.UTF8.GetBytes(new string('X', 1000)), 0, false);
            Assert(!log.Entries[3].ScrollBounds.IsEmpty && log.AutoScrollMinSize.Width <= log.Width, "long payload scrolls inside its card without widening the log");
            log.WordWrap = true;
            Assert(log.Entries[3].ScrollBounds.IsEmpty && log.AutoScrollMinSize.Width <= log.Width, "word wrap removes the card scrollbar");
            log.AutoScrollPosition = Point.Empty;
            log.AddEntry("RX", "/later", Encoding.UTF8.GetBytes("new message"), 0, false);
            Assert(log.AutoScrollPosition.Y == 0, "new messages do not interrupt reading older entries");
            log.AutoScrollPosition = new Point(0, log.AutoScrollMinSize.Height);
            log.SetDisplayFormat(MqttPayloadFormat.Hex);
            Assert(-log.AutoScrollPosition.Y + log.ClientSize.Height >= log.AutoScrollMinSize.Height - 24, "format changes preserve bottom following");
            ((ToolStripMenuItem)Field(log, "delete")).PerformClick();
            Assert(!log.Entries.Contains(received), "context delete removes selected card");
            log.Clear(); Assert(log.Entries.Count == 0 && log.AutoScrollMinSize.Height == 0, "clear removes all entries");
            foreach (string value in new[] { "{\"x\":1,\"x\":2,\"big\":123456789012345678901234567890}", "[{},[],\"escaped \\\" \\\\ text\",true,null]", "1e9999" })
            {
                MqttLogEntry entry = new MqttLogEntry("RX", "/json", Encoding.UTF8.GetBytes(value), 0, false);
                entry.SetFormat(MqttPayloadFormat.Json);
                Assert(JsonSyntaxValidator.Validate(entry.DisplayText) == null && entry.RawText == value, "JSON formatting preserves token semantics");
            }
            string arrays = "{\"bytes\":[67,\r\n225,0,0],\"numbers\":[-0,1.20,1e+9999,123456789012345678901234567890],\"mixed\":[1,\"2\"],\"nested\":[[1,2],[3,4]]}";
            MqttLogEntry numeric = new MqttLogEntry("RX", "/arrays", Encoding.UTF8.GetBytes(arrays), 0, false);
            numeric.SetFormat(MqttPayloadFormat.Json);
            Assert(numeric.DisplayText.Contains("[67, 225, 0, 0]") &&
                numeric.DisplayText.Contains("[-0, 1.20, 1e+9999, 123456789012345678901234567890]"), "numeric arrays stay inline without changing number tokens");
            Assert(numeric.DisplayText.Contains("[1, 2]") && numeric.DisplayText.Contains("[3, 4]") &&
                !numeric.DisplayText.Contains("[1, \"2\"]"), "nested numeric arrays compact independently and mixed arrays remain expanded");
            Assert(numeric.RawText == arrays && JsonSyntaxValidator.Validate(numeric.DisplayText) == null, "compact arrays preserve payload and valid JSON");
            log.Clear(); host.Size = new Size(860, 700); log.WordWrap = false;
            log.SetDisplayFormat(MqttPayloadFormat.Json);
            string longJson = "{\"function\":\"execute\",\"data\":{\"Voltages\":\"" + new string('A', 220) + "END\",\"Currents\":\"" + new string('B', 180) + "\"}}";
            log.AddEntry("RX", "/response", Encoding.UTF8.GetBytes(longJson), 0, false);
            log.AddEntry("TX", "/request/FCS1234567890", Encoding.UTF8.GetBytes(longJson), 0, false);
            Application.DoEvents();
            Dictionary<MqttLogEntry, HScrollBar> bars = (Dictionary<MqttLogEntry, HScrollBar>)Field(log, "scrollBars");
            Assert(bars.Count == 2, "each visible overflowing card has its own scrollbar");
            MqttLogEntry first = log.Entries[0], second = log.Entries[1];
            MqttPayloadTextBox firstEditor = log.Controls.Cast<Control>().SelectMany(x => x.Controls.Cast<Control>()).OfType<MqttPayloadTextBox>()
                .First(x => x.AccessibleName == "MQTT mesaj içeriği: /response");
            Assert(firstEditor.ReadOnly && firstEditor.Text.Contains("Voltages"), "card payload has a native selectable read-only text field");
            firstEditor.Select(firstEditor.Text.IndexOf("function", StringComparison.Ordinal), 8);
            Assert(firstEditor.SelectedText == "function" && first.SelectionText == "function" && first.RawText == longJson, "selection captures just the chosen text without changing payload");
            HScrollBar firstBar = bars[first];
            firstBar.Value = firstBar.Maximum - firstBar.LargeChange + 1;
            Assert(first.HorizontalOffset > 0 && second.HorizontalOffset == 0, "scrollbar scrolls only its own payload");
            Assert(first.Bounds.Contains(first.ScrollBounds) && first.Bounds.Width == second.Bounds.Width && second.Bounds.X > first.Bounds.X,
                "scrollbars stay inside equally sized left and right cards");
            foreach (bool dark in new[] { false, true })
            {
                log.ApplyTheme(dark); Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(log.Width, log.Height))
                { log.DrawToBitmap(bitmap, log.ClientRectangle); bitmap.Save("tests\\bin\\mqtt-card-scroll-" + (dark ? "dark" : "light") + ".png"); }
            }
            int savedOffset = first.HorizontalOffset;
            for (int i = 0; i < 20; i++) log.AddEntry("RX", "/later", Encoding.UTF8.GetBytes(longJson), 0, false);
            Invoke(log, "OnKeyDown", new KeyEventArgs(Keys.Home)); Assert(log.AutoScrollPosition.Y == 0, "Home goes to top");
            object[] navigation = { new Message(), Keys.PageDown };
            Assert((bool)log.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(log, navigation) && log.AutoScrollPosition.Y < 0,
                "Page Down is handled through command-key routing, including child scrollbar focus");
            Invoke(log, "OnKeyDown", new KeyEventArgs(Keys.Home));
            Invoke(log, "OnKeyDown", new KeyEventArgs(Keys.PageDown)); int pageY = -log.AutoScrollPosition.Y;
            Assert(pageY > 0 && pageY < log.AutoScrollMinSize.Height - log.ClientSize.Height, "Page Down moves one viewport");
            Invoke(log, "OnKeyDown", new KeyEventArgs(Keys.PageUp)); Assert(log.AutoScrollPosition.Y == 0, "Page Up moves back");
            Invoke(log, "OnKeyDown", new KeyEventArgs(Keys.End));
            Assert(-log.AutoScrollPosition.Y + log.ClientSize.Height >= log.AutoScrollMinSize.Height - 1, "End goes to bottom");
            log.AutoScrollPosition = new Point(0, log.AutoScrollMinSize.Height); log.Refresh(); Application.DoEvents();
            Assert(!bars.ContainsKey(first) && bars.Count <= 4 && !log.HorizontalScroll.Visible, "vertical history scrolling recycles bars without a panel-wide horizontal scrollbar");
            log.AutoScrollPosition = Point.Empty; log.Refresh(); Application.DoEvents();
            using (Bitmap bitmap = new Bitmap(log.Width, log.Height)) log.DrawToBitmap(bitmap, log.ClientRectangle);
            Assert(bars.ContainsKey(first) && bars[first].Value == savedOffset, "returning to an older card restores its horizontal position: saved=" + savedOffset + ", offset=" + first.HorizontalOffset + ", visible=" + bars.ContainsKey(first) + ", y=" + log.AutoScrollPosition.Y);
            host.Width = 1000; Application.DoEvents();
            Assert(first.HorizontalOffset <= first.PayloadWidth - first.PayloadBounds.Width && !log.HorizontalScroll.Visible,
                "resizing clamps the card offset without horizontal panel overflow");
            log.SetFilter("LATER");
            Assert(log.VisibleEntries.Count == 20 && log.Entries.Count == 22 && !bars.ContainsKey(first), "filter hides unmatched rows and their scrollbars without deleting history");
            log.SetFilter("no-match"); Assert(log.VisibleEntries.Count == 0 && log.AutoScrollMinSize.Height == 0, "empty results do not leave stale scroll space");
            log.SetFilter("execute"); Assert(log.VisibleEntries.Count == 22, "payload contents are searchable");
            log.SetFilter(""); Assert(log.VisibleEntries.Count == 22, "empty filter restores all rows");
            log.WordWrap = true; Application.DoEvents();
            Assert(bars.Count == 0, "wrapping disposes unused scrollbars");
            log.WordWrap = false; log.SetDisplayFormat(MqttPayloadFormat.Hex); Application.DoEvents();
            Assert(first.ScrollBounds.Width > 0, "hex overflow also uses card scrollbar");
            log.DeleteEntry(first); Assert(!bars.ContainsKey(first), "deleting a card removes its scrollbar");
            log.Clear(); Assert(bars.Count == 0, "clearing log removes scrollbar controls");
        }
    }

    private static void CheckConnectionWorkspace(Form host, MqttWorkspaceControl workspace, string path)
    {
        MqttProfilesControl profiles = (MqttProfilesControl)Field(workspace, "profiles");
        Dictionary<string, Control> fields = (Dictionary<string, Control>)Field(profiles, "fields");
        ComboBox profileSelector = (ComboBox)Field(profiles, "profileSelector");
        ListBox topics = (ListBox)Field(workspace, "subscribedTopics");
        SplitContainer communication = (SplitContainer)Field(workspace, "communicationSplit");
        int subscriptionWidth = communication.Panel1.Width;
        host.Width = 1100; Application.DoEvents();
        Assert(communication.IsSplitterFixed && communication.Panel1.Width == subscriptionWidth, "subscription panel width stays fixed when window grows");
        host.Width = 1000; Application.DoEvents();
        TcpListener probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        using (MqttServer server = new MqttFactory().CreateMqttServer(new MqttServerOptionsBuilder().WithDefaultEndpoint()
            .WithDefaultEndpointPort(port).WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).Build()))
        {
            Pump(server.StartAsync());
            fields["Name"].Text = "First"; fields["Host"].Text = "127.0.0.1"; ((NumericUpDown)fields["Port"]).Value = port;
            profiles.UpdateSubscriptions(new[] { "first/#" }); Invoke(profiles, "SaveProfile");
            Assert(topics.Items.Count == 1 && (string)topics.Items[0] == "first/#", "saved profile displays its own topic list");
            profileSelector.SelectedIndex = 0;
            Assert(topics.Items.Count == 0, "new profile displays an empty topic list");
            fields["Name"].Text = "Second"; fields["Host"].Text = "127.0.0.1"; ((NumericUpDown)fields["Port"]).Value = port;
            profiles.UpdateSubscriptions(new[] { "second/#" }); Invoke(profiles, "SaveProfile");
            profileSelector.SelectedIndex = 1;
            Assert((string)topics.Items[0] == "first/#", "switching profile updates visible topics immediately");
            SplitContainer split = (SplitContainer)Field(workspace, "connectionSplit");
            split.SplitterDistance = 220;
            Pump(workspace.ToggleConnectionAsync());
            Assert(workspace.IsConnected && split.SplitterDistance == 100, "successful connection collapses settings to 100 pixels");
            Assert(topics.Items.Count == 1 && (string)topics.Items[0] == "first/#", "connected topics match saved profile");
            Button save = (Button)Field(profiles, "saveButton");
            Assert(save.Height >= 28 && save.Parent.ClientRectangle.Contains(save.Bounds), "save button fits its toolbar when collapsed");
            ((TextBox)Field(workspace, "subscriptionTopic")).Text = "extra/#";
            ((Button)Field(workspace, "subscribeButton")).PerformClick();
            Until(delegate { return profiles.GetSelectedSubscriptions().Length == 2; });
            Assert(MqttProfileStore.Load(path + ".profiles")[0].Subscriptions.Count == 2, "subscribing persists topics to the selected profile");
            topics.SelectedItem = "extra/#";
            ((Button)Field(workspace, "unsubscribeButton")).PerformClick();
            Until(delegate { return profiles.GetSelectedSubscriptions().Length == 1; });
            Assert(MqttProfileStore.Load(path + ".profiles")[0].Subscriptions.Count == 1, "unsubscribing persists topic removal");
            JsonMessageTextBox editor = (JsonMessageTextBox)Field(workspace, "publishMessage");
            MqttLogControl log = (MqttLogControl)Field(workspace, "mqttLog");
            ((TextBox)Field(workspace, "publishTopic")).Text = "first/test";
            editor.Text = "{\r\n  function: \"test\"\r\n}";
            Pump((Task)workspace.GetType().GetMethod("PublishAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(workspace, null));
            Assert(editor.ValidationError != null && !log.Entries.Any(x => x.Direction == "TX" && x.Topic == "first/test"), "invalid JSON is marked and never published");
            foreach (bool dark in new[] { false, true })
            {
                workspace.ApplyTheme(dark); Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(editor.Width, editor.Height))
                {
                    editor.DrawToBitmap(bitmap, editor.ClientRectangle);
                    bool red = false;
                    for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
                    { Color color = bitmap.GetPixel(x, y); if (color.R > 180 && color.G < 100 && color.B < 100) red = true; }
                    Assert(red, "red diagnostic underline renders in " + (dark ? "dark" : "light") + " theme");
                    bitmap.Save("tests\\bin\\json-error-" + (dark ? "dark" : "light") + ".png");
                }
                using (Bitmap bitmap = new Bitmap(host.Width, host.Height))
                { host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size)); bitmap.Save("tests\\bin\\mqtt-connected-" + (dark ? "dark" : "light") + ".png"); }
            }
            editor.Text = "{\"function\":\"test\"}";
            Assert(editor.ValidationError == null, "fixing JSON clears stale diagnostics");
            Pump((Task)workspace.GetType().GetMethod("PublishAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(workspace, null));
            Until(delegate { return log.Entries.Any(x => x.Direction == "RX" && x.Topic == "first/test"); });
            Assert(log.Entries.Any(x => x.Direction == "TX" && x.Topic == "first/test"), "valid JSON publishes and receives through actual broker");
            ((ComboBox)Field(workspace, "logFormat")).SelectedIndex = 1;
            Assert(log.Entries.Where(x => x.IsMessage).All(x => x.Format == MqttPayloadFormat.Json), "workspace combobox reformats live messages");
            foreach (bool dark in new[] { false, true })
            {
                workspace.ApplyTheme(dark); Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(host.Width, host.Height))
                { host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size)); bitmap.Save("tests\\bin\\mqtt-log-workspace-" + (dark ? "dark" : "light") + ".png"); }
            }
            Pump(workspace.DisconnectAsync());
            Assert(split.SplitterDistance == 220, "disconnect restores user's previous panel position");
            profileSelector.SelectedIndex = 2;
            Assert(!log.Entries.Any(x => x.Topic == "first/test"), "changing selected profile hides the other connection's history");
            log.AddEntry("RX", "second/abcd", Encoding.UTF8.GetBytes("second connection"), 0, false);
            profileSelector.SelectedIndex = 1;
            MqttLogEntry firstReceived = log.Entries.First(x => x.Direction == "RX" && x.Topic == "first/test");
            Assert(!workspace.IsConnected && !log.Entries.Any(x => x.Topic == "second/abcd"), "selecting profile restores its history before connecting");
            using (Form reopenHost = new Form { Size = new Size(1000, 720), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) })
            using (MqttWorkspaceControl reopened = new MqttWorkspaceControl(new MqttProfilesControl(path + ".profiles"), path))
            {
                reopenHost.Controls.Add(reopened); reopenHost.Show(); Application.DoEvents();
                MqttProfilesControl savedProfiles = (MqttProfilesControl)Field(reopened, "profiles");
                ComboBox selectProfile = (ComboBox)Field(savedProfiles, "profileSelector");
                selectProfile.SelectedIndex = 1;
                MqttLogControl restoredLog = (MqttLogControl)Field(reopened, "mqttLog");
                Assert(!reopened.IsActive && restoredLog.Entries.Any(x => x.Id == firstReceived.Id && x.Timestamp == firstReceived.Timestamp),
                    "new workspace restores real broker messages and original timestamps offline");
                Assert(((ComboBox)Field(reopened, "logFormat")).Width == 150, "format combobox is one hundred fifty pixels wide");
                TextBox filter = (TextBox)Field(reopened, "logFilter");
                filter.Text = "first/test";
                int beforeFilter = restoredLog.VisibleEntries.Count;
                Assert(beforeFilter == restoredLog.Entries.Count, "typing alone does not apply filter");
                ((Button)Field(reopened, "filterButton")).PerformClick();
                Assert(restoredLog.VisibleEntries.Count == 2 && restoredLog.VisibleEntries.All(x => x.Topic == "first/test"), "filter button applies topic search");
                filter.Text = "ABCD";
                Invoke(filter, "OnKeyDown", new KeyEventArgs(Keys.Enter));
                restoredLog.AddEntry("RX", "/new", Encoding.UTF8.GetBytes("abcD payload"), 0, false);
                restoredLog.AddEntry("RX", "/hidden", Encoding.UTF8.GetBytes("saved while hidden"), 0, false);
                Assert(restoredLog.VisibleEntries.Count == 1 && restoredLog.VisibleEntries[0].Topic == "/new", "new messages obey applied case-insensitive filter");
                filter.Text = "abcd AND ("; ((Button)Field(reopened, "filterButton")).PerformClick();
                Assert(((ErrorProvider)Field(reopened, "filterErrors")).GetError(filter).Length > 0 && restoredLog.VisibleEntries.Count == 1,
                    "invalid query is explained while prior results remain intact");
                filter.Text = ""; Invoke(filter, "OnKeyDown", new KeyEventArgs(Keys.Enter));
                Assert(restoredLog.VisibleEntries.Count == restoredLog.Entries.Count && restoredLog.Entries.Any(x => x.Topic == "/hidden"), "empty Enter restores hidden messages");
                restoredLog.DeleteEntry(restoredLog.Entries.First(x => x.Id == firstReceived.Id));
                selectProfile.SelectedIndex = 2; selectProfile.SelectedIndex = 1;
                Assert(!restoredLog.Entries.Any(x => x.Id == firstReceived.Id) && restoredLog.Entries.Any(x => x.Topic == "/hidden"), "delete and filtered arrivals persist");
                restoredLog.SetFilter("no-match"); restoredLog.Clear();
                selectProfile.SelectedIndex = 2;
                Assert(restoredLog.Entries.Any(x => x.Topic == "second/abcd"), "Clear leaves other connections intact");
                selectProfile.SelectedIndex = 1;
                Assert(restoredLog.Entries.Count == 0, "Clear removes even filtered-out history permanently");
                reopenHost.Hide();
            }
            profileSelector.SelectedIndex = 0;
            fields["Name"].Text = "Auto history"; fields["Host"].Text = "127.0.0.1"; ((NumericUpDown)fields["Port"]).Value = port;
            Pump(workspace.ToggleConnectionAsync());
            Assert(workspace.IsConnected && profiles.SelectedProfileId != null && MqttProfileStore.Load(path + ".profiles").Count == 3,
                "connecting a new unsaved profile saves a stable selectable identity for history");
            Pump(((CommStudio.MqttSession)Field(workspace, "session")).PublishAsync("auto/test", "{\"value\":1}"));
            Pump(workspace.DisconnectAsync());
            string autoId = profiles.SelectedProfileId;
            fields["Name"].Text = "Renamed history"; Invoke(profiles, "SaveProfile");
            profileSelector.SelectedIndex = 1; profileSelector.SelectedIndex = 3;
            Assert(profiles.SelectedProfileId == autoId && log.Entries.Any(x => x.Topic == "auto/test" && x.Direction == "TX"),
                "renaming a connection preserves its recorded messages");
            Pump(server.StopAsync(new MqttServerStopOptions()));
        }
    }
    private static void Pump(Task task) { Until(delegate { return task.IsCompleted; }); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        Assert(condition(), "asynchronous operation completed within timeout");
        Application.DoEvents();
    }
    private static object Field(object instance, string name) { return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
    private static void Invoke(object instance, string name, params object[] values) { instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, values); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
