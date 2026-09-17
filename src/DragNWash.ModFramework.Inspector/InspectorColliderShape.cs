using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace DragNWash.ModFramework.Inspector
{
    // The true shape of a collider or a light, drawn over the game: a box as a
    // box, a sphere as a sphere, a capsule as a capsule, a mesh collider as its
    // wireframe, a spot light as its cone. The physics module is not referenced
    // by this library, so each collider's fields are read by reflection, looked
    // up once per type.
    //
    // Everything here draws inside the debug view's GL pass: lines in screen
    // space, so the caller has already set the material and begun GL.LINES.
    internal static class InspectorColliderShape
    {
        private enum Form { Unknown, Box, Sphere, Capsule, Mesh }

        private sealed class Reader
        {
            public Form Form;
            public PropertyInfo Center, Size, Radius, Height, Direction, SharedMesh;
        }

        private static readonly Dictionary<Type, Reader> Readers = new Dictionary<Type, Reader>();
        private const int MeshEdgeCap = 4000;

        // A collision mesh's corners and triangles, read once per mesh. A game
        // keeps its meshes on the GPU only, so they are read back from there.
        private sealed class MeshShape
        {
            public Vector3[] Vertices;
            public int[] Triangles;
        }

        private static readonly Dictionary<int, MeshShape> MeshShapes = new Dictionary<int, MeshShape>();
        private static readonly HashSet<int> MeshFailures = new HashSet<int>();

        // A collider whose mesh cannot be read is asked for its shape instead:
        // rays from all around, each tested against that one collider
        // (Collider.Raycast), give points on the surface the physics engine
        // really uses. Kept in the collider's own space, so they follow it.
        private sealed class RayShape
        {
            public readonly List<Patch> Patches = new List<Patch>();
        }

        // What one of the six directions saw: a grid of surface points, and the
        // longest step between neighbours that is still one surface.
        private sealed class Patch
        {
            public Vector3[,] Points;
            public bool[,] Hit;
            public float MaxEdge;
        }

        private static readonly Dictionary<int, RayShape> RayShapes = new Dictionary<int, RayShape>();

        /// <summary>
        /// True when the collider's shape on screen was scanned with rays, not
        /// read: an approximation that can differ from the real shape.
        /// </summary>
        internal static bool IsScanned(Component collider)
        {
            return !ReferenceEquals(collider, null) && collider && RayShapes.ContainsKey(collider.GetInstanceID());
        }
        private const int ScanCells = 24;

        private static Reader ReaderFor(Type t)
        {
            if (Readers.TryGetValue(t, out Reader r))
            {
                return r;
            }
            r = new Reader();
            switch (t.Name)
            {
                case "BoxCollider":
                    r.Form = Form.Box;
                    r.Center = t.GetProperty("center");
                    r.Size = t.GetProperty("size");
                    break;
                case "SphereCollider":
                    r.Form = Form.Sphere;
                    r.Center = t.GetProperty("center");
                    r.Radius = t.GetProperty("radius");
                    break;
                case "CapsuleCollider":
                    r.Form = Form.Capsule;
                    r.Center = t.GetProperty("center");
                    r.Radius = t.GetProperty("radius");
                    r.Height = t.GetProperty("height");
                    r.Direction = t.GetProperty("direction");
                    break;
                case "MeshCollider":
                    r.Form = Form.Mesh;
                    r.SharedMesh = t.GetProperty("sharedMesh");
                    break;
            }
            Readers[t] = r;
            return r;
        }

        /// <summary>
        /// Draws the collider in its own shape. False when the shape is not one
        /// this knows (a terrain, a 2D collider, a mesh not read or unreadable):
        /// the caller falls back to the bounds box.
        /// </summary>
        internal static bool Draw(Camera cam, Component collider)
        {
            if (ReferenceEquals(collider, null) || !collider)
            {
                return false;
            }
            Reader r = ReaderFor(collider.GetType());
            Transform t = collider.transform;
            try
            {
                switch (r.Form)
                {
                    case Form.Box:
                        {
                            Vector3 c = (Vector3)r.Center.GetValue(collider, null);
                            Vector3 half = (Vector3)r.Size.GetValue(collider, null) * 0.5f;
                            Box(cam, t.localToWorldMatrix, c, half);
                            return true;
                        }
                    case Form.Sphere:
                        {
                            Vector3 c = t.TransformPoint((Vector3)r.Center.GetValue(collider, null));
                            float radius = (float)r.Radius.GetValue(collider, null) * MaxScale(t.lossyScale);
                            Sphere(cam, c, radius);
                            return true;
                        }
                    case Form.Capsule:
                        {
                            Vector3 c = t.TransformPoint((Vector3)r.Center.GetValue(collider, null));
                            int dir = (int)r.Direction.GetValue(collider, null);
                            Vector3 scale = t.lossyScale;
                            // Unity scales the radius by the larger of the two
                            // axes across the capsule, the height by its own axis.
                            float across = dir == 0 ? Mathf.Max(Abs(scale.y), Abs(scale.z)) : dir == 1 ? Mathf.Max(Abs(scale.x), Abs(scale.z)) : Mathf.Max(Abs(scale.x), Abs(scale.y));
                            float along = dir == 0 ? Abs(scale.x) : dir == 1 ? Abs(scale.y) : Abs(scale.z);
                            float radius = (float)r.Radius.GetValue(collider, null) * across;
                            float height = (float)r.Height.GetValue(collider, null) * along;
                            Vector3 axis = (dir == 0 ? t.right : dir == 1 ? t.up : t.forward).normalized;
                            Vector3 u = (dir == 0 ? t.up : t.right).normalized;
                            Vector3 v = Vector3.Cross(axis, u).normalized;
                            Capsule(cam, c, axis, u, v, radius, height);
                            return true;
                        }
                    case Form.Mesh:
                        {
                            var mesh = r.SharedMesh.GetValue(collider, null) as Mesh;
                            if (mesh == null)
                            {
                                return false;
                            }
                            int meshId = mesh.GetInstanceID();
                            if (MeshShapes.TryGetValue(meshId, out MeshShape shape))
                            {
                                Wireframe(cam, t.localToWorldMatrix, shape);
                                return true;
                            }
                            if (RayShapes.TryGetValue(collider.GetInstanceID(), out RayShape sampled))
                            {
                                DrawRayShape(cam, t.localToWorldMatrix, sampled);
                                return true;
                            }
                            if (MeshFailures.Contains(meshId))
                            {
                                // The shape itself lives only in the physics engine; the
                                // mesh's own bounds, in the collider's space, fit far
                                // closer than a box around the world.
                                Bounds local = mesh.bounds;
                                if (!BoundsBelievable(collider, t, local))
                                {
                                    return false;
                                }
                                Box(cam, t.localToWorldMatrix, local.center, local.extents);
                                return true;
                            }
                            return false;
                        }
                }
            }
            catch (Exception ex)
            {
                // A collider type whose fields are not where they were expected:
                // said once, then drawn as its box like the rest.
                Readers[collider.GetType()] = new Reader();
                InspectorPlugin.Log.LogWarning($"[debug view] {collider.GetType().Name}: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Reads a mesh collider's mesh, once, so <see cref="Draw"/> can show
        /// its triangles. Called while gathering, outside any GL drawing; a mesh
        /// that cannot be read is said once in the log and drawn as its box.
        /// Returns false when nothing was read this call.
        /// </summary>
        internal static bool Prepare(Component collider)
        {
            if (ReferenceEquals(collider, null) || !collider)
            {
                return false;
            }
            Reader r = ReaderFor(collider.GetType());
            if (r.Form != Form.Mesh)
            {
                return false;
            }
            Mesh mesh;
            try
            {
                mesh = r.SharedMesh.GetValue(collider, null) as Mesh;
            }
            catch (Exception)
            {
                return false;
            }
            if (mesh == null)
            {
                return false;
            }
            int id = mesh.GetInstanceID();
            if (MeshFailures.Contains(id) && !RayShapes.ContainsKey(collider.GetInstanceID()) && !RayFailures.Contains(collider.GetInstanceID()))
            {
                return SampleByRays(collider, mesh);
            }
            if (MeshShapes.ContainsKey(id) || MeshFailures.Contains(id))
            {
                return false;
            }
            MeshShape shape = ReadMesh(mesh, out string why);
            if (shape == null)
            {
                MeshFailures.Add(id);
                InspectorPlugin.Log.LogInfo($"[debug view] {mesh.name}: not readable ({why}); asking the physics engine for its shape instead.");
                return true;
            }
            MeshShapes[id] = shape;
            return true;
        }

        // The positions and the triangles of a mesh. A readable mesh gives them
        // at once; a GPU-only one has its buffers read back and the bytes taken
        // apart here, without making a mesh (nothing is uploaded).
        private static MeshShape ReadMesh(Mesh mesh, out string why)
        {
            why = null;
            try
            {
                if (mesh.isReadable)
                {
                    return new MeshShape { Vertices = mesh.vertices, Triangles = mesh.triangles };
                }
                if (!SystemInfo.supportsAsyncGPUReadback)
                {
                    why = "this graphics API cannot read a mesh back";
                    return null;
                }
                if (!mesh.HasVertexAttribute(VertexAttribute.Position)
                    || mesh.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32
                    || mesh.GetVertexAttributeDimension(VertexAttribute.Position) < 3)
                {
                    why = "its positions are not stored as plain floats";
                    return null;
                }
                int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
                int offset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
                int stride = mesh.GetVertexBufferStride(stream);
                int count = mesh.vertexCount;
                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;

                byte[] vbytes = ReadBack(mesh.GetVertexBuffer(stream), "vertex", out why);
                if (vbytes == null) return null;
                if (vbytes.Length < offset + (count - 1) * stride + 12)
                {
                    why = "the vertex buffer is shorter than the mesh says";
                    return null;
                }
                var vertices = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    int p = offset + i * stride;
                    vertices[i] = new Vector3(BitConverter.ToSingle(vbytes, p), BitConverter.ToSingle(vbytes, p + 4), BitConverter.ToSingle(vbytes, p + 8));
                }

                byte[] ibytes;
                try
                {
                    ibytes = ReadBack(mesh.GetIndexBuffer(), "index", out why);
                }
                catch (Exception ex)
                {
                    ibytes = null;
                    why = ex.Message;
                }
                if (ibytes == null)
                {
                    // A mesh only physics uses is never drawn, so the game keeps
                    // no triangles on the GPU. Its corners still show its shape.
                    if (!Plausible(vertices, mesh.bounds))
                    {
                        why = "the physics engine holds its shape; neither its triangles nor its vertices are on the GPU";
                        return null;
                    }
                    InspectorPlugin.Log.LogInfo($"[debug view] {mesh.name}: no triangles on the GPU ({why}); drawn as its {count} vertices.");
                    why = null;
                    return new MeshShape { Vertices = vertices, Triangles = new int[0] };
                }
                bool wide = mesh.indexFormat == IndexFormat.UInt32;
                int size = wide ? 4 : 2;
                var triangles = new List<int>();
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    SubMeshDescriptor d = mesh.GetSubMesh(sm);
                    if (d.topology != MeshTopology.Triangles) continue;
                    for (int j = d.indexStart; j < d.indexStart + d.indexCount; j++)
                    {
                        int at = j * size;
                        if (at + size > ibytes.Length) break;
                        int index = (wide ? (int)BitConverter.ToUInt32(ibytes, at) : BitConverter.ToUInt16(ibytes, at)) + d.baseVertex;
                        triangles.Add(index < count ? index : 0);
                    }
                }
                return new MeshShape { Vertices = vertices, Triangles = triangles.ToArray() };
            }
            catch (Exception ex)
            {
                why = "read back failed: " + ex.Message;
                return null;
            }
        }

        private static readonly HashSet<int> RayFailures = new HashSet<int>();

        // A physics-only mesh can report bounds much smaller than its shape.
        // Trusted only when, turned into the world, they are at least half the
        // size of the bounds the collider itself reports.
        private static bool BoundsBelievable(Component collider, Transform t, Bounds local)
        {
            if (!(collider.GetType().GetProperty("bounds")?.GetValue(collider, null) is Bounds world))
            {
                return false;
            }
            Vector3 size = Vector3.Scale(local.size, t.lossyScale);
            return size.magnitude >= world.size.magnitude * 0.5f;
        }
        private static MethodInfo _raycast;
        private static PropertyInfo _hitPoint;

        // Scans the collider like a depth camera from each of the six sides of
        // the bounds the physics engine reports: a ScanCells x ScanCells grid of
        // parallel rays per side, each against this collider alone. A concave
        // shape (steps, a frame) comes out as flat patches instead of rays
        // meeting in the middle. Returns true when it did the work this call
        // (one collider a round).
        private static bool SampleByRays(Component collider, Mesh mesh)
        {
            int cid = collider.GetInstanceID();
            try
            {
                if (_raycast == null)
                {
                    Type hitType = collider.GetType().Assembly.GetType("UnityEngine.RaycastHit");
                    _raycast = hitType == null ? null : collider.GetType().GetMethod("Raycast", new[] { typeof(Ray), hitType.MakeByRefType(), typeof(float) });
                    _hitPoint = hitType?.GetProperty("point");
                }
                if (_raycast == null || _hitPoint == null)
                {
                    RayFailures.Add(cid);
                    InspectorPlugin.Log.LogInfo($"[debug view] {mesh.name}: Collider.Raycast was not found; drawn as a box.");
                    return true;
                }
                // The physics engine's own bounds for the collider: a physics-only
                // mesh can report bounds far smaller than the shape.
                if (!(collider.GetType().GetProperty("bounds")?.GetValue(collider, null) is Bounds world) || world.size.sqrMagnitude < 1e-8f)
                {
                    RayFailures.Add(cid);
                    InspectorPlugin.Log.LogInfo($"[debug view] {mesh.name}: the collider reports no bounds; drawn as a box.");
                    return true;
                }
                Matrix4x4 toLocal = collider.transform.worldToLocalMatrix;
                var shape = new RayShape();
                var args = new object[3];
                int hits = 0;
                Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
                for (int a = 0; a < 3; a++)
                {
                    Vector3 axis = axes[a];
                    Vector3 u = axes[(a + 1) % 3];
                    Vector3 v = axes[(a + 2) % 3];
                    float depth = Vector3.Dot(world.extents, axis);
                    float halfU = Vector3.Dot(world.extents, u);
                    float halfV = Vector3.Dot(world.extents, v);
                    float cell = Mathf.Max(halfU, halfV) * 2f / ScanCells;
                    foreach (float side in new[] { 1f, -1f })
                    {
                        var patch = new Patch
                        {
                            Points = new Vector3[ScanCells, ScanCells],
                            Hit = new bool[ScanCells, ScanCells],
                            // Neighbours on one surface are about a cell apart, more
                            // on a slope; a bigger step is a gap or a ledge.
                            MaxEdge = cell * 2.5f,
                        };
                        int patchHits = 0;
                        Vector3 start = world.center + axis * (side * (depth + 0.05f));
                        for (int i = 0; i < ScanCells; i++)
                        {
                            float du = ((i + 0.5f) / ScanCells * 2f - 1f) * halfU;
                            for (int j = 0; j < ScanCells; j++)
                            {
                                float dv = ((j + 0.5f) / ScanCells * 2f - 1f) * halfV;
                                args[0] = new Ray(start + u * du + v * dv, -axis * side);
                                args[1] = null;
                                args[2] = depth * 2f + 0.1f;
                                if ((bool)_raycast.Invoke(collider, args))
                                {
                                    patch.Points[i, j] = toLocal.MultiplyPoint3x4((Vector3)_hitPoint.GetValue(args[1], null));
                                    patch.Hit[i, j] = true;
                                    patchHits++;
                                }
                            }
                        }
                        if (patchHits > 0)
                        {
                            shape.Patches.Add(patch);
                            hits += patchHits;
                        }
                    }
                }
                if (hits < 8)
                {
                    RayFailures.Add(cid);
                    InspectorPlugin.Log.LogInfo($"[debug view] {mesh.name}: only {hits} rays hit (a switched-off collider does not answer); drawn as a box.");
                    return true;
                }
                RayShapes[cid] = shape;
                InspectorPlugin.Log.LogInfo($"[debug view] {mesh.name}: shape scanned from the physics engine from six sides, {6 * ScanCells * ScanCells} rays, {hits} hits.");
            }
            catch (Exception ex)
            {
                RayFailures.Add(cid);
                InspectorPlugin.Log.LogInfo($"[debug view] {mesh.name}: asking the physics engine failed ({ex.GetBaseException().Message}); drawn as a box.");
            }
            return true;
        }

        private static void DrawRayShape(Camera cam, Matrix4x4 m, RayShape shape)
        {
            var world = new Vector3[ScanCells, ScanCells];
            foreach (Patch patch in shape.Patches)
            {
                float max2 = patch.MaxEdge * patch.MaxEdge;
                for (int i = 0; i < ScanCells; i++)
                {
                    for (int j = 0; j < ScanCells; j++)
                    {
                        if (patch.Hit[i, j]) world[i, j] = m.MultiplyPoint3x4(patch.Points[i, j]);
                    }
                }
                for (int i = 0; i < ScanCells; i++)
                {
                    for (int j = 0; j < ScanCells; j++)
                    {
                        if (!patch.Hit[i, j])
                        {
                            continue;
                        }
                        Vector3 a = world[i, j];
                        if (i + 1 < ScanCells && patch.Hit[i + 1, j] && (world[i + 1, j] - a).sqrMagnitude <= max2)
                        {
                            Seg(cam, a, world[i + 1, j]);
                        }
                        if (j + 1 < ScanCells && patch.Hit[i, j + 1] && (world[i, j + 1] - a).sqrMagnitude <= max2)
                        {
                            Seg(cam, a, world[i, j + 1]);
                        }
                    }
                }
            }
        }

        // Read-back bytes of a buffer that was never filled come back as zeros
        // or noise. Real vertices spread out, and stay inside the mesh's bounds.
        private static bool Plausible(Vector3[] vertices, Bounds bounds)
        {
            if (vertices.Length == 0)
            {
                return false;
            }
            Bounds grown = bounds;
            grown.Expand(bounds.size.magnitude * 0.05f + 0.001f);
            Vector3 first = vertices[0];
            bool spread = false;
            foreach (Vector3 v in vertices)
            {
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || !grown.Contains(v))
                {
                    return false;
                }
                if (!spread && (v - first).sqrMagnitude > 1e-8f)
                {
                    spread = true;
                }
            }
            return spread;
        }

        private static byte[] ReadBack(GraphicsBuffer buffer, string what, out string why)
        {
            why = null;
            if (buffer == null)
            {
                why = $"its {what} buffer could not be opened";
                return null;
            }
            using (buffer)
            {
                AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(buffer);
                req.WaitForCompletion();
                if (req.hasError)
                {
                    why = $"its {what} buffer could not be read back";
                    return null;
                }
                using (NativeArray<byte> data = req.GetData<byte>())
                {
                    return data.ToArray();
                }
            }
        }

        /// <summary>
        /// Draws the light's reach: a sphere for a point light, a cone for a
        /// spot light, an arrow for a directional one.
        /// </summary>
        internal static void DrawLight(Camera cam, Light light)
        {
            Transform t = light.transform;
            switch (light.type)
            {
                case LightType.Spot:
                    {
                        float half = light.spotAngle * 0.5f * Mathf.Deg2Rad;
                        float radius = light.range * Mathf.Tan(half);
                        Vector3 far = t.position + t.forward * light.range;
                        Circle(cam, far, t.right * radius, t.up * radius);
                        for (int i = 0; i < 4; i++)
                        {
                            float a = i * Mathf.PI * 0.5f;
                            Seg(cam, t.position, far + (t.right * Mathf.Cos(a) + t.up * Mathf.Sin(a)) * radius);
                        }
                        break;
                    }
                case LightType.Directional:
                    {
                        Vector3 p = t.position;
                        Circle(cam, p, t.right * 0.25f, t.up * 0.25f);
                        for (int i = 0; i < 4; i++)
                        {
                            float a = i * Mathf.PI * 0.5f;
                            Vector3 o = (t.right * Mathf.Cos(a) + t.up * Mathf.Sin(a)) * 0.25f;
                            Seg(cam, p + o, p + o + t.forward);
                        }
                        break;
                    }
                default:
                    Sphere(cam, t.position, Mathf.Max(0.05f, light.range));
                    break;
            }
        }

        // ---- the shapes ------------------------------------------------------------

        private static void Box(Camera cam, Matrix4x4 m, Vector3 centre, Vector3 half)
        {
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                p[i] = m.MultiplyPoint3x4(centre + new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z));
            }
            int[] edges = { 0, 1, 1, 3, 3, 2, 2, 0, 4, 5, 5, 7, 7, 6, 6, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
            for (int i = 0; i < edges.Length; i += 2)
            {
                Seg(cam, p[edges[i]], p[edges[i + 1]]);
            }
        }

        private static void Sphere(Camera cam, Vector3 centre, float radius)
        {
            Circle(cam, centre, Vector3.right * radius, Vector3.up * radius);
            Circle(cam, centre, Vector3.right * radius, Vector3.forward * radius);
            Circle(cam, centre, Vector3.up * radius, Vector3.forward * radius);
        }

        private static void Capsule(Camera cam, Vector3 centre, Vector3 axis, Vector3 u, Vector3 v, float radius, float height)
        {
            // The caps meet in the middle when the height is no more than two radii.
            float straight = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 top = centre + axis * straight, bottom = centre - axis * straight;
            Circle(cam, top, u * radius, v * radius);
            Circle(cam, bottom, u * radius, v * radius);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                Vector3 o = (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
                Seg(cam, bottom + o, top + o);
            }
            // The domes: a half circle in each of the two planes through the axis.
            Arc(cam, top, u * radius, axis * radius);
            Arc(cam, top, v * radius, axis * radius);
            Arc(cam, bottom, u * radius, -axis * radius);
            Arc(cam, bottom, v * radius, -axis * radius);
        }

        private static void Wireframe(Camera cam, Matrix4x4 m, MeshShape shape)
        {
            Vector3[] vertices = shape.Vertices;
            int[] triangles = shape.Triangles;
            if (triangles.Length == 0)
            {
                Points(cam, m, vertices);
                return;
            }
            int drawn = 0;
            for (int i = 0; i + 2 < triangles.Length && drawn < MeshEdgeCap; i += 3, drawn += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(vertices[triangles[i]]);
                Vector3 b = m.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                Vector3 c = m.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                Seg(cam, a, b);
                Seg(cam, b, c);
                Seg(cam, c, a);
            }
        }

        // Each vertex as a small cross, at most PointCap of them, spread evenly.
        private const int PointCap = 3000;

        private static void Points(Camera cam, Matrix4x4 m, Vector3[] vertices)
        {
            int step = Mathf.Max(1, vertices.Length / PointCap);
            for (int i = 0; i < vertices.Length; i += step)
            {
                Vector3 p = cam.WorldToScreenPoint(m.MultiplyPoint3x4(vertices[i]));
                if (p.z <= 0)
                {
                    continue;
                }
                float x = p.x, y = Screen.height - p.y;
                GL.Vertex3(x - 2, y, 0);
                GL.Vertex3(x + 3, y, 0);
                GL.Vertex3(x, y - 2, 0);
                GL.Vertex3(x, y + 3, 0);
            }
        }

        // ---- the lines -------------------------------------------------------------

        private static void Seg(Camera cam, Vector3 a, Vector3 b)
        {
            Vector3 pa = cam.WorldToScreenPoint(a);
            Vector3 pb = cam.WorldToScreenPoint(b);
            if (pa.z <= 0 || pb.z <= 0)
            {
                // A line crossing behind the camera: left out rather than drawn mirrored.
                return;
            }
            GL.Vertex3(pa.x, Screen.height - pa.y, 0);
            GL.Vertex3(pb.x, Screen.height - pb.y, 0);
        }

        // A full circle; the two axes carry the radius.
        private static void Circle(Camera cam, Vector3 centre, Vector3 u, Vector3 v, int segments = 24)
        {
            Vector3 prev = centre + u;
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 p = centre + u * Mathf.Cos(a) + v * Mathf.Sin(a);
                Seg(cam, prev, p);
                prev = p;
            }
        }

        // Half a circle, from u round through v to -u.
        private static void Arc(Camera cam, Vector3 centre, Vector3 u, Vector3 v, int segments = 12)
        {
            Vector3 prev = centre + u;
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI / segments;
                Vector3 p = centre + u * Mathf.Cos(a) + v * Mathf.Sin(a);
                Seg(cam, prev, p);
                prev = p;
            }
        }

        private static float Abs(float f)
        {
            return f < 0 ? -f : f;
        }

        private static float MaxScale(Vector3 scale)
        {
            return Mathf.Max(Abs(scale.x), Mathf.Max(Abs(scale.y), Abs(scale.z)));
        }
    }
}
