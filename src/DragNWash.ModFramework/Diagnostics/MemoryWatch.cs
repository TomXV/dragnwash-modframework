using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine.Profiling;

namespace DragNWash.ModFramework.Diagnostics
{
    /// <summary>One reading of the game's memory, see <see cref="MemoryWatch"/>.</summary>
    public sealed class MemorySample
    {
        /// <summary>When it was taken.</summary>
        public DateTime Time { get; internal set; }

        /// <summary>Managed (GC) heap in use, bytes.</summary>
        public long GcUsed { get; internal set; }

        /// <summary>Managed (GC) heap reserved from the system, bytes.</summary>
        public long GcReserved { get; internal set; }

        /// <summary>Garbage collections since the game started.</summary>
        public int Collections { get; internal set; }

        /// <summary>A collection ran since the sample before this one.</summary>
        public bool GcRan { get; internal set; }

        /// <summary>What Unity's memory manager has allocated, bytes.</summary>
        public long UnityAllocated { get; internal set; }

        /// <summary>What Unity's memory manager has reserved, bytes.</summary>
        public long UnityReserved { get; internal set; }

        /// <summary>Memory the system counts for the game, bytes; -1 when this Unity does not say.</summary>
        public long SystemUsed { get; internal set; } = -1;

        /// <summary>Audio memory in use, bytes; -1 when this Unity does not say.</summary>
        public long Audio { get; internal set; } = -1;

        /// <summary>Video memory in use (video playback, not the GPU), bytes; -1 when this Unity does not say.</summary>
        public long Video { get; internal set; } = -1;
    }

    /// <summary>
    /// The game's memory as a release build lets it be read: the managed heap
    /// and Unity's own totals, once a second while Developer tools are on, for
    /// the last five minutes. Per-object sizes (textures, meshes) need a
    /// development build and are not here. Experimental (core 1.7).
    /// </summary>
    public static class MemoryWatch
    {
        /// <summary>How many samples <see cref="History"/> keeps: five minutes at one a second.</summary>
        public const int Capacity = 300;

        private static readonly List<MemorySample> Samples = new List<MemorySample>();
        private static DateTime _next;
        private static int _lastCollections = -1;
        private static ProfilerRecorder _system, _audio, _video;
        private static bool _recording;

        /// <summary>The samples, oldest first. Empty while Developer tools are off.</summary>
        public static IReadOnlyList<MemorySample> History
        {
            get { lock (Samples) { return Samples.ToArray(); } }
        }

        /// <summary>The last sample, or null when none was taken.</summary>
        public static MemorySample Latest
        {
            get { lock (Samples) { return Samples.Count > 0 ? Samples[Samples.Count - 1] : null; } }
        }

        // From the plugin's Update.
        internal static void Tick()
        {
            if (!DeveloperTools.Enabled)
            {
                if (_recording) Stop();
                return;
            }
            DateTime now = DateTime.Now;
            if (now < _next) return;
            _next = now.AddSeconds(1);
            try
            {
                MemorySample s = Now();
                lock (Samples)
                {
                    Samples.Add(s);
                    if (Samples.Count > Capacity) Samples.RemoveRange(0, Samples.Count - Capacity);
                }
            }
            catch (Exception ex)
            {
                // Once, then no more samples this session.
                ModFramework.Log.LogWarning($"[memory] reading the memory failed, the Memory tab stops: {ex.Message}");
                _next = DateTime.MaxValue;
            }
        }

        /// <summary>A reading taken now. Main thread only.</summary>
        public static MemorySample Now()
        {
            if (!_recording) Start();
            int collections = GC.CollectionCount(0);
            var s = new MemorySample
            {
                Time = DateTime.Now,
                GcUsed = Profiler.GetMonoUsedSizeLong(),
                GcReserved = Profiler.GetMonoHeapSizeLong(),
                Collections = collections,
                GcRan = _lastCollections >= 0 && collections != _lastCollections,
                UnityAllocated = Profiler.GetTotalAllocatedMemoryLong(),
                UnityReserved = Profiler.GetTotalReservedMemoryLong(),
                SystemUsed = Read(_system),
                Audio = Read(_audio),
                Video = Read(_video),
            };
            if (s.GcUsed <= 0) s.GcUsed = GC.GetTotalMemory(false);
            _lastCollections = collections;
            return s;
        }

