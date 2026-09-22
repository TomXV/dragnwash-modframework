using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DragNWash.ModFramework.Diagnostics
{
    // Turns a session record that did not end cleanly into a crash report
    // folder. Plain .NET, no Unity: the core uses it at the next start, and the
    // external CrashReporter.exe uses the same source right after the game
    // exits. Whichever gets there first writes the report and marks the
    // session record as reported, so the other leaves it alone.
    internal static class CrashReportWriter
    {
        internal const string SessionFile = "session.log";
        // The language the player sees the game in ("ja", "zh-Hans"; "-" when not known), kept by the core for the report window.
        internal const string LocaleFile = "locale.txt";
        internal const string CleanEnd = "END clean exit";
        internal const string ReportedEnd = "END reported";
        internal const int KeepReports = 10;

        // True when the session record holds neither marker. Not only the last
        // line: Unity tears down after Application.quitting, and anything noted
        // then (released GPU resources) comes after the clean-exit marker.
        internal static bool NeedsReport(string folder)
        {
            string session = Path.Combine(folder, SessionFile);
            if (!File.Exists(session))
            {
                return false;
            }
            bool any = false;
            foreach (string line in ReadLines(session))
            {
                if (line.Length == 0) continue;
                any = true;
                if (line.IndexOf(CleanEnd, StringComparison.Ordinal) >= 0 || line.IndexOf(ReportedEnd, StringComparison.Ordinal) >= 0)
                {
                    return false;
                }
            }
            return any;
        }

        // Writes <folder>/<yyyy-MM-dd_HHmmss>/ with report.txt, the session
        // record, and Unity's crash.dmp when Unity made a crash folder
        // (unityCrashes = %TEMP%/<company>/<product>/Crashes) near the end.
        // Returns the report folder, or null when there was nothing to report.
        internal static string Write(string folder, string unityCrashes)
        {
            if (!NeedsReport(folder))
            {
                return null;
            }
            string session = Path.Combine(folder, SessionFile);
            List<string> lines = ReadLines(session).Where(l => l.Length > 0).ToList();
            DateTime ended = File.GetLastWriteTime(session);
            string dir = Path.Combine(folder, ended.ToString("yyyy-MM-dd_HHmmss"));
            Directory.CreateDirectory(dir);
            string older = Path.Combine(folder, "session.1.log");
            if (File.Exists(older) && File.GetLastWriteTime(older) > ended.AddHours(-12))
            {
                File.Copy(older, Path.Combine(dir, "session.1.log"), overwrite: true);
            }
            File.Copy(session, Path.Combine(dir, SessionFile), overwrite: true);

            var report = new StringBuilder();
            report.AppendLine($"Crash report: the session that ended {ended:yyyy-MM-dd HH:mm:ss} did not exit cleanly.");
            report.AppendLine();
            report.AppendLine("Session start:");
            report.AppendLine("  " + (lines.FirstOrDefault(l => l.Contains(" start ")) ?? "(not recorded)"));
            report.AppendLine("  " + (lines.FirstOrDefault(l => l.Contains(" mods ")) ?? "(mods not recorded)"));
            report.AppendLine();
            report.AppendLine("Last notes before the end:");
            foreach (string l in LastNotes(lines, 40))
            {
                report.AppendLine("  " + l);
            }
            string native = UnityCrashStack(unityCrashes, ended, out string unityFolder);
            report.AppendLine();
            if (unityFolder != null && File.Exists(Path.Combine(unityFolder, "crash.dmp")))
            {
                File.Copy(Path.Combine(unityFolder, "crash.dmp"), Path.Combine(dir, "crash.dmp"), overwrite: true);
                report.AppendLine("crash.dmp: Unity's memory dump of the crash (it holds part of the game's memory: share it privately).");
            }
            // Freezes of this session only: from its start to its end.
            DateTime started = Started(lines) ?? ended.AddHours(-12);
            foreach (string hang in Directory.GetDirectories(folder, "*_hang").Where(d => Directory.GetCreationTime(d) >= started.AddSeconds(-5) && Directory.GetCreationTime(d) <= ended.AddMinutes(1)))
            {
                report.AppendLine($"The game froze during this session; see {Path.GetFileName(hang)}.");
            }
            if (native != null)
            {
                report.AppendLine($"Unity's crash folder: {unityFolder}");
                report.AppendLine("Native stack:");
                report.AppendLine(native);
            }
            else
            {
                report.AppendLine("Unity made no crash folder near that time (a freeze that was closed by hand, a power loss, or the process was killed).");
            }
            File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString(), new UTF8Encoding(false));
            File.AppendAllText(session, $"{DateTime.Now:HH:mm:ss.fff} {ReportedEnd} {Path.GetFileName(dir)}{Environment.NewLine}");

            foreach (DirectoryInfo old in new DirectoryInfo(folder).GetDirectories().OrderByDescending(d => d.Name).Skip(KeepReports))
            {
                try { old.Delete(recursive: true); } catch { }
            }
            return dir;
        }

        // The last notes, with each run of heartbeats folded into one line (the
        // first and last time, the frame times), so the heartbeats show how long
        // the game kept running without pushing the events that matter out.
        internal static List<string> LastNotes(List<string> lines, int count)
        {
            var folded = new List<string>();
            int i = 0;
            while (i < lines.Count)
            {
                if (!IsBeat(lines[i]))
                {
                    folded.Add(lines[i]);
                    i++;
                    continue;
                }
                int j = i;
                while (j + 1 < lines.Count && IsBeat(lines[j + 1])) j++;
                if (j == i)
                {
                    folded.Add(lines[i]);
                }
                else
                {
                    var times = new List<double>();
                    for (int k = i; k <= j; k++)
                    {
                        string[] parts = lines[k].Split(' ');
                        int at = Array.IndexOf(parts, "beat");
                        if (at >= 0 && at + 1 < parts.Length && double.TryParse(parts[at + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ms)) times.Add(ms);
                    }
                    string range = times.Count > 0 ? $", {times.Min():0.0}-{times.Max():0.0} ms/frame" : "";
                    folded.Add($"{Head(lines[i])} beat x{j - i + 1} until {Head(lines[j]).Split(' ')[0]} ({Head(lines[j]).Split(' ').Skip(1).FirstOrDefault()}){range}");
                }
                i = j + 1;
            }
            return folded.Skip(Math.Max(0, folded.Count - count)).ToList();
        }

        // "HH:mm:ss.fff f0 main start yyyy-MM-dd ..." gives the session's start.
        private static DateTime? Started(List<string> lines)
        {
            string start = lines.FirstOrDefault(l => l.Contains(" start "));
            if (start == null) return null;
            string[] parts = start.Split(' ');
            int at = Array.IndexOf(parts, "start");
            if (at < 0 || at + 1 >= parts.Length) return null;
            return DateTime.TryParseExact(parts[at + 1] + " " + parts[0], "yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime t)
                ? t
                : (DateTime?)null;
        }

        private static bool IsBeat(string line) => line.IndexOf(" beat ", StringComparison.Ordinal) > 0;

        // "HH:mm:ss.fff f123 main" of a note.
        private static string Head(string line)
        {
            string[] parts = line.Split(' ');
            return parts.Length >= 3 ? parts[0] + " " + parts[1] + " " + parts[2] : line;
        }

        // Unity writes Crash_<time>/ with a Player.log whose end holds the
        // native stack. Take the one closest to the end of the session, within
        // two minutes.
        internal static string UnityCrashStack(string unityCrashes, DateTime ended, out string folder)
        {
            folder = null;
            try
            {
                if (string.IsNullOrEmpty(unityCrashes) || !Directory.Exists(unityCrashes)) return null;
                DirectoryInfo best = new DirectoryInfo(unityCrashes).GetDirectories("Crash_*")
                    .OrderBy(d => Math.Abs((d.LastWriteTime - ended).TotalSeconds))
                    .FirstOrDefault();
                if (best == null || Math.Abs((best.LastWriteTime - ended).TotalSeconds) > 120) return null;
                folder = best.FullName;
                string log = Path.Combine(best.FullName, "Player.log");
                if (!File.Exists(log)) return null;
                string[] all = ReadLines(log).ToArray();
                int start = Array.FindIndex(all, l => l.Contains("OUTPUTTING STACK TRACE"));
                if (start < 0) return null;
                int end = Array.FindIndex(all, start, l => l.Contains("END OF STACKTRACE"));
                return string.Join(Environment.NewLine, all.Skip(start + 1).Take((end < 0 ? all.Length : end) - start - 1).Where(l => l.Length > 0).Select(l => "  " + l));
            }
            catch
            {
                return null;
            }
        }

        // Read while the game may still hold the file open for writing.
        private static IEnumerable<string> ReadLines(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    yield return line;
                }
            }
        }
    }
}
