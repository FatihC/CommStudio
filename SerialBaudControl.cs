using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace CommStudio
{
    internal interface ISerialBaudPort
    {
        int BaudRate { get; set; }
        double BitsPerCharacter { get; }
        void Write(byte[] data);
        void DrainOutput();
        void CancelOutput();
    }

    internal sealed class SerialBaudPort : ISerialBaudPort
    {
        private readonly SerialPort port;
        public SerialBaudPort(SerialPort port) { this.port = port; }
        public int BaudRate { get { return port.BaudRate; } set { port.BaudRate = value; } }
        public double BitsPerCharacter
        {
            get { return 1 + port.DataBits + (port.Parity == Parity.None ? 0 : 1) +
                (port.StopBits == StopBits.Two ? 2 : port.StopBits == StopBits.OnePointFive ? 1.5 : 1); }
        }
        public void Write(byte[] data) { port.Write(data, 0, data.Length); }
        public void DrainOutput() { port.BaseStream.Flush(); }
        public void CancelOutput() { port.DiscardOutBuffer(); }
    }

    internal static class SerialBaudControl
    {
        public static int GetIecTargetBaud(byte[] data)
        {
            // Only mode C readout/programming ACKs; mode E also changes framing and is not handled here.
            if (data == null || data.Length != 6 || data[0] != 0x06 || data[1] != '0' ||
                data[2] < '0' || data[2] > '6' || (data[3] != '0' && data[3] != '1') ||
                data[4] != 0x0D || data[5] != 0x0A) return 0;
            return new int[] { 300, 600, 1200, 2400, 4800, 9600, 19200 }[data[2] - '0'];
        }

        public static async Task SendAndChangeAsync(ISerialBaudPort port, byte[] data, int targetBaud,
            CancellationToken cancellationToken, int timeoutMilliseconds, int minimumSwitchDelayMilliseconds = 250)
        {
            if (minimumSwitchDelayMilliseconds < 0 || minimumSwitchDelayMilliseconds > 1000)
                throw new ArgumentOutOfRangeException("minimumSwitchDelayMilliseconds");
            using (CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                CancellationToken token = operation.Token;
                Task work = Task.Run(delegate
                {
                    token.ThrowIfCancellationRequested();
                    double minimumTransmitMs = data == null ? 0 : Math.Max(minimumSwitchDelayMilliseconds,
                        Math.Ceiling(1000.0 * data.Length * port.BitsPerCharacter / port.BaudRate));
                    Stopwatch transmitTime = Stopwatch.StartNew();
                    if (data != null) port.Write(data);
                    // USB drivers can report Flush complete while the adapter is still transmitting.
                    // Wait at least the frame's wire time, or the configured minimum from Write start.
                    port.DrainOutput();
                    while (transmitTime.Elapsed.TotalMilliseconds < minimumTransmitMs)
                    {
                        int remaining = (int)Math.Ceiling(minimumTransmitMs - transmitTime.Elapsed.TotalMilliseconds);
                        if (remaining > 0) token.WaitHandle.WaitOne(remaining);
                        token.ThrowIfCancellationRequested();
                    }
                    token.ThrowIfCancellationRequested();
                    port.BaudRate = targetBaud;
                });
                Task deadline = Task.Delay(timeoutMilliseconds, token);
                Task completed = await Task.WhenAny(work, deadline);
                if (completed == work)
                {
                    operation.Cancel();
                    await work;
                    return;
                }

                operation.Cancel();
                // A flow-controlled Flush has no driver write timeout. Purging aborts it on failure only.
                try { port.CancelOutput(); } catch { }
                ObserveFailureAsync(work);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("Serial transmission did not finish before the baud-change timeout. The connection will close; retry from the meter's initial baud rate.");
            }
        }

        private static async void ObserveFailureAsync(Task work)
        {
            try { await work; } catch { }
        }
    }
}
