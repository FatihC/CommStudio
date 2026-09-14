using System;
using System.Drawing;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed class JsonValidationError
    {
        public int Offset;
        public int Length;
        public int Line;
        public int Column;
        public string Message;
    }

    // Validate syntax without deserializing or rewriting the payload. Offsets are UTF-16,
    // matching the native editor even when the JSON contains non-ASCII text.
    internal sealed class JsonSyntaxValidator
    {
        private readonly string text;
        private int position;
        private JsonValidationError error;
        private JsonSyntaxValidator(string value) { text = value ?? ""; }

        public static JsonValidationError Validate(string text)
        {
            JsonSyntaxValidator parser = new JsonSyntaxValidator(text);
            if (parser.Value(0))
            {
                parser.WhiteSpace();
                if (parser.position != parser.text.Length) parser.Fail("JSON değerinden sonra beklenmeyen metin.");
            }
            return parser.error;
        }

        private bool Fail(string message)
        {
            int line = 1, column = 1;
            for (int i = 0; i < position; i++)
            {
                if (text[i] == '\r') { line++; column = 1; }
                else if (text[i] == '\n') { if (i == 0 || text[i - 1] != '\r') line++; column = 1; }
                else column++;
            }
            int length = 1;
            if (position < text.Length && char.IsLetter(text[position]))
                while (position + length < text.Length && char.IsLetterOrDigit(text[position + length])) length++;
            error = new JsonValidationError { Offset = position, Length = length, Line = line, Column = column, Message = message };
            return false;
        }
        private void WhiteSpace()
        { while (position < text.Length && (text[position] == ' ' || text[position] == '\t' || text[position] == '\r' || text[position] == '\n')) position++; }
        private bool Take(char value)
        { if (position < text.Length && text[position] == value) { position++; return true; } return false; }
        private bool Value(int depth)
        {
            WhiteSpace();
            if (depth > 256) return Fail("JSON iç içe geçme sınırı (256) aşıldı.");
            if (position == text.Length) return Fail("JSON değeri bekleniyor.");
            char current = text[position];
            if (current == '"') return String();
            if (current == '{') return Object(depth + 1);
            if (current == '[') return Array(depth + 1);
            if (current == 't') return Literal("true");
            if (current == 'f') return Literal("false");
            if (current == 'n') return Literal("null");
            if (current == '-' || Digit(current)) return Number();
            return Fail("Geçerli bir JSON değeri bekleniyor.");
        }
        private bool Object(int depth)
        {
            position++; WhiteSpace();
            if (Take('}')) return true;
            while (true)
            {
                if (position == text.Length || text[position] != '"') return Fail("Alan adı çift tırnak içinde olmalı.");
                if (!String()) return false;
                WhiteSpace();
                if (!Take(':')) return Fail("Alan adından sonra ':' bekleniyor.");
                if (!Value(depth)) return false;
                WhiteSpace();
                if (Take('}')) return true;
                if (!Take(',')) return Fail("',' veya '}' bekleniyor.");
                WhiteSpace();
            }
        }
        private bool Array(int depth)
        {
            position++; WhiteSpace();
            if (Take(']')) return true;
            while (true)
            {
                if (!Value(depth)) return false;
                WhiteSpace();
                if (Take(']')) return true;
                if (!Take(',')) return Fail("',' veya ']' bekleniyor.");
            }
        }
        private bool String()
        {
            position++;
            while (position < text.Length)
            {
                char current = text[position];
                if (current == '"') { position++; return true; }
                if (current < 0x20) return Fail("Metin içinde kontrol karakteri kullanılamaz; kaçış dizisi kullanın.");
                position++;
                if (current != '\\') continue;
                if (position == text.Length) return Fail("Tamamlanmamış kaçış dizisi.");
                char escaped = text[position];
                if (escaped == 'u')
                {
                    position++;
                    for (int i = 0; i < 4; i++)
                    {
                        if (position == text.Length || !Hex(text[position])) return Fail("\\u sonrasında dört onaltılık basamak bekleniyor.");
                        position++;
                    }
                }
                else if ("\"\\/bfnrt".IndexOf(escaped) >= 0) position++;
                else return Fail("Geçersiz kaçış dizisi.");
            }
            return Fail("Metni kapatan çift tırnak eksik.");
        }
        private bool Literal(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (position == text.Length || text[position] != value[i]) return Fail("'" + value + "' bekleniyor.");
                position++;
            }
            return true;
        }
        private static bool Digit(char value) { return value >= '0' && value <= '9'; }
        private static bool Hex(char value) { return Digit(value) || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F'); }
        private bool Number()
        {
            Take('-');
            if (!Take('0'))
            {
                if (position == text.Length || !Digit(text[position])) return Fail("Sayı basamağı bekleniyor.");
                while (position < text.Length && Digit(text[position])) position++;
            }
            if (Take('.'))
            {
                if (position == text.Length || !Digit(text[position])) return Fail("Ondalık basamak bekleniyor.");
                while (position < text.Length && Digit(text[position])) position++;
            }
            if (Take('e') || Take('E'))
            {
                if (!Take('+')) Take('-');
                if (position == text.Length || !Digit(text[position])) return Fail("Üs basamağı bekleniyor.");
                while (position < text.Length && Digit(text[position])) position++;
            }
            return true;
        }
    }

    internal static class JsonTextFormatter
    {
        public static string Compress(string text)
        {
            StringBuilder result = new StringBuilder(text.Length);
            bool quoted = false, escaped = false;
            foreach (char c in text)
            {
                if (quoted)
                {
                    result.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                }
                else
                {
                    if (c == '"') quoted = true;
                    if (!char.IsWhiteSpace(c)) result.Append(c);
                }
            }
            return result.ToString();
        }

        // Format tokens without deserializing: preserve duplicate keys, numeric precision and escapes.
        public static string Beautify(string text)
        {
            StringBuilder result = new StringBuilder();
            bool quoted = false, escaped = false;
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    result.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (char.IsWhiteSpace(c)) continue;
                if (c == '"') { quoted = true; result.Append(c); }
                else if (c == '{' || c == '[')
                {
                    int numericEnd = c == '[' ? NumericArrayEnd(text, i + 1) : -1;
                    if (numericEnd >= 0)
                    {
                        for (; i <= numericEnd; i++)
                        {
                            char token = text[i];
                            if (char.IsWhiteSpace(token)) continue;
                            result.Append(token);
                            if (token == ',') result.Append(' ');
                        }
                        i = numericEnd;
                        continue;
                    }
                    result.Append(c);
                    int next = i + 1;
                    while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                    if (next < text.Length && (text[next] == '}' || text[next] == ']'))
                    { result.Append(text[next]); i = next; }
                    else { depth++; result.AppendLine(); result.Append(' ', depth * 2); }
                }
                else if (c == '}' || c == ']') { depth--; result.AppendLine(); result.Append(' ', depth * 2); result.Append(c); }
                else if (c == ',') { result.Append(c); result.AppendLine(); result.Append(' ', depth * 2); }
                else if (c == ':') result.Append(": ");
                else result.Append(c);
            }
            return result.ToString();
        }

        private static int NumericArrayEnd(string text, int start)
        {
            // Input has already passed JSON validation. Inspect token characters rather
            // than parsing numbers, so large values and exponent notation stay exact.
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ']') return i;
                if (char.IsWhiteSpace(c) || (c >= '0' && c <= '9') || c == ',' ||
                    c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') continue;
                return -1;
            }
            return -1;
        }
    }

    internal sealed class JsonMessageTextBox : TextBox
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string text);

        public void ReplaceAllWithUndo(string text)
        {
            SelectAll();
            SendMessage(Handle, 0x00C2, new IntPtr(1), text); // EM_REPLACESEL with undo enabled.
            Select(0, 0);
            ScrollToCaret();
        }

        public JsonValidationError ValidationError { get; private set; }
        public bool ValidateJson()
        {
            ValidationError = JsonSyntaxValidator.Validate(Text);
            if (ValidationError != null)
            {
                Focus();
                Select(Math.Min(ValidationError.Offset, TextLength), 0);
                ScrollToCaret();
            }
            Invalidate();
            return ValidationError == null;
        }
        protected override void OnTextChanged(EventArgs e)
        {
            // After a failed send, keep the marker accurate while the user repairs the JSON.
            if (ValidationError != null) ValidationError = JsonSyntaxValidator.Validate(Text);
            base.OnTextChanged(e);
            Invalidate();
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0203 && IsHandleCreated) // WM_LBUTTONDBLCLK
            {
                // Native EDIT uses whitespace boundaries; JSON words also end at punctuation.
                long coordinates = message.LParam.ToInt64();
                SelectWordAt(new Point((short)(coordinates & 0xffff), (short)((coordinates >> 16) & 0xffff)));
            }
            if (ValidationError == null || !IsHandleCreated) return;
            if (message.Msg == 0x000F) // WM_PAINT: native EDIT paints first, then the diagnostic overlay.
                using (Graphics graphics = Graphics.FromHwnd(Handle)) DrawError(graphics);
            else if (message.Msg == 0x0318 && message.WParam != IntPtr.Zero) // WM_PRINTCLIENT
                using (Graphics graphics = Graphics.FromHdc(message.WParam)) DrawError(graphics);
            else if (message.Msg == 0x0114 || message.Msg == 0x0115 || message.Msg == 0x020A || message.Msg == 0x0101)
                Invalidate();
        }
        private void SelectWordAt(Point point)
        {
            string text = Text;
            if (text.Length == 0) return;
            int index = GetCharIndexFromPosition(point);
            // Keep the native behavior when the user clicks a separator itself.
            if (index < 0 || index >= text.Length || IsWordSeparator(text[index])) return;
            int start = index, end = index + 1;
            while (start > 0 && !IsWordSeparator(text[start - 1])) start--;
            while (end < text.Length && !IsWordSeparator(text[end])) end++;
            Select(start, end - start);
        }
        private static bool IsWordSeparator(char value)
        {
            return Char.IsWhiteSpace(value) || Char.IsPunctuation(value) || Char.IsSymbol(value) || Char.IsControl(value);
        }
        private void DrawError(Graphics graphics)
        {
            int start = Math.Min(ValidationError.Offset, TextLength);
            int end = Math.Min(TextLength, start + ValidationError.Length);
            Point first = GetPositionFromCharIndex(start);
            Point last = GetPositionFromCharIndex(end);
            int width = Math.Max(8, last.Y == first.Y ? last.X - first.X : ClientSize.Width - first.X - 2);
            int y = first.Y + Font.Height - 2;
            graphics.SetClip(ClientRectangle);
            using (Pen pen = new Pen(Color.FromArgb(240, 60, 60), 1.5F))
                for (int x = first.X; x < Math.Min(first.X + width, ClientSize.Width - 1); x += 4)
                {
                    graphics.DrawLine(pen, x, y, x + 2, y - 2);
                    graphics.DrawLine(pen, x + 2, y - 2, x + 4, y);
                }
        }
    }
}
