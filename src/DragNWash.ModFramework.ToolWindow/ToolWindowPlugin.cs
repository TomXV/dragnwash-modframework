using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DragNWash.ModFramework.ToolWindow
{
    // The tool window library's BepInEx entry point, and the window itself.
    [BepInPlugin(ToolWindow.Guid, "DragNWash.ModFramework.ToolWindow", ToolWindow.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    internal sealed class ToolWindowPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ToolWindowPlugin Instance;

        private const float HeaderHeight = 48f;
        private const float StripHeight = 34f;
        private const float GripSize = 22f;
        private const float CloseMargin = 58f;

        private ConfigEntry<KeyboardShortcut> _toggleKey;
        private ConfigEntry<string> _fontMode;
        private ConfigEntry<string> _consoleShow;
        private ConfigEntry<string> _consoleLevels;
        private ConfigEntry<bool> _consoleTrace;
        private bool _savingConsole;

        internal bool ShowWindow;
        // The tab being drawn, for ToolWindow.Busy.
        private ToolTab _drawingTab;
        // Until when the "developer tools are off" word shows after the key.
        private float _toastUntil;

        private bool _wasOpen;
        private static readonly Rect DefaultRect = new Rect(24, 24, 780, 580);
        private Rect _windowRect = DefaultRect;
        // Where the window was left, and on which tab; it opens there again.
        private ConfigEntry<string> _rectSetting;
        private ConfigEntry<string> _lastTab;
        // The remembered tab, until it is shown or the player picks another:
        // a mod may add it a little after the window first opens.
        private string _wantedTab;
        private Rect? _requestedRect;
        // IMGUI shows a style's hover colour only with a hover background, so
        // the pointer is tested by hand and the brighter style picked.
        private GUIStyle _linkStyle, _linkHoverStyle;
        private GUIStyle _tabStyle, _tabSelectedStyle, _menuItemStyle, _menuItemHoverStyle;
        // The tabs that did not fit on the strip, under More, and its menu.
        private readonly List<ToolTab> _hiddenTabs = new List<ToolTab>();
        private bool _moreOpen;
        private Rect _moreButton, _moreMenu;
        private ToolTab _current;
        private Texture2D _background;
        private GUIStyle _windowStyle;
        private bool _stylesReady;
        private bool _pointerGrabbed;
        private bool _inputBlockingBroken;
        private bool _resizing;
        private int _resizeControl;
        private Vector2 _resizeStartMouse;
        private Vector2 _resizeStartSize;
        private Vector2? _requestedSize;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            ModFramework.Register(new ModInfo
            {
                Guid = ToolWindow.Guid,
                DisplayName = "Drag'n Wash ModFramework: Tool window",
                Description = "One shared in-game window (F1) where mods add tabs for their developer and debug tools, usable with mouse, gamepad and on the Steam Deck.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
            });

            _toggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F1),
                new ConfigDescription("Shows and hides the tool window. Only while Developer tools are on (Options > Mods > Drag'n Wash ModFramework).",
                    null, new SettingMeta { DisplayName = "Open / close key", Order = -10 }));
            // A developer tool: it closes when the switch goes off, and SetOpen
            // and the key refuse to open it while the switch is off.
            DeveloperTools.Changed += () =>
            {
                if (!DeveloperTools.Enabled && ShowWindow)
                {
                    ShowWindow = false;
                }
            };
            _rectSetting = Config.Bind("Window", "Rect", "24,24,780,580",
                new ConfigDescription("Where the window was left and its size, in pixels: x, y, width, height. Saved when it closes; Reset window in its footer puts it back.",
                    null, new HiddenSetting()));
            _lastTab = Config.Bind("Window", "LastTab", "",
                new ConfigDescription("The tab the window was left on; it opens on it again.", null, new HiddenSetting()));
            _windowRect = ParseRect(_rectSetting.Value) ?? DefaultRect;
            _wantedTab = string.IsNullOrEmpty(_lastTab.Value) ? null : _lastTab.Value;
            _fontMode = Config.Bind("General", "FontMode", "auto",
                new ConfigDescription("Font for the tool window: auto (an OS font with Japanese and Chinese, else the bundled one), builtin (Unity's built-in font, ASCII only), skin (the IMGUI skin's font).",
                    new AcceptableValueList<string>("auto", "builtin", "skin"), new SettingMeta { DisplayName = "Font", Advanced = true, RequiresRestart = true }));

            // Built now rather than when the window first opens: Texture2D.Apply
            // uploads to the GPU, and doing that on the frame the window opens is
            // the Direct3D 12 crash (UUM-140564).
            _background = new Texture2D(1, 1);
            _background.SetPixel(0, 0, new Color(0.06f, 0.06f, 0.08f, 0.95f));
            _background.Apply();
            MenuFont.Create(_fontMode.Value);
            // Cut text ends in an ellipsis, and More has its triangle, where the font has them.
            MenuFont.Prepare("\u2026\u25BE");
            ToolWindow.Ellipsis = MenuText.CanDraw(MenuFont.Font, MenuFont.Size, "\u2026") ? "\u2026" : "...";
            ToolWindow.DownArrow = MenuText.CanDraw(MenuFont.Font, MenuFont.Size, "\u25BE") ? "\u25BE" : "v";

            var harmony = new Harmony(ToolWindow.Guid);
            Install("Pad and trackpad clicks", () => VirtualClick.Install(harmony));
            Install("Characters the font lacks", () => MenuText.Install(harmony));
            GameHooks.Require(ToolWindow.Guid, "Free cursor while open", AccessTools.TypeByName("GameStateManager") != null, "GameStateManager");
            Install("Free cursor while open", () => CursorUnlock.Install(harmony));
            ToolWindow.IsAvailable = true;

            Install("Console", SetUpConsole);
            ModReload.Unloading += ToolWindow.RemoveOwned;
        }

        // The console: every BepInEx log line with its level, the player's
        // choice of what to show (kept in the config), and commands.
        private void SetUpConsole()
        {
            _consoleShow = Config.Bind("Console", "Show", "Error,Warning,Message,Info",
                new ConfigDescription("Levels the Console tab shows at all, comma separated: Fatal, Error, Warning, Message, Info, Debug. Everything is still written to BepInEx/LogOutput.log.",
                    null, new SettingMeta { DisplayName = "Levels shown" }, new SectionMeta { DisplayName = "Console tab", Description = "Which log lines the Console tab shows; also changeable from the tab.", Order = 10 }));
            _consoleLevels = Config.Bind("Console", "Levels", "unity:Warning, default:Info",
                "The least severe level shown per log source, comma separated, as source:level. 'unity' is Unity's own log, 'default' every source without its own entry, anything else a source name as the log prints it (e.g. DragNWash.ModFramework.Assets:Debug).");
            _consoleTrace = Config.Bind("Console", "TraceInput", false,
                new ConfigDescription("Writes every key the Console tab sees, and what it did with it, to the log as DragNWash.ConsoleTrace. For debugging the console's input.",
                    null, new SettingMeta { DisplayName = "Trace input", Advanced = true }));
            ConsoleTab.Trace = _consoleTrace.Value;
            _consoleTrace.SettingChanged += (s, e) => ConsoleTab.Trace = _consoleTrace.Value;
            ApplyConsoleConfig();
            _consoleShow.SettingChanged += (s, e) => { if (!_savingConsole) ApplyConsoleConfig(); };
            _consoleLevels.SettingChanged += (s, e) => { if (!_savingConsole) ApplyConsoleConfig(); };
            ConsoleLog.Changed += SaveConsoleConfig;

            BepInEx.Logging.Logger.Listeners.Add(new ConsoleLog.Listener());
            ConsoleCommands.RegisterBuiltIns();
            ConsoleTab.Install();
        }

        private void ApplyConsoleConfig()
        {
            LogLevel shown = LogLevel.None;
            foreach (string part in (_consoleShow.Value ?? "").Split(','))
            {
                if (ConsoleLog.TryParseLevel(part, out LogLevel level))
                {
                    shown |= level;
                    if (level == LogLevel.Error) shown |= LogLevel.Fatal;
                }
            }
            _savingConsole = true;
            try
            {
                ConsoleLog.Shown = shown;
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string part in (_consoleLevels.Value ?? "").Split(','))
                {
                    string[] kv = part.Split(':');
                    if (kv.Length != 2 || !ConsoleLog.TryParseLevel(kv[1], out LogLevel minimum))
                    {
                        continue;
                    }
                    string source = kv[0].Trim();
                    if (source.Equals("unity", StringComparison.OrdinalIgnoreCase)) ConsoleLog.UnityMinimum = minimum;
                    else if (source.Equals("default", StringComparison.OrdinalIgnoreCase)) ConsoleLog.DefaultMinimum = minimum;
                    else { ConsoleLog.SetMinimum(source, minimum); keep.Add(source); }
                }
                foreach (KeyValuePair<string, LogLevel> kv in ConsoleLog.Minimums)
                {
                    if (!keep.Contains(kv.Key)) ConsoleLog.SetMinimum(kv.Key, null);
                }
            }
            finally
            {
                _savingConsole = false;
            }
        }

        private void SaveConsoleConfig()
        {
            if (_savingConsole)
            {
                return;
            }
            _savingConsole = true;
            try
            {
                var show = new List<string>();
                foreach (LogLevel l in new[] { LogLevel.Fatal, LogLevel.Error, LogLevel.Warning, LogLevel.Message, LogLevel.Info, LogLevel.Debug })
                {
                    if ((ConsoleLog.Shown & l) != 0 && l != LogLevel.Fatal) show.Add(l.ToString());
                }
                _consoleShow.Value = string.Join(",", show);
                var levels = new List<string> { "unity:" + ConsoleLog.UnityMinimum, "default:" + ConsoleLog.DefaultMinimum };
                foreach (KeyValuePair<string, LogLevel> kv in ConsoleLog.Minimums)
                {
                    levels.Add(kv.Key + ":" + kv.Value);
                }
                _consoleLevels.Value = string.Join(", ", levels);
            }
            finally
            {
                _savingConsole = false;
            }
        }

        private static void Install(string feature, Action install)
        {
            try
            {
                install();
            }
            catch (Exception ex)
            {
                GameHooks.Require(ToolWindow.Guid, feature, false, ex.Message);
            }
        }

        internal void SetOpen(bool open, string tabTitle)
        {
            if (open && !ToolsOn())
            {
                return;
            }
            ShowWindow = open;
            if (open && tabTitle != null)
            {
                lock (ToolWindow.Tabs)
                {
                    ToolTab tab = ToolWindow.Tabs.FirstOrDefault(t => t.Title == tabTitle);
                    if (tab != null)
                    {
                        Select(tab);
                        _wantedTab = null;
                    }
                }
            }
        }

        private bool _saidToolsOff;

        // False while developer tools are off, saying why once per switch-off.
        private bool ToolsOn()
        {
            if (DeveloperTools.Enabled)
            {
                _saidToolsOff = false;
                return true;
            }
            if (!_saidToolsOff)
            {
                _saidToolsOff = true;
                Log.LogMessage("The Tool window stays closed: developer tools are off. Turn them on in Options > Mods > Drag'n Wash ModFramework > Developer tools.");
            }
            return false;
        }

        private void Select(ToolTab tab)
        {
            if (!ReferenceEquals(tab, _current))
            {
                _current = tab;
                WindowFooter.Clear();
                InlineConfirm.Cancel();
            }
        }

        private void Update()
        {
            MenuFont.FlushQueued();
            ConsoleTab.Tick();

            if (_toggleKey.Value.IsDown())
            {
                if (ShowWindow || ToolsOn())
                {
                    ShowWindow = !ShowWindow;
                }
                else
                {
                    // Not nothing: the key was pressed, and the player sees why
                    // no window came.
                    _toastUntil = Time.realtimeSinceStartup + 6f;
                }
            }
            // The window also closes from its own X button, so follow the state
            // here rather than only on the key.
            if (ShowWindow != _wasOpen)
            {
                _wasOpen = ShowWindow;
                if (ShowWindow)
                {
                    CursorUnlock.Hold(this);
                }
                else
                {
                    CursorUnlock.Release();
                    VirtualClick.Cancel();
                    InlineConfirm.Cancel();
                    Remember();
                }
                ToolWindow.RaiseOpenChanged(ShowWindow);
            }

            if (ShowWindow)
            {
                CursorUnlock.Tick();
                VirtualClick.Poll(_windowRect);
                if (VirtualClick.UpdateDrag(ref _windowRect, HeaderHeight, CloseMargin, GripSize))
                {
                    _windowRect = Clamp(_windowRect, Screen.width, Screen.height);
                }
            }

            // Last, and swallowing its own failures, so a problem here never
            // stops the rest of Update.
            if (!_inputBlockingBroken)
            {
                try
                {
                    UpdateInputBlocking();
                }
                catch (Exception ex)
                {
                    _inputBlockingBroken = true;
                    InputBlocker.SetBlocking(false);
                    Log.LogWarning($"Input blocking disabled: {ex.Message}");
                }
            }
        }

        // Suspend the game's input while the pointer works the window. A held
        // button keeps the block after the pointer leaves, so dragging the window
        // past its edge does not hand the drag to the game mid-gesture. The
        // pointer is read through the Input System: this game has legacy input
        // switched off, so every UnityEngine.Input read throws.
        private void UpdateInputBlocking()
        {
            Mouse mouse = Mouse.current;
            if (!ShowWindow || mouse == null)
            {
                _pointerGrabbed = false;
                InputBlocker.SetBlocking(false);
                return;
            }
            Vector2 position = mouse.position.ReadValue();
            // The Input System measures from the bottom left, IMGUI from the top.
            bool over = _windowRect.Contains(new Vector2(position.x, Screen.height - position.y));
            bool held = mouse.leftButton.isPressed || mouse.rightButton.isPressed;
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            {
                _pointerGrabbed = over;
            }
            else if (!held)
            {
                _pointerGrabbed = false;
            }
            // In pick mode the click on the game selects an object and must not move the player.
            InputBlocker.SetBlocking(over || _pointerGrabbed || ToolWindow.InputBlockRequested);
        }

        // Kept in the config, so the next start opens the window as it was left.
        private void Remember()
        {
            if (_rectSetting == null)
            {
                return;
            }
            string rect = string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0},{2:0},{3:0}", _windowRect.x, _windowRect.y, _windowRect.width, _windowRect.height);
            if (_rectSetting.Value != rect)
            {
                _rectSetting.Value = rect;
            }
            if (_current != null && _lastTab.Value != _current.Title)
            {
                _lastTab.Value = _current.Title;
            }
        }

        private static Rect? ParseRect(string text)
        {
            string[] parts = (text ?? "").Split(',');
            if (parts.Length != 4)
            {
                return null;
            }
            var v = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || float.IsNaN(v[i]) || float.IsInfinity(v[i]))
                {
                    return null;
                }
            }
            return v[2] > 0 && v[3] > 0 ? new Rect(v[0], v[1], v[2], v[3]) : (Rect?)null;
        }

        // Hides a setting from the Mods screen, as BepInEx.ConfigurationManager
        // tags do: the window's place is kept, not chosen there.
        private sealed class HiddenSetting
        {
            public bool Browsable = false;
        }

        private void OnApplicationQuit()
        {
            if (ShowWindow)
            {
                Remember();
            }
        }

        private void OnDestroy()
        {
            // Leaving the game's input suspended would soft-lock it.
            InputBlocker.SetBlocking(false);
            CursorUnlock.Release();
        }

        internal static Rect Clamp(Rect rect, float screenWidth, float screenHeight)
        {
            float width = Mathf.Max(1, screenWidth);
            float height = Mathf.Max(1, screenHeight);
            rect.width = Mathf.Clamp(rect.width, Mathf.Min(420, width), width);
            // Tall enough for the tab strip, a body of 80, a notice and the
            // hint line; shorter and the body would run into the footer.
            rect.height = Mathf.Clamp(rect.height, Mathf.Min(400, height), height);
            rect.x = Mathf.Clamp(rect.x, 0, width - rect.width);
            rect.y = Mathf.Clamp(rect.y, 0, height - rect.height);
            return rect;
        }

        private GUIStyle Style(GUIStyle basis, Color textColor)
        {
            var style = new GUIStyle(basis)
            {
                font = MenuFont.Font != null ? MenuFont.Font : basis.font,
                fontSize = MenuFont.Size,
                fontStyle = MenuFont.Bold ? FontStyle.Bold : FontStyle.Normal,
                richText = false,
            };
            foreach (GUIStyleState state in States(style))
            {
                state.textColor = textColor;
            }
            return style;
        }

        private static IEnumerable<GUIStyleState> States(GUIStyle s)
        {
            return new[] { s.normal, s.hover, s.active, s.focused, s.onNormal, s.onHover, s.onActive, s.onFocused };
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }
            _stylesReady = true;
            _windowStyle = Style(GUI.skin.window, Color.white);
            _windowStyle.normal.background = _background;
            _windowStyle.onNormal.background = _background;
            _windowStyle.focused.background = _background;
            _windowStyle.border = new RectOffset(0, 0, 0, 0);
            _windowStyle.padding = new RectOffset(0, 0, 0, 0);

            ToolWindowStyles styles = ToolWindow.Styles;
            styles.Label = Style(GUI.skin.label, new Color(0.91f, 0.94f, 0.97f));
            styles.Label.alignment = TextAnchor.MiddleLeft;
            styles.MutedLabel = Style(styles.Label, ToolWindow.MutedColor);
            styles.WrappedLabel = Style(styles.MutedLabel, ToolWindow.MutedColor);
            styles.WrappedLabel.wordWrap = true;
            styles.WrappedLabel.alignment = TextAnchor.UpperLeft;
            styles.LogLabel = Style(styles.Label, new Color(0.86f, 0.91f, 0.94f));
            styles.LogLabel.wordWrap = true;
            styles.LogLabel.alignment = TextAnchor.UpperLeft;
            styles.LogLabel.padding = new RectOffset(4, 4, 4, 4);
            styles.Button = Style(GUI.skin.button, new Color(0.88f, 0.92f, 0.95f));
            styles.Button.padding = new RectOffset(10, 10, 4, 4);
            styles.SelectedButton = Style(styles.Button, ToolWindow.AccentColor);
            styles.Hint = Style(styles.MutedLabel, ToolWindow.MutedColor);
            styles.Hint.fontSize = Mathf.Max(10, MenuFont.Size - 2);
            styles.Hint.wordWrap = false;
            styles.Tag = Style(styles.Label, new Color(0.91f, 0.94f, 0.97f));
            styles.Tag.fontSize = Mathf.Max(10, MenuFont.Size - 2);
            styles.Tag.fontStyle = FontStyle.Bold;
            styles.Tag.wordWrap = false;
            styles.Danger = Style(styles.Label, ToolWindow.ErrorColor);
            styles.Danger.wordWrap = false;
            styles.Danger.clipping = TextClipping.Clip;
            styles.WrappedText = Style(styles.Label, new Color(0.91f, 0.94f, 0.97f));
            styles.WrappedText.wordWrap = true;
            styles.WrappedText.alignment = TextAnchor.UpperLeft;
            styles.AccentLabel = Style(styles.Label, ToolWindow.AccentColor);
            // The busy spinner's glyphs differ in width; centred, they turn in place.
            styles.AccentLabel.alignment = TextAnchor.MiddleCenter;
            _linkStyle = Style(styles.MutedLabel, ToolWindow.MutedColor);
            _linkStyle.wordWrap = false;
            _linkHoverStyle = Style(_linkStyle, new Color(0.91f, 0.94f, 0.97f));
            // The tab strip: words, not buttons; the selected tab's inset and
            // accent bar are painted under its word.
            _tabStyle = Style(GUI.skin.label, ToolWindow.MutedColor);
            _tabStyle.alignment = TextAnchor.MiddleCenter;
            _tabStyle.wordWrap = false;
            _tabStyle.clipping = TextClipping.Clip;
            // The word centred under the accent bar, not across it.
            _tabStyle.padding = new RectOffset(0, 0, 2, 0);
            _tabSelectedStyle = Style(_tabStyle, new Color(0.91f, 0.94f, 0.97f));
            _menuItemStyle = Style(_tabStyle, ToolWindow.MutedColor);
            _menuItemStyle.alignment = TextAnchor.MiddleLeft;
            _menuItemStyle.padding = new RectOffset(0, 0, 0, 0);
            _menuItemHoverStyle = Style(_menuItemStyle, new Color(0.91f, 0.94f, 0.97f));
            // Not GUI.skin.textField: its built-in textures would be uploaded on
            // first draw, while the window is open. Our own 1x1 texture instead.
            styles.TextField = Style(styles.Label, new Color(0.91f, 0.94f, 0.97f));
            styles.TextField.padding = new RectOffset(8, 8, 4, 4);
            styles.TextField.border = new RectOffset(0, 0, 0, 0);
            foreach (GUIStyleState state in States(styles.TextField))
            {
                state.background = _background;
            }
        }

        private void OnGUI()
        {
            if (!ShowWindow)
            {
                if (_resizing && GUIUtility.hotControl == _resizeControl)
                {
                    GUIUtility.hotControl = 0;
                }
                _resizing = false;
                if (_toastUntil > Time.realtimeSinceStartup && !DeveloperTools.Enabled && Event.current.type == EventType.Repaint)
                {
                    DrawToolsOffToast();
                }
                return;
            }

            EnsureStyles();
            _windowRect = Clamp(_windowRect, Screen.width, Screen.height);
            // Pad and trackpad presses that never reach IMGUI as mouse buttons
            // (Steam Deck) become clicks in VirtualClick.
            VirtualClick.Observe(Event.current);
            Color color = GUI.color, background = GUI.backgroundColor, content = GUI.contentColor;
            try
            {
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;
                GUI.contentColor = Color.white;
                MenuText.Begin(MenuFont.Font, MenuFont.Size);
                // Overlays (outlines, gizmos, pick modes) live in the game's
                // screen space, under the window.
                ToolWindow.DrawOverlays(_windowRect);
                _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow, string.Empty, _windowStyle);
                MenuText.End();
                // GUI.Window returns its own rectangle after the callback; apply a
                // resize afterwards so that cannot undo it.
                if (_requestedRect.HasValue)
                {
                    _windowRect = Clamp(_requestedRect.Value, Screen.width, Screen.height);
                    _requestedRect = null;
                    _requestedSize = null;
                }
                if (_requestedSize.HasValue)
                {
                    _windowRect.size = _requestedSize.Value;
                    _requestedSize = null;
                    _windowRect = Clamp(_windowRect, Screen.width, Screen.height);
                }
            }
            finally
            {
                MenuText.End();
                GUI.color = color;
                GUI.backgroundColor = background;
                GUI.contentColor = content;
            }
        }

        // F1 while the developer tools are off: a few seconds' word where the
        // window would have opened, rather than nothing on screen.
        private void DrawToolsOffToast()
        {
            EnsureStyles();
            ToolWindowStyles s = ToolWindow.Styles;
            Color color = GUI.color;
            try
            {
                GUI.color = Color.white;
                MenuText.Begin(MenuFont.Font, MenuFont.Size);
                Rect at = Clamp(_windowRect, Screen.width, Screen.height);
                float w = Mathf.Min(470f, Screen.width - at.x);
                var box = new Rect(at.x, at.y, w, 56);
                // A soft shadow straight under it, deepest at its edge.
                var shadow = new Color(0f, 0f, 0f, 0.2f);
                for (int i = 1; i <= 3; i++)
                {
                    ToolWindow.Fill(new Rect(box.x, box.yMax, box.width, i * 2), shadow);
                }
                ToolWindow.Fill(box, new Color(0.06f, 0.06f, 0.08f, 0.95f));
                ToolWindow.Fill(new Rect(box.x, box.y, 3, box.height), ToolWindow.WarningColor);
                float tw = w - 27;
                GUI.Label(new Rect(box.x + 15, box.y + 6, tw, 24), ToolWindow.Elide("Tool window stays closed: developer tools are off.", s.Label, tw), s.Label);
                GUI.Label(new Rect(box.x + 15, box.y + 30, tw, 20), ToolWindow.Elide("Options > Mods > Drag'n Wash ModFramework > Developer tools", s.Hint, tw), s.Hint);
            }
            finally
            {
                MenuText.End();
                GUI.color = color;
            }
        }

        internal void MarkBusy(string what, string detail)
        {
            if (_drawingTab != null)
            {
                WindowFooter.Busy(what, detail, _drawingTab);
            }
        }

        private void DrawWindow(int id)
        {
            ToolWindowStyles styles = ToolWindow.Styles;
            float width = _windowRect.width;
            float height = _windowRect.height;
            float bodyWidth = width - ToolWindow.Padding * 2;
            WindowFooter.BeginDraw();

            ToolWindow.Fill(new Rect(0, 0, width, HeaderHeight), ToolWindow.PanelColor);
            ToolWindow.Fill(new Rect(0, 0, 4, HeaderHeight), ToolWindow.AccentColor);
            GUI.Label(new Rect(ToolWindow.Padding, 8, width - 80, 32), "DRAG'N WASH  /  TOOLS", styles.Label);
            var close = new Rect(width - 46, 10, 30, 28);
            ToolWindow.Hint(close, $"Close the window ({_toggleKey.Value} opens it again).");
            if (GUI.Button(close, "X", styles.Button))
            {
                ShowWindow = false;
            }

            ToolTab[] tabs;
            lock (ToolWindow.Tabs)
            {
                tabs = ToolWindow.Tabs.ToArray();
            }
            if (_wantedTab != null)
            {
                ToolTab wanted = Array.Find(tabs, t => t.Title == _wantedTab);
                if (wanted != null)
                {
                    Select(wanted);
                    _wantedTab = null;
                }
            }
            if (_current == null || Array.IndexOf(tabs, _current) < 0)
            {
                Select(tabs.Length > 0 ? tabs[0] : null);
            }

            float bodyTop = HeaderHeight + StripHeight;
            // Under the body: the notice strip, when there is a notice, and the
            // hint line, which stays.
            float bodyBottom = WindowFooter.BodyBottom(width, height, out Rect noticeRect, out Rect hintRect);
            var body = new Rect(ToolWindow.Padding, bodyTop, bodyWidth, Mathf.Max(80, bodyBottom - bodyTop));
            DrawTabStrip(tabs, width, body);

            // What lies over the body - the notice's whole text, the busy
            // overlay, the More menu - takes its input before the tab does:
            // IMGUI hands an event to controls in drawing order, and the tab
            // is drawn first.
            Event ev = Event.current;
            bool busy = WindowFooter.IsBusy(_current);
            WindowFooter.HandleInput(ev, noticeRect);
            // Esc answers a waiting question with Cancel, whatever the tab does with Esc.
            if (InlineConfirm.Active && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
            {
                InlineConfirm.Cancel();
                ev.Use();
            }
            if (busy && (ev.isKey || ((ev.isMouse || ev.type == EventType.ScrollWheel || ev.type == EventType.ContextClick) && body.Contains(ev.mousePosition))))
            {
                ev.Use();
            }
            // Nor does the tab paint a hover look under them.
            Vector2 pointer = ev.mousePosition;
            bool pointerHidden = ev.type == EventType.Repaint && ((busy && body.Contains(pointer)) || WindowFooter.Covers(pointer) || (_moreOpen && _moreMenu.Contains(pointer)));
            if (pointerHidden)
            {
                ev.mousePosition = new Vector2(-100000f, -100000f);
            }
            // The body in the selected tab's colour, joined to it.
            ToolWindow.Fill(body, ToolWindow.InsetColor);
            // A control's tooltip is shown on the hint line (ToolWindow.Hint).
            if (ev.type == EventType.Repaint)
            {
                GUI.tooltip = string.Empty;
            }

            if (_current == null)
            {
                GUI.Label(body, "No mod has added a tab yet.", styles.WrappedLabel);
            }
            else if (_current.Failure != null)
            {
                float row = ToolWindow.RowHeight;
                GUI.Label(new Rect(body.x + 12, body.y + 12, body.width - 24, body.height - 24 - row - 8),
                    $"This tab stopped working and was turned off. See BepInEx/LogOutput.log.\n\n{_current.Owner}: {_current.Failure}", styles.WrappedLabel);
                // Some of what a tab draws is gone by the time it is drawn - an
                // object destroyed, a scene changed - and the next frame would
                // have been fine. One press to find out, rather than a restart.
                if (GUI.Button(new Rect(body.x + 12, body.yMax - row - 10, 120, row), "Try again", styles.Button))
                {
                    Log.LogInfo($"The tool window tab \"{_current.Title}\" of {_current.Owner} was turned on again by hand.");
                    _current.Failure = null;
                    WindowFooter.Clear();
                }
            }
            else
            {
                // Inside a group, so a tab drawn for a taller window is clipped
                // at the body's edge instead of running over the footer line.
                GUI.BeginGroup(body);
                _drawingTab = _current;
                try
                {
                    _current.Draw(new Rect(0, 0, body.width, body.height));
                }
                catch (Exception ex) when (!(ex is ExitGUIException))
                {
                    _current.Failure = ex.GetType().Name + ": " + ex.Message;
                    Log.LogError($"The tool window tab \"{_current.Title}\" of {_current.Owner} threw and was turned off: {ex}");
                    WindowFooter.Show($"\"{_current.Title}\" stopped working; see the log.", NoticeKind.Error, 0f);
                }
                finally
                {
                    _drawingTab = null;
                    GUI.EndGroup();
                }
            }
            if (pointerHidden)
            {
                ev.mousePosition = pointer;
            }
            if (ev.type == EventType.Repaint && !string.IsNullOrEmpty(GUI.tooltip))
            {
                WindowFooter.SetHint(GUI.tooltip, WindowFooter.HintPointer);
            }

            if (busy)
            {
                WindowFooter.DrawBusy(body, styles);
            }
            DrawMoreMenu();
            WindowFooter.DrawNotice(noticeRect, styles);
            // Reset window, at the end of the hint line: the way back when a
            // remembered place is off screen after a resolution change.
            var resetContent = new GUIContent("Reset window");
            float resetWidth = _linkStyle.CalcSize(resetContent).x;
            var resetRect = new Rect(hintRect.xMax - resetWidth, hintRect.y, resetWidth, hintRect.height);
            hintRect.width -= resetWidth + 16;
            bool onReset = resetRect.Contains(ev.mousePosition);
            if (onReset)
            {
                WindowFooter.SetHint($"Reset window: back to ({DefaultRect.x:0}, {DefaultRect.y:0}), {DefaultRect.width:0} x {DefaultRect.height:0}.", WindowFooter.HintPointer);
            }
            if (GUI.Button(resetRect, resetContent, onReset ? _linkHoverStyle : _linkStyle))
            {
                _requestedRect = DefaultRect;
            }
            ToolWindow.Fill(new Rect(resetRect.x, resetRect.center.y + _linkStyle.lineHeight / 2f, resetRect.width, 1), onReset ? new Color(0.91f, 0.94f, 0.97f) : ToolWindow.MutedColor);
            WindowFooter.DrawHint(hintRect, $"{_toggleKey.Value}: toggle    |    Drag title to move    |    Drag corner to resize", styles);
            if (ev.type == EventType.Repaint)
            {
                WindowFooter.Tick();
                InlineConfirm.Tick();
            }

            HandleResize(new Rect(width - GripSize, height - GripSize, GripSize, GripSize));
            // Dragging only by the title, so selecting text or scrolling never
            // moves the window.
            GUI.DragWindow(new Rect(4, 0, width - CloseMargin, HeaderHeight));
        }

        // The resize corner: three short diagonal strokes, brighter under the
        // pointer and while dragging. Painted with small squares, as IMGUI has no lines.
        private void DrawGrip(Rect grip)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            bool hot = _resizing || grip.Contains(Event.current.mousePosition);
            Color color = hot ? new Color(0.91f, 0.94f, 0.97f) : ToolWindow.MutedColor;
            float ox = grip.xMax - 20, oy = grip.yMax - 20;
            foreach (int from in new[] { 3, 8, 13 })
            {
                for (int t = 0; t <= 15 - from; t++)
                {
                    ToolWindow.Fill(new Rect(ox + 15 - t - 0.75f, oy + from + t - 0.75f, 1.5f, 1.5f), color);
                }
            }
        }

        // One row under the header: the selected tab in the body's colour
        // with an accent bar on top, joined to the body; the others as words.
        // What does not fit goes under More at the end of the row, which
        // carries the selected tab's name when it is one of those.
        private void DrawTabStrip(ToolTab[] tabs, float width, Rect body)
        {
            Event ev = Event.current;
            const float gap = 2f, pad = 24f, most = 220f, itemHeight = 28f;
            float left = ToolWindow.Padding, right = width - ToolWindow.Padding, top = HeaderHeight;
            string arrow = " " + ToolWindow.DownArrow;
            var widths = new float[tabs.Length];
            float total = 0f;
            for (int i = 0; i < tabs.Length; i++)
            {
                widths[i] = Mathf.Min(most, _tabStyle.CalcSize(new GUIContent(tabs[i].Title)).x + pad);
                total += widths[i] + (i > 0 ? gap : 0f);
            }
            _hiddenTabs.Clear();
            int shown = tabs.Length;
            float moreWidth = 0f;
            if (total > right - left)
            {
                moreWidth = _tabStyle.CalcSize(new GUIContent("More" + arrow)).x + pad;
                if (_current != null)
                {
                    moreWidth = Mathf.Max(moreWidth, Mathf.Min(most, _tabStyle.CalcSize(new GUIContent(_current.Title + arrow)).x + pad));
                }
                float fit = left;
                shown = 0;
                while (shown < tabs.Length && fit + widths[shown] <= right - moreWidth - gap)
                {
                    fit += widths[shown] + gap;
                    shown++;
                }
                for (int i = shown; i < tabs.Length; i++)
                {
                    _hiddenTabs.Add(tabs[i]);
                }
            }
            if (_hiddenTabs.Count == 0)
            {
                _moreOpen = false;
            }
            bool currentHidden = _current != null && _hiddenTabs.Contains(_current);
            _moreButton = _hiddenTabs.Count > 0 ? new Rect(right - moreWidth, top, moreWidth, StripHeight) : Rect.zero;
            float menuWidth = 170f;
            foreach (ToolTab t in _hiddenTabs)
            {
                menuWidth = Mathf.Max(menuWidth, _menuItemStyle.CalcSize(new GUIContent(t.Title)).x + 28);
            }
            menuWidth = Mathf.Min(menuWidth, right - left);
            float menuHeight = Mathf.Min(_hiddenTabs.Count * itemHeight + 8, Mathf.Max(itemHeight + 8, body.yMax - body.y));
            _moreMenu = _moreOpen ? new Rect(right - menuWidth, top + StripHeight, menuWidth, menuHeight) : Rect.zero;

            // The open menu lies over the body, so its input comes first.
            if (_moreOpen)
            {
                if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
                {
                    _moreOpen = false;
                    ev.Use();
                }
                else if (ev.type == EventType.MouseDown && _moreMenu.Contains(ev.mousePosition))
                {
                    int i = Mathf.FloorToInt((ev.mousePosition.y - _moreMenu.y - 4) / itemHeight);
                    if (i >= 0 && i < _hiddenTabs.Count)
                    {
                        Select(_hiddenTabs[i]);
                        _wantedTab = null;
                    }
                    _moreOpen = false;
                    ev.Use();
                }
                else if (ev.type == EventType.MouseDown && !_moreButton.Contains(ev.mousePosition))
                {
                    // A click beside it closes it; on the body, that is all it does.
                    _moreOpen = false;
                    if (body.Contains(ev.mousePosition))
                    {
                        ev.Use();
                    }
                }
                else if ((ev.isMouse || ev.type == EventType.ScrollWheel || ev.type == EventType.ContextClick) && _moreMenu.Contains(ev.mousePosition))
                {
                    ev.Use();
                }
            }

            float x = left;
            for (int i = 0; i < shown; i++)
            {
                var r = new Rect(x, top, widths[i], StripHeight);
                bool selected = ReferenceEquals(tabs[i], _current);
                if (selected)
                {
                    ToolWindow.Fill(r, ToolWindow.InsetColor);
                    ToolWindow.Fill(new Rect(r.x, r.y, r.width, 2), ToolWindow.AccentColor);
                }
                string title = ToolWindow.Elide(tabs[i].Title, _tabStyle, r.width - pad + 8);
                if (title != tabs[i].Title)
                {
                    ToolWindow.Hint(r, tabs[i].Title);
                }
                if (GUI.Button(r, title, selected || r.Contains(ev.mousePosition) ? _tabSelectedStyle : _tabStyle))
                {
                    Select(tabs[i]);
                    _wantedTab = null;
                    _moreOpen = false;
                }
                x += widths[i] + gap;
            }
            if (_hiddenTabs.Count > 0)
            {
                if (currentHidden)
                {
                    ToolWindow.Fill(_moreButton, ToolWindow.InsetColor);
                    ToolWindow.Fill(new Rect(_moreButton.x, _moreButton.y, _moreButton.width, 2), ToolWindow.AccentColor);
                }
                string label = (currentHidden ? ToolWindow.Elide(_current.Title, _tabStyle, _moreButton.width - pad - _tabStyle.CalcSize(new GUIContent(arrow)).x + 8) : "More") + arrow;
                ToolWindow.Hint(_moreButton, $"{_hiddenTabs.Count} more tab(s) that do not fit; a wider window shows them in the row.");
                if (GUI.Button(_moreButton, label, currentHidden || _moreOpen || _moreButton.Contains(ev.mousePosition) ? _tabSelectedStyle : _tabStyle))
                {
                    _moreOpen = !_moreOpen;
                }
            }
        }

        // Painted after the body, which it lies over; its input was taken in DrawTabStrip.
        private void DrawMoreMenu()
        {
            if (!_moreOpen || _hiddenTabs.Count == 0 || Event.current.type != EventType.Repaint)
            {
                return;
            }
            Rect box = _moreMenu;
            ToolWindow.Fill(new Rect(box.x - 1, box.y - 1, box.width + 2, box.height + 2), new Color(0.165f, 0.2f, 0.26f));
            ToolWindow.Fill(box, ToolWindow.PanelColor);
            GUI.BeginGroup(box);
            float y = 4f;
            Vector2 pointer = Event.current.mousePosition;
            foreach (ToolTab t in _hiddenTabs)
            {
                var line = new Rect(0, y, box.width, 28f);
                y += 28f;
                if (line.y >= box.height)
                {
                    break;
                }
                bool selected = ReferenceEquals(t, _current);
                if (selected)
                {
                    ToolWindow.Fill(line, ToolWindow.InsetColor);
                    ToolWindow.Fill(new Rect(0, line.y, 2, line.height), ToolWindow.AccentColor);
                }
                GUIStyle style = selected || line.Contains(pointer) ? _menuItemHoverStyle : _menuItemStyle;
                // Past the 2-pixel bar, 12 in, as the tabs are padded.
                GUI.Label(new Rect(14, line.y, line.width - 26, line.height), ToolWindow.Elide(t.Title, style, line.width - 26), style);
            }
            GUI.EndGroup();
        }

        private void HandleResize(Rect grip)
        {
            DrawGrip(grip);
            int control = GUIUtility.GetControlID("DragNWashToolWindowResize".GetHashCode(), FocusType.Passive);
            Event current = Event.current;
            Vector2 screenMouse = current.mousePosition + _windowRect.position;
            if (current.type == EventType.MouseDown && current.button == 0 && grip.Contains(current.mousePosition))
            {
                _resizing = true;
                _resizeControl = control;
                GUIUtility.hotControl = control;
                _resizeStartMouse = screenMouse;
                _resizeStartSize = _windowRect.size;
                current.Use();
            }
            if (_resizing && GUIUtility.hotControl == _resizeControl)
            {
                if (current.type == EventType.MouseDrag)
                {
                    _requestedSize = _resizeStartSize + screenMouse - _resizeStartMouse;
                    current.Use();
                }
                else if (current.type == EventType.MouseUp)
                {
                    _resizing = false;
                    GUIUtility.hotControl = 0;
                    current.Use();
                }
            }
        }
    }
}
