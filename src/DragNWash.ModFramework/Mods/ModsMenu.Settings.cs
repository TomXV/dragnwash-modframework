using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // A mod's Settings tab: its BepInEx config entries by section, one row
    // each with the name, the description and the control side by side, so
    // the player can read while changing. Switches for on/off, sliders for
    // numbers with a range, - and + for other numbers, buttons for a few
    // choices, the key and Change for a shortcut, a swatch beside a colour.
    // A row whose value is not the default has a dot and a button back to it.
    // A warning about a row sits right under it.
    internal sealed partial class ModsMenu
    {
        internal const string TextSettings = "Settings";
        internal const string TextDefault = "Default";
        internal const string TextResetToDefault = "Reset to default";
        internal const string TextAfterRestart = "Some mods only use a change after a restart.";
        internal const string TextSaved = "Saved";
        internal const string TextEditInFile = "Change this in the mod's config file in BepInEx/config.";
        internal const string TextNotAccepted = "Not accepted";
        internal const string TextChange = "Change";
        internal const string TextPressKey = "Press a key...";
        internal const string TextShowAdvanced = "Show advanced settings";
        internal const string TextRestart = "Takes effect after the game restarts.";
        internal const string TextSameKeyAs = "Same key as";
        internal const string TextBothAnswer = "Both will answer it.";
        internal const string TextAllAnswer = "All of them will answer it.";
        internal const string TextSearchSettings = "Search settings";
        internal const string TextNoSettingsMatch = "No settings match.";
        private bool _showAdvanced;

        // What is typed in the search above the settings, kept while the
        // tab is built again for a change, and cleared for another mod.
        private string _settingsQuery = "";

        // Each section's spacer, heading and description, by section, hidden
        // when the search leaves the section empty.
        private readonly Dictionary<string, List<GameObject>> _sectionHeads = new Dictionary<string, List<GameObject>>();
        private GameObject _noSettingsMatch;

        // A mod with only a few settings needs no search.
        private const int SearchFromSettings = 6;

        // Changes are saved at once; a "Saved" tag on the row says so.
        private const float SavedSeconds = 2f;
        private ConfigItem _savedItem;
        private float _savedAt = float.NegativeInfinity;

        private List<ConfigItem> _items = new List<ConfigItem>();
        private readonly Dictionary<ConfigItem, SettingRow> _settingRows = new Dictionary<ConfigItem, SettingRow>();

        // Why a typed value was refused, shown under its row until the row is
        // built again for another reason.
        private readonly Dictionary<ConfigItem, string> _refused = new Dictionary<ConfigItem, string>();

        private RectTransform _settingsContent;

        // What changes on a row without building it again, so a slider being
        // dragged is not taken from under the pointer.
        private sealed class SettingRow
        {
            internal RectTransform Root;
            internal GameObject Dot;
            internal Button Reset;
            internal GameObject DefaultLine;
            internal TMP_InputField Field;
            internal Slider Slider;
            internal List<double> Positions;
        }

        // The settings on the page: advanced ones only when asked for.
        private IEnumerable<ConfigItem> Shown()
        {
            return _items.Where(i => _showAdvanced || !i.Advanced);
        }

        // Uses the settings TabsOf read for this build.
        private void BuildSettingsTab(ModCatalog.Entry entry)
        {
            _settingRows.Clear();
            _sectionHeads.Clear();
            _noSettingsMatch = null;
            _launchOptionRow = null;
            RectTransform content = ScrollArea(_body);
            content.GetComponent<VerticalLayoutGroup>().spacing = 8f;
            _settingsContent = content;

            bool searchable = _items.Count >= SearchFromSettings;
            if (searchable)
            {
                CreateSettingsSearch(content);
            }
            Line(content, TextAfterRestart, 18f, FontStyles.Italic, ModsLook.Muted, 0f);
            if (_items.Any(i => i.Advanced))
            {
                CreateAdvancedRow(content);
            }
            string section = null;
            foreach (ConfigItem item in Shown())
            {
                if (item.Section != section)
                {
                    section = item.Section;
                    var heads = new List<GameObject>();
                    Spacer(content, 6f);
                    heads.Add(LastChild(content));
                    Heading(content, Escape(item.SectionTitle));
                    heads.Add(LastChild(content));
                    if (!string.IsNullOrEmpty(item.SectionDescription))
                    {
                        Line(content, Escape(item.SectionDescription), 18f, FontStyles.Normal, ModsLook.Muted, 0f);
                        heads.Add(LastChild(content));
                    }
                    _sectionHeads[section ?? ""] = heads;
                    if (entry.IsFramework && section == Updates.LauncherUpdate.Section)
                    {
                        BuildLaunchOptionRow(content, item.SectionTitle);
                    }
                }
                _settingRows[item] = BuildSettingRow(content, item);
            }
            if (searchable)
            {
                Line(content, TextNoSettingsMatch, 20f, FontStyles.Italic, ModsLook.Muted, 0f);
                _noSettingsMatch = LastChild(content);
                ApplySettingsSearch();
            }
        }

        private static GameObject LastChild(Transform parent)
        {
            return parent.GetChild(parent.childCount - 1).gameObject;
        }

        // The search above the settings: finds them by name, key, description
        // or section, hiding the rest without building anything again.
        private void CreateSettingsSearch(RectTransform content)
        {
            RectTransform row = ModsLook.Rect(content, "SettingsSearchRow");
            ModsLook.Size(row.gameObject, -1f, 48f, 1f, 0f);
            TMP_InputField field = SearchBox(row, "SettingsSearch", TextSearchSettings, 20f, OnSettingsSearch);
            var box = (RectTransform)field.transform;
            box.anchorMin = new Vector2(0f, 0f);
            box.anchorMax = new Vector2(0f, 1f);
            box.pivot = new Vector2(0f, 0.5f);
            box.offsetMin = Vector2.zero;
            box.offsetMax = new Vector2(Mathf.Min(440f, InnerWidth), 0f);
            field.SetTextWithoutNotify(_settingsQuery);
        }

        private void OnSettingsSearch(string value)
        {
            string query = (value ?? "").Trim();
            if (query == _settingsQuery)
            {
                return;
            }
            _settingsQuery = query;
            ApplySettingsSearch();
        }

        private void ApplySettingsSearch()
        {
            var sections = new HashSet<string>();
            int shown = 0;
            foreach (KeyValuePair<ConfigItem, SettingRow> pair in _settingRows)
            {
                if (pair.Value.Root == null)
                {
                    continue;
                }
                bool match = SettingMatches(pair.Key);
                pair.Value.Root.gameObject.SetActive(match);
                if (match)
                {
                    shown++;
                    sections.Add(pair.Key.Section ?? "");
                }
            }
            if (_launchOptionRow != null)
            {
                bool match = LaunchOptionMatches();
                _launchOptionRow.SetActive(match);
                if (match)
                {
                    shown++;
                    sections.Add(Updates.LauncherUpdate.Section);
                }
            }
            foreach (KeyValuePair<string, List<GameObject>> pair in _sectionHeads)
            {
                foreach (GameObject head in pair.Value)
                {
                    if (head != null)
                    {
                        head.SetActive(sections.Contains(pair.Key));
                    }
                }
            }
            if (_noSettingsMatch != null)
            {
                _noSettingsMatch.SetActive(shown == 0 && _settingsQuery.Length > 0);
            }
        }

        private bool SettingMatches(ConfigItem item)
        {
            string query = _settingsQuery;
            return query.Length == 0 || Contains(item.Title, query) || Contains(item.Key, query) ||
                   Contains(item.Description, query) || Contains(item.SectionTitle, query);
        }

        // A row at the top of the tab that shows or hides the advanced settings.
        private void CreateAdvancedRow(RectTransform content)
        {
            RectTransform row = RowFrame(content, "Advanced");
            RectTransform top = RowTop(row);
            RectTransform text = TextColumn(top);
            ModsLook.Text(text, "Title", TextShowAdvanced, 22f, ModsLook.Label, FontStyles.Bold, true);
            RectTransform controls = Controls(top);
            SwitchButton(controls, "Advanced", _showAdvanced, () =>
            {
                _showAdvanced = !_showAdvanced;
                RebuildDetails(false);
                Focus("Advanced");
            });
        }

        private SettingRow BuildSettingRow(RectTransform content, ConfigItem item)
        {
            var built = new SettingRow();
            RectTransform row = RowFrame(content, "Setting " + item.Section + "." + item.Key);
            built.Root = row;

            // The dot left of the name: the value is not the default.
            RectTransform dot = ModsLook.Rect(row, "Changed");
            dot.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            dot.anchorMin = dot.anchorMax = new Vector2(0f, 1f);
            dot.pivot = new Vector2(0.5f, 0.5f);
            dot.sizeDelta = new Vector2(8f, 8f);
            dot.anchoredPosition = new Vector2(9f, -27f);
            ModsLook.Shape(dot.gameObject, ModsLook.Pill, ModsLook.Accent).raycastTarget = false;
            built.Dot = dot.gameObject;

            RectTransform top = RowTop(row);
            RectTransform text = TextColumn(top);
            ModsLook.Text(text, "Title", Escape(item.Title), 22f, ModsLook.Label, FontStyles.Bold, true);
            if (!string.IsNullOrEmpty(item.Description))
            {
                ModsLook.Text(text, "Description", item.Description, 18f, ModsLook.Muted, FontStyles.Normal, true);
            }
            if (item.RequiresRestart)
            {
                ModsLook.Text(text, "Restart", TextRestart, 18f, ModsLook.AccentText, FontStyles.Italic, true);
            }
            // What the reset button goes back to, while the value is another.
            RectTransform defaultLine = ModsLook.Rect(text, "Default");
            HorizontalLayoutGroup pair = defaultLine.gameObject.AddComponent<HorizontalLayoutGroup>();
            pair.spacing = 10f;
            pair.childControlWidth = true;
            pair.childControlHeight = true;
            pair.childForceExpandWidth = false;
            pair.childForceExpandHeight = false;
            TMP_Text defaultLabel = ModsLook.Text(defaultLine, "Label", TextDefault, 18f, ModsLook.Muted, FontStyles.Bold, false);
            ModsLook.Size(defaultLabel.gameObject, ModsLook.Width(defaultLabel), -1f, 0f, 0f);
            TMP_Text defaultValue = ModsLook.Text(defaultLine, "Value", Escape(item.DefaultText), 18f, ModsLook.Muted, FontStyles.Normal, false);
            LayoutElement defaultSize = ModsLook.Size(defaultValue.gameObject, -1f, -1f, 1f, 0f);
            defaultSize.minWidth = 0f;
            defaultSize.preferredWidth = 0f;
            built.DefaultLine = defaultLine.gameObject;

            RectTransform controls = Controls(top);
            switch (item.Type)
            {
                case ConfigItem.Kind.Toggle:
                    SwitchButton(controls, "Toggle", (bool)item.Entry.BoxedValue, () => Change(item, 1, "Toggle"));
                    break;
                case ConfigItem.Kind.Choice:
                    if (!ChoiceButtons(controls, item))
                    {
                        Stepper(controls, item, built, false);
                    }
                    break;
                case ConfigItem.Kind.Number:
                    if (item.HasRange && item.Max > item.Min)
                    {
                        built.Field = ValueField(controls, item, 120f);
                        SliderLine(row, item, built);
                    }
                    else
                    {
                        Stepper(controls, item, built, true);
                    }
                    break;
                case ConfigItem.Kind.Text:
                    if (item.IsShortcut)
                    {
                        built.Field = ValueField(controls, item, 180f);
                        // As wide as the key (F1 is short, Ctrl + Shift + F12 is
                        // not), so on a small screen the description keeps its room.
                        float keyWidth = Mathf.Ceil(built.Field.textComponent.GetPreferredValues(built.Field.text).x) + 28f;
                        ModsLook.Size(built.Field.gameObject, Mathf.Clamp(keyWidth, 72f, 200f), 44f, 0f, 0f);
                        GameObject change = FlatButton(controls, "Capture", TextChange, 20f, ModsLook.Raised, ModsLook.Label, null);
                        // Wide enough for "Press a key..." too, which it says while taking one.
                        TMP_Text changeLabel = change.GetComponentInChildren<TMP_Text>();
                        float changeWidth = Mathf.Max(((RectTransform)change.transform).sizeDelta.x,
                            Mathf.Ceil(changeLabel.GetPreferredValues(TextPressKey).x) + 34f);
                        ModsLook.Size(change, changeWidth, 44f, 0f, 0f);
                        change.GetComponent<Button>().onClick.AddListener(() => ToggleCapture(item, change));
                    }
                    else
                    {
                        if (item.IsColor)
                        {
                            Swatch(controls, item);
                        }
                        built.Field = ValueField(controls, item, 260f);
                    }
                    break;
                default:
                    TMP_Text shown = ModsLook.Text(controls, "Value", Escape(item.ValueText), 20f, ModsLook.Label, FontStyles.Bold, false);
                    ModsLook.Size(shown.gameObject, Mathf.Min(260f, ModsLook.Width(shown)), 44f, 0f, 0f);
                    break;
            }

            if (item.Type != ConfigItem.Kind.ReadOnly)
            {
                built.Reset = ResetButton(controls, item);
            }

            // Warnings about this row, under it.
            if (item.Type == ConfigItem.Kind.ReadOnly)
            {
                Band(row, "EditInFile", null, TextEditInFile, ModsLook.Muted);
            }
            List<string> shared = SharedKeyWarning(item);
            if (shared != null)
            {
                // The names are the mods' own words: not read as rich text.
                Band(row, "SharedKey", TextSameKeyAs, Escape(string.Join(", ", shared)), ModsLook.Warning,
                    note: KeyBindings.Answer(shared.Count));
                if (built.Field != null)
                {
                    RectTransform edge = ModsLook.Rect(built.Field.transform, "Edge");
                    ModsLook.Stretch(edge);
                    ModsLook.Shape(edge.gameObject, ModsLook.Outline, ModsLook.Warning, 10f).raycastTarget = false;
                }
            }
            if (_refused.TryGetValue(item, out string reason))
            {
                _refused.Remove(item);
                Band(row, "NotAccepted", TextNotAccepted, Escape(reason), ModsLook.Error);
            }

            float elapsed = Time.unscaledTime - _savedAt;
            if (ReferenceEquals(item, _savedItem) && elapsed < SavedSeconds)
            {
                SavedTag(row, SavedSeconds - elapsed);
            }
            Refresh(item, built);
            return built;
        }

        // The parts that follow the value: the dot, the reset button, the
        // default line, and the field and slider when not being used.
        private static void Refresh(ConfigItem item, SettingRow row)
        {
            bool changed = !item.IsDefault;
            row.Dot.SetActive(changed);
            row.DefaultLine.SetActive(changed && item.Type != ConfigItem.Kind.Toggle);
            if (row.Reset != null)
            {
                row.Reset.interactable = changed;
                CanvasGroup group = row.Reset.GetComponent<CanvasGroup>();
                group.alpha = changed ? 1f : 0.35f;
            }
            if (row.Field != null && !row.Field.isFocused)
            {
                row.Field.SetTextWithoutNotify(item.Type == ConfigItem.Kind.Number ? item.ValueText : item.SerializedText);
            }
            if (row.Slider != null)
            {
                row.Slider.SetValueWithoutNotify(NearestPosition(row.Positions, item));
            }
        }

        // ---- the pieces of a row ----

        // A setting's row: a see-through card with rounded corners, its
        // children in a column. Pages other mods add use it too (ModsScreenLook.Card).
        internal static RectTransform RowFrame(RectTransform content, string name)
        {
            RectTransform row = ModsLook.Rect(content, name);
            ModsLook.Shape(row.gameObject, ModsLook.Rounded, ModsLook.Card, 10f).raycastTarget = false;
            VerticalLayoutGroup column = row.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(20, 14, 12, 12);
            column.spacing = 8f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            return row;
        }

        // The name and description on the left, the controls on the right.
        private static RectTransform RowTop(RectTransform row)
        {
            RectTransform top = ModsLook.Rect(row, "Top");
            HorizontalLayoutGroup line = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            line.spacing = 16f;
            line.childAlignment = TextAnchor.UpperLeft;
            line.childControlWidth = true;
            line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = false;
            return top;
        }

        private static RectTransform TextColumn(RectTransform top)
        {
            RectTransform text = ModsLook.Rect(top, "Text");
            VerticalLayoutGroup lines = text.gameObject.AddComponent<VerticalLayoutGroup>();
            lines.spacing = 4f;
            lines.childControlWidth = true;
            lines.childControlHeight = true;
            lines.childForceExpandWidth = true;
            lines.childForceExpandHeight = false;
            LayoutElement size = ModsLook.Size(text.gameObject, -1f, -1f, 1f, 0f);
            size.minWidth = 0f;
            size.preferredWidth = 0f;
            return text;
        }

        private static RectTransform Controls(RectTransform top)
        {
            RectTransform controls = ModsLook.Rect(top, "Controls");
            HorizontalLayoutGroup line = controls.gameObject.AddComponent<HorizontalLayoutGroup>();
            line.spacing = 8f;
            line.childAlignment = TextAnchor.MiddleRight;
            line.childControlWidth = true;
            line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = false;
            ModsLook.Size(controls.gameObject, -1f, -1f, 0f, 0f);
            return controls;
        }

        // A switch that is a button: the track and knob show the value.
        private static GameObject SwitchButton(RectTransform parent, string name, bool on, UnityAction onClick)
        {
            RectTransform hit = ModsLook.Rect(parent, name);
            ModsLook.Size(hit.gameObject, 76f, 40f, 0f, 0f);
            Image face = hit.gameObject.AddComponent<Image>();
            face.color = ModsLook.Clear;
            Button button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            ModsLook.Colors(button, ModsLook.Clear, ModsLook.Clear, ModsLook.Clear);
            RectTransform track = ModsLook.Switch(hit, "Track", on, 76f, 40f);
            ModsLook.Stretch(track);
            button.onClick.AddListener(onClick);
            return hit.gameObject;
        }

        // Up to four choices as buttons side by side, the chosen one lit.
        // Returns false when they would not fit; the row then uses - and +.
        private bool ChoiceButtons(RectTransform parent, ConfigItem item)
        {
            if (item.Choices.Length > 4)
            {
                return false;
            }
            float budget = InnerWidth * 0.5f;
            var labels = item.Choices.Select(c => Escape(item.Format(c))).ToList();
            var built = new List<GameObject>();
            float total = 0f;
            for (int i = 0; i < item.Choices.Length; i++)
            {
                object choice = item.Choices[i];
                bool chosen = Equals(choice, item.Entry.BoxedValue);
                int index = i;
                GameObject button = FlatButton(parent, "Choice" + i, labels[i], 19f, chosen ? ModsLook.Accent : ModsLook.Raised,
                    chosen ? ModsLook.Inset : ModsLook.Label, () => Choose(item, index));
                float width = ((RectTransform)button.transform).sizeDelta.x;
                ModsLook.Size(button, width, 44f, 0f, 0f);
                built.Add(button);
                total += width + 8f;
            }
            if (total <= budget)
            {
                return true;
            }
            foreach (GameObject button in built)
            {
                button.SetActive(false);
                Destroy(button);
            }
            return false;
        }

        // - value + for choices and numbers without a range. A number's value
        // can be typed as well.
        private void Stepper(RectTransform parent, ConfigItem item, SettingRow row, bool typed)
        {
            StepButton(parent, "Step-", "-", () => Change(item, -1, "Step-"));
            if (typed)
            {
                row.Field = ValueField(parent, item, 120f);
            }
            else
            {
                TMP_Text value = ModsLook.Text(parent, "Value", Escape(item.ValueText), 20f, ModsLook.Label, FontStyles.Bold, false);
                value.alignment = TextAlignmentOptions.Center;
                ModsLook.Size(value.gameObject, Mathf.Clamp(ModsLook.Width(value) + 16f, 90f, 240f), 44f, 0f, 0f);
            }
            StepButton(parent, "Step+", "+", () => Change(item, 1, "Step+"));
        }

        private static void StepButton(RectTransform parent, string name, string text, UnityAction onClick)
        {
            GameObject button = FlatButton(parent, name, text, 26f, ModsLook.Raised, ModsLook.Label, onClick);
            ModsLook.Size(button, 50f, 44f, 0f, 0f);
        }

        // A line under the row with - , the slider and +. The slider stops at
        // the same values as the buttons, one step for each press of left or
        // right on the pad.
        private void SliderLine(RectTransform row, ConfigItem item, SettingRow built)
        {
            RectTransform line = ModsLook.Rect(row, "SliderLine");
            HorizontalLayoutGroup parts = line.gameObject.AddComponent<HorizontalLayoutGroup>();
            parts.spacing = 16f;
            parts.childAlignment = TextAnchor.MiddleLeft;
            parts.childControlWidth = true;
            parts.childControlHeight = true;
            parts.childForceExpandWidth = false;
            parts.childForceExpandHeight = false;

            StepButton(line, "Step-", "-", () => Change(item, -1, "Step-"));

            RectTransform area = ModsLook.Rect(line, "Slider");
            ModsLook.Size(area.gameObject, -1f, 40f, 1f, 0f);
            // Something under the whole area to catch the pointer.
            area.gameObject.AddComponent<Image>().color = ModsLook.Clear;

            RectTransform track = ModsLook.Rect(area, "Track");
            track.anchorMin = new Vector2(0f, 0.5f);
            track.anchorMax = new Vector2(1f, 0.5f);
            track.sizeDelta = new Vector2(0f, 8f);
            ModsLook.Shape(track.gameObject, ModsLook.Pill, ModsLook.Border).raycastTarget = false;

            RectTransform fillArea = ModsLook.Rect(area, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            // Inset like the handle's area, so the fill ends under the knob.
            fillArea.sizeDelta = new Vector2(-28f, 8f);
            RectTransform fill = ModsLook.Rect(fillArea, "Fill");
            ModsLook.Stretch(fill);
            ModsLook.Shape(fill.gameObject, ModsLook.Pill, ModsLook.Accent).raycastTarget = false;

            RectTransform handleArea = ModsLook.Rect(area, "Handle Slide Area");
            ModsLook.Stretch(handleArea, 14f, 0f, 14f, 0f);
            RectTransform handle = ModsLook.Rect(handleArea, "Handle");
            // The slider stretches the handle to its area's height (40); 12
            // less makes it 28 high.
            handle.sizeDelta = new Vector2(28f, -12f);
            Image knob = ModsLook.Shape(handle.gameObject, ModsLook.Pill, Color.white, 14f);
            RectTransform ring = ModsLook.Rect(handle, "Ring");
            ModsLook.Stretch(ring);
            ModsLook.Shape(ring.gameObject, ModsLook.PillOutline, ModsLook.Accent, 14f).raycastTarget = false;

            Slider slider = area.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = knob;
            slider.direction = Slider.Direction.LeftToRight;
            List<double> positions = item.Positions();
            slider.wholeNumbers = true;
            slider.minValue = 0f;
            slider.maxValue = Mathf.Max(1, positions.Count - 1);
            slider.SetValueWithoutNotify(NearestPosition(positions, item));
            ModsLook.Colors(slider, ModsLook.Label, Color.white, ModsLook.Muted);
            // Left and right move the slider, up and down go to the next row.
            // With a neighbour on the left, Unity's slider would go there instead.
            slider.navigation = new Navigation { mode = Navigation.Mode.Vertical };
            built.Slider = slider;
            built.Positions = positions;
            slider.onValueChanged.AddListener(v =>
            {
                int index = Mathf.Clamp(Mathf.RoundToInt(v), 0, positions.Count - 1);
                string before = item.SerializedText;
                item.SetNumber(positions[index]);
                MarkSaved(item, before);
                // Only the parts that follow the value: the slider stays in hand.
                Refresh(item, built);
                if (ReferenceEquals(item, _savedItem) && item.SerializedText != before)
                {
                    SavedTag(built.Root, SavedSeconds);
                }
            });

            StepButton(line, "Step+", "+", () => Change(item, 1, "Step+"));
        }

        private static int NearestPosition(List<double> positions, ConfigItem item)
        {
            if (positions == null || positions.Count == 0)
            {
                return 0;
            }
            double value = Convert.ToDouble(item.Entry.BoxedValue, CultureInfo.InvariantCulture);
            int best = 0;
            for (int i = 1; i < positions.Count; i++)
            {
                if (Math.Abs(positions[i] - value) < Math.Abs(positions[best] - value))
                {
                    best = i;
                }
            }
            return best;
        }

        // A field for values BepInEx reads as text (strings, shortcuts,
        // colours, numbers typed in). Enter or leaving the field applies the
        // text; a value the config parser refuses is put back and the reason
        // shown under the row.
        private TMP_InputField ValueField(RectTransform parent, ConfigItem item, float width)
        {
            RectTransform box = ModsLook.Rect(parent, "Field");
            ModsLook.Size(box.gameObject, width, 44f, 0f, 0f);
            // Built inactive: the input field looks for its text component when
            // it wakes, which must be set by then.
            box.gameObject.SetActive(false);
            Image background = ModsLook.Shape(box.gameObject, ModsLook.Rounded, Color.white, 10f);
            // A thin edge, so the see-through field still reads as a box.
            RectTransform edge = ModsLook.Rect(box, "Edge");
            ModsLook.Stretch(edge);
            ModsLook.Shape(edge.gameObject, ModsLook.Outline, ModsLook.FieldEdge, 8f).raycastTarget = false;

            RectTransform area = ModsLook.Rect(box, "Text Area");
            ModsLook.Stretch(area, 12f, 4f, 12f, 4f);
            area.gameObject.AddComponent<RectMask2D>();

            TMP_Text text = ModsLook.Text(area, "Text", "", 20f, ModsLook.Label, FontStyles.Normal, false);
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            if (item.Type == ConfigItem.Kind.Number || item.IsShortcut)
            {
                text.alignment = TextAlignmentOptions.Center;
            }

            TMP_InputField field = box.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = background;
            field.textViewport = area;
            field.textComponent = text;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.customCaretColor = true;
            field.caretColor = ModsLook.Label;
            field.selectionColor = new Color(ModsLook.Accent.r, ModsLook.Accent.g, ModsLook.Accent.b, 0.4f);
            ModsLook.Colors(field, ModsLook.Field, ModsLook.Hover, ModsLook.Field);
            // The pad passing over it does not start typing (on the Steam Deck
            // that would open the keyboard); A does.
            field.shouldActivateOnSelect = false;
            field.onEndEdit.AddListener(value => ApplyText(item, field, value));
            box.gameObject.SetActive(true);
            // Only now: text given to the field while it was inactive was not shown.
            field.text = item.Type == ConfigItem.Kind.Number ? item.ValueText : item.SerializedText;
            field.ForceLabelUpdate();
            return field;
        }

        // A square of the colour, beside its text.
        private static void Swatch(RectTransform parent, ConfigItem item)
        {
            RectTransform swatch = ModsLook.Rect(parent, "Swatch");
            ModsLook.Size(swatch.gameObject, 40f, 40f, 0f, 0f);
            Color color = item.Entry.BoxedValue is Color c ? c : Color.clear;
            color.a = 1f;
            ModsLook.Shape(swatch.gameObject, ModsLook.Rounded, color, 8f).raycastTarget = false;
            RectTransform edge = ModsLook.Rect(swatch, "Edge");
            ModsLook.Stretch(edge);
            ModsLook.Shape(edge.gameObject, ModsLook.Outline, ModsLook.Border, 8f).raycastTarget = false;
        }

        // The arrow going round: back to the default. Dim and passed over by
        // the pad while the value is the default.
        private Button ResetButton(RectTransform parent, ConfigItem item)
        {
            GameObject button;
            if (ModsLook.ResetArrow != null)
            {
                button = FlatButton(parent, "Reset", "", 20f, ModsLook.Raised, ModsLook.Label, () => Reset(item));
                RectTransform icon = ModsLook.Rect(button.transform, "Icon");
                icon.sizeDelta = new Vector2(26f, 26f);
                Image image = icon.gameObject.AddComponent<Image>();
                image.sprite = ModsLook.ResetArrow;
                image.color = ModsLook.Label;
                image.raycastTarget = false;
                ModsLook.Size(button, 50f, 44f, 0f, 0f);
            }
            else
            {
                button = FlatButton(parent, "Reset", TextResetToDefault, 18f, ModsLook.Raised, ModsLook.Label, () => Reset(item));
                ModsLook.Size(button, ((RectTransform)button.transform).sizeDelta.x, 44f, 0f, 0f);
            }
            button.AddComponent<CanvasGroup>();
            return button.GetComponent<Button>();
        }

        // "Saved" on the row's top edge for two seconds after each change (a
        // reset too). A value set to what it already was is not a change.
        private void MarkSaved(ConfigItem item, string before)
        {
            if (item != null && item.SerializedText != before)
            {
                _savedItem = item;
                _savedAt = Time.unscaledTime;
            }
        }

        private static void SavedTag(RectTransform row, float seconds)
        {
            Transform old = row.Find("Saved");
            if (old != null)
            {
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
            RectTransform tag = ModsLook.Rect(row, "Saved");
            tag.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            ModsLook.Shape(tag.gameObject, ModsLook.Pill, ModsLook.Inset, 13f).raycastTarget = false;
            RectTransform edge = ModsLook.Rect(tag, "Edge");
            ModsLook.Stretch(edge);
            ModsLook.Shape(edge.gameObject, ModsLook.PillOutline, ModsLook.Accent, 13f).raycastTarget = false;
            TMP_Text label = ModsLook.Text(tag, "Label", TextSaved, 17f, ModsLook.AccentText, FontStyles.Bold, false);
            label.alignment = TextAlignmentOptions.Center;
            tag.anchorMin = tag.anchorMax = new Vector2(1f, 1f);
            tag.pivot = new Vector2(1f, 0.5f);
            tag.sizeDelta = new Vector2(ModsLook.Width(label) + 24f, 26f);
            tag.anchoredPosition = new Vector2(-16f, 0f);
            tag.gameObject.AddComponent<SavedTagTimer>().Until = Time.unscaledTime + seconds;
        }

        // Another setting on the same key, said where the note is: right after
        // a key is captured or typed, and whenever a setting that already
        // clashes is opened. A report only; the key stays as the player set it,
        // since one key doing two things may be just what they want.
        // Its parts are shown apart, each fixed sentence a text of its own,
        // so a language pack can translate them.
        private List<string> SharedKeyWarning(ConfigItem item)
        {
            if (!item.IsShortcut)
            {
                return null;
            }
            try
            {
                return KeyBindings.NamesSharing(item.Entry);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not look for other mods on the key of {item.Section}.{item.Key}: {ex.Message}");
                return null;
            }
        }

        // ---- changing a value ----

        private void ApplyText(ConfigItem item, TMP_InputField field, string value)
        {
            if (item == null || field == null || !field.gameObject.activeInHierarchy)
            {
                return;
            }
            string current = item.Type == ConfigItem.Kind.Number ? item.ValueText : item.SerializedText;
            if (value == current)
            {
                return;
            }
            string before = item.SerializedText;
            string error = item.SetText(value);
            if (error != null)
            {
                _refused[item] = error;
            }
            else
            {
                MarkSaved(item, before);
            }
            // Not now: the edit ends because the selection is changing (a
            // button was pressed), and building the row now would destroy that
            // button in the middle of it. At the end of the frame instead.
            _pending += () =>
            {
                // The tab may have been left in the meantime.
                if (_tab != TabSettings || _settingsContent == null || !_settingsContent.gameObject.activeInHierarchy)
                {
                    return;
                }
                // A shortcut can start or stop clashing with other rows' keys.
                if (item.IsShortcut)
                {
                    RebuildSettingsKeeping(item, "Field");
                }
                else
                {
                    RebuildRow(item, "Field");
                }
            };
        }

        // Back while typing in a setting's field: the value it had goes back
        // in and typing stops, so ending the edit saves nothing.
        private bool StopTyping(GameObject focused)
        {
            TMP_InputField field = focused.GetComponent<TMP_InputField>();
            if (field == null || !field.isFocused)
            {
                return false;
            }
            foreach (KeyValuePair<ConfigItem, SettingRow> pair in _settingRows)
            {
                if (ReferenceEquals(pair.Value.Field, field))
                {
                    ConfigItem item = pair.Key;
                    field.SetTextWithoutNotify(item.Type == ConfigItem.Kind.Number ? item.ValueText : item.SerializedText);
                    field.DeactivateInputField();
                    return true;
                }
            }
            return false;
        }

        // Starts taking the next key for a shortcut, or stops if already taking one.
        private void ToggleCapture(ConfigItem item, GameObject button)
        {
            ShortcutCapture running = button.GetComponent<ShortcutCapture>();
            if (running != null)
            {
                running.Cancel();
                return;
            }
            ShortcutCapture capture = button.AddComponent<ShortcutCapture>();
            capture.Menu = this;
            capture.Item = item;
            capture.Label = button.GetComponentInChildren<TMP_Text>();
        }

        internal void AfterCapture(ConfigItem item, string before)
        {
            MarkSaved(item, before);
            RebuildSettingsKeeping(item, "Capture");
        }

        private void Change(ConfigItem item, int direction, string focus)
        {
            string before = item.SerializedText;
            item.Step(direction);
            MarkSaved(item, before);
            RebuildRow(item, focus);
        }

        private void Choose(ConfigItem item, int index)
        {
            string before = item.SerializedText;
            item.Choose(index);
            MarkSaved(item, before);
            RebuildRow(item, "Choice" + index);
        }

        private void Reset(ConfigItem item)
        {
            string before = item.SerializedText;
            item.ResetToDefault();
            MarkSaved(item, before);
            // The reset button is passed over now; the pad goes to the control.
            if (item.IsShortcut)
            {
                RebuildSettingsKeeping(item, "Capture");
            }
            else
            {
                RebuildRow(item, "Toggle", "Choice0", "Step+", "Field", "Slider");
            }
        }

        // Builds one row again in its place, and puts the pad back on the
        // first of the named controls it has. Nothing else on the tab moves.
        private void RebuildRow(ConfigItem item, params string[] focus)
        {
            if (_settingsContent == null || !_settingRows.TryGetValue(item, out SettingRow old) || old.Root == null)
            {
                RebuildSettingsKeeping(item, focus);
                return;
            }
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            bool onRow = selected != null && selected.transform.IsChildOf(old.Root);
            int index = old.Root.GetSiblingIndex();
            old.Root.gameObject.SetActive(false);
            Destroy(old.Root.gameObject);
            SettingRow row = BuildSettingRow(_settingsContent, item);
            row.Root.SetSiblingIndex(index);
            _settingRows[item] = row;
            RefocusRow(row, onRow ? selected.name : null, onRow || selected == null || !selected.activeInHierarchy, focus);
        }

        // Puts the pad back on the row: on the control it was on, else on the
        // first of `focus` it has. Selecting a field does not start typing
        // (shouldActivateOnSelect is off), so the Steam Deck's keyboard stays shut.
        private static void RefocusRow(SettingRow row, string was, bool move, string[] focus)
        {
            if (was != null && FocusIn(row.Root, was))
            {
                return;
            }
            if (!move)
            {
                return;
            }
            foreach (string name in focus)
            {
                if (FocusIn(row.Root, name))
                {
                    return;
                }
            }
        }

        // The whole tab again, for changes that reach other rows.
        private void RebuildSettingsKeeping(ConfigItem item, params string[] focus)
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            bool onRow = selected != null && _settingRows.TryGetValue(item, out SettingRow old) && old.Root != null && selected.transform.IsChildOf(old.Root);
            bool lost = selected == null || !selected.activeInHierarchy || Details != null && selected.transform.IsChildOf(Details);
            float scroll = _settingsContent != null ? _settingsContent.anchoredPosition.y : 0f;
            RebuildDetails(false);
            if (_settingsContent != null)
            {
                // Laid out now, or the scroll view would find it empty and
                // put it back at the top.
                LayoutRebuilder.ForceRebuildLayoutImmediate(_settingsContent);
                _settingsContent.anchoredPosition = new Vector2(_settingsContent.anchoredPosition.x, scroll);
            }
            if (_settingRows.TryGetValue(item, out SettingRow row))
            {
                RefocusRow(row, onRow ? selected.name : null, onRow || lost, focus);
            }
        }

        // Selection moves to a button of the rebuilt panel so a pad user keeps
        // their place; a button held while the mods are checked is passed over.
        private void Focus(params string[] names)
        {
            if (EventSystem.current == null)
            {
                return;
            }
            foreach (string name in names)
            {
                if (FocusIn(Details, name))
                {
                    return;
                }
            }
        }
    }

    // Hides the "Saved" tag when its time is up, in real time, so a paused
    // game does not keep it up.
    internal sealed class SavedTagTimer : MonoBehaviour
    {
        internal float Until;

        private void Update()
        {
            if (Time.unscaledTime >= Until)
            {
                gameObject.SetActive(false);
            }
        }
    }
}
