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
    // The list of mods on the left: a search field and filters above it, the
    // player's own mods first, and the framework with its libraries folded
    // into a group of their own below. Each row shows the mod's icon (or its
    // initials), name, version and author, a tag for everything that applies,
    // and whether it is on.
    internal sealed partial class ModsMenu
    {
        internal const string TextSearchMods = "Search mods";
        internal const string TextAll = "All";
        internal const string TextNeedsAttention = "Needs attention";
        internal const string TextYourMods = "Your mods";
        internal const string TextLibraries = "Libraries";
        internal const string TextNoMatch = "No mods match.";
        internal const string TextNeedsRestart = "Needs restart";
        internal const string TextNotLoadedTag = "Not loaded";

        // The search field and the filters, above the list.
        internal const float ListTopHeight = 132f;

        internal RectTransform ListTop;

        private enum ListFilter
        {
            All,
            On,
            Off,
            Attention,
        }

        private TMP_InputField _search;
        private RectTransform _chips;
        private string _query = "";
        private ListFilter _filter = ListFilter.All;

        // Folded until the player opens it; remembered while the game runs.
        private static bool _librariesOpen;

        private readonly List<ModRowSelect> _listRows = new List<ModRowSelect>();

        // What the search shows and hides without building the list again.
        private GameObject _yoursHeader;
        private TMP_Text _yoursCount;
        private GameObject _librariesRow;
        private TMP_Text _librariesCount;
        private RectTransform _librariesArrow;
        private readonly List<GameObject> _librariesTags = new List<GameObject>();
        private GameObject _noMatch;

        // Colours for the initials of a mod without an icon, picked by its name.
        private static readonly Color[] InitialsColors =
        {
            new Color(0.50f, 0.70f, 0.90f),
            new Color(0.91f, 0.72f, 0.45f),
            new Color(0.32f, 0.78f, 0.72f),
            new Color(0.90f, 0.58f, 0.62f),
            new Color(0.68f, 0.60f, 0.92f),
            new Color(0.58f, 0.80f, 0.50f),
        };

        // The framework and its libraries: the group that folds.
        private static bool IsLibraryLike(ModCatalog.Entry entry)
        {
            return entry.IsFramework || entry.IsLibrary;
        }

        // Something the player may need to act on: a conflict, a connection
        // the mod did not declare, a mod that did not load, a feature this
        // game build lacks.
        private bool NeedsAttention(ModCatalog.Entry entry)
        {
            if (ConflictsOf(entry).Count > 0 || FailedToLoad(entry))
            {
                return true;
            }
            if (entry.Guid == null)
            {
                return false;
            }
            return NetworkWatch.HasUndeclared(entry.Guid) || GameHooks.UnavailableFeatures(entry.Guid).Count > 0;
        }

        private static bool FailedToLoad(ModCatalog.Entry entry)
        {
            return !entry.Loaded && entry.WantOn && entry.ProblemLabel != null && !entry.IsPatcher;
        }

        // Switched on or off since the game started, so a restart is due.
        private static bool NeedsRestart(ModCatalog.Entry entry)
        {
            return !entry.IsPatcher && !entry.PendingUninstall && entry.Loaded != entry.WantOn && entry.ProblemLabel == null;
        }

        // The mod shown when the screen opens: the first of the player's own,
        // not the framework.
        private ModCatalog.Entry FirstShown(List<ModCatalog.Entry> entries)
        {
            return entries.FirstOrDefault(e => !IsLibraryLike(e)) ?? entries.FirstOrDefault();
        }

        private bool Passes(ModCatalog.Entry entry, ListFilter filter)
        {
            return InFilter(entry, filter) && Matches(entry);
        }

        private bool InFilter(ModCatalog.Entry entry, ListFilter filter)
        {
            switch (filter)
            {
                case ListFilter.On:
                    return entry.WantOn;
                case ListFilter.Off:
                    return !entry.WantOn;
                case ListFilter.Attention:
                    return NeedsAttention(entry);
                default:
                    return true;
            }
        }

        // What is typed in the search field, in the name, id or authors.
        private bool Matches(ModCatalog.Entry entry)
        {
            if (_query.Length == 0)
            {
                return true;
            }
            return Contains(entry.DisplayName, _query) || Contains(entry.Name, _query) || Contains(entry.Guid, _query) || Contains(entry.Authors, _query);
        }

        private static bool Contains(string text, string part)
        {
            return text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- above the list ----

        private void BuildListTop()
        {
            if (ListTop == null || _search != null)
            {
                return;
            }
            _search = CreateSearchField(ListTop);
            _chips = ModsLook.Rect(ListTop, "Filters");
            _chips.anchorMin = new Vector2(0f, 1f);
            _chips.anchorMax = new Vector2(1f, 1f);
            _chips.pivot = new Vector2(0f, 1f);
            _chips.offsetMin = new Vector2(ListLeftMargin - 20f, -118f);
            _chips.offsetMax = new Vector2(-14f, -78f);
        }

        // The search field is built once and kept: it takes the colours of
        // the glass, or of the tint alone, when they change.
        private void RepaintSearch()
        {
            if (_search == null)
            {
                return;
            }
            ModsLook.Colors(_search, ModsLook.Field, ModsLook.Hover, ModsLook.Field);
            if (_search.placeholder != null)
            {
                _search.placeholder.color = ModsLook.Muted;
            }
            Transform icon = _search.transform.Find("Icon");
            Image image = icon != null ? icon.GetComponent<Image>() : null;
            if (image != null)
            {
                image.color = ModsLook.Muted;
            }
        }

        private TMP_InputField CreateSearchField(RectTransform parent)
        {
            TMP_InputField field = SearchBox(parent, "Search", TextSearchMods, 22f, OnSearch);
            var box = (RectTransform)field.transform;
            box.anchorMin = new Vector2(0f, 1f);
            box.anchorMax = new Vector2(1f, 1f);
            box.pivot = new Vector2(0.5f, 1f);
            box.offsetMin = new Vector2(ListLeftMargin - 20f, -66f);
            box.offsetMax = new Vector2(-14f, -14f);
            return field;
        }

        // A search field: a magnifier, the placeholder, an accent line under
        // it. The caller places it.
        private static TMP_InputField SearchBox(Transform parent, string name, string placeholderText, float size, UnityAction<string> onChange)
        {
            RectTransform box = ModsLook.Rect(parent, name);
            // Built inactive: the input field looks for its text component when
            // it wakes, which must be set by then.
            box.gameObject.SetActive(false);
            Image background = ModsLook.Shape(box.gameObject, ModsLook.Rounded, Color.white);

            RectTransform line = ModsLook.Rect(box, "Underline");
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.offsetMin = new Vector2(8f, 0f);
            line.offsetMax = new Vector2(-8f, 2f);
            line.gameObject.AddComponent<Image>().color = ModsLook.Accent;
            line.GetComponent<Image>().raycastTarget = false;

            if (ModsLook.Magnifier != null)
            {
                RectTransform icon = ModsLook.Rect(box, "Icon");
                icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                icon.pivot = new Vector2(0f, 0.5f);
                icon.sizeDelta = new Vector2(24f, 24f);
                icon.anchoredPosition = new Vector2(14f, 0f);
                Image image = icon.gameObject.AddComponent<Image>();
                image.sprite = ModsLook.Magnifier;
                image.color = ModsLook.Muted;
                image.raycastTarget = false;
            }

            RectTransform area = ModsLook.Rect(box, "Text Area");
            ModsLook.Stretch(area, 50f, 4f, 14f, 4f);
            area.gameObject.AddComponent<RectMask2D>();

            TMP_Text text = ModsLook.Text(area, "Text", "", size, ModsLook.Label, FontStyles.Normal, false);
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            TMP_Text placeholder = ModsLook.Text(area, "Placeholder", placeholderText, size, ModsLook.Muted, FontStyles.Normal, false);

            TMP_InputField field = box.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = background;
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.customCaretColor = true;
            field.caretColor = ModsLook.Label;
            field.selectionColor = new Color(ModsLook.Accent.r, ModsLook.Accent.g, ModsLook.Accent.b, 0.4f);
            ModsLook.Colors(field, ModsLook.Field, ModsLook.Hover, ModsLook.Field);
            // The pad passing over it does not start typing (on the Steam Deck
            // that would open the keyboard): A does, or Y (FocusSearch).
            field.shouldActivateOnSelect = false;
            field.onValueChanged.AddListener(onChange);
            box.gameObject.SetActive(true);
            return field;
        }

        // Y on the pad: the search field, ready to type in.
        internal void FocusSearch()
        {
            if (SelectSearch())
            {
                _search.ActivateInputField();
            }
        }

        // The search field selected, without starting to type.
        private bool SelectSearch()
        {
            if (_search == null || !_search.isActiveAndEnabled || EventSystem.current == null)
            {
                return false;
            }
            EventSystem.current.SetSelectedGameObject(_search.gameObject);
            return true;
        }

        private void OnSearch(string value)
        {
            string query = (value ?? "").Trim();
            if (query == _query)
            {
                return;
            }
            _query = query;
            ApplySearch();
        }

        // The filters, each with how many mods it holds, laid out left to
        // right as wide as their words. When they do not fit (a long
        // translation, a narrow window), all of them get smaller together.
        private void RebuildChips()
        {
            if (_chips == null)
            {
                return;
            }
            Clear(_chips);
            var filters = new[]
            {
                (ListFilter.All, TextAll, "FilterAll"),
                (ListFilter.On, TextOn, "FilterOn"),
                (ListFilter.Off, TextOff, "FilterOff"),
                (ListFilter.Attention, TextNeedsAttention, "FilterAttention"),
            };
            var chips = new List<(RectTransform Chip, float Width)>();
            foreach ((ListFilter filter, string text, string name) in filters)
            {
                // How many mods the filter holds, whatever is typed.
                int count = _entries.Count(e => InFilter(e, filter));
                chips.Add(CreateChip(filter, text, name, count));
            }
            float available = _chips.rect.width;
            float total = chips.Sum(c => c.Width) + 10f * (chips.Count - 1);
            float scale = available > 0f && total > available ? Mathf.Max(0.6f, available / total) : 1f;
            float x = 0f;
            foreach ((RectTransform chip, float width) in chips)
            {
                chip.localScale = new Vector3(scale, scale, 1f);
                chip.anchoredPosition = new Vector2(x, 0f);
                x += (width + 10f) * scale;
            }
        }

        private (RectTransform, float) CreateChip(ListFilter filter, string text, string name, int count)
        {
            bool chosen = _filter == filter;
            RectTransform chip = ModsLook.Rect(_chips, name);
            chip.anchorMin = chip.anchorMax = new Vector2(0f, 0.5f);
            chip.pivot = new Vector2(0f, 0.5f);
            Image face = ModsLook.Shape(chip.gameObject, ModsLook.Pill, Color.white, 20f);
            Button button = chip.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            ModsLook.Colors(button, chosen ? ModsLook.Card : ModsLook.Clear, ModsLook.Hover, ModsLook.Raised);

            RectTransform edge = ModsLook.Rect(chip, "Edge");
            ModsLook.Stretch(edge);
            ModsLook.Shape(edge.gameObject, ModsLook.PillOutline, chosen ? ModsLook.Accent : ModsLook.ChipEdge, 20f).raycastTarget = false;

            TMP_Text label = ModsLook.Text(chip, "Label", text, 19f, chosen ? ModsLook.Label : ModsLook.Muted, FontStyles.Bold, false);
            TMP_Text number = ModsLook.Text(chip, "Count", count.ToString(), 19f,
                filter == ListFilter.Attention && count > 0 ? ModsLook.Warning : ModsLook.Muted, FontStyles.Normal, false);
            float labelWidth = ModsLook.Width(label);
            float numberWidth = ModsLook.Width(number);
            float width = 18f + labelWidth + 8f + numberWidth + 18f;
            chip.sizeDelta = new Vector2(width, 40f);
            Place(label, 18f, labelWidth);
            Place(number, 18f + labelWidth + 8f, numberWidth);

            button.onClick.AddListener(() =>
            {
                _filter = filter;
                RebuildList();
                FocusIn(_chips, name);
            });
            return (chip, width);
        }

        // A one-line label from `left`, `width` wide, the full height of its parent.
        private static void Place(TMP_Text label, float left, float width)
        {
            var rect = (RectTransform)label.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(left + width, 0f);
        }

        // ---- the list ----

        // Every row that passes the filter is built, the folded libraries too;
        // what the search and the fold show is then only a matter of hiding
        // rows (ApplySearch), so typing does not build the list again.
        private void BuildModList()
        {
            BuildListTop();
            RebuildChips();
            _listRows.Clear();
            _librariesTags.Clear();

            List<ModCatalog.Entry> shown = _entries.Where(e => InFilter(e, _filter)).ToList();
            _yoursHeader = CreateGroupRow(TextYourMods, out _yoursCount);
            _rows.Add(_yoursHeader);
            foreach (ModCatalog.Entry entry in shown.Where(e => !IsLibraryLike(e)))
            {
                _rows.Add(CreateListRow(entry));
            }
            List<ModCatalog.Entry> libraries = shown.Where(IsLibraryLike).ToList();
            _librariesRow = CreateLibrariesRow(libraries);
            _rows.Add(_librariesRow);
            foreach (ModCatalog.Entry entry in libraries)
            {
                _rows.Add(CreateListRow(entry));
            }
            _noMatch = EmptyRow("NoMatch", 64f);
            TMP_Text label = ModsLook.Text(_noMatch.transform, "Label", TextNoMatch, 20f, ModsLook.Muted, FontStyles.Italic, false);
            ((RectTransform)label.transform).offsetMin = new Vector2(ListLeftMargin, 0f);
            _rows.Add(_noMatch);
            ApplySearch();
        }

        // Shows the rows that match what is typed, opens the libraries while
        // the list is narrowed, and counts what each group shows.
        private void ApplySearch()
        {
            bool narrowed = _query.Length > 0 || _filter != ListFilter.All;
            bool open = _librariesOpen || narrowed;
            int yours = 0;
            int libraries = 0;
            foreach (ModRowSelect row in _listRows)
            {
                if (row == null)
                {
                    continue;
                }
                bool library = IsLibraryLike(row.Entry);
                bool match = Matches(row.Entry);
                if (match)
                {
                    if (library)
                    {
                        libraries++;
                    }
                    else
                    {
                        yours++;
                    }
                }
                row.transform.parent.gameObject.SetActive(match && (!library || open));
            }
            if (_yoursHeader != null)
            {
                _yoursHeader.SetActive(yours > 0 || !narrowed);
                _yoursCount.text = yours.ToString();
            }
            if (_librariesRow != null)
            {
                _librariesRow.SetActive(libraries > 0);
                _librariesCount.text = libraries.ToString();
                if (_librariesArrow != null)
                {
                    _librariesArrow.localRotation = Quaternion.Euler(0f, 0f, open ? -90f : 0f);
                }
                foreach (GameObject tag in _librariesTags)
                {
                    tag.SetActive(!open);
                }
            }
            if (_noMatch != null)
            {
                _noMatch.SetActive(yours + libraries == 0);
            }
        }

        private GameObject EmptyRow(string name, float height)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(Content, false);
            LayoutElement layout = row.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleWidth = 1f;
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, height);
            return row;
        }

        // A group's heading: its name in small capitals and how many it holds.
        // Not selectable, so the pad passes over it.
        private GameObject CreateGroupRow(string title, out TMP_Text count)
        {
            GameObject row = EmptyRow("Group " + title, 48f);
            TMP_Text label = GroupLabel(row.transform, title);
            float width = ModsLook.Width(label);
            var rect = (RectTransform)label.transform;
            rect.offsetMin = new Vector2(ListLeftMargin - 6f, 0f);
            rect.offsetMax = new Vector2(0f, -10f);
            TMP_Text number = ModsLook.Text(row.transform, "Count", "0", 18f, ModsLook.Muted, FontStyles.Normal, false);
            var numberRect = (RectTransform)number.transform;
            numberRect.offsetMin = new Vector2(ListLeftMargin - 6f + width + 12f, 0f);
            numberRect.offsetMax = new Vector2(0f, -10f);
            count = number;
            return row;
        }

        private static TMP_Text GroupLabel(Transform parent, string title)
        {
            // Shown in capitals, but the text stays the phrase itself, so a
            // translation pack still finds it.
            TMP_Text label = ModsLook.Text(parent, "Label", title, 18f, ModsLook.Muted, FontStyles.Bold | FontStyles.UpperCase, false);
            label.characterSpacing = 8f;
            return label;
        }

        // The framework and its libraries, folded until pressed. A problem in
        // one of them shows on the folded row too, so none is hidden.
        private GameObject CreateLibrariesRow(List<ModCatalog.Entry> libraries)
        {
            GameObject row = EmptyRow("Libraries", 60f);
            RectTransform band = ModsLook.Rect(row.transform, "Libraries");
            ModsLook.Stretch(band, ListLeftMargin - 20f, 4f, 0f, 4f);
            Image face = ModsLook.Shape(band.gameObject, ModsLook.Rounded, Color.white);
            Button button = band.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            ModsLook.Colors(button, ModsLook.Clear, ModsLook.Hover, ModsLook.Raised);
            button.onClick.AddListener(() =>
            {
                _librariesOpen = !_librariesOpen;
                ApplySearch();
            });

            float x = 14f;
            if (ModsLook.Triangle != null)
            {
                RectTransform arrow = ModsLook.Rect(band, "Arrow");
                arrow.anchorMin = arrow.anchorMax = new Vector2(0f, 0.5f);
                arrow.pivot = new Vector2(0.5f, 0.5f);
                arrow.sizeDelta = new Vector2(18f, 18f);
                arrow.anchoredPosition = new Vector2(x + 9f, 0f);
                Image image = arrow.gameObject.AddComponent<Image>();
                image.sprite = ModsLook.Triangle;
                image.color = ModsLook.Muted;
                image.raycastTarget = false;
                _librariesArrow = arrow;
                x += 30f;
            }
            else
            {
                _librariesArrow = null;
            }
            TMP_Text label = GroupLabel(band, TextLibraries);
            float width = ModsLook.Width(label);
            Place(label, x, width);
            x += width + 12f;
            TMP_Text number = ModsLook.Text(band, "Count", libraries.Count.ToString(), 18f, ModsLook.Muted, FontStyles.Normal, false);
            _librariesCount = number;
            // Room for up to three digits, as the search changes the count.
            float numberWidth = Mathf.Max(ModsLook.Width(number), 40f);
            Place(number, x, numberWidth);
            x += numberWidth + 14f;

            // Shown while folded.
            if (libraries.Any(NeedsAttention))
            {
                x += TagAt(band, "Attention", TextNeedsAttention, ModsLook.Warning, x) + 8f;
                _librariesTags.Add(band.Find("Attention").gameObject);
            }
            if (libraries.Any(e => e.Loaded && Updates.UpdateCheck.NewerRelease(e.Guid, e.Version) != null))
            {
                TagAt(band, "Update", TextUpdateTag, ModsLook.Accent, x);
                _librariesTags.Add(band.Find("Update").gameObject);
            }
            return row;
        }

        // A tag placed `left` from its parent's left edge, centred on the
        // parent's height (or on its lower half with `lower`). Returns its width.
        private static float TagAt(Transform parent, string name, string text, Color color, float left, bool lower = false)
        {
            float width = ModsLook.Tag(parent, name, text, color, 16f, 26f);
            var rect = (RectTransform)parent.Find(name);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, lower ? 0.27f : 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(left, 0f);
            return width;
        }

        private GameObject CreateListRow(ModCatalog.Entry entry)
        {
            GameObject row = EmptyRow("Mod " + entry.Name, RowHeight);
            RectTransform band = ModsLook.Rect(row.transform, "Band");
            ModsLook.Stretch(band, ListLeftMargin - 20f, 2f, 0f, 2f);
            Image bandImage = ModsLook.Shape(band.gameObject, ModsLook.Rounded, Color.white);
            Button button = band.gameObject.AddComponent<Button>();
            button.targetGraphic = bandImage;

            // The line on the left of the mod the details show.
            RectTransform bar = ModsLook.Rect(band, "Bar");
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(0f, 1f);
            bar.pivot = new Vector2(0f, 0.5f);
            bar.offsetMin = new Vector2(0f, 14f);
            bar.offsetMax = new Vector2(4f, -14f);
            ModsLook.Shape(bar.gameObject, ModsLook.Pill, ModsLook.Accent).raycastTarget = false;

            ModRowSelect select = band.gameObject.AddComponent<ModRowSelect>();
            select.Menu = this;
            select.Entry = entry;
            select.Button = button;
            select.Bar = bar.gameObject;
            select.SetShown(SameMod(entry, _selected));
            _listRows.Add(select);
            button.onClick.AddListener(() =>
            {
                Select(entry);
                // With the pad or the keyboard, pressing a mod moves on to its buttons.
                if (PadSupport.SubmitPressedThisFrame())
                {
                    FlushDetails();
                    Focus("Switch", "Tab" + _tab);
                }
            });

            ModIcon(band, entry, 52f, 16f);

            // From the right: the switch (only a picture: the one that switches
            // is in the details, so the pad stops once a row).
            float right = 16f;
            if (entry.PendingUninstall)
            {
                TMP_Text state = ModsLook.Text(band, "State", TextUninstall, 20f, ModsLook.Warning, FontStyles.Bold, false);
                FitRight(state, right);
                right += ModsLook.Width(state) + 12f;
            }
            else if (entry.CanSwitch)
            {
                RectTransform track = ModsLook.Switch(band, "Switch", entry.WantOn, 60f, 32f);
                track.anchorMin = track.anchorMax = new Vector2(1f, 0.5f);
                track.pivot = new Vector2(1f, 0.5f);
                track.anchoredPosition = new Vector2(-right, 0f);
                right += 60f + 12f;
            }

            bool dim = !entry.WantOn;
            TMP_Text name = ModsLook.Text(band, "Name", Escape(ShownName(entry)), 26f, dim ? ModsLook.Muted : ModsLook.Label, FontStyles.Bold, false);
            var nameRect = (RectTransform)name.transform;
            nameRect.anchorMin = new Vector2(0f, 0.5f);
            nameRect.offsetMin = new Vector2(84f, -2f);
            nameRect.offsetMax = new Vector2(-right, -6f);

            // Below the name: the tags first, so none is cut off, then the
            // version and author in what is left. Width is measured in the
            // list's own size, which does not change with the window's.
            float x = 84f;
            float limit = Mathf.Max(120f, ((RectTransform)Content).rect.width - (ListLeftMargin - 20f) - right);
            foreach ((string tagName, string text, Color color) in TagsOf(entry))
            {
                float width = TagAt(band, tagName, text, color, x, true);
                if (x + width > limit && x > 84f)
                {
                    // No room left: the details show every one of them.
                    GameObject cut = band.Find(tagName).gameObject;
                    cut.SetActive(false);
                    Destroy(cut);
                    break;
                }
                x += width + 6f;
            }
            string meta = string.IsNullOrEmpty(entry.Version) ? "" : "v" + Escape(entry.Version);
            if (!string.IsNullOrEmpty(entry.Authors))
            {
                meta += (meta.Length > 0 ? "   " : "") + Escape(entry.Authors);
            }
            if (meta.Length > 0)
            {
                TMP_Text sub = ModsLook.Text(band, "Meta", meta, 19f, ModsLook.Muted, FontStyles.Normal, false);
                var subRect = (RectTransform)sub.transform;
                subRect.anchorMax = new Vector2(1f, 0.5f);
                subRect.offsetMin = new Vector2(x + (x > 84f ? 4f : 0f), 4f);
                subRect.offsetMax = new Vector2(-right, 2f);
            }
            return row;
        }

        // A library goes by what follows the framework's name ("Inspector"),
        // in the list and in the details' title: in full, it was cut to
        // "Drag'n Wash ModFramework: Insp..." in the list and took three lines
        // in the details. The details give the whole name on a line of its own.
        private string ShownName(ModCatalog.Entry entry)
        {
            return IsLibraryLike(entry) && !entry.IsFramework && entry.Guid != null
                ? ModCatalog.ShortNameOf(_entries, entry.Guid)
                : entry.DisplayName;
        }

        // Every tag that applies to a mod, the most pressing first.
        private List<(string Name, string Text, Color Color)> TagsOf(ModCatalog.Entry entry)
        {
            var tags = new List<(string, string, Color)>();
            if (FailedToLoad(entry))
            {
                tags.Add(("NotLoaded", TextNotLoadedTag, ModsLook.Error));
            }
            if (ConflictsOf(entry).Count > 0)
            {
                tags.Add(("Conflict", TextConflictTag, ModsLook.Warning));
            }
            bool online = IsOnline(entry, out bool undeclared);
            if (online && undeclared)
            {
                tags.Add(("Online", TextOnlineTag, ModsLook.Warning));
            }
            if (entry.Loaded && Updates.UpdateCheck.NewerRelease(entry.Guid, entry.Version) != null)
            {
                tags.Add(("Update", TextUpdateTag, ModsLook.Accent));
            }
            if (NeedsRestart(entry))
            {
                tags.Add(("Restart", TextNeedsRestart, ModsLook.Muted));
            }
            if (online && !undeclared)
            {
                tags.Add(("Online", TextOnlineTag, ModsLook.Muted));
            }
            return tags;
        }

        // The mod's own icon, or its initials on a coloured square. Nothing
        // is drawn for it: only what the mod gave, or letters of its name.
        private static void ModIcon(RectTransform parent, ModCatalog.Entry entry, float side, float left)
        {
            RectTransform icon = ModsLook.Rect(parent, "Icon");
            icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
            icon.pivot = new Vector2(0f, 0.5f);
            icon.sizeDelta = new Vector2(side, side);
            icon.anchoredPosition = new Vector2(left, 0f);
            Texture2D texture = entry.Info?.Icon;
            if (texture != null)
            {
                RawImage image = icon.gameObject.AddComponent<RawImage>();
                image.texture = texture;
                image.raycastTarget = false;
                return;
            }
            string name = DropPublisher(ShortName(entry.DisplayName));
            ModsLook.Shape(icon.gameObject, ModsLook.Rounded, InitialsColors[StableHash(entry.Guid ?? name) % InitialsColors.Length], side * 0.25f).raycastTarget = false;
            TMP_Text letters = ModsLook.Text(icon, "Initials", Escape(Initials(name)), side * 0.4f, ModsLook.Inset, FontStyles.Bold, false);
            letters.alignment = TextAlignmentOptions.Center;
            letters.overflowMode = TextOverflowModes.Overflow;
        }

        // A leading "Drag'n Wash" (either apostrophe, any case) is the publisher's own
        // name, not the mod's: "Drag'n Wash Localization" is known by "Localization"
        // here, the same as a library already is by ShortName's colon. Kept whole when
        // nothing is left after it ("Drag'n Wash" alone is DW, not just a stray D).
        private static string DropPublisher(string name)
        {
            const string prefix1 = "drag'n wash";
            const string prefix2 = "drag’n wash";
            string lower = name.ToLowerInvariant();
            bool has = lower.StartsWith(prefix1, StringComparison.Ordinal) || lower.StartsWith(prefix2, StringComparison.Ordinal);
            if (!has || name.Length <= prefix1.Length)
            {
                return name;
            }
            char after = name[prefix1.Length];
            if (after != ' ' && after != ':' && after != '-')
            {
                return name;
            }
            string rest = name.Substring(prefix1.Length + 1).TrimStart(' ', ':', '-');
            return rest.Length > 0 ? rest : name;
        }

        // "Drag'n Wash ModFramework: Inspector" is known by "Inspector".
        private static string ShortName(string name)
        {
            name = name ?? "";
            int colon = name.LastIndexOf(": ", StringComparison.Ordinal);
            return colon >= 0 && colon + 2 < name.Length ? name.Substring(colon + 2) : name;
        }

        // Words that say what state a mod is in, not what it is: no initial.
        private static readonly string[] StateWords = { "experimental", "beta", "alpha", "preview", "wip" };

        // The first letter of each of the first two words, or the first
        // letter alone for one word (and for names in Japanese or Chinese).
        // What is in brackets and words like "experimental" are passed over:
        // "Inspector (experimental)" is I, not IE.
        private static string Initials(string name)
        {
            var kept = new System.Text.StringBuilder(name.Length);
            int depth = 0;
            foreach (char c in name)
            {
                if (c == '(' || c == '[')
                {
                    depth++;
                }
                else if ((c == ')' || c == ']') && depth > 0)
                {
                    depth--;
                }
                else if (depth == 0)
                {
                    kept.Append(c);
                }
            }
            string letters = "";
            foreach (string word in kept.ToString().Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (StateWords.Contains(word.ToLowerInvariant()))
                {
                    continue;
                }
                char first = word.FirstOrDefault(char.IsLetterOrDigit);
                if (first != default(char))
                {
                    letters += char.ToUpperInvariant(first);
                }
                if (letters.Length == 2)
                {
                    break;
                }
            }
            return letters.Length > 0 ? letters : "?";
        }

        // The same colour for a mod every time the game runs.
        private static int StableHash(string text)
        {
            int hash = 17;
            foreach (char c in text ?? "")
            {
                hash = unchecked(hash * 31 + c);
            }
            return hash & 0x7fffffff;
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

        // Marks the row of the mod the details now show, without building the
        // list again (that would take the pad's place away).
        private void MarkShownRow()
        {
            foreach (ModRowSelect row in _listRows)
            {
                if (row != null)
                {
                    row.SetShown(SameMod(row.Entry, _selected));
                }
            }
        }

        // Children are hidden at once and destroyed at the end of the frame,
        // so a search for what is on screen never finds the old ones.
        private static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        // Selects the button of that name under `root`, if one is showing.
        private static bool FocusIn(Transform root, string name)
        {
            if (root == null || EventSystem.current == null)
            {
                return false;
            }
            Selectable target = root.GetComponentsInChildren<Selectable>(false)
                .FirstOrDefault(s => s.name == name && s.IsInteractable());
            if (target == null)
            {
                return false;
            }
            EventSystem.current.SetSelectedGameObject(target.gameObject);
            return true;
        }
    }

    // Shows a mod's details as soon as its row is selected, by mouse or pad,
    // and marks the row of the mod the details show.
    internal sealed class ModRowSelect : MonoBehaviour, ISelectHandler
    {
        internal ModsMenu Menu;
        internal ModCatalog.Entry Entry;
        internal Button Button;
        internal GameObject Bar;

        public void OnSelect(BaseEventData eventData)
        {
            Menu?.Select(Entry);
        }

        internal void SetShown(bool shown)
        {
            if (Button != null)
            {
                ModsLook.Colors(Button, shown ? ModsLook.Card : ModsLook.Clear, ModsLook.Hover, ModsLook.Raised);
            }
            if (Bar != null)
            {
                Bar.SetActive(shown);
            }
        }
    }
}
