using System;
using System.Collections.Generic;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using DragNWash.ModFramework.ToolWindow;
using UnityEngine;

namespace DragNWash.ModFramework.Assets
{
    // The "Assets" tab of the Tool window: what textures are loaded, which
    // replacements mods shipped and where they clashed, and a button to apply
    // them again. Only referenced when the Tool window library is present.
    internal static class AssetsTab
    {
        private static IDisposable _tab;
        // One line per row: the window's label styles wrap, and a long texture
        // name in a narrow window ran into the rows below it.
        private static GUIStyle _cell, _mutedCell, _accentCell;

        private static void EnsureCells(ToolWindowStyles s)
        {
            if (_cell != null)
            {
                return;
            }
            _cell = new GUIStyle(s.Label) { wordWrap = false, clipping = TextClipping.Clip };
            _mutedCell = new GUIStyle(s.MutedLabel) { wordWrap = false, clipping = TextClipping.Clip };
            _accentCell = new GUIStyle(_cell);
            _accentCell.normal.textColor = TW.AccentColor;
            _accentCell.hover.textColor = TW.AccentColor;
        }
        private static List<TextureInfo> _textures;
        private static string _filter = "";
        private static Vector2 _scroll;
        private static string _status = "Press List to walk the loaded textures.";
        private static bool _showReplacements;

        // The Inspector's Go on a texture: list, filter to the name, open the tab.
        internal static void ShowTexture(string textureName)
        {
            _textures = AssetCatalog.Textures();
            _filter = textureName ?? "";
            _showReplacements = false;
            _scroll = Vector2.zero;
            _status = $"{_textures.Count} texture(s) loaded; showing \"{_filter}\".";
            TW.Open("Assets");
        }

        // The Inspector library, when it is loaded: a texture row's Inspect
        // button opens the texture in its Objects view.
        private static System.Reflection.MethodInfo _inspect;
        private static bool _inspectLookedUp;
        private static System.Reflection.MethodInfo InspectMethod()
        {
            if (!_inspectLookedUp)
            {
                _inspectLookedUp = true;
                Type type = Type.GetType("DragNWash.ModFramework.Inspector.Inspector, DragNWash.ModFramework.Inspector", false);
                _inspect = type?.GetMethod("Inspect", new[] { typeof(UnityEngine.Object) });
            }
            return _inspect;
        }

        public static void Install()
        {
            if (_tab != null)
            {
                return;
            }
            _tab = TW.AddTab(GameFonts.Guid, "Assets", Draw, 50);
            TW.AddCommand(GameFonts.Guid, "assets", "assets textures [filter] | assets replacements | assets apply | assets reload", Command,
                args => args.Length == 1 ? new[] { "textures", "replacements", "apply", "reload" } : new string[0]);
        }

        private static string Command(string[] args)
        {
            string what = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            switch (what)
            {
                case "textures":
                {
                    string filter = args.Length > 1 ? args[1] : null;
                    var lines = new List<string>();
                    foreach (TextureInfo t in AssetCatalog.Textures())
                    {
                        if (filter == null || t.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            lines.Add($"{t.Name}  {t.Width}x{t.Height} {t.Format}  {t.MaterialUsers} mat, {t.Sprites} sprite{(t.Replaced ? "  replacement" : "")}");
                        }
                    }
                    return lines.Count == 0 ? "No texture matches." : string.Join("\n", lines);
                }
                case "replacements":
                {
                    var lines = new List<string>();
                    foreach (TextureReplacement r in AssetReplacements.All)
                    {
                        lines.Add($"{r.Name}  from {r.Mod}" + (r.Language != null ? $" ({r.Language})" : "") + $"  in {r.Applied} place(s)" + (r.Overrides.Count > 0 ? "  overrides " + string.Join(", ", r.Overrides) : "") + (r.Problem != null ? "  NOT reloaded: " + r.Problem : ""));
                    }
                    if (AssetReplacements.PendingLanguage != null)
                    {
                        lines.Add($"Pictures for \"{AssetReplacements.PendingLanguage}\" apply after a restart (Direct3D 12).");
                    }
                    return lines.Count == 0 ? "No mod ships texture replacements." : string.Join("\n", lines);
                }
                case "apply":
                    return $"Replacements applied in {AssetReplacements.ApplyNow()} place(s).";
                case "reload":
                {
                    var lines = new List<string>();
                    foreach (ReloadResult r in AssetReplacements.ReloadFiles())
                    {
                        lines.Add($"{r.Name}: {r.Status}");
                    }
                    return string.Join("\n", lines);
                }
                default:
                    return "assets textures [filter] | assets replacements | assets apply | assets reload";
            }
        }

