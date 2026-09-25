using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // The details of the selected mod, on the right, top to bottom:
    //   - a header that stays put: icon, name, version and author, website,
    //     and the mod's switch with the word for what it is now (On or Off),
    //   - the notes, one band each, every one of them: nothing is dropped
    //     for lack of room (the notes scroll when there are very many),
    //   - tabs: About, Settings, Internet and the pages the mod added, as
    //     many as there are,
    //   - the chosen tab, which scrolls.
    internal sealed partial class ModsMenu
    {
        internal const string TextAbout = "About";
        internal const string TextSelect = "Select";
        internal const string TextBack = "Back";
        internal const string TextSearch = "Search";
        internal const string TextTabs = "Tabs";

        private const string TabAbout = "About";
        private const string TabSettings = "Settings";
        private const string TabInternet = "Internet";

        // The tab showing, by key: one of the above, or "Page" and the page's
        // place among the mod's pages.
        private string _tab = TabAbout;

        // Where the chosen tab is built.
        private RectTransform _body;

        private const float DetailsPadding = 24f;

        // The buttons of the new version's band, by name, so the focus can be
        // put back on them. Update keeps its name while it asks, so the
        // focus stays on it.
        private const string ReleasePageButton = "ReleasePage";
        private const string UpdateButton = "UpdateNow";

        // Quit and update, asking: the Uninstall button's asking look in the
        // warning colour.
        private static readonly Color AskingFace = new Color(0.29f, 0.235f, 0.09f);

        // A button at the right of a note.
        private sealed class BandButton
        {
            internal string Name;
            internal string Text;
            internal UnityAction OnClick;
            // Asking to be pressed again.
            internal bool Asking;
            // Waiting for the check of the mods.
            internal bool Held;
        }

        // The title in the header, cut to two lines once laid out.
        private TMP_Text _headerName;

        private sealed class Tab
        {
            internal string Key;
            internal string Title;
            internal string Count;
            internal bool Warn;
            internal ModsScreenPage Page;
        }

        // A page another mod built: it is not built again on its own (on a
        // resize or an update result), since that could lose what the player
        // has done on it.
        private bool OnModPage => _tab.StartsWith("Page", StringComparison.Ordinal);

        private float InnerWidth => Mathf.Max(200f, Details.rect.width - 2f * DetailsPadding);

        private void BuildDetails(ModCatalog.Entry entry, bool focusSwitch)
        {
            GameObject view = Part("View", 0f, 1f, 0f, 1f);
            VerticalLayoutGroup column = view.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset((int)DetailsPadding, (int)DetailsPadding, 20, 20);
            column.spacing = 14f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            Updates.UpdateCheck.Release newer = entry.Loaded ? Updates.UpdateCheck.NewerRelease(entry.Guid, entry.Version) : null;
            List<Tab> tabs = TabsOf(entry);
            if (tabs.All(t => t.Key != _tab))
            {
                _tab = TabAbout;
            }

            BuildHeader(view.transform, entry, focusSwitch);
            (LayoutElement notesSize, RectTransform notes) = BuildNotes(view.transform, entry, newer);
            BuildTabs(view.transform, entry, tabs);

            _body = ModsLook.Rect(view.transform, "Body");
            LayoutElement bodySize = ModsLook.Size(_body.gameObject, -1f, -1f, 1f, 1f);
            bodySize.minHeight = 140f;
            if (UnityEngine.InputSystem.Gamepad.current != null)
            {
                BuildHints(view.transform);
            }

            // The notes take what they need, up to about a third of the panel,
            // and scroll beyond that. How much they need is only known once
            // the column is laid out at its width.
            var viewRect = (RectTransform)view.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(viewRect);
            // A title of more than two lines at this width ends in ... on the second.
            if (_headerName != null)
            {
                _headerName.ForceMeshUpdate();
                int lines = _headerName.textInfo.lineCount;
                if (lines > 2)
                {
                    float height = Mathf.Ceil(_headerName.preferredHeight / lines * 2f) + 2f;
                    ModsLook.Size(_headerName.gameObject, -1f, height, -1f, -1f);
                    _headerName.overflowMode = TextOverflowModes.Ellipsis;
                    LayoutRebuilder.ForceRebuildLayoutImmediate(viewRect);
                }
            }
            if (notes != null)
            {
                float needed = LayoutUtility.GetPreferredHeight(notes);
                float height = Mathf.Min(needed, Mathf.Max(120f, Details.rect.height * 0.36f));
                notesSize.minHeight = height;
                notesSize.preferredHeight = height;
                LayoutRebuilder.ForceRebuildLayoutImmediate(viewRect);
            }
            // Only now, with the tab's area at its size: a page another mod
            // builds may measure it.
            BuildTab(entry, tabs.First(t => t.Key == _tab));
        }

        // ---- header ----

        private void BuildHeader(Transform parent, ModCatalog.Entry entry, bool focusSwitch)
        {
            RectTransform header = ModsLook.Rect(parent, "Header");
            HorizontalLayoutGroup row = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 20f;
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            RectTransform iconHolder = ModsLook.Rect(header, "IconHolder");
            ModsLook.Size(iconHolder.gameObject, 88f, 88f, 0f, 0f);
            ModIcon(iconHolder, entry, 88f, 0f);

            RectTransform text = ModsLook.Rect(header, "Text");
            VerticalLayoutGroup lines = text.gameObject.AddComponent<VerticalLayoutGroup>();
            lines.spacing = 2f;
            lines.childControlWidth = true;
            lines.childControlHeight = true;
            lines.childForceExpandWidth = true;
            lines.childForceExpandHeight = false;
            LayoutElement textSize = ModsLook.Size(text.gameObject, -1f, -1f, 1f, 0f);
            textSize.minWidth = 0f;
            textSize.preferredWidth = 0f;

            // Two lines at most (BuildDetails cuts it once it is laid out). A
            // library's title is its short name, with the whole name under it.
            string title = ShownName(entry);
            _headerName = ModsLook.Text(text, "Name", Escape(title), 34f, ModsLook.Label, FontStyles.Bold, true);
            if (title != entry.DisplayName)
            {
                ModsLook.Text(text, "FullName", Escape(entry.DisplayName), 19f, ModsLook.Muted, FontStyles.Normal, false);
            }
            string meta = string.IsNullOrEmpty(entry.Version) ? "" : "v" + Escape(entry.Version);
            if (!string.IsNullOrEmpty(entry.Authors))
            {
                meta += (meta.Length > 0 ? "   " : "") + Escape(entry.Authors);
            }
            if (meta.Length > 0)
            {
                ModsLook.Text(text, "Meta", meta, 21f, ModsLook.Muted, FontStyles.Normal, true);
            }
            if (!string.IsNullOrEmpty(entry.Website))
            {
                ModsLook.Text(text, "Website", Escape(entry.Website), 19f, ModsLook.AccentText, FontStyles.Normal, false);
            }

            RectTransform state = ModsLook.Rect(header, "State");
            HorizontalLayoutGroup stateRow = state.gameObject.AddComponent<HorizontalLayoutGroup>();
            stateRow.spacing = 12f;
            stateRow.childAlignment = TextAnchor.MiddleRight;
            stateRow.childControlWidth = true;
            stateRow.childControlHeight = true;
            stateRow.childForceExpandWidth = false;
            stateRow.childForceExpandHeight = false;
            ModsLook.Size(state.gameObject, -1f, 52f, 0f, 0f);

            if (entry.IsFramework)
            {
                TMP_Text required = ModsLook.Text(state, "Required", TextRequired, 22f, ModsLook.Muted, FontStyles.Bold, false);
                ModsLook.Size(required.gameObject, ModsLook.Width(required), 52f, 0f, 0f);
                return;
            }
            if (!entry.CanSwitch)
            {
                return;
            }
            // The word says what the mod is set to now; the switch shows the same.
            TMP_Text word = ModsLook.Text(state, "StateWord", entry.WantOn ? TextOn : TextOff, 22f, ModsLook.Muted, FontStyles.Bold, false);
            word.alignment = TextAlignmentOptions.MidlineRight;
            ModsLook.Size(word.gameObject, ModsLook.Width(word), 52f, 0f, 0f);

            RectTransform hit = ModsLook.Rect(state, "Switch");
            ModsLook.Size(hit.gameObject, 88f, 48f, 0f, 0f);
            Image face = hit.gameObject.AddComponent<Image>();
            face.color = ModsLook.Clear;
            Button button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            ModsLook.Colors(button, ModsLook.Clear, ModsLook.Clear, ModsLook.Clear);
            RectTransform track = ModsLook.Switch(hit, "Track", entry.WantOn, 88f, 48f);
            ModsLook.Stretch(track);
            button.onClick.AddListener(() => OnSwitch(entry));
            if (Checking)
            {
                Hold(hit.gameObject);
            }
            else if (focusSwitch && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(hit.gameObject);
            }
        }

        // ---- notes ----

        private (LayoutElement, RectTransform) BuildNotes(Transform parent, ModCatalog.Entry entry, Updates.UpdateCheck.Release newer)
        {
            RectTransform viewport = ModsLook.Rect(parent, "Notes");
            viewport.gameObject.AddComponent<RectMask2D>();
            // Catches the wheel over the gaps between the bands.
            viewport.gameObject.AddComponent<Image>().color = ModsLook.Clear;
            LayoutElement size = ModsLook.Size(viewport.gameObject, -1f, 0f, 1f, 0f);

            RectTransform content = ModsLook.Rect(viewport, "NotesContent");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VerticalLayoutGroup column = content.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 8f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = ModsLook.WheelStep;

            AddNotes(content, entry, newer);
            if (content.childCount == 0)
            {
                viewport.gameObject.SetActive(false);
                return (size, null);
            }
            return (size, content);
        }

        // Every note about the mod: what the player just pressed first, then
        // problems, then news, then plain facts.
        private void AddNotes(RectTransform content, ModCatalog.Entry entry, Updates.UpdateCheck.Release newer)
        {
            string neededBy = Escape(string.Join(", ", entry.Dependents.Select(g => ModCatalog.NameOf(_entries, g))));
            string status = Status(entry);
            if (_confirmingUpdate == entry)
            {
                Band(content, "ConfirmUpdate", null, TextConfirmUpdate, ModsLook.Warning);
            }
            else if (_updateProblemFor == entry && _updateProblem != null)
            {
                Band(content, "UpdateProblem", null, _updateProblem, ModsLook.Warning);
            }
            if (_confirmingUninstall == entry)
            {
                Band(content, "ConfirmUninstall", null, TextConfirmUninstall, ModsLook.Warning);
                if (entry.Dependents.Count > 0)
                {
                    Band(content, "NeededBy", TextNeededBy, neededBy, ModsLook.Warning);
                }
            }
            else if (_confirming == entry)
            {
                Band(content, "ConfirmOff", null, TextConfirmOff, ModsLook.Warning);
                Band(content, "NeededBy", TextNeededBy, neededBy, ModsLook.Warning);
            }
            else if (status != null && !entry.IsFramework)
            {
                Color color = FailedToLoad(entry) ? ModsLook.Error : entry.PendingUninstall ? ModsLook.Warning : NeedsRestart(entry) ? ModsLook.Accent : ModsLook.Muted;
                if (status == entry.ProblemLabel && entry.ProblemGuids.Count > 0)
                {
                    // "Not loaded. It needs" and the mods it names, as one note.
                    Band(content, "Problem", status, Escape(string.Join(", ", entry.ProblemGuids.Select(g => ModCatalog.NameOf(_entries, g)))), color);
                }
                else
                {
                    Band(content, "Status", null, status, color);
                }
            }
            if (status != entry.ProblemLabel && entry.ProblemGuids.Count > 0)
            {
                Band(content, "ProblemMods", null, Escape(string.Join(", ", entry.ProblemGuids.Select(g => ModCatalog.NameOf(_entries, g)))), ModsLook.Muted);
            }

            if (entry.Guid != null)
            {
                List<string> undeclared = NetworkWatch.SeenBy(entry.Guid).Where(c => !c.Declared).Select(c => c.Host).ToList();
                if (undeclared.Count > 0)
                {
                    Band(content, "NetUndeclared", TextUndeclaredOnline, Escape(string.Join(", ", undeclared)), ModsLook.Warning,
                        entry.Loaded ? TextInternetPage : null, () => ShowTab(TabInternet));
                }
            }
            foreach (PatchConflicts.Conflict c in ConflictsOf(entry))
            {
                string others = string.Join(", ", c.Guids.Where(g => g != entry.Guid).Select(g => ModCatalog.NameOf(_entries, g)));
                Band(content, "Conflict", c.Risky ? TextSameCodeRisky : TextSameCode, Escape(others + " (" + c.Method + ")"), ModsLook.Warning);
            }
            IReadOnlyList<string> unavailable = GameHooks.UnavailableFeatures(entry.Guid);
            if (unavailable.Count > 0)
            {
                Band(content, "Unavailable", TextUnavailable, Escape(string.Join(", ", unavailable)), ModsLook.Warning);
            }
            if (newer != null)
            {
                string url = newer.Url;
                var buttons = new List<BandButton>();
                if (_confirmingUpdate == entry)
                {
                    buttons.Add(new BandButton { Name = ReleasePageButton, Text = TextCancel, OnClick = CancelUpdate });
                    buttons.Add(new BandButton { Name = UpdateButton, Text = TextQuitAndUpdate, OnClick = () => OnUpdate(entry), Asking = true, Held = Checking });
                }
                else
                {
                    buttons.Add(new BandButton { Name = ReleasePageButton, Text = TextOpenReleasePage, OnClick = () => OpenReleasePage(url) });
                    // Only for a mod the installer put in, which the launcher
                    // can update; not again once the launcher was found missing.
                    if (_updateProblemFor != entry && Updates.LauncherUpdate.CanUpdate(entry.Guid))
                    {
                        buttons.Add(new BandButton { Name = UpdateButton, Text = TextUpdateNow, OnClick = () => OnUpdate(entry), Held = Checking });
                    }
                }
                Band(content, "Update", TextNewVersion, Escape(newer.Tag), ModsLook.Accent, buttons: buttons);
            }
            if (entry.Guid != null && !NetworkWatch.HasUndeclared(entry.Guid))
            {
                List<string> hosts = NetworkWatch.DeclaredBy(entry.Guid).Select(u => u.Host).Distinct().ToList();
                if (hosts.Count > 0)
                {
                    Band(content, "Net", TextUsesInternet, Escape(string.Join(", ", hosts)), ModsLook.Muted);
                }
            }
            int reloads = ModReload.ReloadCount(entry.Guid);
            if (reloads > 0)
            {
                Band(content, "Reloaded", TextReloaded, reloads.ToString(CultureInfo.InvariantCulture), ModsLook.Muted, note: TextNotLoadedFile);
            }
            // What the mod clashes with is only certain once the check is in.
            if (Checking && !entry.IsFramework)
            {
                Band(content, "Checking", null, TextChecking, ModsLook.Muted, spinner: true);
            }
        }

        // One note: a bar of its colour on the left, a bold label in that
        // colour, the text (as given: names from mods are escaped by the
        // caller), maybe a fixed sentence under it, and maybe a button or
        // two. A label too long to sit beside the text (common in Japanese or
        // German, or beside two buttons) goes above it.
        private void Band(RectTransform parent, string name, string label, string value, Color color,
            string buttonText = null, UnityAction onClick = null, bool spinner = false, string note = null, List<BandButton> buttons = null)
        {
            var shown = new List<BandButton>();
            if (buttonText != null)
            {
                shown.Add(new BandButton { Name = name + "Button", Text = buttonText, OnClick = onClick });
            }
            if (buttons != null)
            {
                shown.AddRange(buttons);
            }

            RectTransform band = ModsLook.Rect(parent, name);
            ModsLook.Shape(band.gameObject, ModsLook.Rounded, ModsLook.Card, 8f).raycastTarget = false;
            HorizontalLayoutGroup row = band.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(20, 12, 10, 10);
            row.spacing = 12f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            RectTransform bar = ModsLook.Rect(band, "Bar");
            bar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(0f, 1f);
            bar.pivot = new Vector2(0f, 0.5f);
            bar.offsetMin = new Vector2(0f, 6f);
            bar.offsetMax = new Vector2(4f, -6f);
            ModsLook.Shape(bar.gameObject, ModsLook.Pill, color).raycastTarget = false;

            const float size = 20f;
            float width = InnerWidth - 32f;
            if (spinner)
            {
                TMP_Text turn = ModsLook.Text(band, "Spinner", SpinnerFrames.Substring(0, 1), size, CheckingColor, FontStyles.Normal, false);
                ModsLook.Size(turn.gameObject, 20f, -1f, 0f, 0f);
                _spinners.Add(turn);
                width -= 32f;
            }

            RectTransform text = ModsLook.Rect(band, "Text");
            LayoutElement textSize = ModsLook.Size(text.gameObject, -1f, -1f, 1f, 0f);
            textSize.minWidth = 0f;
            textSize.preferredWidth = 0f;

            foreach (BandButton button in shown)
            {
                width -= 12f + CreateBandButton(band, button);
            }

            TMP_Text head = null;
            if (label != null)
            {
                head = ModsLook.Text(text, "Label", label, size, ModsLook.Readable(color), FontStyles.Bold, false);
            }
            float headWidth = head != null ? ModsLook.Width(head) : 0f;
            // Beside two buttons the value would be squeezed into a narrow
            // column, so the label always goes above it then.
            bool beside = head == null || shown.Count < 2 && headWidth <= width * 0.45f;
            HorizontalOrVerticalLayoutGroup lines = beside
                ? (HorizontalOrVerticalLayoutGroup)text.gameObject.AddComponent<HorizontalLayoutGroup>()
                : text.gameObject.AddComponent<VerticalLayoutGroup>();
            lines.spacing = beside ? 12f : 2f;
            lines.childAlignment = TextAnchor.UpperLeft;
            lines.childControlWidth = true;
            lines.childControlHeight = true;
            lines.childForceExpandWidth = !beside;
            lines.childForceExpandHeight = false;
            if (head != null)
            {
                if (beside)
                {
                    ModsLook.Size(head.gameObject, headWidth, -1f, 0f, 0f);
                }
                else
                {
                    head.textWrappingMode = TextWrappingModes.Normal;
                    head.overflowMode = TextOverflowModes.Overflow;
                }
            }
            TMP_Text body = ModsLook.Text(text, "Value", value, size,
                spinner ? CheckingColor : ModsLook.Label, spinner ? FontStyles.Italic : FontStyles.Normal, true);
            LayoutElement bodySize = ModsLook.Size(body.gameObject, -1f, -1f, 1f, 0f);
            bodySize.minWidth = 0f;
            bodySize.preferredWidth = 0f;
            if (note != null)
            {
                // A fixed sentence under the value, a text of its own so a
                // language pack can translate it whole.
                RectTransform values = ModsLook.Rect(text, "Values");
                LayoutElement valuesSize = ModsLook.Size(values.gameObject, -1f, -1f, 1f, 0f);
                valuesSize.minWidth = 0f;
                valuesSize.preferredWidth = 0f;
                VerticalLayoutGroup stack = values.gameObject.AddComponent<VerticalLayoutGroup>();
                stack.spacing = 2f;
                stack.childControlWidth = true;
                stack.childControlHeight = true;
                stack.childForceExpandWidth = true;
                stack.childForceExpandHeight = false;
                body.transform.SetParent(values, false);
                TMP_Text after = ModsLook.Text(values, "Note", note, size, ModsLook.Label, FontStyles.Normal, true);
                LayoutElement afterSize = ModsLook.Size(after.gameObject, -1f, -1f, 1f, 0f);
                afterSize.minWidth = 0f;
                afterSize.preferredWidth = 0f;
            }
            // The buttons, if any, go last, at the right, in their order.
            foreach (BandButton button in shown)
            {
                band.Find(button.Name)?.SetAsLastSibling();
            }
        }

        private static float CreateBandButton(Transform parent, BandButton spec)
        {
            GameObject button = FlatButton(parent, spec.Name, spec.Text, 19f, spec.Asking ? AskingFace : ModsLook.Raised, ModsLook.Label, spec.OnClick);
            float width = ((RectTransform)button.transform).sizeDelta.x;
            ModsLook.Size(button, width, 44f, 0f, 0f);
            if (spec.Asking)
            {
                RectTransform edge = ModsLook.Rect(button.transform, "Edge");
                ModsLook.Stretch(edge);
                ModsLook.Shape(edge.gameObject, ModsLook.Outline, ModsLook.Warning, 10f).raycastTarget = false;
            }
            if (spec.Held)
            {
                Hold(button);
            }
            return width;
        }

        // A button with a rounded face and its label, as wide as the label.
        // Pages other mods add use it too (ModsScreenLook.Button).
        internal static GameObject FlatButton(Transform parent, string name, string text, float size, Color face, Color color, UnityAction onClick)
        {
            RectTransform rect = ModsLook.Rect(parent, name);
            Image image = ModsLook.Shape(rect.gameObject, ModsLook.Rounded, Color.white, 10f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ModsLook.Colors(button, face, ModsLook.Hover, ModsLook.Inset);
            TMP_Text label = ModsLook.Text(rect, "Label", text, size, color, FontStyles.Bold, false);
            label.alignment = TextAlignmentOptions.Center;
            rect.sizeDelta = new Vector2(ModsLook.Width(label) + 32f, 44f);
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }
            return rect.gameObject;
        }

        // ---- tabs ----

        private List<Tab> TabsOf(ModCatalog.Entry entry)
        {
            var tabs = new List<Tab> { new Tab { Key = TabAbout, Title = TextAbout } };
            // Read once a build: the Settings tab uses the same list.
            _items = new List<ConfigItem>();
            if (!entry.Loaded || entry.Guid == null)
            {
                return tabs;
            }
            _items = ConfigItem.For(entry);
            int settings = _items.Count;
            if (settings > 0)
            {
                tabs.Add(new Tab { Key = TabSettings, Title = TextSettings, Count = settings.ToString() });
            }
            if (IsOnline(entry, out bool undeclared))
            {
                tabs.Add(new Tab { Key = TabInternet, Title = TextInternetPage, Warn = undeclared, Page = InternetPage(entry) });
            }
            List<ModsScreenPage> pages = ModFramework.PagesFor(entry.Guid);
            for (int i = 0; i < pages.Count; i++)
            {
                tabs.Add(new Tab { Key = "Page" + i, Title = pages[i].Title, Page = pages[i] });
            }
            return tabs;
        }

        // The tabs in a row, as wide as their words, going on to a second row
        // when there are more than fit. The one showing has a line under it.
        private void BuildTabs(Transform parent, ModCatalog.Entry entry, List<Tab> tabs)
        {
            RectTransform strip = ModsLook.Rect(parent, "Tabs");
            const float height = 48f;
            float width = InnerWidth;
            float x = 0f;
            float y = 0f;
            foreach (Tab tab in tabs)
            {
                bool chosen = tab.Key == _tab;
                RectTransform rect = ModsLook.Rect(strip, "Tab" + tab.Key);
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                Image face = ModsLook.Shape(rect.gameObject, ModsLook.Rounded, Color.white, 8f);
                Button button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = face;
                ModsLook.Colors(button, ModsLook.Clear, ModsLook.Hover, ModsLook.Raised);

                TMP_Text label = ModsLook.Text(rect, "Label", tab.Title, 22f, chosen ? ModsLook.Label : ModsLook.Muted, FontStyles.Bold, false);
                // A long page title (or translation) is cut short rather than
                // run past the panel; room is left for the count and the dot.
                float labelWidth = Mathf.Min(ModsLook.Width(label), Mathf.Max(60f, width - 100f));
                float w = 16f + labelWidth + 16f;
                Place(label, 16f, labelWidth);
                if (tab.Count != null)
                {
                    TMP_Text count = ModsLook.Text(rect, "Count", tab.Count, 18f, ModsLook.Muted, FontStyles.Normal, false);
                    float countWidth = ModsLook.Width(count);
                    Place(count, w - 8f, countWidth);
                    w += countWidth + 4f;
                }
                if (tab.Warn)
                {
                    RectTransform dot = ModsLook.Rect(rect, "Warn");
                    dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
                    dot.pivot = new Vector2(0f, 0.5f);
                    dot.sizeDelta = new Vector2(10f, 10f);
                    dot.anchoredPosition = new Vector2(w - 8f, 0f);
                    ModsLook.Shape(dot.gameObject, ModsLook.Pill, ModsLook.Warning).raycastTarget = false;
                    w += 14f;
                }
                if (chosen)
                {
                    RectTransform line = ModsLook.Rect(rect, "Line");
                    line.anchorMin = new Vector2(0f, 0f);
                    line.anchorMax = new Vector2(1f, 0f);
                    line.pivot = new Vector2(0.5f, 0f);
                    line.offsetMin = new Vector2(8f, 0f);
                    line.offsetMax = new Vector2(-8f, 3f);
                    ModsLook.Shape(line.gameObject, ModsLook.Pill, ModsLook.Accent).raycastTarget = false;
                }

                if (x > 0f && x + w > width)
                {
                    x = 0f;
                    y += height;
                }
                rect.sizeDelta = new Vector2(w, height);
                rect.anchoredPosition = new Vector2(x, -y);
                x += w + 6f;

                string key = tab.Key;
                button.onClick.AddListener(() => ShowTab(key));
            }

            RectTransform rule = ModsLook.Rect(strip, "Rule");
            rule.anchorMin = new Vector2(0f, 0f);
            rule.anchorMax = new Vector2(1f, 0f);
            rule.pivot = new Vector2(0.5f, 0f);
            rule.offsetMin = Vector2.zero;
            rule.offsetMax = new Vector2(0f, 1f);
            Image ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.color = ModsLook.Border;
            ruleImage.raycastTarget = false;
            ModsLook.Size(strip.gameObject, -1f, y + height + 2f, 1f, 0f);
        }

        private void ShowTab(string key)
        {
            ModCatalog.Entry entry = _selected;
            if (entry == null)
            {
                return;
            }
            _tab = key;
            RebuildDetails(false);
            Focus("Tab" + key);
        }

        // Moves one tab left (-1) or right (+1), for the pad's shoulder buttons.
        internal void StepTab(int direction)
        {
            ModCatalog.Entry entry = _selected;
            if (entry == null || !isActiveAndEnabled)
            {
                return;
            }
            List<Tab> tabs = TabsOf(entry);
            int index = tabs.FindIndex(t => t.Key == _tab);
            int next = ((index < 0 ? 0 : index + direction) % tabs.Count + tabs.Count) % tabs.Count;
            if (tabs[next].Key != _tab)
            {
                ShowTab(tabs[next].Key);
            }
        }

        private void BuildTab(ModCatalog.Entry entry, Tab tab)
        {
            if (tab.Page != null)
            {
                BuildPage(tab.Page);
                return;
            }
            if (tab.Key == TabSettings)
            {
                BuildSettingsTab(entry);
                return;
            }
            BuildAbout(entry);
        }

        // ---- About ----

        private void BuildAbout(ModCatalog.Entry entry)
        {
            RectTransform content = ScrollArea(_body);
            content.GetComponent<VerticalLayoutGroup>().spacing = 6f;

            if (!string.IsNullOrEmpty(entry.Description))
            {
                Line(content, entry.Description, 24f, FontStyles.Normal, ModsLook.Label, 0f);
                Spacer(content, 12f);
            }

            if (entry.Uses.Count > 0 && !entry.IsFramework)
            {
                Heading(content, TextUses);
                Chips(content, "UsesChips", entry.Uses.Select(u =>
                    ModCatalog.ShortNameOf(_entries, u.Key) + (u.Value != null && u.Value > new Version(0, 0) ? " " + u.Value + "+" : "")).ToList());
                Spacer(content, 12f);
            }
            if (entry.Dependents.Count > 0)
            {
                Heading(content, TextNeededBy);
                Chips(content, "NeededByChips", entry.Dependents.Select(g => ModCatalog.ShortNameOf(_entries, g)).ToList());
                Spacer(content, 12f);
            }

            if (entry.CanUninstall)
            {
                Spacer(content, 8f);
                RectTransform row = ModsLook.Rect(content, "UninstallRow");
                ModsLook.Size(row.gameObject, -1f, 56f, 1f, 0f);
                bool confirming = _confirmingUninstall == entry;
                string text = entry.PendingUninstall ? TextCancelUninstall : TextUninstall;
                GameObject uninstall = FlatButton(row, "Uninstall", text, 22f, confirming ? new Color(0.30f, 0.12f, 0.12f) : ModsLook.Clear,
                    entry.PendingUninstall ? ModsLook.Label : ModsLook.Error, () => OnUninstall(entry));
                var rect = (RectTransform)uninstall.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(rect.sizeDelta.x + 16f, 52f);
                RectTransform edge = ModsLook.Rect(rect, "Edge");
                ModsLook.Stretch(edge);
                ModsLook.Shape(edge.gameObject, ModsLook.Outline, entry.PendingUninstall ? ModsLook.Border : ModsLook.Error, 10f).raycastTarget = false;
                if (Checking)
                {
                    Hold(uninstall);
                }
            }
        }

        // A small heading in capitals over a group of lines.
        private static void Heading(RectTransform content, string text)
        {
            var row = new GameObject("Heading", typeof(RectTransform));
            ((RectTransform)row.transform).SetParent(content, false);
            HorizontalLayoutGroup pad = row.AddComponent<HorizontalLayoutGroup>();
            pad.childControlHeight = true;
            pad.childControlWidth = true;
            pad.childForceExpandWidth = true;
            TMP_Text label = GroupLabel(row.transform, text);
            label.alignment = TextAlignmentOptions.BottomLeft;
        }

        // Names in rounded chips, left to right, going on to the next line
        // when a row is full.
        private void Chips(RectTransform content, string name, List<string> names)
        {
            RectTransform holder = ModsLook.Rect(content, name);
            const float height = 36f;
            const float gap = 10f;
            float width = InnerWidth;
            float x = 0f;
            float y = 0f;
            foreach (string text in names)
            {
                RectTransform chip = ModsLook.Rect(holder, "Chip");
                chip.anchorMin = chip.anchorMax = new Vector2(0f, 1f);
                chip.pivot = new Vector2(0f, 1f);
                ModsLook.Shape(chip.gameObject, ModsLook.Pill, ModsLook.Card, height / 2f).raycastTarget = false;
                TMP_Text label = ModsLook.Text(chip, "Label", Escape(text), 19f, ModsLook.Label, FontStyles.Normal, false);
                label.alignment = TextAlignmentOptions.Center;
                float w = Mathf.Min(width, ModsLook.Width(label) + 30f);
                if (x > 0f && x + w > width)
                {
                    x = 0f;
                    y += height + gap;
                }
                chip.sizeDelta = new Vector2(w, height);
                chip.anchoredPosition = new Vector2(x, -y);
                x += w + gap;
            }
            ModsLook.Size(holder.gameObject, -1f, y + height, 1f, 0f);
        }

        // ---- the pad's buttons ----

        // A line at the bottom naming the pad's buttons for this screen.
        private static void BuildHints(Transform parent)
        {
            RectTransform hints = ModsLook.Rect(parent, "Hints");
            ModsLook.Size(hints.gameObject, -1f, 34f, 1f, 0f);
            HorizontalLayoutGroup row = hints.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8f;
            row.childAlignment = TextAnchor.MiddleRight;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            Hint(hints, new[] { "A" }, TextSelect);
            Hint(hints, new[] { "B" }, TextBack);
            Hint(hints, new[] { "Y" }, TextSearch);
            Hint(hints, new[] { "LB", "RB" }, TextTabs);
        }

        private static void Hint(RectTransform parent, string[] buttons, string text)
        {
            foreach (string button in buttons)
            {
                RectTransform glyph = ModsLook.Rect(parent, "Button " + button);
                ModsLook.Shape(glyph.gameObject, ModsLook.PillOutline, ModsLook.Muted, 16f).raycastTarget = false;
                TMP_Text letter = ModsLook.Text(glyph, "Label", button, 15f, ModsLook.Label, FontStyles.Bold, false);
                letter.alignment = TextAlignmentOptions.Center;
                ModsLook.Size(glyph.gameObject, Mathf.Max(32f, ModsLook.Width(letter) + 16f), 32f, 0f, 0f);
            }
            TMP_Text label = ModsLook.Text(parent, "Hint", text, 18f, ModsLook.Muted, FontStyles.Bold, false);
            ModsLook.Size(label.gameObject, ModsLook.Width(label) + 14f, 32f, 0f, 0f);
        }

        // ---- going back ----

        // Back steps out one level at a time: from what is on a tab to the
        // tab, from the tabs to the mod's row in the list, from the list to
        // Options. A click on the game's Back button always leaves.
        private bool StepBack()
        {
            GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (focused == null || !focused.activeInHierarchy || mouse != null && mouse.leftButton.wasReleasedThisFrame)
            {
                return false;
            }
            // Back while a key is being taken (Esc, or B on the pad) stops
            // taking it, and stays.
            ShortcutCapture capture = focused.GetComponent<ShortcutCapture>();
            if (capture != null)
            {
                capture.Cancel();
                return true;
            }
            // Back while typing a setting's value (Esc, or B on the pad) puts
            // the value back and stops typing, and stays. Left alone, leaving
            // the field would save what was half typed.
            if (StopTyping(focused))
            {
                return true;
            }
            // Back while Update asks takes it back, and stays on the button.
            if (_confirmingUpdate != null && Details != null && focused.transform.IsChildOf(Details))
            {
                CancelUpdate();
                return true;
            }
            if (Details != null && focused.transform.IsChildOf(Details))
            {
                string tab = "Tab" + _tab;
                if (focused.name != tab && FocusIn(Details, tab))
                {
                    return true;
                }
                // The mod's row may be hidden by the search or the folded
                // libraries: then the first row, or the search field.
                return FocusRow(_selected) || FocusFirstRow() || SelectSearch();
            }
            // Leaving the search field: to the list, not off the screen.
            if (focused.GetComponent<TMP_InputField>() != null && ListTop != null && focused.transform.IsChildOf(ListTop))
            {
                return FocusRow(_selected) || FocusFirstRow();
            }
            return false;
        }

        private bool FocusRow(ModCatalog.Entry entry)
        {
            ModRowSelect row = _listRows.FirstOrDefault(r => r != null && r.isActiveAndEnabled && SameMod(r.Entry, entry));
            if (row == null || EventSystem.current == null)
            {
                return false;
            }
            EventSystem.current.SetSelectedGameObject(row.gameObject);
            return true;
        }

        // From the game's buttons on the left (Back) into the list: the mod's
        // row, else the first row, else the search field. The game's own
        // navigation only knows its buttons, so it never leads here.
        internal bool FocusList()
        {
            return FocusRow(_selected) || FocusFirstRow() || SelectSearch();
        }

        private bool FocusFirstRow()
        {
            ModRowSelect row = _listRows.FirstOrDefault(r => r != null && r.isActiveAndEnabled);
            if (row == null || EventSystem.current == null)
            {
                return false;
            }
            EventSystem.current.SetSelectedGameObject(row.gameObject);
            return true;
        }
    }
}
