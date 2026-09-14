using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommStudio;

internal static class BaudSwitchTests
{
    private static int Main()
    {
        try
        {
            RunAsync().GetAwaiter().GetResult();
            Console.WriteLine("Baud switching tests passed: ACK parsing, early USB flush, physical frame time, configurable delay, cancellation and failures.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static async Task RunAsync()
    {
        byte[] ack = ByteCodec.ParseAsciiCommand("[ACK]050[CR][LF]");
        Assert(SerialBaudControl.GetIecTargetBaud(ack) == 9600, "Luna example selects 9600");
        Assert(SerialBaudControl.GetIecTargetBaud(ByteCodec.ParseHexCommand("06 30 35 30 0D 0A")) == 9600, "Hex ACK supported");
        int[] rates = { 300, 600, 1200, 2400, 4800, 9600, 19200 };
        for (int i = 0; i < rates.Length; i++)
            Assert(SerialBaudControl.GetIecTargetBaud(ByteCodec.ParseAsciiCommand("[ACK]0" + i + "1[CR][LF]")) == rates[i], "mode C speed code " + i);
        foreach (string invalid in new string[] { "[ACK]", "[ACK]070[CR][LF]", "[ACK]050[CR]", "[ACK]050[CR][LF]extra", "[ACK]250[CR][LF]", "[ACK]053[CR][LF]" })
            Assert(SerialBaudControl.GetIecTargetBaud(ByteCodec.ParseAsciiCommand(invalid)) == 0, "unrelated/incomplete commands unchanged");

        FakePort port = new FakePort();
        Task operation = SerialBaudControl.SendAndChangeAsync(port, ack, 9600, CancellationToken.None, 2000);
        await WaitForDrain(port);
        Assert(port.BaudRate == 300 && !operation.IsCompleted, "baud stays 300 while ACK is still transmitting");
        port.DrainGate.Set();
        await operation;
        port.Write(new byte[] { 0x41 });
        Assert(string.Join(",", port.Events) == "write:300,drain,baud:9600,write:9600", "ACK at 300, drain, switch, later commands at 9600");

        // Reproduce an adapter whose Write and Flush return before the 200 ms ACK has left the wire.
        port = new FakePort();
        port.DrainGate.Set();
        await SerialBaudControl.SendAndChangeAsync(port, ack, 9600, CancellationToken.None, 2000);
        Assert(port.SwitchedAfterMs >= 250, "early USB drain cannot cause a premature baud switch");

        port = new FakePort();
        port.DrainGate.Set();
        await SerialBaudControl.SendAndChangeAsync(port, ack, 9600, CancellationToken.None, 2000, 0);
        Assert(port.SwitchedAfterMs >= 200, "zero extra delay still respects 6-byte 7E1 frame time at 300 baud");

        port = new FakePort();
        port.DrainGate.Set();
        await SerialBaudControl.SendAndChangeAsync(port, ack, 9600, CancellationToken.None, 2000, 350);
        Assert(port.SwitchedAfterMs >= 350, "configured minimum measured from write start");

        port = new FakePort { FrameBits = 11 };
        port.DrainGate.Set();
        await SerialBaudControl.SendAndChangeAsync(port, ack, 9600, CancellationToken.None, 2000, 0);
        Assert(port.SwitchedAfterMs >= 220, "frame time respects parity and stop bits");

        port = new FakePort();
        port.DrainGate.Set();
        await SerialBaudControl.SendAndChangeAsync(port, null, 1200, CancellationToken.None, 2000);
        Assert(string.Join(",", port.Events) == "drain,baud:1200", "manual change does not transmit a command");

        port = new FakePort { FailDrain = true };
        try { await SerialBaudControl.SendAndChangeAsync(port, ack, 9600, CancellationToken.None, 2000); throw new Exception("Expected drain failure"); }
        catch (IOException) { }
        Assert(port.BaudRate == 300, "failed transmission never switches baud");

        port = new FakePort();
        try { await SerialBaudControl.SendAndChangeAsync(port, ack, 9600, CancellationToken.None, 100); throw new Exception("Expected timeout"); }
        catch (TimeoutException) { }
        Assert(port.CancelCount == 1 && port.BaudRate == 300, "stalled drain is aborted without switching");

        port = new FakePort();
        using (CancellationTokenSource cancel = new CancellationTokenSource())
        {
            operation = SerialBaudControl.SendAndChangeAsync(port, ack, 9600, cancel.Token, 2000);
            await WaitForDrain(port);
            cancel.Cancel();
            try { await operation; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
            Assert(port.CancelCount == 1 && port.BaudRate == 300, "disconnect cancels pending change");
        }

        port = new FakePort();
        port.DrainGate.Set();
        using (CancellationTokenSource cancel = new CancellationTokenSource())
        {
            operation = SerialBaudControl.SendAndChangeAsync(port, ack, 9600, cancel.Token, 2000, 1000);
            await WaitForDrain(port);
            cancel.Cancel();
            try { await operation; throw new Exception("Expected guard cancellation"); } catch (OperationCanceledException) { }
            Assert(port.BaudRate == 300, "disconnect during timing guard prevents delayed baud change");
        }
    }

    private static async Task WaitForDrain(FakePort port)
    {
        Assert(await Task.WhenAny(port.Draining.Task, Task.Delay(2000)) == port.Draining.Task, "worker reaches drain");
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    private sealed class FakePort : ISerialBaudPort
    {
        public readonly ManualResetEventSlim DrainGate = new ManualResetEventSlim(false);
        public readonly TaskCompletionSource<bool> Draining = new TaskCompletionSource<bool>();
        public readonly List<string> Events = new List<string>();
        public bool FailDrain;
        public int CancelCount;
        public double FrameBits = 10;
        public double BitsPerCharacter { get { return FrameBits; } }
        private Stopwatch transmittedAt;
        public double SwitchedAfterMs;
        private int baud = 300;
        public int BaudRate { get { return baud; } set { SwitchedAfterMs = transmittedAt == null ? 0 : transmittedAt.Elapsed.TotalMilliseconds; baud = value; Events.Add("baud:" + value); } }
        public void Write(byte[] data) { transmittedAt = Stopwatch.StartNew(); Events.Add("write:" + baud); }
        public void DrainOutput()
        {
            Events.Add("drain");
            Draining.TrySetResult(true);
            if (FailDrain) throw new IOException("Simulated write failure");
            DrainGate.Wait();
        }
        public void CancelOutput() { CancelCount++; DrainGate.Set(); }
    }
}
