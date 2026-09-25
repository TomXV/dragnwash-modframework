using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace DragNWash.Installer
{
    internal static class Paths
    {
        internal const string SteamAppId = "4739660";
        internal const string GameExe = "DragNWash.exe";
        internal const string GameProcess = "DragNWash";
        internal const string FrameworkPrefix = "DragNWash.ModFramework";
        internal const string FrameworkConfigPrefix = "com.tomxv.dragnwash.modframework";
        internal const string FrameworkPatcher = "DragNWash.ModFramework.Preloader.dll";
        internal const string BepInExVersion = "5.4.23.5";
        internal const string BepInExUrl = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip";
        internal const string BepInExSha256 = "82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4";
        internal const long BepInExSize = 639118;

        // Where a mod's pinned ModFramework comes from (manifest schema 2). The address is
        // built here from the version alone; github.com sends the download on to
        // release-assets.githubusercontent.com. api.github.com is never asked.
        internal const string FrameworkRepo = "TomXV/dragnwash-modframework";
        internal const string BepInExRepo = "BepInEx/BepInEx";
        internal const string ReleaseAssetsHost = "release-assets.githubusercontent.com";

        internal static string FrameworkUrl(string version) => $"https://github.com/{FrameworkRepo}/releases/download/v{version}/DragNWash.ModFramework-{version}.zip";

        internal static string FrameworkReleasePage(string version) => $"https://github.com/{FrameworkRepo}/releases/tag/v{version}";

        // Left in BepInEx/ when an installer added BepInEx. The second name is the one
        // Drag'n Wash Localization's own installers used before this installer existed.
        internal const string Marker = ".bepinex-installed-by-dragnwash-installer";
        internal const string OldMarker = ".bepinex-installed-by-dragnwash-localization";

        // In BepInEx/: the installer's staging folder and the backup of the files the
        // last install replaced. BepInEx loads nothing from it.
        internal const string InstallerFolder = "DragNWash.Installer";

        // The launcher (docs/LAUNCHER_APP.md) lives in the installer's folder, next to the
        // WebView2 files it needs; the framework's zip has them all there. Steam's launch
        // option starts it: "<game>\BepInEx\DragNWash.Installer\Launcher.exe" %command%.
        internal const string LauncherExe = "Launcher.exe";

        // The first framework release whose zip has the launcher.
        internal static readonly Version LauncherSince = new Version(1, 6, 0);

        internal static string Launcher(string game) => Path.Combine(game, "BepInEx", InstallerFolder, LauncherExe);

        // What Doorstop (winhttp.dll) starts for BepInEx 5, relative to the game folder.
        internal const string BepInExPreloader = @"BepInEx\core\BepInEx.Preloader.dll";

        // The other Drag'n Wash loader, KrazenLabs/dnw-modloader: Doorstop too, with its
        // own files in this folder next to the game.
        internal const string DnwModLoaderFolder = "DnWModLoader";
        internal const string DnwModLoaderName = "dnw-modloader";

        // Files of the framework's Mods screen that name plugins by path.
        internal static readonly string[] FrameworkLists =
        {
            "com.tomxv.dragnwash.modframework.disabled.txt",
            "com.tomxv.dragnwash.modframework.state.txt",
            "com.tomxv.dragnwash.modframework.uninstall.txt",
        };
    }

    // Install and uninstall, without any UI. Every step writes a line to the log.
    internal sealed class InstallerCore
    {
        private readonly ModManifest _manifest;
        private readonly string _payload; // folder with BepInEx/ from the zip
        private readonly Action<string> _log;

        internal InstallerCore(ModManifest manifest, string payload, Action<string> log)
        {
            _manifest = manifest;
            _payload = payload;
            _log = log;
        }

        // ---- finding the game ----

        internal static string FindGame()
        {
            var libraries = new List<string>();
            foreach (string steam in SteamRoots())
            {
                libraries.Add(steam);
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf))
                {
                    continue;
                }
                foreach (string line in File.ReadAllLines(vdf))
                {
                    Match m = Regex.Match(line, "^\\s*\"path\"\\s+\"(.+)\"\\s*$");
                    if (m.Success)
                    {
                        libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                    }
                }
            }
            foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string acf = Path.Combine(library, "steamapps", $"appmanifest_{Paths.SteamAppId}.acf");
                    if (!File.Exists(acf))
                    {
                        continue;
                    }
                    string dir = File.ReadAllLines(acf).Select(l => Regex.Match(l, "^\\s*\"installdir\"\\s+\"(.+)\"")).Where(m => m.Success).Select(m => m.Groups[1].Value).FirstOrDefault();
                    string full = dir == null ? null : Path.Combine(library, "steamapps", "common", dir);
                    if (full != null && IsGameFolder(full))
                    {
                        return full;
                    }
                }
                catch (Exception)
                {
                    // A library on a drive that is gone; try the next one.
                }
            }
            return null;
        }

#if DEBUG
        // DNW_INSTALLER_STEAM=<folder>: a Debug build takes that folder for Steam's, to
        // test finding the game and the launch option without the real Steam. A Release
        // build never reads it.
        internal static readonly string SteamOverride = Environment.GetEnvironmentVariable("DNW_INSTALLER_STEAM") is string s && s.Length > 0 ? Path.GetFullPath(s) : null;
