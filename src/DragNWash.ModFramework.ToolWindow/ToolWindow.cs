using System;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    /// <summary>
    /// Tool window library: one shared in-game window for developer and debug
    /// tools, opened with F1 by default, where each mod adds its own tabs.
    /// </summary>
    /// <remarks>
    /// The window is drawn with Unity's IMGUI. The library takes care of what
    /// makes that hard in this game: it frees the cursor the game keeps locked
    /// when a gamepad is present, stops clicks and drags on the window from
    /// reaching the game behind it, turns gamepad and Steam Deck trackpad presses
    /// into clicks, lets the sticks scroll, and draws with a font that has
    /// Japanese and Chinese glyphs where the system has one.
    /// <para>
    /// Depend on it with
    /// <c>[BepInDependency(ToolWindow.Guid, BepInDependency.DependencyFlags.HardDependency)]</c>.
    /// </para>
    /// <para>
    /// On Direct3D 12, rasterizing a character the window font has not drawn yet
    /// uploads a texture, and an upload while the game is presenting a frame can
    /// crash the game (Unity UUM-140564). Prepare every non-ASCII character a tab
    /// shows with <see cref="PrepareCharacters"/> from Awake or Update, never
    /// from the draw callback.
    /// </para>
    /// </remarks>
    public static class ToolWindow
    {
        /// <summary>BepInEx GUID of the tool window library.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.toolwindow";

        /// <summary>Library version. Keep in sync with the csproj.</summary>
        public const string Version = "1.2.0";

        /// <summary>Height of one row of controls, in pixels.</summary>
        public const float RowHeight = 30f;

        /// <summary>Space between the window edge and its content, in pixels.</summary>
        public const float Padding = 16f;

        /// <summary>Header band colour.</summary>
        public static readonly Color PanelColor = new Color(0.09f, 0.11f, 0.15f);

        /// <summary>Background of a tab's content area.</summary>
        public static readonly Color InsetColor = new Color(0.055f, 0.07f, 0.10f);

        /// <summary>Accent colour (selected buttons, the header mark).</summary>
        public static readonly Color AccentColor = new Color(0.32f, 0.78f, 0.72f);

        /// <summary>Colour of secondary text.</summary>
        public static readonly Color MutedColor = new Color(0.60f, 0.66f, 0.73f);

        /// <summary>Errors in the console.</summary>
        public static readonly Color ErrorColor = new Color(0.96f, 0.45f, 0.40f);

        /// <summary>Warnings in the console.</summary>
        public static readonly Color WarningColor = new Color(0.93f, 0.75f, 0.30f);

        internal static readonly List<ToolTab> Tabs = new List<ToolTab>();
        private static int _nextSerial;

        /// <summary>True when the window can be shown on this game build.</summary>
        public static bool IsAvailable { get; internal set; }

        /// <summary>True while the window is open.</summary>
        public static bool IsOpen => ToolWindowPlugin.Instance != null && ToolWindowPlugin.Instance.ShowWindow;

        /// <summary>Raised when the window opens (true) or closes (false).</summary>
        public static event Action<bool> OpenChanged;

        /// <summary>The styles controls in a tab should use. Only valid inside a draw callback.</summary>
        public static ToolWindowStyles Styles { get; } = new ToolWindowStyles();

        /// <summary>The font the window draws with, or null for the IMGUI skin's font.</summary>
        public static Font Font => MenuFont.Font;

        /// <summary>Font size used by every style.</summary>
        public static int FontSize => MenuFont.Size;

        /// <summary>
        /// Adds a tab. <paramref name="draw"/> is called from OnGUI with the tab's
        /// content area in window coordinates. Tabs are ordered by
        /// <paramref name="order"/>, then by when they were added. Dispose the
        /// returned object to remove the tab.
        /// </summary>
        /// <param name="owner">GUID of the mod adding the tab, shown when it fails.</param>
        /// <param name="title">Tab button text. Keep it short and ASCII (see <see cref="PrepareCharacters"/>).</param>
        /// <param name="draw">Draws the tab. An exception is logged with the owner and the tab shows it instead.</param>
        /// <param name="order">Lower comes first.</param>
        public static IDisposable AddTab(string owner, string title, Action<Rect> draw, int order = 0)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(title) || draw == null)
            {
                throw new ArgumentException("A tab needs an owner, a title and a draw callback.");
            }
            var tab = new ToolTab { Owner = owner, Title = title, Draw = draw, Order = order, Serial = _nextSerial++ };
            lock (Tabs)
            {
                Tabs.Add(tab);
                Tabs.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Serial.CompareTo(b.Serial));
            }
            return new Removal(tab);
        }

        /// <summary>
        /// Adds a command to the Console tab (experimental, Tool window 1.1).
        /// <paramref name="run"/> gets the words typed after the name and returns
        /// what to print, one line per '\n'. An exception is printed in red with
        /// the owner and stops nothing else. When another mod already registered
        /// the same name, this one is reachable as <c>owner:name</c> only. Dispose
        /// the returned object to remove the command.
        /// </summary>
        /// <param name="owner">GUID of the mod adding the command.</param>
        /// <param name="name">One lower-case word, e.g. "tl".</param>
        /// <param name="description">One line for <c>help</c>.</param>
        /// <param name="run">Runs the command.</param>
        public static IDisposable AddCommand(string owner, string name, string description, Func<string[], string> run)
        {
            return AddCommand(owner, name, description, run, null);
        }

        /// <summary>
        /// As <see cref="AddCommand(string, string, string, Func{string[], string})"/>, with
        /// completions: <paramref name="complete"/> gets the words typed after the
        /// name so far, the last one possibly partial (or "" right after a space),
        /// and returns what could stand there. The console shows them as the
        /// person types and fills them in on Tab.
        /// </summary>
        public static IDisposable AddCommand(string owner, string name, string description, Func<string[], string> run, Func<string[], IEnumerable<string>> complete)
        {
            ConsoleCommand command = ConsoleCommands.Register(owner, name, description, run, complete);
            return new CommandRemoval(command);
        }

        private sealed class CommandRemoval : IDisposable
        {
            private ConsoleCommand _command;
            public CommandRemoval(ConsoleCommand command) { _command = command; }
            public void Dispose()
            {
                if (_command != null)
                {
                    ConsoleCommands.Unregister(_command);
                    _command = null;
                }
            }
        }

        /// <summary>Opens the window, on the tab with this title when one is given.</summary>
        public static void Open(string tabTitle = null)
        {
            ToolWindowPlugin.Instance?.SetOpen(true, tabTitle);
        }

        /// <summary>
        /// Adds an overlay: <paramref name="draw"/> is called from OnGUI while the
        /// window is open, before the window itself, with the window's rectangle,
        /// in screen coordinates (top-left origin). For outlines, gizmos and
        /// pick modes drawn over the game. An exception is logged with the
        /// owner and the overlay is switched off. Dispose the handle to remove it.
        /// </summary>
        public static IDisposable AddOverlay(string owner, Action<Rect> draw)
        {
            if (string.IsNullOrEmpty(owner) || draw == null)
            {
                throw new ArgumentException("An overlay needs an owner and a draw callback.");
            }
            var overlay = new Overlay { Owner = owner, Draw = draw };
            lock (Overlays)
            {
                Overlays.Add(overlay);
            }
            return new OverlayRemoval(overlay);
        }

        /// <summary>
        /// Holds the game's input (its action maps) while <paramref name="block"/>
        /// is true for <paramref name="owner"/>, on top of the window's own
        /// blocking while the pointer is over it. For a pick mode or a drag in
        /// the game view that must not move the player. Cleared with false, and
        /// when the owner's mod is reloaded.
        /// </summary>
        public static void BlockGameInput(string owner, bool block)
        {
            if (string.IsNullOrEmpty(owner))
            {
                return;
            }
            lock (InputHolders)
            {
                if (block) InputHolders.Add(owner); else InputHolders.Remove(owner);
            }
        }

        /// <summary>
        /// The text as the window can draw it now: characters the window font has
        /// not rasterised yet come out as '?' (and are prepared for later frames
        /// where that is safe). For text a tab did not know in advance, such as
        /// object names.
        /// </summary>
        public static string Drawable(string text)
        {
            return ConsoleTab.Drawable(text ?? "");
        }

        internal static readonly List<Overlay> Overlays = new List<Overlay>();
        private static readonly HashSet<string> InputHolders = new HashSet<string>(StringComparer.Ordinal);

        internal static bool InputBlockRequested
        {
            get { lock (InputHolders) { return InputHolders.Count > 0; } }
        }

        internal static void DrawOverlays(Rect window)
        {
            Overlay[] all;
            lock (Overlays)
            {
                all = Overlays.ToArray();
            }
            foreach (Overlay o in all)
            {
                if (o.Failed) continue;
                try
                {
                    o.Draw(window);
                }
                catch (Exception ex) when (!(ex is ExitGUIException))
                {
                    o.Failed = true;
                    ToolWindowPlugin.Log.LogError($"The overlay of {o.Owner} threw and was turned off: {ex}");
                }
            }
        }

        internal sealed class Overlay
        {
            public string Owner;
            public Action<Rect> Draw;
            public bool Failed;
        }

        private sealed class OverlayRemoval : IDisposable
        {
            private Overlay _overlay;
            public OverlayRemoval(Overlay overlay) { _overlay = overlay; }
            public void Dispose()
            {
                if (_overlay == null) return;
                lock (Overlays) { Overlays.Remove(_overlay); }
                _overlay = null;
            }
        }

        /// <summary>Closes the window.</summary>
        public static void Close()
        {
            ToolWindowPlugin.Instance?.SetOpen(false, null);
        }

        /// <summary>
        /// Shows a one-line message in the window's footer until the tab changes:
        /// <see cref="ShowNotice(string, NoticeKind, float)"/> with
        /// <see cref="NoticeKind.Info"/> and no time limit. An empty message clears
        /// the notice showing.
        /// </summary>
        public static void ShowNotice(string message)
        {
            ShowNotice(message, NoticeKind.Info, 0f);
        }

        /// <summary>
        /// Shows a message in the notice strip of the window's footer, above the
        /// hint line: one line with a colour bar for its kind, cut with "..." when
        /// it is too long (the whole text shows while the pointer is on it; a
        /// click dismisses it).
        /// <para>
        /// With <paramref name="seconds"/> above zero the notice clears itself
        /// that long after it first shows, and a notice that arrives meanwhile
        /// waits its turn ("1 more"); an <see cref="NoticeKind.Error"/> goes
        /// first. Without, it stays until the tab changes or the next notice
        /// replaces it - except an Error, which the next notices wait behind.
        /// The same message again starts its time over instead of queueing a
        /// copy. An empty message clears the notice showing.
        /// </para>
        /// </summary>
        /// <param name="message">One line, ASCII (see <see cref="PrepareCharacters"/>).</param>
        /// <param name="kind">Info (accent bar), Warning (yellow) or Error (red).</param>
        /// <param name="seconds">How long it shows; 0 for until the tab changes.</param>
        public static void ShowNotice(string message, NoticeKind kind, float seconds = 0f)
        {
            WindowFooter.Show(message, kind, seconds);
        }

        /// <summary>
        /// Marks the tab as busy for this draw. Call it from the tab's draw
        /// callback on every draw while it works through something spread over
        /// several frames (a coroutine that does one file a frame, say): the
        /// body is dimmed, a small panel in its middle shows
        /// <paramref name="what"/>, <paramref name="detail"/> and a spinner, the
        /// hint line repeats them, and the tab's controls see no input. The
        /// header, the tab buttons and the close button keep working, and the
        /// tab is back the frame after the calls stop.
        /// </summary>
        /// <param name="what">What runs, e.g. "Reloading files...".</param>
        /// <param name="detail">How far it is, e.g. "MenuButtons0002  (2 of 5)"; may be null.</param>
        public static void Busy(string what, string detail = null)
        {
            ToolWindowPlugin.Instance?.MarkBusy(what, detail);
        }

        // "..." where the window font has no ellipsis; set at startup.
        internal static string Ellipsis = "...";

        // The text as it fits in width with the style: whole, or cut at a
        // character boundary with an ellipsis. Returns the same string when it fits.
        internal static string ElideText(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text) || style == null || style.CalcSize(new GUIContent(text)).x <= width)
            {
                return text ?? "";
            }
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (style.CalcSize(new GUIContent(text.Substring(0, mid).TrimEnd() + Ellipsis)).x <= width) lo = mid;
                else hi = mid - 1;
            }
            // A surrogate pair is never split.
            if (lo > 0 && char.IsHighSurrogate(text[lo - 1])) lo--;
            return text.Substring(0, lo).TrimEnd() + Ellipsis;
        }

        /// <summary>
        /// Rasterizes these characters into the window font now, so drawing them
        /// later uploads nothing. Call from Awake or Update. Called from a draw
        /// callback, the characters are prepared on the next Update instead.
        /// </summary>
        public static void PrepareCharacters(string characters)
        {
            MenuFont.Prepare(characters);
        }

        /// <summary>
        /// True when every character of <paramref name="text"/> has a glyph in the
        /// window font. Characters it cannot draw are shown as "?".
        /// </summary>
        public static bool CanDraw(string text)
        {
            return MenuText.CanDraw(MenuFont.Font, MenuFont.Size, text);
        }

        /// <summary>Fills a rectangle with a colour.</summary>
        public static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        /// <summary>Draws the accent-coloured underline under a text field.</summary>
        public static void Underline(Rect field)
        {
            Fill(new Rect(field.x, field.yMax - 2, field.width, 2), AccentColor);
        }

        /// <summary>
        /// Draws a text field with the accent underline, showing
        /// <paramref name="placeholder"/> in a muted label while it is empty.
        /// Returns the field's new value.
        /// </summary>
        public static string FilterField(Rect rect, string value, string placeholder, ToolWindowStyles s)
        {
            string next = GUI.TextField(rect, value ?? "", s.TextField);
            Underline(rect);
            if (string.IsNullOrEmpty(next))
            {
                GUI.Label(new Rect(rect.x + 6, rect.y, rect.width - 6, rect.height), placeholder, s.MutedLabel);
            }
            return next;
        }

        /// <summary>
        /// Call just before <c>GUI.BeginScrollView</c> for a scrolling area: moves
        /// it by the gamepad stick or d-pad while the pointer is over it. Returns
        /// true when it moved.
        /// </summary>
        public static bool ApplyScroll(Rect view, ref Vector2 scroll)
        {
            return VirtualClick.ApplyScroll(view, ref scroll);
        }

        // On a mod's reload (ModReload.Unloading): its tabs, its commands and
        // its OpenChanged handlers go, so the new build adds them back cleanly.
        internal static void RemoveOwned(string owner, System.Reflection.Assembly assembly)
        {
            lock (Tabs)
            {
                Tabs.RemoveAll(t => t.Owner == owner);
            }
            lock (Overlays)
            {
                Overlays.RemoveAll(o => o.Owner == owner);
            }
            BlockGameInput(owner, false);
            ConsoleCommands.UnregisterOwned(owner);
            OpenChanged = (Action<bool>)ModReload.Prune(OpenChanged, assembly);
        }

        internal static void RaiseOpenChanged(bool open)
        {
            if (OpenChanged == null)
            {
                return;
            }
            foreach (Action<bool> handler in OpenChanged.GetInvocationList())
            {
                try
                {
                    handler(open);
                }
                catch (Exception ex)
                {
                    ToolWindowPlugin.Log.LogError($"A tool window OpenChanged handler threw: {ex}");
                }
            }
        }

        private sealed class Removal : IDisposable
        {
            private ToolTab _tab;

            internal Removal(ToolTab tab)
            {
                _tab = tab;
            }

            public void Dispose()
            {
                if (_tab == null)
                {
                    return;
                }
                lock (Tabs)
                {
                    Tabs.Remove(_tab);
                }
                _tab = null;
            }
        }
    }

    /// <summary>What a notice in the window's footer is, which sets its colour bar.</summary>
    public enum NoticeKind
    {
        /// <summary>Something done, or worth knowing (accent colour).</summary>
        Info,

        /// <summary>Done, but with a problem worth a look (yellow).</summary>
        Warning,

        /// <summary>Something failed (red).</summary>
        Error,
    }

    internal sealed class ToolTab
    {
        public string Owner;
        public string Title;
        public Action<Rect> Draw;
        public int Order;
        public int Serial;
        public string Failure;
    }

    /// <summary>
    /// The window's styles. They exist from the window's first draw on; use them
    /// only inside a draw callback.
    /// </summary>
    public sealed class ToolWindowStyles
    {
        internal ToolWindowStyles() { }

        /// <summary>Normal one-line text.</summary>
        public GUIStyle Label { get; internal set; }

        /// <summary>Secondary one-line text.</summary>
        public GUIStyle MutedLabel { get; internal set; }

        /// <summary>Secondary text that wraps.</summary>
        public GUIStyle WrappedLabel { get; internal set; }

        /// <summary>Wrapping text for logs, with a little padding.</summary>
        public GUIStyle LogLabel { get; internal set; }

        /// <summary>A button.</summary>
        public GUIStyle Button { get; internal set; }

        /// <summary>A selected or active button.</summary>
        public GUIStyle SelectedButton { get; internal set; }

        /// <summary>
        /// A text field. Use this rather than GUI.skin.textField, whose built-in
        /// textures are uploaded on first draw (see <see cref="ToolWindow.PrepareCharacters"/>).
        /// </summary>
        public GUIStyle TextField { get; internal set; }

        // The footer's own: small secondary text, wrapping text in the label
        // colour, and one line in the accent colour.
        internal GUIStyle SmallMuted { get; set; }
        internal GUIStyle WrappedText { get; set; }
        internal GUIStyle AccentLabel { get; set; }
    }
}
