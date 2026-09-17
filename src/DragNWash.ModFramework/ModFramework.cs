using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;

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
        public const string Version = "1.2.0";

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
