using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Networking;

namespace DragNWash.ModFramework.Updates
{
    // Tells players when a mod has a newer release, so they do not have to visit
    // every mod's page to find out.
    //
    // Only mods that name their GitHub repository (ModInfo.UpdateRepository) are
    // checked: guessing a repository from a website link would compare against
    // releases that may not follow the mod's version numbers. Once a day per
    // repository the framework reads GitHub's "latest release", which never is a
    // draft or a pre-release, and compares its tag with the installed version.
    // The request carries nothing about the player or the game. Results are kept
    // in BepInEx/config so a restart does not ask again, and a failed request is
    // only logged and tried again an hour later. Nothing is downloaded or
    // changed: the Mods screen shows the new version and opens the release page.
    internal static class UpdateCheck
    {
        internal sealed class Release
        {
            public string Repository;
            public string Tag;
            public Version Version;
            public DateTime CheckedUtc;

            public string Url => "https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(Tag);
        }

        // Filled by JsonUtility from GitHub's answer.
        [Serializable]
        internal sealed class GitHubRelease
        {
#pragma warning disable CS0649
            public string tag_name;
#pragma warning restore CS0649
        }

        internal const string Section = "Updates";
        internal const string Key = "Check for updates";
        internal const string KeyDescription = "Once a day, ask GitHub whether mods that name their GitHub repository have a newer release. Nothing about you or your game is sent.";

        private const string CacheFileName = ModFramework.Guid + ".updates.txt";
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

        // GitHub's rules for owner and repository names.
        private static readonly Regex RepositoryPattern = new Regex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$");
        private static readonly Regex TagPattern = new Regex(@"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?$");

        private static readonly Dictionary<string, Release> Releases = new Dictionary<string, Release>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> RetryAfter = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static ConfigEntry<bool> _enabled;

        // Goes up whenever a result arrives, so screens know to show it.
        internal static int Revision { get; private set; }

        internal static bool Enabled => _enabled != null && _enabled.Value;

        internal static void Install(ConfigFile config, MonoBehaviour host)
        {
            try
            {
                _enabled = config.Bind(Section, Key, true, KeyDescription);
                _enabled.SettingChanged += (_, __) => Revision++;
                LoadCache();
                host.StartCoroutine(Run());
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Update check is off: {ex.Message}");
            }
        }

        internal static bool IsValidRepository(string repository)
        {
            return repository != null && RepositoryPattern.IsMatch(repository);
        }

        // The newer release of a mod, or null when there is none, the mod does not
        // name a repository, it has not been checked yet or checking is off.
        internal static Release NewerRelease(string guid, string installedVersion)
        {
            if (!Enabled || guid == null)
            {
                return null;
            }
            string repository = ModFramework.GetInfo(guid)?.UpdateRepository;
            if (!IsValidRepository(repository) || !Releases.TryGetValue(repository, out Release release) || release.Version == null)
            {
                return null;
            }
            Version installed = ParseVersion(installedVersion);
            return installed != null && Normalize(release.Version) > Normalize(installed) ? release : null;
        }

        // How many installed mods have a newer release.
        internal static int NewerCount()
        {
            if (!Enabled)
            {
                return 0;
            }
            return Chainloader.PluginInfos.Values.Count(p => NewerRelease(p.Metadata.GUID, p.Metadata.Version.ToString()) != null);
        }

        private static IEnumerator Run()
        {
            // Mods register in Awake or when the framework is ready; let them finish.
            yield return new WaitForSecondsRealtime(5f);
            // Screens drawn before every mod registered (the title screen) show
            // the saved results now, even when nothing is due.
            Revision++;
            while (true)
            {
                if (Enabled)
                {
                    foreach (string repository in DueRepositories())
                    {
                        yield return Check(repository);
                    }
                }
                // Cheap when nothing is due; picks up a game left running for a day
                // and the setting being switched on.
                yield return new WaitForSecondsRealtime(60f);
            }
        }

