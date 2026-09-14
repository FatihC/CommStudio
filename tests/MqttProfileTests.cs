using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using CommStudio;

internal static class MqttProfileTests
{
    [STAThread]
    private static int Main()
    {
        string path = Path.GetFullPath("tests\\bin\\mqtt-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Application.EnableVisualStyles();
            using (Form host = new Form { Size = new Size(1000, 720), ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) })
            using (MqttProfilesControl editor = new MqttProfilesControl(path))
            {
                host.Controls.Add(editor);
                host.Show();
                Application.DoEvents();
                Dictionary<string, Control> fields = (Dictionary<string, Control>)Field(editor, "fields");
                ComboBox selector = (ComboBox)Field(editor, "profileSelector");
                Button save = (Button)Field(editor, "saveButton");
                Assert(selector.Items.Count == 1, "new installation offers Yeni Ekle");
                save.PerformClick();
                Assert(!File.Exists(path), "empty required fields cannot create a profile");
                fields["Name"].Text = "Test broker";
                fields["Host"].Text = "broker.example.test";
                fields["Password"].Text = "test-secret-123";
                fields["MaximumPacketSize"].Text = "4294967295";
                fields["ReceiveMaximum"].Text = "65535";
                fields["TopicAliasMaximum"].Text = "0";
                fields["WillPayload"].Text = "line1\r\nline2";
                fields["AuthenticationMethod"].Text = "custom-auth";
                ((CheckBox)fields["UseTls"]).Checked = true;
                Assert(fields["Scheme"].Text == "mqtts://", "TLS and transport stay consistent");
                DataGridView properties = (DataGridView)Field(editor, "userProperties");
                properties.Rows.Add("site", "test");
                properties.Rows.Add("site", "second value");
                save.PerformClick();
                List<MqttProfile> stored = MqttProfileStore.Load(path);
                Assert(stored.Count == 1 && selector.SelectedIndex == 1, "saving creates and selects a stored connection");
                Assert(!File.ReadAllText(path).Contains("test-secret-123") && stored[0].Password == "test-secret-123",
                    "password survives encrypted storage without plaintext in JSON");
                Assert(stored[0].MaximumPacketSize == uint.MaxValue && stored[0].UserProperties.Count == 2 &&
                    stored[0].AuthenticationMethod == "custom-auth" && stored[0].WillPayload.Contains("\n"),
                    "advanced fields, duplicate user-property keys and multiline will survive save");
                string id = stored[0].Id; MqttProfileStore.Save(path, stored);
                fields["Name"].Text = "Renamed broker";
                fields["Host"].Text = "edited.example.test";
                fields["ReceiveMaximum"].Text = "0";
                save.PerformClick();
                Assert(MqttProfileStore.Load(path)[0].Name == "Test broker", "invalid optional number leaves the stored profile intact");
                fields["ReceiveMaximum"].Text = "";
                save.PerformClick();
                stored = MqttProfileStore.Load(path);
                Assert(stored.Count == 1 && stored[0].Id == id && stored[0].Name == "Renamed broker" &&
                    stored[0].Host == "edited.example.test" && stored[0].ReceiveMaximum == null,
                    "editing updates the selected profile rather than duplicating it: " + ((Label)Field(editor, "feedback")).Text + " Count=" + stored.Count + " Name=" + stored[0].Name + " Receive=" + stored[0].ReceiveMaximum);

                selector.SelectedIndex = 0;
                fields["Name"].Text = "Draft broker";
                fields["Host"].Text = "draft.example.test";
                fields["MaximumPacketSize"].Text = "unfinished";
                selector.SelectedIndex = 1;
                Assert(fields["Name"].Text == "Renamed broker" && fields["Password"].Text == "test-secret-123",
                    "selecting saved profile fills editable fields");
                selector.SelectedIndex = 0;
                Assert(fields["Name"].Text == "Draft broker" && fields["MaximumPacketSize"].Text == "unfinished",
                    "switching profiles preserves unfinished drafts exactly");
                fields["MaximumPacketSize"].Text = "";
                editor.UpdateSubscriptions(new[] { "second/#" });
                save.PerformClick();
                Assert(MqttProfileStore.Load(path).Count == 2, "Yeni Ekle creates a separate connection");
                Assert(editor.GetSelectedSubscriptions()[0] == "second/#", "new profile saves its draft subscriptions");
                selector.SelectedIndex = 1;
                Assert(editor.GetSelectedSubscriptions().Length == 0, "profiles do not inherit another profile's topics");
                fields["Host"].Text = "unsaved.example.test";
                editor.UpdateSubscriptions(new[] { "first/+", "response" });
                Assert(MqttProfileStore.Load(path)[0].Host == "edited.example.test", "subscription save does not save unrelated form edits");
                selector.SelectedIndex = 2;
                Assert(editor.GetSelectedSubscriptions()[0] == "second/#", "profile switching restores its topic list");
                editor.UpdateSubscriptions(new string[0]);
                Assert(MqttProfileStore.Load(path)[1].Subscriptions.Count == 0, "unsubscribe persists removal");
                selector.SelectedIndex = 1;
                MqttProfile copied = editor.GetConnectionProfile();
                copied.Subscriptions.Clear();
                Assert(editor.GetSelectedSubscriptions().Length == 2, "profile copy owns a separate subscription list");
                host.Hide();
            }
            using (MqttProfilesControl reopened = new MqttProfilesControl(path))
            {
                ComboBox selector = (ComboBox)Field(reopened, "profileSelector");
                Assert(selector.Items.Count == 3, "connection list survives reopening the editor");
                selector.SelectedIndex = 1;
                Dictionary<string, Control> fields = (Dictionary<string, Control>)Field(reopened, "fields");
                Assert(fields["Host"].Text == "edited.example.test", "reopened profile contains saved edits");
                Assert(reopened.GetSelectedSubscriptions().Length == 2, "subscriptions survive reopening");
                Button delete = (Button)Field(reopened, "deleteButton");
                reopened.SetSessionActive(true);
                Assert(!delete.Enabled, "active connection cannot be deleted");
                reopened.SetSessionActive(false);
                typeof(MqttProfilesControl).GetMethod("DeleteProfile", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(reopened, null);
                Assert(MqttProfileStore.Load(path).Count == 1 && reopened.GetSelectedSubscriptions().Length == 0,
                    "deleting a connection removes its topics and selects the next profile");
            }
            File.WriteAllText(path, "invalid json");
            using (MqttProfilesControl broken = new MqttProfilesControl(path))
                Assert(!((Button)Field(broken, "saveButton")).Enabled && File.ReadAllText(path) == "invalid json",
                    "unreadable existing records are never silently overwritten");
            Console.WriteLine("MQTT profile tests passed: create, edit, reopen, validation, drafts and encrypted password storage.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static object Field(object instance, string name)
    { return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
