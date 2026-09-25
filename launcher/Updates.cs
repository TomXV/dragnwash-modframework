using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using DragNWash.Installer;

namespace DragNWash.Launcher
{
    // BepInEx/cache/DragNWash.ModFramework/updates.json, written by the game's update
    // check (docs/LAUNCHER.md). Read only. Every field may be missing: a file from an
    // older or newer framework still reads, and what is missing counts as "nothing".
    [DataContract]
    internal sealed class UpdatesFile
    {
        [DataMember(Name = "schema")] public int Schema;
        [DataMember(Name = "written")] public string Written;
        [DataMember(Name = "checking")] public bool? Checking;
        [DataMember(Name = "mods")] public ModUpdate[] Mods;
    }

    [DataContract]
    internal sealed class ModUpdate
    {
        [DataMember(Name = "guid")] public string Guid;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "installedVersion")] public string InstalledVersion;
        [DataMember(Name = "repository")] public string Repository;
        [DataMember(Name = "pluginFolder")] public string PluginFolder;
        [DataMember(Name = "installManifest")] public bool InstallManifest;
        [DataMember(Name = "latest")] public Release Latest;
        [DataMember(Name = "newer")] public bool Newer;

        // Worked out by the launcher, not read from the file.
        internal ReleaseAsset Zip;
        internal string ShownName => string.IsNullOrWhiteSpace(Name) ? Guid : Name;
        internal string ShownVersion => Latest?.Version ?? Latest?.Tag;
    }

    [DataContract]
    internal sealed class Release
    {
        [DataMember(Name = "tag")] public string Tag;
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "htmlUrl")] public string HtmlUrl;
        [DataMember(Name = "publishedAt")] public string PublishedAt;
        [DataMember(Name = "body")] public string Body;
        [DataMember(Name = "bodyTruncated")] public bool BodyTruncated;
        [DataMember(Name = "assets")] public ReleaseAsset[] Assets;
        [DataMember(Name = "checkedUtc")] public string CheckedUtc;
    }

    [DataContract]
    internal sealed class ReleaseAsset
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "size")] public long Size;
        [DataMember(Name = "url")] public string Url;
        [DataMember(Name = "sha256")] public string Sha256;

        // Built from the repository, the tag and the name; the url in the file is only compared with it.
        internal string Download;
    }

    // BepInEx/cache/DragNWash.ModFramework/update-request.json, written by the game when
    // the player pressed Update in the Mods screen and agreed to quit.
    [DataContract]
    internal sealed class UpdateRequest
    {
        [DataMember(Name = "schema")] public int Schema;
        [DataMember(Name = "requestedUtc")] public string RequestedUtc;
        [DataMember(Name = "gamePid")] public int GamePid;
        [DataMember(Name = "mods")] public string[] Mods;
    }

    // BepInEx/cache/DragNWash.ModFramework/launcher-state.json: the launcher's own memory.
    [DataContract]
    internal sealed class LauncherState
    {
        [DataMember(Name = "schema")] public int Schema = 1;

        // guid -> the tag the player said not to be told about ("Skip this version").
        [DataMember(Name = "skipped")] public Dictionary<string, string> Skipped = new Dictionary<string, string>();
    }

    // The files the launcher reads and writes in the game folder, and what it makes of them.
    internal sealed class GameFiles
    {
        internal const string FrameworkGuid = "com.tomxv.dragnwash.modframework";
        internal const string LocalizationGuid = "com.tomxv.dragnwash.localization";

        // Nothing bigger is downloaded: a mod's zip is a few MB.
        private const long MaxZip = 200L * 1024 * 1024;

        private static readonly Regex Repository = new Regex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$");
        private static readonly Regex Tag = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,99}$");
        private static readonly Regex ZipName = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._+ -]{0,150}\.zip$", RegexOptions.IgnoreCase);
        private static readonly Regex Sha256 = new Regex("^[0-9a-f]{64}$");

        internal readonly string Game;

        internal GameFiles(string game)
        {
            Game = game;
        }

        internal string Cache => Path.Combine(Game, "BepInEx", "cache", "DragNWash.ModFramework");
        internal string UpdatesPath => Path.Combine(Cache, "updates.json");
        internal string RequestPath => Path.Combine(Cache, "update-request.json");
        internal string StatePath => Path.Combine(Cache, "launcher-state.json");
        internal string InstallerFolder => Path.Combine(Game, "BepInEx", Paths.InstallerFolder);
        private string Config(string guid) => Path.Combine(Game, "BepInEx", "config", guid + ".cfg");

        // ---- updates.json ----

        internal UpdatesFile ReadUpdates()
        {
            string path = UpdatesPath;
            if (!File.Exists(path))
            {
                Log.Line("updates.json: not there yet (the game writes it after its update check)");
                return null;
            }
            try
            {
                var file = Read<UpdatesFile>(path);
                if (file == null || file.Schema != 1)
                {
                    Log.Line($"updates.json: schema {file?.Schema.ToString(CultureInfo.InvariantCulture) ?? "?"}, this launcher reads schema 1; ignored");
                    return null;
                }
                file.Mods = (file.Mods ?? new ModUpdate[0]).Where(m => m != null && !string.IsNullOrWhiteSpace(m.Guid)).ToArray();
                Log.Line($"updates.json: written {file.Written}, {file.Mods.Length} mods, checking {(file.Checking == false ? "off" : "on")}");
                return file;
            }
            catch (Exception ex)
            {
                Log.Line("updates.json: could not be read: " + ex.Message);
                return null;
            }
        }

        // The updates to tell the player about before the game starts: newer, not skipped,
        // and not installed some other way since the game wrote the file.
        internal List<ModUpdate> Pending(UpdatesFile updates, LauncherState state)
        {
            var list = new List<ModUpdate>();
            if (updates == null || updates.Checking == false)
            {
                return list;
            }
            foreach (ModUpdate mod in updates.Mods)
            {
                if (!mod.Newer || mod.Latest == null || string.IsNullOrWhiteSpace(mod.Latest.Tag))
                {
                    continue;
                }
                if (state.Skipped.TryGetValue(mod.Guid, out string skipped) && skipped == mod.Latest.Tag)
                {
                    Log.Line($"{mod.ShownName} {mod.Latest.Tag}: skipped by the player");
                    continue;
                }
                if (AlreadyInstalled(mod))
                {
                    continue;
                }
                Check(mod);
                list.Add(mod);
            }
            return list;
        }

        // The mods the game asked for (update-request.json or --mods), whatever was skipped.
        internal List<ModUpdate> Requested(UpdatesFile updates, IEnumerable<string> guids)
        {
            var list = new List<ModUpdate>();
            if (updates == null)
            {
                return list;
            }
            foreach (string guid in guids.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ModUpdate mod = updates.Mods.FirstOrDefault(m => string.Equals(m.Guid, guid, StringComparison.OrdinalIgnoreCase));
                if (mod == null || mod.Latest == null || string.IsNullOrWhiteSpace(mod.Latest.Tag))
                {
                    Log.Line($"{guid}: asked for, but updates.json has no release for it");
                    continue;
                }
                if (AlreadyInstalled(mod))
                {
                    continue;
                }
                Check(mod);
                if (mod.Zip == null)
                {
                    Log.Line($"{mod.ShownName}: asked for, but it can't be installed from here; left as it is");
                    continue;
                }
                list.Add(mod);
            }
            return list;
        }

        // A mod the installer put in has its mod-install.json in its folder: when that says
        // the release's version already, the player installed it since the game looked.
        private bool AlreadyInstalled(ModUpdate mod)
        {
            if (string.IsNullOrWhiteSpace(mod.PluginFolder) || !SafeFolder(mod.PluginFolder))
            {
                return false;
            }
            string copy = Path.Combine(Game, "BepInEx", "plugins", mod.PluginFolder, ModManifest.FileName);
            Version have = null;
            try
            {
                have = File.Exists(copy) ? Numeric(ModManifest.Load(copy).Version) : null;
            }
            catch (Exception)
            {
            }
            Version offered = Numeric(mod.Latest.Version ?? mod.Latest.Tag);
            if (have != null && offered != null && have >= offered)
            {
                Log.Line($"{mod.ShownName}: {have} is installed already, not older than {mod.Latest.Tag}");
                return true;
            }
            return false;
        }

        // Whether the launcher can install the mod itself, and which zip: the mod was put in
        // by the installer (mod-install.json in its folder), and its release has one zip that
        // is plainly the mod's. The address is built from the repository and the tag; the
        // one in the file must say the same, or the file was changed by someone else.
        private void Check(ModUpdate mod)
        {
            mod.Zip = null;
            string why = null;
            Release latest = mod.Latest;
            if (!mod.InstallManifest || string.IsNullOrWhiteSpace(mod.PluginFolder) || !SafeFolder(mod.PluginFolder))
            {
                why = "not installed by the mod installer (no mod-install.json)";
            }
            else if (mod.Repository == null || !Repository.IsMatch(mod.Repository))
            {
                why = $"repository \"{mod.Repository}\" is not owner/name";
            }
            else if (!Tag.IsMatch(latest.Tag))
            {
                why = $"tag \"{latest.Tag}\" is not one the launcher accepts";
            }
            else
            {
                var zips = new List<ReleaseAsset>();
                foreach (ReleaseAsset asset in latest.Assets ?? new ReleaseAsset[0])
                {
                    if (asset == null || asset.Name == null || !ZipName.IsMatch(asset.Name))
                    {
                        continue;
                    }
                    string url = $"https://github.com/{mod.Repository}/releases/download/{Uri.EscapeDataString(latest.Tag)}/{Uri.EscapeDataString(asset.Name)}";
                    if (!string.Equals(asset.Url, url, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Line($"{mod.ShownName}: {asset.Name} has the address {asset.Url}, not {url}; not used");
                        continue;
                    }
                    if (asset.Size <= 0 || asset.Size > MaxZip)
                    {
                        Log.Line($"{mod.ShownName}: {asset.Name} is {asset.Size} bytes; not used");
                        continue;
                    }
                    asset.Sha256 = (asset.Sha256 ?? "").Trim().ToLowerInvariant();
                    if (asset.Sha256.Length > 0 && !Sha256.IsMatch(asset.Sha256))
                    {
                        Log.Line($"{mod.ShownName}: {asset.Name} has a SHA-256 that isn't 64 hex digits; not used");
                        continue;
                    }
                    asset.Download = url;
                    zips.Add(asset);
                }
                // One zip is the mod's. With several, the one named with the version
                // ("DragNWash.ModFramework-1.5.1.zip"), as Install.exe names the framework's.
                string version = latest.Version ?? latest.Tag.TrimStart('v', 'V');
                List<ReleaseAsset> named = zips.Where(a => a.Name.IndexOf(version, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (zips.Count == 1)
                {
                    mod.Zip = zips[0];
                }
                else if (named.Count == 1)
                {
                    mod.Zip = named[0];
                }
                else
                {
                    why = zips.Count == 0 ? "the release has no zip the launcher can use" : $"the release has {zips.Count} zips and none is plainly the mod's";
                }
            }
            Log.Line(mod.Zip != null
                ? $"{mod.ShownName}: {mod.InstalledVersion} -> {latest.Tag}, {mod.Zip.Name}, {mod.Zip.Size} bytes, SHA-256 {(mod.Zip.Sha256.Length > 0 ? mod.Zip.Sha256.Substring(0, 8) + "..." : "not given")}"
                : $"{mod.ShownName}: {mod.InstalledVersion} -> {latest.Tag}, release page only: {why}");
        }

        private static bool SafeFolder(string name) => name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name != "." && name != "..";

        // The release page, only on github.com under the mod's own repository; else the one built from the tag.
        internal static string ReleasePage(ModUpdate mod)
        {
            if (mod.Repository == null || !Repository.IsMatch(mod.Repository))
            {
                return null;
            }
            string home = $"https://github.com/{mod.Repository}/";
            string page = mod.Latest?.HtmlUrl;
            if (page != null && page.StartsWith(home, StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(page, UriKind.Absolute, out Uri uri) && uri.Host == "github.com")
            {
                return uri.AbsoluteUri;
            }
            return mod.Latest != null && Tag.IsMatch(mod.Latest.Tag ?? "") ? home + "releases/tag/" + Uri.EscapeDataString(mod.Latest.Tag) : home + "releases";
        }

        // "v1.5.1", "1.5.1-beta" -> 1.5.1; null when it isn't a version number.
        internal static Version Numeric(string text)
        {
            Match m = Regex.Match(text ?? "", @"^[vV]?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?");
            if (!m.Success)
            {
                return null;
            }
            int Part(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
            return new Version(Part(1), Part(2), Part(3), Part(4));
        }

        // ---- update-request.json ----

        // The request the game left, when it was written after since (the launch it is for).
        // Deleted once read, stale or not, so it is never handled twice.
        internal UpdateRequest TakeRequest(DateTime since)
        {
            string path = RequestPath;
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                DateTime written = File.GetLastWriteTimeUtc(path);
                var request = Read<UpdateRequest>(path);
                if (written < since)
                {
                    Log.Line($"update-request.json: written {written:u}, before this launch; ignored");
                    return null;
                }
                if (request == null || request.Schema != 1)
                {
                    Log.Line("update-request.json: not schema 1; ignored");
                    return null;
                }
                request.Mods = (request.Mods ?? new string[0]).Where(g => !string.IsNullOrWhiteSpace(g)).ToArray();
                Log.Line($"update-request.json: {request.Mods.Length} mods asked for at {request.RequestedUtc} by pid {request.GamePid}");
                return request;
            }
            catch (Exception ex)
            {
                Log.Line("update-request.json: could not be read: " + ex.Message);
                return null;
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    Log.Line("update-request.json: could not be deleted: " + ex.Message);
                }
            }
        }

        // ---- launcher-state.json ----

        internal LauncherState ReadState()
        {
            try
            {
                if (File.Exists(StatePath))
                {
                    var state = Read<LauncherState>(StatePath);
                    if (state != null)
                    {
                        state.Skipped = state.Skipped ?? new Dictionary<string, string>();
                        return state;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Line("launcher-state.json: could not be read, starting afresh: " + ex.Message);
            }
            return new LauncherState();
        }

        internal void WriteState(LauncherState state)
        {
            try
            {
                Directory.CreateDirectory(Cache);
                string temp = StatePath + ".tmp";
                var serializer = new DataContractJsonSerializer(typeof(LauncherState), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                using (var stream = File.Create(temp))
                {
                    serializer.WriteObject(stream, state);
                }
                if (File.Exists(StatePath))
                {
                    File.Replace(temp, StatePath, null);
                }
                else
                {
                    File.Move(temp, StatePath);
                }
            }
            catch (Exception ex)
            {
                Log.Line("launcher-state.json: could not be written: " + ex.Message);
            }
        }

        // ---- settings ----

        // [Launcher] in the framework's config file, as the game registers it; the defaults when absent.
        internal (bool Typewriter, bool UnderText) Settings()
        {
            string cfg = Config(FrameworkGuid);
            string lettering = null, bar = null;
            try
            {
                lettering = ConfigFile.Get(cfg, "Launcher", "Logo lettering");
                bar = ConfigFile.Get(cfg, "Launcher", "Progress bar");
            }
            catch (Exception ex)
            {
                Log.Line("Settings: the config file could not be read, using the defaults: " + ex.Message);
            }
            bool typewriter = string.Equals(lettering, "Typewriter", StringComparison.OrdinalIgnoreCase);
            bool underText = string.Equals(bar, "Under text", StringComparison.OrdinalIgnoreCase);
            Log.Line($"Settings: Logo lettering = {(typewriter ? "Typewriter" : "Handwriting")}, Progress bar = {(underText ? "Under text" : "Bottom edge")}");
            return (typewriter, underText);
        }

        // Japanese when Localization is set to Japanese; without Localization's setting, Windows' language.
        internal string Language()
        {
            try
            {
                string locale = ConfigFile.Get(Config(LocalizationGuid), "General", "TargetLocale");
                if (!string.IsNullOrWhiteSpace(locale))
                {
                    return locale.Trim().StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
                }
            }
            catch (Exception)
            {
            }
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en";
        }

        private static T Read<T>(string path) where T : class
        {
            var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            // Read whole and without a BOM: the serializer wants UTF-8 bytes it can take as they are.
            string text = File.ReadAllText(path, Encoding.UTF8);
            using (var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(text)))
            {
                return serializer.ReadObject(stream) as T;
            }
        }
    }
}
