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
    //
    // If writing or showing the report fails, a plain message box still tells
    // the player that the game crashed and where to look, rather than nothing.
    internal static class Program
    {
        private enum Stage { Watching, Writing, Showing, Shown }

        private static Stage _stage;

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
                    _stage = Stage.Showing;
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
                _stage = Stage.Writing;
                WaitForUnityCrashHandler();
                string report = CrashReportWriter.Write(folder, Arg("--unity"));
                // Null: the core, started again meanwhile, reported it first.
                if (report == null)
                {
                    return 0;
                }
                _stage = Stage.Showing;
                return Show(report, windowsLanguage);
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
                if (_stage == Stage.Writing || _stage == Stage.Showing)
                {
                    Fallback(_stage == Stage.Writing);
                }
                return 1;
            }
        }

        // A standard message box needs no form of ours, so it still shows when
        // the window could not be made. In the window's language when the words
        // can be had, else in English.
        private static void Fallback(bool notWritten)
        {
            string title, text;
            try
            {
                title = Strings.Get(Strings.Key.WindowTitle);
                text = Strings.Get(notWritten ? Strings.Key.NotWritten : Strings.Key.NoWindow);
            }
            catch
            {
                title = "Drag'n Wash - crash report";
                text = "Drag'n Wash closed unexpectedly. The crash report is in the game's folder under BepInEx\\CrashReports.";
            }
            try
            {
                Application.EnableVisualStyles();
                MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch
            {
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
            // A failure inside the window reaches Main (and its message box),
            // not WinForms' own exception dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            var form = new ReportForm(diagnosis);
            // Once the player has seen the window, a later failure needs no message box.
            form.Shown += (s, e) => _stage = Stage.Shown;
            Application.Run(form);
            return 0;
        }
    }
}