        /// <summary>
        /// Every memory counter this Unity build offers, with its value now, as
        /// "name: value" lines. Main thread only.
        /// </summary>
        public static List<string> Counters()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var lines = new List<string>();
            foreach (ProfilerRecorderHandle h in handles)
            {
                ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                if (d.Category != ProfilerCategory.Memory) continue;
                using (ProfilerRecorder r = ProfilerRecorder.StartNew(d.Category, d.Name))
                {
                    long v = r.Valid ? r.CurrentValue : -1;
                    lines.Add($"{d.Name}: {(d.UnitType == ProfilerMarkerDataUnit.Bytes ? Size(v) : v.ToString())}");
                }
            }
            lines.Sort(StringComparer.OrdinalIgnoreCase);
            return lines;
        }

        /// <summary>Runs a full garbage collection now and says what it freed. Main thread only.</summary>
        public static string CollectNow()
        {
            long before = GC.GetTotalMemory(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long after = GC.GetTotalMemory(false);
            string line = $"Garbage collection: {Size(before)} -> {Size(after)} ({Size(Math.Max(0, before - after))} freed).";
            ModFramework.Log.LogInfo("[memory] " + line);
            return line;
        }

        /// <summary>A few lines for people: the sample, and how the last minute went.</summary>
        public static string Describe(MemorySample s)
        {
            if (s == null) return "No memory reading yet.";
            var sb = new StringBuilder();
            sb.Append($"GC (managed): {Size(s.GcUsed)} used / {Size(s.GcReserved)} reserved, {s.Collections} collections");
            string mode = GcMode();
            if (mode != null) sb.Append($", {mode}");
            sb.Append('\n');
            sb.Append($"Unity: {Size(s.UnityAllocated)} allocated / {Size(s.UnityReserved)} reserved ({Size(Math.Max(0, s.UnityReserved - s.UnityAllocated))} unused)\n");
            var more = new List<string>();
            if (s.SystemUsed >= 0) more.Add("System used " + Size(s.SystemUsed));
            if (s.Audio >= 0) more.Add("Audio " + Size(s.Audio));
            if (s.Video >= 0) more.Add("Video " + Size(s.Video));
            if (more.Count > 0) sb.Append(string.Join(" | ", more.ToArray())).Append('\n');
            IReadOnlyList<MemorySample> h = History;
            int from = -1;
            for (int i = h.Count - 1; i >= 0; i--)
            {
                if ((s.Time - h[i].Time).TotalSeconds > 60) break;
                from = i;
            }
            if (from >= 0 && h.Count - from > 1)
            {
                int ran = 0;
                for (int i = from + 1; i < h.Count; i++) if (h[i].GcRan) ran++;
                sb.Append($"Last {(h[h.Count - 1].Time - h[from].Time).TotalSeconds:0} s: GC used {Size(h[from].GcUsed)} -> {Size(h[h.Count - 1].GcUsed)}, {ran} collection{(ran == 1 ? "" : "s")}\n");
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>The garbage collector's mode, e.g. "incremental, 3 ms slice"; null when Unity does not say.</summary>
        public static string GcMode()
        {
            try
            {
                if (!UnityEngine.Scripting.GarbageCollector.isIncremental) return "not incremental";
                return $"incremental, {UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds / 1000000.0:0.#} ms slice";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Bytes for people: "12.4 MB", "1.21 GB".</summary>
        public static string Size(long bytes)
        {
            if (bytes < 0) return "?";
            if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.00} GB";
            if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.0} MB";
            if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):0} KB";
            return bytes + " B";
        }

        private static void Start()
        {
            _system = Recorder("System Used Memory");
            _audio = Recorder("Audio Used Memory");
            _video = Recorder("Video Used Memory");
            _recording = true;
        }

        // Developer tools went off: nothing is kept or read.
        private static void Stop()
        {
            _system.Dispose();
            _audio.Dispose();
            _video.Dispose();
            _recording = false;
            _lastCollections = -1;
            _next = DateTime.MinValue;
            lock (Samples) { Samples.Clear(); }
        }

        private static ProfilerRecorder Recorder(string name)
        {
            try
            {
                return ProfilerRecorder.StartNew(ProfilerCategory.Memory, name);
            }
            catch
            {
                return default;
            }
        }

        private static long Read(ProfilerRecorder r)
        {
            try
            {
                return r.Valid ? r.CurrentValue : -1;
            }
            catch
            {
                return -1;
            }
        }
    }
}
