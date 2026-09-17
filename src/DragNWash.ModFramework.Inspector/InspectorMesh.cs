using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;

namespace DragNWash.ModFramework.Inspector
{
    // The meshes of the selected object drawn as wireframes in the game view,
    // and a vertex editor for the ones that can be read: a click near a vertex
    // selects it, a drag moves it in the plane facing the camera, and the
    // change goes into a copy of the mesh (the shared asset is never touched)
    // with a History entry per drag. A mesh the game marked unreadable keeps
    // its shape to itself: it gets its bounds as a box, and no editing. A
    // skinned mesh is drawn from a baked copy and cannot be edited here.
    internal static class InspectorMesh
    {
        internal static bool Wireframe;
        internal static bool Editing;
        internal static bool Dragging => _vertex >= 0 && _dragging;

        private const int MaxEdges = 60000;
        private const float Grab = 10f;

        private static readonly Mesh Baked = new Mesh();
        private static MeshFilter _editFilter;
        private static Mesh _editMesh;
        private static Vector3[] _vertices;
        private static int _vertex = -1;
        private static bool _dragging;
        private static Vector3[] _before;
        private static Plane _plane;
        private static Vector3 _grabOffset;
        private static readonly Dictionary<int, Mesh> Originals = new Dictionary<int, Mesh>();
        private static MeshFilter _failedFilter;

        internal static void OnGUI(Rect window, GameObject selected)
        {
            if ((!Wireframe && !Editing) || selected == null)
            {
                return;
            }
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            Event ev = Event.current;
            Material lines = InspectorGizmo.Lines;

            // Editing: the first readable MeshFilter on the selection or under it.
            MeshFilter filter = null;
            if (Editing)
            {
                foreach (MeshFilter f in selected.GetComponentsInChildren<MeshFilter>())
                {
                    if (f.sharedMesh != null) { filter = f; break; }
                }
                if (filter == null)
                {
                    if (ev.type == EventType.Repaint) TW.ShowNotice("Edit mesh: no mesh here (a skinned mesh cannot be edited in its pose).");
                }
                else if (filter != _editFilter && !ReferenceEquals(filter, _failedFilter))
                {
                    if (!BeginEdit(filter))
                    {
                        _failedFilter = filter;
                        filter = null;
                    }
                }
                else if (ReferenceEquals(filter, _failedFilter))
                {
                    filter = null;
                }
                if (filter != null && _vertices != null)
                {
                    HandleVertexInput(ev, cam, window);
                }
            }

            if (ev.type != EventType.Repaint || lines == null)
            {
                return;
            }
            lines.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.LINES);
            int edges = 0;
            foreach (Renderer r in selected.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                Mesh mesh = null;
                Matrix4x4 toWorld = r.transform.localToWorldMatrix;
                if (r is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                {
                    skin.BakeMesh(Baked);
                    mesh = Baked;
                    toWorld = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                }
                else if (r is MeshRenderer)
                {
                    MeshFilter f = r.GetComponent<MeshFilter>();
                    mesh = f != null ? (ReferenceEquals(f, _editFilter) ? _editMesh : f.sharedMesh) : null;
                }
                if (mesh == null) continue;
                if (!mesh.isReadable)
                {
                    GL.Color(new Color(0.5f, 0.8f, 1f, 0.6f));
                    Box(cam, r.bounds);
                    continue;
                }
                GL.Color(ReferenceEquals(r.GetComponent<MeshFilter>(), _editFilter) && Editing ? new Color(1f, 0.6f, 0.2f, 0.8f) : new Color(0.3f, 0.9f, 1f, 0.55f));
                Vector3[] v = ReferenceEquals(r.GetComponent<MeshFilter>(), _editFilter) && _vertices != null ? _vertices : mesh.vertices;
                int[] tris = mesh.triangles;
                var screen = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    screen[i] = cam.WorldToScreenPoint(toWorld.MultiplyPoint3x4(v[i]));
                }
                for (int i = 0; i + 2 < tris.Length && edges < MaxEdges; i += 3)
                {
                    Edge(screen[tris[i]], screen[tris[i + 1]]);
                    Edge(screen[tris[i + 1]], screen[tris[i + 2]]);
                    Edge(screen[tris[i + 2]], screen[tris[i]]);
                    edges += 3;
                }
            }
            GL.End();
            GL.PopMatrix();