        private static void Draw(Rect area)
        {
            ToolWindowStyles s = TW.Styles;
            EnsureCells(s);
            float row = TW.RowHeight, pad = TW.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;

            if (GUI.Button(new Rect(x, y, 90, row), "List", s.Button))
            {
                _textures = AssetCatalog.Textures();
                _status = $"{_textures.Count} texture(s) loaded.";
                _selected = null;
            }
            if (GUI.Button(new Rect(x + 100, y, 170, row), "Apply replacements", s.Button))
            {
                int n = AssetReplacements.ApplyNow();
                _status = $"Replacements applied in {n} place(s).";
                _textures = null;
            }
            if (GUI.Button(new Rect(x + 280, y, 150, row), _showReplacements ? "Show textures" : "Show replacements", s.Button))
            {
                _showReplacements = !_showReplacements;
                _scroll = Vector2.zero;
            }
            bool wasEnabled = GUI.enabled;
            GUI.enabled = !AssetReplacements.ReloadDisabled;
            if (GUI.Button(new Rect(x + 440, y, 110, row), "Reload files", s.Button))
            {
                int n = 0, bad = 0;
                foreach (ReloadResult r in AssetReplacements.ReloadFiles())
                {
                    if (r.Status == "reloaded") n++;
                    else if (r.Status != "unchanged") bad++;
                }
                _status = $"Reloaded {n} file(s)" + (bad > 0 ? $", {bad} with problems (see Show replacements)" : "") + ".";
                _showReplacements = bad > 0 || _showReplacements;
                _textures = null;
            }
            GUI.enabled = wasEnabled;
            // The text field blends into the panel; an underline and a placeholder show where it is.
            var filterRect = new Rect(x + 560, y, Mathf.Max(80, w - 560), row);
            _filter = GUI.TextField(filterRect, _filter ?? "", s.TextField);
            Color was = GUI.color;
            GUI.color = TW.AccentColor;
            GUI.DrawTexture(new Rect(filterRect.x, filterRect.yMax - 2, filterRect.width, 2), Texture2D.whiteTexture);
            GUI.color = was;
            if (string.IsNullOrEmpty(_filter))
            {
                GUI.Label(new Rect(filterRect.x + 6, filterRect.y, filterRect.width - 6, row), "Filter by name", s.MutedLabel);
            }
            y += row + 8;

            string summary = $"{AssetReplacements.All.Count} replacement(s) from mods";
            if (AssetReplacements.ConflictCount > 0)
            {
                summary += $", {AssetReplacements.ConflictCount} overridden by another mod";
            }
            GUI.Label(new Rect(x, y, w, row), _status + "    |    " + summary, s.MutedLabel);
            y += row;
            string reloadNote = AssetReplacements.ReloadDisabled
                ? "Reload files: " + AssetReplacements.ReloadDisabledReason
                : GameFonts.RuntimeUploadsAreSafe
                    ? "Reload files re-reads changed PNGs and uploads them; Apply replacements only re-points materials and sprites."
                    : "Reload files uploads textures while the game runs, which can crash it on Direct3D 12; Apply replacements is always safe. Work with -force-d3d11 to reload freely.";
            // Wraps on narrow windows; take as many rows as it needs.
            float noteHeight = Mathf.Max(row, s.WrappedLabel.CalcHeight(new GUIContent(reloadNote), w));
            GUI.Label(new Rect(x, y, w, noteHeight), reloadNote, s.WrappedLabel);
            y += noteHeight + 4;

            var view = new Rect(x, y, w, area.yMax - pad - y);
            if (_showReplacements)
            {
                DrawReplacements(view, s, row);
            }
            else
            {
                // A selected texture gets a preview: beside the list where there
                // is room, above it in a narrow window.
                if (_selected != null && _selected.Texture)
                {
                    if (view.width >= 640)
                    {
                        float pw = Mathf.Clamp(view.width * 0.38f, 220, 420);
                        DrawPreview(new Rect(view.xMax - pw, view.y, pw, view.height), s, row);
                        view.width -= pw + 8;
                    }
                    else
                    {
                        float ph = Mathf.Min(220, view.height * 0.45f);
                        DrawPreview(new Rect(view.x, view.y, view.width, ph), s, row);
                        view.y += ph + 8;
                        view.height -= ph + 8;
                    }
                }
                DrawTextures(view, s, row);
            }
        }

        private static TextureInfo _selected;

