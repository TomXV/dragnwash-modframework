using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace DragNWash.ModFramework.Inspector
{
    // Estimated sizes for the object explorer: a release build of Unity does
    // not say how much memory a texture, mesh or sound takes, so it is worked
    // out from what they do say (size, format, mips, vertex layout, samples).
    // -1 when it cannot be worked out, -2 for kinds that get no estimate.
    // Experimental.
    internal static class InspectorSizes
    {
        internal const long Unknown = -1;
        internal const long NotEstimated = -2;

        internal static long Estimate(UnityEngine.Object o, ObjectKind kind)
        {
            try
            {
                switch (kind)
                {
                    case ObjectKind.Textures: return o is Texture t ? Texture(t) : Unknown;
                    case ObjectKind.Meshes: return o is Mesh m ? Mesh(m) : Unknown;
                    case ObjectKind.Audio: return Audio(o);
                    default: return NotEstimated;
                }
            }
            catch (Exception)
            {
                return Unknown;
            }
        }

        // "~21.3 MB", "size ?" or "".
        internal static string Text(long size)
        {
            if (size == NotEstimated) return "";
            return size < 0 ? "size ?" : "~" + Diagnostics.MemoryWatch.Size(size);
        }

        private static long Texture(Texture t)
        {
            if (t is RenderTexture rt)
            {
                long color = rt.graphicsFormat == GraphicsFormat.None ? 0 : Levels(rt.width, rt.height, rt.graphicsFormat, rt.useMipMap ? rt.mipmapCount : 1);
                color *= Math.Max(1, rt.volumeDepth) * Math.Max(1, rt.antiAliasing);
                long depth = rt.depthStencilFormat == GraphicsFormat.None ? 0 : (long)rt.width * rt.height * 4 * Math.Max(1, rt.antiAliasing);
                return color + depth;
            }
            GraphicsFormat format = t.graphicsFormat;
            if (format == GraphicsFormat.None) return Unknown;
            long size = Levels(t.width, t.height, format, Math.Max(1, t.mipmapCount));
            switch (t)
            {
                case Cubemap _: return size * 6;
                case Texture2DArray a: return size * Math.Max(1, a.depth);
                case Texture3D d: return size * Math.Max(1, d.depth);
                case CubemapArray ca: return size * 6 * Math.Max(1, ca.cubemapCount);
            }
            return size;
        }

        private static long Levels(int width, int height, GraphicsFormat format, int mips)
        {
            long total = 0;
            for (int i = 0; i < mips; i++)
            {
                total += GraphicsFormatUtility.ComputeMipmapSize(Math.Max(1, width >> i), Math.Max(1, height >> i), format);
            }
            return total;
        }

        private static long Mesh(Mesh m)
        {
            long vertices = 0;
            for (int s = 0; s < m.vertexBufferCount; s++)
            {
                vertices += (long)m.GetVertexBufferStride(s) * m.vertexCount;
            }
            long indices = 0;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                indices += m.GetIndexCount(s);
            }
            return vertices + indices * (m.indexFormat == IndexFormat.UInt16 ? 2 : 4);
        }

        // AudioClip is in a module this library does not reference: read by
        // reflection. Only a clip decompressed on load has a size to work out
        // (16-bit samples); a compressed or streamed one is unknown.
        private static long Audio(UnityEngine.Object o)
        {
            Type t = o.GetType();
            object loadType = t.GetProperty("loadType")?.GetValue(o, null);
            if (loadType == null || loadType.ToString() != "DecompressOnLoad") return Unknown;
            object samples = t.GetProperty("samples")?.GetValue(o, null);
            object channels = t.GetProperty("channels")?.GetValue(o, null);
            if (!(samples is int n) || !(channels is int c)) return Unknown;
            return (long)n * c * 2;
        }
    }
}
