using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DragNWash.CodeGraph
{
    // Started by the Bridge (the Inspector's Graph buttons):
    //
    //   CodeGraph.exe [--focus m:<method id>|t:<type>] [--port <n>]
    //
    // One window at a time: a second start hands its focus to the window that
    // is already open (through a named pipe) and leaves.
    internal static class Program
    {
        internal const string PipeName = "DragNWash.CodeGraph";

        [STAThread]
        private static int Main(string[] args)
        {
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            string focus = Arg("--focus");
            int port = int.TryParse(Arg("--port"), out int p) && p > 1023 && p < 65536 ? p : PortFromConfig();

            using (var single = new Mutex(true, @"Local\DragNWash.CodeGraph", out bool first))
            {
                if (!first)
                {
                    HandOver(focus);
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new GraphForm(port, focus));
                return 0;
            }
        }

        // The window already open shows this focus and comes to the front.
        private static void HandOver(string focus)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    pipe.Connect(2000);
                    byte[] data = Encoding.UTF8.GetBytes((focus ?? "") + "\n");
                    pipe.Write(data, 0, data.Length);
                }
            }
            catch
            {
                // The other window is closing: nothing to hand over to.
            }
        }

        // [Bridge] Port from the game's config, next to this exe's plugin folder
        // (BepInEx/plugins/DragNWash.ModFramework.Bridge → BepInEx/config).
        private static int PortFromConfig()
        {
            try
            {
                string config = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "config", "com.tomxv.dragnwash.modframework.bridge.cfg");
                foreach (string line in File.ReadAllLines(config))
                {
                    string t = line.Trim();
                    if (!t.StartsWith("Port", StringComparison.Ordinal)) continue;
                    int eq = t.IndexOf('=');
                    if (eq > 0 && int.TryParse(t.Substring(eq + 1).Trim(), out int port)) return port;
                }
            }
            catch
            {
            }
            return 47821;
        }
    }
}
