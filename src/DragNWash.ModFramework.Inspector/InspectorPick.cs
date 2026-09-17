using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector's view of the game itself: an outline around the selected
    // object, and a pick mode in which the next click in the game selects what
    // is under the pointer. Drawn from the plugin's OnGUI outside the window.
    //
    // No physics: an object is hit when the pointer is inside its renderers'
    // bounds projected to the screen (the smallest such rectangle wins), and
    // uGUI elements are asked through the EventSystem's raycasters first. So
    // decor without a collider is pickable too.
    internal static class InspectorPick
    {
        internal static bool Picking { get; private set; }
        internal static bool Highlight = true;

        private static readonly List<RaycastResult> UiHits = new List<RaycastResult>();
        // Pick mode over dense objects: everything under the pointer, smallest
        // first, and the mouse wheel walks through them.
        private static readonly List<GameObject> Candidates = new List<GameObject>();
        private static int _cycle;
        private static int _candidateKey;

        internal static void Begin()
        {
            Picking = true;
            _cycle = 0;
            TW.ShowNotice("Pick: click an object in the game; the mouse wheel walks through overlapping ones (Escape cancels).");
        }

        internal static void End()
        {
            if (Picking)
            {
                Picking = false;
                TW.ShowNotice(string.Empty);
            }
        }

        // From OnGUI, before the window is drawn, in screen coordinates (top-left origin).
        internal static void OnGUI(Rect window, GameObject selected)
        {
            Event ev = Event.current;
            if (Picking)
            {
                if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
                {
                    End();
                    ev.Use();
                    return;
                }
                if (ev.type == EventType.ScrollWheel && !window.Contains(ev.mousePosition))
                {
                    Refresh(ev.mousePosition);
                    if (Candidates.Count > 1)
                    {
                        _cycle = (_cycle + (ev.delta.y > 0 ? 1 : Candidates.Count - 1)) % Candidates.Count;
                    }
                    ev.Use();
                    return;
                }
                if (ev.type == EventType.MouseDown && ev.button == 0 && !window.Contains(ev.mousePosition))
                {
                    Refresh(ev.mousePosition);
                    GameObject hit = Candidates.Count > 0 ? Candidates[Mathf.Clamp(_cycle, 0, Candidates.Count - 1)] : null;
                    End();
                    if (hit != null)
                    {
                        InspectorTab.Select(hit);
                        TW.ShowNotice($"Picked {hit.name}.");
                    }
                    else
                    {
                        TW.ShowNotice("Nothing under the pointer.");
                    }
                    ev.Use();
                    return;
                }
                if (ev.type == EventType.Repaint)
                {
                    // What would be picked, outlined and named as the pointer moves.
                    Refresh(ev.mousePosition);
                    if (Candidates.Count > 0)
                    {
                        _cycle = Mathf.Clamp(_cycle, 0, Candidates.Count - 1);
                        GameObject hover = Candidates[_cycle];
                        if (hover != null && ScreenRect(hover, out Rect r))
                        {
                            Outline(r, TW.WarningColor, 2);
                            string text = Candidates.Count > 1 ? $"{hover.name}  ({_cycle + 1}/{Candidates.Count}, wheel)" : hover.name;
                            Tag(r, text, TW.WarningColor);
                        }
                    }
                }
            }
            // The selection's outline and name; while picking, the hovered
            // candidate's tag already names it, so only the outline is kept.
            if (Highlight && selected != null && ev.type == EventType.Repaint && ScreenRect(selected, out Rect rect))
            {
                Outline(rect, TW.AccentColor, 2);
                if (!Picking)
                {
                    Tag(rect, selected.name, TW.AccentColor);
                }
            }
        }

        // The object's name at the outline's top-left corner, on a dark strip,
        // kept on screen.
        private static GUIStyle _tagStyle;

        private static void Tag(Rect outline, string text, Color color)
        {
            if (TW.Styles.Label == null)
            {
                return;
            }
            // One line, never wrapped or clipped: the strip is sized from the text.
            if (_tagStyle == null)
            {
                _tagStyle = new GUIStyle(TW.Styles.Label) { wordWrap = false, clipping = TextClipping.Overflow, alignment = TextAnchor.MiddleLeft };
            }
            GUIStyle style = _tagStyle;
            var content = new GUIContent(TW.Drawable(text));
            Vector2 size = style.CalcSize(content);
            size.x += 4;
            float x = Mathf.Clamp(outline.xMin, 0, Screen.width - size.x - 8);
            float y = outline.yMin - size.y - 4;
            if (y < 0)
            {
                y = Mathf.Min(outline.yMin + 2, Screen.height - size.y - 4);
            }
            var strip = new Rect(x, y, size.x + 8, size.y + 2);
            TW.Fill(strip, new Color(0.06f, 0.06f, 0.08f, 0.9f));
            TW.Fill(new Rect(strip.x, strip.yMax - 2, strip.width, 2), color);
            GUI.Label(new Rect(strip.x + 4, strip.y, size.x, size.y), content, style);
        }

        // Everything under the pointer, smallest first; the cycle index is kept
        // while the set stays the same and reset when it changes.
        private static void Refresh(Vector2 guiPoint)
        {
            Candidates.Clear();
            var screen = new Vector2(guiPoint.x, Screen.height - guiPoint.y);
            EventSystem es = EventSystem.current;
            if (es != null)
            {
                var data = new PointerEventData(es) { position = screen };
                UiHits.Clear();
                es.RaycastAll(data, UiHits);
                foreach (RaycastResult r in UiHits)
                {
                    if (r.gameObject != null && r.gameObject.transform.root.gameObject.name != "BepInEx_Manager" && !Candidates.Contains(r.gameObject))
                    {
                        Candidates.Add(r.gameObject);
                    }
                }
            }
            Camera cam = Camera.main;
            if (cam != null)
            {
                var hits = new List<KeyValuePair<float, GameObject>>();
                foreach (Renderer rend in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (rend == null || !rend.enabled || !rend.gameObject.activeInHierarchy || !rend.isVisible)
                    {
                        continue;
                    }
                    if (Project(cam, rend.bounds, out Rect r) && r.Contains(guiPoint))
                    {
                        hits.Add(new KeyValuePair<float, GameObject>(r.width * r.height, rend.gameObject));
                    }
                }
                hits.Sort((a, b) => a.Key.CompareTo(b.Key));
                foreach (KeyValuePair<float, GameObject> h in hits)
                {
                    if (!Candidates.Contains(h.Value))
                    {
                        Candidates.Add(h.Value);
                    }
                }
            }
            int key = Candidates.Count;
            foreach (GameObject c in Candidates)
            {
                key = key * 31 + c.GetInstanceID();
            }
            if (key != _candidateKey)
            {
                _candidateKey = key;
                _cycle = 0;
            }
        }

        // The screen rectangle around an object: its RectTransform corners, or
        // the union of its renderers' projected bounds.
        internal static bool ScreenRect(GameObject go, out Rect rect)
        {
            rect = default;
            if (go == null)
            {
                return false;
            }
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Canvas canvas = go.GetComponentInParent<Canvas>();
                Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? (canvas.worldCamera ?? Camera.main) : null;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (Vector3 c in corners)
                {
                    Vector3 s = uiCam != null ? uiCam.WorldToScreenPoint(c) : c;
                    if (uiCam != null && s.z < 0) return false;
                    minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
                    minY = Mathf.Min(minY, s.y); maxY = Mathf.Max(maxY, s.y);
                }
                rect = Rect.MinMaxRect(minX, Screen.height - maxY, maxX, Screen.height - minY);
                return rect.width > 0 && rect.height > 0;
            }
            Camera cam = Camera.main;
            if (cam == null)
            {
                return false;
            }
            bool any = false;
            Rect union = default;
            foreach (Renderer rend in go.GetComponentsInChildren<Renderer>())
            {
                if (rend == null || !rend.enabled || rend.GetType().Name == "ParticleSystemRenderer" || rend.GetType().Name == "TrailRenderer")
                {
                    continue;
                }
                Rect r;
                if (rend is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                {
                    // A skinned renderer's bounds are the loose box Unity keeps for
                    // culling, often far larger than the pose; the baked mesh is tight.
                    skin.BakeMesh(BakedForBounds);
                    Bounds local = BakedForBounds.bounds;
                    if (!ProjectLocal(cam, local, Matrix4x4.TRS(rend.transform.position, rend.transform.rotation, Vector3.one), out r))
                    {
                        continue;
                    }
                }
                else if (!Project(cam, rend.bounds, out r))
                {
                    continue;
                }
                union = any ? Rect.MinMaxRect(Mathf.Min(union.xMin, r.xMin), Mathf.Min(union.yMin, r.yMin), Mathf.Max(union.xMax, r.xMax), Mathf.Max(union.yMax, r.yMax)) : r;
                any = true;
            }
            rect = union;
            return any;
        }

        private static readonly Mesh BakedForBounds = new Mesh();

        // Local bounds under a transform to a screen rectangle.
        private static bool ProjectLocal(Camera cam, Bounds b, Matrix4x4 toWorld, out Rect rect)
        {
            rect = default;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z));
                Vector3 s = cam.WorldToScreenPoint(toWorld.MultiplyPoint3x4(corner));
                if (s.z < 0)
                {
                    return false;
                }
                minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
                minY = Mathf.Min(minY, s.y); maxY = Mathf.Max(maxY, s.y);
            }
            rect = Rect.MinMaxRect(minX, Screen.height - maxY, maxX, Screen.height - minY);
            return rect.width > 0 && rect.height > 0;
        }

        // Bounds to a screen rectangle (top-left origin); false when behind the camera.
        private static bool Project(Camera cam, Bounds b, out Rect rect)
        {
            rect = default;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z));
                Vector3 s = cam.WorldToScreenPoint(corner);
                if (s.z < 0)
                {
                    return false;
                }
                minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
                minY = Mathf.Min(minY, s.y); maxY = Mathf.Max(maxY, s.y);
            }
            rect = Rect.MinMaxRect(minX, Screen.height - maxY, maxX, Screen.height - minY);
            return rect.width > 0 && rect.height > 0 && rect.xMax > 0 && rect.yMax > 0 && rect.xMin < Screen.width && rect.yMin < Screen.height;
        }

        private static void Outline(Rect r, Color color, float thickness)
        {
            TW.Fill(new Rect(r.xMin, r.yMin, r.width, thickness), color);
            TW.Fill(new Rect(r.xMin, r.yMax - thickness, r.width, thickness), color);
            TW.Fill(new Rect(r.xMin, r.yMin, thickness, r.height), color);
            TW.Fill(new Rect(r.xMax - thickness, r.yMin, thickness, r.height), color);
        }
    }
}
