using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CommStudio
{
    internal static class ByteCodec
    {
        private static readonly string[] ControlNames =
        {
            "NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL",
            "BS", "HT", "LF", "VT", "FF", "CR", "SO", "SI",
            "DLE", "DC1", "DC2", "DC3", "DC4", "NAK", "SYN", "ETB",
            "CAN", "EM", "SUB", "ESC", "FS", "GS", "RS", "US"
        };

        private static readonly Dictionary<string, byte> NamedBytes = CreateNamedBytes();

        private static Dictionary<string, byte> CreateNamedBytes()
        {
            Dictionary<string, byte> result = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ControlNames.Length; i++)
            {
                result[ControlNames[i]] = (byte)i;
            }

            result["TAB"] = 0x09;
            result["DEL"] = 0x7F;
            return result;
        }

        public static byte[] ParseAsciiCommand(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return new byte[0];
            }

            using (MemoryStream output = new MemoryStream())
            {
                int index = 0;
                while (index < input.Length)
                {
                    if (input[index] == '[')
                    {
                        int closing = input.IndexOf(']', index + 1);
                        if (closing > index + 1)
                        {
                            string token = input.Substring(index + 1, closing - index - 1);
                            byte tokenValue;
                            if (TryParseAsciiToken(token, out tokenValue))
                            {
                                output.WriteByte(tokenValue);
                                index = closing + 1;
                                continue;
                            }
                        }
                    }

                    char current = input[index];
                    output.WriteByte(current <= 0x7F ? (byte)current : (byte)'?');
                    index++;
                }

                return output.ToArray();
            }
        }

        private static bool TryParseAsciiToken(string token, out byte value)
        {
            if (NamedBytes.TryGetValue(token, out value))
            {
                return true;
            }

            string candidate = token;
            if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.Substring(2);
            }

            return candidate.Length == 2 && byte.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        public static byte[] ParseHexCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new byte[0];
            }

            StringBuilder digits = new StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                char current = input[i];
                if (current == '0' && i + 1 < input.Length && (input[i + 1] == 'x' || input[i + 1] == 'X'))
                {
                    i++;
                    continue;
                }

                if (Uri.IsHexDigit(current))
                {
                    digits.Append(current);
                    continue;
                }

                if (char.IsWhiteSpace(current) || current == ',' || current == ';' || current == '-' || current == ':')
                {
                    continue;
                }

                throw new FormatException("Hex input contains an invalid character: '" + current + "'.");
            }

            if (digits.Length % 2 != 0)
            {
                throw new FormatException("Hex input must contain complete byte pairs (for example: 06 41 0D). ");
            }

            byte[] result = new byte[digits.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = byte.Parse(digits.ToString(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return result;
        }

        public static string ToHex(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            return BitConverter.ToString(data).Replace('-', ' ');
        }

        public static string ToAscii(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder output = new StringBuilder();
            foreach (byte value in data)
            {
                if (value >= 0x20 && value <= 0x7E)
                {
                    output.Append((char)value);
                }
                else if (value <= 0x1F)
                {
                    output.Append('[').Append(ControlNames[value]).Append(']');
                }
                else if (value == 0x7F)
                {
                    output.Append("[DEL]");
                }
                else
                {
                    output.Append('[').Append(value.ToString("X2", CultureInfo.InvariantCulture)).Append(']');
                }
            }

            return output.ToString();
        }
    }
}
