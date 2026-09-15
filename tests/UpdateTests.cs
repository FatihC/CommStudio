using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CommStudio;

internal static class UpdateTests
{
    private static int assertions;

    [STAThread]
    private static int Main()
    {
        try
        {
            TestVersionsAndMetadata();
            TestVerification();
            TestInstallation();
            TestDialogs();
            Console.WriteLine("Update tests passed: " + assertions + " assertions (versions, metadata, integrity, running EXE replacement, restart, rollback, locked files and dialogs).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static GithubRelease Release(string tag)
    {
        return new GithubRelease
        {
            Tag = tag, Assets = new[] { new GithubAsset
            {
                Name = "CommStudio.exe", Size = 100,
                Url = "https://github.com/FatihC/CommStudio/releases/download/" + tag + "/CommStudio.exe",
                Digest = "sha256:" + new string('a', 64)
            } }
        };
    }

    private static void TestVersionsAndMetadata()
    {
        Version current = new Version(0, 2, 0, 0);
        Assert(UpdateService.SelectRelease(Release("v0.10.0"), current).Version == new Version(0, 10, 0, 0), "numeric version order");
        Assert(UpdateService.SelectRelease(Release("v0.2.0"), current) == null, "equal version");
        Assert(UpdateService.SelectRelease(Release("v0.1.0"), current) == null, "no downgrade");
        foreach (string invalid in new[] { "v0.2.1-beta", "v01.2.3", "v1.2", "v1.2.3.4", "v65535.0.0", "../1.2.3", "v1.2.3\n" })
            Reject(delegate { UpdateService.ParseVersion(invalid); }, "reject invalid tag " + invalid);
        GithubRelease release = Release("v0.3.0");
        release.Prerelease = true;
        Assert(UpdateService.SelectRelease(release, current) == null, "skip prerelease");
        release.Prerelease = false;
        release.Draft = true;
        Assert(UpdateService.SelectRelease(release, current) == null, "skip draft");
        release = Release("v0.3.0");
        release.Assets = new GithubAsset[0];
        Reject(delegate { UpdateService.SelectRelease(release, current); }, "missing EXE");
        foreach (string url in new[] { "http://github.com/FatihC/CommStudio/releases/download/v0.3.0/CommStudio.exe",
            "https://github.com.evil.test/FatihC/CommStudio/releases/download/v0.3.0/CommStudio.exe",
            "https://github.com/another/repo/releases/download/v0.3.0/CommStudio.exe" })
        {
            release = Release("v0.3.0"); release.Assets[0].Url = url;
            Reject(delegate { UpdateService.SelectRelease(release, current); }, "untrusted download address");
        }
        foreach (string hash in new[] { null, "sha256:bad", "sha1:" + new string('a', 64) })
        {
            release = Release("v0.3.0"); release.Assets[0].Digest = hash;
            Reject(delegate { UpdateService.SelectRelease(release, current); }, "missing or invalid hash");
        }
        release = Release("v0.3.0"); release.Assets[0].Size = 0;
        Reject(delegate { UpdateService.SelectRelease(release, current); }, "empty asset");
        string json = "{\"tag_name\":\"v0.3.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"CommStudio.exe\",\"size\":100,"
            + "\"browser_download_url\":\"https://github.com/FatihC/CommStudio/releases/download/v0.3.0/CommStudio.exe\",\"digest\":\"sha256:" + new string('b', 64) + "\"}]}";
        using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            Assert(UpdateService.SelectRelease((GithubRelease)new DataContractJsonSerializer(typeof(GithubRelease)).ReadObject(stream), current) != null,
                "real API field names deserialize");
    }

    private static string Fixture(bool updated)
    {
        return Path.GetFullPath(Path.Combine("tests", "bin", updated ? "update-new" : "update-old", "CommStudio.exe"));
    }

    private static string Hash(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (Stream stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    private static void TestVerification()
    {
        UpdateService.VerifyExecutable(Fixture(true), Hash(Fixture(true)), new Version(0, 2, 1, 0));
        Assert(true, "valid EXE accepted");
        Reject(delegate { UpdateService.VerifyExecutable(Fixture(true), new string('0', 64), new Version(0, 2, 1, 0)); }, "corrupt hash");
        Reject(delegate { UpdateService.VerifyExecutable(Fixture(true), Hash(Fixture(true)), new Version(0, 3, 0, 0)); }, "wrong embedded version");
        string testExe = Assembly.GetExecutingAssembly().Location;
        Reject(delegate { UpdateService.VerifyExecutable(testExe, Hash(testExe), Assembly.GetExecutingAssembly().GetName().Version); }, "wrong assembly name");
    }

    private static void TestInstallation()
    {
        string root = Path.GetFullPath(Path.Combine("tests", "bin", "update scenarios " + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        foreach (string scenario in new[] { "success", "restart failure", "locked target" })
        {
            string directory = Path.Combine(root, scenario);
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, "Renamed app.exe");
            File.Copy(Fixture(false), target);
            File.Copy(Fixture(true), Path.Combine(directory, "download.exe"));
            if (scenario == "success")
            {
                string ready = Path.Combine(directory, "ready.txt");
                using (Process old = Process.Start(new ProcessStartInfo(target, "--wait \"" + ready + "\"") { UseShellExecute = false, CreateNoWindow = true }))
                {
                    Stopwatch clock = Stopwatch.StartNew();
                    while (!File.Exists(ready) && clock.ElapsedMilliseconds < 5000) Thread.Sleep(20);
                    Assert(File.Exists(ready) && !old.HasExited, "old EXE is running before replacement");
                    string marker = Path.Combine(directory, "restarted.txt");
                    UpdateService.Install(directory, target, delegate
                    {
                        using (Process next = Process.Start(new ProcessStartInfo(target, "\"" + marker + "\"") { UseShellExecute = false, CreateNoWindow = true }))
                            Assert(next.WaitForExit(5000) && next.ExitCode == 0, "new EXE starts");
                    });
                    Assert(File.ReadAllText(marker) == "0.2.1.0", "restart uses new version at original renamed path with spaces");
                }
            }
            else if (scenario == "restart failure")
            {
                Reject(delegate { UpdateService.Install(directory, target, delegate { throw new IOException("Simulated restart failure"); }); }, "restart error surfaces");
                Assert(Hash(target) == Hash(Fixture(false)), "restart failure restores original bytes");
            }
            else
            {
                using (FileStream locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Reject(delegate { UpdateService.Install(directory, target, delegate { throw new Exception("Must not restart"); }); }, "locked EXE fails safely");
                Assert(Hash(target) == Hash(Fixture(false)), "locked target preserved");
            }
            Assert(Hash(Path.Combine(directory, "previous.exe")) == Hash(Fixture(false)), "backup contains previous version");
            Assert(Directory.GetFiles(directory, ".commstudio-update-*").Length == 0, "no temporary candidate left beside EXE");
        }
        // Leave fixtures in ignored tests/bin for inspection; never delete recursively outside a test sandbox.
    }

    private static void TestDialogs()
    {
        Application.EnableVisualStyles();
        foreach (bool dark in new[] { false, true })
        {
            using (UpdateForm form = new UpdateForm(UpdateService.SelectRelease(Release("v0.3.0"), new Version(0, 2, 0, 0)), dark))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-3000, -3000);
                form.Show();
                Application.DoEvents();
                Assert(form.AcceptButton != null && form.CancelButton != null, "update dialog has install and later actions");
                Assert(dark ? form.BackColor.R < 80 : form.BackColor.R > 200, "update dialog follows theme");
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine("tests", "bin", "update-" + (dark ? "dark" : "light") + ".png"), ImageFormat.Png);
                }
                form.Close();
                Assert(form.PreparedDirectory == null, "later does not prepare or install");
            }
        }
    }

    private static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (Exception) { rejected = true; }
        Assert(rejected, name);
    }

    private static void Assert(bool condition, string name)
    {
        assertions++;
        if (!condition) throw new Exception("Failed: " + name);
    }
}
