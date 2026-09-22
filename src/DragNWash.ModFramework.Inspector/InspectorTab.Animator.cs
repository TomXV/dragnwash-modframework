using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's Animator controls: pause/resume/step on a selected
    // Animator, its layers with per-layer time sliders while paused, and its
    // clips with Preview and Replace. Parameters and layer weights are rows
    // among the members; the buttons and lists here stand in for them.
    internal static partial class InspectorTab
    {
        private static string _animatorNote = "";
        private static bool _showClips;
        private static UnityEngine.Object _swapFrom;
        private static string _clipsFilter = "";
        private static Vector2 _scrollClips;

        private static bool _showLayers;
        private static Vector2 _scrollLayers;

        private static bool ShowingLayers => _showLayers && !_showClips && _target is Component c && InspectorAnimators.IsAnimator(c);

        // Pause or Resume, and Step while paused.
        private static void AnimatorButtons(Component animator, ref float bx, ref float y, float x, float w, ToolWindowStyles s, float row)
        {
            bool paused = InspectorAnimators.IsPaused(animator);
            string pause = paused ? "Resume animation" : "Pause animation";
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, pause), pause, paused, s, row))
            {
                _animatorNote = paused ? InspectorAnimators.Resume(animator) : InspectorAnimators.Pause(animator);
            }
            if (InspectorAnimators.IsPaused(animator) && FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Step"), "Step", false, s, row))
            {
                _animatorNote = InspectorAnimators.Step(animator);
            }
        }

        // Every layer of the animator, what it plays, and while paused a time
        // slider each, in place of the members.
        private static void DrawLayers(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            var animator = (Component)_target;
            float x = pane.x + 4, y = pane.y + 2, w = pane.width - 8;
            bool paused = InspectorAnimators.IsPaused(animator);
            int layers = InspectorAnimators.LayerCount(animator);
            GUI.Label(new Rect(x, y, w, row), Drawable($"Layers of {InspectorAnimators.ControllerName(animator)}: {layers}{(paused ? ". Drag a slider to put a layer's state at that point." : ". Pause to move them by hand.")}"), _mutedCell);
            y += row;
            float bx = x;
            AnimatorButtons(animator, ref bx, ref y, x, w, s, row);
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "< Members"), "< Members", false, s, row)) _showLayers = false;
            y += row + 4;
            if (!string.IsNullOrEmpty(_animatorNote))
            {
                GUI.Label(new Rect(x, y, w, row), Drawable(_animatorNote), _mutedCell);
                y += row;
            }
            var view = new Rect(x, y, w, pane.yMax - y - 2);
            float inner = view.width - 20;
            float lineH = paused ? row * 2 : row;
            TW.ApplyScroll(view, ref _scrollLayers);
            _scrollLayers = GUI.BeginScrollView(view, _scrollLayers, new Rect(0, 0, inner, Mathf.Max(view.height, layers * lineH)), false, false);
            float ry = 0;
            for (int layer = 0; layer < layers; layer++)
            {
                if (ry + lineH >= _scrollLayers.y && ry <= _scrollLayers.y + view.height)
                {
                    GUI.Label(new Rect(0, ry, inner, row), Drawable(InspectorAnimators.Describe(animator, layer)), _cell);
                    if (paused)
                    {
                        float t = InspectorAnimators.NormalizedTime(animator, layer);
                        float pass = t - Mathf.Floor(t);
                        float next = GUI.HorizontalSlider(new Rect(8, ry + row + row / 2 - 6, inner - 16, 12), pass, 0f, 0.999f);
                        if (Mathf.Abs(next - pass) > 0.0005f)
                        {
                            InspectorAnimators.SetTime(animator, layer, Mathf.Floor(t) + next);
                        }
                    }
                }
                ry += lineH;
            }
            GUI.EndScrollView();
        }

        private static bool ShowingClips => _showClips && _target is Component c && InspectorAnimators.IsAnimator(c);

        // The clip being previewed on this animator: its time, a slider, pause and stop.
        private static float DrawPreview(Component animator, float x, float y, float w, ToolWindowStyles s, float row)
        {
            if (!InspectorAnimators.Previewing(animator))
            {
                return y;
            }
            UnityEngine.Object clip = InspectorAnimators.PreviewClip;
            float length = InspectorAnimators.ClipLength(clip);
            float t = InspectorAnimators.PreviewTime;
            GUI.Label(new Rect(x, y, w, row), Drawable($"Previewing {(clip != null ? clip.name : "?")}: {t:0.00} / {length:0.00} s"), _accentCell);
            y += row;
            if (length > 0)
            {
                float next = GUI.HorizontalSlider(new Rect(x + 8, y + row / 2 - 6, w - 16, 12), t, 0f, length);
                if (Mathf.Abs(next - t) > 0.001f)
                {
                    InspectorAnimators.SetPreviewPaused(true);
                    InspectorAnimators.PreviewTime = next;
                }
                y += row;
            }
            float bx = x;
            string play = InspectorAnimators.PreviewPaused ? "Play preview" : "Pause preview";
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, play), play, InspectorAnimators.PreviewPaused, s, row))
            {
                InspectorAnimators.SetPreviewPaused(!InspectorAnimators.PreviewPaused);
            }
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Stop preview"), "Stop preview", false, s, row))
            {
                InspectorAnimators.StopPreview();
                _animatorNote = "Preview stopped.";
            }
            return y + row + 4;
        }

        // The controller's clips (the game's, with what each is swapped for),
        // each with Preview and Replace; Replace lists every clip loaded, to
        // pick the one to play instead.
        private static void DrawClips(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            var animator = (Component)_target;
            float x = pane.x + 4, y = pane.y + 2, w = pane.width - 8;
            y = DrawPreview(animator, x, y, w, s, row);
            List<UnityEngine.Object> clips = _swapFrom != null ? InspectorAnimators.LoadedClips() : InspectorAnimators.ClipsOf(animator);
            var shown = new List<UnityEngine.Object>();
            foreach (UnityEngine.Object c in clips)
            {
                if (string.IsNullOrEmpty(_clipsFilter) || c.name.IndexOf(_clipsFilter, StringComparison.OrdinalIgnoreCase) >= 0) shown.Add(c);
            }
            string head = _swapFrom != null
                ? $"Play {_swapFrom.name} as: {shown.Count} of {clips.Count} clips loaded."
                : $"Clips of {InspectorAnimators.ControllerName(animator)}: {shown.Count} of {clips.Count}.";
            GUI.Label(new Rect(x, y, w, row), Drawable(head), _mutedCell);
            y += row;
            float bx = x;
            if (_swapFrom != null)
            {
                if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Cancel"), "Cancel", false, s, row)) _swapFrom = null;
                if (_swapFrom != null && InspectorAnimators.SwappedFor(animator, _swapFrom) != null && FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "The game's clip"), "The game's clip", false, s, row))
                {
                    _animatorNote = InspectorAnimators.Swap(animator, _swapFrom, null, WhereLabel());
                    _swapFrom = null;
                }
            }
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "< Members"), "< Members", false, s, row))
            {
                _showClips = false;
                _swapFrom = null;
            }
            y += row + 4;
            var filterRect = new Rect(x, y, w, row);
            _clipsFilter = TW.FilterField(filterRect, _clipsFilter, "Filter by name", s);
            y += row + 4;
            if (!string.IsNullOrEmpty(_animatorNote))
            {
                GUI.Label(new Rect(x, y, w, row), Drawable(_animatorNote), _mutedCell);
                y += row;
            }
            var view = new Rect(x, y, w, pane.yMax - y - 2);
            float inner = view.width - 20;
            float lineH = row * 2;
            TW.ApplyScroll(view, ref _scrollClips);
            _scrollClips = GUI.BeginScrollView(view, _scrollClips, new Rect(0, 0, inner, Mathf.Max(view.height, shown.Count * lineH)), false, false);
            float ry = 0;
            foreach (UnityEngine.Object clip in shown)
            {
                if (ry + lineH >= _scrollClips.y && ry <= _scrollClips.y + view.height)
                {
                    UnityEngine.Object swapped = _swapFrom == null ? InspectorAnimators.SwappedFor(animator, clip) : null;
                    bool previewing = InspectorAnimators.Previewing(animator) && InspectorAnimators.PreviewClip == clip;
                    float buttons = _swapFrom != null ? 78 : 166;
                    GUI.Label(new Rect(0, ry, inner - buttons, row), Drawable(clip.name + (swapped != null ? "  ->  " + swapped.name : "")), swapped != null || previewing ? _accentCell : _cell);
                    GUI.Label(new Rect(0, ry + row, inner - buttons, row), Drawable(InspectorAnimators.DescribeClip(clip)), _mutedCell);
                    if (_swapFrom != null)
                    {
                        if (GUI.Button(new Rect(inner - 78, ry + 2, 74, row - 4), "Use", s.Button))
                        {
                            _animatorNote = InspectorAnimators.Swap(animator, _swapFrom, clip, WhereLabel());
                            _swapFrom = null;
                        }
                    }
                    else
                    {
                        if (GUI.Button(new Rect(inner - 166, ry + 2, 80, row - 4), previewing ? "Stop" : "Preview", previewing ? s.SelectedButton : s.Button))
                        {
                            if (previewing) InspectorAnimators.StopPreview();
                            else _animatorNote = InspectorAnimators.Preview(animator, clip);
                        }
                        if (GUI.Button(new Rect(inner - 82, ry + 2, 78, row - 4), "Replace", s.Button))
                        {
                            _swapFrom = clip;
                            _clipsFilter = "";
                            _scrollClips = Vector2.zero;
                        }
                    }
                }
                ry += lineH;
            }
            GUI.EndScrollView();
        }

        // A selected Animator: its controller, what each layer plays, and a
        // pause with a step and, while paused, a time slider per layer. Its
        // parameters and layer weights are rows among the members below.
        private static float DrawAnimatorControls(Component animator, float x, float y, float w, ToolWindowStyles s, float row)
        {
            bool paused = InspectorAnimators.IsPaused(animator);
            GUI.Label(new Rect(x, y, w, row), Drawable($"{InspectorAnimators.ControllerName(animator)}, speed {(paused ? "0 (paused)" : InspectorAnimators.Speed(animator).ToString("0.##"))}"), _accentCell);
            y += row;
            // The base layer only; every layer, with its time slider, is in Layers.
            int layers = InspectorAnimators.LayerCount(animator);
            if (layers > 0)
            {
                GUI.Label(new Rect(x, y, w, row), Drawable(InspectorAnimators.Describe(animator, 0)), _mutedCell);
                y += row;
            }
            float bx = x;
            AnimatorButtons(animator, ref bx, ref y, x, w, s, row);
            string layersLabel = $"Layers ({layers})";
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, layersLabel), layersLabel, false, s, row))
            {
                _showLayers = true;
                _showClips = false;
            }
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Clips"), "Clips", false, s, row))
            {
                _showClips = true;
                _showLayers = false;
                _swapFrom = null;
            }
            y += row;
            y = DrawPreview(animator, x, y, w, s, row);
            if (!string.IsNullOrEmpty(_animatorNote))
            {
                GUI.Label(new Rect(x, y, w, row), Drawable(_animatorNote), _mutedCell);
                y += row;
            }
            return y + 4;
        }
    }
}
