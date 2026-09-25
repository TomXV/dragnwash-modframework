using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using DragNWash.Installer;

namespace DragNWash.Launcher
{
    // Starting the game and waiting for it. The launcher stays the game's parent, so
    // Steam, which watches what it started, keeps counting the play time.
    internal sealed class Game
    {
        internal const string LauncherVariable = "DNW_LAUNCHER";

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        private readonly string[] _command;
        private Process _process;

        // The command Steam gave for %command%: the game's exe and its arguments.
        internal Game(string[] command)
        {
            _command = command;
        }

        internal bool HasCommand => _command.Length > 0;

        internal DateTime StartedUtc { get; private set; }

        // Starts the command again each time it is called. False when it could not.
        internal bool Start()
        {
            if (!HasCommand)
            {
                return false;
            }
            try
            {
                string exe = _command[0];
                var info = new ProcessStartInfo(exe, string.Join(" ", _command.Skip(1).Select(Quote)))
                {
                    UseShellExecute = false,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exe)),
                };
                // Tells the game the launcher is its parent and waits for it: its Update
                // button then only leaves update-request.json and quits.
                info.EnvironmentVariables[LauncherVariable] = "1";
                StartedUtc = DateTime.UtcNow;
                _process?.Dispose();
                _process = Process.Start(info);
                Log.Line($"Game: started {exe}{(_command.Length > 1 ? " with " + (_command.Length - 1) + " arguments" : "")}, pid {_process?.Id}");
                if (_process != null)
                {
                    // The launcher's window is gone by now; the game's window may come to the front.
                    AllowSetForegroundWindow(_process.Id);
                }
                return _process != null;
            }
            catch (Exception ex)
            {
                Log.Line("Game: could not start: " + ex.Message);
                return false;
            }
        }

        internal Task WaitForExit()
        {
            Process process = _process;
            if (process == null)
            {
                return Task.CompletedTask;
            }
            return Task.Run(() =>
            {
                try
                {
                    process.WaitForExit();
                    Log.Line($"Game: exited with code {process.ExitCode}");
                }
                catch (Exception ex)
                {
                    Log.Line("Game: could not be waited for: " + ex.Message);
                }
            });
        }

        // Waits for another process (the game that started the launcher) to exit.
        internal static Task WaitForPid(int pid)
        {
            return Task.Run(() =>
            {
                try
                {
                    using (Process process = Process.GetProcessById(pid))
                    {
                        Log.Line($"Launcher: waiting for pid {pid} ({process.ProcessName}) to exit");
                        process.WaitForExit();
                    }
                    Log.Line($"Launcher: pid {pid} has exited");
                }
                catch (ArgumentException)
                {
                    Log.Line($"Launcher: pid {pid} is not running");
                }
                catch (Exception ex)
                {
                    Log.Line($"Launcher: could not wait for pid {pid}: {ex.Message}");
                }
            });
        }

        // After the game's process is gone, its files can stay in use for a moment.
        internal static async Task WaitUntilClosed(string game)
        {
            for (int i = 0; i < 50 && InstallerCore.GameRunning(game); i++)
            {
                await Task.Delay(200);
            }
        }

        // A launcher that started the game (Steam's launch option) ends right after it. Steam
        // counts the game as running until that launcher has gone too, so wait for it, for
        // ten seconds at most.
        internal static async Task WaitForOtherLaunchers()
        {
            for (int i = 0; i < 50 && OtherLauncherRunning(); i++)
            {
                await Task.Delay(200);
            }
        }

        private static bool OtherLauncherRunning()
        {
            string exe;
            int self;
            using (Process me = Process.GetCurrentProcess())
            {
                exe = me.MainModule.FileName;
                self = me.Id;
            }
            bool found = false;
            foreach (Process process in Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(exe)))
            {
                using (process)
                {
                    try
                    {
                        found |= process.Id != self && string.Equals(process.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (Exception)
                    {
                        // One that can't be asked isn't ours.
                    }
                }
            }
            return found;
        }

        // For a game that was not started through the launcher: Steam starts it, so it
        // runs with Steam's overlay and play time as usual.
        internal static void StartThroughSteam()
        {
#if DEBUG
            // For testing without Steam or the real game; only in Debug builds.
            if (Environment.GetEnvironmentVariable("DNW_LAUNCHER_NO_STEAM") == "1")
            {
                Log.Line("(debug) DNW_LAUNCHER_NO_STEAM is set: Steam is not asked to start the game");
                return;
            }
#endif
            try
            {
                Process.Start(new ProcessStartInfo("steam://rungameid/" + Paths.SteamAppId) { UseShellExecute = true })?.Dispose();
                Log.Line("Game: asked Steam to start it (steam://rungameid/" + Paths.SteamAppId + ")");
            }
            catch (Exception ex)
            {
                Log.Line("Game: Steam could not be asked to start it: " + ex.Message);
            }
        }

        // One argument as Windows' command line reads it back (CommandLineToArgvW).
        private static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return arg;
            }
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }
                sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                slashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', slashes * 2);
            return sb.Append('"').ToString();
        }
    }
}
