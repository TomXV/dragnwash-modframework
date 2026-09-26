using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DragNWash.ModFramework.Diagnostics
{
    /// <summary>
    /// A snapshot of the running game for later reading, without a crash: a
    /// memory dump (Windows), every managed thread's stack, the memory numbers
    /// and the loaded modules, in a folder of its own under
    /// BepInEx/CrashReports. Experimental (core 1.7).
    /// </summary>
    public static class Snapshot
    {
        /// <summary>Writes one now and returns what it wrote, for people. Main thread only; the dump holds the game for a second or two.</summary>
        public static string Write(out string folder)
        {
            folder = Path.Combine(CrashReports.Folder, DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + "_snapshot");
            Directory.CreateDirectory(folder);
            var written = new List<string>();

            File.WriteAllText(Path.Combine(folder, "memory.txt"), MemoryText(MemoryWatch.Now(), true), Utf8);
            written.Add("memory.txt");
            File.WriteAllText(Path.Combine(folder, "stacks.txt"), StacksText(ManagedStacks.Capture(out string reason), reason), Utf8);
            written.Add("stacks.txt");
            File.WriteAllText(Path.Combine(folder, "modules.txt"), ModulesText(), Utf8);
            written.Add("modules.txt");

            string dumpNote = null;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                string dump = Path.Combine(folder, "snapshot.dmp");
                string failed = CrashReports.WriteDump(dump);
                if (failed == null)
                {
                    written.Insert(0, $"snapshot.dmp {MemoryWatch.Size(new FileInfo(dump).Length)}");
                    dumpNote = "The dump holds part of the game's memory: share it privately.";
                }
                else
                {
                    dumpNote = "No memory dump: " + failed;
                }
            }
            CrashReports.Note(ModFramework.Guid, "snapshot", "written: " + folder);
            ModFramework.Log.LogInfo($"[snapshot] {folder}: {string.Join(", ", written.ToArray())}");
            return $"Written: {Shown(folder)}\n  {string.Join(", ", written.ToArray())}" + (dumpNote != null ? "\n  " + dumpNote : "");
        }

        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        // BepInEx/CrashReports/..., the way people find it from the game folder.
        private static string Shown(string folder)
        {
            string root = BepInEx.Paths.GameRootPath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return (folder.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? folder.Substring(root.Length) : folder).Replace('\\', '/');
        }

        // memory.txt: the numbers, then (when asked) every counter this Unity has.
        internal static string MemoryText(MemorySample sample, bool counters)
        {
            var sb = new StringBuilder();
            sb.Append(sample != null ? $"Memory at {sample.Time:yyyy-MM-dd HH:mm:ss}\n" : "");
            sb.Append(MemoryWatch.Describe(sample)).Append('\n');
            if (counters)
            {
                try
                {
                    sb.Append("\nMemory counters this Unity build offers:\n");
                    foreach (string line in MemoryWatch.Counters()) sb.Append("  ").Append(line).Append('\n');
                }
                catch (Exception ex)
                {
                    sb.Append("  (could not list them: " + ex.Message + ")\n");
                }
            }
            return sb.ToString();
        }

        // stacks.txt: every managed thread, all frames.
        internal static string StacksText(List<ThreadStack> threads, string reason)
        {
            if (threads == null) return $"Managed stacks could not be read: {reason}\n";
            return $"Managed threads at {DateTime.Now:yyyy-MM-dd HH:mm:ss}, main thread first ({threads.Count}). Frames are Type.Method, innermost first, with (+IL offset) where Mono knows it.\n\n"
                + ManagedStacks.Text(threads, 0) + "\n";
        }

        // modules.txt: where each native module was loaded, to read the dump's
        // addresses against.
        private static string ModulesText()
        {
            var sb = new StringBuilder();
            try
            {
                using (Process me = Process.GetCurrentProcess())
                {
                    foreach (ProcessModule m in me.Modules)
                    {
                        sb.Append($"0x{m.BaseAddress.ToInt64():x16} {m.ModuleMemorySize,10} {m.FileName}\n");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.Append("(could not list the modules: " + ex.Message + ")\n");
            }
            return sb.ToString();
        }
    }
}