#endif

        internal static IEnumerable<string> SteamRoots()
        {
#if DEBUG
            if (SteamOverride != null)
            {
                yield return SteamOverride;
                yield break;
            }
#endif
            foreach (var (hive, key, value) in new[]
            {
                (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            })
            {
                string path = null;
                try
                {
                    using (RegistryKey k = hive.OpenSubKey(key))
                    {
                        path = k?.GetValue(value) as string;
                    }
                }
                catch (Exception)
                {
                }
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    yield return path.Replace('/', '\\');
                }
            }
        }

        internal static bool IsGameFolder(string path)
        {
            return !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, Paths.GameExe));
        }

        // Only a game started from this folder counts; when its path cannot be
        // read, assume it is this one.
        internal static bool GameRunning(string game)
        {
            string exe = Path.GetFullPath(Path.Combine(game, Paths.GameExe));
            foreach (Process process in Process.GetProcessesByName(Paths.GameProcess))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(process.MainModule.FileName), exe, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    return true;
                }
                finally
                {
                    process.Dispose();
                }
            }
            return false;
        }

        internal static bool HasBepInEx(string game)
        {
            return File.Exists(Path.Combine(game, "BepInEx", "core", "BepInEx.dll"));
        }

        // Another mod loader in the game folder, which unpacking BepInEx would break by
        // replacing its winhttp.dll and doorstop_config.ini. Found says what gave it
        // away (English, for the log) and is null when there is none; Name is the
        // loader's name when it is one this installer knows.
        internal static (string Name, string Found) OtherLoader(string game)
        {
            string folder = Path.Combine(game, Paths.DnwModLoaderFolder);
            if (Directory.Exists(folder))
            {
                return (Paths.DnwModLoaderName, $"{Paths.DnwModLoaderFolder}\\ is in the game folder");
            }
            bool config = File.Exists(Path.Combine(game, "doorstop_config.ini"));
            if (config && !DoorstopStartsBepInEx(game, out string target))
            {
                string name = target != null && target.IndexOf(Paths.DnwModLoaderFolder, StringComparison.OrdinalIgnoreCase) >= 0 ? Paths.DnwModLoaderName : null;
                return (name, $"doorstop_config.ini starts {target ?? "nothing it names"}, not {Paths.BepInExPreloader}");
            }
            // A winhttp.dll whose config starts BepInEx is what is left of BepInEx, and
            // unpacking BepInEx again mends it.
            if (!config && File.Exists(Path.Combine(game, "winhttp.dll")) && !HasBepInEx(game))
            {
                return (null, "winhttp.dll is in the game folder without BepInEx\\core\\BepInEx.dll or a doorstop_config.ini");
            }
            return (null, null);
        }

        // Whether doorstop_config.ini names BepInEx's preloader as the assembly to start.
        // Doorstop 4 (BepInEx 5.4.22 on) calls the key target_assembly, Doorstop 3
        // targetAssembly; the path may be relative to the game folder or absolute.
        private static bool DoorstopStartsBepInEx(string game, out string target)
        {
            target = null;
            foreach (string raw in File.ReadAllLines(Path.Combine(game, "doorstop_config.ini")))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal) || eq <= 0)
                {
                    continue;
                }
                string key = line.Substring(0, eq).Trim();
                if (string.Equals(key, "target_assembly", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "targetAssembly", StringComparison.OrdinalIgnoreCase))
                {
                    target = line.Substring(eq + 1).Trim().Trim('"');
                    break;
                }
            }
            if (string.IsNullOrEmpty(target))
            {
                target = null;
                return false;
            }
            try
            {
                return string.Equals(
                    Path.GetFullPath(Path.Combine(game, target.Replace('/', '\\'))),
                    Path.GetFullPath(Path.Combine(game, Paths.BepInExPreloader)),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // Not a path at all.
                return false;
            }
        }

        private static InstallerException OtherLoaderError(string name, string found)
        {
            return name == null
                ? new InstallerException(Strings.Key.OtherLoader, found)
                : new InstallerException(Strings.Key.OtherLoaderNamed, found, name);
        }

        // The installed version of the mod: its manifest copy, or its first DLL.
        internal string InstalledVersion(string game)
        {
            string folder = Path.Combine(game, "BepInEx", "plugins", _manifest.Plugins[0]);
            string copy = Path.Combine(folder, ModManifest.FileName);
            if (File.Exists(copy))
            {
                try
                {
                    return ModManifest.Load(copy).Version ?? "?";
                }
                catch (Exception)
                {
                }
            }
            if (!Directory.Exists(folder))
            {
                return null;
            }
            // A folder left with only the player's kept data is not an installed mod.
            string dll = Directory.GetFiles(folder, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            return dll == null ? null : FileVersionInfo.GetVersionInfo(dll).FileVersion ?? "?";
        }

        internal string ReadConfig(string game, ModConfigTarget target)
        {
            return ConfigFile.Get(Path.Combine(game, "BepInEx", "config", target.File), target.Section, target.Key);
        }

        internal static void CheckReady(string game)
        {
            if (!IsGameFolder(game))
            {
                throw new InstallerException(Strings.Key.NotFound);
            }
            if (GameRunning(game))
            {
                throw new InstallerException(Strings.Key.Running);
            }
        }

        // ---- install ----

        // progress hears which step Install is on and how far a download is. cancel stops
        // it while it downloads and checks; once the game folder is being changed it is
        // too late. The result says what was done, for the window's summary.
        internal InstallResult Install(string game, IDictionary<string, string> choices, InstallOptions options = null,
            IProgress<InstallProgress> progress = null, CancellationToken cancel = default)
        {
            options = options ?? new InstallOptions();
            CheckReady(game);
            string payloadPlugins = Path.Combine(_payload, "BepInEx", "plugins");
            foreach (string plugin in _manifest.Plugins)
            {
                if (!Directory.Exists(Path.Combine(payloadPlugins, plugin)))
                {
                    throw new InstallerException(Strings.Key.NoPayload);
                }
            }
            _log($"Game: {game}");
            // Before anything is downloaded or changed: one loader per game folder.
            var (loader, found) = OtherLoader(game);
            if (found != null)
            {
                _log($"Loader: another mod loader{(loader == null ? "" : " (" + loader + ")")}: {found}; nothing was changed");
                throw OtherLoaderError(loader, found);
            }
            _log(HasBepInEx(game)
                ? $"Loader: BepInEx {DllVersion(Path.Combine(game, "BepInEx", "core", "BepInEx.dll"))}, no other loader found"
                : "Loader: none yet, no other loader found");
            progress?.Report(InstallProgress.At(InstallStage.Check));

            var result = new InstallResult
            {
                OldMod = InstalledVersion(game),
                OldFramework = InstalledFramework(game),
                OtherMods = OtherMods(game).Count(n => Directory.Exists(Path.Combine(game, "BepInEx", "plugins", n))),
            };
            FrameworkPlan framework = PlanFramework(game);
            if (framework == null && options.FrameworkZip != null)
            {
                _log("ModFramework: --framework-zip not used, this mod's zip brings its own framework or needs none");
            }
            bool bepInEx = !HasBepInEx(game);
            bool fetchFramework = framework != null && framework.Download && !(options.KeepFramework && framework.MinimumsMet);
            bool downloadFramework = fetchFramework && options.FrameworkZip == null;
            bool downloadBepInEx = bepInEx && options.BepInExZip == null;
            // --no-download: stop before the first connection, not halfway.
            if (options.NoDownload && downloadFramework)
            {
                throw new InstallerException(Strings.Key.FwNoDownload, (string)null, framework.Pin.Version)
                {
                    LogText = $"ModFramework: {framework.Pin.Version} has to be downloaded, but --no-download is set; nothing was changed",
                };
            }
            if (options.NoDownload && downloadBepInEx)
            {
                throw new InstallerException(Strings.Key.BepInExNoDownload, (string)null, Paths.BepInExVersion)
                {
                    LogText = $"BepInEx: {Paths.BepInExVersion} has to be downloaded, but --no-download is set; nothing was changed",
                };
            }

            string temp = null;
            string Temp()
            {
                if (temp == null)
                {
                    // A folder of this run's own, so nothing else in %TEMP% is read or replaced.
                    temp = Path.Combine(Path.GetTempPath(), Paths.InstallerFolder, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(temp);
                }
                return temp;
            }
            try
            {
                int downloads = (downloadFramework ? 1 : 0) + (downloadBepInEx ? 1 : 0);
                string frameworkZip = framework == null ? null : FetchFramework(framework, options, Temp, downloads, progress, cancel, result);
                string bepInExZip = !bepInEx ? null : options.BepInExZip ?? DownloadBepInEx(Temp(), downloads, progress, cancel);
                progress?.Report(InstallProgress.At(InstallStage.Verify));
                if (bepInExZip != null)
                {
                    CheckBepInEx(bepInExZip);
                }
                // Nothing in the game folder has changed up to here, so stopping is
                // still clean. From here on a failure is undone from the journal.
                cancel.ThrowIfCancellationRequested();
                progress?.Report(InstallProgress.At(InstallStage.Backup));
                var journal = new InstallJournal(game);
                try
                {
                    Put(journal, game, choices, bepInExZip, framework, frameworkZip, result, progress);
                }
                catch (Exception ex)
                {
                    _log($"Copying failed, putting everything back: {ex.Message}");
                    int undone = journal.RollBack(_log, out int failed);
                    if (failed > 0)
                    {
                        throw new InstallerException(Strings.Key.RolledBackPartly, ex, failed, journal.BackupShown);
                    }
                    if (undone > 0)
                    {
                        throw new InstallerException(Strings.Key.RolledBack, ex, undone);
                    }
                    throw;
                }
                journal.Commit(_log);
                result.Replaced = journal.Replaced;
                result.Backup = journal.BackupShown;
                result.BepInEx = bepInExZip != null;
            }
            finally
            {
                if (temp != null)
                {
                    try
                    {
                        InstallJournal.DeleteTree(temp);
                        Directory.Delete(Path.GetDirectoryName(temp)); // only when no other run's folder is in it
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            result.NewFramework = InstalledFramework(game);
            result.Choices = choices == null ? new Dictionary<string, string>() : new Dictionary<string, string>(choices);
            _log($"{_manifest.Name} {_manifest.Version}: installed");
            progress?.Report(InstallProgress.At(InstallStage.Done));
            return result;
        }

        // Every change Install makes in the game folder, each through the journal.
        private void Put(InstallJournal journal, string game, IDictionary<string, string> choices, string bepInExZip,
            FrameworkPlan framework, string frameworkZip, InstallResult result, IProgress<InstallProgress> progress)
        {
            // Unpacked and checked first, so a zip that lacks a part stops before BepInEx is put in place.
            string staged = frameworkZip == null ? null : StageFramework(journal, framework, frameworkZip, result);
            if (bepInExZip == null)
            {
                _log("BepInEx: already present");
            }
            else
            {
                InstallBepInEx(journal, game, bepInExZip);
            }
            if (staged != null)
            {
                PutFramework(journal, game, framework, staged, result);
                PutLauncher(journal, game, staged, result);
            }
            else if (framework == null)
            {
                InstallFramework(journal, game, result);
                PutLauncher(journal, game, _payload, result);
            }

            progress?.Report(InstallProgress.At(InstallStage.Put));
            string payloadPlugins = Path.Combine(_payload, "BepInEx", "plugins");
            string plugins = Path.Combine(game, "BepInEx", "plugins");
            foreach (string plugin in _manifest.Plugins)
            {
                // Copied over what is there: files the player added (their own
                // translations, working files) are never deleted by an update.
                journal.CopyTree(Path.Combine(payloadPlugins, plugin), Path.Combine(plugins, plugin));
                EnableFiles(game, plugin, journal);
                _log($"{plugin}: copied");
            }
            // Lets the Mods screen and later installers know what belongs to the mod.
            journal.WriteAllText(Path.Combine(plugins, _manifest.Plugins[0], ModManifest.FileName), _manifest.ToJson());

            progress?.Report(InstallProgress.At(InstallStage.Settings));
            foreach (ModChoice choice in _manifest.Choices)
            {
                if (choices != null && choices.TryGetValue(choice.Id, out string value) && choice.Options.Any(o => o.Value == value))
                {
                    string config = Path.Combine(game, "BepInEx", "config", choice.Config.File);
                    journal.Change(config);
                    ConfigFile.Set(config, choice.Config.Section, choice.Config.Key, value);
                    _log($"{choice.Id}: {value}");
                }
            }
        }

        // Into the run's temp folder, so a download that fails or is stopped leaves the
        // game folder as it was.
        private string DownloadBepInEx(string temp, int downloads, IProgress<InstallProgress> progress, CancellationToken cancel)
        {
            string zip = Path.Combine(temp, Path.GetFileName(new Uri(Paths.BepInExUrl).AbsolutePath));
            _log($"BepInEx: downloading {Paths.BepInExUrl}");
            try
            {
                Download(InstallStage.DownloadBepInEx, "BepInEx " + Paths.BepInExVersion, Paths.BepInExUrl, zip, Paths.BepInExSize, downloads, progress, cancel);
            }
            catch (DownloadFailure ex)
            {
                throw DownloadError(ex, "BepInEx", Paths.BepInExUrl, null);
            }
            return zip;
        }

        private void Download(InstallStage stage, string what, string url, string file, long size, int downloads,
            IProgress<InstallProgress> progress, CancellationToken cancel)
        {
            int index = stage == InstallStage.DownloadBepInEx ? downloads : 1;
            int shown = -1;
            progress?.Report(InstallProgress.Downloaded(stage, what, new Uri(url).Host, index, downloads, 0, size));
            Downloader.Fetch(url, file, size, done =>
            {
                int percent = (int)(done * 100 / size);
                if (percent != shown)
                {
                    shown = percent;
                    progress?.Report(InstallProgress.Downloaded(stage, what, new Uri(url).Host, index, downloads, done, size));
                }
            }, _log, what.Split(' ')[0], cancel);
        }

        // Before the game folder is touched: the zip must be the pinned release.
        private void CheckBepInEx(string zip)
        {
            string hash = Downloader.Sha256Of(zip);
            if (hash != Paths.BepInExSha256)
            {
                throw new InstallerException(Strings.Key.BepInExHash, hash);
            }
            _log("BepInEx: SHA-256 OK, unpacking");
        }

        // Unpacked into the staging folder first, so a zip that breaks off halfway
        // has not touched the game's own files, then copied into place.
        private void InstallBepInEx(InstallJournal journal, string game, string zip)
        {
            string staging = journal.Staging(Path.GetFileNameWithoutExtension(new Uri(Paths.BepInExUrl).AbsolutePath));
            Unpack(zip, staging, _ => true);
            journal.CopyTree(staging, game);
            journal.WriteAllText(Path.Combine(game, "BepInEx", Paths.Marker), "BepInEx was added by the Drag'n Wash mod installer.\r\n");
            _log("BepInEx: installed");
        }

        // The entries of zip that take accepts (by their name in the zip, with '/'),
        // unpacked under dir. An entry that would land outside dir is skipped.
        private static void Unpack(string zip, string dir, Func<string, bool> take)
        {
            string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !take(entry.FullName))
                    {
                        continue;
                    }
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
        }

        // The framework and its libraries from this mod's own zip (schema 1): another
        // mod may have brought a newer version already, and an older one must never
        // replace it.
        private void InstallFramework(InstallJournal journal, string game, InstallResult result)
        {
            string source = Path.Combine(_payload, "BepInEx", "plugins");
            string plugins = Path.Combine(game, "BepInEx", "plugins");
            journal.CreateDirectory(plugins);
            foreach (string folder in Directory.Exists(source) ? Directory.GetDirectories(source) : new string[0])
            {
                string name = Path.GetFileName(folder);
                if (!name.StartsWith(Paths.FrameworkPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                Version offered = DllVersion(Path.Combine(folder, name + ".dll"));
                if (name.Equals(Paths.FrameworkPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Offered = ShortVersion(offered);
                }
                Version have = DllVersion(Path.Combine(plugins, name, name + ".dll"));
                if (have != null && offered != null && have > offered)
                {
                    _log($"{name}: kept {have} (newer than {offered})");
                    result.Kept.Add((FrameworkPart.ShortName(name), have, true));
                    continue;
                }
                journal.CopyTree(folder, Path.Combine(plugins, name));
                EnableFiles(game, name, journal);
                result.Updated.Add(FrameworkPart.ShortName(name));
                _log($"{name}: {offered}");
            }

            string patcher = Path.Combine(_payload, "BepInEx", "patchers", Paths.FrameworkPatcher);
            if (File.Exists(patcher))
            {
                string patchers = Path.Combine(game, "BepInEx", "patchers");
                Version offered = DllVersion(patcher);
                Version have = DllVersion(Path.Combine(patchers, Paths.FrameworkPatcher));
                if (!(have != null && offered != null && have > offered))
                {
                    journal.CopyFile(patcher, Path.Combine(patchers, Paths.FrameworkPatcher));
                    result.Updated.Add(FrameworkPart.PreloaderName);
                }
            }
        }

        // The launcher and the files it needs, from the framework's zip (or this mod's,
        // when it brings the framework) into BepInEx/DragNWash.Installer, whether or not
        // Steam's launch option is set: the Update button in the game uses it too. Never
        // an older launcher over a newer one. A file in use, when the launcher itself is
        // installing an update, is moved aside first (InstallJournal.CopyFileInUse).
        private void PutLauncher(InstallJournal journal, string game, string source, InstallResult result)
        {
            string from = Path.Combine(source, "BepInEx", Paths.InstallerFolder);
            string exe = Path.Combine(from, Paths.LauncherExe);
            if (!File.Exists(exe))
            {
                return;
            }
            string to = Path.Combine(game, "BepInEx", Paths.InstallerFolder);
            Version offered = DllVersion(exe), have = DllVersion(Path.Combine(to, Paths.LauncherExe));
            if (have != null && offered != null && have > offered)
            {
                _log($"Launcher: kept {ShortVersion(have)} (newer than {ShortVersion(offered)})");
                return;
            }
            InstallJournal.DeleteMovedAside(to);
            int copied = 0;
            foreach (string file in Directory.GetFiles(from))
            {
                string target = Path.Combine(to, Path.GetFileName(file));
                if (!InstallJournal.SameBytes(file, target))
                {
                    journal.CopyFileInUse(file, target);
                    copied++;
                }
            }
            result.Launcher = copied > 0;
            _log(copied == 0 ? $"Launcher: {ShortVersion(offered)} already in place" : $"Launcher: {ShortVersion(offered)} -> BepInEx\\{Paths.InstallerFolder} ({copied} files)");
        }

        // Whether Install puts the launcher in place: this mod's zip brings it with the
        // framework, or the framework is fetched from a release that has it. Reads the
        // folder only.
        internal bool PutsLauncher(string game)
        {
            FrameworkPlan plan = PlanFramework(game);
            if (plan != null)
            {
                return plan.Download && plan.Pin.Pinned >= Paths.LauncherSince;
            }
            string from = Path.Combine(_payload, "BepInEx", Paths.InstallerFolder);
            string exe = Path.Combine(from, Paths.LauncherExe);
            if (!File.Exists(exe))
            {
                return false;
            }
            string to = Path.Combine(game, "BepInEx", Paths.InstallerFolder);
            Version offered = DllVersion(exe), have = DllVersion(Path.Combine(to, Paths.LauncherExe));
            return !(have != null && offered != null && have > offered)
                   && Directory.GetFiles(from).Any(f => !InstallJournal.SameBytes(f, Path.Combine(to, Path.GetFileName(f))));
        }

        // Whether the launcher is there after Install: already, or put by it.
        internal bool LauncherAfterInstall(string game)
        {
            return File.Exists(Paths.Launcher(game)) || PutsLauncher(game);
        }

        // ---- the framework from its GitHub release (schema 2) ----

        // Whether this installer may fetch the framework from the internet: the manifest
        // pins one and this mod's zip does not bring its own.
        internal bool FetchesFramework
        {
            get
            {
                string payloadPlugins = Path.Combine(_payload, "BepInEx", "plugins");
                return _manifest.Framework != null && !(Directory.Exists(payloadPlugins) && Directory.EnumerateDirectories(payloadPlugins, Paths.FrameworkPrefix + "*").Any());
            }
        }

        // What the framework needs in this game folder, from what is installed there:
        // null when this mod's zip brings the framework itself (schema 1) or the
        // manifest pins none. Reads the folder only.
        internal FrameworkPlan PlanFramework(string game)
        {
            FrameworkPin pin = _manifest.Framework;
            if (!FetchesFramework)
            {
                return null;
            }
            string plugins = Path.Combine(game, "BepInEx", "plugins");
            var parts = new List<FrameworkPart>();
            foreach (string name in new[] { Paths.FrameworkPrefix }.Concat(pin.Libraries()))
            {
                bool named = pin.Needs.Keys.Contains(name, StringComparer.OrdinalIgnoreCase);
                parts.Add(new FrameworkPart(name, false, DllVersion(Path.Combine(plugins, name, name + ".dll")), named ? pin.Minimum(name) : null));
            }
            parts.Add(new FrameworkPart(Path.GetFileNameWithoutExtension(Paths.FrameworkPatcher), true,
                DllVersion(Path.Combine(game, "BepInEx", "patchers", Paths.FrameworkPatcher)), null));
            return new FrameworkPlan(pin, parts);
        }

        // The zip the framework's parts come from, checked against the manifest; null
        // when nothing is to be fetched (everything is there, or the installed
        // framework is kept on purpose). The game folder is not touched here.
        private string FetchFramework(FrameworkPlan plan, InstallOptions options, Func<string> temp, int downloads,
            IProgress<InstallProgress> progress, CancellationToken cancel, InstallResult result)
        {
            FrameworkPin pin = plan.Pin;
            if (!plan.Download)
            {
                foreach (FrameworkPart part in plan.Parts)
                {
                    _log(part.Describe(pin, null) + " -> keep");
                }
                _log("ModFramework: everything this mod needs is installed; nothing is downloaded, no connection is made");
                if (options.FrameworkZip != null)
                {
                    _log("ModFramework: --framework-zip not used");
                }
                result.FrameworkSatisfied = true;
                return null;
            }
            if (options.KeepFramework)
            {
                if (!plan.MinimumsMet)
                {
                    throw new InstallerException(Strings.Key.FwKeepNotMet, (string)null, pin.Version);
                }
                _log($"ModFramework: kept the installed {ShortVersion(plan.Core)}, which meets this mod's minimums; {pin.Version} is not downloaded");
                result.FrameworkKept = true;
                return null;
            }
            _log($"ModFramework: {pin.Version} is needed ({string.Join("; ", plan.Parts.Where(p => p.Fetch(pin)).Select(p => p.Describe(pin, null)))})");
            if (options.FrameworkZip != null)
            {
                _log($"ModFramework: using {options.FrameworkZip}, nothing is downloaded");
                CheckFramework(plan, options.FrameworkZip, true);
                return options.FrameworkZip;
            }
            // Built here from the pinned version, never taken from the manifest.
            string url = Paths.FrameworkUrl(pin.Version);
            string zip = Path.Combine(temp(), pin.ZipName);
            _log($"ModFramework: downloading {url}");
            try
            {
                Download(InstallStage.DownloadFramework, "ModFramework " + pin.Version, url, zip, pin.Size, downloads, progress, cancel);
            }
            catch (DownloadFailure ex)
            {
                throw DownloadError(ex, "ModFramework", url, plan);
            }
            CheckFramework(plan, zip, false);
            return zip;
        }

        // The zip must be the pinned release, byte for byte, before anything is taken from it.
        private void CheckFramework(FrameworkPlan plan, string zip, bool chosen)
        {
            FrameworkPin pin = plan.Pin;
            long size = new FileInfo(zip).Length;
            string hash = size <= FrameworkPin.MaxSize ? Downloader.Sha256Of(zip) : null;
            if (size == pin.Size && hash == pin.Sha256)
            {
                _log($"ModFramework: SHA-256 OK ({pin.Sha256.Substring(0, 8)}...{pin.Sha256.Substring(59)})");
                return;
            }
            string what = size != pin.Size ? "size" : "SHA-256";
            string detail = $"ModFramework {(chosen ? "zip" : "download")}: {what} mismatch" + Environment.NewLine +
                            $"expected {pin.Sha256}" + Environment.NewLine +
                            $"actual   {hash ?? "(not read, over 20 MB)"}" + Environment.NewLine +
                            $"size     {size} bytes (expected {pin.Size})";
            if (chosen)
            {
                throw new InstallerException(Strings.Key.FwLocalMismatch, detail, pin.Version)
                {
                    HelpKey = Strings.Key.FwNotUsedHelp,
                    Framework = FrameworkHelp.For(plan),
                    LogText = $"ModFramework: {zip} is not the pinned {pin.Version} ({what} mismatch); not used, the game folder was not changed",
                };
            }
            TryDelete(zip);
            throw new InstallerException(what == "size" ? Strings.Key.FwSize : Strings.Key.FwHash, detail, pin.Version)
            {
                HelpKey = Strings.Key.FwDeletedHelp,
                Framework = FrameworkHelp.For(plan),
                LogText = $"ModFramework: {what} mismatch, file deleted; the game folder was not changed",
            };
        }

        // A download that did not give the file, as the failure window explains it.
        // plan: null for BepInEx, whose window offers no release page or zip to choose.
        private static InstallerException DownloadError(DownloadFailure failure, string name, string url, FrameworkPlan plan)
        {
            string version = plan?.Pin.Version;
            Strings.Key key;
            object[] args = { version };
            Strings.Key help = Strings.Key.NothingChanged;
            switch (failure.Problem)
            {
                case DownloadProblem.Offline:
                    key = Strings.Key.NetOffline;
                    help = Strings.Key.DownloadFailedHelp;
                    break;
                case DownloadProblem.Busy:
                    key = Strings.Key.NetBusy;
                    break;
                case DownloadProblem.Limited:
                    key = failure.Minutes > 0 ? Strings.Key.NetLimited : Strings.Key.NetLimitedLater;
                    args = new object[] { failure.Minutes };
                    break;
                case DownloadProblem.Tls:
                    key = Strings.Key.NetTls;
                    break;
                case DownloadProblem.NotFound:
                    key = plan != null ? Strings.Key.FwNotPublished : Strings.Key.DownloadFailed;
                    break;
                case DownloadProblem.Size:
                    key = plan != null ? Strings.Key.FwSize : Strings.Key.BepInExHash;
                    help = Strings.Key.FwDeletedHelp;
                    break;
                default:
                    key = plan != null ? Strings.Key.FwDownloadFailed : Strings.Key.DownloadFailed;
                    break;
            }
            return new InstallerException(key, $"{name} download: {failure.Message}" + Environment.NewLine + $"from {url}", args)
            {
                HelpKey = help,
                Framework = plan == null ? null : FrameworkHelp.For(plan),
                LogText = $"{name}: {failure.Message}; {(failure.Problem == DownloadProblem.Size ? "file deleted; " : "")}the game folder was not changed",
            };
        }

        // The parts the mod needs, unpacked from the checked zip into the staging folder;
        // the rest of the zip is not unpacked. Every part must be there, at least at the
        // mod's minimum. Returns the staging folder.
        private string StageFramework(InstallJournal journal, FrameworkPlan plan, string zip, InstallResult result)
        {
            FrameworkPin pin = plan.Pin;
            string staging = journal.Staging(Path.GetFileNameWithoutExtension(pin.ZipName));
            var wanted = new HashSet<string>(plan.Parts.Where(p => !p.Patcher).Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var others = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            Unpack(zip, staging, name =>
            {
                string[] path = name.Split('/');
                if (path.Any(p => p == ".." || p == "."))
                {
                    return false;
                }
                if (path.Length >= 4 && path[0] == "BepInEx" && path[1] == "plugins" && path[2].StartsWith(Paths.FrameworkPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (wanted.Contains(path[2]))
                    {
                        return true;
                    }
                    others.Add(path[2]);
                    return false;
                }
                // The launcher's files, directly in BepInEx/DragNWash.Installer.
                if (path.Length == 3 && path[0] == "BepInEx" && path[1] == Paths.InstallerFolder)
                {
                    return path[2].Length > 0;
                }
                return name == "BepInEx/patchers/" + Paths.FrameworkPatcher;
            });
            result.NotInstalled = others.Select(FrameworkPart.ShortName).ToList();
            foreach (FrameworkPart part in plan.Parts)
            {
                part.Offered = DllVersion(part.Dll(staging));
                if (part.Offered == null)
                {
                    throw new InstallerException(Strings.Key.FwZipLacks, $"{pin.ZipName} has no {part.Name}", pin.Version, part.Name);
                }
                if (part.Min != null && part.Offered < part.Min)
                {
                    throw new InstallerException(Strings.Key.FwZipTooOld, $"{pin.ZipName} has {part.Name} {part.Offered}, this mod needs {part.Min}",
                        pin.Version, part.Name, ShortVersion(part.Offered), ShortVersion(part.Min));
                }
            }
            return staging;
        }

        // Each part from the staging folder unless the installed one is the same or
        // newer: never older over newer. Within a part's folder only the files the zip
        // has are written; files another mod or the player added there stay.
        private void PutFramework(InstallJournal journal, string game, FrameworkPlan plan, string staging, InstallResult result)
        {
            journal.CreateDirectory(Path.Combine(game, "BepInEx", "plugins"));
            foreach (FrameworkPart part in plan.Parts)
            {
                bool keep = part.Have != null && part.Have >= part.Offered;
                _log(part.Describe(plan.Pin, part.Offered) + (keep ? " -> keep" : part.Have == null ? " -> install" : " -> update"));
                if (keep)
                {
                    bool newer = part.Have > part.Offered;
                    _log($"{part.Name}: kept {ShortVersion(part.Have)} ({(newer ? "newer than " + ShortVersion(part.Offered) : "same")})");
                    result.Kept.Add((part.Short, part.Have, newer));
                    continue;
                }
                if (part.Patcher)
                {
                    journal.CopyFile(part.Dll(staging), Path.Combine(game, "BepInEx", "patchers", Paths.FrameworkPatcher));
                }
                else
                {
                    journal.CopyTree(Path.Combine(staging, "BepInEx", "plugins", part.Name), Path.Combine(game, "BepInEx", "plugins", part.Name));
                    EnableFiles(game, part.Name, journal);
                }
                result.Updated.Add(part.Short);
                _log($"{part.Name}: {ShortVersion(part.Offered)}");
            }
            result.Checked = true;
            result.Offered = plan.Pin.Version;
        }

        internal static Version InstalledFramework(string game)
        {
            return DllVersion(Path.Combine(game, "BepInEx", "plugins", Paths.FrameworkPrefix, Paths.FrameworkPrefix + ".dll"));
        }

        // A DLL the player switched off on the Mods screen was renamed to
        // .dll.disabled. Installing means wanting the mod: drop the switched-off
        // copies and the framework's records of them, including a pending
        // uninstall from the Mods screen.
        private static void EnableFiles(string game, string folder, InstallJournal journal)
        {
            string dir = Path.Combine(game, "BepInEx", "plugins", folder);
            foreach (string off in Directory.GetFiles(dir, "*.dll.disabled", SearchOption.AllDirectories))
            {
                if (File.Exists(off.Substring(0, off.Length - ".disabled".Length)))
                {
                    journal.Delete(off);
                }
            }
            RemoveFromFrameworkLists(game, folder, journal);
        }

        // journal: null when uninstalling, which has nothing to undo.
        private static void RemoveFromFrameworkLists(string game, string folder, InstallJournal journal = null)
        {
            foreach (string name in Paths.FrameworkLists)
            {
                string list = Path.Combine(game, "BepInEx", "config", name);
                if (!File.Exists(list))
                {
                    continue;
                }
                string prefix = folder + "/";
                string[] lines = File.ReadAllLines(list, Encoding.UTF8);
                string[] kept = lines.Where(l =>
                {
                    string rel = l.Split('\t')[0].Trim().Replace('\\', '/');
                    return !(rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || string.Equals(rel, folder, StringComparison.OrdinalIgnoreCase));
                }).ToArray();
                if (kept.Length != lines.Length)
                {
                    journal?.Change(list);
                    File.WriteAllLines(list, kept, new UTF8Encoding(false));
                }
            }
        }

        // ---- uninstall ----

        // keepLauncher: Steam's launch option still starts the launcher (it could not be
        // taken out), so the launcher stays even when the framework goes; without it the
        // game would not start from Steam.
        internal void Uninstall(string game, bool keepData, bool removeBepInEx, bool keepLauncher = false)
        {
            CheckReady(game);
            _log($"Game: {game}");
            string plugins = Path.Combine(game, "BepInEx", "plugins");
            var keptPaths = new List<string>();

            foreach (string plugin in _manifest.Plugins)
            {
                string dir = Path.Combine(plugins, plugin);
                RemoveFromFrameworkLists(game, plugin);
                if (!Directory.Exists(dir))
                {
                    continue;
                }
                string[] keep = keepData
                    ? _manifest.Keep.Where(k => k.StartsWith(plugin + "/", StringComparison.OrdinalIgnoreCase)).Select(k => k.Substring(plugin.Length + 1)).ToArray()
                    : new string[0];
                if (DeleteExcept(dir, keep))
                {
                    keptPaths.Add(dir);
                    _log($"{plugin}: removed, kept your data ({string.Join(", ", keep)})");
                }
                else
                {
                    _log($"{plugin}: removed");
                }
            }

            foreach (string file in _manifest.ConfigFiles)
            {
                if (TryDelete(Path.Combine(game, "BepInEx", "config", file)))
                {
                    _log($"{file}: removed");
                }
            }

            // The staging folder and the backup of the last install go with any mod's
            // uninstall; the launcher goes with the framework.
            bool othersLeft = OtherMods(game).Any();
            string installer = Path.Combine(game, "BepInEx", Paths.InstallerFolder);
            if (Directory.Exists(installer))
            {
                if (!othersLeft && !keepLauncher)
                {
                    InstallJournal.DeleteTree(installer);
                    _log($"BepInEx\\{Paths.InstallerFolder}: removed");
                }
                else
                {
                    InstallJournal.DeleteTree(Path.Combine(installer, "backup"));
                    InstallJournal.DeleteTree(Path.Combine(installer, "staging"));
                    if (File.Exists(Paths.Launcher(game)))
                    {
                        _log($"BepInEx\\{Paths.InstallerFolder}: backup removed; the launcher kept, {(othersLeft ? "other mods use the framework" : "it is still in Steam's launch options")}");
                    }
                    else if (!Directory.EnumerateFileSystemEntries(installer).Any())
                    {
                        Directory.Delete(installer);
                        _log($"BepInEx\\{Paths.InstallerFolder}: removed");
                    }
                }
            }

            if (othersLeft)
            {
                _log($"ModFramework: kept, other mods are installed ({string.Join(", ", OtherMods(game))})");
            }
            else
            {
                RemoveFramework(game, keepData);
            }

            if (removeBepInEx && HasBepInEx(game))
            {
                if (othersLeft || Directory.Exists(Path.Combine(game, "BepInEx", "patchers")) && Directory.EnumerateFileSystemEntries(Path.Combine(game, "BepInEx", "patchers")).Any())
                {
                    _log("BepInEx: kept, other mods or patchers are installed");
                }
                else
                {
                    RemoveBepInEx(game, keepData && (keptPaths.Count > 0 || Directory.Exists(Path.Combine(game, "BepInEx", "SaveHistory"))), keepLauncher);
                }
            }
            _log($"{_manifest.Name}: uninstalled");
        }

        // Whether uninstalling this mod removes the framework too: no other mod is left.
        internal bool RemovesFramework(string game)
        {
            return !OtherMods(game).Any();
        }

        // Plugins that are neither the framework nor this mod.
        private IEnumerable<string> OtherMods(string game)
        {
            string plugins = Path.Combine(game, "BepInEx", "plugins");
            if (!Directory.Exists(plugins))
            {
                return new string[0];
            }
            return Directory.EnumerateFileSystemEntries(plugins)
                .Select(Path.GetFileName)
                .Where(n => !n.StartsWith(Paths.FrameworkPrefix, StringComparison.OrdinalIgnoreCase) && !_manifest.Plugins.Contains(n, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        private void RemoveFramework(string game, bool keepData)
        {
            string plugins = Path.Combine(game, "BepInEx", "plugins");
            bool any = false;
            foreach (string dir in Directory.Exists(plugins) ? Directory.GetDirectories(plugins) : new string[0])
            {
                if (Path.GetFileName(dir).StartsWith(Paths.FrameworkPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(dir, true);
                    any = true;
                }
            }
            any |= TryDelete(Path.Combine(game, "BepInEx", "patchers", Paths.FrameworkPatcher));
            string config = Path.Combine(game, "BepInEx", "config");
            foreach (string file in Directory.Exists(config) ? Directory.GetFiles(config, Paths.FrameworkConfigPrefix + "*") : new string[0])
            {
                TryDelete(file);
            }
            // Snapshots of save files taken by the framework's saves library.
            string history = Path.Combine(game, "BepInEx", "SaveHistory");
            if (Directory.Exists(history) && !keepData)
            {
                Directory.Delete(history, true);
                _log("SaveHistory: removed");
            }
            if (any)
            {
                _log("ModFramework: removed, no other mod needs it");
            }
        }

        private void RemoveBepInEx(string game, bool keepData, bool keepLauncher)
        {
            bool ours = File.Exists(Path.Combine(game, "BepInEx", Paths.Marker)) || File.Exists(Path.Combine(game, "BepInEx", Paths.OldMarker));
            // Doorstop's files at the top of the game folder go only when they start
            // BepInEx: with another loader there, they are that loader's.
            bool config = File.Exists(Path.Combine(game, "doorstop_config.ini"));
            if (config ? DoorstopStartsBepInEx(game, out _) : !Directory.Exists(Path.Combine(game, Paths.DnwModLoaderFolder)))
            {
                foreach (string file in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" })
                {
                    TryDelete(Path.Combine(game, file));
                }
            }
            else
            {
                var (loader, found) = OtherLoader(game);
                _log($"winhttp.dll, doorstop_config.ini: kept, they belong to another mod loader{(loader == null ? "" : " (" + loader + ")")}: {found}");
            }
            string changelog = Path.Combine(game, "changelog.txt");
            if (File.Exists(changelog) && Regex.IsMatch(File.ReadAllText(changelog), "BepInEx|Doorstop|commits since v5"))
            {
                TryDelete(changelog);
            }
            string bep = Path.Combine(game, "BepInEx");
            if (keepData || keepLauncher)
            {
                // The player's data lives inside BepInEx, and so does the launcher; take
                // BepInEx apart around them.
                foreach (string entry in Directory.EnumerateFileSystemEntries(bep).ToList())
                {
                    string name = Path.GetFileName(entry);
                    if (keepData && (name == "plugins" || name == "SaveHistory") || keepLauncher && name == Paths.InstallerFolder)
                    {
                        continue;
                    }
                    DeletePath(entry);
                }
                if (keepData)
                {
                    _log("BepInEx: removed; your data stays in BepInEx/plugins and BepInEx/SaveHistory");
                }
                if (keepLauncher)
                {
                    _log($"BepInEx: removed; the launcher stays in BepInEx/{Paths.InstallerFolder}, it is still in Steam's launch options");
                }
            }
            else
            {
                Directory.Delete(bep, true);
                _log("BepInEx: removed");
            }
            if (!ours)
            {
                _log("BepInEx: note that it was installed before this installer ran");
            }
        }

        // Deletes dir except the relative paths in keep. True when something was kept.
        private static bool DeleteExcept(string dir, string[] keep)
        {
            string[] existing = keep.Where(k => File.Exists(Path.Combine(dir, k)) || Directory.Exists(Path.Combine(dir, k))).ToArray();
            if (existing.Length == 0)
            {
                Directory.Delete(dir, true);
                return false;
            }
            Prune(dir, "", existing);
            return true;
        }

        private static void Prune(string dir, string rel, string[] keep)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(dir).ToList())
            {
                string childRel = (rel.Length == 0 ? "" : rel + "/") + Path.GetFileName(entry);
                if (keep.Any(k => string.Equals(k, childRel, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                bool onPath = keep.Any(k => k.StartsWith(childRel + "/", StringComparison.OrdinalIgnoreCase));
                if (onPath && Directory.Exists(entry))
                {
                    Prune(entry, childRel, keep);
                }
                else
                {
                    DeletePath(entry);
                }
            }
        }

        // ---- what Install and Uninstall will do ----

        // The steps Install takes in this game folder with these choices, in the
        // installer's language, for the window to show before anything runs.
        // launch: what happens to the game's launch options in Steam, decided by the window.
        internal List<string> InstallPlan(string game, IDictionary<string, string> choices, LaunchOptionChange launch = LaunchOptionChange.None)
        {
            var (loader, found) = OtherLoader(game);
            if (found != null)
            {
                // Install stops before it changes anything; that is the whole plan.
                return new List<string> { loader == null ? Strings.Get(Strings.Key.PlanOtherLoader) : Strings.Get(Strings.Key.PlanOtherLoaderNamed, loader) };
            }
            string host = new Uri(Paths.BepInExUrl).Host;
            var steps = new List<string>
            {
                HasBepInEx(game)
                    ? Strings.Get(Strings.Key.PlanHaveBepInEx)
                    : Strings.Get(Strings.Key.PlanDownloadBepInEx, Paths.BepInExVersion, host, Paths.ReleaseAssetsHost),
            };
            FrameworkPlan framework = PlanFramework(game);
            if (framework != null)
            {
                // Schema 2: the framework comes from its own release, and only when something is missing or older.
                FrameworkPin pin = framework.Pin;
                if (framework.Download)
                {
                    int libraries = pin.Libraries().Count;
                    steps.Add(Strings.Get(Strings.Key.PlanDownloadFramework, pin.Version, host, Paths.ReleaseAssetsHost));
                    steps.Add(Strings.Get(libraries == 0 ? Strings.Key.PlanFrameworkBringCore : libraries == 1 ? Strings.Key.PlanFrameworkBringOne : Strings.Key.PlanFrameworkBring,
                        libraries, pin.Version));
                }
                else
                {
                    steps.Add(Strings.Get(Strings.Key.PlanFrameworkSatisfied, ShortVersion(framework.Core)));
                }
                steps.Add(Strings.Get(Strings.Key.PlanBackup));
                steps.Add(Strings.Get(Strings.Key.PlanMod, _manifest.Name));
            }
            else
            {
                steps.Add(Strings.Get(Strings.Key.PlanBackup));
                string core = Path.Combine("BepInEx", "plugins", Paths.FrameworkPrefix, Paths.FrameworkPrefix + ".dll");
                Version offered = DllVersion(Path.Combine(_payload, core));
                Version have = DllVersion(Path.Combine(game, core));
                if (offered != null && have != null && have > offered)
                {
                    steps.Add(Strings.Get(Strings.Key.PlanMod, _manifest.Name));
                    steps.Add(Strings.Get(Strings.Key.PlanKeepNewerFramework, ShortVersion(have)));
                }
                else
                {
                    steps.Add(offered != null
                        ? Strings.Get(Strings.Key.PlanFrameworkAndMod, ShortVersion(offered), _manifest.Name)
                        : Strings.Get(Strings.Key.PlanMod, _manifest.Name));
                }
            }
            foreach (ModChoice choice in _manifest.Choices)
            {
                ModChoiceOption option = choices != null && choices.TryGetValue(choice.Id, out string value) ? choice.Options.FirstOrDefault(o => o.Value == value) : null;
                if (option != null)
                {
                    steps.Add(Strings.Get(Strings.Key.PlanSet, choice.LabelFor(Strings.Current), option.Name ?? option.Value, choice.Config.File));
                }
            }
            if (PutsLauncher(game))
            {
                steps.Add(Strings.Get(Strings.Key.PlanLauncher));
            }
            string launchLine = LaunchOptionPlan(launch);
            if (launchLine != null)
            {
                steps.Add(launchLine);
            }
            steps.Add(Strings.Get(Strings.Key.PlanNothingElse));
            return steps;
        }

        // The window's checklist while Install runs: each line with the stage that does
        // it, in the order Install goes through them.
        internal List<(InstallStage Stage, string Text)> ProgressSteps(string game, IDictionary<string, string> choices, InstallOptions options)
        {
            options = options ?? new InstallOptions();
            string host = new Uri(Paths.BepInExUrl).Host;
            bool bepInEx = !HasBepInEx(game);
            Version framework = InstalledFramework(game);
            string no = Strings.Get(Strings.Key.No);
            var steps = new List<(InstallStage, string)>
            {
                (InstallStage.Check, Strings.Get(Strings.Key.StepChecked, framework == null ? no : ShortVersion(framework),
                    bepInEx ? no : DllVersion(Path.Combine(game, "BepInEx", "core", "BepInEx.dll"))?.ToString())),
            };
            FrameworkPlan plan = PlanFramework(game);
            bool fetch = plan != null && plan.Download && !(options.KeepFramework && plan.MinimumsMet);
            int files = (fetch ? 1 : 0) + (bepInEx ? 1 : 0);
            if (fetch && options.FrameworkZip == null)
            {
                steps.Add((InstallStage.DownloadFramework, Strings.Get(Strings.Key.StepDownload, "ModFramework " + plan.Pin.Version, host)));
            }
            if (bepInEx && options.BepInExZip == null)
            {
                steps.Add((InstallStage.DownloadBepInEx, Strings.Get(Strings.Key.StepDownload, "BepInEx " + Paths.BepInExVersion, host)));
            }
            if (files > 0)
            {
                steps.Add((InstallStage.Verify, Strings.Get(files > 1 ? Strings.Key.StepHashBoth : Strings.Key.StepHash)));
            }
            steps.Add((InstallStage.Backup, Strings.Get(Strings.Key.StepBackup)));
            if (bepInEx)
            {
                steps.Add((InstallStage.Backup, Strings.Get(Strings.Key.StepPutBepInEx, Paths.BepInExVersion)));
            }
            string payloadCore = Path.Combine(_payload, "BepInEx", "plugins", Paths.FrameworkPrefix);
            bool frameworkToo = fetch || plan == null && Directory.Exists(payloadCore);
            steps.Add((InstallStage.Put, Strings.Get(frameworkToo ? Strings.Key.StepPutBoth : Strings.Key.StepPut, _manifest.Name)));
            foreach (ModChoice choice in _manifest.Choices)
            {
                ModChoiceOption option = choices != null && choices.TryGetValue(choice.Id, out string value) ? choice.Options.FirstOrDefault(o => o.Value == value) : null;
                if (option != null)
                {
                    steps.Add((InstallStage.Settings, Strings.Get(Strings.Key.StepSet, choice.LabelFor(Strings.Current), option.Name ?? option.Value)));
                }
            }
            return steps;
        }

        // What Install did, as the window lists it afterwards: each line with whether it
        // was done (true) or something was left as it was (false).
        internal List<(bool Done, string Text)> Summary(InstallResult result)
        {
            var lines = new List<(bool, string)>();
            if (result.BepInEx)
            {
                lines.Add((true, Strings.Get(Strings.Key.DoneBepInEx, Paths.BepInExVersion)));
            }
            if (result.Updated.Count > 0)
            {
                Version from = result.OldFramework, to = result.NewFramework;
                string versions = from != null && to != null && from != to ? $"{ShortVersion(from)} → {ShortVersion(to)}" : to == null ? "" : ShortVersion(to);
                string parts = JoinList(result.Updated.Select(p => p == FrameworkPart.PreloaderName ? Strings.Get(Strings.Key.PartPreloader) : p));
                lines.Add((true, Strings.Get(Strings.Key.DoneFramework, versions, parts) + (result.Checked ? Strings.Get(Strings.Key.DoneChecked) : "")));
            }
            List<string> same = result.Kept.Where(k => !k.Newer).Select(k => k.Part + " " + ShortVersion(k.Have)).ToList();
            List<string> newer = result.Kept.Where(k => k.Newer).Select(k => k.Part + " " + ShortVersion(k.Have)).ToList();
            if (same.Count > 0)
            {
                lines.Add((false, Strings.Get(Strings.Key.DoneKeptSame, JoinList(same))));
            }
            if (newer.Count > 0)
            {
                lines.Add((false, Strings.Get(Strings.Key.DoneKeptNewer, JoinList(newer), result.Offered)));
            }
            if (result.FrameworkSatisfied)
            {
                lines.Add((false, Strings.Get(Strings.Key.DoneFrameworkSatisfied, ShortVersion(result.NewFramework))));
            }
            if (result.FrameworkKept)
            {
                lines.Add((false, Strings.Get(Strings.Key.DoneKeptFramework, ShortVersion(result.NewFramework))));
            }
            if (result.NotInstalled.Count > 0)
            {
                lines.Add((false, Strings.Get(Strings.Key.DoneNotInstalled, JoinList(result.NotInstalled))));
            }
            if (result.Launcher)
            {
                lines.Add((true, Strings.Get(Strings.Key.DoneLauncher)));
            }
            var mod = new List<string>
            {
                _manifest.Name + " " + (result.OldMod != null && result.OldMod != _manifest.Version ? result.OldMod + " → " + _manifest.Version : _manifest.Version),
            };
            foreach (ModChoice choice in _manifest.Choices)
            {
                ModChoiceOption option = result.Choices.TryGetValue(choice.Id, out string value) ? choice.Options.FirstOrDefault(o => o.Value == value) : null;
                if (option != null)
                {
                    mod.Add(Strings.Get(Strings.Key.DoneSetting, choice.LabelFor(Strings.Current), option.Name ?? option.Value));
                }
            }
            lines.Add((true, string.Join(Strings.Get(Strings.Key.ListComma), mod)));
            switch (result.LaunchOption)
            {
                case LaunchOptionOutcome.Added:
                    lines.Add((true, Strings.Get(Strings.Key.DoneLaunchOptionAdded)));
                    break;
                case LaunchOptionOutcome.Removed:
                    lines.Add((true, Strings.Get(Strings.Key.DoneLaunchOptionRemoved)));
                    break;
                case LaunchOptionOutcome.SteamRunning:
                case LaunchOptionOutcome.Skipped:
                    lines.Add((false, Strings.Get(Strings.Key.DoneLaunchOptionSkipped)));
                    break;
                case LaunchOptionOutcome.Failed:
                    lines.Add((false, Strings.Get(Strings.Key.DoneLaunchOptionFailed)));
                    break;
            }
            if (result.OtherMods > 0)
            {
                lines.Add((true, result.OtherMods == 1 ? Strings.Get(Strings.Key.DoneOtherModsOne) : Strings.Get(Strings.Key.DoneOtherMods, result.OtherMods)));
            }
            if (result.Replaced > 0)
            {
                lines.Add((true, Strings.Get(result.Replaced == 1 ? Strings.Key.DoneBackupOne : Strings.Key.DoneBackup, result.Replaced, result.Backup)));
            }
            return lines;
        }

        private static string LaunchOptionPlan(LaunchOptionChange launch)
        {
            switch (launch)
            {
                case LaunchOptionChange.Add:
                    return Strings.Get(Strings.Key.PlanLaunchOptionAdd);
                case LaunchOptionChange.AlreadySet:
                    return Strings.Get(Strings.Key.PlanLaunchOptionHave);
                case LaunchOptionChange.Remove:
                    return Strings.Get(Strings.Key.PlanLaunchOptionRemove);
                default:
                    return null;
            }
        }

        // The same decisions Uninstall makes below, taken from the folder as it is now.
        // launch: Remove when the launcher is in the game's launch options and goes.
        internal List<string> UninstallPlan(string game, bool keepData, bool removeBepInEx, LaunchOptionChange launch = LaunchOptionChange.None)
        {
            var steps = new List<string>();
            string plugins = Path.Combine(game, "BepInEx", "plugins");
            bool keptAny = false;
            foreach (string plugin in _manifest.Plugins)
            {
                string dir = Path.Combine(plugins, plugin);
                if (!Directory.Exists(dir))
                {
                    continue;
                }
                string[] keep = keepData
                    ? _manifest.Keep.Where(k => k.StartsWith(plugin + "/", StringComparison.OrdinalIgnoreCase)).Select(k => k.Substring(plugin.Length + 1))
                        .Where(k => File.Exists(Path.Combine(dir, k)) || Directory.Exists(Path.Combine(dir, k))).ToArray()
                    : new string[0];
                keptAny |= keep.Length > 0;
                steps.Add(keep.Length == 0
                    ? Strings.Get(Strings.Key.PlanRemovePlugin, plugin)
                    : Strings.Get(Strings.Key.PlanRemovePluginKeep, plugin, JoinList(keep.Select(k => k.Replace('/', '\\')))));
            }
            foreach (string file in _manifest.ConfigFiles)
            {
                if (File.Exists(Path.Combine(game, "BepInEx", "config", file)))
                {
                    steps.Add(Strings.Get(Strings.Key.PlanRemoveConfig, file));
                }
            }

            List<string> others = OtherMods(game).ToList();
            bool framework = Directory.Exists(plugins) && Directory.EnumerateDirectories(plugins, Paths.FrameworkPrefix + "*").Any();
            string history = Path.Combine(game, "BepInEx", "SaveHistory");
            if (others.Count > 0)
            {
                if (framework)
                {
                    steps.Add(Strings.Get(Strings.Key.PlanKeepFramework, JoinList(others)));
                }
            }
            else
            {
                if (framework)
                {
                    steps.Add(Strings.Get(Strings.Key.PlanRemoveFramework));
                }
                if (!keepData && Directory.Exists(history))
                {
                    steps.Add(Strings.Get(Strings.Key.PlanRemoveSaveHistory));
                }
            }

            // The launcher belongs to the framework: it leaves the launch options and the
            // game folder with it.
            if (launch == LaunchOptionChange.Remove)
            {
                steps.Add(Strings.Get(Strings.Key.PlanLaunchOptionRemove));
            }
            string installer = Path.Combine(game, "BepInEx", Paths.InstallerFolder);
            if (others.Count == 0 && File.Exists(Paths.Launcher(game)))
            {
                steps.Add(Strings.Get(Strings.Key.PlanRemoveLauncher));
            }
            if (Directory.Exists(Path.Combine(installer, "backup")) || Directory.Exists(Path.Combine(installer, "staging")))
            {
                steps.Add(Strings.Get(Strings.Key.PlanRemoveInstallerFolder));
            }

            if (HasBepInEx(game))
            {
                string patchers = Path.Combine(game, "BepInEx", "patchers");
                bool patchersLeft = Directory.Exists(patchers) && Directory.EnumerateFileSystemEntries(patchers)
                    .Any(e => !string.Equals(Path.GetFileName(e), Paths.FrameworkPatcher, StringComparison.OrdinalIgnoreCase));
                if (!removeBepInEx)
                {
                    steps.Add(Strings.Get(Strings.Key.PlanKeepBepInEx));
                }
                else if (others.Count > 0 || patchersLeft)
                {
                    steps.Add(Strings.Get(Strings.Key.PlanKeepBepInExUsed));
                }
                else
                {
                    steps.Add(Strings.Get(keepData && (keptAny || Directory.Exists(history)) ? Strings.Key.PlanRemoveBepInExKeep : Strings.Key.PlanRemoveBepInEx));
                }
            }
            return steps;
        }

        internal static string ShortVersion(Version v)
        {
            return v == null ? "" : v.Build >= 0 ? v.ToString(3) : v.ToString();
        }

        // "a", "a and b", "a, b and c", in the installer's language.
        private static string JoinList(IEnumerable<string> items)
        {
            List<string> list = items.ToList();
            if (list.Count < 2)
            {
                return string.Concat(list);
            }
            return string.Join(Strings.Get(Strings.Key.ListComma), list.Take(list.Count - 1)) + Strings.Get(Strings.Key.ListAnd) + list[list.Count - 1];
        }

        // ---- helpers ----

        internal static Version DllVersion(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            return Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion ?? "", out Version v) ? v : new Version(0, 0);
        }

        private static void DeletePath(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            else
            {
                TryDelete(path);
            }
        }

        private static bool TryDelete(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return true;
        }
    }

    // BepInEx .cfg files: [Section] headers and "Key = Value" lines.
    internal static class ConfigFile
    {
        internal static string Get(string path, string section, string key)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            string current = null;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    current = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (current == section && eq > 0 && line.Substring(0, eq).Trim() == key)
                {
                    return line.Substring(eq + 1).Trim();
                }
            }
            return null;
        }

        internal static void Set(string path, string section, string key, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).ToList() : new List<string>();
            string current = null;
            int sectionAt = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    current = line.Substring(1, line.Length - 2).Trim();
                    if (current == section)
                    {
                        sectionAt = i;
                    }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (current == section && eq > 0 && line.Substring(0, eq).Trim() == key)
                {
                    lines[i] = $"{key} = {value}";
                    File.WriteAllLines(path, lines, new UTF8Encoding(false));
                    return;
                }
            }
            if (sectionAt >= 0)
            {
                lines.Insert(sectionAt + 1, $"{key} = {value}");
            }
            else
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0)
                {
                    lines.Add("");
                }
                lines.Add($"[{section}]");
                lines.Add("");
                lines.Add($"{key} = {value}");
            }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }
    }

    // How Install may get what it needs. From the command line, --install is the
    // player's consent to download; the window asks first (ConsentDialog).
    internal sealed class InstallOptions
    {
        // A BepInEx or ModFramework zip on disk instead of the download, checked the same way.
        internal string BepInExZip;
        internal string FrameworkZip;

        // Never go online: stop, before anything changes, when something would have to be downloaded.
        internal bool NoDownload;

        // Leave the installed framework as it is when it meets the mod's minimums, and
        // install only the mod (offered when the pinned release cannot be had).
        internal bool KeepFramework;

        internal InstallOptions Copy()
        {
            return (InstallOptions)MemberwiseClone();
        }
    }

    // What Install or Uninstall is to do with the game's launch options in Steam. Steam.cs
    // does it, after Install (or before Uninstall takes the launcher out); the launcher's
    // own updates never touch them.
    internal enum LaunchOptionChange
    {
        None,
        Add,
        AlreadySet,
        Remove,
    }

    // What became of them.
    internal enum LaunchOptionOutcome
    {
        None,
        Added,
        Removed,
        Unchanged,
        SteamRunning,
        Skipped,
        Failed,
    }

    // The steps of an install as the window lists them while it runs, in their order.
    internal enum InstallStage
    {
        Check,
        DownloadFramework,
        DownloadBepInEx,
        Verify,
        Backup,
        Put,
        Settings,
        Done,
    }

    // How far Install has got, for the window to show.
    internal readonly struct InstallProgress
    {
        internal readonly InstallStage Stage;

        // While downloading: what ("ModFramework 1.4.3"), from which host, which of
        // how many downloads, and how many bytes of how many.
        internal readonly string What;
        internal readonly string Host;
        internal readonly int Index;
        internal readonly int Count;
        internal readonly long Done;
        internal readonly long Total;

        private InstallProgress(InstallStage stage, string what, string host, int index, int count, long done, long total)
        {
            Stage = stage;
            What = what;
            Host = host;
            Index = index;
            Count = count;
            Done = done;
            Total = total;
        }

        internal bool Downloading => Stage == InstallStage.DownloadFramework || Stage == InstallStage.DownloadBepInEx;

        // Nothing in the game folder has changed yet, so Install can still be stopped.
        internal bool CanStop => Stage < InstallStage.Backup;

        // Of the download; -1 when it is not known.
        internal int Percent => Total > 0 ? (int)Math.Min(100, Done * 100 / Total) : -1;

        internal static InstallProgress At(InstallStage stage) => new InstallProgress(stage, null, null, 0, 0, 0, 0);

        internal static InstallProgress Downloaded(InstallStage stage, string what, string host, int index, int count, long done, long total) =>
            new InstallProgress(stage, what, host, index, count, done, total);
    }

    // What an install did, for the summary the window shows afterwards.
    internal sealed class InstallResult
    {
        internal string OldMod;
        internal Version OldFramework;
        internal Version NewFramework;

        // Framework parts put in place ("core", "Text", ..., FrameworkPart.PreloaderName),
        // and those kept because the installed one is the same or newer.
        internal readonly List<string> Updated = new List<string>();
        internal readonly List<(string Part, Version Have, bool Newer)> Kept = new List<(string, Version, bool)>();

        // The framework version the parts were offered from: the pinned release, or the one in this mod's zip.
        internal string Offered;

        // Libraries in the pinned release that this mod does not use.
        internal List<string> NotInstalled = new List<string>();

        // The parts came from a zip whose SHA-256 was checked against the manifest.
        internal bool Checked;

        // Nothing had to be fetched; or the installed framework was kept on purpose.
        internal bool FrameworkSatisfied;
        internal bool FrameworkKept;

        internal bool BepInEx;
        internal int OtherMods;

        // The launcher was put in BepInEx/DragNWash.Installer; and what became of the
        // game's launch options in Steam (set by the window or the command line).
        internal bool Launcher;
        internal LaunchOptionOutcome LaunchOption;
        internal int Replaced;
        internal string Backup;
        internal Dictionary<string, string> Choices = new Dictionary<string, string>();
    }

    // Which parts of the pinned framework an install needs, from the game folder
    // before anything is downloaded. A part is fetched when it is missing, when it is
    // older than the mod's minimum, or when the core or the preloader is older than
    // the pinned release. A library's own version in that release is known only once
    // the zip is read; then it is updated when older and kept when the same or newer.
    internal sealed class FrameworkPlan
    {
        internal readonly FrameworkPin Pin;

        // The core first, then the needed libraries, then the preloader.
        internal readonly List<FrameworkPart> Parts;

        internal FrameworkPlan(FrameworkPin pin, List<FrameworkPart> parts)
        {
            Pin = pin;
            Parts = parts;
        }

        // The installed core's version, or null.
        internal Version Core => Parts[0].Have;

        internal bool Download => Parts.Any(p => p.Fetch(Pin));

        // Whether the mod can run on what is installed, without the pinned release.
        internal bool MinimumsMet => Parts.Where(p => !p.Patcher).All(p => p.Have != null && (p.Min == null || p.Have >= p.Min));
    }

    internal sealed class FrameworkPart
    {
        internal const string PreloaderName = "Preloader";

        // The plugin folder (and DLL) name, or the preloader's DLL name without .dll.
        internal readonly string Name;
        internal readonly bool Patcher;
        internal readonly Version Have;

        // The mod's minimum; null when the manifest does not name the part.
        internal readonly Version Min;

        // In the pinned zip, once it is unpacked.
        internal Version Offered;

        internal FrameworkPart(string name, bool patcher, Version have, Version min)
        {
            Name = name;
            Patcher = patcher;
            Have = have;
            Min = min;
        }

        // "core", "Text", "Preloader": how the summary names it.
        internal string Short => Patcher ? PreloaderName : ShortName(Name);

        internal static string ShortName(string folder)
        {
            return string.Equals(folder, Paths.FrameworkPrefix, StringComparison.OrdinalIgnoreCase) ? "core"
                : folder.StartsWith(Paths.FrameworkPrefix + ".", StringComparison.OrdinalIgnoreCase) ? folder.Substring(Paths.FrameworkPrefix.Length + 1)
                : folder;
        }

        // The core and the preloader carry the release's own version; a library has its own.
        private bool Released => Patcher || string.Equals(Name, Paths.FrameworkPrefix, StringComparison.OrdinalIgnoreCase);

        internal bool Fetch(FrameworkPin pin)
        {
            return Have == null || Min != null && Have < Min || Released && Have < pin.Pinned;
        }

        // The DLL under a folder laid out like BepInEx's (the game, or the staging folder).
        internal string Dll(string root)
        {
            return Patcher
                ? Path.Combine(root, "BepInEx", "patchers", Paths.FrameworkPatcher)
                : Path.Combine(root, "BepInEx", "plugins", Name, Name + ".dll");
        }

        // For the log: "DragNWash.ModFramework: have 1.4.0, needs >= 1.2.0, pinned 1.4.3".
        // offered: the version in the pinned zip, null before it is read.
        internal string Describe(FrameworkPin pin, Version offered)
        {
            string pinned = offered != null ? InstallerCore.ShortVersion(offered) : Released ? pin.Version : null;
            return $"{Name}: have {(Have == null ? "none" : InstallerCore.ShortVersion(Have))}" +
                   (Min == null ? "" : ", needs >= " + InstallerCore.ShortVersion(Min)) +
                   (pinned == null ? "" : ", pinned " + pinned);
        }
    }

    // What the failure window offers when getting the pinned ModFramework failed.
    internal sealed class FrameworkHelp
    {
        internal string Version;
        internal string ReleasePage;
        internal string ZipName;

        // The installed core's version when it meets every minimum of the mod, so the
        // mod alone can be installed; null otherwise.
        internal string KeepVersion;

        internal static FrameworkHelp For(FrameworkPlan plan)
        {
            return new FrameworkHelp
            {
                Version = plan.Pin.Version,
                ReleasePage = Paths.FrameworkReleasePage(plan.Pin.Version),
                ZipName = plan.Pin.ZipName,
                KeepVersion = plan.MinimumsMet ? InstallerCore.ShortVersion(plan.Core) : null,
            };
        }
    }

    internal sealed class InstallerException : Exception
    {
        internal readonly Strings.Key Key;
        internal readonly string Detail;

        // Fill the key's {0}, {1}...
        internal readonly object[] Args;

        // What to do about it, when the failure underneath does not say (see ErrorDialog.Describe).
        internal Strings.Key? HelpKey;

        // Set when getting ModFramework failed: the failure window's release page, zip
        // to choose and "install only the mod".
        internal FrameworkHelp Framework;

        // The log's English line for it, when it says more than the key and the detail.
        internal string LogText;

        internal InstallerException(Strings.Key key, string detail = null, params object[] args) : base(key + (detail == null ? "" : ": " + detail))
        {
            Key = key;
            Detail = detail;
            Args = args;
        }

        // For what failed underneath, which the details show in full.
        internal InstallerException(Strings.Key key, Exception cause, params object[] args) : base(key + ": " + cause.Message, cause)
        {
            Key = key;
            Detail = cause.ToString();
            Args = args;
        }

        // In the installer's language, or in English for the log and bug reports.
        internal string Text(bool english = false)
        {
            return english ? Strings.English(Key, Args) : Strings.Get(Key, Args);
        }
    }
}
