using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using DragNWash.Installer;
using Microsoft.Win32;
using UnityEngine;

namespace DragNWash.ModFramework.Updates
{
    // The Mods screen's switch for Steam's launch option: the same thing
    // Install.exe's "Check for mod updates when the game starts" box sets,
    // the launcher in front of %command%. installer/SteamConfig.cs has the
    // rules for the text, and it is compiled in here too, so the game reads
    // the option just as the installer and the launcher do.
    //
    // The game only reads Steam's file. Steam keeps its settings in memory
    // while it runs and writes localconfig.vdf again when it exits, so the
    // change has to wait until Steam has closed, and the game can't close
    // Steam under itself. The launcher does it: the game starts
    // Launcher.exe --launch-option on|off --wait-pid <pid> and quits, and the
    // launcher closes Steam, changes the file and starts Steam and the game
    // again. docs/LAUNCHER.md describes it.
    internal static class LaunchOptionSwitch
    {
        private const string SteamAppId = "4739660";

        internal enum Outcome
        {
            // The launcher is started and the game is quitting.
            Quitting,
            // The launcher couldn't be started; the log says why.
            Failed,
        }

        private static bool _read;
        private static bool? _on;

        // Read again the next time it's asked for: the Mods screen opened.
        internal static void Forget()
        {
            _read = false;
        }

        // Whether the launcher is in the launch options of the Steam account
        // playing now. Null when the switch isn't offered: not on Windows (the
        // launcher is Windows only, and under Proton Steam is the Linux one),
        // no Launcher.exe or WebView2 runtime, or Steam's file can't be read.
        internal static bool? State()
        {
            if (!_read)
            {
                _read = true;
                _on = Read();
            }
            return _on;
        }

        private static bool? Read()
        {
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT || CrashReports.RunningUnderWine())
                {
                    return null;
                }
                string launcher = Path.GetFullPath(LauncherUpdate.LauncherPath);
                if (!File.Exists(launcher) || !LauncherUpdate.WebView2Installed())
                {
                    return null;
                }
                string file = ConfigFile();
                if (file == null)
                {
                    ModFramework.Log.LogDebug("Launch option: no localconfig.vdf for the Steam account playing now, so the Mods screen doesn't offer the switch.");
                    return null;
                }
                string options = LocalConfig.Options(new VdfFile(File.ReadAllText(file, Encoding.UTF8)), SteamAppId);
                // The launcher at this game folder's path. Steam's folder can be
                // written in another case, so the path's case doesn't count.
                bool on = string.Equals(LaunchOption.Add(options, launcher), options, StringComparison.OrdinalIgnoreCase);
                ModFramework.Log.LogDebug($"Launch option: {(on ? "set" : "not set")} in {file}");
                return on;
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Launch option: could not read Steam's launch options, so the Mods screen doesn't offer the switch: {ex.Message}");
                return null;
            }
        }

        // userdata/<account>/config/localconfig.vdf of the account Steam has
        // signed in now, else the one that signed in last; null when there is none.
        private static string ConfigFile()
        {
            string root;
            using (RegistryKey steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                root = steam?.GetValue("SteamPath") as string;
            }
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }
            root = root.Replace('/', '\\');
            string id = null;
            using (RegistryKey active = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
            {
                if (active?.GetValue("ActiveUser") is int user && user != 0)
                {
                    id = unchecked((uint)user).ToString(CultureInfo.InvariantCulture);
                }
            }
            if (id == null)
            {
                string users = Path.Combine(root, "config", "loginusers.vdf");
                if (File.Exists(users))
                {
                    id = LocalConfig.LastSignedIn(new VdfFile(File.ReadAllText(users, Encoding.UTF8)));
                }
            }
            if (id == null)
            {
                return null;
            }
            string file = Path.Combine(root, "userdata", id, "config", "localconfig.vdf");
            return File.Exists(file) ? file : null;
        }

        // Starts the launcher to switch the launch option once the game has
        // closed, and quits. Never throws.
        internal static Outcome QuitAndSwitch(bool on)
        {
            string value = on ? "on" : "off";
            try
            {
                int pid;
                using (Process me = Process.GetCurrentProcess())
                {
                    pid = me.Id;
                }
                string launcher = LauncherUpdate.LauncherPath;
                var start = new ProcessStartInfo(launcher, $"--launch-option {value} --wait-pid {pid.ToString(CultureInfo.InvariantCulture)}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(launcher),
                };
                // The launcher this starts is not the game's parent.
                start.EnvironmentVariables.Remove(LauncherUpdate.LauncherVariable);
                using (Process.Start(start))
                {
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Launch option: could not start the launcher: {ex.Message}");
                return Outcome.Failed;
            }
            ModFramework.Log.LogInfo($"Launch option: switching it {value}; quitting, and the launcher closes Steam, changes it and starts Steam and the game again.");
            // As the game's own Quit button does.
            Application.Quit();
            return Outcome.Quitting;
        }
    }
}
