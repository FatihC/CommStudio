using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CommStudio
{
    [DataContract]
    internal sealed class SavedMqttMessage
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public DateTime CreatedAtUtc { get; set; }
        [DataMember] public DateTime UpdatedAtUtc { get; set; }
        [DataMember] public string Topic { get; set; }
        [DataMember] public string Message { get; set; }
        public override string ToString() { return Name + " · " + UpdatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"); }
        public SavedMqttMessage Copy() { return (SavedMqttMessage)MemberwiseClone(); }
    }

    internal static class SavedMqttMessageStore
    {
        public static string DefaultPath { get { return Path.Combine(Path.GetDirectoryName(MqttProfileStore.DefaultPath), "mqtt-messages.json"); } }
        public static List<SavedMqttMessage> Load(string path)
        {
            if (!File.Exists(path)) return new List<SavedMqttMessage>();
            using (FileStream stream = File.OpenRead(path))
            {
                List<SavedMqttMessage> messages = (List<SavedMqttMessage>)new DataContractJsonSerializer(typeof(List<SavedMqttMessage>)).ReadObject(stream);
                if (messages == null) throw new InvalidDataException("Mesaj kayıtları okunamadı.");
                foreach (SavedMqttMessage message in messages)
                    if (message == null || string.IsNullOrEmpty(message.Id) || string.IsNullOrWhiteSpace(message.Name))
                        throw new InvalidDataException("Geçersiz mesaj kaydı.");
                return messages;
            }
        }
        public static void Save(string path, List<SavedMqttMessage> messages)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(List<SavedMqttMessage>)).WriteObject(stream, messages);
                if (File.Exists(path)) File.Replace(temporary, path, null, true);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
