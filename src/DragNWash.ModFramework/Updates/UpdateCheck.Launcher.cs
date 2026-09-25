using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;

namespace DragNWash.ModFramework.Updates
{
    // BepInEx/cache/DragNWash.ModFramework/updates.json: what the update check
    // found, for a launcher that starts before the game. The launcher does not
    // go online to look for updates; it reads this file. docs/LAUNCHER.md
    // describes the format.
    //
    // Written after the mods have registered (so installed versions are this
    // session's) and whenever a result arrives or checking is switched on or
    // off. With checking off, the file lists no mods. It is also where the
    // release details are read back from at start: the txt cache in
    // BepInEx/config keeps only the tag, as it did in 1.5.0.
    internal static partial class UpdateCheck
    {
        private const int LauncherFileSchema = 1;
        private const string LauncherFolderName = "DragNWash.ModFramework";
        private const string LauncherFileName = "updates.json";
        // The installer writes this into the mod's first plugin folder
        // (installer/Manifest.cs); a launcher can update a mod that has one.
        private const string InstallManifestName = "mod-install.json";
        // Anything bigger is not a file this framework wrote.
        private const long MaxLauncherFileBytes = 16L * 1024 * 1024;
        private const long MaxManifestBytes = 256 * 1024;

        private static bool _modsRegistered;

        // BepInEx/cache/DragNWash.ModFramework, where the game and the launcher
        // leave files for each other.
        internal static string LauncherCacheFolder => Path.Combine(Paths.CachePath, LauncherFolderName);

        private static string LauncherFilePath => Path.Combine(LauncherCacheFolder, LauncherFileName);

        private static void WriteLauncherFile()
        {
            if (!_modsRegistered)
            {
                return;
            }
            try
            {
                var mods = new List<object>();
                if (Enabled)
                {
                    foreach (ModInfo info in ModFramework.AllInfos().Where(i => IsValidRepository(i.UpdateRepository)).OrderBy(i => i.Guid, StringComparer.Ordinal))
                    {
                        Dictionary<string, object> entry = LauncherEntry(info);
                        if (entry != null)
                        {
                            mods.Add(entry);
                        }
                    }
                }
                var file = new Dictionary<string, object>
                {
                    ["schema"] = LauncherFileSchema,
                    ["written"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ["checking"] = Enabled,
                    ["mods"] = mods,
                };
                string json = Operations.ToJson(file, indented: true);
                Directory.CreateDirectory(Path.GetDirectoryName(LauncherFilePath));
                SafeFile.Write(LauncherFilePath, new UTF8Encoding(false), w => w.Write(json));
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not write {LauncherFileName}: {ex.Message}");
            }
        }

        // Whether the launcher can update this mod itself: the installer put it
        // in a folder of its own under plugins and left mod-install.json there.
        // The same answer as installManifest in updates.json; the Mods screen
        // shows its Update button only then.
        internal static bool CanLauncherUpdate(string guid)
        {
            try
            {
                return guid != null && TryInstalled(guid, out _, out string location) && InstallManifestOf(PluginFolderOf(location)) != null;
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogDebug($"Update check: could not tell whether the launcher can update {guid}: {ex.Message}");
                return false;
            }
        }

        // The version installed this session and the file it was loaded from,
        // or false when the mod is registered but not installed as a plugin or
        // a data mod.
        private static bool TryInstalled(string guid, out string version, out string location)
        {
            if (Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo plugin) && plugin.Metadata != null)
            {
                version = plugin.Metadata.Version.ToString();
                location = plugin.Location;
                return true;
            }
            ModFramework.DataMod data = ModFramework.AllDataMods().FirstOrDefault(d => d.Info.Guid == guid);
            version = data?.Version;
            location = data?.ManifestPath;
            return data != null;
        }

        // The installer's mod-install.json in that folder under plugins, or
        // null when there is none (or no folder).
        private static string InstallManifestOf(string folder)
        {
            if (folder.Length == 0)
            {
                return null;
            }
            string manifest = Path.Combine(Path.Combine(Paths.PluginPath, folder), InstallManifestName);
            return File.Exists(manifest) ? manifest : null;
        }

        // One installed mod, or null when it is registered but not installed as
        // a plugin or a data mod.
        private static Dictionary<string, object> LauncherEntry(ModInfo info)
        {
            if (!TryInstalled(info.Guid, out string version, out string location))
            {
                return null;
            }

            string folder = PluginFolderOf(location);
            string manifest = InstallManifestOf(folder);
            bool hasManifest = manifest != null;
            string manifestName = "";
            string manifestVersion = "";
            if (hasManifest)
            {
                ReadInstallManifest(manifest, out manifestName, out manifestVersion);
            }

            Releases.TryGetValue(info.UpdateRepository, out Release release);
            return new Dictionary<string, object>
            {
                ["guid"] = info.Guid,
                ["name"] = ModFramework.NameOf(info.Guid) ?? info.Guid,
                ["installedVersion"] = version ?? "",
                ["repository"] = info.UpdateRepository,
                ["pluginFolder"] = folder,
                ["installManifest"] = hasManifest,
                ["manifestName"] = manifestName,
                ["manifestVersion"] = manifestVersion,
                ["latest"] = release == null || release.Tag.Length == 0 ? null : LauncherRelease(release),
                ["newer"] = IsNewer(release, version),
            };
        }

