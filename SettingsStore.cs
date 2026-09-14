using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace CommStudio
{
    internal static class SettingsStore
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommStudio");

        private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return CreateDefaults();
                }

                using (FileStream stream = File.OpenRead(SettingsPath))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    AppSettings settings = serializer.ReadObject(stream) as AppSettings;
                    return Normalize(settings);
                }
            }
            catch
            {
                return CreateDefaults();
            }
        }

        public static void Save(AppSettings settings)
        {
            Directory.CreateDirectory(SettingsDirectory);
            string temporaryPath = SettingsPath + ".tmp";
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));

            using (FileStream stream = File.Create(temporaryPath))
            {
                serializer.WriteObject(stream, settings);
            }

            if (File.Exists(SettingsPath))
            {
                File.Replace(temporaryPath, SettingsPath, null);
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }
        }

        private static AppSettings Normalize(AppSettings settings)
        {
            if (settings == null)
            {
                return CreateDefaults();
            }

            if (string.IsNullOrWhiteSpace(settings.Host))
            {
                settings.Host = "localhost";
            }

            if (settings.Port < 1 || settings.Port > 65535)
            {
                settings.Port = 2404;
            }

            if (settings.WindowWidth < 720)
            {
                settings.WindowWidth = 1000;
            }

            if (settings.WindowHeight < 520)
            {
                settings.WindowHeight = 720;
            }

            if (settings.LogFontSize < 7F || settings.LogFontSize > 24F)
            {
                settings.LogFontSize = 9.5F;
            }

            if (settings.CommandPanelHeight < 120)
            {
                settings.CommandPanelHeight = 238;
            }

            if (settings.Commands == null)
            {
                settings.Commands = new System.Collections.Generic.List<CommandSetting>();
            }

            settings.ConnectionType = settings.ConnectionType == "MQTT" ? "MQTT" : settings.ConnectionType == "Serial" ? "Serial" : "TCP";
            if (settings.MqttLogFormat != "Json" && settings.MqttLogFormat != "Hex") settings.MqttLogFormat = "PlainText";
            if (!settings.IecBaudSwitchDelayMs.HasValue || settings.IecBaudSwitchDelayMs < 0 || settings.IecBaudSwitchDelayMs > 1000)
                settings.IecBaudSwitchDelayMs = 250;
            settings.SerialPortName = settings.SerialPortName ?? string.Empty;
            if (settings.BaudRate < 1 || settings.BaudRate > 4000000) settings.BaudRate = 9600;
            if (settings.DataBits < 5 || settings.DataBits > 8) settings.DataBits = 8;
            if (!IsNamedEnum<System.IO.Ports.Parity>(settings.SerialParity)) settings.SerialParity = "None";
            if (!IsNamedEnum<System.IO.Ports.StopBits>(settings.SerialStopBits) || settings.SerialStopBits == "None")
                settings.SerialStopBits = "One";
            if (!IsNamedEnum<System.IO.Ports.Handshake>(settings.SerialHandshake)) settings.SerialHandshake = "None";

            while (settings.Commands.Count < 3)
            {
                settings.Commands.Add(new CommandSetting { Text = string.Empty, IsHex = false });
            }

            return settings;
        }

        private static AppSettings CreateDefaults()
        {
            return Normalize(new AppSettings());
        }

        private static bool IsNamedEnum<T>(string value)
        {
            return value != null && Array.IndexOf(Enum.GetNames(typeof(T)), value) >= 0;
        }
    }
}
