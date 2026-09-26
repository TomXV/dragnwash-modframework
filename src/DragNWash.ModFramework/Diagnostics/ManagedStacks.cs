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

        /// <summary>Innermost first: "Type.Method", with " (+0x3a)" when Mono knows the IL offset.</summary>
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
                    // The thread asking sees itself inside this capture (the
                    // Mono call, reflection, Capture): start below Capture.
                    int start = 0;
                    if (thread == Thread.CurrentThread)
                    {
                        for (int i = 0; i < all.Length; i++)
                        {
                            MethodBase m = all[i].GetMethod();
                            if (m != null && m.DeclaringType == typeof(ManagedStacks) && m.Name == nameof(Capture)) start = i + 1;
                        }
                    }
                    for (int i = start; i < all.Length; i++)
                    {
                        frames.Add(Frame(all[i]));
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
            var empty = new List<ThreadStack>();
            foreach (ThreadStack t in threads)
            {
                if (t.Frames.Count == 0 && !t.IsMain)
                {
                    empty.Add(t);
                    continue;
                }
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
            if (empty.Count > 0)
            {
                sb.Append(EmptyLine(empty)).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// One line for the threads with no managed frames (mostly Unity's own
        /// threads that Mono knows of): "25 threads with no managed frames: t6, t7 (name), ...".
        /// </summary>
        public static string EmptyLine(IList<ThreadStack> threads)
        {
            var parts = new string[threads.Count];
            for (int i = 0; i < threads.Count; i++)
            {
                parts[i] = "t" + threads[i].Id + (threads[i].Name.Length > 0 ? " (" + threads[i].Name + ")" : "");
            }
            return $"{threads.Count} thread{(threads.Count == 1 ? "" : "s")} with no managed frames: {string.Join(", ", parts)}";
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
            if (m == null) return "(native)";
            string type = m.DeclaringType != null ? TypeName(m.DeclaringType) : "";
            string name = m.Name;
            // What the compiler made, the way it was written: a lambda
            // (Type+<>c.<Method>b__12_0) as "Type.Method (lambda)", an iterator or
            // async method (Type+<Method>d__5.MoveNext) as "Type.Method (iterator)".
            int nested = type.IndexOf("+<", StringComparison.Ordinal);
            if (nested >= 0)
            {
                string inner = type.Substring(nested + 1);
                type = type.Substring(0, nested);
                int close = inner.IndexOf('>');
                if (name == "MoveNext" && inner.Length > 2 && inner[1] != '>' && close > 1)
                {
                    name = inner.Substring(1, close - 1) + " (iterator)";
                }
            }
            if (name.StartsWith("<", StringComparison.Ordinal) && name.IndexOf('>') > 1)
            {
                name = name.Substring(1, name.IndexOf('>') - 1) + " (lambda)";
            }
            if (type.Length > 0) type += ".";
            int il = f.GetILOffset();
            return il >= 0 ? $"{type}{name} (+0x{il:x})" : type + name;
        }

        // Namespace.Name, with generic arguments short: System.Action<Rect>, not
        // Action`1[[UnityEngine.Rect, UnityEngine.CoreModule, Version=...]].
        private static string TypeName(Type t)
        {
            string name = t.IsNested && t.DeclaringType != null ? TypeName(t.DeclaringType) + "+" + t.Name : (t.Namespace != null ? t.Namespace + "." : "") + t.Name;
            if (!t.IsGenericType) return name;
            int tick = name.LastIndexOf('`');
            if (tick > 0) name = name.Substring(0, tick);
            Type[] args = t.GetGenericArguments();
            var shortArgs = new string[args.Length];
            for (int i = 0; i < args.Length; i++) shortArgs[i] = args[i].IsGenericParameter ? args[i].Name : Short(args[i]);
            return name + "<" + string.Join(", ", shortArgs) + ">";
        }

        private static string Short(Type t)
        {
            if (!t.IsGenericType) return t.Name;
            string name = t.Name;
            int tick = name.IndexOf('`');
            if (tick > 0) name = name.Substring(0, tick);
            Type[] args = t.GetGenericArguments();
            var parts = new string[args.Length];
            for (int i = 0; i < args.Length; i++) parts[i] = Short(args[i]);
            return name + "<" + string.Join(", ", parts) + ">";
        }
    }
}