            // The vertices of the mesh being edited, the selected one larger.
            if (Editing && _editFilter != null && _vertices != null)
            {
                Matrix4x4 toWorld = _editFilter.transform.localToWorldMatrix;
                for (int i = 0; i < _vertices.Length; i++)
                {
                    Vector3 s = cam.WorldToScreenPoint(toWorld.MultiplyPoint3x4(_vertices[i]));
                    if (s.z <= 0) continue;
                    bool sel = i == _vertex;
                    float size = sel ? 8 : 4;
                    TW.Fill(new Rect(s.x - size / 2, Screen.height - s.y - size / 2, size, size), sel ? TW.AccentColor : new Color(1f, 0.6f, 0.2f, 0.9f));
                }
            }
        }

        private static void Edge(Vector3 a, Vector3 b)
        {
            if (a.z <= 0 || b.z <= 0) return;
            GL.Vertex3(a.x, Screen.height - a.y, 0);
            GL.Vertex3(b.x, Screen.height - b.y, 0);
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
            foreach (int[] ed in edges) Edge(p[ed[0]], p[ed[1]]);
        }

        // The mesh is copied once per MeshFilter; the copy is what the filter
        // draws from then on, and the original is kept for Reset mesh.
        private static bool BeginEdit(MeshFilter filter)
        {
            int id = filter.GetInstanceID();
            if (!Originals.ContainsKey(id))
            {
                Mesh copy = ReadableCopy(filter.sharedMesh, out string why);
                if (copy == null)
                {
                    TW.ShowNotice("Edit mesh: " + why);
                    InspectorPlugin.Log.LogWarning($"[mesh] {filter.sharedMesh.name}: {why}");
                    return false;
                }
                Originals[id] = filter.sharedMesh;
                copy.name = filter.sharedMesh.name + " (edited)";
                filter.sharedMesh = copy;
            }
            _editFilter = filter;
            _editMesh = filter.sharedMesh;
            _vertices = _editMesh.vertices;
            _vertex = -1;
            _dragging = false;
            TW.ShowNotice($"Edit mesh (experimental): {_vertices.Length} vertices of {filter.name}. Click one, drag it.");
            return true;
        }

