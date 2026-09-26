using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DragNWash.ModFramework
{
    // Direct3D 12 crash (Unity UUM-140564), found with the crash reports' GPU
    // trace: the first time the Tool window draws text, one frame uploaded the
    // same 1024x1024 font atlas 32 times (32 MB), then the scratch memory those
    // uploads went through was trimmed and Unity died in
    // D3D12ScratchAllocator::DestroyScratch.
    //
    // The uploads come from the font engines, not from mods: TextCore (IMGUI's
    // text in Unity 6) and TextMeshPro (the game's UI) queue a font asset's
    // atlas when a glyph is added and apply the queue after every piece of text
    // they lay out. A window of new text lays out dozens of labels, and each
    // one uploads the whole atlas again.
    //
    // On Direct3D 12 this takes the queue over: each atlas that changed is
    // uploaded once, at the end of the frame. A glyph added this frame may be
    // drawn blank for that one frame. Other graphics APIs keep the engines' own
    // behaviour. [Direct3D12] BatchFontAtlasUploads switches it off.
    //
    // Many different atlases in one frame crash it the same way: the first
    // time the Tool window checked every character mods had prepared (about
    // 870 for the Localization mod), glyphs went into the window font and six
    // OS fallback fonts, and one frame uploaded 35 atlases. So at most
    // MaxPerFrame atlases go up in a frame; the rest wait for the next ones,
    // oldest first, and a glyph in them may be blank for a few frames.
    internal static class FontAtlasUploads
    {
        private const int MaxPerFrame = 4;
        private static ConfigEntry<bool> _enabled;
        private static readonly List<Texture2D> Pending = new List<Texture2D>();
        private static readonly HashSet<int> PendingIds = new HashSet<int>();
        private static readonly Dictionary<Type, (FieldInfo queue, FieldInfo lookup)> Queues = new Dictionary<Type, (FieldInfo, FieldInfo)>();
        private static int _batched, _uploaded;

        internal static void Install(ConfigFile config, Harmony harmony, MonoBehaviour host)
        {
            _enabled = config.Bind("Direct3D12", "BatchFontAtlasUploads", true,
                new ConfigDescription("On Direct3D 12, uploads each changed font atlas once per frame instead of once per piece of text, which avoids the crash when a screen full of new text opens (Unity UUM-140564). A glyph may be drawn blank for one frame. Takes effect at the next start.",
                    null, new SettingMeta { DisplayName = "Batch font atlas uploads (Direct3D 12)", Advanced = true, RequiresRestart = true },
                    new SectionMeta { DisplayName = "Direct3D 12" }));
            if (!GameInfo.IsDirect3D12 || !_enabled.Value)
            {
                return;
            }
            int hooked = 0;
            foreach (string typeName in new[] { "UnityEngine.TextCore.Text.FontAsset", "TMPro.TMP_FontAsset" })
            {
                Type type = AccessTools.TypeByName(typeName);
                MethodInfo method = type != null ? AccessTools.Method(type, "UpdateAtlasTexturesInQueue") : null;
                FieldInfo queue = type != null ? AccessTools.Field(type, "k_FontAssets_AtlasTexturesUpdateQueue") : null;
                FieldInfo lookup = type != null ? AccessTools.Field(type, "k_FontAssets_AtlasTexturesUpdateQueueLookup") : null;
                if (method == null || queue == null || lookup == null || !method.IsStatic || !queue.IsStatic || !lookup.IsStatic)
                {
                    ModFramework.Log.LogInfo($"[d3d12] {typeName}: no atlas upload queue to batch in this version.");
                    continue;
                }
                try
                {
                    Queues[type] = (queue, lookup);
                    harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(FontAtlasUploads), nameof(Take))));
                    hooked++;
                }
                catch (Exception ex)
                {
                    Queues.Remove(type);
                    ModFramework.Log.LogWarning($"[d3d12] Could not batch {typeName} atlas uploads: {ex.Message}");
                }
            }
            if (hooked > 0)
            {
                GameInfo.FontAtlasUploadsBatched = true;
                host.StartCoroutine(Flush());
                ModFramework.Log.LogInfo($"[d3d12] Font atlas uploads are batched to once per frame ({hooked} font engine(s)).");
            }
        }

        // Instead of applying the queue now, remember its textures for the end of the frame.
        private static bool Take(MethodBase __originalMethod)
        {
            try
            {
                if (!Queues.TryGetValue(__originalMethod.DeclaringType, out var fields))
                {
                    return true;
                }
                var queue = (IList)fields.queue.GetValue(null);
                var lookup = fields.lookup.GetValue(null);
                if (queue == null || queue.Count == 0)
                {
                    return false;
                }
                foreach (object item in queue)
                {
                    if (item is Texture2D texture && texture != null && PendingIds.Add(texture.GetInstanceID()))
                    {
                        Pending.Add(texture);
                    }
                    _batched++;
                }
                queue.Clear();
                lookup?.GetType().GetMethod("Clear")?.Invoke(lookup, null);
                return false;
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"[d3d12] Batching an atlas upload failed, uploading it now: {ex.Message}");
                return true;
            }
        }

        private static IEnumerator Flush()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                if (Pending.Count == 0)
                {
                    continue;
                }
                int count = Math.Min(MaxPerFrame, Pending.Count);
                for (int i = 0; i < count; i++)
                {
                    Texture2D texture = Pending[i];
                    try
                    {
                        if (texture != null)
                        {
                            texture.Apply(false, false);
                            _uploaded++;
                        }
                    }
                    catch (Exception ex)
                    {
                        ModFramework.Log.LogWarning($"[d3d12] A font atlas upload failed: {ex.Message}");
                    }
                }
                if (CrashReports.Recording)
                {
                    CrashReports.Write("d3d12", $"font atlas: {count} upload(s) this frame, {Pending.Count - count} left for the next frames, for {_batched} request(s) so far ({_uploaded} uploads in all)");
                }
                // An atlas that changes again while it waits is still one
                // upload, and it goes up with what it has by then.
                for (int i = 0; i < count; i++)
                {
                    Texture2D done = Pending[i];
                    if (done != null)
                    {
                        PendingIds.Remove(done.GetInstanceID());
                    }
                }
                Pending.RemoveRange(0, count);
            }
        }
    }
}
