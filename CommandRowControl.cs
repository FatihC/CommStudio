using System;
using System.Drawing;
using System.Windows.Forms;

namespace CommStudio
{
    internal sealed class CommandRowControl : UserControl
    {
        private readonly TextBox commandTextBox;
        private readonly CheckBox hexCheckBox;
        private readonly Button sendButton;
        private readonly Button removeButton;
        private readonly TableLayoutPanel layout;
        private bool sendEnabled;
        private bool isDarkMode;

        public event EventHandler SendRequested;
        public event EventHandler RemoveRequested;

        public string CommandText
        {
            get { return commandTextBox.Text; }
            set { commandTextBox.Text = value ?? string.Empty; }
        }

        public bool IsHex
        {
            get { return hexCheckBox.Checked; }
            set { hexCheckBox.Checked = value; }
        }

        public CommandRowControl()
        {
            Height = 48;
            MinimumSize = new Size(440, 48);
            Margin = new Padding(0, 0, 0, 7);
            BackColor = Color.White;

            layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 4;
            layout.RowCount = 1;
            layout.Margin = Padding.Empty;
            layout.Padding = Padding.Empty;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));

            commandTextBox = new TextBox();
            commandTextBox.Dock = DockStyle.Fill;
            commandTextBox.Multiline = true;
            commandTextBox.AcceptsReturn = true;
            commandTextBox.ScrollBars = ScrollBars.Vertical;
            commandTextBox.Font = new Font("Consolas", 10F);
            commandTextBox.Margin = new Padding(0, 2, 8, 2);
            commandTextBox.KeyDown += CommandTextBoxOnKeyDown;

            hexCheckBox = new CheckBox();
            hexCheckBox.Text = "HEX";
            hexCheckBox.Dock = DockStyle.Fill;
            hexCheckBox.TextAlign = ContentAlignment.MiddleLeft;
            hexCheckBox.Margin = new Padding(0);

            sendButton = CreateButton("Send", Color.FromArgb(32, 111, 214), Color.White);
            sendButton.Font = new Font("Segoe UI", 16F, FontStyle.Regular);
            sendButton.Margin = new Padding(0, 2, 8, 2);
            sendButton.Click += delegate { OnSendRequested(); };

            removeButton = CreateButton("×", Color.FromArgb(238, 241, 245), Color.FromArgb(90, 98, 108));
            removeButton.Font = new Font("Segoe UI", 14F, FontStyle.Regular);
            removeButton.Margin = new Padding(0, 2, 0, 2);
            removeButton.Click += delegate
            {
                EventHandler handler = RemoveRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            layout.Controls.Add(commandTextBox, 0, 0);
            layout.Controls.Add(hexCheckBox, 1, 0);
            layout.Controls.Add(sendButton, 2, 0);
            layout.Controls.Add(removeButton, 3, 0);
            Controls.Add(layout);
        }

        public void SetSendEnabled(bool enabled)
        {
            sendEnabled = enabled;
            // Keep the control enabled so WinForms honors our theme colors.
            // SendCommandAsync already reports a clear error when no TCP connection exists.
            sendButton.Enabled = true;
            ApplyButtonTheme();
        }

        public void ApplyTheme(bool darkMode)
        {
            isDarkMode = darkMode;
            Color surface = darkMode ? Color.FromArgb(48, 48, 48) : Color.FromArgb(240, 240, 240);
            Color input = darkMode ? Color.FromArgb(63, 63, 63) : Color.White;
            Color primaryText = darkMode ? Color.FromArgb(224, 224, 224) : Color.FromArgb(40, 40, 40);

            BackColor = surface;
            layout.BackColor = surface;
            commandTextBox.BackColor = input;
            commandTextBox.ForeColor = primaryText;
            hexCheckBox.BackColor = surface;
            hexCheckBox.ForeColor = primaryText;
            removeButton.BackColor = darkMode
                ? Color.FromArgb(72, 72, 72)
                : Color.FromArgb(224, 224, 224);
            removeButton.ForeColor = darkMode
                ? Color.FromArgb(224, 224, 224)
                : Color.FromArgb(80, 80, 80);
            ApplyButtonTheme();
        }

        public void FocusCommand()
        {
            commandTextBox.Focus();
        }

        private void CommandTextBoxOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                OnSendRequested();
            }
        }

        private void OnSendRequested()
        {
            EventHandler handler = SendRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void ApplyButtonTheme()
        {
            sendButton.BackColor = sendEnabled
                ? Color.FromArgb(32, 111, 214)
                : (isDarkMode ? Color.FromArgb(72, 72, 72) : Color.FromArgb(224, 224, 224));
            sendButton.ForeColor = sendEnabled
                ? Color.White
                : (isDarkMode ? Color.FromArgb(200, 200, 200) : Color.FromArgb(95, 95, 95));
        }

        private static Button CreateButton(string text, Color backColor, Color foreColor)
        {
            Button button = new Button();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.Cursor = Cursors.Hand;
            return button;
        }
    }
}
