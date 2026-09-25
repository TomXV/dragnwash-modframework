using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DragNWash.Launcher
{
    // BepInEx/DragNWash.Installer/launcher.log: this run's lines, each with the time.
    // The run before is kept as launcher.prev.log. English, like the installer's log,
    // so it reads the same in a bug report.
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static StreamWriter _file;

        internal static string Path { get; private set; }

        // Heard by the window while an update runs, to show the lines in its log box.
        internal static Action<string> Listener;

        internal static void Open(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string path = System.IO.Path.Combine(folder, "launcher.log");
                string previous = System.IO.Path.Combine(folder, "launcher.prev.log");
                if (File.Exists(path))
                {
                    File.Copy(path, previous, true);
                }
                _file = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                Path = path;
            }
            catch (Exception)
            {
                // Another launcher has the file (the game started one with --update-after-exit
                // while this one waits): this one's lines go to a file of its own.
                try
                {
                    string path = System.IO.Path.Combine(folder, $"launcher.{System.Diagnostics.Process.GetCurrentProcess().Id}.log");
                    _file = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                    Path = path;
                }
                catch (Exception)
                {
                    _file = null;
                }
            }
        }

        internal static void Line(string text)
        {
            lock (Gate)
            {
                try
                {
                    _file?.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + text);
                }
                catch (Exception)
                {
                    // A full disk must not stop the game from starting.
                }
            }
            try
            {
                Listener?.Invoke(text);
            }
            catch (Exception)
            {
            }
        }
    }
}
