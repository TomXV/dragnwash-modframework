using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Globalization;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // A colour picker for the Inspector: a saturation/value square with a hue
    // bar, RGBA sliders, a hex field and a swatch, applied live. The square is
    // two fixed gradient textures tinted at draw time (white-to-hue across,
    // clear-to-black down), so nothing is uploaded while the game runs
    // (Direct3D 12); the textures are made once, at the plugin's Awake.
    internal static class InspectorColorPicker
    {
        internal static bool Open { get; private set; }
        internal static string Key { get; private set; }

        private static Color _value;
        private static float _hue;          // kept apart: RGB loses the hue at zero saturation
        private static Action<Color> _apply;
        private static string _hex = "";
        private static bool _hexEditing;
        private static int _dragging = -1;
        private const string HexControl = "DnWInspectHex";
        private const int SquareId = 10, HueId = 11;
        private const float Square = 132f;

        private static Texture2D _acrossAlpha, _downBlack, _hueStrip;

        internal const float Height = 26 + Square + 8 + 4 * 26 + 12;

        // From the plugin's Awake: the gradients, uploaded before any frame is presented.
        internal static void CreateTextures()
        {
            if (_acrossAlpha != null)
            {
                return;
            }
            const int n = 64;
            _acrossAlpha = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _downBlack = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _hueStrip = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                _acrossAlpha.SetPixel(i, 0, new Color(1, 1, 1, t));
                // Texture rows go bottom-up; the top of the square is the brightest.
                _downBlack.SetPixel(0, i, new Color(0, 0, 0, 1 - t));
                _hueStrip.SetPixel(0, i, Color.HSVToRGB(1 - t, 1, 1));
            }
            _acrossAlpha.Apply();
            _downBlack.Apply();
            _hueStrip.Apply();
        }

        // apply: live, on every change; commit: once a change is done (a drag
        // ends, hex is typed, the picker closes), for one history entry.
        private static Action<Color, Color> _commit;
        // The colour the row held at the last commit (or when the picker opened,
        // or when something else changed the row): what a commit reports as "before".
        private static Color _committed;

        internal static void Show(string key, Color current, Action<Color> apply, Action<Color, Color> commit)
        {
            Open = true;
            Key = key;
            _value = current;
            _committed = current;
            Color.RGBToHSV(current, out _hue, out _, out _);
            _apply = apply;
            _commit = commit;
            _hex = ToHex(current);
            _hexEditing = false;
        }

        // Records what the picker changed so far, keeping it open.
        internal static void CommitPending()
        {
            if (Open) Commit();
        }

        private static void Commit()
        {
            if (_value == _committed)
            {
                return;
            }
            Color before = _committed;
            _committed = _value;
            try { _commit?.Invoke(before, _value); } catch (Exception ex) { TW.ShowNotice("History: " + ex.Message, NoticeKind.Error); }
        }

        internal static void Close()
        {
            if (Open)
            {
                Commit();
            }
            Open = false;
            Key = null;
            _apply = null;
            _commit = null;
            _dragging = -1;
        }

        // The current colour as another row would show it after live edits.
        internal static void Sync(Color current)
        {
            if (_dragging < 0 && !_hexEditing && current != _value)
            {
                // Changed from outside the picker (Reset, Undo, a typed component):
                // shown as it is, and it is the baseline from here on.
                _value = current;
                _committed = current;
                Color.RGBToHSV(current, out float h, out float sat, out _);
                if (sat > 0.001f)
                {
                    _hue = h;
                }
                _hex = ToHex(current);
            }
        }

        internal static void Draw(Rect area, ToolWindowStyles s)
        {
            if (!Open)
            {
                return;
            }
            TW.Fill(area, TW.PanelColor);
            float x = area.x + 8, y = area.y + 6, w = area.width - 16;
            const float line = 26;
            Event ev = Event.current;

            // Swatch, hex and Close on the first line.
            TW.Fill(new Rect(x, y + 2, 60, line - 4), _value);
            GUI.SetNextControlName(HexControl);
            var hexRect = new Rect(x + 68, y, 110, line - 2);
            string typed = GUI.TextField(hexRect, _hex, s.TextField);
            TW.Underline(hexRect);
            bool hexFocused = GUI.GetNameOfFocusedControl() == HexControl;
            if (ev.type == EventType.Repaint)
            {
                _hexEditing = hexFocused;
            }
            if (typed != _hex)
            {
                _hex = typed;
                if (TryParseHex(typed, out Color parsed))
                {
                    Set(parsed);
                    Commit();
                }
            }
            GUI.Label(new Rect(x + 186, y, Mathf.Max(0, w - 186 - 76), line), "hex", Hint(s));
            if (GUI.Button(new Rect(area.xMax - 70, y, 62, line - 2), "Close", s.Button))
            {
                Close();
                return;
            }
            y += line;

            // The saturation/value square and the hue bar.
            Color.RGBToHSV(_value, out float h, out float sat, out float v);
            if (sat > 0.001f && _dragging != HueId)
            {
                _hue = h;
            }
            var square = new Rect(x, y, Square, Square);
            var hueBar = new Rect(square.xMax + 10, y, 22, Square);
            if (_acrossAlpha != null)
            {
                TW.Fill(square, Color.white);
                Color was = GUI.color;
                GUI.color = Color.HSVToRGB(_hue, 1, 1);
                GUI.DrawTexture(square, _acrossAlpha);
                GUI.color = Color.white;
                GUI.DrawTexture(square, _downBlack);
                GUI.DrawTexture(hueBar, _hueStrip);
                GUI.color = was;
            }
            // Markers: a small ring on the square, a line on the bar.
            var mark = new Vector2(square.x + sat * square.width, square.y + (1 - v) * square.height);
            Outline(new Rect(mark.x - 5, mark.y - 5, 10, 10), v > 0.5f && sat < 0.5f ? Color.black : Color.white);
            TW.Fill(new Rect(hueBar.x - 2, hueBar.y + _hue * hueBar.height - 1, hueBar.width + 4, 3), Color.white);
            if (ev.type == EventType.MouseDown && ev.button == 0)
            {
                if (square.Contains(ev.mousePosition)) { _dragging = SquareId; ev.Use(); }
                else if (new Rect(hueBar.x - 4, hueBar.y, hueBar.width + 8, hueBar.height).Contains(ev.mousePosition)) { _dragging = HueId; ev.Use(); }
            }
            if ((_dragging == SquareId || _dragging == HueId) && (ev.type == EventType.MouseDrag || ev.type == EventType.MouseDown))
            {
                if (_dragging == SquareId)
                {
                    sat = Mathf.Clamp01((ev.mousePosition.x - square.x) / square.width);
                    v = 1 - Mathf.Clamp01((ev.mousePosition.y - square.y) / square.height);
                }
                else
                {
                    _hue = Mathf.Clamp01((ev.mousePosition.y - hueBar.y) / hueBar.height);
                }
                Color c = Color.HSVToRGB(_hue, sat, v);
                c.a = _value.a;
                Set(c);
                ev.Use();
            }
            else if ((_dragging == SquareId || _dragging == HueId) && ev.type == EventType.MouseUp)
            {
                _dragging = -1;
                Commit();
                ev.Use();
            }
            GUI.Label(new Rect(hueBar.xMax + 12, y, Mathf.Max(0, area.xMax - 8 - (hueBar.xMax + 12)), line), "saturation across, value down; hue on the bar", Hint(s));
            y += Square + 8;

            float[] values = { _value.r, _value.g, _value.b, _value.a };
            string[] names = { "R", "G", "B", "A" };
            for (int i = 0; i < 4; i++)
            {
                GUI.Label(new Rect(x, y, 20, line), names[i], s.Label);
                var bar = new Rect(x + 26, y + 8, w - 26 - 60, line - 16);
                float before = values[i];
                float after = Slider(i, bar, before, ev);
                GUI.Label(new Rect(bar.xMax + 6, y, 54, line), (i < 4 ? Mathf.RoundToInt(after * 255).ToString() : after.ToString("0.00", CultureInfo.InvariantCulture)), s.MutedLabel);
                if (after != before)
                {
                    values[i] = after;
                    Set(new Color(values[0], values[1], values[2], values[3]));
                }
                y += line;
            }
        }

        // One line, clipped: the window's label style wraps by default.
        private static GUIStyle _hint;
        private static GUIStyle Hint(ToolWindowStyles s)
        {
            return _hint ?? (_hint = new GUIStyle(s.MutedLabel) { wordWrap = false, clipping = TextClipping.Clip });
        }

        private static void Set(Color c)
        {
            _value = c;
            if (!_hexEditing)
            {
                _hex = ToHex(c);
            }
            try
            {
                _apply?.Invoke(c);
            }
            catch (Exception ex)
            {
                TW.ShowNotice("Colour not applied: " + ex.Message, NoticeKind.Error);
            }
        }

        // A bar with a handle, dragged with the mouse; the window's own texture only.
        private static float Slider(int id, Rect bar, float value, Event ev)
        {
            TW.Fill(bar, TW.InsetColor);
            TW.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(value), bar.height), TW.MutedColor);
            float hx = bar.x + bar.width * Mathf.Clamp01(value);
            TW.Fill(new Rect(hx - 2, bar.y - 4, 4, bar.height + 8), TW.AccentColor);
            var hit = new Rect(bar.x - 4, bar.y - 6, bar.width + 8, bar.height + 12);
            if (ev.type == EventType.MouseDown && ev.button == 0 && hit.Contains(ev.mousePosition))
            {
                _dragging = id;
                ev.Use();
            }
            if (_dragging == id)
            {
                if (ev.type == EventType.MouseDrag || ev.type == EventType.MouseDown)
                {
                    value = Mathf.Clamp01((ev.mousePosition.x - bar.x) / bar.width);
                    ev.Use();
                }
                else if (ev.type == EventType.MouseUp)
                {
                    _dragging = -1;
                    Commit();
                    ev.Use();
                }
            }
            return value;
        }

        private static void Outline(Rect r, Color color)
        {
            TW.Fill(new Rect(r.xMin, r.yMin, r.width, 1), color);
            TW.Fill(new Rect(r.xMin, r.yMax - 1, r.width, 1), color);
            TW.Fill(new Rect(r.xMin, r.yMin, 1, r.height), color);
            TW.Fill(new Rect(r.xMax - 1, r.yMin, 1, r.height), color);
        }

        internal static string ToHex(Color c)
        {
            Color32 b = c;
            return b.a == 255 ? $"#{b.r:X2}{b.g:X2}{b.b:X2}" : $"#{b.r:X2}{b.g:X2}{b.b:X2}{b.a:X2}";
        }

        internal static bool TryParseHex(string text, out Color color)
        {
            color = Color.white;
            string t = (text ?? "").Trim().TrimStart('#');
            if (t.Length != 6 && t.Length != 8)
            {
                return false;
            }
            if (!int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int n))
            {
                return false;
            }
            if (t.Length == 6)
            {
                color = new Color32((byte)(n >> 16), (byte)(n >> 8), (byte)n, 255);
            }
            else
            {
                color = new Color32((byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n);
            }
            return true;
        }
    }
}
