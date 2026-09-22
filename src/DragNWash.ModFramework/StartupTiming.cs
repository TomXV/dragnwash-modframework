using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DragNWash.ModFramework
{
    // Where startup time goes, written to the log once per launch. Player.log
    // only shows when lines arrive to about a third of a second, which cannot
    // tell a slow plugin from a slow font or a slow first frame. From the
    // core's Awake this stamps every BepInEx log line with the high-resolution
    // clock, and writes two "[startup]" blocks: one when the chainloader is
    // done (each plugin's share, the longest silences between lines), and one
    // 10 seconds after the first scene has loaded (slow frames, silences, and
    // where the first frame after it went: see StartupTiming.FirstFrame.cs).
    // Then it lets go of the log and stops running. It costs a few tens of
    // milliseconds, so it only runs when developer tools are on at launch.
    internal static partial class StartupTiming
    {
        private const string Prefix = "[startup] ";
        private const int MaxLines = 20000;
        private const int TextLength = 100;
        private const double SlowFrameMs = 100;
        private const double SceneWindowSeconds = 10;

        private struct Line
        {
            internal long At;
            internal string Source;
            internal string Text;
        }

        private static readonly List<Line> Lines = new List<Line>();
        private static ManualLogSource _log;
        private static Listener _listener;
        private static bool _installed;
        private static volatile bool _failed;
        private static int _dropped;
        private static long _awake, _complete, _sceneLoaded;
        private static DateTime _awakeClock;
        private static string _sceneName;
        // Milliseconds from the process start to the core's Awake; NaN when
        // the process start time cannot be read.
        private static double _processToAwakeMs = double.NaN;

        // First thing in the core's Awake, so it sees every log line from there.
        // Whether to go on is known only once the config is read: see Begin.
        internal static void Install(ManualLogSource log)
        {
            if (_installed) return;
            _installed = true;
            _log = log;
            _awake = Stopwatch.GetTimestamp();
            _awakeClock = DateTime.Now;
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            try
            {
                _listener = new Listener();
                BepInEx.Logging.Logger.Listeners.Add(_listener);
            }
            catch (Exception ex)
            {
                Stop("could not start: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // After DeveloperTools.Install: goes on when the tools are on, and
        // otherwise lets go of the log at once, without a word.
        internal static void Begin(MonoBehaviour host)
        {
            if (_listener == null) return;
            if (!DeveloperTools.Enabled)
            {
                Stop(null);
                return;
            }
            try
            {
                using (Process self = Process.GetCurrentProcess())
                {
                    _processToAwakeMs = (_awakeClock - self.StartTime).TotalMilliseconds;
                }
            }
            catch
            {
                // Times are then given from the core's Awake instead.
            }
            try
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                host.StartCoroutine(Run());
            }
            catch (Exception ex)
            {
                Stop("could not start: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private sealed class Listener : ILogListener
        {
            public void LogEvent(object sender, LogEventArgs eventArgs)
            {
                if (_failed) return;
                try
                {
                    string source = eventArgs.Source?.SourceName ?? "";
                    string text = eventArgs.Data?.ToString() ?? "";
                    if (source == _log.SourceName && text.StartsWith(Prefix, StringComparison.Ordinal)) return;
                    int newline = text.IndexOfAny(new[] { '\r', '\n' });
                    if (newline >= 0) text = text.Substring(0, newline);
                    if (text.Length > TextLength) text = text.Substring(0, TextLength) + "...";
                    // Lines can come from other threads (Unity's log among them).
                    lock (Lines)
                    {
                        long at = Stopwatch.GetTimestamp();
                        if (_complete == 0 && source == "BepInEx" && text.StartsWith("Chainloader startup complete", StringComparison.Ordinal))
                        {
                            Interlocked.Exchange(ref _complete, at);
                        }
                        if (Lines.Count < MaxLines) Lines.Add(new Line { At = at, Source = source, Text = text });
                        else _dropped++;
                    }
                }
                catch
                {
                    // Never from here: BepInEx is walking its listeners. Run() reports it.
                    _failed = true;
                }
            }

            public void Dispose()
            {
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                if (_sceneLoaded != 0 || Interlocked.Read(ref _complete) == 0) return;
                _sceneLoaded = Stopwatch.GetTimestamp();
                _sceneName = scene.name;
                if (!_failed) StartProbes();
            }
            catch
            {
                _failed = true;
            }
        }

        private static IEnumerator Run()
        {
            // The chainloader finishes before the first frame, so this is normally
            // there at once.
            while (Interlocked.Read(ref _complete) == 0)
            {
                if (_failed) { Stop("a log line could not be read"); yield break; }
                if (Seconds(_awake, Stopwatch.GetTimestamp()) > 60) { Stop("no \"Chainloader startup complete\" line"); yield break; }
                yield return null;
            }
            long writing = Stopwatch.GetTimestamp();
            if (!Guard(WriteChainloaderBlock)) yield break;
            // Normally written in the first frame after the scene, so part of it.
            if (_sceneLoaded != 0 && writing > _sceneLoaded) _ownInFrameMs = Ms(writing, Stopwatch.GetTimestamp());

            while (_sceneLoaded == 0)
            {
                if (_failed) { Stop("a log line could not be read"); yield break; }
                if (Seconds(_complete, Stopwatch.GetTimestamp()) > 120) { Stop("no scene loaded within two minutes"); yield break; }
                yield return null;
            }

            // Real time, so it is the same as unscaled time whatever the game's time scale.
            var slow = new List<KeyValuePair<long, long>>();
            int frames = 0;
            long last = _sceneLoaded, firstFrameEnd = 0, end;
            while (true)
            {
                yield return null;
                if (_failed) { Stop("a log line could not be read"); yield break; }
                long now = Stopwatch.GetTimestamp();
                if (frames == 0) { firstFrameEnd = now; EndProbes(); }
                frames++;
                if (Ms(last, now) > SlowFrameMs) slow.Add(new KeyValuePair<long, long>(last, now));
                last = now;
                if (Seconds(_sceneLoaded, now) >= SceneWindowSeconds) { end = now; break; }
            }
            if (Guard(() => WriteSceneBlock(slow, frames, firstFrameEnd, end))) Stop(null);
        }

        private static void WriteChainloaderBlock()
        {
            long complete = Interlocked.Read(ref _complete);
            Line[] lines = Snapshot(_awake, complete);
            var sb = new StringBuilder();
            string processPart = double.IsNaN(_processToAwakeMs)
                ? "process start -> core Awake unknown"
                : $"process start -> core Awake {Format(_processToAwakeMs)} ms";
            Add(sb, $"Chainloader: {processPart}, core Awake -> chainloader complete {Format(Ms(_awake, complete))} ms (complete at {At(complete)})");

            Add(sb, "Plugins, from each \"Loading [...]\" line to the next (Awake and whatever else runs while it loads):");
            string name = ModFramework.Name + " " + ModFramework.Version + " (from its Awake)";
            long start = _awake;
            foreach (Line line in lines)
            {
                if (line.Source != "BepInEx" || !line.Text.StartsWith("Loading [", StringComparison.Ordinal)) continue;
                Add(sb, $"  {Format(Ms(start, line.At)),6} ms  {name}");
                name = line.Text.Substring("Loading [".Length).TrimEnd(']');
                start = line.At;
            }
            Add(sb, $"  {Format(Ms(start, complete)),6} ms  {name}");

            AddGaps(sb, "Longest gaps between log lines, with the line that ended each:", lines, _awake);
            Write(sb);
        }

        private static void WriteSceneBlock(List<KeyValuePair<long, long>> slow, int frames, long firstFrameEnd, long end)
        {
            Line[] lines = Snapshot(_sceneLoaded, end);
            var sb = new StringBuilder();
            Add(sb, $"After the first scene: '{_sceneName}' loaded {Format(Ms(Interlocked.Read(ref _complete), _sceneLoaded))} ms after chainloader complete (at {At(_sceneLoaded)})");
            Add(sb, $"Frames over {Format(SlowFrameMs)} ms in the {Format(SceneWindowSeconds)} s after it: {slow.Count} of {frames}");
            foreach (KeyValuePair<long, long> frame in slow.OrderByDescending(f => f.Value - f.Key).Take(5))
            {
                Line[] during = lines.Where(l => l.At > frame.Key && l.At <= frame.Value).ToArray();
                Add(sb, $"  {Format(Ms(frame.Key, frame.Value)),6} ms  frame ending at {At(frame.Value)}, {during.Length} log line{(during.Length == 1 ? "" : "s")}{OwnPart(frame.Key, frame.Value)}");
                foreach (Line line in during.Take(6)) Add(sb, $"             [{line.Source}] {line.Text}");
                if (during.Length > 6) Add(sb, $"             (and {during.Length - 6} more)");
            }
            AddFirstFrame(sb, firstFrameEnd);
            AddGaps(sb, "Longest gaps between log lines in those seconds:", lines, _sceneLoaded);
            Write(sb);
        }

        private static void AddGaps(StringBuilder sb, string title, Line[] lines, long from)
        {
            Add(sb, title);
            long previous = from;
            var gaps = new List<KeyValuePair<double, Line>>(lines.Length);
            foreach (Line line in lines)
            {
                gaps.Add(new KeyValuePair<double, Line>(Ms(previous, line.At), line));
                previous = line.At;
            }
            foreach (KeyValuePair<double, Line> gap in gaps.OrderByDescending(g => g.Key).Take(8))
            {
                Add(sb, $"  {Format(gap.Key),6} ms  before {At(gap.Value.At)} [{gap.Value.Source}] {gap.Value.Text}");
            }
            if (_dropped > 0) Add(sb, $"  (only the first {MaxLines} log lines were kept)");
        }

        private static Line[] Snapshot(long from, long to)
        {
            lock (Lines)
            {
                return Lines.Where(l => l.At > from && l.At <= to).ToArray();
            }
        }

        // Leaves the log and the scene event, whether it finished or failed.
        private static void Stop(string reason)
        {
            _failed = true;
            StopProbes(_recording ? "startup timing stopped first" : null);
            try
            {
                if (_listener != null) BepInEx.Logging.Logger.Listeners.Remove(_listener);
                SceneManager.sceneLoaded -= OnSceneLoaded;
                lock (Lines) Lines.Clear();
                if (reason != null) _log?.LogDebug(Prefix + "Startup timing stopped: " + reason);
            }
            catch
            {
            }
            _listener = null;
        }

        // Which part of a slow frame was this timing's own patching.
        private static string OwnPart(long from, long to)
        {
            double own = Overlap(from, to, _probeStart, _probeEnd) + Overlap(from, to, _unprobeStart, _unprobeEnd);
            return own >= 1 ? $", {Format(own)} ms of it this timing's own patches" : "";
        }

        private static double Overlap(long from, long to, long start, long end)
        {
            if (start == 0 || end == 0) return 0;
            long a = Math.Max(from, start), b = Math.Min(to, end);
            return b > a ? Ms(a, b) : 0;
        }

        private static bool Guard(Action write)
        {
            try
            {
                write();
                return true;
            }
            catch (Exception ex)
            {
                Stop(ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static void Add(StringBuilder sb, string line)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(Prefix).Append(line);
        }

        // One log call, so the block stays together in Player.log.
        private static void Write(StringBuilder sb) => _log.LogInfo(sb.ToString());

        private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

        private static double Seconds(long from, long to) => (to - from) / (double)Stopwatch.Frequency;

        private static string Format(double value) => Math.Round(value).ToString("0", CultureInfo.InvariantCulture);

        // A time since the process started, or since the core's Awake when that is unknown.
        private static string At(long at)
        {
            double ms = Ms(_awake, at);
            return double.IsNaN(_processToAwakeMs)
                ? "Awake+" + (ms / 1000).ToString("0.000", CultureInfo.InvariantCulture) + " s"
                : ((ms + _processToAwakeMs) / 1000).ToString("0.000", CultureInfo.InvariantCulture) + " s";
        }
    }
}
