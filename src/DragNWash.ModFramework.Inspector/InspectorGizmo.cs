using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // A transform gizmo in the game view for the Inspector's selected object:
    // its local axes drawn from its position, with a handle at each tip that
    // moves, rotates or scales the object along or around that axis when
    // dragged, and a readout of position, rotation and scale. Lines are drawn
    // with GL through a material made once at the plugin's Awake.
    internal static class InspectorGizmo
    {
        internal enum GizmoMode { None, Move, Rotate, Scale }

        internal static GizmoMode Mode;
        internal static bool Dragging => _axis >= 0;
        internal static Material Lines => _lines;

        private static Material _lines;
        private static int _axis = -1;
        private static Vector3 _startPosition, _startScale;
        private static Quaternion _startRotation;
        private static float _startT;
        private static Vector3 _startLocalPosition, _startLocalEuler;
        private static Vector2 _startMouse;
        private static float _axisLength;

        private static readonly string[] Members = { "localPosition", "localEulerAngles", "localScale" };

        private static readonly Color[] AxisColors = { new Color(0.95f, 0.35f, 0.35f), new Color(0.45f, 0.9f, 0.4f), new Color(0.4f, 0.6f, 1f) };
        private static readonly string[] AxisNames = { "x", "y", "z" };
        private const float HandleSize = 18f;
        private const float GrabRadius = 22f;
        private const float LineGrab = 9f;

        // From the plugin's Awake: the line material, before any frame is presented.
        internal static void CreateMaterial()
        {
            if (_lines != null)
            {
                return;
            }
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return;
            }
            _lines = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _lines.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lines.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lines.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _lines.SetInt("_ZWrite", 0);
            _lines.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        internal static bool HasOriginal(GameObject go)
        {
            if (go == null) return false;
            foreach (string m in Members)
            {
                if (InspectorHistory.TryOriginal(go.transform, m, out _)) return true;
            }
            return false;
        }

        // Every part the gizmo changed goes back to what it held before the first drag.
        internal static void ResetTransform(GameObject go)
        {
            if (go == null) return;
            Transform t = go.transform;
            if (InspectorHistory.TryOriginal(t, "localPosition", out object p)) { t.localPosition = (Vector3)p; InspectorHistory.ForgetOriginal(t, "localPosition"); }
            if (InspectorHistory.TryOriginal(t, "localEulerAngles", out object r)) { t.localEulerAngles = (Vector3)r; InspectorHistory.ForgetOriginal(t, "localEulerAngles"); }
            if (InspectorHistory.TryOriginal(t, "localScale", out object sc)) { t.localScale = (Vector3)sc; InspectorHistory.ForgetOriginal(t, "localScale"); }
        }

        // From OnGUI, outside the window, in GUI coordinates (top-left origin).
        internal static void OnGUI(Rect window, GameObject selected)
        {
            if (selected == null || Mode == GizmoMode.None)
            {
                _axis = -1;
                return;
            }
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            Transform t = selected.transform;
            Event ev = Event.current;
            Vector3 origin = t.position;
            Vector3 originScreen = cam.WorldToScreenPoint(origin);
            if (originScreen.z <= 0)
            {
                return;
            }
            // The axes keep a screen size: their world length grows with the distance.
            float length = Vector3.Distance(cam.transform.position, origin) * 0.18f;
            Vector3[] dirs = { t.right, t.up, t.forward };
            var tips = new Vector2[3];
            var tipScreens = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                tipScreens[i] = cam.WorldToScreenPoint(origin + dirs[i] * length);
                tips[i] = ToGui(tipScreens[i]);
            }
            Vector2 originGui = ToGui(originScreen);

            // Input: a press on a handle starts a drag along that axis. The
            // drag holds IMGUI's hot control so every MouseDrag reaches it.
            int control = GUIUtility.GetControlID("DragNWashInspectorGizmo".GetHashCode(), FocusType.Passive);
            if (ev.type == EventType.MouseDown && ev.button == 0 && !window.Contains(ev.mousePosition))
            {
                for (int i = 0; i < 3; i++)
                {
                    bool onTip = Vector2.Distance(ev.mousePosition, tips[i]) <= GrabRadius;
                    bool onLine = DistanceToSegment(ev.mousePosition, originGui, tips[i]) <= LineGrab;
                    if (tipScreens[i].z > 0 && (onTip || onLine))
                    {
                        _axis = i;
                        _startPosition = t.position;
                        _startRotation = t.rotation;
                        _startScale = t.localScale;
                        _startLocalPosition = t.localPosition;
                        _startLocalEuler = t.localEulerAngles;
                        _startMouse = ev.mousePosition;
                        _axisLength = length;
                        _startT = AlongAxis(cam, ev.mousePosition, _startPosition, dirs[i]);
                        GUIUtility.hotControl = control;
                        InspectorPlugin.Log.LogInfo($"[gizmo] drag {Mode} along {AxisNames[i]} of {selected.name} from {t.position}");
                        ev.Use();
                        break;
                    }
                }
            }
            else if (_axis >= 0 && (ev.type == EventType.MouseDrag || (ev.type == EventType.MouseMove && GUIUtility.hotControl == control)))
            {
                Vector3 axis = _startRotation * (_axis == 0 ? Vector3.right : _axis == 1 ? Vector3.up : Vector3.forward);
                switch (Mode)
                {
                    case GizmoMode.Move:
                    {
                        float now = AlongAxis(cam, ev.mousePosition, _startPosition, axis);
                        t.position = _startPosition + axis * (now - _startT);
                        break;
                    }
                    case GizmoMode.Scale:
                    {
                        float now = AlongAxis(cam, ev.mousePosition, _startPosition, axis);
                        float factor = Mathf.Max(0.01f, 1f + (now - _startT) / Mathf.Max(0.0001f, _axisLength));
                        Vector3 s = _startScale;
                        s[_axis] *= factor;
                        t.localScale = s;
                        break;
                    }
                    case GizmoMode.Rotate:
                    {
                        float angle = (ev.mousePosition.x - _startMouse.x) * 0.5f;
                        t.rotation = Quaternion.AngleAxis(angle, axis) * _startRotation;
                        break;
                    }
                }
                ev.Use();
            }
            else if (_axis >= 0 && ev.type == EventType.MouseUp)
            {
                InspectorPlugin.Log.LogInfo($"[gizmo] drag end: {selected.name} at {t.position}, rot {t.rotation.eulerAngles}, scale {t.localScale}");
                // One history entry for the part this drag changed.
                string where = InspectorModel.PathOf(t) + " : Transform (gizmo)";
                Transform tt = t;
                switch (Mode)
                {
                    case GizmoMode.Move:
                        if (t.position != _startPosition) InspectorHistory.Record(t, where, "localPosition", _startLocalPosition, t.localPosition, () => tt.localPosition, v => tt.localPosition = (Vector3)v);
                        break;
                    case GizmoMode.Rotate:
                        if (t.rotation != _startRotation) InspectorHistory.Record(t, where, "localEulerAngles", _startLocalEuler, t.localEulerAngles, () => tt.localEulerAngles, v => tt.localEulerAngles = (Vector3)v);
                        break;
                    case GizmoMode.Scale:
                        if (t.localScale != _startScale) InspectorHistory.Record(t, where, "localScale", _startScale, t.localScale, () => tt.localScale, v => tt.localScale = (Vector3)v);
                        break;
                }
                _axis = -1;
                if (GUIUtility.hotControl == control)
                {
                    GUIUtility.hotControl = 0;
                }
                ev.Use();
            }

            if (ev.type != EventType.Repaint)
            {
                return;
            }
            // The axes, then the handles, then the readout.
            if (_lines != null)
            {
                _lines.SetPass(0);
                GL.PushMatrix();
                GL.LoadPixelMatrix();
                GL.Begin(GL.LINES);
                for (int i = 0; i < 3; i++)
                {
                    if (tipScreens[i].z <= 0) continue;
                    Color c = AxisColors[i];
                    if (_axis == i) c = Color.white;
                    GL.Color(c);
                    // Inside OnGUI the pixel matrix follows the GUI: top-left origin.
                    GL.Vertex3(originGui.x, originGui.y, 0);
                    GL.Vertex3(tips[i].x, tips[i].y, 0);
                    // A second, offset line makes the axis two pixels wide.
                    GL.Vertex3(originGui.x + 1, originGui.y + 1, 0);
                    GL.Vertex3(tips[i].x + 1, tips[i].y + 1, 0);
                }
                GL.End();
                GL.PopMatrix();
            }
            for (int i = 0; i < 3; i++)
            {
                if (tipScreens[i].z <= 0) continue;
                Color c = _axis == i ? Color.white : AxisColors[i];
                float h = HandleSize;
                var handle = new Rect(tips[i].x - h / 2, tips[i].y - h / 2, h, h);
                if (Mode == GizmoMode.Rotate)
                {
                    Ring(handle, c);
                }
                else
                {
                    TW.Fill(handle, c);
                    if (Mode == GizmoMode.Scale)
                    {
                        TW.Fill(new Rect(handle.x + 3, handle.y + 3, h - 6, h - 6), new Color(0.06f, 0.06f, 0.08f));
                    }
                }
                GUIStyle style = TW.Styles.Label;
                if (style != null)
                {
                    GUI.Label(new Rect(tips[i].x + h / 2 + 2, tips[i].y - 12, 20, 24), AxisNames[i], style);
                }
            }
            TW.Fill(new Rect(originGui.x - 3, originGui.y - 3, 6, 6), Color.white);
            Readout(originGui, t);
        }

        // The position, rotation and scale, next to the origin.
        private static void Readout(Vector2 originGui, Transform t)
        {
            GUIStyle style = TW.Styles.MutedLabel;
            if (style == null)
            {
                return;
            }
            Vector3 e = t.rotation.eulerAngles;
            string mode = Mode == GizmoMode.Move ? "move" : Mode == GizmoMode.Rotate ? "rotate" : "scale";
            string text = $"{mode}: drag a handle\npos  {F(t.position.x)}  {F(t.position.y)}  {F(t.position.z)}\nrot  {F(e.x)}  {F(e.y)}  {F(e.z)}\nscale  {F(t.localScale.x)}  {F(t.localScale.y)}  {F(t.localScale.z)}";
            var content = new GUIContent(text);
            var wrapped = new GUIStyle(style) { wordWrap = false, alignment = TextAnchor.UpperLeft };
            Vector2 size = wrapped.CalcSize(content);
            float x = Mathf.Clamp(originGui.x + 14, 0, Screen.width - size.x - 8);
            float y = Mathf.Clamp(originGui.y + 14, 0, Screen.height - size.y - 8);
            TW.Fill(new Rect(x - 4, y - 2, size.x + 8, size.y + 4), new Color(0.06f, 0.06f, 0.08f, 0.85f));
            GUI.Label(new Rect(x, y, size.x, size.y), content, wrapped);
        }

        private static string F(float v)
        {
            return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Where along the axis line (from origin, in world units) the mouse
        // ray passes closest: the parameter of the closest point between the
        // axis line and the pointer's ray.
        private static float AlongAxis(Camera cam, Vector2 guiMouse, Vector3 origin, Vector3 axis)
        {
            Ray ray = cam.ScreenPointToRay(new Vector3(guiMouse.x, Screen.height - guiMouse.y, 0));
            Vector3 w0 = origin - ray.origin;
            Vector3 d = axis.normalized, r = ray.direction.normalized;
            float b = Vector3.Dot(d, r);
            float d0 = Vector3.Dot(d, w0), e = Vector3.Dot(r, w0);
            float denom = 1f - b * b;
            if (denom < 1e-6f)
            {
                return 0f;
            }
            return (b * e - d0) / denom;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
            return Vector2.Distance(p, a + ab * t);
        }

        private static Vector2 ToGui(Vector3 screen)
        {
            return new Vector2(screen.x, Screen.height - screen.y);
        }

        private static void Ring(Rect r, Color c)
        {
            TW.Fill(new Rect(r.xMin, r.yMin, r.width, 2), c);
            TW.Fill(new Rect(r.xMin, r.yMax - 2, r.width, 2), c);
            TW.Fill(new Rect(r.xMin, r.yMin, 2, r.height), c);
            TW.Fill(new Rect(r.xMax - 2, r.yMin, 2, r.height), c);
        }
    }
}
