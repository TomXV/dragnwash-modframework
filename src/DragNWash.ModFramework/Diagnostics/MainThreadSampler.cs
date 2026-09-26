using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx.Bootstrap;

namespace DragNWash.ModFramework.Diagnostics
{
    /// <summary>One row of <see cref="MainThreadSampler.Report"/>.</summary>
    public sealed class SampleRow
    {
        /// <summary>The frame, as the stacks write it.</summary>
        public string Frame { get; internal set; }

        /// <summary>Samples with this frame innermost (or, with mods only, the innermost mod frame).</summary>
        public int Self { get; internal set; }

        /// <summary>Samples with this frame anywhere on the stack.</summary>
        public int Total { get; internal set; }
    }

    /// <summary>
    /// A small sampling profiler for the main thread: a few times a second it
    /// notes where the main thread is, and says which methods it was found in
    /// most. Managed frames through <see cref="ManagedStacks"/> everywhere;
    /// with native frames through <see cref="NativeStacks"/> on Windows.
    /// Keeps the last ten minutes. Experimental (core 1.7).
    /// </summary>
    public static class MainThreadSampler
    {
        private const int MaxSamples = 6000;
        private const string NoFrames = "(no managed frames: in Unity's native code)";

        private static readonly List<string[]> Samples = new List<string[]>();
        private static Thread _thread;
        private static volatile bool _running;
        private static int _perSecond = 10;
        private static bool _native;
        private static DateTime _started;
        private static HashSet<string> _modTypes;

        /// <summary>Whether it is sampling now.</summary>
        public static bool Running => _running;

        /// <summary>Samples kept.</summary>
        public static int Count { get { lock (Samples) { return Samples.Count; } } }

        /// <summary>When the samples kept began.</summary>
        public static DateTime Started => _started;

        /// <summary>Samples a second, 5 or 10.</summary>
        public static int PerSecond => _perSecond;

        /// <summary>Whether the samples carry native frames.</summary>
        public static bool Native => _native;

        /// <summary>Starts sampling (clearing what was kept), or changes the rate. Native frames only on 64-bit Windows.</summary>
        public static void Start(int perSecond, bool native)
        {
            Stop();
            _perSecond = perSecond >= 10 ? 10 : 5;
            _native = native && NativeStacks.Available;
            lock (Samples) Samples.Clear();
            _started = DateTime.Now;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "MainThreadSampler" };
            _thread.Start();
            ModFramework.Log.LogInfo($"[sampler] started: {_perSecond} a second{(_native ? ", with native frames" : "")}.");
        }

        /// <summary>Stops sampling; what was kept stays until the next start.</summary>
        public static void Stop()
        {
            if (!_running) return;
            _running = false;
            _thread?.Join(1000);
            _thread = null;
            ModFramework.Log.LogInfo($"[sampler] stopped with {Count} samples.");
        }

        /// <summary>
        /// The frames found most, the most first: by <see cref="SampleRow.Self"/>
        /// or by <see cref="SampleRow.Total"/>. With <paramref name="modsOnly"/>,
        /// only frames in mods' code (from BepInEx/plugins), each sample counted
        /// for its innermost mod frame.
        /// </summary>
        public static List<SampleRow> Report(bool byTotal, bool modsOnly, int max = 50)
        {
            string[][] samples;
            lock (Samples) samples = Samples.ToArray();
            var rows = new Dictionary<string, SampleRow>();
            var seen = new HashSet<string>();
            foreach (string[] s in samples)
            {
                seen.Clear();
                bool selfDone = false;
                foreach (string f in s)
                {
                    if (modsOnly && !IsMod(f)) continue;
                    if (!rows.TryGetValue(f, out SampleRow row)) rows[f] = row = new SampleRow { Frame = f };
                    if (!selfDone)
                    {
                        row.Self++;
                        selfDone = true;
                    }
                    if (seen.Add(f)) row.Total++;
                }
            }
            IEnumerable<SampleRow> sorted = byTotal
                ? rows.Values.OrderByDescending(r => r.Total).ThenByDescending(r => r.Self)
                : rows.Values.OrderByDescending(r => r.Self).ThenByDescending(r => r.Total);
            return sorted.Where(r => byTotal ? r.Total > 0 : r.Self > 0).Take(max).ToList();
        }

