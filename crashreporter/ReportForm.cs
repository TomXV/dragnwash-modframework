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
            layout.Controls.Add(Label(Strings.Get(Strings.Key.Headline), 16f, FontStyle.Bold, Color.FromArgb(160, 40, 40), 0, 2));
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
            layout.Controls.Add(Label("", 4f, FontStyle.Regular, Color.White, 0, 4));

            if (diagnosis.HasCrashDump || diagnosis.HasHangDump)
            {
                layout.Controls.Add(Label(Strings.Get(Strings.Key.DumpNote), 9f, FontStyle.Regular, Color.FromArgb(150, 90, 0), 0, 6));
            }
            layout.Controls.Add(Label(Strings.Get(Strings.Key.Privacy), 8.5f, FontStyle.Regular, Color.FromArgb(130, 130, 130), 0, 4));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(16, 8, 16, 12), BackColor = Color.FromArgb(245, 245, 245) };
            Button close = MakeButton(Strings.Get(Strings.Key.Close), (s, e) => Close());
            Button copy = null;
            copy = MakeButton(Strings.Get(Strings.Key.CopyReport), (s, e) =>
            {
                try
                {
                    Clipboard.SetText(_diagnosis.ReportText.Length > 0 ? _diagnosis.ReportText : _diagnosis.Folder);
                    copy.Text = Strings.Get(Strings.Key.Copied);
                }
                catch
                {
                }
            });
            Button open = MakeButton(Strings.Get(Strings.Key.OpenFolder), (s, e) =>
            {
                try
                {
                    Process.Start("explorer.exe", "\"" + _diagnosis.Folder + "\"");
                }
                catch
                {
                }
            });
            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            buttons.Controls.Add(open);
            Controls.Add(buttons);
            _buttons = buttons;
            AcceptButton = close;
            CancelButton = close;
            Shown += (s, e) => { Wrap(); FitHeight(); Activate(); TopMost = false; };
            layout.Layout += (s, e) => Wrap();
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
            if (_wrapping || _layout == null) return;
            _wrapping = true;
            try
            {
                int available = _layout.ClientSize.Width - _layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4;
                if (available < 100) return;
                foreach (Control c in _layout.Controls)
                {
                    if (c is Label label) label.MaximumSize = new Size(available, 0);
                }
            }
            finally
            {
                _wrapping = false;
            }
        }

        // As tall as the texts need (they differ from one kind of crash to
        // another), no taller than most of the screen; centred again after.
        private void FitHeight()
        {
            if (_layout == null) return;
            _layout.PerformLayout();
            int bottom = 0;
            foreach (Control c in _layout.Controls)
            {
                bottom = Math.Max(bottom, c.Bottom + c.Margin.Bottom);
            }
            int wanted = bottom - _layout.AutoScrollPosition.Y + _layout.Padding.Bottom + (_buttons != null ? _buttons.Height : 0);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int height = Math.Min(wanted, (int)(area.Height * 0.9));
            ClientSize = new Size(ClientSize.Width, Math.Max(height, 200));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            Wrap();
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(8, 0, 0, 0), UseVisualStyleBackColor = true };
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