        // The texture as it is on the GPU, scaled to fit, with its facts. Drawing
        // a loaded texture uploads nothing, so this is safe on Direct3D 12.
        private static void DrawPreview(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.PanelColor);
            TextureInfo t = _selected;
            float x = pane.x + 6, y = pane.y + 4, w = pane.width - 12;
            GUI.Label(new Rect(x, y, w - 60, row), t.Name, _cell);
            if (GUI.Button(new Rect(pane.xMax - 58, y + 2, 52, row - 4), "Close", s.Button))
            {
                _selected = null;
                return;
            }
            y += row;
            GUI.Label(new Rect(x, y, w, row), $"{t.Width}x{t.Height}  {t.Format}  {(t.Readable ? "readable" : "GPU only")}  {t.MaterialUsers} mat, {t.Sprites} sprite{(t.Replaced ? "  replacement" : "")}", _mutedCell);
            y += row;
            var box = new Rect(x, y, w, pane.yMax - y - 6);
            if (box.height < 24)
            {
                return;
            }
            // A dark and a light square behind it, so a transparent or dark image still reads.
            TW.Fill(box, new Color(0.18f, 0.2f, 0.24f));
            TW.Fill(new Rect(box.x, box.y, box.width / 2, box.height / 2), new Color(0.26f, 0.28f, 0.32f));
            TW.Fill(new Rect(box.x + box.width / 2, box.y + box.height / 2, box.width / 2, box.height / 2), new Color(0.26f, 0.28f, 0.32f));
            float scale = Mathf.Min(box.width / t.Width, box.height / t.Height);
            float dw = t.Width * scale, dh = t.Height * scale;
            var fit = new Rect(box.x + (box.width - dw) / 2, box.y + (box.height - dh) / 2, dw, dh);
            GUI.DrawTexture(fit, t.Texture, ScaleMode.StretchToFill, true);
        }

        private static void DrawTextures(Rect view, ToolWindowStyles s, float row)
        {
            if (_textures == null)
            {
                GUI.Label(view, "Nothing listed yet.", s.MutedLabel);
                return;
            }
            var shown = new List<TextureInfo>();
            foreach (TextureInfo t in _textures)
            {
                if (string.IsNullOrEmpty(_filter) || t.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    shown.Add(t);
                }
            }
            float inner = view.width - 20;
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, inner, Mathf.Max(view.height, shown.Count * row)), false, false);
            float ry = 0;
            foreach (TextureInfo t in shown)
            {
                if (ry + row >= _scroll.y && ry <= _scroll.y + view.height)
                {
                    bool selected = ReferenceEquals(t, _selected);
                    if (selected)
                    {
                        TW.Fill(new Rect(0, ry, inner, row), TW.PanelColor);
                    }
                    // The name is a button: it selects the texture for the preview.
                    if (GUI.Button(new Rect(0, ry, inner * 0.45f - 6, row), t.Name, selected ? _accentCell ?? _cell : _cell))
                    {
                        _selected = selected ? null : t;
                    }
                    GUI.Label(new Rect(inner * 0.45f, ry, inner * 0.2f - 6, row), $"{t.Width}x{t.Height} {t.Format}", _mutedCell);
                    GUI.Label(new Rect(inner * 0.65f, ry, inner * 0.2f - 6, row), $"{t.MaterialUsers} mat, {t.Sprites} sprite", _mutedCell);
                    if (t.Replaced)
                    {
                        GUI.Label(new Rect(inner * 0.85f, ry, inner * 0.15f - 70, row), "replacement", _mutedCell);
                    }
                    if (InspectMethod() != null && GUI.Button(new Rect(inner - 66, ry + 2, 66, row - 4), "Inspect", s.Button))
                    {
                        // The Inspector's Objects view, whose Used by lists its materials and sprites.
                        InspectMethod().Invoke(null, new object[] { t.Texture });
                    }
                }
                ry += row;
            }
            GUI.EndScrollView();
        }

        private static void DrawReplacements(Rect view, ToolWindowStyles s, float row)
        {
            var all = new List<TextureReplacement>(AssetReplacements.All);
            all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (all.Count == 0)
            {
                GUI.Label(view, "No mod ships texture replacements (BepInEx/plugins/<Mod>/assets/textures/<name>.png).", s.WrappedLabel);
                return;
            }
            float inner = view.width - 20;
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, inner, Mathf.Max(view.height, all.Count * row)), false, false);
            float ry = 0;
            foreach (TextureReplacement r in all)
            {
                GUI.Label(new Rect(0, ry, inner * 0.4f - 6, row), r.Name, _cell);
                GUI.Label(new Rect(inner * 0.4f, ry, inner * 0.3f - 6, row), r.Language != null ? $"{r.Mod} ({r.Language})" : r.Mod, _mutedCell);
                string note = $"{r.Texture.width}x{r.Texture.height}, in {r.Applied} place(s)";
                if (r.Overrides.Count > 0)
                {
                    note += " - overrides " + string.Join(", ", r.Overrides);
                }
                if (r.Problem != null)
                {
                    note = "NOT reloaded: " + r.Problem;
                }
                GUI.Label(new Rect(inner * 0.7f, ry, inner * 0.3f, row), note, _mutedCell);
                ry += row;
            }
            GUI.EndScrollView();
        }
    }
}