        private static Dictionary<string, object> LauncherRelease(Release release)
        {
            return new Dictionary<string, object>
            {
                ["tag"] = release.Tag,
                ["version"] = release.Version == null ? null : Normalize(release.Version).ToString(),
                ["htmlUrl"] = release.HtmlUrl.Length > 0 ? release.HtmlUrl : release.Url,
                ["publishedAt"] = release.PublishedAt,
                ["body"] = release.Body,
                ["bodyTruncated"] = release.BodyTruncated,
                ["assets"] = release.Assets.Select(a => (object)new Dictionary<string, object>
                {
                    ["name"] = a.Name,
                    ["size"] = a.Size,
                    ["url"] = a.Url,
                    ["sha256"] = a.Sha256,
                }).ToList(),
                ["checkedUtc"] = release.CheckedUtc.ToString("o", CultureInfo.InvariantCulture),
            };
        }

        // The folder directly under BepInEx/plugins that holds the file, or ""
        // when the file sits in plugins itself or somewhere else.
        private static string PluginFolderOf(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }
            string plugins = Path.GetFullPath(Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(plugins, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            string rest = full.Substring(plugins.Length);
            int slash = rest.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            return slash > 0 ? rest.Substring(0, slash) : "";
        }

        // Only the name and version, for the launcher to show; a manifest that
        // does not read leaves them empty and is still reported as there.
        private static void ReadInstallManifest(string path, out string name, out string version)
        {
            name = "";
            version = "";
            try
            {
                if (new FileInfo(path).Length > MaxManifestBytes)
                {
                    return;
                }
                var o = Json.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                name = Json.String(o, "name") ?? "";
                version = Json.String(o, "version") ?? "";
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogDebug($"Update check: could not read {path}: {ex.Message}");
            }
        }

        // At start, after the txt cache: the notes and files of each release
        // whose tag is still the one the cache knows. A release not found here
        // is asked for once more (DueRepositories).
        private static void LoadLauncherFile()
        {
            try
            {
                var file = new FileInfo(LauncherFilePath);
                if (!file.Exists || file.Length > MaxLauncherFileBytes)
                {
                    return;
                }
                var root = Json.Parse(File.ReadAllText(file.FullName, Encoding.UTF8)) as Dictionary<string, object>;
                if (Json.Int(root, "schema") != LauncherFileSchema || !(root.TryGetValue("mods", out object mods) && mods is List<object> list))
                {
                    return;
                }
                foreach (Dictionary<string, object> mod in list.OfType<Dictionary<string, object>>())
                {
                    string repository = Json.String(mod, "repository");
                    if (repository == null || !Releases.TryGetValue(repository, out Release release) || release.HasDetails ||
                        !(mod.TryGetValue("latest", out object value) && value is Dictionary<string, object> latest) ||
                        Json.String(latest, "tag") != release.Tag)
                    {
                        continue;
                    }
                    // Checked as when it came from GitHub: the file can be edited.
                    string htmlUrl = Json.String(latest, "htmlUrl");
                    release.HtmlUrl = IsGitHubUrl(htmlUrl) ? htmlUrl : "";
                    release.PublishedAt = Json.String(latest, "publishedAt") ?? "";
                    string body = Json.String(latest, "body") ?? "";
                    release.Body = CutBody(body);
                    release.BodyTruncated = Json.Bool(latest, "bodyTruncated") || release.Body.Length < body.Length;
                    if (latest.TryGetValue("assets", out object assets) && assets is List<object> assetList)
                    {
                        foreach (Dictionary<string, object> a in assetList.OfType<Dictionary<string, object>>().Take(MaxAssets))
                        {
                            string url = Json.String(a, "url");
                            string name = Json.String(a, "name");
                            if (string.IsNullOrEmpty(name) || !IsAssetUrl(release.Repository, url))
                            {
                                continue;
                            }
                            string sha256 = Json.String(a, "sha256") ?? "";
                            release.Assets.Add(new Asset
                            {
                                Name = name,
                                Size = SizeOf(a),
                                Url = url,
                                Sha256 = Sha256Pattern.IsMatch(sha256) ? sha256 : "",
                            });
                        }
                    }
                    release.HasDetails = true;
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not read {LauncherFileName}: {ex.Message}");
            }
        }
    }
}
