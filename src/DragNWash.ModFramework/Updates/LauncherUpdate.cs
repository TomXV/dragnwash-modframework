using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using Microsoft.Win32;
using UnityEngine;

namespace DragNWash.ModFramework.Updates
{
    // The Mods screen's Update button hands a mod to the launcher
    // (BepInEx/DragNWash.Installer/Launcher.exe), which updates it once the
    // game has closed and then starts the game again. The game itself still
    // downloads nothing: it leaves update-request.json in the cache folder and
    // quits. docs/LAUNCHER.md describes the file.
    //
    // Started by the launcher (Steam's launch options), the game only quits:
    // the launcher is waiting for it. Started any other way, the game starts
    // Launcher.exe --update-after-exit first, which waits for this process to
    // end, updates and starts the game again through Steam.
    //
    // Also the launcher's own settings ([Launcher] in the framework's config):
    // the launcher reads them from the file before the game starts.
    internal static class LauncherUpdate
    {
        // The launcher sets this to 1 for the game it starts.
        internal const string LauncherVariable = "DNW_LAUNCHER";

        private const int RequestSchema = 1;
        private const string RequestFileName = "update-request.json";

        // The WebView2 runtime's key under EdgeUpdate. The launcher has no
        // window without the runtime, and then it doesn't update anything.
        private const string WebView2Client = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

        internal const string Section = "Launcher";
        internal const string LetteringHandwriting = "Handwriting";
        internal const string LetteringTypewriter = "Typewriter";
        internal const string ProgressBottomEdge = "Bottom edge";
        internal const string ProgressUnderText = "Under text";

        internal enum Outcome
        {
            // The request is written and the game is quitting.
            Quitting,
            // Launcher.exe is not there; nothing was written.
            NoLauncher,
            // Something failed; the log says what, and nothing is left behind.
            Failed,
        }

        private static bool? _webView2;

