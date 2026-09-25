using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// Experimental. Crash reports: the last moments of each session, kept on
    /// this computer so a crash can be looked at afterwards.
    /// </summary>
    /// <remarks>
    /// While the game runs, the framework writes short notes ("breadcrumbs")
    /// to <c>BepInEx/CrashReports/session.log</c>, one line each, flushed at
    /// once: scene changes, Unity errors, mod reloads, a heartbeat, and what
    /// mods add with <see cref="Note"/>. A clean exit ends the file with a
    /// marker. When the next start finds no marker, the game did not exit
    /// cleanly, and a report is made in <c>BepInEx/CrashReports/&lt;time&gt;/</c>:
    /// the notes, the mods and versions that were loaded, and the native stack
    /// from Unity's own crash folder when there is one. The last ten reports
    /// are kept. Nothing is sent anywhere.
    /// <para>
    /// Memory dumps: Unity's own <c>crash.dmp</c> is copied into the report, and
    /// when the game freezes (the main thread finishes no frame for
    /// <see cref="HangSeconds"/> seconds while the window has focus) a watchdog
    /// writes a minidump of the process (Windows), which a freeze never gets
    /// from Unity. Dumps hold part of the game's memory: share them privately,
    /// not in public bug reports.
    /// </para>
    /// <para>
    /// <c>[Diagnostics] TraceGpuUploads</c> (off, for the Direct3D 12 crash,
    /// Unity UUM-140564) adds a note for every upload to the GPU and every GPU
    /// resource released, with the mod that caused it.
    /// </para>
    /// </remarks>
    public static class CrashReports
    {
        private static ConfigEntry<bool> _record;
        private static ConfigEntry<bool> _traceGpu;
        private static StreamWriter _out;
        private static readonly object Gate = new object();
        private static int _mainThread;
        private static int _frame;
        private static float _nextBeat;
        private static float _beatInterval = 10f;
        private static bool _headerWritten;
        private static long _written;
        private static volatile bool _ended;

        private const string CleanEnd = Diagnostics.CrashReportWriter.CleanEnd;

        /// <summary>How long the main thread may go without a frame, with the window focused, before it counts as frozen.</summary>
        public const int HangSeconds = 15;

        private static ConfigEntry<bool> _hangDumps;
        private static ConfigEntry<bool> _reporterWindow;
        private static readonly Stopwatch SinceFrame = Stopwatch.StartNew();
        private static volatile bool _focused = true;
        private static volatile bool _ticking;
        private const long RollOverBytes = 4L * 1024 * 1024;

        /// <summary>Folder the session notes and the crash reports are kept in.</summary>
        public static string Folder => Path.Combine(Paths.BepInExRootPath, "CrashReports");

        /// <summary>The report made at this start for a previous session that crashed, or null.</summary>
        public static string LastCrashReport { get; private set; }

        /// <summary>True while notes are being recorded.</summary>
        public static bool Recording => _out != null;

        /// <summary>
        /// Leaves a note in the session record: something worth knowing if the
        /// game crashes soon after (a large load, a mode switch, an external call).
        /// Cheap, and safe from any thread; does nothing while recording is off.
        /// Keep notes short and do not put personal data in them.
        /// </summary>
        /// <remarks>
        /// A note in the <c>language</c> category (a locale code such as "ja" or
        /// "zh-Hans", or "-" for none, as GameFonts.SetLanguage leaves it) also
        /// sets the language of the crash report window.
        /// </remarks>
        public static void Note(string ownerGuid, string category, string message)
        {
            Write(category, message, ownerGuid);
            if (category == "language")
            {
                CrashReportLocale.ModLanguage(message);
            }
        }

        internal static bool TracingGpu => _out != null && _traceGpu != null && _traceGpu.Value;

        internal static void Install(ConfigFile config, Harmony harmony)
        {
            var section = new SectionMeta { DisplayName = "Diagnostics", Description = "Crash reports, kept on this computer only." };
            _record = config.Bind("Diagnostics", "CrashReports", true,
                new ConfigDescription("Keeps short notes of what happened in each session (scene changes, errors, mod reloads) in BepInEx/CrashReports, and makes a report there when the game did not exit cleanly. Nothing is sent anywhere. Takes effect at the next start.",
                    null, new SettingMeta { DisplayName = "Crash reports", RequiresRestart = true }, section));
            _traceGpu = config.Bind("Diagnostics", "TraceGpuUploads", false,
                new ConfigDescription("Experimental, for the Direct3D 12 crash: also notes every texture upload, font atlas rebuild and GPU resource released, with the mod that caused it. Slows uploads down a little. Needs Crash reports on. Takes effect at the next start.",
                    null, new SettingMeta { DisplayName = "Trace GPU uploads (Direct3D 12 investigation)", Advanced = true, RequiresRestart = true }, section));
            _reporterWindow = config.Bind("Diagnostics", "CrashReporterWindow", true,
                new ConfigDescription("On Windows, a small separate program (CrashReporter.exe) waits for the game to close and, if it crashed or froze, shows what happened in a window of its own right away. Takes effect at the next start.",
                    null, new SettingMeta { DisplayName = "Crash report window", RequiresRestart = true }, section));
            _hangDumps = config.Bind("Diagnostics", "HangDumps", true,
                new ConfigDescription($"When the game freezes for {HangSeconds} seconds while in front, writes a memory dump of it into BepInEx/CrashReports (Windows). A dump holds part of the game's memory: share it privately, not in public bug reports. Needs Crash reports on.",
                    null, new SettingMeta { DisplayName = "Memory dump when the game freezes", Advanced = true }, section));
            if (!_record.Value)
            {
                return;
            }
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            try
            {
                Directory.CreateDirectory(Folder);
                KeepPreviousCrash();
                Open(Path.Combine(Folder, "session.log"), append: false);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"[crash] Could not start the session record: {ex.Message}");
                _out = null;
                return;
            }
            Write("start", $"{DateTime.Now:yyyy-MM-dd} {ModFramework.Name} {ModFramework.Version}, Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType} \"{SystemInfo.graphicsDeviceName}\" ({SystemInfo.graphicsDeviceVersion}), " +
                               $"{SystemInfo.operatingSystem}, screen {Screen.width}x{Screen.height} {Screen.fullScreenMode}, args \"{string.Join(" ", Environment.GetCommandLineArgs().Skip(1))}\"");

            CrashReportLocale.Start();

            SceneManager.sceneLoaded += (scene, mode) => Write("scene", $"loaded '{scene.name}' ({mode})");
            SceneManager.sceneUnloaded += scene => Write("scene", $"unloaded '{scene.name}'");
            ModReload.Unloading += (guid, assembly) => Write("mods", $"reloading {guid}");
            Application.logMessageReceivedThreaded += (message, stack, type) =>
            {
                if (message == null) return;
                bool device = message.IndexOf("d3d12", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("GPU device", StringComparison.OrdinalIgnoreCase) >= 0;
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || device)
                {
                    Write("unity-" + type.ToString().ToLowerInvariant(), message.Split('\n')[0]);
                }
            };
            // The marker is the last note: what Unity does while it tears down
            // after this (releasing GPU resources) is not recorded.
            Application.quitting += () => { Write(CleanEnd, $"{_written} notes"); _ended = true; };

            var watchdog = new Thread(Watch) { IsBackground = true, Name = "CrashReports watchdog" };
            watchdog.Start();
            if (_reporterWindow.Value)
            {
                StartReporter();
            }

            if (_traceGpu.Value)
            {
                _beatInterval = 1f;
                int patched = GpuUploadTrace.Install(harmony);
                ModFramework.Log.LogMessage($"[crash] Tracing GPU uploads into the session record ({patched} methods).");
            }
            if (LastCrashReport != null)
            {
                ModFramework.Log.LogWarning($"[crash] The game did not exit cleanly last time. A crash report was saved in {LastCrashReport}");
            }
        }

        // From the core plugin's Update: the frame number (Time.frameCount may
        // only be read on the main thread), the loaded mods once, a heartbeat.
        internal static void Tick()
        {
            if (_out == null) return;
            _frame = Time.frameCount;
            SinceFrame.Restart();
            _ticking = true;
            if (!_headerWritten && _frame > 1)
            {
                _headerWritten = true;
                Write("mods", string.Join(", ", Chainloader.PluginInfos.Values.Select(p => p.Metadata.GUID + " " + p.Metadata.Version)));
            }
            if (Time.unscaledTime >= _nextBeat)
            {
                _nextBeat = Time.unscaledTime + _beatInterval;
                Write("beat", $"{Time.unscaledDeltaTime * 1000f:0.0} ms/frame");
            }
            CrashReportLocale.Tick();
        }

        // From the core plugin's OnApplicationFocus: a game in the background
        // may stop running frames on purpose, which is not a freeze.
        internal static void Focus(bool focused)
        {
            _focused = focused;
            SinceFrame.Restart();
            if (_out != null) Write("focus", focused ? "gained" : "lost");
        }

        private static void Watch()
        {
            bool frozen = false;
            while (true)
            {
                Thread.Sleep(1000);
                try
                {
                    if (!_ticking || _out == null) continue;
                    double seconds = SinceFrame.Elapsed.TotalSeconds;
                    if (!frozen && InFront() && seconds >= HangSeconds)
                    {
                        frozen = true;
                        Write("hang", $"the main thread has not finished a frame for {seconds:0} s");
                        if (_hangDumps.Value)
                        {
                            string dir = Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + "_hang");
                            Directory.CreateDirectory(dir);
                            string dump = Path.Combine(dir, "hang.dmp");
                            string result = WriteDump(dump);
                            Write("hang", result == null ? $"memory dump written: {dump}" : $"memory dump failed: {result}");
                            File.WriteAllText(Path.Combine(dir, "report.txt"),
                                $"The game froze: no frame for {seconds:0} s while in front, at {DateTime.Now:yyyy-MM-dd HH:mm:ss} (frame {_frame}).{Environment.NewLine}" +
                                $"hang.dmp is a memory dump taken then; open it in WinDbg or Visual Studio with the game's PDB files. It holds part of the game's memory: share it privately.{Environment.NewLine}",
                                new UTF8Encoding(false));
                        }
                    }
                    else if (frozen && seconds < 2)
                    {
                        frozen = false;
                        Write("hang", "the main thread is running again");
                    }
                }
                catch
                {
                    // The watchdog must never take the game down.
                }
            }
        }

        // Is the game the window in front? Asked of Windows, not of Unity: a
        // game that freezes the moment it comes back (exclusive fullscreen on
        // Direct3D 12) never gets to hear that it has focus again.
        private static bool InFront()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return _focused;
            }
            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero) return false;
                GetWindowThreadProcessId(window, out uint pid);
                return pid == (uint)Process.GetCurrentProcess().Id;
            }
            catch
            {
                return _focused;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(IntPtr process, uint processId, Microsoft.Win32.SafeHandles.SafeFileHandle file, uint type, IntPtr exception, IntPtr userStream, IntPtr callback);

        // A normal minidump with thread info and unloaded modules: every thread's
        // stack, a few MB. Returns null on success, else the reason.
        private static string WriteDump(string path)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return "not on Windows";
            }
            try
            {
                using (Process me = Process.GetCurrentProcess())
                using (var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    const uint type = 0x00000000 | 0x00001000 | 0x00000020;
                    return MiniDumpWriteDump(me.Handle, (uint)me.Id, file.SafeFileHandle, type, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)
                        ? null
                        : "error " + Marshal.GetLastWin32Error();
                }
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        internal static void Write(string category, string message, string owner = null)
        {
            StreamWriter w = _out;
            if (w == null || _ended) return;
            int thread = Thread.CurrentThread.ManagedThreadId;
            string line = $"{DateTime.Now:HH:mm:ss.fff} f{_frame} {(thread == _mainThread ? "main" : "t" + thread)} {category} {message}{(owner != null ? " <- " + owner : "")}";
            lock (Gate)
            {
                try
                {
                    w.WriteLine(line);
                    _written++;
                    if (w.BaseStream.Length > RollOverBytes)
                    {
                        // A long session: keep the previous part, start a new one.
                        w.Dispose();
                        string current = Path.Combine(Folder, "session.log");
                        string previous = Path.Combine(Folder, "session.1.log");
                        File.Copy(current, previous, overwrite: true);
                        Open(current, append: false);
                    }
                }
                catch
                {
                    // A diagnostic must never break the game.
                }
            }
        }

        private static void Open(string path, bool append)
        {
            var stream = new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough);
            _out = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }

        // The previous session left no end marker and the external reporter did
        // not report it (it was not running, or not on Windows): report it now.
        private static void KeepPreviousCrash()
        {
            string dir = Diagnostics.CrashReportWriter.Write(Folder, UnityCrashesFolder);
            if (dir != null)
            {
                LastCrashReport = dir;
            }
        }

        private static string UnityCrashesFolder => Path.Combine(Path.GetTempPath(), Application.companyName, Application.productName, "Crashes");

        // Starts CrashReporter.exe, which waits for the game to exit and, if it
        // did not exit cleanly, writes the report and shows it in a window of
        // its own. Windows only (not under Wine/Proton, where a WinForms window
        // is not worth the risk); the report at the next start covers the rest.
        private static void StartReporter()
        {
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT || RunningUnderWine())
                {
                    return;
                }
                string exe = Path.Combine(Path.GetDirectoryName(typeof(CrashReports).Assembly.Location) ?? "", "CrashReporter.exe");
                if (!File.Exists(exe))
                {
                    return;
                }
                string language = System.Globalization.CultureInfo.CurrentUICulture.Name;
                var start = new ProcessStartInfo(exe,
                    $"--pid {Process.GetCurrentProcess().Id} --folder \"{Folder}\" --unity \"{UnityCrashesFolder}\" --lang {language}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                };
                Process.Start(start);
                Write("reporter", "CrashReporter.exe is watching");
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"[crash] Could not start CrashReporter.exe: {ex.Message}");
            }
        }

        // Wine (and Proton) export wine_get_version from their ntdll; Windows
        // does not. A registry key is no proof: some Windows tools create
        // HKCU\Software\Wine too.
        internal static bool RunningUnderWine()
        {
            try
            {
                IntPtr ntdll = GetModuleHandle("ntdll.dll");
                return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
    }
}
