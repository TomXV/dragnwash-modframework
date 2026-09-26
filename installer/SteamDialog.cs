using System;
using System.Drawing;
using System.Windows.Forms;

namespace DragNWash.Installer
{
    // After Install, Update or Uninstall is pressed, before the download question and
    // before anything changes, when the game's launch options have to change and Steam
    // is running: Steam writes over its settings file while it runs. "Close Steam for
    // me" asks Steam to exit and waits; "I'll close it" waits; either way the window
    // carries on by itself once Steam is gone. "Skip this option" goes on without
    // changing the launch options this time (Ignore). The close box and Esc cancel the
    // whole run, with nothing changed.
    internal sealed class SteamDialog : Form
    {
        private readonly TableLayoutPanel _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(20, 18, 20, 12), BackColor = SystemColors.Window };
        private readonly TableLayoutPanel _buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 4, Padding = new Padding(12, 8, 12, 8), BackColor = SystemColors.Control };
        private readonly TableLayoutPanel _waitRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 12, 0, 0), Visible = false };
        private readonly ProgressBar _marquee = new ProgressBar { Style = ProgressBarStyle.Marquee, Width = 64, Height = 12, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 10, 0) };
        private readonly Label _waitText = new Label { AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false, Margin = Padding.Empty };
        private readonly Button _close;
        private readonly Button _self;
        private readonly Timer _poll = new Timer { Interval = 500 };
        private readonly Action<string> _log;

        // When "Close Steam for me" asked Steam to exit; null until then.
        private DateTime? _askedAt;

        // The steam.exe the installer asked to exit, to start again once the run is done;
        // null when the player closed Steam.
        internal string ClosedExe { get; private set; }

        // remove: the launcher is to be taken out of the launch options, not put in.
        internal SteamDialog(string title, bool remove, Action<string> log)
        {
            _log = log;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Strings.UiFont();
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = SystemColors.Window;
            ClientSize = new Size(520, 200);

            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var icon = new PictureBox { Image = SystemIcons.Information.ToBitmap(), SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 0, 14, 0) };
            _layout.Controls.Add(icon, 0, 0);
            Add(Paragraph(Strings.Get(Strings.Key.SteamHead), 2, bold: true));
            Add(Paragraph(Strings.Get(remove ? Strings.Key.SteamBodyRemove : Strings.Key.SteamBody), 6));
            Label keep = Paragraph(Strings.Get(Strings.Key.SteamKeep), 6);
            keep.ForeColor = SystemColors.GrayText;
            Add(keep);
            _waitRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _waitRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _waitRow.Controls.Add(_marquee, 0, 0);
            _waitRow.Controls.Add(_waitText, 1, 0);
            Add(_waitRow);
            _layout.SetRowSpan(icon, _layout.RowCount);

            var skip = new Button { AutoSize = true, MinimumSize = new Size(88, 28), Text = Strings.Get(remove ? Strings.Key.SteamSkipRemove : Strings.Key.SteamSkip), DialogResult = DialogResult.Ignore };
            _close = new Button { AutoSize = true, MinimumSize = new Size(88, 28), Text = Strings.Get(Strings.Key.SteamClose) };
            _self = new Button { AutoSize = true, MinimumSize = new Size(88, 28), Text = Strings.Get(Strings.Key.SteamSelf) };
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.Controls.Add(skip, 0, 0);
            _buttons.Controls.Add(_close, 2, 0);
            _buttons.Controls.Add(_self, 3, 0);
            AcceptButton = _close;
            _close.Click += (_, __) => CloseSteam();
            _self.Click += (_, __) => Wait(Strings.Get(Strings.Key.SteamWaiting));

            // Steam gone, however it went: carry on.
            _poll.Tick += (_, __) =>
            {
                if (!Steam.Running())
                {
                    _poll.Stop();
                    _log("Steam: closed");
                    DialogResult = DialogResult.OK;
                }
                else if (_askedAt != null && DateTime.UtcNow - _askedAt.Value > TimeSpan.FromSeconds(Steam.ExitSeconds))
                {
                    _askedAt = null;
                    _log($"Steam: still running {Steam.ExitSeconds} s after it was asked to exit");
                    _marquee.Visible = false;
                    _waitText.Text = Strings.Get(Strings.Key.SteamSlow, Steam.ExitSeconds);
                    _close.Enabled = true;
                    _self.Enabled = true;
                    FitHeight();
                }
            };

            Controls.Add(_layout);
            Controls.Add(_buttons);
            FitHeight();
            Load += (_, __) =>
            {
                FitHeight();
                _close.Focus();
                _poll.Start();
            };
            FormClosed += (_, __) => _poll.Stop();
        }

        // Esc cancels the whole run, like the close box; there is no Cancel button.
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _poll.Dispose();
            }
            base.Dispose(disposing);
        }

        private void CloseSteam()
        {
            try
            {
                string exe = Steam.AskToExit(_log);
                if (exe != null)
                {
                    ClosedExe = exe;
                    _askedAt = DateTime.UtcNow;
                    _close.Enabled = false;
                    Wait(Strings.Get(Strings.Key.SteamClosing));
                    return;
                }
            }
            catch (Exception ex)
            {
                _log($"Steam: could not ask it to exit: {ex.Message}");
            }
            // Nothing to ask: the player closes it.
            Wait(Strings.Get(Strings.Key.SteamWaiting));
        }

        private void Wait(string text)
        {
            _self.Enabled = false;
            _marquee.Visible = true;
            _waitText.Text = text;
            _waitRow.Visible = true;
            FitHeight();
        }

        private Label Paragraph(string text, int top, bool bold = false)
        {
            return new Label
            {
                AutoSize = true, Dock = DockStyle.Fill, Text = text, UseMnemonic = false, Margin = new Padding(0, top, 0, 0),
                Font = bold ? new Font(Font, FontStyle.Bold) : null,
            };
        }

        private void Add(Control control)
        {
            _layout.Controls.Add(control, 1, _layout.RowCount);
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.RowCount++;
        }

        // Fit the height to the text at the dialog's width, which wraps.
        private void FitHeight()
        {
            _layout.PerformLayout();
            int height = _layout.GetPreferredSize(new Size(ClientSize.Width, 0)).Height;
            ClientSize = new Size(ClientSize.Width, height + _buttons.GetPreferredSize(new Size(ClientSize.Width, 0)).Height);
        }
    }
}
