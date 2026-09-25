using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DragNWash.Launcher
{
    // One showing of the window, from its init to the answer "start the game" (true)
    // or "don't" (false). It runs the updates the page asks for and tells the page
    // how they go. Everything here runs on the window's thread, except the engine.
    internal sealed class Session : IUpdateEvents
    {
        // Without an answer by then after the update is done, the game starts anyway.
        private static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(60);

        private static readonly string UserData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragNWash ModFramework", "Launcher", "WebView2");

        private enum State { Choosing, Running, Writing, Finished }

        private readonly GameFiles _files;
        private readonly LauncherState _state;
        private readonly List<ModUpdate> _mods;
        private readonly bool _restart;
        private readonly TaskCompletionSource<bool> _answer = new TaskCompletionSource<bool>();
        private LauncherWindow _window;
        private UpdateEngine _engine;
        private CancellationTokenSource _cancel;
        private List<ModUpdate> _chosen;
        private State _now = State.Choosing;
        private System.Windows.Forms.Timer _safety;

        // What the page's messages do: OnMessage, or the intro's own.
        private Action<PageMessage> _handler;

        // The intro's: whether the game's window is up already (then the window isn't shown).
        private Func<bool> _quiet;

        private Session(GameFiles files, LauncherState state, List<ModUpdate> mods, bool restart)
        {
            _files = files;
            _state = state;
            _mods = mods;
            _restart = restart;
        }

        // Board 0 while the game starts: closes when the logo is done and the game's window is up.
        internal static async Task Intro(GameFiles files, Game game)
        {
            var session = new Session(files, null, new List<ModUpdate>(), false);
            bool introDone = false;
            var timer = new System.Windows.Forms.Timer { Interval = 250 };
            DateTime opened = DateTime.UtcNow;
            timer.Tick += (s, e) =>
            {
                // The game's window covers this one soon; it goes when both are ready, and
                // after 20 s in any case.
                if ((introDone && game.WindowShown) || game.Exited || DateTime.UtcNow - opened > TimeSpan.FromSeconds(20))
                {
                    session.Finish(true);
                }
            };
            session._handler = message =>
            {
                if (message.Cmd == "introDone")
                {
                    introDone = true;
                }
                else if (message.Cmd == "close" || message.Cmd == "play")
                {
                    session.Finish(true);
                }
            };
            session._quiet = () => game.WindowShown;
            timer.Start();
            try
            {
                await session.Show("intro", false);
            }
            finally
            {
                timer.Dispose();
            }
        }

        // Boards 0, 1, 2 and 3 before the game starts: the updates, and the choice. True: start the game.
        internal static Task<bool> Choose(GameFiles files, LauncherState state, List<ModUpdate> mods)
        {
            return new Session(files, state, mods, false).Show("list", false);
        }

        // Board B: the update the player asked for in the game, then the countdown. waitFor is
        // the game closing; true: start the game again.
        internal static async Task<bool> Restart(GameFiles files, List<ModUpdate> mods, Task waitFor)
        {
            var session = new Session(files, null, mods, true);
            Task<bool> shown = session.Show("update", true);
            if (session._window != null && await session._window.Ready)
            {
                session.Step("wait");
                await waitFor;
                await Game.WaitUntilClosed(files.Game);
                Log.Line("Launcher: the game has closed");
                // Closed with its X while it waited: no update, and no start.
                if (!session._answer.Task.IsCompleted)
                {
                    session.Run(mods);
                }
            }
            else
            {
                await waitFor;
                await Game.WaitUntilClosed(files.Game);
            }
            return await shown;
        }

        private async Task<bool> Show(string mode, bool wait)
        {
            _handler = _handler ?? OnMessage;
            try
            {
                _window = new LauncherWindow(Init(mode, wait));
                _window.Message += message =>
                {
                    try
                    {
                        _handler(message);
                    }
                    catch (Exception ex)
                    {
                        Log.Line("Window: " + message.Cmd + " failed: " + ex);
                    }
                };
                _window.CloseAsked += () => _handler(new PageMessage { Cmd = "close" });
                _window.Quiet = _quiet != null;
                _window.TooLate = _quiet;
                _window.Begin(UserData);
                Task timeout = Task.Delay(TimeSpan.FromSeconds(15));
                if (await Task.WhenAny(_window.Ready, timeout) == timeout || !await _window.Ready)
                {
                    Log.Line(_quiet?.Invoke() == true ? "Window: the game's window is up already; no intro" : "Window: the page did not come up; going on without it");
                    Finish(true);
                }
            }
            catch (Exception ex)
            {
                Log.Line("Window: could not be shown: " + ex);
                Finish(true);
            }
            bool answer = await _answer.Task;
            _window?.CloseForGood();
            _window?.Dispose();
            _window = null;
            return answer;
        }

        private void Finish(bool play)
        {
            _safety?.Dispose();
            _safety = null;
            _answer.TrySetResult(play);
        }

        private void OnMessage(PageMessage message)
        {
            switch (message.Cmd)
            {
                case "skip":
                    ModUpdate skip = _mods.FirstOrDefault(m => m.Guid == message.Guid);
                    if (skip != null && _state != null)
                    {
                        if (message.On)
                        {
                            _state.Skipped[skip.Guid] = skip.Latest.Tag;
                        }
                        else
                        {
                            _state.Skipped.Remove(skip.Guid);
                        }
                        _files.WriteState(_state);
                        Log.Line($"{skip.ShownName} {skip.Latest.Tag}: {(message.On ? "skipped" : "no longer skipped")}");
                    }
                    break;
                case "open":
                    ModUpdate page = _mods.FirstOrDefault(m => m.Guid == message.Guid);
                    string url = page == null ? null : GameFiles.ReleasePage(page);
                    if (url != null)
                    {
                        Log.Line("Window: opening " + url);
                        Shell(url);
                    }
                    break;
                case "update":
                    if (_now != State.Choosing)
                    {
                        break;
                    }
                    var chosen = _mods.Where(m => m.Zip != null && (message.Mods ?? new string[0]).Contains(m.Guid)).ToList();
                    if (chosen.Count == 0)
                    {
                        Log.Line("Window: Update and play with nothing to install; starting the game");
                        Finish(true);
                        break;
                    }
                    Run(chosen);
                    break;
                case "retry":
                    if (_now == State.Finished && _chosen != null && !_restart)
                    {
                        Log.Line("Launcher: trying again");
                        Run(_chosen);
                    }
                    break;
                case "proceed":
                    if (_now == State.Running)
                    {
                        _now = State.Writing;
                        _engine?.Proceed();
                    }
                    break;
                case "cancel":
                    if (_now == State.Running)
                    {
                        Log.Line("Window: Cancel pressed");
                        _cancel?.Cancel();
                    }
                    break;
                case "play":
                    if (_now == State.Choosing || _now == State.Finished)
                    {
                        Finish(true);
                    }
                    break;
                case "close":
                    // The window's X: before the game starts it means "play without updating";
                    // once an update is done it means "don't start it now". While files are
                    // being written it waits; before that it cancels.
                    if (_now == State.Running)
                    {
                        _cancel?.Cancel();
                    }
                    else if (_now == State.Choosing)
                    {
                        Finish(!_restart);
                    }
                    else if (_now == State.Finished)
                    {
                        Log.Line("Window: closed without starting the game");
                        Finish(false);
                    }
                    break;
                case "openLog":
                    if (Log.Path != null)
                    {
                        Shell(Path.GetDirectoryName(Log.Path));
                    }
                    break;
                case "copy":
                    try
                    {
                        Clipboard.SetText(string.IsNullOrEmpty(message.Text) ? " " : message.Text);
                    }
                    catch (Exception ex)
                    {
                        Log.Line("Window: could not copy: " + ex.Message);
                    }
                    break;
            }
        }

        // Runs the update on a thread of its own and tells the page how it ended.
        private async void Run(List<ModUpdate> mods)
        {
            _chosen = mods;
            _now = State.Running;
            _cancel = new CancellationTokenSource();
            _engine = new UpdateEngine(_files.Game, this);
            Log.Listener = line => _window?.Send(Json.Object("type", "log", "text", line));
            UpdateOutcome outcome;
            try
            {
                UpdateEngine engine = _engine;
                CancellationToken token = _cancel.Token;
                outcome = await Task.Run(() => engine.Run(mods, token));
            }
            finally
            {
                Log.Listener = null;
            }
            if (outcome.Cancelled)
            {
                _now = State.Choosing;
                _window?.Send(Json.Object("type", "cancelled"));
                return;
            }
            _now = State.Finished;
            if (outcome.Failure == null)
            {
                _window?.Send(Json.Object("type", "done", "backup", outcome.Backup, "updated", outcome.Updated));
            }
            else
            {
                UpdateFailure f = outcome.Failure;
                _window?.Send(Json.Object("type", "failed", "kind", f.Kind, "changed", f.Changed, "files", f.Files, "minutes", f.Minutes,
                    "mod", f.Mod == null ? null : Json.Raw(Json.Object("name", f.Mod.ShownName, "version", f.Mod.ShownVersion)),
                    "detail", f.Detail, "logPath", Log.Path ?? "", "restart", _restart, "updated", outcome.Updated));
                if (!_restart)
                {
                    // The player chooses: Try again, or play.
                    return;
                }
            }
            // The page counts down and answers; without an answer the game starts anyway.
            _safety = new System.Windows.Forms.Timer { Interval = (int)AnswerWait.TotalMilliseconds };
            _safety.Tick += (s, e) =>
            {
                Log.Line("Window: no answer after the update; starting the game");
                Finish(true);
            };
            _safety.Start();
        }

        // ---- IUpdateEvents, from the engine's thread ----

        public void Step(string step) => _window?.Send(Json.Object("type", "step", "step", step));

        public void Download(int index, int count, ModUpdate mod, long done, long total, long allDone, long allTotal) =>
            _window?.Send(Json.Object("type", "download", "index", index, "count", count, "name", mod.ShownName, "version", mod.ShownVersion,
                "done", done, "total", total, "allDone", allDone, "allTotal", allTotal));

        public void Verified(int index, int count, ModUpdate mod, bool sha) =>
            _window?.Send(Json.Object("type", "verified", "index", index, "count", count, "name", mod.ShownName, "version", mod.ShownVersion, "sha", sha));

        public void Checked() => _window?.Send(Json.Object("type", "checked"));

        // ---- the page's init ----

        private string Init(string mode, bool wait)
        {
            var (typewriter, underText) = _files.Settings();
            string checkedUtc = _mods.Select(m => m.Latest?.CheckedUtc).Where(t => !string.IsNullOrEmpty(t)).OrderByDescending(t => t, StringComparer.Ordinal).FirstOrDefault() ?? "";
            var mods = _mods.Select(m => Json.Raw(Json.Object(
                "guid", m.Guid,
                "name", m.ShownName,
                "from", m.InstalledVersion ?? "",
                "to", m.ShownVersion ?? "",
                "tag", m.Latest?.Tag ?? "",
                "size", m.Zip?.Size ?? 0L,
                "installable", m.Zip != null,
                "notes", Json.Raw(Json.Object("body", m.Latest?.Body ?? "", "truncated", m.Latest?.BodyTruncated ?? false, "publishedAt", m.Latest?.PublishedAt ?? "")),
                "selected", m.Zip != null))).ToList();
            return Json.Object(
                "type", "init",
                "mode", mode,
                "lang", _files.Language(),
                "lettering", typewriter ? "type" : "write",
                "bar", underText ? "text" : "edge",
                "restart", _restart,
                "wait", wait,
                "mods", mods,
                "checkedUtc", checkedUtc,
                "logPath", Log.Path ?? "",
                "backupPath", @"BepInEx\" + Installer.Paths.InstallerFolder + @"\backup");
        }

        private static void Shell(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Line("Window: could not open " + target + ": " + ex.Message);
            }
        }
    }

    // Just enough JSON for the page's events: strings, numbers, booleans, lists and nested objects.
    internal static class Json
    {
        internal sealed class RawJson
        {
            internal readonly string Text;

            internal RawJson(string text)
            {
                Text = text;
            }
        }

        internal static RawJson Raw(string json) => new RawJson(json);

        // Name, value, name, value...
        internal static string Object(params object[] pairs)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                Write(sb, (string)pairs[i]);
                sb.Append(':');
                Write(sb, pairs[i + 1]);
            }
            return sb.Append('}').ToString();
        }

        private static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case RawJson raw:
                    sb.Append(raw.Text);
                    break;
                case string s:
                    sb.Append('"');
                    foreach (char c in s)
                    {
                        // Also escaped: < (so no "</script>" can close anything) and the two JavaScript line breaks.
                        if (c == '"' || c == '\\')
                        {
                            sb.Append('\\').Append(c);
                        }
                        else if (c < ' ' || c == '<' || c == '>' || c == '&' || c == '\u2028' || c == '\u2029')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                    }
                    sb.Append('"');
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    break;
                case long n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in list)
                    {
                        if (!first)
                        {
                            sb.Append(',');
                        }
                        first = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    Write(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }
    }
}
