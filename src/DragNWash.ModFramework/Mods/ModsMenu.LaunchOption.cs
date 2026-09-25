using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // Steam's launch option on the framework's Settings tab, at the top of
    // [Launcher]: the same switch as Install.exe's "Check for mod updates
    // when the game starts". It isn't in the config file. It shows what Steam
    // has now (Updates/LaunchOptionSwitch.cs), so it has no dot and no reset.
    // Only where the launcher can run: on Windows, with Launcher.exe there.
    //
    // A press asks first, as Update does: a band at the top of the details,
    // and Cancel and Restart and apply where the switch was, with the focus
    // on Restart and apply (A twice on a pad). Back, leaving the screen, or
    // picking another mod or tab takes it back. Restart and apply starts the
    // launcher and quits; the launcher closes Steam, changes the option and
    // starts Steam and the game again.
    internal sealed partial class ModsMenu
    {
        internal const string TextLaunchOption = "Check for mod updates when the game starts (Steam launch option)";
        internal const string TextLaunchOptionAbout = "Starts the game through the update launcher, which checks for mod updates first. This is a setting in Steam, so switching it restarts Steam and the game.";
        internal const string TextLaunchOptionOn = "Set in Steam's launch options now";
        internal const string TextLaunchOptionOff = "Not in Steam's launch options now";
        internal const string TextConfirmLaunchOption = "To switch this, the game quits, then Steam and the game start again. Progress you haven't saved may be lost.";
        internal const string TextRestartAndApply = "Restart and apply";
        internal const string TextLaunchOptionFailed = "Couldn't start the launcher, so nothing was switched. See BepInEx/LogOutput.log.";

        // The row's controls, by name, so the focus can be put back on them.
        private const string LaunchOptionToggle = "LaunchOptionToggle";
        private const string LaunchOptionCancel = "LaunchOptionCancel";
        private const string LaunchOptionApply = "LaunchOptionApply";

        // Asking to be applied: what the option would be switched to.
        private bool? _askingLaunchOption;

        // The launcher couldn't be started for it; said in a band until
        // something else is picked.
        private bool _launchOptionFailed;

        // The row on the tab showing, for the settings search.
        private GameObject _launchOptionRow;
        private string _launchOptionSection;

        private void BuildLaunchOptionRow(RectTransform content, string sectionTitle)
        {
            bool? state = Updates.LaunchOptionSwitch.State();
            if (state == null)
            {
                return;
            }
            bool on = state.Value;
            RectTransform row = RowFrame(content, "LaunchOption");
            _launchOptionRow = row.gameObject;
            _launchOptionSection = sectionTitle;
            RectTransform top = RowTop(row);
            RectTransform text = TextColumn(top);
            ModsLook.Text(text, "Title", TextLaunchOption, 22f, ModsLook.Label, FontStyles.Bold, true);
            ModsLook.Text(text, "Description", TextLaunchOptionAbout, 18f, ModsLook.Muted, FontStyles.Normal, true);
            ModsLook.Text(text, "Now", on ? TextLaunchOptionOn : TextLaunchOptionOff, 18f, ModsLook.Muted, FontStyles.Normal, true);
            RectTransform controls = Controls(top);
            if (_askingLaunchOption != null)
            {
                CreateBandButton(controls, new BandButton { Name = LaunchOptionCancel, Text = TextCancel, OnClick = CancelLaunchOption });
                CreateBandButton(controls, new BandButton { Name = LaunchOptionApply, Text = TextRestartAndApply, OnClick = ApplyLaunchOption, Asking = true });
            }
            else
            {
                SwitchButton(controls, LaunchOptionToggle, on, () => AskLaunchOption(!on));
            }
        }

        private bool LaunchOptionMatches()
        {
            string query = _settingsQuery;
            return query.Length == 0 || Contains(TextLaunchOption, query) || Contains(TextLaunchOptionAbout, query) ||
                   Contains(_launchOptionSection, query);
        }

        // The switch was pressed: ask first.
        private void AskLaunchOption(bool on)
        {
            _confirming = null;
            _confirmingUninstall = null;
            ForgetUpdate();
            _askingLaunchOption = on;
            RebuildSettingsFocus(LaunchOptionApply);
        }

        private void CancelLaunchOption()
        {
            _askingLaunchOption = null;
            RebuildSettingsFocus(LaunchOptionToggle);
        }

        // Restart and apply: the launcher takes over once the game has closed.
        // Nothing is said under the row, since nothing has changed yet.
        private void ApplyLaunchOption()
        {
            if (_askingLaunchOption == null)
            {
                return;
            }
            bool on = _askingLaunchOption.Value;
            _askingLaunchOption = null;
            if (Updates.LaunchOptionSwitch.QuitAndSwitch(on) == Updates.LaunchOptionSwitch.Outcome.Quitting)
            {
                return;
            }
            _launchOptionFailed = true;
            RebuildSettingsFocus(LaunchOptionToggle);
        }

        // The details again, for the band at the top and the row's controls,
        // keeping the settings where they were scrolled to.
        private void RebuildSettingsFocus(string focus)
        {
            float scroll = _settingsContent != null ? _settingsContent.anchoredPosition.y : 0f;
            RebuildDetails(false);
            if (_settingsContent != null)
            {
                // Laid out now, or the scroll view would find it empty and
                // put it back at the top.
                LayoutRebuilder.ForceRebuildLayoutImmediate(_settingsContent);
                _settingsContent.anchoredPosition = new Vector2(_settingsContent.anchoredPosition.x, scroll);
            }
            Focus(focus);
        }
    }
}
