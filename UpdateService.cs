using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CommStudio
{
    [DataContract]
    internal sealed class GithubRelease
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public GithubAsset[] Assets { get; set; }
    }

    [DataContract]
    internal sealed class GithubAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string Url { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
        [DataMember(Name = "digest")] public string Digest { get; set; }
    }

    internal sealed class AvailableUpdate
    {
        public Version Version;
        public GithubAsset Asset;
    }

    [DataContract]
    internal sealed class UpdatePlan
    {
        [DataMember] public string Target;
        [DataMember] public string Hash;
        [DataMember] public string Version;
        [DataMember] public int ParentId;
        [DataMember] public long ParentStartTicks;
    }

    internal static class UpdateService
    {
        internal const string Repository = "FatihC/CommStudio";
        private const long MaxExeBytes = 256L * 1024 * 1024;
        internal static readonly string UpdateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommStudio", "updates");
        internal static string PendingDirectory { get; set; }
        internal static Version CurrentVersion { get { return typeof(UpdateService).Assembly.GetName().Version; } }

        internal static Version ParseVersion(string tag)
        {
            if (tag == null || !Regex.IsMatch(tag, @"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z"))
                throw new InvalidDataException("Sürüm numarası v1.2.3 biçiminde olmalı.");
            Version version;
            if (!System.Version.TryParse(tag.TrimStart('v'), out version)
                || version.Major > 65534 || version.Minor > 65534 || version.Build > 65534)
                throw new InvalidDataException("Geçersiz sürüm numarası.");
            return new Version(version.Major, version.Minor, version.Build, 0);
        }

        internal static AvailableUpdate SelectRelease(GithubRelease release, Version current)
        {
            if (release == null) throw new InvalidDataException("GitHub sürüm bilgisi okunamadı.");
            if (release.Draft || release.Prerelease) return null;
            Version version = ParseVersion(release.Tag);
            if (version <= current) return null;
            GithubAsset selected = null;
            foreach (GithubAsset asset in release.Assets ?? new GithubAsset[0])
            {
                if (asset.Name != "CommStudio.exe") continue;
                if (selected != null) throw new InvalidDataException("Sürümde birden fazla CommStudio.exe var.");
                selected = asset;
            }
            if (selected == null) throw new InvalidDataException("Yeni sürümde CommStudio.exe henüz yayımlanmamış.");
            Uri url;
            string prefix = "https://github.com/" + Repository + "/releases/download/";
            if (!Uri.TryCreate(selected.Url, UriKind.Absolute, out url) || !selected.Url.StartsWith(prefix, StringComparison.Ordinal)
                || url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com" || !url.IsDefaultPort
                || !string.IsNullOrEmpty(url.UserInfo) || selected.Size <= 0 || selected.Size > MaxExeBytes
                || selected.Digest == null || !Regex.IsMatch(selected.Digest, @"\Asha256:[a-fA-F0-9]{64}\z"))
                throw new InvalidDataException("Sürümün indirme adresi, boyutu veya SHA-256 doğrulama bilgisi geçersiz.");
            return new AvailableUpdate { Version = version, Asset = selected };
        }

        internal static Task<AvailableUpdate> CheckAsync(CancellationToken cancellation)
        {
            return Task.Run(delegate
            {
                try
                {
                    byte[] json = ReadUrl("https://api.github.com/repos/" + Repository + "/releases/latest",
                        2 * 1024 * 1024, null, null, cancellation);
                    using (MemoryStream stream = new MemoryStream(json))
                        return SelectRelease((GithubRelease)new DataContractJsonSerializer(typeof(GithubRelease)).ReadObject(stream), CurrentVersion);
                }
                catch (WebException error)
                {
                    using (HttpWebResponse response = error.Response as HttpWebResponse)
                    {
                        if (response != null && response.StatusCode == HttpStatusCode.NotFound) return null;
                        if (response != null && ((int)response.StatusCode == 403 || (int)response.StatusCode == 429))
                            throw new IOException("GitHub istek sınırına ulaşıldı. Lütfen daha sonra tekrar deneyin.");
                    }
                    throw;
                }
            }, cancellation);
        }

        private static byte[] ReadUrl(string url, long limit, string destination, Action<long> progress, CancellationToken cancellation)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "CommStudio/" + CurrentVersion.ToString(3);
            request.Accept = destination == null ? "application/vnd.github+json" : "application/octet-stream";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 30000;
            request.AllowAutoRedirect = true;
            request.MaximumAutomaticRedirections = 5;
            using (cancellation.Register(request.Abort))
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if (response.ResponseUri.Scheme != Uri.UriSchemeHttps || response.ContentLength > limit)
                    throw new InvalidDataException("İndirme yanıtı güvenli değil veya dosya çok büyük.");
                using (Stream input = response.GetResponseStream())
                using (Stream output = destination == null ? (Stream)new MemoryStream() : File.Create(destination))
                {
                    byte[] buffer = new byte[65536];
                    long total = 0;
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        total += count;
                        if (total > limit) throw new InvalidDataException("İndirilen dosya beklenen boyutu aşıyor.");
                        output.Write(buffer, 0, count);
                        if (progress != null) progress(total);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    return destination == null ? ((MemoryStream)output).ToArray() : null;
                }
            }
        }

        internal static Task<string> PrepareAsync(AvailableUpdate update, string target, IProgress<int> progress, CancellationToken cancellation)
        {
            return Task.Run(delegate
            {
                string directory = Path.Combine(UpdateRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try
                {
                    // Fail before downloading or closing if the portable EXE's folder is read-only.
                    string probe = Path.Combine(Path.GetDirectoryName(target), ".commstudio-write-" + Guid.NewGuid().ToString("N"));
                    using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                    string payload = Path.Combine(directory, "download.exe");
                    ReadUrl(update.Asset.Url, update.Asset.Size, payload,
                        delegate(long count) { progress.Report((int)(count * 100 / update.Asset.Size)); }, cancellation);
                    if (new FileInfo(payload).Length != update.Asset.Size) throw new InvalidDataException("İndirme tamamlanamadı.");
                    string hash = update.Asset.Digest.Substring(7);
                    VerifyExecutable(payload, hash, update.Version);
                    cancellation.ThrowIfCancellationRequested();
                    File.Copy(Application.ExecutablePath, Path.Combine(directory, "updater.exe"));
                    using (Process parent = Process.GetCurrentProcess())
                    using (FileStream stream = File.Create(Path.Combine(directory, "plan.json")))
                        new DataContractJsonSerializer(typeof(UpdatePlan)).WriteObject(stream, new UpdatePlan
                        {
                            Target = Path.GetFullPath(target), Hash = hash, Version = update.Version.ToString(3),
                            ParentId = parent.Id, ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks
                        });
                    return directory;
                }
                catch { Cleanup(directory); throw; }
            }, cancellation);
        }

        internal static void VerifyExecutable(string path, string hash, Version version)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Dosyanın SHA-256 doğrulaması başarısız. Mevcut sürüm korundu.");
            }
            AssemblyName assembly = AssemblyName.GetAssemblyName(path);
            if (assembly.Name != "CommStudio" || assembly.Version != version)
                throw new InvalidDataException("İndirilen uygulamanın adı veya sürümü yayın bilgisiyle eşleşmiyor.");
        }

        internal static void LaunchPending()
        {
            if (PendingDirectory == null) return;
            Start(Path.Combine(PendingDirectory, "updater.exe"), "--apply-update " + Quote(PendingDirectory));
        }

        internal static bool IsUpdateDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return false;
            string full = Path.GetFullPath(directory);
            Guid id;
            return string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(UpdateRoot), StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(Path.GetFileName(full), "N", out id)
                && Directory.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0;
        }

        internal static void RunHelper(string directory)
        {
            try
            {
                if (!IsUpdateDirectory(directory) || !string.Equals(Application.ExecutablePath,
                    Path.Combine(directory, "updater.exe"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Geçersiz güncelleme işlemi.");
                UpdatePlan plan;
                using (FileStream stream = File.OpenRead(Path.Combine(directory, "plan.json")))
                    plan = (UpdatePlan)new DataContractJsonSerializer(typeof(UpdatePlan)).ReadObject(stream);
                try
                {
                    using (Process parent = Process.GetProcessById(plan.ParentId))
                        if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks && !parent.WaitForExit(60000))
                            throw new IOException("CommStudio kapanmadı. Uygulamayı kapatıp tekrar deneyin.");
                }
                catch (ArgumentException) { /* Parent has already exited. */ }
                Version version = ParseVersion(plan.Version);
                VerifyExecutable(Path.Combine(directory, "download.exe"), plan.Hash, version);
                Install(directory, plan.Target, delegate
                {
                    Start(plan.Target, "--cleanup-update " + Quote(directory));
                });
            }
            catch (Exception error)
            {
                MessageBox.Show("Güncelleme tamamlanamadı. Eski sürüm yedekleri şu klasördedir:\n" + directory
                    + "\n\n" + error.Message + "\n\nCommStudio'yu kendi konumundan yeniden açabilirsiniz.",
                    "CommStudio güncellemesi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Candidate and target share a volume, even when LocalAppData is on another drive.
        internal static void Install(string directory, string target, Action restart)
        {
            if (!Path.IsPathRooted(target) || !string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Geçersiz uygulama konumu.");
            string candidate = Path.Combine(Path.GetDirectoryName(target), ".commstudio-update-" + Guid.NewGuid().ToString("N") + ".tmp");
            string backup = Path.Combine(directory, "previous.exe");
            bool replaced = false;
            try
            {
                File.Copy(target, backup, false);
                File.Copy(Path.Combine(directory, "download.exe"), candidate, false);
                ReplaceWithRetry(candidate, target);
                replaced = true;
                restart();
            }
            catch
            {
                if (replaced)
                {
                    File.Copy(backup, candidate, true);
                    ReplaceWithRetry(candidate, target);
                }
                throw;
            }
            finally { try { File.Delete(candidate); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        private static void ReplaceWithRetry(string source, string target)
        {
            for (int attempt = 0; ; attempt++)
            {
                // Portable folders may grant Modify without permission to merge ACL metadata.
                // The candidate already inherits this same folder's access permissions.
                try { File.Replace(source, target, null, true); return; }
                catch (IOException) { if (attempt >= 20) throw; Thread.Sleep(250); }
                catch (UnauthorizedAccessException) { if (attempt >= 20) throw; Thread.Sleep(250); }
            }
        }

        private static void Start(string path, string arguments)
        {
            using (Process process = Process.Start(new ProcessStartInfo(path, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(path)
            }))
                if (process == null) throw new IOException("Uygulama yeniden başlatılamadı.");
        }

        private static string Quote(string path)
        {
            if (path.IndexOf('"') >= 0 || path.EndsWith("\\", StringComparison.Ordinal)) throw new InvalidDataException("Geçersiz dosya yolu.");
            return "\"" + path + "\"";
        }

        internal static void Cleanup(string directory)
        {
            try
            {
                if (!IsUpdateDirectory(directory)) return;
                foreach (string name in new[] { "download.exe", "updater.exe", "plan.json", "previous.exe" })
                    File.Delete(Path.Combine(directory, name));
                Directory.Delete(directory, false);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        internal static void CleanupAfterRestart(string directory)
        {
            Task.Run(delegate
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    Cleanup(directory);
                    if (!Directory.Exists(directory)) return;
                    Thread.Sleep(250);
                }
            });
        }
    }
}