        /// <summary>What called <paramref name="frame"/>: the frame just outside it in each sample it was in, the most first.</summary>
        public static List<SampleRow> Callers(string frame, int max = 8)
        {
            string[][] samples;
            lock (Samples) samples = Samples.ToArray();
            var rows = new Dictionary<string, SampleRow>();
            foreach (string[] s in samples)
            {
                int i = Array.IndexOf(s, frame);
                if (i < 0) continue;
                string caller = i + 1 < s.Length ? s[i + 1] : "(the thread's start)";
                if (!rows.TryGetValue(caller, out SampleRow row)) rows[caller] = row = new SampleRow { Frame = caller };
                row.Total++;
            }
            return rows.Values.OrderByDescending(r => r.Total).Take(max).ToList();
        }

        /// <summary>Whether a frame's type comes from a mod in BepInEx/plugins.</summary>
        public static bool IsMod(string frame)
        {
            if (frame == null || NativeStacks.IsNative(frame)) return false;
            if (_modTypes == null) _modTypes = ModTypes();
            // "Namespace.Type:Method (args) + 0x1a" from Mono, or
            // "Namespace.Type.Method (+0x1a)" from ManagedStacks.
            int space = frame.IndexOf(' ');
            string head = space >= 0 ? frame.Substring(0, space) : frame;
            int colon = head.IndexOf(':');
            int cut = colon > 0 ? colon : head.LastIndexOf('.');
            if (cut <= 0) return false;
            string type = head.Substring(0, cut).Replace('+', '.').Replace('/', '.');
            int generic = type.IndexOf('<');
            if (generic > 0) type = type.Substring(0, generic);
            return _modTypes.Contains(type);
        }

        private static HashSet<string> ModTypes()
        {
            var types = new HashSet<string>(StringComparer.Ordinal);
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                Assembly a = info.Instance != null ? info.Instance.GetType().Assembly : null;
                if (a == null) continue;
                try
                {
                    foreach (Type t in a.GetTypes()) types.Add(TypeKey(t.FullName));
                }
                catch (ReflectionTypeLoadException ex)
                {
                    foreach (Type t in ex.Types) if (t != null) types.Add(TypeKey(t.FullName));
                }
            }
            return types;
        }

        private static string TypeKey(string fullName)
        {
            int tick = fullName.IndexOf('`');
            return (tick > 0 ? fullName.Substring(0, tick) : fullName).Replace('+', '.');
        }

        private static void Loop()
        {
            int interval = 1000 / _perSecond;
            while (_running)
            {
                DateTime next = DateTime.Now.AddMilliseconds(interval);
                try
                {
                    string[] frames = Take();
                    if (frames != null)
                    {
                        lock (Samples)
                        {
                            Samples.Add(frames);
                            if (Samples.Count > MaxSamples) Samples.RemoveRange(0, Samples.Count - MaxSamples);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModFramework.Log.LogWarning("[sampler] a sample failed, stopping: " + ex.Message);
                    _running = false;
                    return;
                }
                int wait = (int)(next - DateTime.Now).TotalMilliseconds;
                if (wait > 0) Thread.Sleep(wait);
            }
        }

        // The main thread's frames now, innermost first, with the types written
        // the way IsMod reads them.
        private static string[] Take()
        {
            if (_native)
            {
                long[] ips = NativeStacks.CaptureIps(NativeStacks.MainThreadId, out _);
                if (ips == null) return null;
                var frames = new string[ips.Length];
                for (int i = 0; i < ips.Length; i++) frames[i] = NativeStacks.Resolve(ips[i]);
                return frames;
            }
            List<ThreadStack> threads = ManagedStacks.Capture(out _);
            ThreadStack main = threads?.Find(t => t.IsMain);
            if (main == null) return null;
            return main.Frames.Count == 0 ? new[] { NoFrames } : main.Frames.ToArray();
        }
    }
}
