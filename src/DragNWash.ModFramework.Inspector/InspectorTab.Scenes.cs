using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's Scenes and levels view: the level running and the
    // game's cheats for it, every level to start in its place, the scenes
    // loaded and every scene of the build to load. In place of the members,
    // like History. Experimental; see InspectorScenes.
    internal static partial class InspectorTab
    {
        // ---- scenes and levels --------------------------------------------------------------

        private static bool _showScenes;
        private static Vector2 _scrollScenes;
        private static string _scenesNote = "";
        // The level running and the game's cheats for it, every level to start
        // in its place, the scenes loaded and every scene of the build to load
        // (experimental; see InspectorScenes).
        private static void DrawScenes(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            float x = pane.x + 4, y = pane.y + 2, w = pane.width - 8;
            y = WrappedLine(InspectorScenes.Describe(), x, y, w, InspectorScenes.InLevel ? _accentCell : _mutedCell, row);
            float bx = x;
            if (InspectorScenes.InLevel)
            {
                if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Skip level"), "Skip level", false, s, row)) _scenesNote = InspectorScenes.Skip();
                if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Clean the dragon"), "Clean the dragon", false, s, row)) _scenesNote = InspectorScenes.Clean();
            }
            string active = InspectorScenes.ActiveScene;
            string reload = "Reload " + active;
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, reload), Drawable(reload), false, s, row)) _scenesNote = InspectorScenes.LoadScene(active);
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "< Members"), "< Members", false, s, row)) _showScenes = false;
            y += row + 4;
            if (!string.IsNullOrEmpty(_scenesNote))
            {
                y = WrappedLine(_scenesNote, x, y, w, _mutedCell, row);
            }
            if (InspectorScenes.InLevel)
            {
                y = WrappedLine("Start plays a level now as trial play: the game does not save until the title screen, so the save's progress and flags stay as they are.", x, y, w, _mutedCell, row);
            }

            // One scrolling list: the levels (in PlayGame), then the scenes.
            int levels = InspectorScenes.InLevel ? InspectorScenes.LevelCount : 0;
            int current = InspectorScenes.CurrentLevel;
            List<string> loaded = InspectorScenes.LoadedScenes();
            List<string> build = InspectorScenes.BuildScenes();
            var view = new Rect(x, y, w, pane.yMax - y - 2);
            float inner = view.width - 20;
            float total = (levels > 0 ? row * (levels + 2) : 0) + row * (loaded.Count + build.Count + 3);
            TW.ApplyScroll(view, ref _scrollScenes);
            _scrollScenes = GUI.BeginScrollView(view, _scrollScenes, new Rect(0, 0, inner, Mathf.Max(view.height, total)), false, false);
            float ry = 0;
            if (levels > 0)
            {
                GUI.Label(new Rect(0, ry, inner, row), "LEVELS", _mutedCell);
                ry += row;
                for (int i = 0; i < levels; i++)
                {
                    GUI.Label(new Rect(0, ry, inner - 84, row), Drawable($"{i + 1}. {InspectorScenes.DragonOf(i)}, {InspectorScenes.WeatherOf(i)}"), i == current ? _accentCell : _cell);
                    if (GUI.Button(new Rect(inner - 78, ry + 2, 74, row - 4), "Start", s.Button))
                    {
                        _scenesNote = InspectorScenes.StartLevel(i);
                    }
                    ry += row;
                }
                ry += row;
            }
            GUI.Label(new Rect(0, ry, inner, row), "SCENES LOADED", _mutedCell);
            ry += row;
            foreach (string line in loaded)
            {
                GUI.Label(new Rect(0, ry, inner, row), Drawable(line), _cell);
                ry += row;
            }
            ry += row;
            GUI.Label(new Rect(0, ry, inner, row), "SCENES OF THE GAME (Load goes through its loading screen; another scene is trial play)", _mutedCell);
            ry += row;
            foreach (string name in build)
            {
                GUI.Label(new Rect(0, ry, inner - 84, row), Drawable(name), name == active ? _accentCell : _cell);
                if (GUI.Button(new Rect(inner - 78, ry + 2, 74, row - 4), "Load", s.Button))
                {
                    _scenesNote = InspectorScenes.LoadScene(name);
                }
                ry += row;
            }
            GUI.EndScrollView();
        }
    }
}
