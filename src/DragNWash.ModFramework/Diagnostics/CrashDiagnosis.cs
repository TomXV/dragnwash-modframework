using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DragNWash.ModFramework.Diagnostics
{
    // Reads a crash report folder (report.txt, session.log) and tells what most
    // likely happened, as a kind the reader turns into words in its own
    // language. What changes from report to report (the time, the scene, the
    // last error) is in Details. Plain .NET, shared with CrashReporter.exe.
    internal sealed class CrashDiagnosis
    {
        internal enum Kind
        {
            // Unity's D3D12ScratchAllocator on the render thread (UUM-140564).
            Direct3D12Uploads,
            // The Direct3D 12 device or swap chain failed (often exclusive fullscreen).
            GraphicsDevice,
            // The main thread stopped and the game was then closed.
            Freeze,
            // No crash from Unity: killed, closed from the Task Manager, power loss.
            Stopped,
            // Any other crash inside Unity.
            Crash,
        }

        internal enum Detail { Ended, Scene, Played, LastError, Graphics, Report }

        internal string Folder;
        internal Kind What;
        internal bool HasCrashDump;
        internal bool HasHangDump;
        internal string ReportText = "";
        // The language the player had chosen in the game (a locale such as "ja"
        // or "zh-Hans"), from the last "language" note; null when none was noted.
        internal string Language;
        internal readonly List<KeyValuePair<Detail, string>> Details = new List<KeyValuePair<Detail, string>>();

        internal static CrashDiagnosis Read(string folder)
        {
            var d = new CrashDiagnosis { Folder = folder };
            string reportPath = Path.Combine(folder, "report.txt");
            string sessionPath = Path.Combine(folder, CrashReportWriter.SessionFile);
            d.ReportText = File.Exists(reportPath) ? File.ReadAllText(reportPath, Encoding.UTF8) : "";
            string[] notes = File.Exists(sessionPath) ? File.ReadAllLines(sessionPath, Encoding.UTF8) : new string[0];
            d.HasCrashDump = File.Exists(Path.Combine(folder, "crash.dmp"));
            string languageNote = notes.LastOrDefault(l => l.Contains(" language "));
            if (languageNote != null)
            {
                string[] parts = languageNote.Split(' ');
                int at = Array.IndexOf(parts, "language");
                string code = at >= 0 && at + 1 < parts.Length ? parts[at + 1] : null;
                d.Language = string.IsNullOrEmpty(code) || code == "-" ? null : code;
            }
            bool froze = notes.Any(l => l.Contains(" hang the main thread has not finished")) || d.ReportText.Contains("The game froze during this session");
            d.HasHangDump = froze && notes.Any(l => l.Contains("memory dump written"));
            bool unityCrash = d.ReportText.Contains("Native stack:");

            if (unityCrash && d.ReportText.Contains("D3D12ScratchAllocator"))
                d.What = Kind.Direct3D12Uploads;
            else if (d.ReportText.Contains("D3D12SwapChain::Present") || d.ReportText.Contains("D3D12Fence::Wait")
                     || notes.Any(l => l.Contains("swapchain present failed") || l.Contains("Device failed") || l.Contains("Unrecoverable GPU device error")))
                d.What = Kind.GraphicsDevice;
            else if (froze && !unityCrash)
                d.What = Kind.Freeze;
            else if (!unityCrash)
                d.What = Kind.Stopped;
            else
                d.What = Kind.Crash;

            string name = Path.GetFileName(folder);
            d.Details.Add(new KeyValuePair<Detail, string>(Detail.Ended,
                DateTime.TryParseExact(name, "yyyy-MM-dd_HHmmss", null, System.Globalization.DateTimeStyles.None, out DateTime ended)
                    ? ended.ToString("yyyy-MM-dd HH:mm:ss")
                    : Directory.GetLastWriteTime(folder).ToString("yyyy-MM-dd HH:mm")));
            string scene = notes.Where(l => l.Contains(" scene loaded '")).Select(l => Between(l, "loaded '", "'")).LastOrDefault();
            if (!string.IsNullOrEmpty(scene)) d.Details.Add(new KeyValuePair<Detail, string>(Detail.Scene, scene));
            string first = notes.FirstOrDefault(l => l.Contains(" start ")) ?? "";
            string last = notes.LastOrDefault(l => l.Length > 12 && !l.Contains(CrashReportWriter.ReportedEnd)) ?? "";
            TimeSpan? played = Span(first, last);
            if (played.HasValue) d.Details.Add(new KeyValuePair<Detail, string>(Detail.Played, played.Value.TotalMinutes >= 1 ? $"{(int)played.Value.TotalMinutes} min" : $"{(int)played.Value.TotalSeconds} s"));
            string error = notes.Where(l => l.Contains(" unity-error ") || l.Contains(" unity-exception ")).Select(l => l.Substring(l.IndexOf(" unity-", StringComparison.Ordinal) + 1)).LastOrDefault();
            if (!string.IsNullOrEmpty(error)) d.Details.Add(new KeyValuePair<Detail, string>(Detail.LastError, error.Length > 160 ? error.Substring(0, 160) + "..." : error));
            string graphics = Between(first, ", Unity ", ", screen");
            if (!string.IsNullOrEmpty(graphics)) d.Details.Add(new KeyValuePair<Detail, string>(Detail.Graphics, "Unity " + graphics));
            // Short: the full path does not wrap, and the window has a button that opens it.
            d.Details.Add(new KeyValuePair<Detail, string>(Detail.Report, Path.Combine("BepInEx", "CrashReports", name)));
            return d;
        }

        private static TimeSpan? Span(string first, string last)
        {
            if (first.Length < 12 || last.Length < 12) return null;
            if (!TimeSpan.TryParse(first.Substring(0, 12), out TimeSpan a) || !TimeSpan.TryParse(last.Substring(0, 12), out TimeSpan b)) return null;
            TimeSpan span = b - a;
            if (span < TimeSpan.Zero) span += TimeSpan.FromDays(1);
            return span;
        }

        private static string Between(string text, string from, string to)
        {
            int i = text.IndexOf(from, StringComparison.Ordinal);
            if (i < 0) return null;
            i += from.Length;
            int j = text.IndexOf(to, i, StringComparison.Ordinal);
            return j < 0 ? text.Substring(i) : text.Substring(i, j - i);
        }
    }
}
