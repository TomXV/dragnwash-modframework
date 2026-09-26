using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;

namespace DragNWash.ModFramework.Diagnostics
{
    /// <summary>One managed thread and where it is, see <see cref="ManagedStacks"/>.</summary>
    public sealed class ThreadStack
    {
        /// <summary>The managed thread id.</summary>
        public int Id { get; internal set; }

        /// <summary>The thread's name, or "" when it has none.</summary>
        public string Name { get; internal set; }

        /// <summary>Unity's main thread.</summary>
        public bool IsMain { get; internal set; }

        /// <summary>Innermost first: "Type.Method (+0x3a)" with the IL offset.</summary>
        public IReadOnlyList<string> Frames { get; internal set; }
    }

    /// <summary>
    /// Where every managed thread is right now, main thread first. Uses Mono's
    /// own Thread.Mono_GetStackTraces, which stops each thread for a moment;
    /// works from any thread and on Linux too. Threads Unity runs natively
    /// (jobs, rendering) are not managed and are not here. Experimental (core 1.7).
    /// </summary>
    public static class ManagedStacks
    {
        private static readonly MethodInfo GetStackTraces =
            typeof(Thread).GetMethod("Mono_GetStackTraces", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>Unity's main thread, as a managed thread id.</summary>
        public static int MainThreadId { get; internal set; } = -1;

        /// <summary>Whether this Mono has the call it needs.</summary>
        public static bool Available => GetStackTraces != null;

        /// <summary>Every managed thread's stack, main thread first; null with <paramref name="reason"/> when it could not be read.</summary>
        public static List<ThreadStack> Capture(out string reason)
        {
            reason = null;
            if (GetStackTraces == null)
            {
                reason = "this Mono has no Thread.Mono_GetStackTraces";
                return null;
            }
            IDictionary traces;
            try
            {
                traces = GetStackTraces.Invoke(null, null) as IDictionary;
            }
            catch (Exception ex)
            {
                reason = (ex.InnerException ?? ex).Message;
                return null;
            }
            if (traces == null)
            {
                reason = "Mono returned nothing";
                return null;
            }
            var list = new List<ThreadStack>();
            foreach (DictionaryEntry e in traces)
            {
                var thread = e.Key as Thread;
                var trace = e.Value as StackTrace;
                if (thread == null) continue;
                var frames = new List<string>();
                StackFrame[] all = trace?.GetFrames();
                if (all != null)
                {
                    foreach (StackFrame f in all)
                    {
                        frames.Add(Frame(f));
                    }
                }
                list.Add(new ThreadStack
                {
                    Id = thread.ManagedThreadId,
                    Name = thread.Name ?? "",
                    IsMain = thread.ManagedThreadId == MainThreadId,
                    Frames = frames,
                });
            }
            list.Sort((a, b) => a.IsMain != b.IsMain ? (a.IsMain ? -1 : 1) : a.Id.CompareTo(b.Id));
            return list;
        }

        // For the hang watchdog: the capture runs on a thread of its own, so a
        // thread that cannot be stopped holds up nothing but that thread.
        internal static List<ThreadStack> CaptureWithin(int milliseconds, out string reason)
        {
            List<ThreadStack> result = null;
            string why = null;
            var worker = new Thread(() => { result = Capture(out why); }) { IsBackground = true, Name = "ManagedStacks capture" };
            worker.Start();
            if (!worker.Join(milliseconds))
            {
                reason = $"no answer within {milliseconds / 1000} s";
                return null;
            }
            reason = why;
            return result;
        }

        /// <summary>
        /// For people: each thread as "[main] name" or "[t14] name" and its
        /// frames, at most <paramref name="maxFrames"/> each (0 for all).
        /// </summary>
        public static string Text(IEnumerable<ThreadStack> threads, int maxFrames)
        {
            var sb = new StringBuilder();
            foreach (ThreadStack t in threads)
            {
                sb.Append(Label(t)).Append('\n');
                int shown = maxFrames > 0 ? Math.Min(maxFrames, t.Frames.Count) : t.Frames.Count;
                for (int i = 0; i < shown; i++)
                {
                    sb.Append("  at ").Append(t.Frames[i]).Append('\n');
                }
                if (shown < t.Frames.Count)
                {
                    sb.Append($"  ... {t.Frames.Count - shown} more\n");
                }
                if (t.Frames.Count == 0)
                {
                    sb.Append("  (no managed frames)\n");
                }
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>"[main] Unity main thread" or "[t14] CrashReports watchdog".</summary>
        public static string Label(ThreadStack t)
        {
            string name = t.Name.Length > 0 ? t.Name : (t.IsMain ? "Unity main thread" : "(no name)");
            return $"[{(t.IsMain ? "main" : "t" + t.Id)}] {name}";
        }

        private static string Frame(StackFrame f)
        {
            MethodBase m = f.GetMethod();
            if (m == null) return "(unknown)";
            string type = m.DeclaringType != null ? m.DeclaringType.FullName + "." : "";
            int il = f.GetILOffset();
            return il >= 0 ? $"{type}{m.Name} (+0x{il:x})" : type + m.Name;
        }
    }
}
