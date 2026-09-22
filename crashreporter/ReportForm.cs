using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DragNWash.ModFramework.Diagnostics;

namespace DragNWash.CrashReporter
{
    // The window: what happened, what to do, the details, and buttons to open
    // the report folder or copy the report for a bug report.
    internal sealed class ReportForm : Form
    {
        private readonly CrashDiagnosis _diagnosis;
        private TableLayoutPanel _layout;
        private TableLayoutPanel _notes;
        private Label _status;
        private FlowLayoutPanel _buttons;

        internal ReportForm(CrashDiagnosis diagnosis)
        {
            _diagnosis = diagnosis;
            Text = Strings.Get(Strings.Key.WindowTitle);
            Font = new Font(Strings.Current == "ja" ? "Yu Gothic UI" : Strings.Current == "zh" ? "Microsoft YaHei UI" : "Segoe UI", 9.75f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            // Wide and low, then fitted to what it shows once laid out (FitHeight).
            ClientSize = new Size(780, 420);
            MinimumSize = new Size(520, 300);
            BackColor = Color.White;
            TryIcon();

            var layout = _layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(20, 16, 20, 12),
                AutoScroll = true,
            };
            Controls.Add(layout);

            string[] words = Strings.For(diagnosis.What);
            layout.Controls.Add(Label(Strings.Get(Strings.Key.Headline), 16f, FontStyle.Bold, Color.FromArgb(30, 30, 30), 0, 2));
            layout.Controls.Add(Label(Strings.Get(Strings.Key.Subheadline), 10.5f, FontStyle.Regular, Color.FromArgb(90, 90, 90), 0, 12));
            layout.Controls.Add(Label(Strings.Get(Strings.Key.WhatHappened), 10.5f, FontStyle.Bold, Color.FromArgb(40, 40, 40), 4, 2));
            layout.Controls.Add(Label(words[0], 10f, FontStyle.Regular, Color.FromArgb(30, 30, 30), 0, 8));
            if (words[1] != null)
            {
                layout.Controls.Add(Label(Strings.Get(Strings.Key.WhatToDo), 10.5f, FontStyle.Bold, Color.FromArgb(40, 40, 40), 4, 2));
                layout.Controls.Add(Label(words[1], 10f, FontStyle.Regular, Color.FromArgb(30, 30, 30), 0, 8));
            }

            layout.Controls.Add(Label(Strings.Get(Strings.Key.Details), 10.5f, FontStyle.Bold, Color.FromArgb(40, 40, 40), 4, 2));
            // One line per detail, "name: value", wrapped like the texts above
            // (labels inside a nested table would not wrap to the window).
            foreach (var d in diagnosis.Details)
            {
                layout.Controls.Add(Label(Strings.For(d.Key) + (Strings.Current == "en" ? ": " : "：") + d.Value, 9f, FontStyle.Regular, Color.FromArgb(60, 60, 60), 0, 2));
            }
            // The memory dump warning and the privacy line stay in view above the
            // buttons, however small the window: they are what to know before
            // copying or sharing anything. Copy and open failures show here too.
            var notes = _notes = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20, 10, 20, 10),
                BackColor = Color.White,
            };
            notes.Paint += (s, e) =>
            {
                using (var line = new Pen(Color.FromArgb(225, 225, 225)))
                {
                    e.Graphics.DrawLine(line, 0, 0, notes.Width, 0);
                }
            };
            if (diagnosis.HasCrashDump || diagnosis.HasHangDump)
            {
                notes.Controls.Add(Label(Strings.Get(Strings.Key.DumpNote), 9.5f, FontStyle.Regular, Color.FromArgb(122, 68, 0), 0, 4));
            }
            notes.Controls.Add(Label(Strings.Get(Strings.Key.Privacy), 9f, FontStyle.Regular, Color.FromArgb(75, 75, 75), 0, 0));
            _status = Label("", 9.5f, FontStyle.Regular, Color.FromArgb(160, 40, 40), 6, 0);
            _status.Visible = false;
            _status.AccessibleRole = AccessibleRole.Alert;
            notes.Controls.Add(_status);

