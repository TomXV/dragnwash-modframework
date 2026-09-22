using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // A mod's settings page: its BepInEx config entries in the list on the left,
    // the selected entry's description and controls on the right. Back (the
    // game's button, Esc or the pad's cancel) returns to the list of mods.
    internal sealed partial class ModsMenu
    {
        internal const string TextSettings = "Settings";
        internal const string TextDefault = "Default";
        internal const string TextResetToDefault = "Reset to default";
        internal const string TextSavedAtOnce = "Saved right away. Some mods only use a change after a restart.";
        internal const string TextEditInFile = "Change this in the mod's config file in BepInEx/config.";
        internal const string TextNotAccepted = "Not accepted: ";
        internal const string TextCaptureKey = "Capture key";
        internal const string TextPressKey = "Press a key...";
        internal const string TextShowAdvanced = "Show advanced settings";
        internal const string TextRestart = "Takes effect after the game restarts.";
        private bool _showAdvanced;

        private static readonly Color SettingsColor = new Color(0.3f, 0.42f, 0.62f, 1f);
        private static readonly Color StepColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color FieldColor = new Color(0.12f, 0.12f, 0.14f, 1f);
        private static readonly Color NoteErrorColor = new Color(1f, 0.55f, 0.5f, 1f);

        private TMP_Text _settingNote;

        private ModCatalog.Entry _settingsFor;
        private List<ConfigItem> _items = new List<ConfigItem>();
        private ConfigItem _item;

        private void OpenSettings(ModCatalog.Entry entry)
        {
            _items = ConfigItem.For(entry);
            if (_items.Count == 0)
            {
                return;
            }
            _settingsFor = entry;
            _showAdvanced = false;
            _item = Shown().FirstOrDefault() ?? _items[0];
            RebuildList();
            RebuildDetails(false);
            Focus("Step+", "Toggle", "Reset");
        }

        private void CloseSettings()
        {
            _selected = _settingsFor ?? _selected;
            _settingsFor = null;
            _item = null;
            RebuildList();
            RebuildDetails(false);
            Focus("Settings");
        }

        internal void SelectItem(ConfigItem item)
        {
            if (item == null || ReferenceEquals(item, _item))
            {
                return;
            }
            _item = item;
            RebuildDetails(false);
        }

        // The settings on the page: advanced ones only when asked for.
        private IEnumerable<ConfigItem> Shown()
        {
            return _items.Where(i => _showAdvanced || !i.Advanced);
        }

        private void BuildSettingsList()
        {
            if (_items.Any(i => i.Advanced))
            {
                _rows.Add(CreateAdvancedRow());
            }
            string section = null;
            foreach (ConfigItem item in Shown())
            {
                if (item.Section != section)
                {
                    section = item.Section;
                    _rows.Add(CreateSectionRow(item));
                }
                _rows.Add(CreateSettingRow(item));
            }
        }

        private GameObject CreateSectionRow(ConfigItem first)
        {
            bool described = !string.IsNullOrEmpty(first.SectionDescription);
            float height = described ? 84f : 56f;
            var row = new GameObject("Section " + first.Section, typeof(RectTransform));
            row.transform.SetParent(Content, false);
            LayoutElement layout = row.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, height);
            layout.flexibleWidth = 1f;
            TMP_Text label = UiText.Create(row.transform, "Label", Escape(first.SectionTitle), UiText.BodySize);
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.fontStyle |= FontStyles.Bold;
            var labelRect = (RectTransform)label.transform;
            // With a description the heading takes the upper part of the row and
            // the description the lower; the two must not overlap.
            labelRect.offsetMin = new Vector2(110f, described ? 44f : 0f);
            if (described)
            {
                TMP_Text note = UiText.Create(row.transform, "Description", Escape(first.SectionDescription), UiText.BodySize * 0.8f);
                note.alignment = TextAlignmentOptions.TopLeft;
                note.textWrappingMode = TextWrappingModes.NoWrap;
                note.overflowMode = TextOverflowModes.Ellipsis;
                note.color = new Color(note.color.r, note.color.g, note.color.b, 0.75f);
                var noteRect = (RectTransform)note.transform;
                noteRect.offsetMin = new Vector2(110f, 4f);
                noteRect.offsetMax = new Vector2(-20f, -44f);
            }
            return row;
        }

        // A row at the top of the page that shows or hides the advanced settings.
        private GameObject CreateAdvancedRow()
        {
            var row = new GameObject("Advanced", typeof(RectTransform));
            row.transform.SetParent(Content, false);
            LayoutElement layout = row.AddComponent<LayoutElement>();
            layout.minHeight = 72f;
            layout.preferredHeight = 72f;
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, 72f);
            layout.flexibleWidth = 1f;

            var band = new GameObject("Band", typeof(RectTransform));
            var bandRect = (RectTransform)band.transform;
            bandRect.SetParent(row.transform, false);
            bandRect.anchorMin = Vector2.zero;
            bandRect.anchorMax = Vector2.one;
            bandRect.offsetMin = new Vector2(90f, 4f);
            bandRect.offsetMax = new Vector2(0f, -4f);
            Image image = band.AddComponent<Image>();
            image.color = BandColor;
            Button button = band.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = ListColors(button.colors);
            button.onClick.AddListener(() =>
            {
                _showAdvanced = !_showAdvanced;
                if (_item != null && _item.Advanced && !_showAdvanced)
                {
                    _item = Shown().FirstOrDefault() ?? _item;
                }
                RebuildList();
                RebuildDetails(false);
            });

            TMP_Text key = UiText.Create(band.transform, "Key", TextShowAdvanced, UiText.BodySize);
            key.alignment = TextAlignmentOptions.MidlineLeft;
            key.fontStyle |= FontStyles.Italic;
            key.textWrappingMode = TextWrappingModes.NoWrap;
            key.overflowMode = TextOverflowModes.Ellipsis;
            var keyRect = (RectTransform)key.transform;
            keyRect.anchorMax = new Vector2(0.62f, 1f);
            keyRect.offsetMin = new Vector2(20f, 0f);

            TMP_Text value = UiText.Create(band.transform, "Value", _showAdvanced ? TextOn : TextOff, UiText.BodySize);
            value.alignment = TextAlignmentOptions.MidlineRight;
            var valueRect = (RectTransform)value.transform;
            valueRect.anchorMin = new Vector2(0.62f, 0f);
            valueRect.offsetMax = new Vector2(-20f, 0f);
            return row;
        }

        private GameObject CreateSettingRow(ConfigItem item)
        {
            var row = new GameObject("Setting " + item.Key, typeof(RectTransform));
            row.transform.SetParent(Content, false);
            LayoutElement layout = row.AddComponent<LayoutElement>();
            layout.minHeight = 72f;
            layout.preferredHeight = 72f;
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, 72f);
            layout.flexibleWidth = 1f;

            var band = new GameObject("Band", typeof(RectTransform));
            var bandRect = (RectTransform)band.transform;
            bandRect.SetParent(row.transform, false);
            bandRect.anchorMin = Vector2.zero;
            bandRect.anchorMax = Vector2.one;
            bandRect.offsetMin = new Vector2(90f, 4f);
            bandRect.offsetMax = new Vector2(0f, -4f);
            Image image = band.AddComponent<Image>();
            image.color = BandColor;
            Button button = band.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = ListColors(button.colors);
            ConfigRowSelect select = band.AddComponent<ConfigRowSelect>();
            select.Menu = this;
            select.Item = item;
            button.onClick.AddListener(() =>
            {
                SelectItem(item);
                if (PadSupport.PadPressedThisFrame())
                {
                    Focus("Step+", "Toggle", "Reset");
                }
            });

            TMP_Text key = UiText.Create(band.transform, "Key", Escape(item.Title), UiText.BodySize);
            key.alignment = TextAlignmentOptions.MidlineLeft;
            key.textWrappingMode = TextWrappingModes.NoWrap;
            key.overflowMode = TextOverflowModes.Ellipsis;
            var keyRect = (RectTransform)key.transform;
            keyRect.anchorMax = new Vector2(0.62f, 1f);
            keyRect.offsetMin = new Vector2(20f, 0f);

            TMP_Text value = UiText.Create(band.transform, "Value", Escape(item.ValueText), UiText.BodySize);
            value.alignment = TextAlignmentOptions.MidlineRight;
            value.textWrappingMode = TextWrappingModes.NoWrap;
            value.overflowMode = TextOverflowModes.Ellipsis;
            var valueRect = (RectTransform)value.transform;
            valueRect.anchorMin = new Vector2(0.62f, 0f);
            valueRect.offsetMax = new Vector2(-20f, 0f);
            return row;
        }

        private void BuildSettingDetails()
        {
            ConfigItem item = _item;
            if (Details == null || item == null)
            {
                return;
            }

            GameObject band = Part("Band", 0f, 1f, 0f, 1f);
            Image bandImage = band.AddComponent<Image>();
            bandImage.color = BandColor;
            bandImage.raycastTarget = false;

            Label("Mod", Escape(_settingsFor.DisplayName), UiText.BodySize, 0.91f, 0.98f, false);
            Label("Key", Escape(item.Title), UiText.TitleSize * 0.55f, 0.81f, 0.91f, false);
            string section = item.SectionTitle + (item.Title != item.Key ? "   " + item.Key : "");
            Label("Section", Escape(section), UiText.BodySize * 0.85f, 0.75f, 0.81f, false).fontStyle |= FontStyles.Italic;

            string description = item.Description ?? "";
            if (item.RequiresRestart)
            {
                description += (description.Length > 0 ? "\n\n" : "") + TextRestart;
            }
            if (description.Length > 0)
            {
                TMP_Text d = Label("Description", description, UiText.BodySize, 0.45f, 0.74f, true);
                d.alignment = TextAlignmentOptions.TopLeft;
            }

            LabelPair("Default", TextDefault, Escape(item.DefaultText), UiText.BodySize, 0.37f, 0.44f);

            switch (item.Type)
            {
                case ConfigItem.Kind.Toggle:
                    MakeButton("Toggle", item.ValueText, 0.04f, 0.4f, 0.2f, 0.33f,
                        (bool)item.Entry.BoxedValue ? OnColor : OffColor, () => Change(item, 1, "Toggle"));
                    break;
                case ConfigItem.Kind.Choice:
                case ConfigItem.Kind.Number:
                    MakeButton("Step-", "<", 0.04f, 0.16f, 0.2f, 0.33f, StepColor, () => Change(item, -1, "Step-"));
                    TMP_Text value = Label("Value", Escape(item.ValueText), UiText.ButtonSize, 0.2f, 0.33f, false);
                    value.alignment = TextAlignmentOptions.Center;
                    var valueRect = (RectTransform)value.transform;
                    valueRect.anchorMin = new Vector2(0.16f, 0.2f);
                    valueRect.anchorMax = new Vector2(0.62f, 0.33f);
                    MakeButton("Step+", ">", 0.62f, 0.74f, 0.2f, 0.33f, StepColor, () => Change(item, 1, "Step+"));
                    break;
                case ConfigItem.Kind.Text:
                    MakeTextField(item);
                    break;
                default:
                    TMP_Text shown = Label("Value", Escape(item.ValueText), UiText.ButtonSize, 0.26f, 0.34f, false);
                    shown.fontStyle |= FontStyles.Bold;
                    TMP_Text note = Label("EditInFile", TextEditInFile, UiText.BodySize * 0.85f, 0.18f, 0.26f, true);
                    note.fontStyle |= FontStyles.Italic;
                    break;
            }

            if (item.Type != ConfigItem.Kind.ReadOnly)
            {
                MakeButton("Reset", TextResetToDefault, 0.04f, 0.46f, 0.04f, 0.15f, StepColor, () => Reset(item));
                TMP_Text saved = Label("Saved", TextSavedAtOnce, UiText.BodySize * 0.8f, 0.03f, 0.16f, true);
                saved.fontStyle |= FontStyles.Italic;
                var savedRect = (RectTransform)saved.transform;
                savedRect.anchorMin = new Vector2(0.5f, 0.03f);
                _settingNote = saved;
            }
        }

        // A text field for values BepInEx reads as text (strings, shortcuts,
        // colours...). Enter or leaving the field applies the text; a value the
        // config parser refuses is put back and the reason shown. A keyboard
        // shortcut also gets a Capture button that takes the next key pressed.
        private void MakeTextField(ConfigItem item)
        {
            bool shortcut = item.IsShortcut;
            GameObject go = Part("Text", 0.04f, shortcut ? 0.46f : 0.74f, 0.2f, 0.33f);
            // Built inactive: the input field looks for its text component when
            // it wakes, which must be set by then.
            go.SetActive(false);
            Image background = go.AddComponent<Image>();
            background.color = FieldColor;

            var area = new GameObject("Text Area", typeof(RectTransform));
            var areaRect = (RectTransform)area.transform;
            areaRect.SetParent(go.transform, false);
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = new Vector2(12f, 4f);
            areaRect.offsetMax = new Vector2(-12f, -4f);
            area.AddComponent<RectMask2D>();

            TMP_Text text = UiText.Create(area.transform, "Text", "", UiText.ButtonSize);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;
            text.fontSize = UiText.ButtonSize * 0.8f;
            text.richText = false;

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.targetGraphic = background;
            field.textViewport = areaRect;
            field.textComponent = text;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.customCaretColor = true;
            field.caretColor = Color.white;
            field.selectionColor = new Color(0.55f, 0.75f, 1f, 0.5f);
            ColorBlock colors = field.colors;
            colors.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            field.colors = colors;
            field.onEndEdit.AddListener(value => ApplyText(item, field, value));
            go.SetActive(true);
            // Only now: text given to the field while it was inactive was not shown.
            field.text = item.SerializedText;
            field.ForceLabelUpdate();

            if (shortcut)
            {
                MakeButton("Capture", TextCaptureKey, 0.5f, 0.74f, 0.2f, 0.33f, StepColor, () => ToggleCapture(item));
            }
        }

        private void ApplyText(ConfigItem item, TMP_InputField field, string value)
        {
            if (item == null || !ReferenceEquals(item, _item) || value == item.SerializedText)
            {
                return;
            }
            string error = item.SetText(value);
            if (error != null)
            {
                field.text = item.SerializedText;
                if (_settingNote != null)
                {
                    _settingNote.text = TextNotAccepted + error;
                    _settingNote.color = NoteErrorColor;
                }
                return;
            }
            RebuildList();
            RebuildDetails(false);
            Focus("Reset");
        }

        // Starts taking the next key for a shortcut, or stops if already taking one.
        private void ToggleCapture(ConfigItem item)
        {
            GameObject button = _detailParts.FirstOrDefault(p => p != null && p.name == "Capture");
            if (button == null)
            {
                return;
            }
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

        internal void AfterCapture()
        {
            RebuildList();
            RebuildDetails(false);
            Focus("Capture");
        }

        private void Change(ConfigItem item, int direction, string focus)
        {
            item.Step(direction);
            RebuildList();
            RebuildDetails(false);
            Focus(focus);
        }

        private void Reset(ConfigItem item)
        {
            item.ResetToDefault();
            RebuildList();
            RebuildDetails(false);
            Focus("Reset");
        }

        // Selection moves to a button of the rebuilt panel so a pad user keeps
        // their place. Unity destroys the old objects at the end of the frame,
        // so look only among the current parts; a button held while the mods
        // are checked is passed over.
        private void Focus(params string[] names)
        {
            if (EventSystem.current == null)
            {
                return;
            }
            foreach (string name in names)
            {
                GameObject target = _detailParts.FirstOrDefault(p => p != null && p.name == name && p.GetComponent<Selectable>() is Selectable s && s.IsInteractable());
                if (target != null)
                {
                    EventSystem.current.SetSelectedGameObject(target);
                    return;
                }
            }
        }

        private GameObject MakeButton(string name, string text, float left, float right, float bottom, float top, Color color, UnityAction onClick)
        {
            GameObject go = Part(name, left, right, bottom, top);
            Image background = go.AddComponent<Image>();
            background.color = color;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;
            TMP_Text label = UiText.Create(go.transform, "Label", text, UiText.ButtonSize);
            label.alignment = TextAlignmentOptions.Center;
            // One line: a long label (Japanese, German) shrinks instead of breaking
            // in the middle of a word.
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            ((RectTransform)label.transform).offsetMin = new Vector2(8f, 0f);
            ((RectTransform)label.transform).offsetMax = new Vector2(-8f, 0f);
            button.onClick.AddListener(onClick);
            return go;
        }

        private static ColorBlock ListColors(ColorBlock colors)
        {
            colors.normalColor = new Color(1f, 1f, 1f, 0.7f);
            colors.highlightedColor = new Color(0.55f, 0.75f, 1f, 1f);
            colors.selectedColor = new Color(0.55f, 0.75f, 1f, 1f);
            colors.pressedColor = new Color(0.45f, 0.6f, 0.9f, 1f);
            return colors;
        }
    }

    // Shows a setting's details as soon as its row is selected, by mouse or pad.
    internal sealed class ConfigRowSelect : MonoBehaviour, ISelectHandler
    {
        internal ModsMenu Menu;
        internal ConfigItem Item;

        public void OnSelect(BaseEventData eventData)
        {
            Menu?.SelectItem(Item);
        }
    }
}
