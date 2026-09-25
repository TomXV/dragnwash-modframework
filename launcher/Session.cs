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

        // How long the page may take to come up; the intro waits less, since the game waits for it.
        private static readonly TimeSpan PageWait = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan IntroPageWait = TimeSpan.FromSeconds(6);

        // The page has 6 s to come up and the intro then takes about 4.5 s; without its end by
        // then, the game starts anyway.
        private static readonly TimeSpan IntroWait = TimeSpan.FromSeconds(12);

        // The longest the window's closing may take before the game starts regardless.
        private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(2.5);

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

        // The window's X while the update ran: once it has stopped, the window closes without the game.
        private bool _closeAsked;

        // What the page's messages do: OnMessage, or the intro's own.
        private Action<PageMessage> _handler;

        // The intro: the window doesn't take the focus.
        private bool _quiet;

        private Session(GameFiles files, LauncherState state, List<ModUpdate> mods, bool restart)
        {
            _files = files;
            _state = state;
            _mods = mods;
            _restart = restart;
        }

        // Board 0 with nothing to update: the logo, "No updates", then "Starting the game" while
        // the bar fills up. The page says play at its end, and the window has closed by the time
        // this returns. True: start the game; false: the player closed the window.
        internal static async Task<bool> Intro(GameFiles files)
        {
            var session = new Session(files, null, new List<ModUpdate>(), false) { _quiet = true };
            var timer = new System.Windows.Forms.Timer { Interval = (int)IntroWait.TotalMilliseconds };
            timer.Tick += (s, e) =>
            {
                Log.Line("Window: the intro didn't end; starting the game");
                session.Finish(true);
            };
            session._handler = message =>
            {
                if (message.Cmd == "play")
                {
                    session.Finish(true);
                }
                else if (message.Cmd == "close")
                {
                    Log.Line("Window: closed without starting the game");
                    session.Finish(false);
                }
            };
            timer.Start();
            try
            {
                return await session.Show("intro", false);
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
                _window.Quiet = _quiet;
                _window.Begin(UserData);
                Task timeout = Task.Delay(_quiet ? IntroPageWait : PageWait);
                if (await Task.WhenAny(_window.Ready, timeout) == timeout || !await _window.Ready)
                {
                    Log.Line("Window: the page did not come up; going on without it");
                    Finish(true);
                }
            }
            catch (Exception ex)
            {
                Log.Line("Window: could not be shown: " + ex);
                Finish(true);
            }
            bool answer = await _answer.Task;
            await Close();
            return answer;
        }

        // The window fades out and is gone (not on the screen or the taskbar) before the owner
        // starts the game. When that takes too long or fails, it is hidden at once instead.
        private async Task Close()
        {
            LauncherWindow window = _window;
            _window = null;
            if (window == null)
            {
                return;
            }
            try
            {
                Task closing = window.FadeOut();
                if (await Task.WhenAny(closing, Task.Delay(CloseWait)) != closing)
                {
                    Log.Line("Window: closing took too long; hiding it");
                }
            }
            catch (Exception ex)
            {
                Log.Line("Window: could not fade out: " + ex.Message);
            }
            window.Gone();
            try
            {
                window.Dispose();
            }
            catch (Exception ex)
            {
                Log.Line("Window: could not be disposed: " + ex.Message);
            }
            Log.Line("Window: closed");
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
                    // The window's X (and "Don't start it now"): the launcher closes without
                    // starting the game. While files are being written it does nothing; before
                    // that, the update is cancelled first. "Play without updating" is the way
                    // to play without the update.
                    if (_now == State.Running)
                    {
                        Log.Line("Window: closed while updating; cancelling");
                        _closeAsked = true;
                        _cancel?.Cancel();
                    }
                    else if (_now == State.Choosing || _now == State.Finished)
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
                if (_closeAsked)
                {
                    Log.Line("Window: closed without starting the game");
                    Finish(false);
                    return;
                }
                _window?.Send(Json.Object("type", "cancelled"));
                return;
            }
            _now = State.Finished;
            if (_closeAsked)
            {
                // X came too late to cancel: the files were being written by then.
                Log.Line("Window: closed without starting the game");
                Finish(false);
                return;
            }
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
