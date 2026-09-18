using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    // The "Console" tab: the log with levels in colour, filters the player
    // chooses, and a line to type commands into.
    internal static class ConsoleTab
    {
        internal const string Title = "Console";
        private const string InputControl = "DragNWashConsoleInput";

        internal static string SourceFilter = "";

        private static ToolTab _tab;
        private static Vector2 _scroll;
        private static bool _follow = true;
        private static string _input = "";
        private static readonly List<string> History = new List<string>();
        private static int _historyIndex = -1;
        private static bool _focusInput;
        // Whether the input had keyboard focus when it was last drawn. Asked
        // before the field is drawn in a pass, GetNameOfFocusedControl does not
        // know the name yet, so keys would fall through to the field itself.
        private static bool _inputFocused;
        private static int _inputControlId;
        // Set when the line was changed from code (Accept, history, Submit):
        // the field keeps its old caret index, so the caret is moved to the end.
        private static bool _cursorToEnd;
        // Frame in which Enter or Tab was acted on. Unity delivers one key as
        // two KeyDown events, the key code first and then the character (LF,
        // CR or HT); the second must be swallowed, not acted on again.
        private static int _actedFrame = -1;

        // [Console] TraceInput: every key the tab sees and what it did with it,
        // written to the log as DragNWash.ConsoleTrace, for debugging the input.
        internal static bool Trace;
        private static readonly ManualLogSource TraceLog = BepInEx.Logging.Logger.CreateLogSource("DragNWash.ConsoleTrace");

        private static void T(string what)
        {
            if (Trace)
            {
                TraceLog.LogInfo(what);
            }
        }
        private static List<string> _suggestions = new List<string>();
        private static int _selected;
        private const int MaxSuggestionRows = 6;
        private static Dictionary<LogLevel, GUIStyle> _levelStyles;
        private static GUIStyle _dot;

        private static readonly (LogLevel level, string label)[] Toggles =
        {
            (LogLevel.Error | LogLevel.Fatal, "Error"), (LogLevel.Warning, "Warning"), (LogLevel.Message, "Message"), (LogLevel.Info, "Info"), (LogLevel.Debug, "Debug"),
        };

        internal static void Install()
        {
            if (_tab != null)
            {
                return;
            }
            ToolWindow.AddTab(ToolWindow.Guid, Title, Draw, 40);
            lock (ToolWindow.Tabs)
            {
                _tab = ToolWindow.Tabs.Find(t => t.Title == Title && t.Owner == ToolWindow.Guid);
            }
        }

        // Called from the plugin's Update: the badge on the tab button.
        internal static void Tick()
        {
            FlushPending();
            if (_tab != null)
            {
                _tab.Title = ConsoleLog.UnseenErrors > 0 ? Title + " !" : Title;
            }
        }

        private static void EnsureStyles(ToolWindowStyles s)
        {
            if (_levelStyles != null)
            {
                return;
            }
            GUIStyle Colored(Color c)
            {
                var style = new GUIStyle(s.LogLabel);
                style.normal.textColor = c;
                style.hover.textColor = c;
                style.wordWrap = true;
                style.padding = new RectOffset(4, 4, 1, 1);
                return style;
            }
            _levelStyles = new Dictionary<LogLevel, GUIStyle>
            {
                { LogLevel.Fatal, Colored(ToolWindow.ErrorColor) },
                { LogLevel.Error, Colored(ToolWindow.ErrorColor) },
                { LogLevel.Warning, Colored(ToolWindow.WarningColor) },
                { LogLevel.Message, Colored(ToolWindow.AccentColor) },
                { LogLevel.Info, Colored(new Color(0.86f, 0.91f, 0.94f)) },
                { LogLevel.Debug, Colored(ToolWindow.MutedColor) },
            };
            _dot = Colored(ToolWindow.ErrorColor);
        }

        private static void Draw(Rect area)
        {
            ToolWindowStyles s = ToolWindow.Styles;
            EnsureStyles(s);
            float row = ToolWindow.RowHeight, pad = ToolWindow.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;
            ConsoleLog.MarkSeen();

            // Level toggles, wrapping onto more rows in a narrow window, then
            // the source filter and Clear on a row of their own when the
            // toggles leave no room beside them.
            float bx = x;
            foreach ((LogLevel level, string label) in Toggles)
            {
                bool on = (ConsoleLog.Shown & level) != 0;
                float bw = Mathf.Max(70, s.Button.CalcSize(new GUIContent(label)).x + 16);
                if (bx + bw > x + w && bx > x)
                {
                    bx = x;
                    y += row + 6;
                }
                if (GUI.Button(new Rect(bx, y, bw, row), label, on ? s.SelectedButton : s.Button))
                {
                    ConsoleLog.Shown = on ? ConsoleLog.Shown & ~level : ConsoleLog.Shown | level;
                }
                bx += bw + 6;
            }
            if (area.xMax - pad - 80 - (bx + 6) < 120)
            {
                bx = x;
                y += row + 6;
            }
            if (GUI.Button(new Rect(area.xMax - pad - 70, y, 70, row), "Clear", s.Button))
            {
                ConsoleLog.Clear();
            }
            var filterRect = new Rect(bx + (bx > x ? 6 : 0), y, Mathf.Max(60, area.xMax - pad - 80 - bx), row);
            SourceFilter = GUI.TextField(filterRect, SourceFilter ?? "", s.TextField);
            Underline(filterRect);
            if (string.IsNullOrEmpty(SourceFilter))
            {
                GUI.Label(new Rect(filterRect.x + 6, filterRect.y, filterRect.width - 6, row), "Filter by source", s.MutedLabel);
            }
            y += row + 6;

            string note = (ConsoleLog.Shown & LogLevel.Error) == 0 ? "Errors are hidden.    " : "";
            note += $"Unity: {ConsoleLog.UnityMinimum} and above, others: {ConsoleLog.DefaultMinimum} and above (log level <source> <level>)";
            // Sized from the text: in a narrow window it takes two lines.
            var noteContent = new GUIContent(note);
            float noteHeight = Mathf.Max(row, s.WrappedLabel.CalcHeight(noteContent, w));
            GUI.Label(new Rect(x, y, w, noteHeight), noteContent, s.WrappedLabel);
            y += noteHeight;

            // The list is filled below, when a keystroke changes the field, and
            // emptied by Enter, Tab, Escape and the history keys: it appears while
            // the player is typing and never on its own.
            float suggestionHeight = _suggestions.Count > 0 && _inputFocused ? _suggestions.Count * (row - 6) + 6 : 0;

            // The log, oldest first, following the end unless the player scrolled up.
            var view = new Rect(x, y, w, Mathf.Max(40, area.yMax - pad - y - row - 8 - suggestionHeight));
            ToolWindow.Fill(view, ToolWindow.InsetColor);
            List<ConsoleEntry> all = ConsoleLog.Snapshot();
            var shown = new List<ConsoleEntry>(all.Count);
            foreach (ConsoleEntry e in all)
            {
                if (ConsoleLog.IsShown(e) && (string.IsNullOrEmpty(SourceFilter) || e.Source.IndexOf(SourceFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    shown.Add(e);
                }
            }
            float inner = view.width - 20;
            var heights = new float[shown.Count];
            float total = 0;
            for (int i = 0; i < shown.Count; i++)
            {
                heights[i] = _levelStyles[Normalize(shown[i].Level)].CalcHeight(new GUIContent(Line(shown[i])), inner);
                total += heights[i];
            }
            if (_follow)
            {
                _scroll.y = Mathf.Max(0, total - view.height);
            }
            Vector2 before = _scroll;
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, inner, Mathf.Max(view.height, total)), false, false);
            float ry = 0;
            for (int i = 0; i < shown.Count; i++)
            {
                if (ry + heights[i] >= _scroll.y && ry <= _scroll.y + view.height)
                {
                    GUI.Label(new Rect(0, ry, inner, heights[i]), Line(shown[i]), _levelStyles[Normalize(shown[i].Level)]);
                }
                ry += heights[i];
            }
            GUI.EndScrollView();
            if (Event.current.type == EventType.ScrollWheel && view.Contains(Event.current.mousePosition))
            {
                _follow = false;
            }
            if (_scroll.y >= total - view.height - 2)
            {
                _follow = true;
            }
            else if (_scroll != before)
            {
                _follow = false;
            }
            y = view.yMax + 6;

            // The command line. Enter runs; Tab fills in the selected suggestion
            // or brings the cursor here; up and down move through suggestions
            // when there are any, else the history. Keys are handled before the
            // field is drawn, so the field never sees them; a single-line
            // TextField leaves Enter and Tab unused anyway, and an unused Tab
            // is what makes IMGUI move the focus to the next control.
            Event ev = Event.current;
            bool focused = _inputFocused;
            bool suggesting = focused && _suggestions.Count > 0;
            if (ev.type == EventType.KeyDown || ev.type == EventType.KeyUp)
            {
                T($"{ev.type} key={ev.keyCode} ch={(int)ev.character} mods={ev.modifiers} focused={focused} named=\"{GUI.GetNameOfFocusedControl()}\" kb={GUIUtility.keyboardControl} input=\"{_input}\" suggestions={_suggestions.Count} selected={_selected} historyIndex={_historyIndex}");
            }
            bool enter = ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.character == (char)10 || ev.character == (char)13;
            bool tab = ev.keyCode == KeyCode.Tab || ev.character == (char)9;
            if (ev.type == EventType.KeyDown && (enter || tab) && _actedFrame == Time.frameCount)
            {
                // The character half of a key already acted on this frame.
                ev.Use();
            }
            else if (ev.type == EventType.KeyDown && tab)
            {
                if (suggesting)
                {
                    string pick = _suggestions[Mathf.Clamp(_selected, 0, _suggestions.Count - 1)];
                    T("-> Accept " + pick);
                    Accept(pick);
                }
                else
                {
                    T(focused ? "-> Tab with no suggestions" : "-> Tab: focus the command line");
                }
                _focusInput = true;
                _actedFrame = Time.frameCount;
                ev.Use();
            }
            else if (focused && ev.type == EventType.KeyDown)
            {
                if (enter)
                {
                    T("-> Submit");
                    Submit();
                    _actedFrame = Time.frameCount;
                    ev.Use();
                }
                else if (ev.keyCode == KeyCode.Escape && suggesting)
                {
                    _suggestions = new List<string>();
                    ev.Use();
                }
                else if (ev.keyCode == KeyCode.UpArrow)
                {
                    T(suggesting ? "-> select up" : "-> history up");
                    if (suggesting)
                    {
                        _selected = (_selected - 1 + _suggestions.Count) % _suggestions.Count;
                    }
                    else if (History.Count > 0)
                    {
                        _historyIndex = _historyIndex < 0 ? History.Count - 1 : Mathf.Max(0, _historyIndex - 1);
                        _input = History[_historyIndex];
                        _cursorToEnd = true;
                    }
                    ev.Use();
                }
                else if (ev.keyCode == KeyCode.DownArrow)
                {
                    T(suggesting ? "-> select down" : "-> history down");
                    if (suggesting)
                    {
                        _selected = (_selected + 1) % _suggestions.Count;
                    }
                    else if (_historyIndex >= 0)
                    {
                        _historyIndex = _historyIndex + 1 < History.Count ? _historyIndex + 1 : -1;
                        _input = _historyIndex < 0 ? "" : History[_historyIndex];
                        _cursorToEnd = true;
                    }
                    ev.Use();
                }
            }
            // The list sits above the field but is drawn after it (below). IMGUI
            // numbers controls in drawing order, and keyboard focus is that
            // number: buttons drawn before the field gave it a different number
            // whenever the list appeared, so the focus fell off the field on
            // every other pass, keystrokes were dropped and the list flickered.
            var box = new Rect(x + 20, y, w - 20 - 70, suggestionHeight);
            if (suggesting)
            {
                y = box.yMax + 2;
            }
            GUI.Label(new Rect(x, y, 20, row), ">", s.Label);
            GUI.SetNextControlName(InputControl);
            var inputRect = new Rect(x + 20, y, w - 20 - 70, row);
            string inputBefore = _input;
            _input = GUI.TextField(inputRect, _input ?? "", s.TextField);
            bool wasFocused = _inputFocused;
            // Focus is given and read in repaint passes only. The trace showed
            // that GetNameOfFocusedControl answers "" in a Layout pass, and that
            // GUI.FocusControl in a KeyDown pass leaves keyboardControl at 0, so
            // the focus was lost after every Tab. RuntimeUnityEditor's REPL does
            // the same: FocusControl from Repaint, then read the name back. The
            // control id is kept as the witness for every other pass.
            if (ev.type == EventType.Repaint)
            {
                if (_focusInput)
                {
                    GUI.FocusControl(InputControl);
                    _focusInput = false;
                }
                _inputFocused = GUI.GetNameOfFocusedControl() == InputControl;
                _inputControlId = _inputFocused ? GUIUtility.keyboardControl : 0;
                // The line was replaced from code: put the caret after it. Done
                // on the second repaint with focus, after the field has settled
                // its own caret for the new focus.
                if (_cursorToEnd && wasFocused && _inputFocused)
                {
                    var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
                    if (editor != null)
                    {
                        editor.MoveTextEnd();
                    }
                    _cursorToEnd = false;
                }
            }
            else if (_inputControlId != 0)
            {
                _inputFocused = GUIUtility.keyboardControl == _inputControlId;
            }
            if (_input != inputBefore)
            {
                // Typed (or erased) in the field: offer completions for the new
                // text. History and Accept change _input above this point, so
                // they do not count as typing and bring no list of their own.
                _suggestions = string.IsNullOrEmpty(_input) ? new List<string>() : ConsoleCommands.Suggest(_input, MaxSuggestionRows);
                _selected = 0;
            }
            if (_input != inputBefore || _inputFocused != wasFocused)
            {
                T($"field: input=\"{_input}\" focused={_inputFocused} (event {ev.type})");
            }
            Underline(inputRect);
            if (suggesting)
            {
                ToolWindow.Fill(box, ToolWindow.PanelColor);
                float sy = box.y + 3;
                for (int i = 0; i < _suggestions.Count; i++)
                {
                    var line = new Rect(box.x + 6, sy, box.width - 12, row - 6);
                    if (i == _selected)
                    {
                        ToolWindow.Fill(line, ToolWindow.InsetColor);
                    }
                    if (GUI.Button(line, _suggestions[i], i == _selected ? s.Label : s.MutedLabel))
                    {
                        Accept(_suggestions[i]);
                        _focusInput = true;
                    }
                    sy += row - 6;
                }
            }
            if (GUI.Button(new Rect(area.xMax - pad - 64, y, 64, row), "Run", s.Button))
            {
                T("-> Run button");
                Submit();
            }
        }

        // Puts the suggestion in place of the word being typed, with a space after it.
        private static void Accept(string suggestion)
        {
            string line = _input ?? "";
            int cut = line.Length;
            if (cut > 0 && !char.IsWhiteSpace(line[cut - 1]))
            {
                while (cut > 0 && !char.IsWhiteSpace(line[cut - 1]))
                {
                    cut--;
                }
            }
            _input = line.Substring(0, cut) + suggestion + " ";
            // What can follow the accepted word, at once: the arguments of a
            // command, or nothing when it offers none.
            _suggestions = ConsoleCommands.Suggest(_input, MaxSuggestionRows);
            _selected = 0;
            _cursorToEnd = true;
        }

        private static void Submit()
        {
            string line = (_input ?? "").Trim();
            T($"Submit \"{line}\"");
            _input = "";
            _historyIndex = -1;
            _suggestions = new List<string>();
            _cursorToEnd = true;
            _focusInput = true;
            _follow = true;
            if (line.Length == 0)
            {
                return;
            }
            if (History.Count == 0 || History[History.Count - 1] != line)
            {
                History.Add(line);
                if (History.Count > 100)
                {
                    History.RemoveAt(0);
                }
            }
            ConsoleCommands.Execute(line);
        }

        private static string Line(ConsoleEntry e)
        {
            string line = e.Source == ConsoleLog.CommandSource
                ? e.Text
                : $"{e.Time:HH:mm:ss} [{Short(e.Level)}] {e.Source}: {e.Text}";
            return Drawable(line);
        }

        // Log lines carry any text, including Japanese from the localization
        // mod. Drawing a character the window font has not rasterised uploads
        // its atlas in the middle of the frame; on Direct3D 12 many such uploads
        // in one frame crash the game (UUM-140564). Characters some mod already
        // prepared (the localization mod prepares its language's at startup) are
        // drawn as they are. Others are drawn as '?' once and prepared for the
        // next frame from Update, the way PrepareCharacters works. On Direct3D 12
        // that is safe only while the core batches atlas uploads to once per
        // frame; without it they stay '?'.
        private static readonly HashSet<char> Prepared = new HashSet<char>();
        private static readonly StringBuilder Pending = new StringBuilder();
        private static readonly bool NeverPrepare = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12 && !UploadsBatched();

        // Its own method, so a core without the property (older than this Tool
        // window) is caught here instead of failing the whole class.
        private static bool UploadsBatched()
        {
            try
            {
                return BatchedFromCore();
            }
            catch (MissingMemberException)
            {
                return false;
            }
        }

        private static bool BatchedFromCore() => GameInfo.FontAtlasUploadsBatched;

        internal static string Drawable(string text)
        {
            StringBuilder sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                // On Direct3D 12 even a character that was requested once is not
                // safe: the dynamic font can drop it and rebuild its atlas mid-frame.
                if (c < 128 || (!NeverPrepare && (MenuFont.IsPrepared(c) || Prepared.Contains(c))))
                {
                    sb?.Append(c);
                    continue;
                }
                if (sb == null)
                {
                    sb = new StringBuilder(text.Length);
                    sb.Append(text, 0, i);
                }
                sb.Append('?');
                if (!NeverPrepare && Prepared.Add(c))
                {
                    Pending.Append(c);
                }
            }
            return sb == null ? text : sb.ToString();
        }

        // From the plugin's Update: rasterise what the last frame could not draw.
        private static void FlushPending()
        {
            if (Pending.Length > 0)
            {
                MenuFont.Prepare(Pending.ToString());
                Pending.Length = 0;
            }
        }

        private static string Short(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Fatal: return "F";
                case LogLevel.Error: return "E";
                case LogLevel.Warning: return "W";
                case LogLevel.Message: return "M";
                case LogLevel.Info: return "I";
                default: return "D";
            }
        }

        private static LogLevel Normalize(LogLevel level)
        {
            return _levelStyles.ContainsKey(level) ? level : LogLevel.Info;
        }

        private static void Underline(Rect field)
        {
            Color was = GUI.color;
            GUI.color = ToolWindow.AccentColor;
            GUI.DrawTexture(new Rect(field.x, field.yMax - 2, field.width, 2), Texture2D.whiteTexture);
            GUI.color = was;
        }
    }
}
