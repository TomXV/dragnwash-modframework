using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
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
        private readonly Label _gameLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
        private readonly TextBox _game = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly Button _browse = new Button { AutoSize = true };
        private readonly Label _status = new Label { AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
        private readonly List<(ModChoice Choice, Label Label, ComboBox Box)> _choices = new List<(ModChoice, Label, ComboBox)>();
        private readonly CheckBox _keepData = new CheckBox { AutoSize = true, Checked = true };
        private readonly CheckBox _alsoBepInEx = new CheckBox { AutoSize = true };
        private readonly Button _install = new Button { AutoSize = true, MinimumSize = new Size(140, 34) };
        private readonly Button _uninstall = new Button { AutoSize = true, MinimumSize = new Size(140, 34) };
        private readonly LinkLabel _website = new LinkLabel { AutoSize = true, Anchor = AnchorStyles.Right };
        private readonly Label _hint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 6) };
        private readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9f) };

        internal MainForm(ModManifest manifest, string payload)
        {
            _manifest = manifest;
            _core = new InstallerCore(manifest, payload, AppendLog);

            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(620, 520);
            Size = new Size(720, 600);
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

            var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
            top.Controls.Add(_language);
            top.Controls.Add(_languageLabel);
            AddRow(layout, _heading, 2, top, 1);

            AddRow(layout, _gameLabel, 1, _game, 1, _browse, 1);
            AddRow(layout, _status, 3);

            foreach (ModChoice choice in manifest.Choices)
            {
                var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
                var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left, Width = 260 };
                foreach (ModChoiceOption option in choice.Options)
                {
                    box.Items.Add(option.Name ?? option.Value);
                }
                _choices.Add((choice, label, box));
                AddRow(layout, label, 1, box, 2);
            }

            AddRow(layout, _keepData, 3);
            AddRow(layout, _alsoBepInEx, 3);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(_install);
            buttons.Controls.Add(_uninstall);
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
            _install.Click += (_, __) => Run(install: true);
            _uninstall.Click += (_, __) => Run(install: false);
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

        private void UpdateTexts()
        {
            Text = Strings.Get(Strings.Key.Title, _manifest.Name);
            _heading.Text = $"{_manifest.Name} {_manifest.Version}";
            _languageLabel.Text = Strings.Get(Strings.Key.Language);
            _gameLabel.Text = Strings.Get(Strings.Key.GameFolder);
            _browse.Text = Strings.Get(Strings.Key.Browse);
            _keepData.Text = Strings.Get(Strings.Key.KeepData);
            _alsoBepInEx.Text = Strings.Get(Strings.Key.AlsoBepInEx);
            _uninstall.Text = Strings.Get(Strings.Key.Uninstall);
            _website.Text = Strings.Get(Strings.Key.Website);
            _hint.Text = Strings.Get(Strings.Key.SteamLaunchHint);
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
            string installed = found ? _core.InstalledVersion(game) : null;
            _install.Text = Strings.Get(installed == null ? Strings.Key.Install : Strings.Key.Update);
            _uninstall.Enabled = found && installed != null;
            _install.Enabled = found;
            if (!found)
            {
                _status.Text = Strings.Get(Strings.Key.NotFound);
                return;
            }
            _status.Text = $"{Strings.Get(Strings.Key.StatusBepInEx)}: {Strings.Get(InstallerCore.HasBepInEx(game) ? Strings.Key.Yes : Strings.Key.No)}    " +
                           $"{Strings.Get(Strings.Key.StatusMod)}: {installed ?? Strings.Get(Strings.Key.No)}";
            foreach (var (choice, _, box) in _choices)
            {
                if (box.SelectedIndex < 0)
                {
                    string value = choice.DefaultValue(_core.ReadConfig(game, choice.Config));
                    box.SelectedIndex = Array.FindIndex(choice.Options, o => o.Value == value);
                }
            }
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
            if (!install && MessageBox.Show(this, Strings.Get(Strings.Key.ConfirmUninstall, _manifest.Name), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            var choices = _choices.Where(c => c.Box.SelectedIndex >= 0).ToDictionary(c => c.Choice.Id, c => c.Choice.Options[c.Box.SelectedIndex].Value);
            bool keepData = _keepData.Checked;
            bool alsoBepInEx = _alsoBepInEx.Checked;

            SetBusy(true);
            _log.Clear();
            Task.Run(() =>
            {
                try
                {
                    if (install)
                    {
                        _core.Install(game, choices);
                    }
                    else
                    {
                        _core.Uninstall(game, keepData, alsoBepInEx);
                    }
                    return (Ok: true, Message: Strings.Get(install ? Strings.Key.Installed : Strings.Key.Uninstalled));
                }
                catch (InstallerException ex)
                {
                    AppendLog("ERROR: " + ex.Message);
                    return (Ok: false, Message: Strings.Get(ex.Key) + (ex.Detail == null ? "" : Environment.NewLine + ex.Detail));
                }
                catch (Exception ex)
                {
                    AppendLog("ERROR: " + ex);
                    return (Ok: false, Message: Strings.Get(Strings.Key.Failed, ex.Message));
                }
            }).ContinueWith(t =>
            {
                SetBusy(false);
                RefreshStatus();
                MessageBox.Show(this, t.Result.Message, Text, MessageBoxButtons.OK, t.Result.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void SetBusy(bool busy)
        {
            UseWaitCursor = busy;
            foreach (Control c in new Control[] { _install, _uninstall, _browse, _game, _keepData, _alsoBepInEx, _language })
            {
                c.Enabled = !busy;
            }
            foreach (var (_, _, box) in _choices)
            {
                box.Enabled = !busy;
            }
            if (busy)
            {
                _status.Text = Strings.Get(Strings.Key.Working);
            }
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