        // A copy of the mesh that the CPU can read. A readable mesh is simply
        // cloned. A GPU-only one (the usual case in a shipped game) has its
        // vertex and index buffers read back from the GPU, byte for byte, into
        // a new mesh with the same layout, so every attribute survives.
        private static Mesh ReadableCopy(Mesh source, out string why)
        {
            why = null;
            if (source.isReadable)
            {
                return UnityEngine.Object.Instantiate(source);
            }
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                why = "this graphics API cannot read a GPU-only mesh back.";
                return null;
            }
            try
            {
                source.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                source.indexBufferTarget |= GraphicsBuffer.Target.Raw;
                VertexAttributeDescriptor[] layout = source.GetVertexAttributes();
                int streams = 0;
                foreach (VertexAttributeDescriptor a in layout) streams = Mathf.Max(streams, a.stream + 1);
                var copy = new Mesh { name = source.name };
                copy.SetVertexBufferParams(source.vertexCount, layout);
                for (int st = 0; st < streams; st++)
                {
                    using (GraphicsBuffer vb = source.GetVertexBuffer(st))
                    {
                        if (vb == null) { why = "the vertex buffer could not be opened."; return null; }
                        AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(vb);
                        req.WaitForCompletion();
                        if (req.hasError) { why = "the vertex buffer read back failed."; return null; }
                        using (NativeArray<byte> data = req.GetData<byte>())
                        {
                            copy.SetVertexBufferData(data, 0, 0, data.Length, st, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
                        }
                    }
                }
                copy.SetIndexBufferParams(TotalIndices(source), source.indexFormat);
                using (GraphicsBuffer ib = source.GetIndexBuffer())
                {
                    if (ib == null) { why = "the index buffer could not be opened."; return null; }
                    AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(ib);
                    req.WaitForCompletion();
                    if (req.hasError) { why = "the index buffer read back failed."; return null; }
                    using (NativeArray<byte> data = req.GetData<byte>())
                    {
                        copy.SetIndexBufferData(data, 0, 0, data.Length, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
                    }
                }
                copy.subMeshCount = source.subMeshCount;
                for (int i = 0; i < source.subMeshCount; i++)
                {
                    copy.SetSubMesh(i, source.GetSubMesh(i), MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
                }
                copy.bounds = source.bounds;
                copy.bindposes = source.bindposes;
                // Touching the vertices through the managed API makes the copy a
                // normal readable mesh from here on.
                Vector3[] v = copy.vertices;
                if (v == null || v.Length != source.vertexCount) { why = "the read-back mesh has no vertices."; return null; }
                return copy;
            }
            catch (Exception ex)
            {
                why = "read back failed: " + ex.Message;
                return null;
            }
        }

        private static int TotalIndices(Mesh m)
        {
            int n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += (int)m.GetIndexCount(i);
            return n;
        }

        internal static bool HasEdited(GameObject selected)
        {
            if (selected == null) return false;
            foreach (MeshFilter f in selected.GetComponentsInChildren<MeshFilter>())
            {
                if (Originals.ContainsKey(f.GetInstanceID())) return true;
            }
            return false;
        }

        internal static void ResetMesh(GameObject selected)
        {
            if (selected == null) return;
            foreach (MeshFilter f in selected.GetComponentsInChildren<MeshFilter>())
            {
                if (Originals.TryGetValue(f.GetInstanceID(), out Mesh original))
                {
                    Mesh copy = f.sharedMesh;
                    f.sharedMesh = original;
                    Originals.Remove(f.GetInstanceID());
                    if (copy != null && copy != original) UnityEngine.Object.Destroy(copy);
                }
            }
            _editFilter = null;
            _editMesh = null;
            _vertices = null;
            _vertex = -1;
        }

        internal static void StopEditing()
        {
            Editing = false;
            _failedFilter = null;
            _editFilter = null;
            _editMesh = null;
            _vertices = null;
            _vertex = -1;
            _dragging = false;
        }

        private static void HandleVertexInput(Event ev, Camera cam, Rect window)
        {
            Matrix4x4 toWorld = _editFilter.transform.localToWorldMatrix;
            if (ev.type == EventType.MouseDown && ev.button == 0 && !window.Contains(ev.mousePosition) && !InspectorGizmo.Dragging && !InspectorPick.Picking)
            {
                int best = -1;
                float bestD = Grab;
                for (int i = 0; i < _vertices.Length; i++)
                {
                    Vector3 s = cam.WorldToScreenPoint(toWorld.MultiplyPoint3x4(_vertices[i]));
                    if (s.z <= 0) continue;
                    float d = Vector2.Distance(ev.mousePosition, new Vector2(s.x, Screen.height - s.y));
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0)
                {
                    _vertex = best;
                    _dragging = true;
                    _before = (Vector3[])_vertices.Clone();
                    Vector3 world = toWorld.MultiplyPoint3x4(_vertices[best]);
                    _plane = new Plane(-cam.transform.forward, world);
                    Ray ray = cam.ScreenPointToRay(new Vector3(ev.mousePosition.x, Screen.height - ev.mousePosition.y, 0));
                    _grabOffset = _plane.Raycast(ray, out float enter) ? world - ray.GetPoint(enter) : Vector3.zero;
                    ev.Use();
                }
            }
            else if (_dragging && ev.type == EventType.MouseDrag)
            {
                Ray ray = cam.ScreenPointToRay(new Vector3(ev.mousePosition.x, Screen.height - ev.mousePosition.y, 0));
                if (_plane.Raycast(ray, out float enter))
                {
                    Vector3 world = ray.GetPoint(enter) + _grabOffset;
                    _vertices[_vertex] = _editFilter.transform.worldToLocalMatrix.MultiplyPoint3x4(world);
                    _editMesh.vertices = _vertices;
                    _editMesh.RecalculateBounds();
                }
                ev.Use();
            }
            else if (_dragging && ev.type == EventType.MouseUp)
            {
                _dragging = false;
                _editMesh.RecalculateNormals();
                Mesh mesh = _editMesh;
                Vector3[] after = (Vector3[])_vertices.Clone();
                Vector3[] before = _before;
                int index = _vertex;
                if (before != null && before[index] != after[index])
                {
                    InspectorHistory.Record(_editFilter, InspectorModel.PathOf(_editFilter.transform) + " : mesh", "vertex " + index, before[index], after[index],
                        () => mesh.vertices[index],
                        v => { Vector3[] vs = mesh.vertices; vs[index] = (Vector3)v; mesh.vertices = vs; mesh.RecalculateBounds(); mesh.RecalculateNormals(); if (ReferenceEquals(mesh, _editMesh)) _vertices = vs; });
                }
                ev.Use();
            }
        }
    }
}
