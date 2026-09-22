using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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

        // Left in BepInEx/ when an installer added BepInEx. The second name is the one
        // Drag'n Wash Localization's own installers used before this installer existed.
        internal const string Marker = ".bepinex-installed-by-dragnwash-installer";
        internal const string OldMarker = ".bepinex-installed-by-dragnwash-localization";

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

        private static IEnumerable<string> SteamRoots()
        {
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

        private void CheckReady(string game)
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

        internal void Install(string game, IDictionary<string, string> choices, string bepInExZip = null)
        {
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

            InstallBepInEx(game, bepInExZip);
            InstallFramework(game);

            string plugins = Path.Combine(game, "BepInEx", "plugins");
            foreach (string plugin in _manifest.Plugins)
            {
                // Copied over what is there: files the player added (their own
                // translations, working files) are never deleted by an update.
                CopyTree(Path.Combine(payloadPlugins, plugin), Path.Combine(plugins, plugin));
                EnableFiles(game, plugin);
                _log($"{plugin}: copied");
            }
            // Lets the Mods screen and later installers know what belongs to the mod.
            File.WriteAllText(Path.Combine(plugins, _manifest.Plugins[0], ModManifest.FileName), _manifest.ToJson(), new UTF8Encoding(false));

            foreach (ModChoice choice in _manifest.Choices)
            {
                if (choices != null && choices.TryGetValue(choice.Id, out string value) && choice.Options.Any(o => o.Value == value))
                {
                    ConfigFile.Set(Path.Combine(game, "BepInEx", "config", choice.Config.File), choice.Config.Section, choice.Config.Key, value);
                    _log($"{choice.Id}: {value}");
                }
            }
            _log($"{_manifest.Name} {_manifest.Version}: installed");
        }

        private void InstallBepInEx(string game, string localZip)
        {
            if (HasBepInEx(game))
            {
                _log("BepInEx: already present");
                return;
            }
            string zip = localZip;
            bool downloaded = false;
            if (zip == null)
            {
                zip = Path.Combine(Path.GetTempPath(), "BepInEx_win_x64_5.4.23.5.zip");
                _log($"BepInEx: downloading {Paths.BepInExUrl}");
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "DragNWash.Installer";
                    client.DownloadFile(Paths.BepInExUrl, zip);
                }
                downloaded = true;
            }
            try
            {
                string hash;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(zip))
                {
                    hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }
                if (hash != Paths.BepInExSha256)
                {
                    throw new InstallerException(Strings.Key.BepInExHash, hash);
                }
                _log("BepInEx: SHA-256 OK, unpacking");
                string root = Path.GetFullPath(game).TrimEnd('\\') + "\\";
                using (ZipArchive archive = ZipFile.OpenRead(zip))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
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
                File.WriteAllText(Path.Combine(game, "BepInEx", Paths.Marker), "BepInEx was added by the Drag'n Wash mod installer.\r\n");
                _log("BepInEx: installed");
            }
            finally
            {
                if (downloaded)
                {
                    TryDelete(zip);
                }
            }
        }

        // The framework and its libraries: another mod may have brought a newer
        // version already, and an older one must never replace it.
        private void InstallFramework(string game)
        {
            string source = Path.Combine(_payload, "BepInEx", "plugins");
            string plugins = Path.Combine(game, "BepInEx", "plugins");
            Directory.CreateDirectory(plugins);
            foreach (string folder in Directory.Exists(source) ? Directory.GetDirectories(source) : new string[0])
            {
                string name = Path.GetFileName(folder);
                if (!name.StartsWith(Paths.FrameworkPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                Version offered = DllVersion(Path.Combine(folder, name + ".dll"));
                Version have = DllVersion(Path.Combine(plugins, name, name + ".dll"));
                if (have != null && offered != null && have > offered)
                {
                    _log($"{name}: kept {have} (newer than {offered})");
                    continue;
                }
                CopyTree(folder, Path.Combine(plugins, name));
                EnableFiles(game, name);
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
                    Directory.CreateDirectory(patchers);
                    File.Copy(patcher, Path.Combine(patchers, Paths.FrameworkPatcher), true);
                }
            }
        }

        // A DLL the player switched off on the Mods screen was renamed to
        // .dll.disabled. Installing means wanting the mod: drop the switched-off
        // copies and the framework's records of them, including a pending
        // uninstall from the Mods screen.
        private static void EnableFiles(string game, string folder)
        {
            string dir = Path.Combine(game, "BepInEx", "plugins", folder);
            foreach (string off in Directory.GetFiles(dir, "*.dll.disabled", SearchOption.AllDirectories))
            {
                if (File.Exists(off.Substring(0, off.Length - ".disabled".Length)))
                {
                    TryDelete(off);
                }
            }
            RemoveFromFrameworkLists(game, folder);
        }

        private static void RemoveFromFrameworkLists(string game, string folder)
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
                    File.WriteAllLines(list, kept, new UTF8Encoding(false));
                }
            }
        }

        // ---- uninstall ----

        internal void Uninstall(string game, bool keepData, bool removeBepInEx)
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

            bool othersLeft = OtherMods(game).Any();
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
                    RemoveBepInEx(game, keepData && (keptPaths.Count > 0 || Directory.Exists(Path.Combine(game, "BepInEx", "SaveHistory"))));
                }
            }
            _log($"{_manifest.Name}: uninstalled");
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

        private void RemoveBepInEx(string game, bool keepData)
        {
            bool ours = File.Exists(Path.Combine(game, "BepInEx", Paths.Marker)) || File.Exists(Path.Combine(game, "BepInEx", Paths.OldMarker));
            foreach (string file in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" })
            {
                TryDelete(Path.Combine(game, file));
            }
            string changelog = Path.Combine(game, "changelog.txt");
            if (File.Exists(changelog) && Regex.IsMatch(File.ReadAllText(changelog), "BepInEx|Doorstop|commits since v5"))
            {
                TryDelete(changelog);
            }
            string bep = Path.Combine(game, "BepInEx");
            if (keepData)
            {
                // The player's data lives inside BepInEx; take BepInEx apart around it.
                foreach (string entry in Directory.EnumerateFileSystemEntries(bep).ToList())
                {
                    string name = Path.GetFileName(entry);
                    if (name == "plugins" || name == "SaveHistory")
                    {
                        continue;
                    }
                    DeletePath(entry);
                }
                _log("BepInEx: removed; your data stays in BepInEx/plugins and BepInEx/SaveHistory");
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
        internal List<string> InstallPlan(string game, IDictionary<string, string> choices)
        {
            var steps = new List<string>
            {
                HasBepInEx(game)
                    ? Strings.Get(Strings.Key.PlanHaveBepInEx)
                    : Strings.Get(Strings.Key.PlanDownloadBepInEx, Paths.BepInExVersion, new Uri(Paths.BepInExUrl).Host),
            };
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
            foreach (ModChoice choice in _manifest.Choices)
            {
                ModChoiceOption option = choices != null && choices.TryGetValue(choice.Id, out string value) ? choice.Options.FirstOrDefault(o => o.Value == value) : null;
                if (option != null)
                {
                    steps.Add(Strings.Get(Strings.Key.PlanSet, choice.LabelFor(Strings.Current), option.Name ?? option.Value, choice.Config.File));
                }
            }
            steps.Add(Strings.Get(Strings.Key.PlanNothingElse));
            return steps;
        }

        // The same decisions Uninstall makes below, taken from the folder as it is now.
        internal List<string> UninstallPlan(string game, bool keepData, bool removeBepInEx)
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

        private static string ShortVersion(Version v)
        {
            return v.Build >= 0 ? v.ToString(3) : v.ToString();
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

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            }
            foreach (string dir in Directory.GetDirectories(from))
            {
                CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
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

    internal sealed class InstallerException : Exception
    {
        internal readonly Strings.Key Key;
        internal readonly string Detail;

        internal InstallerException(Strings.Key key, string detail = null) : base(key + (detail == null ? "" : ": " + detail))
        {
            Key = key;
            Detail = detail;
        }
    }
}
