using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CommStudio
{
    internal enum LogDirection
    {
        Sent,
        Received,
        System,
        Error
    }

    internal enum DisplayMode
    {
        Ascii,
        Hex
    }

    internal sealed class LogEntry
    {
        public DateTime Timestamp { get; private set; }
        public LogDirection Direction { get; private set; }
        public byte[] Data { get; private set; }
        public string Text { get; private set; }

        private LogEntry(DateTime timestamp, LogDirection direction, byte[] data, string text)
        {
            Timestamp = timestamp;
            Direction = direction;
            Data = data;
            Text = text;
        }

        public static LogEntry Message(LogDirection direction, byte[] data)
        {
            return new LogEntry(DateTime.Now, direction, data, null);
        }

        public static LogEntry Notice(LogDirection direction, string text)
        {
            return new LogEntry(DateTime.Now, direction, null, text);
        }
    }

    [DataContract]
    internal sealed class CommandSetting
    {
        [DataMember(Order = 1)]
        public string Text { get; set; }

        [DataMember(Order = 2)]
        public bool IsHex { get; set; }
    }

    [DataContract]
    internal sealed class AppSettings
    {
        public AppSettings()
        {
            Host = "localhost";
            Port = 2404;
            WindowWidth = 1000;
            WindowHeight = 720;
            Commands = new List<CommandSetting>();
            DisplayMode = "Ascii";
            LogFontSize = 9.5F;
            CommandPanelHeight = 238;
            IsDarkMode = false;
            ConnectionType = "TCP";
            SerialPortName = string.Empty;
            BaudRate = 9600;
            DataBits = 8;
            SerialParity = "None";
            SerialStopBits = "One";
            SerialHandshake = "None";
            MqttLogFormat = "PlainText";
        }

        [DataMember(Order = 1)]
        public string Host { get; set; }

        [DataMember(Order = 2)]
        public int Port { get; set; }

        [DataMember(Order = 3)]
        public int WindowWidth { get; set; }

        [DataMember(Order = 4)]
        public int WindowHeight { get; set; }

        [DataMember(Order = 5)]
        public int WindowX { get; set; }

        [DataMember(Order = 6)]
        public int WindowY { get; set; }

        [DataMember(Order = 7)]
        public bool HasWindowPosition { get; set; }

        [DataMember(Order = 8)]
        public bool IsMaximized { get; set; }

        [DataMember(Order = 9)]
        public string DisplayMode { get; set; }

        [DataMember(Order = 10)]
        public List<CommandSetting> Commands { get; set; }

        [DataMember(Order = 11)]
        public float LogFontSize { get; set; }

        [DataMember(Order = 12)]
        public int CommandPanelHeight { get; set; }

        [DataMember(Order = 13)]
        public bool IsDarkMode { get; set; }

        [DataMember(Order = 14)]
        public string ConnectionType { get; set; }
        [DataMember(Order = 15)]
        public string SerialPortName { get; set; }
        [DataMember(Order = 16)]
        public int BaudRate { get; set; }
        [DataMember(Order = 17)]
        public int DataBits { get; set; }
        [DataMember(Order = 18)]
        public string SerialParity { get; set; }
        [DataMember(Order = 19)]
        public string SerialStopBits { get; set; }
        [DataMember(Order = 20)]
        public string SerialHandshake { get; set; }
        [DataMember(Order = 21)]
        public bool DtrEnabled { get; set; }
        [DataMember(Order = 22)]
        public bool RtsEnabled { get; set; }

        [DataMember(Order = 23)]
        public bool LogWordWrap { get; set; }

        [DataMember(Order = 24)]
        public bool AutoIecBaudSwitch { get; set; }

        [DataMember(Order = 25)]
        public int? IecBaudSwitchDelayMs { get; set; }

        [DataMember(Order = 26)]
        public string MqttLogFormat { get; set; }
    }
}
