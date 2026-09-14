using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed class MqttPayloadTextBox : RichTextBox
    {
        private readonly Action<int> scrollHistory;
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, ref Point point);
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct FormatRange
        { public IntPtr Hdc, Target; public NativeRect Area, Page; public int Start, End; }
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, ref FormatRange range);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
        public MqttPayloadTextBox(Action<int> scroll)
        {
            scrollHistory = scroll;
            ReadOnly = true; BorderStyle = BorderStyle.None; DetectUrls = false;
            HideSelection = false; ScrollBars = RichTextBoxScrollBars.None;
            Multiline = true; MaxLength = int.MaxValue; TabStop = true;
        }
        public int HorizontalPosition
        {
            get { Point point = Point.Empty; SendMessage(Handle, 0x04DD, IntPtr.Zero, ref point); return point.X; }
            set { Point point = new Point(value, 0); SendMessage(Handle, 0x04DE, IntPtr.Zero, ref point); }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { scrollHistory(e.Delta); }
        protected override void WndProc(ref Message message)
        {
            // RichEdit omits its text in the usual WinForms DrawToBitmap path.
            // Use its native formatter for print/preview, leaving interactive painting native.
            if ((message.Msg == 0x0317 || message.Msg == 0x0318) && message.WParam != IntPtr.Zero)
            {
                using (Graphics graphics = Graphics.FromHdc(message.WParam))
                {
                    graphics.Clear(BackColor);
                    int width = WordWrap ? ClientSize.Width : Math.Max(ClientSize.Width,
                        TextRenderer.MeasureText(Text, Font, new Size(1000000, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.ExpandTabs).Width);
                    float x = 1440F / graphics.DpiX, y = 1440F / graphics.DpiY;
                    NativeRect rectangle = new NativeRect { Left = (int)(-HorizontalPosition * x), Top = 0,
                        Right = (int)((width - HorizontalPosition) * x), Bottom = (int)(ClientSize.Height * y) };
                    FormatRange range = new FormatRange { Hdc = message.WParam, Target = message.WParam, Area = rectangle, Page = rectangle, Start = 0, End = TextLength };
                    SendMessage(Handle, 0x0439, new IntPtr(1), ref range);
                    SendMessage(Handle, 0x0439, IntPtr.Zero, IntPtr.Zero);
                }
                message.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref message);
        }
    }
}
