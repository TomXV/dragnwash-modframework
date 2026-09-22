using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DragNWash.Installer
{
    internal sealed class MainForm : Form
    {
        private readonly ModManifest _manifest;
        private readonly InstallerCore _core;

        private readonly Label _heading = new Label { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 13f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
        private readonly Label _languageLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Right, TextAlign = ContentAlignment.MiddleRight };
        private readonly ComboBox _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Anchor = AnchorStyles.Right };
        private readonly Label _gameLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 8, 3) };
        private readonly TextBox _game = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly Button _browse = new Button { AutoSize = true };
        private readonly Label _status = new Label { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        private readonly Label _actionLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 8, 3) };
        private readonly RadioButton _modeInstall = new RadioButton { AutoSize = true, Checked = true, Margin = new Padding(0, 3, 16, 3) };
        private readonly RadioButton _modeUninstall = new RadioButton { AutoSize = true, Margin = new Padding(0, 3, 16, 3) };
        private readonly Label _nothingToUninstall = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 3, 0, 3) };

        // Only the controls of the chosen action are shown, with what it will do.
        private readonly GroupBox _installGroup = new WrappingGroupBox();
        private readonly GroupBox _uninstallGroup = new WrappingGroupBox();
        private readonly List<(ModChoice Choice, Label Label, ComboBox Box)> _choices = new List<(ModChoice, Label, ComboBox)>();
        private readonly CheckBox _keepData = new CheckBox { AutoSize = true, Checked = true };
        private readonly CheckBox _alsoBepInEx = new CheckBox { AutoSize = true };
        private readonly Label _installWill = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
        private readonly Label _uninstallWill = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
        private readonly TableLayoutPanel _installSteps = StepList();
        private readonly TableLayoutPanel _uninstallSteps = StepList();

        // While Install or Uninstall runs; Close becomes Cancel for as long as stopping is clean.
        private readonly TableLayoutPanel _progressRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 8, 0, 0), Visible = false };
        private readonly Label _progressText = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
        private readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Fill, Height = 23, Margin = Padding.Empty };
        private readonly Label _percent = new Label { AutoSize = false, Width = 40, Anchor = AnchorStyles.Right, TextAlign = ContentAlignment.MiddleRight, Margin = Padding.Empty };

        private readonly Button _run = new Button { AutoSize = true, MinimumSize = new Size(140, 34) };
        private readonly Button _close = new Button { AutoSize = true, MinimumSize = new Size(140, 34) };
        private readonly LinkLabel _website = new LinkLabel { AutoSize = true, Anchor = AnchorStyles.Right };
        private readonly Label _hint = new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 6) };
        private readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9f) };

        // The mod's version in the chosen game folder, or null when it is not there.
        private string _installed;
        private bool _busy;
        private CancellationTokenSource _cancel;
        private bool _closeWhenDone;

        internal MainForm(ModManifest manifest, string payload)
        {
            _manifest = manifest;
            _core = new InstallerCore(manifest, payload, AppendLog);

            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(620, 560);
            Size = new Size(720, 640);
            Font = SystemFonts.MessageBoxFont;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
            }

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(14) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var top = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
            top.Controls.Add(_language);
            top.Controls.Add(_languageLabel);
            var head = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddRow(head, _heading, 1, top, 1);
            AddRow(layout, head, 3);

            AddRow(layout, _gameLabel, 1, _game, 1, _browse, 1);
            AddRow(layout, _status, 3);

            var modes = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            modes.Controls.Add(_modeInstall);
            modes.Controls.Add(_modeUninstall);
            modes.Controls.Add(_nothingToUninstall);
            AddRow(layout, _actionLabel, 1, modes, 2);

            TableLayoutPanel install = GroupLayout(_installGroup);
            foreach (ModChoice choice in manifest.Choices)
            {
                var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 8, 3) };
                var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left, Width = 260 };
                foreach (ModChoiceOption option in choice.Options)
                {
                    box.Items.Add(option.Name ?? option.Value);
                }
                box.SelectedIndexChanged += (_, __) => RefreshPlan();
                _choices.Add((choice, label, box));
                AddRow(install, label, 1, box, 1);
            }
            AddRow(install, _installWill, 2);
            AddRow(install, _installSteps, 2);
            AddRow(layout, _installGroup, 3);
            // The choices' boxes start where the game folder box does, one column down the window.
            _game.LocationChanged += (_, __) => AlignChoices();

            TableLayoutPanel uninstall = GroupLayout(_uninstallGroup);
            AddRow(uninstall, _keepData, 2);
            AddRow(uninstall, _alsoBepInEx, 2);
            AddRow(uninstall, _uninstallWill, 2);
            AddRow(uninstall, _uninstallSteps, 2);
            AddRow(layout, _uninstallGroup, 3);

            _progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddRow(_progressRow, _progressText, 1, _progress, 1, _percent, 1);
            AddRow(layout, _progressRow, 3);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(_run);
            buttons.Controls.Add(_close);
            AddRow(layout, buttons, 2, _website, 1);
            AddRow(layout, _hint, 3);

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(_log, 0, layout.RowCount);
            layout.SetColumnSpan(_log, 3);
            layout.RowCount++;
            Controls.Add(layout);

            foreach (string code in Strings.Languages)
            {
                _language.Items.Add(Strings.LanguageName(code));
            }
            _language.SelectedIndex = Array.IndexOf(Strings.Languages, Strings.Current);
            _language.SelectedIndexChanged += (_, __) =>
            {
                Strings.Current = Strings.Languages[_language.SelectedIndex];
                UpdateTexts();
            };

            _browse.Click += (_, __) => Browse();
            _game.TextChanged += (_, __) => RefreshStatus();
            _modeInstall.CheckedChanged += (_, __) => ApplyMode();
            _keepData.CheckedChanged += (_, __) => RefreshPlan();
            _alsoBepInEx.CheckedChanged += (_, __) => RefreshPlan();
            _run.Click += (_, __) => Run(install: _modeInstall.Checked);
            _close.Click += (_, __) =>
            {
                if (_busy)
                {
                    StopDownload();
                }
                else
                {
                    Close();
                }
            };
            FormClosing += (_, e) =>
            {
                // Closing mid-download stops it and closes once the game folder is
                // known to be untouched; while it is being changed, it waits.
                if (_busy && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    _closeWhenDone |= StopDownload();
                }
            };
            // Enter runs the chosen action, Esc closes (or cancels the download).
            AcceptButton = _run;
            CancelButton = _close;
            _website.Visible = !string.IsNullOrEmpty(manifest.Website) && manifest.Website.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            _website.LinkClicked += (_, __) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(manifest.Website) { UseShellExecute = true });
                }
                catch (Exception)
                {
                }
            };

            _game.Text = InstallerCore.FindGame() ?? "";
            UpdateTexts();
        }

        private static void AddRow(TableLayoutPanel layout, params object[] cells)
        {
            int column = 0;
            for (int i = 0; i < cells.Length; i += 2)
            {
                var control = (Control)cells[i];
                int span = (int)cells[i + 1];
                layout.Controls.Add(control, column, layout.RowCount);
                layout.SetColumnSpan(control, span);
                column += span;
            }
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowCount++;
        }

        private static TableLayoutPanel GroupLayout(GroupBox group)
        {
            var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            group.Controls.Add(layout);
            return layout;
        }

        // A GroupBox measures its contents without letting their text wrap; this one
        // measures them at the width the window gives it, so a wrapped line still fits.
        private sealed class WrappingGroupBox : GroupBox
        {
            internal WrappingGroupBox()
            {
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Dock = DockStyle.Fill;
                Padding = new Padding(10, 6, 10, 8);
            }

            public override Size GetPreferredSize(Size proposedSize)
            {
                Rectangle inside = DisplayRectangle;
                Size chrome = Size - inside.Size;
                bool constrained = proposedSize.Width > chrome.Width && proposedSize.Width < int.MaxValue / 2;
                Size content = Controls.Count == 0 ? Size.Empty : Controls[0].GetPreferredSize(constrained ? new Size(proposedSize.Width - chrome.Width, 0) : Size.Empty);
                return new Size(constrained ? proposedSize.Width : content.Width + chrome.Width, content.Height + chrome.Height);
            }
        }

        private void AlignChoices()
        {
            if (_choices.Count == 0)
            {
                return;
            }
            // Where the group's rows start, also before the group has been placed.
            int rows = _installGroup.Parent.Padding.Left + _installGroup.Margin.Left + _installGroup.DisplayRectangle.Left;
            int labels = _game.Left - rows - _choices[0].Label.Margin.Horizontal - _choices[0].Box.Margin.Left;
            foreach (var (_, label, _) in _choices)
            {
                // A label longer than that just makes the column wider.
                label.MinimumSize = new Size(Math.Max(0, labels), 0);
            }
        }

        // A bulleted list whose lines wrap to the window's width.
        private static TableLayoutPanel StepList()
        {
            var list = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            list.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return list;
        }

        private static void FillSteps(TableLayoutPanel list, IList<string> steps)
        {
            list.SuspendLayout();
            while (list.Controls.Count > 0)
            {
                list.Controls[0].Dispose();
            }
            list.RowStyles.Clear();
            list.RowCount = 0;
            foreach (string step in steps)
            {
                AddRow(list,
                    new Label { AutoSize = true, Text = "\u2022", Margin = new Padding(4, 1, 6, 1) }, 1,
                    new Label { AutoSize = true, Dock = DockStyle.Fill, Text = step, UseMnemonic = false, Margin = new Padding(0, 1, 0, 1) }, 1);
            }
            list.ResumeLayout();
        }

        private void UpdateTexts()
        {
            Text = Strings.Get(Strings.Key.Title, _manifest.Name);
            Font = Strings.UiFont();
            if (_heading.Font.FontFamily.Name != Font.FontFamily.Name)
            {
                _heading.Font = new Font(Font.FontFamily, 13f, FontStyle.Bold);
            }
            _heading.Text = $"{_manifest.Name} {_manifest.Version}";
            _languageLabel.Text = Strings.Get(Strings.Key.Language);
            _gameLabel.Text = Strings.Get(Strings.Key.GameFolder);
            _browse.Text = Strings.Get(Strings.Key.Browse);
            _actionLabel.Text = Strings.Get(Strings.Key.Action);
            _modeInstall.Text = Strings.Get(Strings.Key.Install);
            _modeUninstall.Text = Strings.Get(Strings.Key.Uninstall);
            _nothingToUninstall.Text = Strings.Get(Strings.Key.NothingToUninstall);
            _installGroup.Text = Strings.Get(Strings.Key.Install);
            _uninstallGroup.Text = Strings.Get(Strings.Key.Uninstall);
            _installWill.Text = Strings.Get(Strings.Key.InstallWill);
            _uninstallWill.Text = Strings.Get(Strings.Key.UninstallWill);
            _keepData.Text = Strings.Get(Strings.Key.KeepData);
            _alsoBepInEx.Text = Strings.Get(Strings.Key.AlsoBepInEx);
            _close.Text = Strings.Get(Strings.Key.Close);
            _website.Text = Strings.Get(Strings.Key.Website);
            foreach (var (choice, label, _) in _choices)
            {
                label.Text = choice.LabelFor(Strings.Current);
            }
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            string game = _game.Text.Trim();
            bool found = InstallerCore.IsGameFolder(game);
            _installed = found ? _core.InstalledVersion(game) : null;
            _run.Enabled = found;
            _modeUninstall.Enabled = _installed != null;
            _nothingToUninstall.Visible = found && _installed == null;
            if (_installed == null)
            {
                _modeInstall.Checked = true;
            }
            if (!found)
            {
                _status.Text = Strings.Get(Strings.Key.NotFound);
                ApplyMode();
                return;
            }
            _status.Text = $"{Strings.Get(Strings.Key.StatusBepInEx)}: {Strings.Get(InstallerCore.HasBepInEx(game) ? Strings.Key.Yes : Strings.Key.No)}    " +
                           $"{Strings.Get(Strings.Key.StatusMod)}: {_installed ?? Strings.Get(Strings.Key.No)}";
            foreach (var (choice, _, box) in _choices)
            {
                if (box.SelectedIndex < 0)
                {
                    string value = choice.DefaultValue(_core.ReadConfig(game, choice.Config));
                    box.SelectedIndex = Array.FindIndex(choice.Options, o => o.Value == value);
                }
            }
            ApplyMode();
        }

        private void ApplyMode()
        {
            bool install = _modeInstall.Checked;
            _installGroup.Visible = install;
            _uninstallGroup.Visible = !install;
            if (!_busy)
            {
                _progressRow.Visible = false;
            }
            _run.Text = Strings.Get(!install ? Strings.Key.UninstallButton : _installed == null ? Strings.Key.InstallButton : Strings.Key.UpdateButton);
            // Installing: why Windows may warn about this file. Uninstalling: the other way to do it.
            _hint.Text = Strings.Get(install ? Strings.Key.SmartScreenHint : Strings.Key.SteamLaunchHint);
            RefreshPlan();
        }

        // What the chosen action will do, shown before it runs instead of a question afterwards.
        private void RefreshPlan()
        {
            bool install = _modeInstall.Checked;
            string game = _game.Text.Trim();
            List<string> steps = null;
            if (InstallerCore.IsGameFolder(game))
            {
                try
                {
                    steps = install ? _core.InstallPlan(game, SelectedChoices()) : _core.UninstallPlan(game, _keepData.Checked, _alsoBepInEx.Checked);
                }
                catch (Exception)
                {
                    // A folder that cannot be read; Install or Uninstall will say why.
                }
            }
            (install ? _installWill : _uninstallWill).Visible = steps != null;
            FillSteps(install ? _installSteps : _uninstallSteps, steps ?? new List<string>());
        }

        private Dictionary<string, string> SelectedChoices()
        {
            return _choices.Where(c => c.Box.SelectedIndex >= 0).ToDictionary(c => c.Choice.Id, c => c.Choice.Options[c.Box.SelectedIndex].Value);
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = Strings.Get(Strings.Key.NotFound), ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _game.Text = dialog.SelectedPath;
                }
            }
        }

        private void Run(bool install)
        {
            string game = _game.Text.Trim();
            Dictionary<string, string> choices = SelectedChoices();
            bool keepData = _keepData.Checked;
            bool alsoBepInEx = _alsoBepInEx.Checked;
            _cancel = new CancellationTokenSource();
            CancellationToken cancel = _cancel.Token;
            var progress = new Progress<InstallProgress>(ShowProgress);

            SetBusy(true);
            ShowProgress(install && InstallerCore.IsGameFolder(game) && !InstallerCore.HasBepInEx(game) ? InstallProgress.Downloaded(0) : InstallProgress.Changing);
            _log.Clear();
            Task.Run(() =>
            {
                // .NET's own messages in the log and the error details stay English too.
                Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
                try
                {
                    if (install)
                    {
                        _core.Install(game, choices, progress: progress, cancel: cancel);
                    }
                    else
                    {
                        _core.Uninstall(game, keepData, alsoBepInEx);
                    }
                    return (Cancelled: false, Error: (Exception)null, Details: (string)null);
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    AppendLog("Cancelled; the game folder was not changed");
                    return (Cancelled: true, Error: null, Details: null);
                }
                catch (InstallerException ex)
                {
                    AppendLog("ERROR: " + ex.Message);
                    return (Cancelled: false, Error: ex, Details: ErrorDialog.Details(ex));
                }
                catch (Exception ex)
                {
                    AppendLog("ERROR: " + ex);
                    return (Cancelled: false, Error: ex, Details: ErrorDialog.Details(ex));
                }
            }).ContinueWith(t =>
            {
                SetBusy(false);
                _cancel.Dispose();
                _cancel = null;
                if (_closeWhenDone)
                {
                    Close();
                    return;
                }
                RefreshStatus();
                if (!t.Result.Cancelled && t.Result.Error == null)
                {
                    MessageBox.Show(this, Strings.Get(install ? Strings.Key.Installed : Strings.Key.Uninstalled), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                // The progress row says how it ended until the next change in the window.
                _progressRow.Visible = true;
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = 0;
                _percent.Text = "";
                if (t.Result.Cancelled)
                {
                    _progressText.Text = Strings.Get(Strings.Key.Cancelled);
                    return;
                }
                _progressText.Text = Strings.Get(Strings.Key.Stopped);
                using (var dialog = new ErrorDialog(Text, t.Result.Error, t.Result.Details + Environment.NewLine + AboutThisRun()))
                {
                    if (dialog.ShowDialog(this) == DialogResult.Retry)
                    {
                        Run(install);
                    }
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // The last lines of "Copy details": enough to reproduce a report.
        private string AboutThisRun()
        {
            return $"{_manifest.Name} {_manifest.Version}, Install.exe {typeof(MainForm).Assembly.GetName().Version.ToString(3)}" + Environment.NewLine +
                   $"Installer language: {Strings.Current}   Windows {Environment.OSVersion.Version}   {RuntimeInformation.FrameworkDescription}";
        }

        private void ShowProgress(InstallProgress progress)
        {
            if (!_busy)
            {
                return;
            }
            bool known = progress.Downloading && progress.Percent >= 0;
            _progressText.Text = progress.Downloading
                ? Strings.Get(Strings.Key.Downloading, Paths.BepInExVersion, new Uri(Paths.BepInExUrl).Host)
                : Strings.Get(Strings.Key.Working);
            _progress.Style = known ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
            _progress.Value = known ? progress.Percent : 0;
            _percent.Text = known ? progress.Percent + "%" : "";
            _close.Enabled = progress.Downloading && !_cancel.IsCancellationRequested;
            UseWaitCursor = !progress.Downloading;
        }

        // True when a download was stopped; false when the game folder is being changed and it cannot be.
        private bool StopDownload()
        {
            if (_cancel == null || !_close.Enabled)
            {
                return false;
            }
            _cancel.Cancel();
            _close.Enabled = false;
            return true;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UseWaitCursor = false;
            foreach (Control c in new Control[] { _run, _browse, _game, _modeInstall, _modeUninstall, _keepData, _alsoBepInEx, _language })
            {
                c.Enabled = !busy;
            }
            foreach (var (_, _, box) in _choices)
            {
                box.Enabled = !busy;
            }
            _close.Text = Strings.Get(busy ? Strings.Key.Cancel : Strings.Key.Close);
            _close.Enabled = !busy;
            _progressRow.Visible = busy;
        }

        private void AppendLog(string line)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), line);
                return;
            }
            _log.AppendText(line + Environment.NewLine);
        }
    }
}
