using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// Identity and lifetime of Drag'n Wash ModFramework.
    /// </summary>
    /// <remarks>
    /// A mod that uses the framework declares the dependency so BepInEx loads
    /// the framework first:
    /// <code>
    /// [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    /// </code>
    /// </remarks>
    public static class ModFramework
    {
        /// <summary>BepInEx GUID to depend on.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework";

        /// <summary>Plugin name shown by BepInEx.</summary>
        public const string Name = "DragNWash.ModFramework";

        /// <summary>
        /// Framework version. Semantic versioning: while it is 0.x the public API
        /// may still change between minor versions; from 1.0 on, breaking changes
        /// only come with a new major version. Keep in sync with the csproj.
        /// </summary>
        public const string Version = "1.6.0";

        private static ManualLogSource _log;
        private static readonly Dictionary<string, ModInfo> Infos = new Dictionary<string, ModInfo>(StringComparer.Ordinal);

        /// <summary>True once the framework has finished starting.</summary>
        public static bool IsReady { get; private set; }

        /// <summary>
        /// Raised once when the framework has finished starting. A handler added
        /// after that point is called immediately.
        /// </summary>
        public static event Action Ready
        {
            add
            {
                if (IsReady)
                {
                    value?.Invoke();
                }
                else
                {
                    _ready += value;
                }
            }
            remove { _ready -= value; }
        }

        private static Action _ready;

        /// <summary>
        /// The name the Mods screen shows for a mod: its registered display name,
        /// else its BepInEx name, else <paramref name="guid"/> itself. Takes a
        /// Harmony ID too, since mods usually patch under their GUID. Since 1.4.0.
        /// </summary>
        public static string NameOf(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return guid;
            lock (Infos)
            {
                if (Infos.TryGetValue(guid, out ModInfo info) && !string.IsNullOrEmpty(info.DisplayName)) return info.DisplayName;
            }
            return BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(guid, out BepInEx.PluginInfo plugin) && plugin.Metadata != null ? plugin.Metadata.Name : guid;
        }

        /// <summary>
        /// Who else answers a key. Every loaded plugin keeps its shortcuts in its
        /// own settings, so two mods can sit on one key without either of them
        /// knowing; this says which mods have a setting for
        /// <paramref name="key"/>, as <c>Drag'n Wash Localization: [Debug]
        /// DumpDialogueKey</c>, leaving out <paramref name="exceptGuid"/> (your
        /// own mod). It is a report and nothing more: a player may well want one
        /// key to do two things, and only they can say. Since 1.4.2.
        /// </summary>
        public static IReadOnlyList<string> WhoElseUses(KeyCode key, string exceptGuid = null)
        {
            if (key == KeyCode.None)
            {
                return new List<string>();
            }
            return Mods.KeyBindings.All()
                .Where(b => b.Shortcut.MainKey == key && !string.Equals(b.Guid, exceptGuid, StringComparison.Ordinal))
                .Select(b => $"{b.Mod}: {b.Setting}")
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// The line the Mods screen shows under a shortcut setting whose key
        /// another setting also has, for a mod that lets the player change a key
        /// in a window of its own: <c>C is also used by Screenshot key (Photo
        /// Mode). Both will answer it.</c> Each other setting is named as on its
        /// own settings page, with its mod's name after it when that is another
        /// mod; settings of the same mod count too. Null when no other setting
        /// in any loaded plugin has the main key of <paramref name="shortcut"/>,
        /// or when it is not a <c>KeyboardShortcut</c> setting. Like
        /// <see cref="WhoElseUses"/>, a report and nothing more. Since 1.5.0.
        /// </summary>
        public static string SharedKeyNote(ConfigEntryBase shortcut)
        {
            try
            {
                return Mods.KeyBindings.Note(shortcut);
            }
            catch (Exception)
            {
                // A plugin's config that cannot be read is no reason to fail a draw.
                return null;
            }
        }

        /// <summary>
        /// Tells the Mods screen more about a mod than BepInEx knows: description,
        /// authors and website. Registering again with the same GUID replaces the
        /// earlier information.
        /// </summary>
        public static void Register(ModInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.Guid))
            {
                throw new ArgumentException("ModInfo.Guid is required.", nameof(info));
            }
            if (info.Icon == null && !string.IsNullOrEmpty(info.IconPath))
            {
                info.Icon = Mods.IconLoader.Load(info.IconPath);
            }
            lock (Infos)
            {
                Infos[info.Guid] = info;
            }
        }

        internal sealed class DataMod
        {
            public ModInfo Info;
            public string Version;
            public string ManifestPath;
        }

        private static readonly List<DataMod> DataMods = new List<DataMod>();

        /// <summary>
        /// Lists a mod that has no DLL of its own on the Mods screen: a folder in
        /// BepInEx/plugins whose files another library reads (the Overrides
        /// library's <c>mod.json</c> and <c>overrides/</c>). It shows like a
        /// plugin, with the details in <paramref name="info"/>, and switching it
        /// off renames <paramref name="manifestPath"/> (which must be named
        /// <c>mod.json</c> and lie in a folder under BepInEx/plugins) to
        /// <c>mod.json.disabled</c> at the next launch, so the library that read
        /// it no longer finds it. Experimental. Since 1.4.0.
        /// </summary>
        public static void RegisterDataMod(ModInfo info, string version, string manifestPath)
        {
            if (info == null || string.IsNullOrEmpty(info.Guid) || string.IsNullOrEmpty(manifestPath))
            {
                throw new ArgumentException("RegisterDataMod needs ModInfo.Guid and the manifest path.");
            }
            if (!string.Equals(System.IO.Path.GetFileName(manifestPath), "mod.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The manifest of a data mod is a file named mod.json.", nameof(manifestPath));
            }
            Register(info);
            lock (DataMods)
            {
                DataMods.RemoveAll(d => d.Info.Guid == info.Guid);
                DataMods.Add(new DataMod { Info = info, Version = version ?? "", ManifestPath = manifestPath });
            }
        }

        internal static List<DataMod> AllDataMods()
        {
            lock (DataMods)
            {
                return DataMods.ToList();
            }
        }

        private static readonly List<ModsScreenPage> Pages = new List<ModsScreenPage>();

        /// <summary>Adds a page for a mod on the Mods screen, opened with a button in its details.</summary>
        public static void AddModsPage(ModsScreenPage page)
        {
            if (page == null || string.IsNullOrEmpty(page.Guid) || string.IsNullOrEmpty(page.Title) || page.Build == null)
            {
                throw new ArgumentException("ModsScreenPage needs Guid, Title and Build.", nameof(page));
            }
            lock (Pages)
            {
                Pages.Add(page);
            }
        }

        internal static List<ModsScreenPage> PagesFor(string guid)
        {
            lock (Pages)
            {
                return Pages.Where(p => p.Guid == guid).ToList();
            }
        }

        // On a mod's reload: its Mods-screen pages and Ready handlers go. Its
        // ModInfo stays; the new build's Register replaces it.
        internal static void Forget(string guid, Assembly assembly)
        {
            lock (Pages)
            {
                Pages.RemoveAll(p => p.Guid == guid);
            }
            _ready = (Action)ModReload.Prune(_ready, assembly);
        }

        internal static ModInfo GetInfo(string guid)
        {
            lock (Infos)
            {
                return guid != null && Infos.TryGetValue(guid, out ModInfo info) ? info : null;
            }
        }

        internal static List<ModInfo> AllInfos()
        {
            lock (Infos)
            {
                return Infos.Values.ToList();
            }
        }

        internal static ManualLogSource Log => _log;

        internal static void Initialize(ManualLogSource log)
        {
            if (IsReady)
            {
                return;
            }

            _log = log;
            _log.LogInfo($"{Name} {Version} on Unity {GameInfo.UnityVersion} / {GameInfo.GraphicsApi}");

            Register(new ModInfo
            {
                Guid = Guid,
                DisplayName = "Drag'n Wash ModFramework",
                Description = "Shared tools for Drag'n Wash mods, including this Mods screen.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                UpdateRepository = "TomXV/dragnwash-modframework",
                Network = new[]
                {
                    new NetworkUse
                    {
                        Host = "api.github.com",
                        Purpose = "Once a day, asks GitHub whether mods that name their GitHub repository have a newer release.",
                        Sends = "The names of those repositories. Nothing about you or your game.",
                        TurnOff = "Mods → Drag'n Wash ModFramework → Settings → Check for updates",
                    },
                },
                IconPath = Path.Combine(Path.GetDirectoryName(typeof(ModFramework).Assembly.Location) ?? "", "icon.png"),
            });

            IsReady = true;
            Action handlers = _ready;
            _ready = null;
            if (handlers == null)
            {
                return;
            }

            // One failing subscriber must not stop the others or the framework.
            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    _log.LogError($"A Ready handler threw: {ex}");
                }
            }
        }
    }
}
