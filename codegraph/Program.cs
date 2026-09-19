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
    //   CodeGraph.exe --from-game [--focus m:<method id>|t:<type>] [--port <n>]
    //
    // One window at a time: a second start hands its focus to the window that
    // is already open (through a named pipe) and leaves.
    //
    // Started by the game, it would count as part of the game for Steam (which
    // follows what the game starts), and Steam would say the game is still
    // running while this window is open. So a start --from-game leaves its focus
    // and port in a file, has Windows' shell start this exe again, apart from
    // the game, and exits; that second start picks them up.
    internal static class Program
    {
        internal const string PipeName = "DragNWash.CodeGraph";
        private const string MutexName = @"Local\DragNWash.CodeGraph";
        private static readonly string Pending = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragNWash ModFramework", "CodeGraph", "open.txt");

        [STAThread]
        private static int Main(string[] args)
        {
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            string focus = Arg("--focus");
            string portText = Arg("--port");
            bool fromGame = Array.IndexOf(args, "--from-game") >= 0;
            if (!fromGame) TakePending(ref focus, ref portText);
            int port = int.TryParse(portText, out int p) && p > 1023 && p < 65536 ? p : PortFromConfig();

            // A window already open takes the focus. The start from the game does not hold the
            // name itself, so the start from the shell that follows at once can.
            if (Mutex.TryOpenExisting(MutexName, out Mutex open))
            {
                open.Dispose();
                HandOver(focus);
                return 0;
            }
            if (fromGame && StartApart(focus, port)) return 0;
            using (var single = new Mutex(true, MutexName, out bool first))
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

        // Leaves focus and port for the next start, and has the shell (explorer.exe) start this
        // exe: its parent is then the shell, not the game. False when that cannot be done:
        // this start then opens the window itself.
        private static bool StartApart(string focus, int port)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Pending));
                File.WriteAllText(Pending, port + Environment.NewLine + (focus ?? ""));
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(explorer, "\"" + exe + "\"") { UseShellExecute = false });
                return true;
            }
            catch
            {
                return false;
            }
        }

        // What a start --from-game left, if it is fresh (the shell starts us within seconds).
        private static void TakePending(ref string focus, ref string port)
        {
            try
            {
                if (!File.Exists(Pending)) return;
                bool fresh = DateTime.Now - File.GetLastWriteTime(Pending) < TimeSpan.FromSeconds(30);
                string[] lines = File.ReadAllLines(Pending);
                File.Delete(Pending);
                if (!fresh || lines.Length == 0) return;
                if (port == null) port = lines[0].Trim();
                if (focus == null && lines.Length > 1 && lines[1].Trim().Length > 0) focus = lines[1].Trim();
            }
            catch
            {
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