        // The launcher the game starts gets the game's environment. Two things in it must
        // not go on: DNW_LAUNCHER (this launcher is not the game's parent), and the
        // variables Doorstop sets once it has loaded BepInEx. A Steam the launcher starts
        // would hand those to the next game, and Doorstop would then skip loading mods.
        internal static void CleanEnvironment(ProcessStartInfo start)
        {
            var names = new List<string>();
            foreach (string name in start.EnvironmentVariables.Keys)
            {
                if (name.StartsWith("DOORSTOP_", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }
            names.Add(LauncherVariable);
            foreach (string name in names)
            {
                start.EnvironmentVariables.Remove(name);
            }
        }

        internal static string LauncherPath => Path.Combine(Path.Combine(Paths.BepInExRootPath, "DragNWash.Installer"), "Launcher.exe");

        private static string RequestPath => Path.Combine(UpdateCheck.LauncherCacheFolder, RequestFileName);

        internal static void Install(ConfigFile config)
        {
            try
            {
                var section = new SectionMeta
                {
                    DisplayName = "Launcher",
                    Description = "The window that opens before the game when it's started through the launcher. It reads these when it starts.",
                };
                config.Bind(Section, "Logo lettering", LetteringHandwriting,
                    new ConfigDescription("How MOD FRAMEWORK is drawn on the logo when the launcher starts: written by hand, one stroke at a time, or typed one letter at a time.",
                        new AcceptableValueList<string>(LetteringHandwriting, LetteringTypewriter),
                        section));
                config.Bind(Section, "Progress bar", ProgressBottomEdge,
                    new ConfigDescription("Where the progress bar sits on the logo screen: along the bottom edge of the window, or under the text.",
                        new AcceptableValueList<string>(ProgressBottomEdge, ProgressUnderText),
                        section));
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not add the launcher's settings: {ex.Message}");
            }
        }

        // Whether the Mods screen offers Update for this mod: the installer put
        // it there (UpdateCheck.CanLauncherUpdate), and the launcher can show
        // its window. Without the WebView2 runtime the launcher only starts the
        // game again, so quitting for it would be for nothing.
        internal static bool CanUpdate(string guid)
        {
            return UpdateCheck.CanLauncherUpdate(guid) && WebView2Installed();
        }

        // Writes the request, starts the launcher when it is not already waiting
        // for the game, and quits. Never throws.
        internal static Outcome QuitAndUpdate(string guid)
        {
            bool fromLauncher;
            try
            {
                fromLauncher = StartedByLauncher();
                if (!fromLauncher && !File.Exists(LauncherPath))
                {
                    ModFramework.Log.LogWarning($"Update: {LauncherPath} is not there, so {guid} can't be updated from the Mods screen. Installing again with the installer puts it back.");
                    return Outcome.NoLauncher;
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Update: could not look for the launcher: {ex.Message}");
                return Outcome.Failed;
            }

            int pid;
            try
            {
                using (Process me = Process.GetCurrentProcess())
                {
                    pid = me.Id;
                }
                WriteRequest(guid, pid);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Update: could not write {RequestFileName}: {ex.Message}");
                return Outcome.Failed;
            }

            if (!fromLauncher)
            {
                try
                {
                    var start = new ProcessStartInfo(LauncherPath,
                        $"--update-after-exit --wait-pid {pid.ToString(CultureInfo.InvariantCulture)} --mods \"{guid}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(LauncherPath),
                    };
                    CleanEnvironment(start);
                    using (Process.Start(start))
                    {
                    }
                }
                catch (Exception ex)
                {
                    ModFramework.Log.LogWarning($"Update: could not start the launcher: {ex.Message}");
                    DeleteRequest();
                    return Outcome.Failed;
                }
            }

            ModFramework.Log.LogInfo(fromLauncher
                ? $"Update: {guid} asked for; quitting, and the launcher that started the game takes over."
                : $"Update: {guid} asked for; the launcher waits for the game to close, then updates it and starts the game again.");
            // As the game's own Quit button does.
            Application.Quit();
            return Outcome.Quitting;
        }

        // { "schema": 1, "requestedUtc": "...", "gamePid": 1234, "mods": ["guid"] }
        private static void WriteRequest(string guid, int pid)
        {
            var request = new Dictionary<string, object>
            {
                ["schema"] = RequestSchema,
                ["requestedUtc"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["gamePid"] = pid,
                ["mods"] = new List<object> { guid },
            };
            string json = Operations.ToJson(request, indented: true);
            Directory.CreateDirectory(UpdateCheck.LauncherCacheFolder);
            SafeFile.Write(RequestPath, new UTF8Encoding(false), w => w.Write(json));
        }

        private static void DeleteRequest()
        {
            try
            {
                File.Delete(RequestPath);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Update: could not delete {RequestFileName}: {ex.Message}");
            }
        }

        // Started through the launcher: it set DNW_LAUNCHER=1, or (an older or
        // hand-started launcher) it is the game's parent process.
        private static bool StartedByLauncher()
        {
            if (Environment.GetEnvironmentVariable(LauncherVariable) == "1")
            {
                return true;
            }
            string parent = ParentPath();
            return parent != null && string.Equals(Path.GetFullPath(parent), Path.GetFullPath(LauncherPath), StringComparison.OrdinalIgnoreCase);
        }

        // ---- Windows ----

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessBasicInformation
        {
            public IntPtr ExitStatus;
            public IntPtr PebBaseAddress;
            public IntPtr AffinityMask;
            public IntPtr BasePriority;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInformation info, int size, out int returned);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        // The exe of the process that started the game, or null when it is gone
        // or can't be asked.
        private static string ParentPath()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return null;
            }
            try
            {
                var info = new ProcessBasicInformation();
                using (Process me = Process.GetCurrentProcess())
                {
                    if (NtQueryInformationProcess(me.Handle, 0, ref info, Marshal.SizeOf(info), out _) != 0)
                    {
                        return null;
                    }
                }
                const uint QueryLimitedInformation = 0x1000;
                IntPtr parent = OpenProcess(QueryLimitedInformation, false, info.InheritedFromUniqueProcessId.ToInt32());
                if (parent == IntPtr.Zero)
                {
                    return null;
                }
                try
                {
                    var path = new StringBuilder(1024);
                    int size = path.Capacity;
                    return QueryFullProcessImageName(parent, 0, path, ref size) ? path.ToString(0, size) : null;
                }
                finally
                {
                    CloseHandle(parent);
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogDebug($"Update: could not find the game's parent process: {ex.Message}");
                return null;
            }
        }

        // Asked once a session, the way Microsoft says to: the runtime's
        // EdgeUpdate client key, for the machine or for this user, with a
        // version. When the registry can't be read, the launcher is left to
        // find out.
        internal static bool WebView2Installed()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return false;
            }
            if (_webView2 == null)
            {
                try
                {
                    _webView2 = HasVersion(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\" + WebView2Client) ||
                                HasVersion(Registry.LocalMachine, @"SOFTWARE\" + WebView2Client) ||
                                HasVersion(Registry.CurrentUser, @"Software\" + WebView2Client);
                    if (_webView2 == false)
                    {
                        ModFramework.Log.LogInfo("Update: the WebView2 runtime isn't installed, so the launcher can't update mods; the Mods screen links to release pages instead.");
                    }
                }
                catch (Exception ex)
                {
                    ModFramework.Log.LogDebug($"Update: could not look for the WebView2 runtime: {ex.Message}");
                    _webView2 = true;
                }
            }
            return _webView2.Value;
        }

        private static bool HasVersion(RegistryKey root, string path)
        {
            using (RegistryKey key = root.OpenSubKey(path))
            {
                string version = key?.GetValue("pv") as string;
                return !string.IsNullOrEmpty(version) && version != "0.0.0.0";
            }
        }
    }
}
