using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace CommStudio
{
    // Append-only journal: receiving a message never rewrites the entire history.
    internal sealed class MqttHistoryStore
    {
        [DataContract]
        private sealed class Record
        {
            [DataMember] public string Operation;
            [DataMember] public string Id;
            [DataMember] public string Direction;
            [DataMember] public string Topic;
            [DataMember] public string Payload;
            [DataMember] public long TimestampTicks;
            [DataMember] public int Qos;
            [DataMember] public bool Retain;
        }
        public readonly string ProfileId;
        internal readonly string FilePath;
        private bool ready;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        public MqttHistoryStore(string directory, string profileId)
        {
            ProfileId = profileId;
            using (SHA256 hash = SHA256.Create())
                FilePath = Path.Combine(directory, BitConverter.ToString(hash.ComputeHash(Utf8.GetBytes(profileId))).Replace("-", "") + ".jsonl");
        }

        public List<MqttLogEntry> Load()
        {
            ready = false;
            List<MqttLogEntry> entries = new List<MqttLogEntry>();
            Dictionary<string, MqttLogEntry> byId = new Dictionary<string, MqttLogEntry>();
            long validBytes = 0;
            bool repairTail = false, addNewline = false;
            if (File.Exists(FilePath))
            {
                using (FileStream stream = File.OpenRead(FilePath))
                using (StreamReader reader = new StreamReader(stream, Utf8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        Record record;
                        try
                        {
                            if (JsonSyntaxValidator.Validate(line) != null) throw new SerializationException("Eksik veya geçersiz geçmiş kaydı.");
                            using (MemoryStream bytes = new MemoryStream(Utf8.GetBytes(line)))
                                record = (Record)new DataContractJsonSerializer(typeof(Record)).ReadObject(bytes);
                            if (record == null || (record.Operation != "add" && record.Operation != "delete" && record.Operation != "clear"))
                                throw new SerializationException("Geçersiz geçmiş kaydı.");
                        }
                        catch (SerializationException)
                        {
                            // A crash can leave an incomplete final append. Only repair an
                            // unterminated tail; malformed complete records remain untouched.
                            if (reader.EndOfStream && stream.Length > validBytes && !EndsWithNewline(FilePath))
                            { repairTail = true; break; }
                            throw;
                        }
                        if (record.Operation == "clear") { entries.Clear(); byId.Clear(); }
                        else if (record.Operation == "delete")
                        {
                            MqttLogEntry found;
                            if (record.Id != null && byId.TryGetValue(record.Id, out found)) { entries.Remove(found); byId.Remove(record.Id); }
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(record.Id) || string.IsNullOrEmpty(record.Direction)) throw new SerializationException("Geçersiz mesaj kaydı.");
                            MqttLogEntry entry = new MqttLogEntry(record.Direction, record.Topic, Convert.FromBase64String(record.Payload ?? ""),
                                record.Qos, record.Retain, new DateTime(record.TimestampTicks, DateTimeKind.Utc).ToLocalTime(), record.Id);
                            if (!byId.ContainsKey(entry.Id)) { entries.Add(entry); byId.Add(entry.Id, entry); }
                        }
                        validBytes += Utf8.GetByteCount(line) + 1;
                    }
                    addNewline = !repairTail && stream.Length > 0 && !EndsWithNewline(FilePath);
                }
                if (repairTail)
                    using (FileStream stream = new FileStream(FilePath, FileMode.Open, FileAccess.Write)) { stream.SetLength(validBytes); stream.Flush(true); }
                else if (addNewline)
                    using (FileStream stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write)) { stream.WriteByte(10); stream.Flush(true); }
            }
            ready = true;
            return entries;
        }
        private static bool EndsWithNewline(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            { if (stream.Length == 0) return true; stream.Seek(-1, SeekOrigin.End); return stream.ReadByte() == 10; }
        }
        public void Append(string operation, MqttLogEntry entry)
        {
            if (!ready) throw new IOException("MQTT geçmişi yüklenemediği için mevcut dosya korunuyor.");
            Record record = new Record { Operation = operation, Id = entry == null ? null : entry.Id };
            if (operation == "add")
            {
                record.Direction = entry.Direction; record.Topic = entry.Topic; record.Payload = entry.PayloadBase64;
                record.TimestampTicks = entry.Timestamp.ToUniversalTime().Ticks; record.Qos = entry.Qos; record.Retain = entry.Retain;
            }
            byte[] bytes;
            using (MemoryStream stream = new MemoryStream())
            { new DataContractJsonSerializer(typeof(Record)).WriteObject(stream, record); stream.WriteByte(10); bytes = stream.ToArray(); }
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            using (FileStream stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            {
                long start = stream.Length;
                stream.Position = start;
                try { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                catch { try { stream.SetLength(start); } catch { ready = false; } throw; }
            }
        }
    }
}
