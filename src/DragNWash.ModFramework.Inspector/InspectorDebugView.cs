using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;

namespace DragNWash.ModFramework.Inspector
{
    // The debug view: everything of a kind outlined over the game at once, the
    // way a debug build shows its guts. What is shown is a choice (every
    // renderer the camera sees, the selection's children, or what matches the
    // search text by name or component type), colliders and lights can be
    // added (they have no look of their own), and each entry is drawn as a
    // screen rectangle, a 3D box, or both, coloured by kind, with names on the
    // ones near the pointer when there are many.
    internal static class InspectorDebugView
    {
        internal enum Scope { Off, Visible, Children, Filter }

        internal static Scope Mode = Scope.Off;
        internal static bool Colliders;
        internal static bool Lights;
        // Colliders and lights of the selection and its children only, rather than the whole scene.
        internal static bool SelectionOnly = true;
        internal static bool Rects = true;
        internal static bool Boxes;
        // Colliders and lights in their own shape rather than as a box around them.
        internal static bool Shapes = true;
        internal static bool Names = true;
        internal static string Filter = "";

        private enum Kind { Renderer, Ui, Collider, Trigger, Light }

        private sealed class Entry
        {
            public GameObject Object;
            public Kind Kind;
            public Bounds World;        // for boxes; Renderer and Collider entries
            public bool HasWorld;
            public string Label;
            public Component Collider;  // for the true shape of a collider
            public Light Light;         // for the reach of a light
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static float _gatheredAt = -1f;
        private const int Cap = 400;
        private const float Refresh = 0.25f;

        private static readonly Color RendererColor = new Color(0.35f, 0.85f, 1f, 0.8f);
        private static readonly Color UiColor = new Color(0.95f, 0.5f, 0.9f, 0.85f);
        private static readonly Color ColliderColor = new Color(0.45f, 0.95f, 0.45f, 0.85f);
        private static readonly Color TriggerColor = new Color(0.98f, 0.85f, 0.3f, 0.85f);
        private static readonly Color LightColor = new Color(1f, 0.6f, 0.25f, 0.9f);

        internal static bool Active => Mode != Scope.Off || Colliders || Lights;

        internal static string Status()
        {
            if (!Active) return "";
            string what = Mode == Scope.Visible ? "everything the camera sees" : Mode == Scope.Children ? "the selection's children" : Mode == Scope.Filter ? $"\"{Filter}\" by name or component" : "nothing";
            string where = SelectionOnly ? " of the selection" : " in the scene";
            string extras = Colliders || Lights ? (Colliders ? " + colliders" : "") + (Lights ? " + lights" : "") + where : "";
            return $"Debug view: {what}{extras}, {Entries.Count} shown{(Entries.Count >= Cap ? " (capped)" : "")}.";
        }

        // ---- gathering -------------------------------------------------------------

        private static void Gather(GameObject selected)
        {
            if (Time.realtimeSinceStartup - _gatheredAt < Refresh)
            {
                return;
            }
            _gatheredAt = Time.realtimeSinceStartup;
            Entries.Clear();
            var seen = new HashSet<int>();
            switch (Mode)
            {
                case Scope.Visible:
                    foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        if (r == null || !r.enabled || !r.isVisible || r.GetType().Name == "ParticleSystemRenderer") continue;
                        AddRenderer(r, seen);
                        if (Entries.Count >= Cap) break;
                    }
                    break;
                case Scope.Children:
                    if (selected != null)
                    {
                        foreach (Transform t in selected.GetComponentsInChildren<Transform>())
                        {
                            AddObject(t.gameObject, seen);
                            if (Entries.Count >= Cap) break;
                        }
                    }
                    break;
                case Scope.Filter:
                    if (!string.IsNullOrEmpty(Filter))
                    {
                        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
                        {
                            if (t == null || !t.gameObject.scene.IsValid() || t.hideFlags != HideFlags.None) continue;
                            bool match = t.name.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!match)
                            {
                                foreach (Component c in t.GetComponents<Component>())
                                {
                                    if (c != null && c.GetType().Name.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
                                }
                            }
                            if (match) AddObject(t.gameObject, seen);
                            if (Entries.Count >= Cap) break;
                        }
                    }
                    break;
            }
            if (Colliders)
            {
                // Colliders live in the physics module, which this library does not
                // reference: found by name, their bounds and trigger flag read by reflection.
                // A mesh collider's mesh is read back from the GPU for its shape,
                // one mesh a round so a scene full of them does not stall a frame.
                bool read = !Shapes;
                foreach (Component c in CollidersIn(selected))
                {
                    // Only colliders the physics engine is using: an inactive
                    // object's or a switched-off collider is not in the scene's physics.
                    if (!c.gameObject.activeInHierarchy) continue;
                    if (c is Behaviour b && !b.enabled) continue;
                    PropertyInfo enabledProp = c.GetType().GetProperty("enabled");
                    if (enabledProp != null && enabledProp.GetValue(c, null) is bool en && !en) continue;
                    if (!read) read = InspectorColliderShape.Prepare(c);
                    PropertyInfo boundsProp = c.GetType().GetProperty("bounds");
                    if (boundsProp == null) continue;
                    bool trigger = c.GetType().GetProperty("isTrigger")?.GetValue(c, null) is bool tr && tr;
                    // A MeshCollider can be told to act as its convex hull.
                    bool convex = c.GetType().GetProperty("convex")?.GetValue(c, null) is bool cv && cv;
                    Entries.Add(new Entry { Object = c.gameObject, Kind = trigger ? Kind.Trigger : Kind.Collider, World = (Bounds)boundsProp.GetValue(c, null), HasWorld = true, Collider = c, Label = c.gameObject.name + " (" + c.GetType().Name + (convex ? ", convex" : "") + (trigger ? ", trigger" : "") + ")" });
                    if (Entries.Count >= Cap) break;
                }
            }
            if (Lights)
            {
                foreach (Light l in SelectionOnly && selected != null ? selected.GetComponentsInChildren<Light>(true) : UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l == null || !l.enabled) continue;
                    float r = l.type == LightType.Directional ? 0.5f : Mathf.Max(0.2f, l.range);
                    Entries.Add(new Entry { Object = l.gameObject, Kind = Kind.Light, World = new Bounds(l.transform.position, Vector3.one * r * 2), HasWorld = true, Light = l, Label = $"{l.gameObject.name} ({l.type} light, range {l.range:0.#})" });
                    if (Entries.Count >= Cap) break;
                }
            }
        }

