using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // The Mods screen. A game Menu, so MenuManager shows and hides it, and the
    // game handles the cursor and pad input as on any other screen.
    //
    // Two columns, like Forge's mod list: the installed mods on the left (in
    // the game's scroll view), details of the selected one on the right with
    // its On/Off button. Every fixed word is its own label so translation mods
    // can translate it. Switching takes effect at the next launch.
    internal sealed partial class ModsMenu : Menu
    {
        internal RectTransform Content;
        internal RectTransform Details;

        // Strings shown on this screen. Translation packs key rows by the exact
        // English, so change them only together with the packs.
        internal const string TextRequired = "Required";
        internal const string TextOn = "On";
        internal const string TextOff = "Off";
        internal const string TextOffNextLaunch = "Off from the next launch";
        internal const string TextOnNextLaunch = "On from the next launch";
        internal const string TextConfirmOff = "Other mods need this one. Press Off again to switch it off anyway.";
        internal const string TextOutsidePlugins = "Installed outside BepInEx/plugins, so it cannot be switched off here";
        internal const string TextNeededBy = "Needed by";
        internal const string TextUses = "Uses";
        internal const string TextLibrary = "Library";
        internal const string TextUnavailable = "Unavailable on this game build:";
        internal const string TextConflictTag = "Conflict";
        internal const string TextSameCode = "Changes the same game code as:";
        internal const string TextSameCodeRisky = "Changes the same game code as, and may override:";
        internal const string TextUpdateTag = "Update";
        internal const string TextReloaded = "Reloaded";
        internal const string TextNewVersion = "New version available:";
        internal const string TextOpenReleasePage = "Open release page";
        internal const string TextUninstall = "Uninstall";
        internal const string TextCancelUninstall = "Cancel uninstall";
        internal const string TextConfirmUninstall = "Press Uninstall again to remove this mod when the game next starts. Your settings for it are removed too.";
        internal const string TextUninstallNextLaunch = "Removed when the game next starts";

        private static readonly Color WarnColor = new Color(1f, 0.75f, 0.5f, 1f);
        private static readonly Color UpdateColor = new Color(0.6f, 0.95f, 0.75f, 1f);
        private List<PatchConflicts.Conflict> _conflicts = new List<PatchConflicts.Conflict>();

        // The Back button's pointing hand is drawn just right of the button,
        // over the start of the list; keep the text clear of it.
        private const float ListLeftMargin = 110f;
        private const float RowHeight = 96f;

        private static readonly Color BandColor = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color OnColor = new Color(0.36f, 0.62f, 0.36f, 1f);
        private static readonly Color OffColor = new Color(0.62f, 0.3f, 0.27f, 1f);

        private List<ModCatalog.Entry> _entries = new List<ModCatalog.Entry>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<GameObject> _detailParts = new List<GameObject>();
        private ModCatalog.Entry _selected;
        private ModCatalog.Entry _confirming;
        private ModCatalog.Entry _confirmingUninstall;

        protected override void OnShow(MenuResponseTransition response)
        {
            base.OnShow(response);
            _confirming = null;
            _confirmingUninstall = null;
            _settingsFor = null;
            _page = null;
            DropCheck();
            try
            {
                // The loaded mods at once; the rest when the check is in (ModsMenu.Check.cs).
                _entries = ModCatalog.Build(null);
                _conflicts = new List<PatchConflicts.Conflict>();
                _selected = _entries.FirstOrDefault(e => SameMod(e, _selected)) ?? _entries.FirstOrDefault();
                StartCheck();
                RebuildList();
                RebuildDetails(false);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Could not list mods: {ex}");
            }
        }

        public override MenuResponse OnEvent(MenuEvent e)
        {
            if (e is MenuEventUserIntent intent && (intent.name == "Back" || intent.name == "Cancel"))
            {
                if (_page != null)
                {
                    ClosePage();
                    return new MenuResponseIgnored();
                }
                if (_settingsFor != null)
                {
                    CloseSettings();
                    return new MenuResponseIgnored();
                }
                return new MenuResponseTransition("Menu_Options", "Player left the Mods screen.");
            }
            return new MenuResponseIgnored();
        }

        // The details panel changed size (the window was resized). Its notes
        // measure the panel to decide where labels wrap, so lay them out again,
        // keeping the focused button. A page another mod built is left alone:
        // building it again could lose what the player has done on it.
        internal void OnDetailsResized()
        {
            if (_page != null || !isActiveAndEnabled)
            {
                return;
            }
            GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            string focusName = focused != null && _detailParts.Contains(focused) ? focused.name : null;
            RebuildDetails(false);
            if (focusName != null)
            {
                Focus(focusName);
            }
        }

        internal void Select(ModCatalog.Entry entry)
        {
            if (entry == null || ReferenceEquals(entry, _selected))
            {
                return;
            }
            _selected = entry;
            _confirming = null;
            _confirmingUninstall = null;
            RebuildDetails(false);
        }

        // ---- list ----

        private void RebuildList()
        {
            foreach (GameObject row in _rows)
            {
                if (row != null)
                {
                    Destroy(row);
                }
            }
            _rows.Clear();

            if (_settingsFor != null)
            {
                BuildSettingsList();
                return;
            }

            foreach (ModCatalog.Entry entry in _entries)
            {
                _rows.Add(CreateListRow(entry));
            }
            if (Checking)
            {
                _rows.Add(CreateCheckingRow());
            }
        }

        private GameObject CreateListRow(ModCatalog.Entry entry)
        {
            var row = new GameObject("Mod " + entry.Name, typeof(RectTransform));
            row.transform.SetParent(Content, false);
            LayoutElement layout = row.AddComponent<LayoutElement>();
            layout.minHeight = RowHeight;
            layout.preferredHeight = RowHeight;
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, RowHeight);
            layout.flexibleWidth = 1f;

            var band = new GameObject("Band", typeof(RectTransform));
            var bandRect = (RectTransform)band.transform;
            bandRect.SetParent(row.transform, false);
            bandRect.anchorMin = Vector2.zero;
            bandRect.anchorMax = Vector2.one;
            bandRect.offsetMin = new Vector2(ListLeftMargin - 20f, 6f);
            bandRect.offsetMax = new Vector2(0f, -6f);
            Image bandImage = band.AddComponent<Image>();
            bandImage.color = BandColor;

            Button button = band.AddComponent<Button>();
            button.targetGraphic = bandImage;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.7f);
            colors.highlightedColor = new Color(0.55f, 0.75f, 1f, 1f);
            colors.selectedColor = new Color(0.55f, 0.75f, 1f, 1f);
            colors.pressedColor = new Color(0.45f, 0.6f, 0.9f, 1f);
            button.colors = colors;
            ModRowSelect select = band.AddComponent<ModRowSelect>();
            select.Menu = this;
            select.Entry = entry;
            button.onClick.AddListener(() =>
            {
                Select(entry);
                // With the pad, pressing a mod moves on to its buttons.
                if (PadSupport.PadPressedThisFrame())
                {
                    Focus("Settings", "Switch");
                }
            });

            // From the right: the on/off state, then a tag, each as wide as its
            // text (which differs a lot between languages); the name takes what
            // is left and ends in an ellipsis. Measured widths do not depend on
            // the window size, so the row holds together however narrow it gets.
            float used = 20f;
            if (!entry.IsFramework && !entry.IsPatcher)
            {
                TMP_Text state = UiText.Create(band.transform, "State", entry.PendingUninstall ? TextUninstall : entry.WantOn ? TextOn : TextOff, UiText.BodySize);
                state.color = entry.PendingUninstall ? WarnColor : entry.WantOn ? new Color(0.65f, 0.95f, 0.65f, 1f) : new Color(1f, 0.6f, 0.55f, 1f);
                used += FitRight(state, used) + 16f;
            }

            bool conflict = ConflictsOf(entry).Count > 0;
            bool update = entry.Loaded && Updates.UpdateCheck.NewerRelease(entry.Guid, entry.Version) != null;
            string tagText = entry.IsLibrary ? TextLibrary : conflict ? TextConflictTag : update ? TextUpdateTag : null;
            if (tagText != null)
            {
                TMP_Text tag = UiText.Create(band.transform, entry.IsLibrary ? "Library" : conflict ? "Conflict" : "Update", tagText, UiText.BodySize * 0.8f);
                tag.color = entry.IsLibrary ? new Color(0.7f, 0.8f, 1f, 1f) : conflict ? WarnColor : UpdateColor;
                used += FitRight(tag, used) + 16f;
            }
            if (IsOnline(entry, out bool undeclaredOnline))
            {
                TMP_Text net = UiText.Create(band.transform, "Online", TextOnlineTag, UiText.BodySize * 0.8f);
                net.color = undeclaredOnline ? WarnColor : OnlineColor;
                used += FitRight(net, used) + 16f;
            }

            TMP_Text name = UiText.Create(band.transform, "Name", Escape(entry.DisplayName), UiText.NameSize);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var nameRect = (RectTransform)name.transform;
            nameRect.offsetMin = new Vector2(20f, 0f);
            nameRect.offsetMax = new Vector2(-used, 0f);
            return row;
        }

        // Puts a one-line label at the right of its row, ending `right` pixels
        // from the row's right edge and as wide as its text. Returns the width.
        private static float FitRight(TMP_Text label, float right)
        {
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.MidlineRight;
            float width = Mathf.Ceil(label.GetPreferredValues(label.text).x) + 2f;
            var rect = (RectTransform)label.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-right - width, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
            return width;
        }

        // ---- details ----

        private void RebuildDetails(bool focusSwitch)
        {
            foreach (GameObject part in _detailParts)
            {
                if (part != null)
                {
                    Destroy(part);
                }
            }
            _detailParts.Clear();

            if (_page != null)
            {
                BuildPage();
                return;
            }
            if (_settingsFor != null)
            {
                BuildSettingDetails();
                return;
            }

            ModCatalog.Entry entry = _selected;
            if (Details == null || entry == null)
            {
                return;
            }

            GameObject band = Part("Band", 0f, 1f, 0f, 1f);
            Image bandImage = band.AddComponent<Image>();
            bandImage.color = BandColor;
            bandImage.raycastTarget = false;

            TMP_Text nameLabel = Label("Name", Escape(entry.DisplayName), UiText.TitleSize * 0.6f, 0.86f, 0.98f, false);
            Texture2D icon = entry.Info?.Icon;
            if (icon != null)
            {
                // Square, as tall as the name's band, with the name moved beside it.
                float side = Details.rect.height * 0.12f - 8f;
                GameObject iconPart = Part("Icon", 0f, 0f, 0.92f, 0.92f);
                var iconRect = (RectTransform)iconPart.transform;
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.sizeDelta = new Vector2(side, side);
                iconRect.anchoredPosition = new Vector2(28f, 0f);
                RawImage image = iconPart.AddComponent<RawImage>();
                image.texture = icon;
                image.raycastTarget = false;
                ((RectTransform)nameLabel.transform).offsetMin = new Vector2(28f + side + 16f, 0f);
            }

            string meta = string.IsNullOrEmpty(entry.Version) ? "" : "v" + Escape(entry.Version);
            if (!string.IsNullOrEmpty(entry.Authors))
            {
                meta += (meta.Length > 0 ? "   " : "") + Escape(entry.Authors);
            }
            if (meta.Length > 0)
            {
                Label("Meta", meta, UiText.BodySize, 0.78f, 0.86f, false);
            }

            string description = entry.Description;
            if (!string.IsNullOrEmpty(description))
            {
                TMP_Text d = Label("Description", description, UiText.BodySize, 0.57f, 0.77f, true);
                d.alignment = TextAlignmentOptions.TopLeft;
            }

            string website = entry.Website;
            if (!string.IsNullOrEmpty(website))
            {
                Label("Website", Escape(website), UiText.BodySize * 0.85f, 0.51f, 0.57f, false);
            }

            string status = Status(entry);
            if (status != null)
            {
                TMP_Text s = Label("Status", status, UiText.BodySize * 0.9f, 0.42f, 0.51f, true);
                s.fontStyle |= FontStyles.Italic;
                s.alignment = TextAlignmentOptions.TopLeft;
            }

            // Notes about the mod, one per line from the top of this band down.
            var notes = new List<(string Name, string Label, string Value, bool Warn)>();
            if (_confirming != entry)
            {
                AddNetworkNote(entry, notes, true);
            }
            Updates.UpdateCheck.Release newer = entry.Loaded ? Updates.UpdateCheck.NewerRelease(entry.Guid, entry.Version) : null;
            if (newer != null && _confirming != entry)
            {
                notes.Add(("Update", TextNewVersion, newer.Tag, false));
            }
            if (_confirming != entry)
            {
                AddNetworkNote(entry, notes, false);
            }
            int reloads = ModReload.ReloadCount(entry.Guid);
            if (reloads > 0 && _confirming != entry)
            {
                notes.Add(("Reloaded", TextReloaded, reloads == 1 ? "once this session; not the file BepInEx loaded" : reloads + " times this session; not the file BepInEx loaded", false));
            }
            if (entry.ProblemGuids.Count > 0 && _confirming != entry)
            {
                notes.Add(("ProblemMods", null, string.Join(", ", entry.ProblemGuids.Select(g => ModCatalog.NameOf(_entries, g))), false));
            }
            IReadOnlyList<string> unavailable = GameHooks.UnavailableFeatures(entry.Guid);
            if (unavailable.Count > 0 && _confirming != entry)
            {
                notes.Add(("Unavailable", TextUnavailable, string.Join(", ", unavailable), true));
            }
            // What the mod uses and clashes with is only certain once the check
            // is in; until then one line says it is coming, in the place those
            // notes take, so the notes above it neither move nor drop out of
            // the band when the check ends.
            if (Checking && !entry.IsFramework)
            {
                notes.Add(("Checking", null, TextChecking, false));
            }
            foreach (PatchConflicts.Conflict c in ConflictsOf(entry))
            {
                string others = string.Join(", ", c.Guids.Where(g => g != entry.Guid).Select(g => ModCatalog.NameOf(_entries, g)));
                notes.Add(("Conflict", c.Risky ? TextSameCodeRisky : TextSameCode, others + " (" + c.Method + ")", true));
            }
            if (entry.Uses.Count > 0 && _confirming != entry && !entry.IsFramework && !Checking)
            {
                notes.Add(("Uses", TextUses, string.Join(", ", entry.Uses.Select(u =>
                    ModCatalog.ShortNameOf(_entries, u.Key) + (u.Value != null && u.Value > new Version(0, 0) ? " " + u.Value + "+" : ""))), false));
            }
            if (entry.IsLibrary && entry.Dependents.Count > 0 && _confirming != entry && entry.ProblemGuids.Count == 0 && !Checking)
            {
                notes.Add(("UsedBy", TextNeededBy, string.Join(", ", entry.Dependents.Select(g => ModCatalog.NameOf(_entries, g))), false));
            }
            if (_confirmingUninstall == entry && entry.Dependents.Count > 0)
            {
                notes.Add(("NeededBy", TextNeededBy, string.Join(", ", entry.Dependents.Select(g => ModCatalog.NameOf(_entries, g))), true));
            }
            if (_confirming == entry)
            {
                notes.Add(("NeededBy", TextNeededBy, string.Join(", ", entry.Dependents.Select(g => ModCatalog.NameOf(_entries, g))), false));
            }

            List<ModsScreenPage> pages = entry.Loaded && entry.Guid != null ? ModFramework.PagesFor(entry.Guid) : new List<ModsScreenPage>();
            if (entry.Loaded && IsOnline(entry, out _))
            {
                pages.Insert(0, InternetPage(entry));
            }
            bool hasButtonRow = pages.Count > 0 || newer != null;
            int maxLines = hasButtonRow ? 2 : 3;
            float size = UiText.BodySize * 0.9f;
            float width = Details.rect.width - 56f;
            int line = 0;
            for (int i = 0; i < notes.Count && line < maxLines; i++)
            {
                var note = notes[i];
                float top = 0.42f - line * 0.07f;
                TMP_Text text;
                if (note.Label == null)
                {
                    text = Label(note.Name + i, Escape(note.Value), size, top - 0.07f, top, false);
                    if (note.Name == "Checking")
                    {
                        CheckingStyle(text);
                        _detailParts.Add(AddSpinner(Details, text, 28f, size).gameObject);
                    }
                    line++;
                }
                else
                {
                    TMP_Text head = Label(note.Name + i + "Label", note.Label, size, top - 0.07f, top, false);
                    head.fontStyle |= FontStyles.Bold;
                    head.enableAutoSizing = false;
                    head.fontSize = size;
                    float labelWidth = head.GetPreferredValues(head.text).x;
                    // Measured in the label's bold style, a little wider than the value's.
                    float valueWidth = head.GetPreferredValues(Escape(note.Value)).x;
                    bool squeezed = labelWidth > width * 0.5f || labelWidth + 16f + valueWidth * 0.75f > width;
                    if (width > 0f && squeezed && line + 1 < maxLines)
                    {
                        // A long label (common in Japanese or German), or names that
                        // would shrink to fit beside it, get their own line.
                        text = Label(note.Name + i, Escape(note.Value), size, top - 0.14f, top - 0.07f, false);
                        ((RectTransform)text.transform).offsetMin = new Vector2(56f, 0f);
                        line += 2;
                    }
                    else
                    {
                        text = Label(note.Name + i, Escape(note.Value), size, top - 0.07f, top, false);
                        ((RectTransform)text.transform).offsetMin = new Vector2(28f + labelWidth + 16f, 0f);
                        line++;
                    }
                    if (note.Warn)
                    {
                        head.color = WarnColor;
                    }
                    else if (note.Name == "Update")
                    {
                        head.color = UpdateColor;
                    }
                }
                if (note.Warn)
                {
                    text.color = WarnColor;
                }
                else if (note.Name == "Update")
                {
                    text.color = UpdateColor;
                }
            }

            // Bottom row: On/Off, Settings and, for mods that can be uninstalled, Uninstall.
            bool three = entry.CanUninstall;
            if (entry.CanSwitch)
            {
                GameObject button = CreateSwitch(entry, three ? 0.32f : 0.4f);
                if (Checking)
                {
                    Hold(button);
                }
                else if (focusSwitch && EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(button);
                }
            }

            if (entry.Loaded && ConfigItem.For(entry).Count > 0)
            {
                MakeButton("Settings", TextSettings, three ? 0.35f : 0.44f, three ? 0.63f : 0.8f, 0.04f, 0.16f, SettingsColor, () => OpenSettings(entry));
            }

            if (three)
            {
                bool confirming = _confirmingUninstall == entry;
                GameObject uninstall = MakeButton("Uninstall", entry.PendingUninstall ? TextCancelUninstall : TextUninstall, 0.66f, 0.96f, 0.04f, 0.16f,
                    confirming ? OffColor : UninstallColor, () => OnUninstall(entry));
                if (Checking)
                {
                    Hold(uninstall);
                }
            }

            // A row of up to two buttons above Switch and Settings: the release page
            // first, then the pages the mod added.
            int slot = 0;
            if (newer != null)
            {
                string url = newer.Url;
                MakeButton("Release", TextOpenReleasePage, 0.04f, 0.4f, 0.18f, 0.26f, SettingsColor, () => OpenReleasePage(url));
                slot++;
            }
            for (int i = 0; i < pages.Count && slot < 2; i++, slot++)
            {
                ModsScreenPage page = pages[i];
                float left = 0.04f + slot * 0.4f;
                MakeButton("Page" + i, page.Title, left, left + 0.36f, 0.18f, 0.26f, SettingsColor, () => OpenPage(entry, page));
            }
        }

        private static void OpenReleasePage(string url)
        {
            try
            {
                Application.OpenURL(url);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not open {url}: {ex.Message}");
            }
        }

        // An update check finished while the screen is open: show its result,
        // keeping what the pad or keyboard had selected. Settings and pages are
        // left alone until the player comes back to the list.
        internal void OnUpdatesChanged()
        {
            RebuildKeepingFocus();
        }

        private GameObject Part(string name, float left, float right, float bottom, float top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(Details, false);
            rect.anchorMin = new Vector2(left, bottom);
            rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _detailParts.Add(go);
            return go;
        }

        private TMP_Text Label(string name, string text, float size, float bottom, float top, bool wrap)
        {
            TMP_Text label = UiText.Create(Details, name, text, size);
            _detailParts.Add(label.gameObject);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            if (wrap)
            {
                // Wraps at full size, and only shrinks a little when the text still
                // does not fit (a narrow window, a long translation).
                label.fontSizeMin = size * 0.7f;
            }
            var rect = (RectTransform)label.transform;
            rect.anchorMin = new Vector2(0f, bottom);
            rect.anchorMax = new Vector2(1f, top);
            rect.offsetMin = new Vector2(28f, 0f);
            rect.offsetMax = new Vector2(-28f, 0f);
            return label;
        }

        private List<PatchConflicts.Conflict> ConflictsOf(ModCatalog.Entry entry)
        {
            return entry.Guid == null ? new List<PatchConflicts.Conflict>() : _conflicts.Where(c => c.Guids.Contains(entry.Guid)).ToList();
        }

        // A bold label and its value on one line. The value starts after the label's
        // actual width, which differs a lot between languages.
        private TMP_Text LabelPair(string name, string label, string value, float size, float bottom, float top)
        {
            TMP_Text head = Label(name + "Label", label, size, bottom, top, false);
            head.fontStyle |= FontStyles.Bold;
            head.enableAutoSizing = false;
            head.fontSize = size;
            float width = head.GetPreferredValues(head.text).x;
            TMP_Text text = Label(name, value, size, bottom, top, false);
            ((RectTransform)text.transform).offsetMin = new Vector2(28f + width + 16f, 0f);
            return text;
        }

        private GameObject CreateSwitch(ModCatalog.Entry entry, float right)
        {
            GameObject go = Part("Switch", 0.04f, right, 0.04f, 0.16f);
            Image background = go.AddComponent<Image>();
            background.color = entry.WantOn ? OnColor : OffColor;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;

            TMP_Text label = UiText.Create(go.transform, "Label", entry.WantOn ? TextOn : TextOff, UiText.ButtonSize);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            button.onClick.AddListener(() => OnSwitch(entry));
            return go;
        }

        private static readonly Color UninstallColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        // First press asks, second press records it; on a mod waiting to be
        // uninstalled the button takes the wish back.
        private void OnUninstall(ModCatalog.Entry entry)
        {
            if (Checking)
            {
                return;
            }
            try
            {
                if (!entry.PendingUninstall && _confirmingUninstall != entry)
                {
                    _confirming = null;
                    _confirmingUninstall = entry;
                    RebuildDetails(false);
                    Focus("Uninstall");
                    return;
                }
                _confirmingUninstall = null;
                ModCatalog.SetUninstall(_entries, entry, !entry.PendingUninstall);
                RebuildList();
                RebuildDetails(false);
                Focus("Uninstall");
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Could not change the uninstall of {entry.Name}: {ex}");
            }
        }

        private void OnSwitch(ModCatalog.Entry entry)
        {
            if (Checking)
            {
                return;
            }
            try
            {
                if (entry.WantOn)
                {
                    bool needed = entry.Dependents.Any(g => _entries.Any(x => x.Guid == g && x.WantOn));
                    if (needed && _confirming != entry)
                    {
                        _confirming = entry;
                        RebuildDetails(true);
                        return;
                    }
                }
                _confirming = null;
                ModCatalog.SetWantOn(_entries, entry, !entry.WantOn);
                RebuildList();
                RebuildDetails(true);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Could not switch {entry.Name}: {ex}");
            }
        }

        private string Status(ModCatalog.Entry entry)
        {
            if (entry.IsFramework)
            {
                return TextRequired;
            }
            if (_confirmingUninstall == entry)
            {
                return TextConfirmUninstall;
            }
            if (entry.PendingUninstall)
            {
                return TextUninstallNextLaunch;
            }
            if (_confirming == entry)
            {
                return TextConfirmOff;
            }
            if (entry.ProblemLabel != null && entry.WantOn)
            {
                return entry.ProblemLabel;
            }
            if (entry.Loaded && !entry.WantOn)
            {
                return TextOffNextLaunch;
            }
            if (!entry.Loaded && entry.WantOn)
            {
                return TextOnNextLaunch;
            }
            if (entry.RelativePath == null)
            {
                return TextOutsidePlugins;
            }
            return null;
        }

        private static bool SameMod(ModCatalog.Entry a, ModCatalog.Entry b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            return a.Guid == b.Guid && string.Equals(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase);
        }

        // Mod names and descriptions are plain text, not rich text.
        private static string Escape(string text)
        {
            return (text ?? "").Replace("<", "<noparse><</noparse>");
        }
    }

    // Tells the menu when the details panel is resized, at most once a frame
    // and only once the size has settled for that frame.
    internal sealed class DetailsResizeWatcher : UIBehaviour
    {
        internal ModsMenu Menu;
        private Vector2 _builtFor;
        private bool _dirty;

        protected override void OnEnable()
        {
            base.OnEnable();
            _builtFor = ((RectTransform)transform).rect.size;
            _dirty = false;
        }

        protected override void OnRectTransformDimensionsChange()
        {
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (!_dirty)
            {
                return;
            }
            _dirty = false;
            Vector2 size = ((RectTransform)transform).rect.size;
            if (Mathf.Abs(size.x - _builtFor.x) < 1f && Mathf.Abs(size.y - _builtFor.y) < 1f)
            {
                return;
            }
            _builtFor = size;
            try
            {
                Menu?.OnDetailsResized();
            }
            catch (System.Exception ex)
            {
                ModFramework.Log.LogError($"Could not lay out the Mods screen again: {ex}");
            }
        }
    }

    // Tells the menu when an update check has a result, and when the check of
    // the mods' files is in.
    internal sealed class UpdateResultWatcher : MonoBehaviour
    {
        internal ModsMenu Menu;
        private int _seen = -1;
        private int _seenNetwork = -1;

        private void OnEnable()
        {
            _seen = Updates.UpdateCheck.Revision;
            _seenNetwork = NetworkWatch.Revision;
        }

        // Also when the framework sees a mod connect somewhere new.
        private void LateUpdate()
        {
            try
            {
                Menu?.PollCheck();
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Could not show the check of the mods: {ex}");
            }
            if (_seen == Updates.UpdateCheck.Revision && _seenNetwork == NetworkWatch.Revision)
            {
                return;
            }
            _seen = Updates.UpdateCheck.Revision;
            _seenNetwork = NetworkWatch.Revision;
            try
            {
                Menu?.OnUpdatesChanged();
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"Could not show update check results: {ex}");
            }
        }
    }

    // Shows a mod's details as soon as its row is selected, by mouse or pad.
    internal sealed class ModRowSelect : MonoBehaviour, ISelectHandler
    {
        internal ModsMenu Menu;
        internal ModCatalog.Entry Entry;

        public void OnSelect(BaseEventData eventData)
        {
            Menu?.Select(Entry);
        }
    }
}
