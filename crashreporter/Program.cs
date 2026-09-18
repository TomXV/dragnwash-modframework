using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using DragNWash.ModFramework.Diagnostics;

namespace DragNWash.CrashReporter
{
    // Started by the ModFramework core when the game starts:
    //
    //   CrashReporter.exe --pid <game process> --folder <BepInEx/CrashReports> --unity <Unity's Crashes folder> [--lang <culture>]
    //
    // It waits for the game to exit. A clean exit ends the session record with a
    // marker, and the reporter leaves without a trace. Otherwise it waits for
    // Unity's crash handler to finish its crash folder, writes the report (the
    // same code the core would run at the next start) and shows it.
    //
    // To look at a report again: CrashReporter.exe --show <report folder>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            try
            {
                string windowsLanguage = Arg("--lang");
                string show = Arg("--show");
                if (show != null)
                {
                    return Show(show, windowsLanguage);
                }
                string folder = Arg("--folder");
                if (!int.TryParse(Arg("--pid"), out int pid) || string.IsNullOrEmpty(folder))
                {
                    return 2;
                }
                try
                {
                    using (Process game = Process.GetProcessById(pid))
                    {
                        game.WaitForExit();
                    }
                }
                catch (ArgumentException)
                {
                    // Already gone.
                }
                if (!CrashReportWriter.NeedsReport(folder))
                {
                    return 0;
                }
                WaitForUnityCrashHandler();
                string report = CrashReportWriter.Write(folder, Arg("--unity"));
                return report == null ? 0 : Show(report, windowsLanguage);
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "DragNWash.CrashReporter.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}");
                }
                catch
                {
                }
                return 1;
            }
        }

        // Unity's crash handler writes the crash folder (with the dump) after
        // the game is gone; give it time so the report can include it.
        private static void WaitForUnityCrashHandler()
        {
            Thread.Sleep(1500);
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 30 && Process.GetProcessesByName("UnityCrashHandler64").Any())
            {
                Thread.Sleep(500);
            }
        }

        private static int Show(string report, string windowsLanguage)
        {
            CrashDiagnosis diagnosis = CrashDiagnosis.Read(report);
            Strings.Choose(diagnosis.Language, windowsLanguage);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ReportForm(diagnosis));
            return 0;
        }
    }
}
