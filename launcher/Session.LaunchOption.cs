using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DragNWash.Installer;

namespace DragNWash.Launcher
{
    // The launch option switched from the game's Mods screen: the updating board (B) with
    // steps of its own. The game has quit by then. Steam keeps its settings in memory and
    // writes localconfig.vdf again when it exits, so it is closed first; then the launcher
    // goes in or out of the launch options (Install.exe's own code, installer/Steam.cs and
    // SteamConfig.cs), and at the end Steam and the game start again. Nothing goes online.
    internal sealed partial class Session
    {
        // What the page shows for the backup Steam.Apply leaves next to the file.
        private const string ShownBackup = @"…\config\localconfig.vdf" + Steam.BackupSuffix;

        // "on" or "off" in this mode; null in the others.
        private string _option;

        // Steam was asked to exit here, so the window's X starts it again (without the game).
        private string _steamExe;

        // Steam didn't close: the page offers Try again.
        private bool _steamStuck;

        // The launch option switch, from waiting for the game to the answer: true starts Steam
        // and the game (steam://rungameid), false starts nothing. When the answer is false and
        // Steam was closed here, Steam is started again on its own before this returns.
        internal static async Task<bool> SwitchLaunchOption(GameFiles files, bool on, Task waitFor)
        {
            var session = new Session(files, null, new List<ModUpdate>(), true) { _option = on ? "on" : "off" };
            session._handler = session.OnSwitchMessage;
            // The log box shows every line from here on, the ones from before the page was up too.
            var early = new List<string>();
            bool live = false;
            Log.Listener = line =>
            {
                lock (early)
                {
                    if (!live)
                    {
                        early.Add(line);
                        return;
                    }
                }
                session._window?.Send(Json.Object("type", "log", "text", line));
            };
            try
            {
                Log.Line($"Launcher: launch option {session._option} (from the Mods screen)");
                Log.Line("Launcher: waiting for the game to close");
                // As after the game's Update button: the window comes once the game has gone.
                if (await Task.WhenAny(waitFor, Task.Delay(TimeSpan.FromSeconds(10))) != waitFor)
                {
                    Log.Line("Launcher: the game is still closing after 10 s; the window shows now");
                }
                Task<bool> shown = session.Show("option", true);
                if (session._window != null && await session._window.Ready)
                {
                    session.Step("wait");
                    lock (early)
                    {
                        foreach (string line in early)
                        {
                            session._window?.Send(Json.Object("type", "log", "text", line));
                        }
                        live = true;
                    }
                    await waitFor;
                    await Game.WaitUntilClosed(files.Game);
                    await Game.WaitForOtherLaunchers();
                    Log.Line("Launcher: the game has closed");
                    // Closed with its X while it waited: nothing changed, and nothing starts.
                    if (!session._answer.Task.IsCompleted)
                    {
                        session.Switch();
                    }
                }
                else
                {
                    // No window: the launch option stays as it is, and the game starts again.
                    await waitFor;
                    await Game.WaitUntilClosed(files.Game);
                }
                bool play = await shown;
                if (!play && session._steamExe != null)
                {
                    await StartSteamAgain(session._steamExe);
                }
                return play;
            }
            finally
            {
                Log.Listener = null;
            }
        }

        // Closes Steam, then changes the launch option. From the start, and again after Try again.
        private async void Switch()
        {
            _now = State.Running;
            _steamStuck = false;
            Step("steam");
            var detail = new List<string>();
            void Note(string line)
            {
                Log.Line(line);
                detail.Add(line);
            }
            bool closed = await CloseSteam(Note);
            if (_closeAsked)
            {
                return;
            }
            if (!closed)
            {
                Note("Launch option: nothing changed");
                Stuck(detail);
                return;
            }

            _now = State.Writing;
            Step("opt");
            detail.Clear();
            bool on = _option == "on";
            string game = _files.Game;
            LaunchOptionOutcome outcome;
            try
            {
                outcome = await Task.Run(() => Steam.Apply(game, on, Note));
            }
            catch (Exception ex)
            {
                Note("Steam launch option: could not be changed: " + ex.Message);
                outcome = LaunchOptionOutcome.Failed;
            }
            _now = State.Finished;
            if (_closeAsked)
            {
                // The X came while the file was being written; it is done now.
                Log.Line("Window: closed without starting the game");
                Finish(false);
                return;
            }
            switch (outcome)
            {
                case LaunchOptionOutcome.Added:
                case LaunchOptionOutcome.Removed:
                case LaunchOptionOutcome.Unchanged:
                    Log.Line("Next: steam://rungameid/" + Paths.SteamAppId);
                    _window?.Send(Json.Object("type", "done", "backup", ShownBackup, "options", NowOptions()));
                    break;
                case LaunchOptionOutcome.SteamRunning:
                    // Steam was started again in the meantime: as if it hadn't closed.
                    Note("Launch option: nothing changed");
                    Stuck(detail);
                    return;
                default:
                    Note("localconfig.vdf: left as it was");
                    bool kept = BackupLeft();
                    if (kept)
                    {
                        Note("Backup kept (localconfig.vdf" + Steam.BackupSuffix + ")");
                    }
                    _window?.Send(Json.Object("type", "failed", "kind", "writefail", "restart", true, "backupKept", kept,
                        "detail", detail, "logPath", Log.Path ?? ""));
                    break;
            }
            // The page counts down and answers; without an answer Steam and the game start anyway.
            _safety = new System.Windows.Forms.Timer { Interval = (int)AnswerWait.TotalMilliseconds };
            _safety.Tick += (s, e) =>
            {
                Log.Line("Window: no answer after the launch option; starting Steam and the game");
                Finish(true);
            };
            _safety.Start();
        }

