using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace CommStudio
{
    internal sealed class MqttSession : IDisposable
    {
        private IMqttClient client;
        private CancellationTokenSource lifetime;
        private MqttProfile profile;
        private MqttClientOptions options;
        private readonly List<string> subscriptions = new List<string>();
        private readonly object subscriptionSync = new object();
        private int reconnecting;
        private Task reconnectTask;
        public event Action StateChanged;
        public event Action<string, string, string> MessageLogged;
        public event Action<string, string, byte[], int, bool> PayloadLogged;
        public bool IsConnected { get { IMqttClient active = client; return active != null && active.IsConnected; } }
        public bool IsActive { get { CancellationTokenSource active = lifetime; return active != null && !active.IsCancellationRequested; } }
        public bool IsReconnecting { get { return reconnecting != 0; } }
        public string[] Subscriptions { get { lock (subscriptionSync) return subscriptions.ToArray(); } }

        public static MqttClientOptions BuildOptions(MqttProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.Host)) throw new ArgumentException("MQTT Host alanını doldurun.");
            if (profile.Port < 1 || profile.Port > 65535) throw new ArgumentException("Port 1–65535 aralığında olmalıdır.");
            if (!string.IsNullOrWhiteSpace(profile.AuthenticationMethod))
                throw new NotSupportedException("Özel Authentication Method henüz desteklenmiyor. Standart bağlantı için bu alanı boş bırakıp Username / Password kullanın.");
            MqttClientOptionsBuilder builder = new MqttClientOptionsBuilder()
                .WithClientId(profile.ClientId)
                .WithProtocolVersion(profile.Version == "3.1.1" ? MqttProtocolVersion.V311 : MqttProtocolVersion.V500)
                .WithCleanSession(profile.CleanStart)
                .WithTimeout(TimeSpan.FromSeconds(profile.ConnectTimeout))
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(profile.KeepAlive));
            bool websocket = profile.Scheme == "ws://" || profile.Scheme == "wss://";
            if (websocket)
            {
                UriBuilder uri = new UriBuilder(new Uri((profile.UseTls ? "wss://" : "ws://") + profile.Host.Trim()));
                uri.Port = profile.Port;
                if (uri.Path == "/") uri.Path = "/mqtt";
                builder.WithWebSocketServer(delegate(MqttClientWebSocketOptionsBuilder ws) { ws.WithUri(uri.Uri.AbsoluteUri); });
            }
            else
            {
                if (profile.Host.Contains("://") || profile.Host.Contains("/"))
                    throw new ArgumentException("TCP MQTT için Host alanına yalnızca sunucu adını veya IP adresini girin.");
                builder.WithTcpServer(profile.Host.Trim(), profile.Port);
            }
            if (profile.UseTls)
                builder.WithTlsOptions(delegate(MqttClientTlsOptionsBuilder tls) { tls.UseTls(true); });
            if (!string.IsNullOrEmpty(profile.Username) || !string.IsNullOrEmpty(profile.Password))
                builder.WithCredentials(profile.Username ?? "", profile.Password ?? "");
            if (!string.IsNullOrEmpty(profile.WillTopic))
            {
                ValidateTopic(profile.WillTopic, false);
                builder.WithWillTopic(profile.WillTopic).WithWillPayload(profile.WillPayload ?? "")
                    .WithWillQualityOfServiceLevel((MqttQualityOfServiceLevel)profile.WillQos).WithWillRetain(profile.WillRetain);
            }
            if (profile.Version != "3.1.1")
            {
                builder.WithSessionExpiryInterval((uint)profile.SessionExpiryInterval)
                    .WithRequestResponseInformation(profile.RequestResponseInfo).WithRequestProblemInformation(profile.RequestProblemInfo);
                if (profile.ReceiveMaximum.HasValue) builder.WithReceiveMaximum((ushort)profile.ReceiveMaximum.Value);
                if (profile.MaximumPacketSize.HasValue) builder.WithMaximumPacketSize((uint)profile.MaximumPacketSize.Value);
                if (profile.TopicAliasMaximum.HasValue) builder.WithTopicAliasMaximum((ushort)profile.TopicAliasMaximum.Value);
                if (!string.IsNullOrEmpty(profile.WillTopic))
                    builder.WithWillDelayInterval((uint)profile.WillDelayInterval)
                        .WithWillPayloadFormatIndicator(profile.PayloadFormatIndicator ? MqttPayloadFormatIndicator.CharacterData : MqttPayloadFormatIndicator.Unspecified);
                if (profile.UserProperties != null)
                    foreach (MqttUserProperty property in profile.UserProperties) builder.WithUserProperty(property.Key, property.Value);
            }
            return builder.Build();
        }

        public async Task StartAsync(MqttProfile selected)
        {
            if (IsActive) throw new InvalidOperationException("MQTT bağlantısı zaten etkin.");
            await StopAsync();
            profile = selected.Copy();
            options = BuildOptions(profile);
            CancellationTokenSource session = new CancellationTokenSource();
            CancellationToken token = session.Token;
            IMqttClient active = new MqttFactory().CreateMqttClient();
            lifetime = session;
            client = active;
            lock (subscriptionSync)
            {
                subscriptions.Clear();
                foreach (string topic in profile.Subscriptions)
                    if (!subscriptions.Contains(topic)) subscriptions.Add(topic);
            }
            active.ApplicationMessageReceivedAsync += delegate(MqttApplicationMessageReceivedEventArgs e)
            {
                if (client == active && !token.IsCancellationRequested)
                {
                    ArraySegment<byte> bytes = e.ApplicationMessage.PayloadSegment;
                    byte[] payload = new byte[bytes.Count];
                    if (bytes.Array != null) Buffer.BlockCopy(bytes.Array, bytes.Offset, payload, 0, bytes.Count);
                    LogPayload("RX", e.ApplicationMessage.Topic, payload, (int)e.ApplicationMessage.QualityOfServiceLevel, e.ApplicationMessage.Retain);
                }
                return Task.FromResult(0);
            };
            active.DisconnectedAsync += delegate(MqttClientDisconnectedEventArgs e)
            {
                if (client == active && !token.IsCancellationRequested && e.ClientWasConnected)
                {
                    Log("SYS", "", "MQTT bağlantısı kesildi: " + e.Reason);
                    if (profile.AutoReconnect && Interlocked.CompareExchange(ref reconnecting, 1, 0) == 0)
                        reconnectTask = Task.Run(delegate { return ReconnectAsync(active, token); });
                    else if (!profile.AutoReconnect) session.Cancel();
                    Changed();
                }
                return Task.FromResult(0);
            };
            Changed();
            Log("SYS", "", "MQTT bağlantısı açılıyor: " + profile.Host + ":" + profile.Port);
            try
            {
                await ConnectOnceAsync(active, token);
            }
            catch
            {
                if (client == active) { Dispose(); Changed(); }
                throw;
            }
        }

        private async Task ConnectOnceAsync(IMqttClient active, CancellationToken token)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(profile.ConnectTimeout));
                MqttClientConnectResult result = await active.ConnectAsync(options, timeout.Token);
                if (result.ResultCode != MqttClientConnectResultCode.Success)
                    throw new InvalidOperationException("MQTT bağlantısı reddedildi: " + result.ResultCode);
            }
            token.ThrowIfCancellationRequested();
            if (client != active) return;
            Log("SYS", "", "MQTT bağlantısı kuruldu.");
            foreach (string topic in Subscriptions)
            {
                try { await SubscribeCoreAsync(active, topic, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    lock (subscriptionSync) subscriptions.Remove(topic);
                    Log("ERR", topic, "Abonelik yenilenemedi: " + exception.Message);
                }
            }
            Changed();
        }

        private async Task ReconnectAsync(IMqttClient active, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && client == active)
                {
                    await Task.Delay(Math.Max(1, profile.ReconnectPeriod), token);
                    try { await ConnectOnceAsync(active, token); return; }
                    catch (OperationCanceledException) { if (token.IsCancellationRequested) return; }
                    catch (Exception exception) { Log("ERR", "", "Yeniden bağlanılamadı: " + exception.Message); }
                }
            }
            catch (OperationCanceledException) { }
            finally { Interlocked.Exchange(ref reconnecting, 0); Changed(); }
        }

        public async Task SubscribeAsync(string topic)
        {
            ValidateTopic(topic, true);
            IMqttClient active = RequireClient();
            lock (subscriptionSync) if (subscriptions.Contains(topic)) return;
            await SubscribeCoreAsync(active, topic, lifetime.Token);
            if (client != active) return;
            lock (subscriptionSync) if (!subscriptions.Contains(topic)) subscriptions.Add(topic);
            Changed();
        }

        private async Task SubscribeCoreAsync(IMqttClient active, string topic, CancellationToken token)
        {
            MqttClientSubscribeOptions request = new MqttClientSubscribeOptionsBuilder().WithTopicFilter(topic).Build();
            MqttClientSubscribeResult result = await active.SubscribeAsync(request, token);
            foreach (MqttClientSubscribeResultItem item in result.Items)
                if ((int)item.ResultCode >= 128) throw new InvalidOperationException("Abonelik reddedildi: " + item.ResultCode);
            Log("SYS", topic, "Abone olundu.");
        }

        public async Task UnsubscribeAsync(string topic)
        {
            IMqttClient active = RequireClient();
            MqttClientUnsubscribeResult result = await active.UnsubscribeAsync(
                new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(topic).Build(), lifetime.Token);
            foreach (MqttClientUnsubscribeResultItem item in result.Items)
                if ((int)item.ResultCode >= 128) throw new InvalidOperationException("Abonelik kaldırılamadı: " + item.ResultCode);
            lock (subscriptionSync) subscriptions.Remove(topic);
            Log("SYS", topic, "Abonelik kaldırıldı.");
            Changed();
        }

        public async Task PublishAsync(string topic, string text)
        {
            ValidateTopic(topic, false);
            IMqttClient active = RequireClient();
            MqttApplicationMessage message = new MqttApplicationMessageBuilder().WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(text ?? "")).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).Build();
            MqttClientPublishResult result = await active.PublishAsync(message, lifetime.Token);
            if (!result.IsSuccess) throw new InvalidOperationException("Mesaj gönderilemedi: " + result.ReasonCode);
            LogPayload("TX", topic, Encoding.UTF8.GetBytes(text ?? ""), 0, false);
        }

        private IMqttClient RequireClient()
        {
            IMqttClient active = client;
            if (active == null || !active.IsConnected) throw new InvalidOperationException("Önce MQTT sunucusuna bağlanın.");
            return active;
        }

        public static void ValidateTopic(string topic, bool filter)
        {
            if (string.IsNullOrEmpty(topic) || topic.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(topic) > 65535)
                throw new ArgumentException("Geçerli bir topic girin (1–65535 UTF-8 bayt).");
            if (!filter && (topic.Contains("+") || topic.Contains("#")))
                throw new ArgumentException("Publish topic, + veya # joker karakterlerini içeremez.");
            if (filter)
            {
                string[] levels = topic.Split('/');
                for (int i = 0; i < levels.Length; i++)
                    if ((levels[i].Contains("+") && levels[i] != "+") ||
                        (levels[i].Contains("#") && (levels[i] != "#" || i != levels.Length - 1)))
                        throw new ArgumentException("+ tek bir topic seviyesini, # ise yalnızca son seviyeyi kaplamalıdır.");
            }
        }

        public async Task StopAsync()
        {
            CancellationTokenSource session = lifetime;
            IMqttClient active = client;
            Task pendingReconnect = reconnectTask;
            lifetime = null;
            client = null;
            if (session != null) session.Cancel();
            if (active != null)
            {
                try
                {
                    if (active.IsConnected)
                        using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                            await active.DisconnectAsync(new MqttClientDisconnectOptions(), timeout.Token);
                }
                catch (Exception) { }
                finally { active.Dispose(); }
            }
            if (pendingReconnect != null) await pendingReconnect;
            reconnectTask = null;
            if (session != null) session.Dispose();
            Changed();
        }

        public void Dispose()
        {
            CancellationTokenSource session = lifetime;
            lifetime = null;
            if (session != null) session.Cancel();
            IMqttClient active = client;
            client = null;
            if (active != null) active.Dispose();
            if (session != null) session.Dispose();
        }
        private void Log(string direction, string topic, string text)
        { Action<string, string, string> handler = MessageLogged; if (handler != null) handler(direction, topic, text); }
        private void LogPayload(string direction, string topic, byte[] payload, int qos, bool retain)
        {
            Action<string, string, byte[], int, bool> handler = PayloadLogged;
            if (handler != null) handler(direction, topic, payload, qos, retain);
            Log(direction, topic, Encoding.UTF8.GetString(payload));
        }
        private void Changed() { Action handler = StateChanged; if (handler != null) handler(); }
    }
}