        // Every Collider component, scene-wide or under the selection. The type
        // is not referenced, so they are recognised by name.
        private static IEnumerable<Component> CollidersIn(GameObject selected)
        {
            Component[] all = SelectionOnly && selected != null
                ? selected.GetComponentsInChildren<Component>(true)
                : UnityEngine.Object.FindObjectsByType<Component>(FindObjectsSortMode.None);
            foreach (Component c in all)
            {
                if (c != null && c.GetType().Name.EndsWith("Collider", StringComparison.Ordinal))
                {
                    yield return c;
                }
            }
        }

        private static void AddRenderer(Renderer r, HashSet<int> seen)
        {
            if (!seen.Add(r.gameObject.GetInstanceID())) return;
            Entries.Add(new Entry { Object = r.gameObject, Kind = Kind.Renderer, World = r.bounds, HasWorld = true, Label = r.gameObject.name });
        }

        private static void AddObject(GameObject go, HashSet<int> seen)
        {
            if (!seen.Add(go.GetInstanceID())) return;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null)
            {
                Entries.Add(new Entry { Object = go, Kind = Kind.Renderer, World = r.bounds, HasWorld = true, Label = go.name });
            }
            else if (go.GetComponent<RectTransform>() != null)
            {
                Entries.Add(new Entry { Object = go, Kind = Kind.Ui, Label = go.name });
            }
            else
            {
                Entries.Add(new Entry { Object = go, Kind = Kind.Renderer, World = new Bounds(go.transform.position, Vector3.one * 0.1f), HasWorld = true, Label = go.name });
            }
        }

        // ---- drawing ---------------------------------------------------------------

        internal static void OnGUI(Rect window, GameObject selected)
        {
            if (!Active)
            {
                Entries.Clear();
                return;
            }
            Event ev = Event.current;
            if (ev.type != EventType.Repaint)
            {
                return;
            }
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            Gather(selected);
            var rects = new List<KeyValuePair<Entry, Rect>>();
            foreach (Entry e in Entries)
            {
                if (e.Object == null) continue;
                Rect r;
                if (e.Kind == Kind.Ui)
                {
                    if (!InspectorPick.ScreenRect(e.Object, out r)) continue;
                }
                else if (!Project(cam, e.World, out r))
                {
                    continue;
                }
                rects.Add(new KeyValuePair<Entry, Rect>(e, r));
            }
            if ((Boxes || Shapes) && InspectorGizmo.Lines != null)
            {
                InspectorGizmo.Lines.SetPass(0);
                GL.PushMatrix();
                GL.LoadPixelMatrix();
                GL.Begin(GL.LINES);
                foreach (KeyValuePair<Entry, Rect> kv in rects)
                {
                    Entry e = kv.Key;
                    GL.Color(ColorOf(e.Kind));
                    // A collider knows its own form (box, sphere, capsule,
                    // mesh) and a light its reach; the bounds box stands in for
                    // the rest, and for a shape that cannot be read.
                    bool shaped = false;
                    if (Shapes)
                    {
                        if (e.Collider != null) shaped = InspectorColliderShape.Draw(cam, e.Collider);
                        else if (e.Light != null) { InspectorColliderShape.DrawLight(cam, e.Light); shaped = true; }
                    }
                    bool fallback = Shapes && !shaped && e.Collider != null;
                    if (e.HasWorld && (Boxes || fallback)) Box(cam, e.World);
                }
                GL.End();
                GL.PopMatrix();
            }
            bool nameAll = rects.Count <= 12 || !Names;
            foreach (KeyValuePair<Entry, Rect> kv in rects)
            {
                Color c = ColorOf(kv.Key.Kind);
                if (Rects || !kv.Key.HasWorld)
                {
                    Outline(kv.Value, c, 1);
                }
                if (Names && (nameAll || kv.Value.Contains(ev.mousePosition) || Vector2.Distance(ev.mousePosition, kv.Value.center) < 40))
                {
                    string label = kv.Key.Label;
                    if (Shapes && kv.Key.Collider != null && InspectorColliderShape.IsScanned(kv.Key.Collider))
                    {
                        // Simulated with rays: the real shape may differ.
                        label += "  - shape estimated by ray scan, may differ from the real one";
                    }
                    Tag(kv.Value, label, c);
                }
            }
        }

        private static Color ColorOf(Kind k)
        {
            switch (k)
            {
                case Kind.Ui: return UiColor;
                case Kind.Collider: return ColliderColor;
                case Kind.Trigger: return TriggerColor;
                case Kind.Light: return LightColor;
                default: return RendererColor;
            }
        }

        private static bool Project(Camera cam, Bounds b, out Rect rect)
        {
            rect = default;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 s = cam.WorldToScreenPoint(new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z)));
                if (s.z < 0) return false;
                minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
                minY = Mathf.Min(minY, s.y); maxY = Mathf.Max(maxY, s.y);
            }
            rect = Rect.MinMaxRect(minX, Screen.height - maxY, maxX, Screen.height - minY);
            return rect.width > 0 && rect.height > 0 && rect.xMax > 0 && rect.yMax > 0 && rect.xMin < Screen.width && rect.yMin < Screen.height;
        }

        private static void Box(Camera cam, Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                p[i] = cam.WorldToScreenPoint(new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z)));
            }
            int[][] edges = { new[] { 0, 1 }, new[] { 1, 3 }, new[] { 3, 2 }, new[] { 2, 0 }, new[] { 4, 5 }, new[] { 5, 7 }, new[] { 7, 6 }, new[] { 6, 4 }, new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 } };
            foreach (int[] ed in edges)
            {
                if (p[ed[0]].z <= 0 || p[ed[1]].z <= 0) continue;
                GL.Vertex3(p[ed[0]].x, Screen.height - p[ed[0]].y, 0);
                GL.Vertex3(p[ed[1]].x, Screen.height - p[ed[1]].y, 0);
            }
        }

        private static void Outline(Rect r, Color color, float thickness)
        {
            TW.Fill(new Rect(r.xMin, r.yMin, r.width, thickness), color);
            TW.Fill(new Rect(r.xMin, r.yMax - thickness, r.width, thickness), color);
            TW.Fill(new Rect(r.xMin, r.yMin, thickness, r.height), color);
            TW.Fill(new Rect(r.xMax - thickness, r.yMin, thickness, r.height), color);
        }

        private static GUIStyle _tagStyle;

        private static void Tag(Rect outline, string text, Color color)
        {
            if (TW.Styles.Label == null) return;
            if (_tagStyle == null)
            {
                _tagStyle = new GUIStyle(TW.Styles.Label) { wordWrap = false, clipping = TextClipping.Overflow, alignment = TextAnchor.MiddleLeft, fontSize = Mathf.Max(10, TW.Styles.Label.fontSize - 2) };
            }
            var content = new GUIContent(TW.Drawable(text));
            Vector2 size = _tagStyle.CalcSize(content);
            float x = Mathf.Clamp(outline.xMin, 0, Screen.width - size.x - 8);
            float y = outline.yMin - size.y - 2;
            if (y < 0) y = Mathf.Min(outline.yMin + 2, Screen.height - size.y - 4);
            var strip = new Rect(x, y, size.x + 8, size.y + 2);
            TW.Fill(strip, new Color(0.06f, 0.06f, 0.08f, 0.85f));
            TW.Fill(new Rect(strip.x, strip.yMax - 2, strip.width, 2), color);
            GUI.Label(new Rect(strip.x + 4, strip.y, size.x, size.y), content, _tagStyle);
        }
    }
}
