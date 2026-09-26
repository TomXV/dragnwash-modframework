using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DragNWash.Installer;

namespace DragNWash.Launcher
{
    // Three ways in (docs/LAUNCHER_APP.md):
    //
    //   Launcher.exe <the game's exe> [its arguments]      Steam's launch option, "...\Launcher.exe" %command%
    //   Launcher.exe --update-after-exit --wait-pid <pid> [--mods <guid>,<guid>]
    //   Launcher.exe --launch-option on|off --wait-pid <pid>
    //
    // The first starts the game (after the updates, if the game found any last time and
    // the player wants them), waits for it as its parent, and when the game quit to be
    // updated (update-request.json), updates and starts it again. The second is started
    // by a game that was not started through the launcher: it waits for that game to
    // exit, updates, and has Steam start the game again. The third comes from the game's
    // Mods screen as well: once the game has closed, it closes Steam, puts itself into Steam's
    // launch options or takes itself out, and has Steam start the game again.
    //
    // The game starts only once the window has closed, so the two never show at once.
    // Whatever goes wrong in here, the game still starts: every failure ends in
    // starting it without the window. Only the window's X leaves it unstarted.
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            bool afterExit = args.Length > 0 && args[0] == "--update-after-exit";
            bool option = args.Length > 0 && args[0] == "--launch-option";
            bool fromGame = afterExit || option;
            var game = new Game(fromGame ? new string[0] : args);
            string folder = GameFolder(here, fromGame ? null : args.FirstOrDefault());
            Log.Open(folder != null ? Path.Combine(folder, "BepInEx", Paths.InstallerFolder) : here);
            Log.Line($"Launcher {typeof(Program).Assembly.GetName().Version.ToString(3)}: {(fromGame ? string.Join(" ", args) : args.Length == 0 ? "no command" : "command " + args[0] + (args.Length > 1 ? $" and {args.Length - 1} arguments" : ""))}");
            Log.Line("Game folder: " + (folder ?? "not found"));
            Game.ForgetDoorstop();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            int code = 0;
            Task run = null;
            // The flow runs on the window thread's message loop; the loop ends when it does.
            var loop = new ApplicationContext();
            SynchronizationContext.Current.Post(_ =>
            {
                run = afterExit ? AfterExit(args, folder) : option ? SwitchLaunchOption(args, folder) : Passthrough(game, folder);
                run.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Log.Line("Launcher: failed: " + t.Exception?.GetBaseException());
                        code = 1;
                    }
                    loop.ExitThread();
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }, null);
            Application.Run(loop);
            Log.Line("Launcher: done");
            return code;
        }

        // The game folder: two folders up from BepInEx\DragNWash.Installer\Launcher.exe, or the
        // folder of the game's exe in the command.
        private static string GameFolder(string here, string exe)
        {
            try
            {
                string up = Path.GetFullPath(Path.Combine(here, "..", ".."));
                if (InstallerCore.IsGameFolder(up))
                {
                    return up;
                }
                string beside = exe == null ? null : Path.GetDirectoryName(Path.GetFullPath(exe));
                return InstallerCore.IsGameFolder(beside) ? beside : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---- Steam's launch option: Launcher.exe %command% ----

        private static async Task Passthrough(Game game, string folder)
        {
            if (!game.HasCommand)
            {
                Log.Line("Launcher: nothing to start. Steam's launch option is \"<game>\\BepInEx\\DragNWash.Installer\\Launcher.exe\" %command%");
                return;
            }
            bool started = false;
            try
            {
                if (folder == null)
                {
                    Log.Line("Launcher: not in a Drag'n Wash folder; starting the game without looking for updates");
                }
                else
                {
                    var files = new GameFiles(folder);
                    bool web = WebView2();
                    // One left from an earlier game (the launcher wasn't waiting then) is stale.
                    files.TakeRequest(DateTime.UtcNow);
                    LauncherState state = files.ReadState();
                    List<ModUpdate> pending = files.Pending(files.ReadUpdates(), state);
                    Log.Line($"Launcher: {pending.Count} {(pending.Count == 1 ? "update" : "updates")} to show");
                    bool play = true;
                    if (pending.Count > 0 && web)
                    {
                        play = await Session.Choose(files, state, pending);
                    }
                    else if (web)
                    {
                        play = await Session.Intro(files);
                    }
                    if (!play)
                    {
                        Log.Line("Launcher: the player closed the window; the game is not started");
                        return;
                    }
                }
                if (!started)
                {
                    started = game.Start();
                }
                if (!started || folder == null)
                {
                    await game.WaitForExit();
                    return;
                }
                await WaitAndRestart(game, new GameFiles(folder));
            }
            catch (Exception ex)
            {
                Log.Line("Launcher: failed: " + ex);
                if (!started)
                {
                    game.Start();
                }
                await game.WaitForExit();
            }
        }

        // Waits as the game's parent. When the game quit to be updated, updates, counts
        // down and starts it again, as often as that happens.
        private static async Task WaitAndRestart(Game game, GameFiles files)
        {
            while (true)
            {
                await game.WaitForExit();
                UpdateRequest request = files.TakeRequest(game.StartedUtc);
                if (request == null)
                {
                    return;
                }
                bool again = true;
                if (!WebView2())
                {
                    Log.Line("Launcher: no WebView2, so no update; starting the game again as it is");
                }
                else
                {
                    List<ModUpdate> mods = files.Requested(files.ReadUpdates(), request.Mods);
                    if (mods.Count > 0)
                    {
                        again = await Session.Restart(files, mods, Task.CompletedTask);
                    }
                    else
                    {
                        Log.Line("Launcher: nothing asked for can be installed; starting the game again");
                    }
                }
                if (!again || !game.Start())
                {
                    return;
                }
            }
        }

        // ---- the game asked for it: Launcher.exe --update-after-exit --wait-pid <pid> [--mods ...] ----

        private static async Task AfterExit(string[] args, string folder)
        {
            Task gone = int.TryParse(Arg(args, "--wait-pid"), out int pid) ? Game.WaitForPid(pid) : Task.CompletedTask;
            bool again = true;
            try
            {
                if (folder == null)
                {
                    Log.Line("Launcher: not in a Drag'n Wash folder, so no update");
                }
                else if (!WebView2())
                {
                    Log.Line("Launcher: no WebView2, so no update; the game is started again as it is");
                }
                else
                {
                    var files = new GameFiles(folder);
                    IEnumerable<string> guids = Arg(args, "--mods")?.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0);
                    // update-request.json is the game's too; without --mods it says which.
                    UpdateRequest request = files.TakeRequest(DateTime.UtcNow.AddMinutes(-10));
                    guids = guids ?? request?.Mods ?? new string[0];
                    List<ModUpdate> mods = files.Requested(files.ReadUpdates(), guids);
                    if (mods.Count > 0)
                    {
                        again = await Session.Restart(files, mods, gone);
                    }
                    else
                    {
                        Log.Line("Launcher: nothing asked for can be installed");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Line("Launcher: failed: " + ex);
            }
            await gone;
            if (again)
            {
                Game.StartThroughSteam();
            }
        }

        // ---- the Mods screen's switch: Launcher.exe --launch-option on|off --wait-pid <pid> ----

        private static async Task SwitchLaunchOption(string[] args, string folder)
        {
            Task gone = int.TryParse(Arg(args, "--wait-pid"), out int pid) ? Game.WaitForPid(pid) : Task.CompletedTask;
            string value = Arg(args, "--launch-option");
            bool again = true;
            try
            {
                if (value != "on" && value != "off")
                {
                    Log.Line($"Launcher: --launch-option takes on or off, not {value ?? "nothing"}; the launch option is left as it is");
                }
                else if (folder == null)
                {
                    Log.Line("Launcher: not in a Drag'n Wash folder, so the launch option is left as it is");
                }
                else if (!WebView2())
                {
                    Log.Line("Launcher: no WebView2, so the launch option is left as it is; the game is started again");
                }
                else
                {
                    again = await Session.SwitchLaunchOption(new GameFiles(folder), value == "on", gone);
                }
            }
            catch (Exception ex)
            {
                Log.Line("Launcher: failed: " + ex);
            }
            await gone;
            if (again)
            {
                Game.StartThroughSteam();
            }
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // Whether the WebView2 runtime is installed. Without it there is no window: the
        // game starts as usual, and its Mods screen still links to each release page.
        private static bool WebView2()
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("DNW_LAUNCHER_NO_WEBVIEW2") == "1")
            {
                Log.Line("(debug) DNW_LAUNCHER_NO_WEBVIEW2 is set");
                return false;
            }
#endif
            try
            {
                string version = RuntimeVersion();
                Log.Line("WebView2: " + version);
                return !string.IsNullOrEmpty(version);
            }
            catch (Exception ex)
            {
                Log.Line("WebView2: not available (" + ex.GetType().Name + ": " + ex.Message + "); no window");
                return false;
            }
        }

        // Apart, so a missing Microsoft.Web.WebView2.Core.dll is an exception WebView2() catches.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string RuntimeVersion() => Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
    }
}
