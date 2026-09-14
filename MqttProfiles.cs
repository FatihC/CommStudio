using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace CommStudio
{
    [DataContract]
    internal sealed class MqttUserProperty
    {
        [DataMember] public string Key { get; set; }
        [DataMember] public string Value { get; set; }
    }

    [DataContract]
    internal sealed class MqttProfile
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Scheme { get; set; }
        [DataMember] public string Host { get; set; }
        [DataMember] public int Port { get; set; }
        [DataMember] public string ClientId { get; set; }
        [DataMember] public string Username { get; set; }
        [IgnoreDataMember] public string Password { get; set; }
        [DataMember] public string ProtectedPassword { get; set; }
        [DataMember] public bool UseTls { get; set; }
        [DataMember] public string Version { get; set; }
        [DataMember] public int ConnectTimeout { get; set; }
        [DataMember] public int KeepAlive { get; set; }
        [DataMember] public bool AutoReconnect { get; set; }
        [DataMember] public int ReconnectPeriod { get; set; }
        [DataMember] public bool CleanStart { get; set; }
        [DataMember] public long SessionExpiryInterval { get; set; }
        [DataMember] public int? ReceiveMaximum { get; set; }
        [DataMember] public long? MaximumPacketSize { get; set; }
        [DataMember] public int? TopicAliasMaximum { get; set; }
        [DataMember] public bool RequestResponseInfo { get; set; }
        [DataMember] public bool RequestProblemInfo { get; set; }
        [DataMember] public string AuthenticationMethod { get; set; }
        [DataMember] public List<MqttUserProperty> UserProperties { get; set; }
        [DataMember] public List<string> Subscriptions { get; set; }
        [DataMember] public string WillTopic { get; set; }
        [DataMember] public int WillQos { get; set; }
        [DataMember] public bool WillRetain { get; set; }
        [DataMember] public string WillPayload { get; set; }
        [DataMember] public string WillPayloadFormat { get; set; }
        [DataMember] public bool PayloadFormatIndicator { get; set; }
        [DataMember] public long WillDelayInterval { get; set; }

        public static MqttProfile CreateNew()
        {
            return new MqttProfile { Id = Guid.NewGuid().ToString("N"), Name = "", Host = "", Scheme = "mqtt://",
                Port = 1883, ClientId = "commstudio_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Version = "5.0", ConnectTimeout = 10, KeepAlive = 60, AutoReconnect = true,
                ReconnectPeriod = 4000, CleanStart = true, RequestProblemInfo = true,
                UserProperties = new List<MqttUserProperty>(), Subscriptions = new List<string>(), WillPayloadFormat = "Plaintext" };
        }

        public MqttProfile Copy()
        {
            MqttProfile copy = (MqttProfile)MemberwiseClone();
            copy.Subscriptions = new List<string>(Subscriptions ?? new List<string>());
            copy.UserProperties = new List<MqttUserProperty>();
            if (UserProperties != null)
                foreach (MqttUserProperty property in UserProperties)
                    copy.UserProperties.Add(new MqttUserProperty { Key = property.Key, Value = property.Value });
            return copy;
        }

        public override string ToString() { return Name; }
    }

    internal static class MqttProfileStore
    {
        public static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CommStudio", "mqtt-connections.json"); }
        }

        public static List<MqttProfile> Load(string path)
        {
            if (!File.Exists(path)) return new List<MqttProfile>();
            List<MqttProfile> profiles;
            using (FileStream stream = File.OpenRead(path))
                profiles = (List<MqttProfile>)new DataContractJsonSerializer(typeof(List<MqttProfile>)).ReadObject(stream);
            if (profiles == null) throw new InvalidDataException("MQTT connection file is empty or invalid.");
            foreach (MqttProfile profile in profiles)
            {
                if (profile == null || string.IsNullOrEmpty(profile.Id))
                    throw new InvalidDataException("MQTT connection file contains an invalid profile.");
                if (!string.IsNullOrEmpty(profile.ProtectedPassword))
                    profile.Password = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                        Convert.FromBase64String(profile.ProtectedPassword), null, DataProtectionScope.CurrentUser));
                if (profile.UserProperties == null) profile.UserProperties = new List<MqttUserProperty>();
                if (profile.Subscriptions == null) profile.Subscriptions = new List<string>();
            }
            return profiles;
        }

        public static void Save(string path, List<MqttProfile> profiles)
        {
            List<MqttProfile> stored = new List<MqttProfile>();
            foreach (MqttProfile profile in profiles)
            {
                MqttProfile copy = profile.Copy();
                copy.ProtectedPassword = string.IsNullOrEmpty(copy.Password) ? null : Convert.ToBase64String(
                    ProtectedData.Protect(Encoding.UTF8.GetBytes(copy.Password), null, DataProtectionScope.CurrentUser));
                stored.Add(copy);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(List<MqttProfile>)).WriteObject(stream, stored);
                // Preserve atomic replacement even when optional file metadata cannot be copied.
                if (File.Exists(path)) File.Replace(temporary, path, null, true);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