        private static List<string> DueRepositories()
        {
            DateTime now = DateTime.UtcNow;
            return ModFramework.AllInfos()
                .Select(i => i.UpdateRepository)
                .Where(IsValidRepository)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(r => !(Releases.TryGetValue(r, out Release known) && now - known.CheckedUtc < CheckInterval && known.CheckedUtc <= now))
                .Where(r => !(RetryAfter.TryGetValue(r, out DateTime retry) && now < retry))
                .ToList();
        }

        private static IEnumerator Check(string repository)
        {
            using (UnityWebRequest request = UnityWebRequest.Get("https://api.github.com/repos/" + repository + "/releases/latest"))
            {
                request.timeout = 15;
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "DragNWash.ModFramework/" + ModFramework.Version);
                yield return request.SendWebRequest();

                try
                {
                    Release release = null;
                    if (request.responseCode == 404)
                    {
                        // No release published yet: remember that, like a result.
                        release = new Release { Repository = repository, Tag = "", CheckedUtc = DateTime.UtcNow };
                    }
                    else if (request.result == UnityWebRequest.Result.Success)
                    {
                        string tag = JsonUtility.FromJson<GitHubRelease>(request.downloadHandler.text)?.tag_name ?? "";
                        release = new Release { Repository = repository, Tag = tag, Version = ParseTag(tag), CheckedUtc = DateTime.UtcNow };
                        if (release.Version == null)
                        {
                            ModFramework.Log.LogInfo($"Update check: {repository} tags its latest release \"{tag}\", which is not a version number; not compared.");
                        }
                    }

                    if (release == null)
                    {
                        RetryAfter[repository] = DateTime.UtcNow + RetryInterval;
                        ModFramework.Log.LogInfo($"Update check: could not reach GitHub for {repository} ({request.responseCode} {request.error}); trying again later.");
                    }
                    else
                    {
                        Releases[repository] = release;
                        RetryAfter.Remove(repository);
                        SaveCache();
                        Revision++;
                        if (release.Version != null)
                        {
                            ModFramework.Log.LogInfo($"Update check: latest release of {repository} is {release.Tag}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    RetryAfter[repository] = DateTime.UtcNow + RetryInterval;
                    ModFramework.Log.LogWarning($"Update check for {repository} failed: {ex.Message}");
                }
            }
        }

        internal static Version ParseTag(string tag)
        {
            Match m = TagPattern.Match(tag ?? "");
            if (!m.Success)
            {
                return null;
            }
            try
            {
                return new Version(
                    int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0);
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        private static Version ParseVersion(string version)
        {
            return Version.TryParse(version ?? "", out Version v) ? v : null;
        }

        // 1.2 and 1.2.0 are the same release; the fourth part is not used by tags.
        private static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        // ---- cache: one line per repository, tab separated ----
        // repository, time checked (UTC, round-trip format), tag (empty: no release)

        private static string CachePath => Path.Combine(Paths.ConfigPath, CacheFileName);

        private static void LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath))
                {
                    return;
                }
                foreach (string line in File.ReadAllLines(CachePath, Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 3 || !IsValidRepository(parts[0]) ||
                        !DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime checkedUtc))
                    {
                        continue;
                    }
                    Releases[parts[0]] = new Release { Repository = parts[0], CheckedUtc = checkedUtc.ToUniversalTime(), Tag = parts[2], Version = ParseTag(parts[2]) };
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not read {CacheFileName}: {ex.Message}");
            }
        }

        private static void SaveCache()
        {
            try
            {
                var lines = Releases.Values
                    .OrderBy(r => r.Repository, StringComparer.OrdinalIgnoreCase)
                    .Select(r => r.Repository + "\t" + r.CheckedUtc.ToString("o", CultureInfo.InvariantCulture) + "\t" + r.Tag.Replace("\t", " "));
                Directory.CreateDirectory(Paths.ConfigPath);
                File.WriteAllLines(CachePath, lines, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not write {CacheFileName}: {ex.Message}");
            }
        }
    }
}