        private void Stuck(List<string> detail)
        {
            _now = State.Finished;
            _steamStuck = true;
            _window?.Send(Json.Object("type", "failed", "kind", "steamstuck", "restart", false, "detail", detail, "logPath", Log.Path ?? ""));
        }

        // Asks Steam to exit, the way its own Exit does, and waits up to 90 seconds for it.
        // True once Steam isn't running. Stops waiting when the window's X is pressed.
        private async Task<bool> CloseSteam(Action<string> note)
        {
            if (!await Task.Run(() => Steam.Running()))
            {
                Log.Line("Steam: not running");
                return true;
            }
            // The X came while that was being looked at: Steam isn't asked, so nothing has to start it again.
            if (_closeAsked)
            {
                return false;
            }
            string exe;
            try
            {
                exe = Steam.AskToExit(note);
            }
            catch (Exception ex)
            {
                note("Steam: could not be asked to exit: " + ex.Message);
                return false;
            }
            if (exe == null)
            {
                return false;
            }
            _steamExe = exe;
            int seconds = Steam.ExitSeconds;
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                await Task.Delay(500);
                if (_closeAsked)
                {
                    return false;
                }
                if (!await Task.Run(() => Steam.Running()))
                {
                    Log.Line($"Steam: closed after {clock.Elapsed.TotalSeconds:0.0} s");
                    return true;
                }
            }
            note($"Steam: still running {seconds} s after it was asked to exit");
            return false;
        }

        // The launch options now, for the line under the bar at the end.
        private static string NowOptions()
        {
            try
            {
                return Steam.Options() ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        // Whether localconfig.vdf's backup is there, for "The backup stays".
        private static bool BackupLeft()
        {
            try
            {
                return Steam.ConfigFiles().Any(f => File.Exists(f + Steam.BackupSuffix));
            }
            catch (Exception)
            {
                return false;
            }
        }

        // After the window's X: Steam, which was closed here, starts again on its own. One still
        // on its way out is waited for first, up to the same 90 seconds.
        private static async Task StartSteamAgain(string exe)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (await Task.Run(() => Steam.Running()))
            {
                if (clock.Elapsed.TotalSeconds >= Steam.ExitSeconds)
                {
                    Log.Line("Steam: still running, so it isn't started again");
                    return;
                }
                await Task.Delay(500);
            }
            Steam.Start(exe, Log.Line);
        }

        private void OnSwitchMessage(PageMessage message)
        {
            switch (message.Cmd)
            {
                case "retry":
                    if (_now == State.Finished && _steamStuck)
                    {
                        Log.Line("Launcher: trying again");
                        Switch();
                    }
                    break;
                case "play":
                    if (_now == State.Finished)
                    {
                        Finish(true);
                    }
                    break;
                case "close":
                    // The window's X (and "Don't start them now") never starts the game. While
                    // the file is being written, the window waits for that to end first.
                    if (_now == State.Writing)
                    {
                        Log.Line("Window: closed while the launch option is written; closing once it's done");
                        _closeAsked = true;
                        break;
                    }
                    if (_now == State.Running)
                    {
                        _closeAsked = true;
                    }
                    Log.Line("Window: closed without starting the game");
                    Finish(false);
                    break;
                case "openLog":
                case "copy":
                    OnMessage(message);
                    break;
            }
        }
    }
}
