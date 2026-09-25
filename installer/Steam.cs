using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DragNWash.Installer
{
    // Steam on this PC, for the game's launch option: where it is, whether it runs,
    // asking it to exit, and each account's userdata/<id>/config/localconfig.vdf.
    // Steam keeps these settings in memory while it runs and writes the file again
    // when it exits, so the file is only changed while Steam is closed. The rules for
    // the text itself are in SteamConfig.cs.
    internal static class Steam
    {
        private const string ProcessName = "steam";

        // How long "Close Steam for me" waits before saying Steam hasn't closed.
        internal static int ExitSeconds
        {
            get
            {
#if DEBUG
                if (int.TryParse(Environment.GetEnvironmentVariable("DNW_INSTALLER_STEAM_WAIT"), out int seconds) && seconds > 0)
                {
                    return seconds;
                }
#endif
                return 90;
            }
        }

        // Steam's folder: the first the registry names that has accounts or steam.exe.
        internal static string Root()
        {
            return InstallerCore.SteamRoots().FirstOrDefault(r => Directory.Exists(Path.Combine(r, "userdata")) || File.Exists(Path.Combine(r, "steam.exe")));
        }

        internal static bool Running()
        {
            return RunningExes().Count > 0;
        }

        // The running steam.exe processes' files; "" for one whose file cannot be read.
        private static List<string> RunningExes()
        {
            var exes = new List<string>();
            foreach (Process process in Process.GetProcessesByName(ProcessName))
            {
                using (process)
                {
                    string exe = "";
                    try
                    {
                        exe = process.MainModule.FileName;
                    }
                    catch (Exception)
                    {
                    }
#if DEBUG
                    // A test Steam folder: only a steam.exe started from it counts, never the real one.
                    string fake = InstallerCore.SteamOverride;
                    if (fake != null && !string.Equals(Path.GetDirectoryName(exe), fake.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
#endif
                    exes.Add(exe);
                }
            }
            return exes;
        }

        // Asks Steam to exit, the way its own Exit menu item does (steam.exe -shutdown),
        // and returns that steam.exe, to start it again afterwards; null when there is
        // no steam.exe to ask. Never ends the process itself.
        internal static string AskToExit(Action<string> log)
        {
            string root = Root();
            string exe = RunningExes().FirstOrDefault(e => e.Length > 0 && File.Exists(e))
                         ?? (root == null ? null : Path.Combine(root, "steam.exe"));
            if (exe == null || !File.Exists(exe))
            {
                log("Steam: steam.exe not found, so it can't be asked to exit");
                return null;
            }
            log($"Steam: asking it to exit ({exe} -shutdown)");
            Process.Start(new ProcessStartInfo(exe, "-shutdown") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose();
            return exe;
        }

        internal static void Start(string exe, Action<string> log)
        {
            try
            {
                log("Steam: starting it again");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose();
            }
            catch (Exception ex)
            {
                log($"Steam: could not start it again: {ex.Message}");
            }
        }

        // ---- the launch option ----

        // Where the game's launch options stand, over every Steam account on this PC.
        internal sealed class LaunchState
        {
            // Accounts whose localconfig.vdf could be read.
            internal int Accounts;

            // Some account has the launcher in its launch options (at any game folder's path).
            internal bool AnyHas;

            // Every account that would get it has it, at this game folder's path.
            internal bool AllSet;
        }

        internal static LaunchState State(string game)
        {
            var state = new LaunchState();
            try
            {
                string launcher = Paths.Launcher(game);
                List<Account> accounts = Load(_ => { });
                state.Accounts = accounts.Count;
                state.AnyHas = accounts.Any(a => LaunchOption.Has(LocalConfig.Options(a.Vdf, Paths.SteamAppId)));
                List<string> targets = Targets(accounts, true);
                state.AllSet = targets.Count > 0 && accounts.Where(a => targets.Contains(a.Id)).All(a => LaunchOption.IsSet(LocalConfig.Options(a.Vdf, Paths.SteamAppId), launcher));
            }
            catch (Exception)
            {
                // No Steam, or one that cannot be read: nothing to show.
            }
            return state;
        }

        // Puts the launcher into the game's launch options (on) or takes it out, in
        // every account that should have it or has it. Only with Steam closed.
        internal static LaunchOptionOutcome Apply(string game, bool on, Action<string> log)
        {
            string launcher = Paths.Launcher(game);
            if (on && !File.Exists(launcher))
            {
                log($"Steam launch option: not set, {launcher} is not there");
                return LaunchOptionOutcome.Failed;
            }
            List<Account> accounts = Load(log);
            if (accounts.Count == 0)
            {
                string root = Root();
                log("Steam launch option: no Steam account's localconfig.vdf found" + (root == null ? " (Steam's folder was not found)" : " under " + Path.Combine(root, "userdata")));
                return on ? LaunchOptionOutcome.Failed : LaunchOptionOutcome.Unchanged;
            }
            // Read first: with nothing to change, whether Steam runs doesn't matter.
            var changes = new List<(Account Account, string Before, string Text)>();
            int odd = 0;
            List<string> targets = Targets(accounts, on);
            foreach (Account account in accounts.Where(a => targets.Contains(a.Id)))
            {
                try
                {
                    string text = LocalConfig.Change(account.Vdf.Text, Paths.SteamAppId, launcher, on);
                    if (text == null)
                    {
                        log($"Steam launch option (account {account.Id}): already {(on ? "set" : "without the launcher")}");
                    }
                    else
                    {
                        changes.Add((account, LocalConfig.Options(account.Vdf, Paths.SteamAppId), text));
                    }
                }
                catch (FormatException ex)
                {
                    odd++;
                    log($"Steam launch option (account {account.Id}): left alone, {account.File} is not laid out as expected: {ex.Message}");
                }
            }
            if (changes.Count == 0)
            {
                return odd > 0 ? LaunchOptionOutcome.Failed : LaunchOptionOutcome.Unchanged;
            }
            if (Running())
            {
                log("Steam launch option: not changed, Steam is running (it writes over localconfig.vdf while it runs)");
                return LaunchOptionOutcome.SteamRunning;
            }
            int changed = 0, failed = 0;
            foreach (var (account, before, text) in changes)
            {
                try
                {
                    Write(account.File, text, account.Bom);
                    string after = LocalConfig.Options(new VdfFile(text), Paths.SteamAppId);
                    log($"Steam launch option (account {account.Id}): was {Shown(before)}, now {Shown(after)} (the file as it was: localconfig.vdf{BackupSuffix})");
                    changed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    log($"Steam launch option (account {account.Id}): could not change {account.File}: {ex.Message}");
                }
            }
            return failed > 0 ? LaunchOptionOutcome.Failed
                : changed > 0 ? (on ? LaunchOptionOutcome.Added : LaunchOptionOutcome.Removed)
                : LaunchOptionOutcome.Unchanged;
        }

        internal const string BackupSuffix = ".dnw-backup";

        // Each account's localconfig.vdf that can be read, for saying whether a backup was left.
        internal static List<string> ConfigFiles()
        {
            return Load(_ => { }).Select(a => a.File).ToList();
        }

        // The game's launch options as the file has them now, in the account that signed in
        // last (or the first that has played the game); null when there is none to read.
        internal static string Options()
        {
            List<Account> accounts = Load(_ => { });
            List<string> targets = Targets(accounts, true);
            Account account = accounts.FirstOrDefault(a => targets.Contains(a.Id));
            return account == null ? null : LocalConfig.Options(account.Vdf, Paths.SteamAppId);
        }

        private static string Shown(string options) => options.Length == 0 ? "(none)" : "[" + options + "]";

        private sealed class Account
        {
            internal string Id;
            internal string File;
            internal VdfFile Vdf;
            internal bool Bom;
        }

        private static List<string> Targets(List<Account> accounts, bool on)
        {
            string lastSignedIn = null;
            try
            {
                string root = Root();
                string users = root == null ? null : Path.Combine(root, "config", "loginusers.vdf");
                if (users != null && File.Exists(users))
                {
                    lastSignedIn = LocalConfig.LastSignedIn(new VdfFile(Read(users, out _)));
                }
            }
            catch (Exception)
            {
                // Then the accounts that have played the game.
            }
            return LocalConfig.Accounts(accounts.Select(a => (a.Id, a.Vdf)).ToList(), Paths.SteamAppId, lastSignedIn, on);
        }

        // Every account's localconfig.vdf that can be read; the others are named in the log.
        private static List<Account> Load(Action<string> log)
        {
            var accounts = new List<Account>();
            string root = Root();
            string userdata = root == null ? null : Path.Combine(root, "userdata");
            if (userdata == null || !Directory.Exists(userdata))
            {
                return accounts;
            }
            foreach (string dir in Directory.GetDirectories(userdata).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string id = Path.GetFileName(dir);
                string file = Path.Combine(dir, "config", "localconfig.vdf");
                if (!ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _) || !File.Exists(file))
                {
                    continue;
                }
                try
                {
                    string text = Read(file, out bool bom);
                    accounts.Add(new Account { Id = id, File = file, Vdf = new VdfFile(text), Bom = bom });
                }
                catch (Exception ex)
                {
                    log($"Steam launch option (account {id}): left alone, {file} could not be read: {ex.Message}");
                }
            }
            return accounts;
        }

        // UTF-8, refused when it is not, so nothing is lost when it is written back.
        private static string Read(string path, out bool bom)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            int skip = bom ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(bytes, skip, bytes.Length - skip);
        }

        // The file as it was goes to localconfig.vdf.dnw-backup, then the new text
        // replaces it in one step: Steam never finds half a file.
        private static void Write(string path, string text, bool bom)
        {
            byte[] body = new UTF8Encoding(false).GetBytes(text);
            byte[] bytes = bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
            string temp = path + ".dnw-new";
            File.WriteAllBytes(temp, bytes);
            File.Copy(path, path + BackupSuffix, true);
            try
            {
                File.Replace(temp, path, null);
            }
            catch (PlatformNotSupportedException)
            {
                // A file system without an atomic replace: copy over it instead.
                File.Copy(temp, path, true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }
    }
}
