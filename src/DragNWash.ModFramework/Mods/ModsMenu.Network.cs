using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // What a mod does online, for players: the "Online" tag in the list, a line
    // in the details and the Internet page, which lists what the mod declared
    // (ModInfo.Network) and what the framework saw it connect to this session.
    // Experimental; see docs/NETWORK.md.
    internal sealed partial class ModsMenu
    {
        internal const string TextOnlineTag = "Online";
        internal const string TextUsesInternet = "Uses the internet:";
        internal const string TextUndeclaredOnline = "Went online without saying so:";
        internal const string TextInternetPage = "Internet";
        internal const string TextNotDeclared = "This mod does not say that it uses the internet.";
        internal const string TextWhatFor = "What for:";
        internal const string TextWhatIsSent = "What is sent:";
        internal const string TextTurnOff = "How to turn it off:";
        internal const string TextNotStated = "Not stated";
        internal const string TextSeenThisSession = "Seen this session";
        internal const string TextNoConnections = "No connections so far.";
        internal const string TextNotDeclaredByMod = "not declared by the mod";
        internal const string TextWatchingOff = "Connection watching is off (Mods → Drag'n Wash ModFramework → Settings → Watch connections).";
        internal const string TextWatchOnly = "The framework only watches: it blocks nothing, and a mod can get around it.";

        private static readonly Color OnlineColor = new Color(0.7f, 0.85f, 1f, 1f);

        private static bool IsOnline(ModCatalog.Entry entry, out bool undeclared)
        {
            undeclared = false;
            if (entry.Guid == null)
            {
                return false;
            }
            undeclared = NetworkWatch.HasUndeclared(entry.Guid);
            return undeclared || NetworkWatch.DeclaredBy(entry.Guid).Count > 0 || NetworkWatch.SeenBy(entry.Guid).Count > 0;
        }

        // The line in the details: a warning when the mod went online without
        // saying so, otherwise the hosts it declared.
        private static void AddNetworkNote(ModCatalog.Entry entry, List<(string Name, string Label, string Value, bool Warn)> notes, bool warningsOnly)
        {
            if (entry.Guid == null)
            {
                return;
            }
            List<string> undeclared = NetworkWatch.SeenBy(entry.Guid).Where(c => !c.Declared).Select(c => c.Host).ToList();
            if (undeclared.Count > 0)
            {
                if (warningsOnly)
                {
                    notes.Add(("NetUndeclared", TextUndeclaredOnline, string.Join(", ", undeclared), true));
                }
                return;
            }
            if (!warningsOnly)
            {
                List<string> hosts = NetworkWatch.DeclaredBy(entry.Guid).Select(u => u.Host).Distinct().ToList();
                if (hosts.Count > 0)
                {
                    notes.Add(("Net", TextUsesInternet, string.Join(", ", hosts), false));
                }
            }
        }

        private ModsScreenPage InternetPage(ModCatalog.Entry entry)
        {
            string guid = entry.Guid;
            return new ModsScreenPage { Guid = guid, Title = TextInternetPage, Build = panel => BuildInternetPage(panel, guid) };
        }

        // Every fixed phrase is its own label, so translation mods can translate it.
        private static void BuildInternetPage(RectTransform panel, string guid)
        {
            RectTransform content = ScrollArea(panel);
            float size = UiText.BodySize * 0.85f;

            IReadOnlyList<NetworkUse> uses = NetworkWatch.DeclaredBy(guid);
            if (uses.Count == 0)
            {
                Line(content, TextNotDeclared, size, FontStyles.Italic, Color.white, 0f);
            }
            foreach (NetworkUse use in uses)
            {
                Line(content, Escape(use.Host), size * 1.1f, FontStyles.Bold, OnlineColor, 0f);
                Pair(content, TextWhatFor, use.Purpose, size);
                Pair(content, TextWhatIsSent, use.Sends, size);
                Pair(content, TextTurnOff, use.TurnOff, size);
                Spacer(content, size * 0.6f);
            }

            Spacer(content, size * 0.4f);
            if (!NetworkWatch.Enabled)
            {
                Line(content, TextWatchingOff, size, FontStyles.Italic, Color.white, 0f);
            }
            else
            {
                Line(content, TextSeenThisSession, size, FontStyles.Bold, Color.white, 0f);
                IReadOnlyList<NetworkWatch.Connection> seen = NetworkWatch.SeenBy(guid);
                if (seen.Count == 0)
                {
                    Line(content, TextNoConnections, size, FontStyles.Normal, Color.white, 24f);
                }
                foreach (NetworkWatch.Connection c in seen)
                {
                    string text = $"{Escape(c.Host)}  ({c.Via}, ×{c.Count})";
                    Line(content, text, size, FontStyles.Normal, c.Declared ? Color.white : WarnColor, 24f);
                    if (!c.Declared)
                    {
                        Line(content, TextNotDeclaredByMod, size * 0.9f, FontStyles.Italic, WarnColor, 48f);
                    }
                }
            }
            Spacer(content, size * 0.6f);
            Line(content, TextWatchOnly, size * 0.85f, FontStyles.Italic, new Color(1f, 1f, 1f, 0.7f), 0f);
        }

        private static void Pair(RectTransform content, string label, string value, float size)
        {
            Line(content, label, size, FontStyles.Bold, Color.white, 24f);
            Line(content, string.IsNullOrEmpty(value) ? TextNotStated : Escape(value), size, string.IsNullOrEmpty(value) ? FontStyles.Italic : FontStyles.Normal, Color.white, 48f);
        }

        // A scrolling column inside the page, so a mod with many hosts still fits.
        private static RectTransform ScrollArea(RectTransform panel)
        {
            var viewport = new GameObject("InternetViewport", typeof(RectTransform), typeof(RectMask2D));
            var viewRect = (RectTransform)viewport.transform;
            viewRect.SetParent(panel, false);
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = Vector2.zero;
            viewRect.offsetMax = Vector2.zero;
            // Something to catch the wheel over empty space.
            Image catcher = viewport.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);

            var content = new GameObject("InternetContent", typeof(RectTransform));
            var contentRect = (RectTransform)content.transform;
            contentRect.SetParent(viewRect, false);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 2f;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.viewport = viewRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return contentRect;
        }

        private static void Line(RectTransform content, string text, float size, FontStyles style, Color color, float indent)
        {
            var row = new GameObject("Line", typeof(RectTransform));
            ((RectTransform)row.transform).SetParent(content, false);
            HorizontalLayoutGroup pad = row.AddComponent<HorizontalLayoutGroup>();
            pad.padding = new RectOffset((int)indent, 0, 0, 0);
            pad.childControlHeight = true;
            pad.childControlWidth = true;
            pad.childForceExpandWidth = true;

            TMP_Text label = UiText.Create(row.transform, "Text", text, size);
            label.enableAutoSizing = false;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
        }

        private static void Spacer(RectTransform content, float height)
        {
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            ((RectTransform)spacer.transform).SetParent(content, false);
            spacer.GetComponent<LayoutElement>().minHeight = height;
        }
    }
}
