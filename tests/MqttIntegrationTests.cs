using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using CommStudio;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Server;
using MqttSession = CommStudio.MqttSession;

internal static class MqttIntegrationTests
{
    private static int Main()
    {
        try { RunAsync().GetAwaiter().GetResult(); Console.WriteLine("MQTT loopback tests passed: MQTT 3.1.1/5, publish/receive, subscriptions, reconnect, rejected login, cancellation and TLS/WS options."); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private static async Task RunAsync()
    {
        TcpListener portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        MqttServerOptions serverOptions = new MqttServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointPort(port)
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointBoundIPV6Address(IPAddress.IPv6Loopback).Build();
        using (MqttServer server = new MqttFactory().CreateMqttServer(serverOptions))
        {
            server.ValidatingConnectionAsync += delegate(ValidatingConnectionEventArgs e)
            {
                if (e.UserName == "reject") e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                return Task.FromResult(0);
            };
            await server.StartAsync();
            foreach (string version in new string[] { "3.1.1", "5.0" })
            {
                MqttProfile profile = MqttProfile.CreateNew();
                profile.Host = "127.0.0.1"; profile.Port = port; profile.Version = version;
                profile.ConnectTimeout = 2; profile.ReconnectPeriod = 120;
                profile.Subscriptions.Add("commstudio/test/#");
                using (MqttSession session = new MqttSession())
                {
                    ConcurrentQueue<string> log = new ConcurrentQueue<string>();
                    ConcurrentQueue<byte[]> raw = new ConcurrentQueue<byte[]>();
                    session.PayloadLogged += delegate(string direction, string topic, byte[] bytes, int qos, bool retain)
                    { if (direction == "RX" && topic == "commstudio/test/binary") raw.Enqueue(bytes); };
                    session.MessageLogged += delegate(string direction, string topic, string message) { log.Enqueue(direction + "|" + topic + "|" + message); };
                    await session.StartAsync(profile);
                    Assert(session.IsConnected && session.IsActive, "connected using " + version);
                    Assert(session.Subscriptions.Contains("commstudio/test/#") && log.Contains("SYS|commstudio/test/#|Abone olundu."),
                        "saved profile subscriptions are registered on initial connection");
                    await session.SubscribeAsync("commstudio/test/#");
                    string payload = "Türkçe mesaj\nsecond line";
                    await session.PublishAsync("commstudio/test/hello", payload);
                    await WaitAsync(delegate { return log.Contains("RX|commstudio/test/hello|" + payload); }, "received exact UTF-8 multiline message");
                    Assert(log.Contains("TX|commstudio/test/hello|" + payload), "outgoing message logged only after publishing");
                    using (IMqttClient sender = new MqttFactory().CreateMqttClient())
                    {
                        await sender.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", port).Build());
                        byte[] binary = { 0, 255, 128, 65, 13, 10 };
                        await sender.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("commstudio/test/binary").WithPayload(binary).Build());
                        await WaitAsync(delegate { return raw.Any(x => x.SequenceEqual(binary)); }, "received binary bytes reach log unchanged");
                        await sender.DisconnectAsync(new MqttClientDisconnectOptions());
                    }
                    await session.UnsubscribeAsync("commstudio/test/#");
                    Assert(session.Subscriptions.Length == 0, "subscription removed after unsubscribe response");
                    await session.PublishAsync("commstudio/test/unsubscribed", "no echo");
                    await Task.Delay(150);
                    Assert(!log.Any(x => x.StartsWith("RX|commstudio/test/unsubscribed|")), "unsubscribed messages are no longer delivered");
                    await session.SubscribeAsync("commstudio/test/#");
                    int beforeReconnect = log.Count(x => x == "SYS|commstudio/test/#|Abone olundu.");
                    await server.StopAsync(new MqttServerStopOptions());
                    await WaitAsync(delegate { return !session.IsConnected; }, "detect broker shutdown");
                    await server.StartAsync();
                    await WaitAsync(delegate { return session.IsConnected && log.Count(x => x == "SYS|commstudio/test/#|Abone olundu.") > beforeReconnect; }, "reconnect restores topic subscriptions");
                    await session.PublishAsync("commstudio/test/reconnected", "again");
                    await WaitAsync(delegate { return log.Contains("RX|commstudio/test/reconnected|again"); }, "receive after automatic reconnect");
                    await session.StopAsync();
                    await Task.Delay(200);
                    Assert(!session.IsConnected && !session.IsActive, "manual disconnect does not auto-reconnect");
                }
            }
            using (MqttSession rejected = new MqttSession())
            {
                MqttProfile bad = MqttProfile.CreateNew(); bad.Host = "127.0.0.1"; bad.Port = port; bad.Username = "reject"; bad.ConnectTimeout = 2;
                bool failed = false;
                try { await rejected.StartAsync(bad); } catch (Exception) { failed = true; }
                Assert(failed && !rejected.IsConnected && !rejected.IsActive, "rejected credentials do not leave an active session");
            }
            await server.StopAsync(new MqttServerStopOptions());
        }

        TcpListener silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        try
        {
            using (MqttSession waiting = new MqttSession())
            {
                MqttProfile profile = MqttProfile.CreateNew(); profile.Host = "127.0.0.1";
                profile.Port = ((IPEndPoint)silent.LocalEndpoint).Port; profile.ConnectTimeout = 10;
                Task start = waiting.StartAsync(profile);
                using (TcpClient accepted = await silent.AcceptTcpClientAsync())
                {
                    await waiting.StopAsync();
                    Assert(await Task.WhenAny(start, Task.Delay(2500)) == start, "disconnect cancels a pending CONNACK wait");
                    try { await start; } catch (Exception) { }
                    Assert(!waiting.IsActive, "cancelled connection stays stopped");
                }
            }
        }
        finally { silent.Stop(); }

        MqttProfile tls = MqttProfile.CreateNew(); tls.Host = "broker.example.test"; tls.Port = 8883; tls.UseTls = true;
        MqttClientTcpOptions tcp = (MqttClientTcpOptions)MqttSession.BuildOptions(tls).ChannelOptions;
        Assert(tcp.TlsOptions.UseTls && !tcp.TlsOptions.AllowUntrustedCertificates && !tcp.TlsOptions.IgnoreCertificateChainErrors,
            "TLS validates certificates");
        tls.Scheme = "wss://"; tls.Port = 443; tls.Host = "broker.example.test/custom-mqtt";
        MqttClientWebSocketOptions websocket = (MqttClientWebSocketOptions)MqttSession.BuildOptions(tls).ChannelOptions;
        Assert(websocket.Uri.Contains("/custom-mqtt") && websocket.Uri.StartsWith("wss://"), "secure WebSocket path is honored");
        bool invalid = false;
        try { MqttSession.ValidateTopic("publish/#", false); } catch (ArgumentException) { invalid = true; }
        Assert(invalid, "publishing to a wildcard is rejected");
        invalid = false;
        try { MqttSession.ValidateTopic("filter/#/bad", true); } catch (ArgumentException) { invalid = true; }
        Assert(invalid, "invalid subscription filters are rejected");
    }
    private static async Task WaitAsync(Func<bool> condition, string message)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(6);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert(condition(), message);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
