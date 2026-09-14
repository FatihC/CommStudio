using System;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed class LogRichTextBox : RichTextBox
    {
        public event EventHandler ZoomInRequested;
        public event EventHandler ZoomOutRequested;
        public event EventHandler ZoomResetRequested;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                if (e.Delta > 0)
                {
                    Raise(ZoomInRequested);
                }
                else if (e.Delta < 0)
                {
                    Raise(ZoomOutRequested);
                }

                return;
            }

            base.OnMouseWheel(e);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if ((keyData & Keys.Control) == Keys.Control)
            {
                Keys keyCode = keyData & Keys.KeyCode;
                if (keyCode == Keys.Add || keyCode == Keys.Oemplus)
                {
                    Raise(ZoomInRequested);
                    return true;
                }

                if (keyCode == Keys.Subtract || keyCode == Keys.OemMinus)
                {
                    Raise(ZoomOutRequested);
                    return true;
                }

                if (keyCode == Keys.D0 || keyCode == Keys.NumPad0)
                {
                    Raise(ZoomResetRequested);
                    return true;
                }
            }

            return base.ProcessCmdKey(ref message, keyData);
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