            // Right to left, so the tab order is set to follow what is seen:
            // Open, Copy, Close. Enter copies (the button that starts with the
            // focus), Esc closes.
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(16, 8, 16, 12), BackColor = Color.FromArgb(245, 245, 245) };
            Button close = MakeButton(Strings.Get(Strings.Key.Close), (s, e) => Close());
            Button copy = null;
            copy = MakeButton(Strings.Get(Strings.Key.CopyReport), (s, e) =>
            {
                try
                {
                    // Retries for a second: another program may hold the clipboard for a moment.
                    Clipboard.SetDataObject(_diagnosis.ReportText.Length > 0 ? _diagnosis.ReportText : _diagnosis.Folder, true, 10, 100);
                    copy.Text = Strings.Get(Strings.Key.Copied);
                    ShowStatus(null);
                }
                catch
                {
                    ShowStatus(Strings.Get(Strings.Key.CopyFailed));
                }
            });
            Button open = MakeButton(Strings.Get(Strings.Key.OpenFolder), (s, e) =>
            {
                try
                {
                    if (!Directory.Exists(_diagnosis.Folder))
                    {
                        throw new DirectoryNotFoundException(_diagnosis.Folder);
                    }
                    Process.Start("explorer.exe", "\"" + _diagnosis.Folder + "\"")?.Dispose();
                    ShowStatus(null);
                }
                catch
                {
                    ShowStatus(string.Format(Strings.Get(Strings.Key.OpenFailed), Path.Combine("BepInEx", "CrashReports", Path.GetFileName(_diagnosis.Folder))));
                }
            });
            open.TabIndex = 0;
            copy.TabIndex = 1;
            close.TabIndex = 2;
            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            buttons.Controls.Add(open);
            // Docked from the last added: the buttons at the bottom, the notes
            // above them, the scrolling texts in what is left.
            Controls.Add(notes);
            Controls.Add(buttons);
            _buttons = buttons;
            AcceptButton = copy;
            CancelButton = close;
            ActiveControl = copy;
            Shown += (s, e) => { KeepButtonsOnOneRow(); Wrap(); FitHeight(); Activate(); copy.Focus(); TopMost = false; };
            layout.Layout += (s, e) => Wrap();
            notes.Layout += (s, e) => Wrap();
        }

        // One line in the notes panel after Copy or Open failed; null hides it.
        // The window grows (or shrinks back) by the line's height, so the texts
        // above keep what they showed instead of losing a line half-way.
        private void ShowStatus(string text)
        {
            int before = _notes.Height;
            _status.Text = text ?? "";
            _status.Visible = text != null;
            PerformLayout();
            int change = _notes.Height - before;
            if (change == 0 || WindowState != FormWindowState.Normal) return;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int height = Math.Max(MinimumSize.Height, Math.Min(Height + change, area.Height));
            int top = Math.Max(area.Top, Math.Min(Top, area.Bottom - height));
            SetBounds(Left, top, Width, height);
        }

        private Label Label(string text, float size, FontStyle style, Color color, int top, int bottom)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(Font.FontFamily, size, style),
                ForeColor = color,
                Margin = new Padding(0, top, 0, bottom),
                UseMnemonic = false,
            };
        }

        // Labels wrap at their MaximumSize, so it follows the width the layout
        // really has (after DPI scaling), and for the details the width left
        // beside their names.
        private bool _wrapping;

        private void Wrap()
        {
            if (_wrapping || _layout == null || _notes == null) return;
            _wrapping = true;
            try
            {
                int available = _layout.ClientSize.Width - _layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4;
                if (available < 100) return;
                foreach (Control c in _layout.Controls)
                {
                    if (c is Label label) label.MaximumSize = new Size(available, 0);
                }
                // The notes never scroll, so they have the scroll bar's width too.
                // They span the window, so its width is theirs before they are
                // docked again (OnLayout): their height then counts every line.
                int notes = ClientSize.Width - _notes.Padding.Horizontal - 4;
                if (notes < 100) return;
                foreach (Control c in _notes.Controls)
                {
                    if (c is Label label) label.MaximumSize = new Size(notes, 0);
                }
            }
            finally
            {
                _wrapping = false;
            }
        }

        // The notes are wrapped to the new width before docking, so a line
        // they gain is not hidden behind the buttons.
        protected override void OnLayout(LayoutEventArgs levent)
        {
            Wrap();
            base.OnLayout(levent);
        }

        // Never so narrow that a button wraps to a second row, out of the order
        // the tab keys follow (the Japanese labels are the longest).
        private void KeepButtonsOnOneRow()
        {
            int row = _buttons.Padding.Horizontal;
            foreach (Control b in _buttons.Controls)
            {
                row += b.Width + b.Margin.Horizontal;
            }
            MinimumSize = new Size(Math.Max(MinimumSize.Width, row + Width - ClientSize.Width), MinimumSize.Height);
        }

        // As tall as the texts need (they differ from one kind of crash to
        // another), no taller than most of the screen; centred again after.
        private void FitHeight()
        {
            if (_layout == null) return;
            PerformLayout();
            _layout.PerformLayout();
            int bottom = 0;
            foreach (Control c in _layout.Controls)
            {
                bottom = Math.Max(bottom, c.Bottom + c.Margin.Bottom);
            }
            int wanted = bottom - _layout.AutoScrollPosition.Y + _layout.Padding.Bottom + _notes.Height + (_buttons != null ? _buttons.Height : 0);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int height = Math.Min(wanted, (int)(area.Height * 0.9));
            ClientSize = new Size(ClientSize.Width, Math.Max(height, 200));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            Wrap();
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(12, 2, 12, 2), Margin = new Padding(8, 0, 0, 0), UseVisualStyleBackColor = true };
            b.Click += onClick;
            return b;
        }

        // The framework's icon lies next to the core DLL, beside this program.
        private void TryIcon()
        {
            try
            {
                string png = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png");
                if (File.Exists(png))
                {
                    using (var bitmap = new Bitmap(png))
                    {
                        Icon = Icon.FromHandle(new Bitmap(bitmap, new Size(64, 64)).GetHicon());
                    }
                }
            }
            catch
            {
            }
        }
    }
}
