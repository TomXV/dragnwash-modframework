using System.Collections.Generic;
using UnityEngine;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;

namespace DragNWash.ModFramework.Inspector
{
    // The armature of the selected object: the bones of its skinned meshes
    // drawn in the game view (a line from each bone to its parent, a square at
    // each joint), and a click on a joint selects that bone, which the gizmo
    // then moves like any transform. A bone an animator writes every frame is
    // put back by it at once; the tab says so.
    internal static class InspectorBones
    {
        internal static bool Show;

        private static readonly List<Transform> Bones = new List<Transform>();
        private static readonly HashSet<Transform> BoneSet = new HashSet<Transform>();
        private static int _forObject;
        private const float Joint = 8f;

        private static void Gather(GameObject selected)
        {
            int id = selected.GetInstanceID();
            if (id == _forObject && Bones.Count > 0)
            {
                return;
            }
            _forObject = id;
            Bones.Clear();
            BoneSet.Clear();
            // The skinned meshes under the selection, or, when a bone is selected, of the character it belongs to.
            SkinnedMeshRenderer[] skins = selected.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0)
            {
                for (Transform p = selected.transform; p != null && skins.Length == 0; p = p.parent)
                {
                    skins = p.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                }
            }
            foreach (SkinnedMeshRenderer s in skins)
            {
                foreach (Transform b in s.bones)
                {
                    if (b != null && BoneSet.Add(b))
                    {
                        Bones.Add(b);
                    }
                }
            }
        }

        internal static int Count(GameObject selected)
        {
            if (selected == null) return 0;
            Gather(selected);
            return Bones.Count;
        }

        // From the overlay, in GUI coordinates.
        internal static void OnGUI(Rect window, GameObject selected)
        {
            if (!Show || selected == null)
            {
                return;
            }
            Gather(selected);
            if (Bones.Count == 0)
            {
                return;
            }
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            Event ev = Event.current;
            var joints = new List<KeyValuePair<Transform, Vector2>>();
            foreach (Transform b in Bones)
            {
                if (b == null) continue;
                Vector3 s = cam.WorldToScreenPoint(b.position);
                if (s.z <= 0) continue;
                joints.Add(new KeyValuePair<Transform, Vector2>(b, new Vector2(s.x, Screen.height - s.y)));
            }
            // A click on a joint selects the bone (unless a gizmo drag or a pick is under way).
            if (ev.type == EventType.MouseDown && ev.button == 0 && !window.Contains(ev.mousePosition) && !InspectorPick.Picking && !InspectorGizmo.Dragging)
            {
                Transform best = null;
                float bestD = Joint + 4;
                foreach (KeyValuePair<Transform, Vector2> j in joints)
                {
                    float d = Vector2.Distance(ev.mousePosition, j.Value);
                    if (d < bestD) { bestD = d; best = j.Key; }
                }
                if (best != null)
                {
                    InspectorTab.Select(best.gameObject);
                    TW.ShowNotice($"Bone {best.name}. An animator that drives it puts it back every frame; Move works between its updates only.");
                    ev.Use();
                    return;
                }
            }
            if (ev.type != EventType.Repaint)
            {
                return;
            }
            Material lines = InspectorGizmo.Lines;
            if (lines != null)
            {
                lines.SetPass(0);
                GL.PushMatrix();
                GL.LoadPixelMatrix();
                GL.Begin(GL.LINES);
                GL.Color(new Color(1f, 0.85f, 0.3f, 0.9f));
                foreach (KeyValuePair<Transform, Vector2> j in joints)
                {
                    Transform p = j.Key.parent;
                    if (p == null || !BoneSet.Contains(p)) continue;
                    Vector3 ps = cam.WorldToScreenPoint(p.position);
                    if (ps.z <= 0) continue;
                    GL.Vertex3(j.Value.x, j.Value.y, 0);
                    GL.Vertex3(ps.x, Screen.height - ps.y, 0);
                }
                GL.End();
                GL.PopMatrix();
            }
            GameObject sel = InspectorTab.SelectedObject;
            foreach (KeyValuePair<Transform, Vector2> j in joints)
            {
                bool isSelected = sel != null && ReferenceEquals(j.Key.gameObject, sel);
                var r = new Rect(j.Value.x - Joint / 2, j.Value.y - Joint / 2, Joint, Joint);
                TW.Fill(r, isSelected ? TW.AccentColor : new Color(1f, 0.85f, 0.3f, 0.9f));
                if (isSelected || Vector2.Distance(ev.mousePosition, j.Value) < Joint + 4)
                {
                    GUIStyle style = TW.Styles.Label;
                    if (style != null)
                    {
                        GUI.Label(new Rect(j.Value.x + Joint, j.Value.y - 12, 300, 24), TW.Drawable(j.Key.name), style);
                    }
                }
            }
        }
    }
}
