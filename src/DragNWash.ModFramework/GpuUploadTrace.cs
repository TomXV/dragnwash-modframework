using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace DragNWash.ModFramework
{
    // The optional GPU source for crash reports ([Diagnostics] TraceGpuUploads),
    // made for the Direct3D 12 crash (Unity UUM-140564). Every dump of it ends
    // the same way, on the render thread:
    //
    //   GfxDeviceD3D12::QueueExecute / QueuePresent
    //     -> D3D12ScratchAllocator::ReclaimMemory -> ReleaseExcessScratch
    //     -> DestroyScratch   (access violation)
    //
    // Uploads to the GPU go through scratch memory, which grows when a frame
    // uploads a lot and is trimmed a few frames later; the trim is where it
    // dies. To learn which upload makes it grow, this notes every texture
    // upload and creation, dynamic font and TextMeshPro atlas growth, mesh
    // upload, asset bundle load and released GPU resource, with its size and
    // the mod on the calling stack. It only watches.
    internal static class GpuUploadTrace
    {
        [ThreadStatic] private static bool _inside;

        // Patches what can be patched (extern methods cannot; their managed
        // overloads usually can) and returns how many methods it hooked.
        internal static int Install(Harmony harmony)
        {
            int patched = 0;
            void Hook(string typeName, string method, bool constructors = false)
            {
                Type type = AccessTools.TypeByName(typeName);
                if (type == null)
                {
                    CrashReports.Write("gpu-setup", $"type {typeName} not found");
                    return;
                }
                IEnumerable<MethodBase> targets = constructors
                    ? AccessTools.GetDeclaredConstructors(type).Cast<MethodBase>()
                    : AccessTools.GetDeclaredMethods(type).Where(m => m.Name == method).Cast<MethodBase>();
                foreach (MethodBase target in targets)
                {
                    try
                    {
                        if (constructors) harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(GpuUploadTrace), nameof(AfterConstructor))));
                        else harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(GpuUploadTrace), nameof(BeforeCall))));
                        patched++;
                    }
                    catch (Exception ex)
                    {
                        CrashReports.Write("gpu-setup", $"skip {type.Name}.{target.Name}({string.Join(",", target.GetParameters().Select(p => p.ParameterType.Name))}): {ex.GetType().Name}");
                    }
                }
            }

            Hook("UnityEngine.Texture2D", "Apply");
            Hook("UnityEngine.Texture2D", "Reinitialize");
            Hook("UnityEngine.Texture2D", null, constructors: true);
            Hook("UnityEngine.ImageConversion", "LoadImage");
            Hook("UnityEngine.RenderTexture", "Create");
            Hook("UnityEngine.RenderTexture", "Release");
            Hook("UnityEngine.Mesh", "UploadMeshData");
            Hook("UnityEngine.Object", "Destroy");
            Hook("UnityEngine.Object", "DestroyImmediate");
            Hook("UnityEngine.AssetBundle", "LoadFromFile");
            Hook("UnityEngine.AssetBundle", "LoadFromMemory");
            Hook("UnityEngine.AssetBundle", "Unload");
            Hook("UnityEngine.Resources", "UnloadUnusedAssets");
            Hook("UnityEngine.Font", "RequestCharactersInTexture");
            Hook("TMPro.TMP_FontAsset", "TryAddCharacters");
            Hook("TMPro.TMP_FontAsset", "CreateFontAsset");
            Hook("TMPro.TMP_FontAsset", "ClearFontAssetData");

            // The IMGUI and uGUI dynamic font atlases: rebuilt, and uploaded
            // whole, when a character they do not hold is drawn.
            Font.textureRebuilt += font =>
            {
                Texture t = font != null ? font.material?.mainTexture : null;
                Note("font-atlas-rebuilt", $"{font?.name} {(t != null ? t.width + "x" + t.height : "?")}");
            };
            CrashReports.Write("gpu-setup", $"{patched} methods hooked");
            return patched;
        }

        private static void BeforeCall(MethodBase __originalMethod, object __instance, object[] __args)
        {
            if (!CrashReports.TracingGpu || _inside) return;
            try
            {
                _inside = true;
                string name = __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name;
                // Only GPU resources are interesting among destroyed objects.
                if (__originalMethod.Name.StartsWith("Destroy", StringComparison.Ordinal))
                {
                    object target = __args != null && __args.Length > 0 ? __args[0] : null;
                    if (!(target is Texture || target is Mesh || target is Font || (target != null && target.GetType().Name == "TMP_FontAsset")))
                    {
                        return;
                    }
                    Note(name, Describe(target));
                    return;
                }
                var sb = new StringBuilder();
                if (__instance != null) sb.Append(Describe(__instance));
                if (__args != null)
                {
                    foreach (object a in __args)
                    {
                        if (a is byte[] bytes) sb.Append(" bytes=").Append(bytes.Length);
                        else if (a is string s) sb.Append(" \"").Append(s.Length > 40 ? s.Substring(0, 40) + "..(" + s.Length + ")" : s).Append('"');
                        else if (a is uint[] codes) sb.Append(" chars=").Append(codes.Length);
                        else if (a is UnityEngine.Object o && sb.Length == 0) sb.Append(Describe(o));
                    }
                }
                Note(name, sb.ToString().Trim());
            }
            catch
            {
                // Tracing must never break the game.
            }
            finally
            {
                _inside = false;
            }
        }

        private static void AfterConstructor(object __instance)
        {
            if (!CrashReports.TracingGpu || _inside) return;
            try
            {
                _inside = true;
                Note("Texture2D.new", Describe(__instance));
            }
            catch
            {
            }
            finally
            {
                _inside = false;
            }
        }

        private static string Describe(object o)
        {
            try
            {
                switch (o)
                {
                    case Texture2D t: return $"'{t.name}' {t.width}x{t.height} {t.format} mips={t.mipmapCount}";
                    case RenderTexture r: return $"'{r.name}' {r.width}x{r.height} {r.format}";
                    case Texture t: return $"'{t.name}' {t.width}x{t.height}";
                    case Mesh m: return $"'{m.name}' {m.vertexCount} verts";
                    case Font f: return $"font '{f.name}'";
                    case UnityEngine.Object u: return $"{u.GetType().Name} '{u.name}'";
                    default: return o?.GetType().Name ?? "";
                }
            }
            catch
            {
                return o?.GetType().Name ?? "";
            }
        }

        private static void Note(string what, string detail)
        {
            string via = Via();
            CrashReports.Write("gpu", what + " " + detail + (via != null ? " via " + via : ""), Owner());
        }

        // The method that made the call, when it is not a mod's: which part of
        // Unity (TextCore's font atlas queue, a font asset being created...)
        // did the upload for the mod further down the stack.
        private static string Via()
        {
            var trace = new StackTrace(3, false);
            for (int i = 0; i < trace.FrameCount && i < 12; i++)
            {
                MethodBase m = trace.GetFrame(i)?.GetMethod();
                Type type = m?.DeclaringType;
                if (type == null || type == typeof(GpuUploadTrace) || type == typeof(CrashReports)) continue;
                if (type == typeof(Texture2D) || type == typeof(UnityEngine.Object)) continue;
                string ns = type.Namespace ?? "";
                return ns.StartsWith("UnityEngine", StringComparison.Ordinal) || ns.StartsWith("TMPro", StringComparison.Ordinal)
                    ? type.Name + "." + m.Name
                    : null;
            }
            return null;
        }

        private static Dictionary<Assembly, string> _plugins;

        // The first mod on the calling stack; "game" when none is.
        private static string Owner()
        {
            if (_plugins == null || _plugins.Count < Chainloader.PluginInfos.Count)
            {
                var map = new Dictionary<Assembly, string>();
                foreach (PluginInfo plugin in Chainloader.PluginInfos.Values)
                {
                    Assembly assembly = plugin.Instance != null ? plugin.Instance.GetType().Assembly : null;
                    if (assembly != null && !map.ContainsKey(assembly)) map[assembly] = plugin.Metadata.GUID;
                }
                _plugins = map;
            }
            var trace = new StackTrace(3, false);
            for (int i = 0; i < trace.FrameCount; i++)
            {
                Type type = trace.GetFrame(i)?.GetMethod()?.DeclaringType;
                if (type == null || type == typeof(GpuUploadTrace) || type == typeof(CrashReports)) continue;
                if (_plugins.TryGetValue(type.Assembly, out string guid))
                {
                    return guid + " (" + type.Name + ")";
                }
            }
            return "game";
        }
    }
}
