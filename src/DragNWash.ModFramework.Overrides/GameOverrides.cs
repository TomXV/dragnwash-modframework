using System;
using System.Collections.Generic;
using System.Linq;

namespace DragNWash.ModFramework.Overrides
{
    /// <summary>
    /// Overrides: mods with no code. A folder in BepInEx/plugins with a
    /// <c>mod.json</c> and <c>overrides/*.json</c> changes values of objects in
    /// the game (a component's field or property, a material's property) when
    /// they are loaded, and shows on the Mods screen like any mod. The Inspector
    /// writes such folders from its History. Experimental.
    /// See docs/OVERRIDES.md.
    /// </summary>
    public static class GameOverrides
    {
        /// <summary>The library's BepInEx GUID, for <c>[BepInDependency]</c>.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.overrides";

        /// <summary>The library's version.</summary>
        public const string Version = "0.1.0";

        /// <summary>What was read from one overrides mod, and how it went.</summary>
        public sealed class ModReport
        {
            /// <summary>The mod's GUID on the Mods screen (<c>mod.json</c>'s <c>guid</c>, or <c>overrides.&lt;folder&gt;</c>).</summary>
            public string Guid { get; internal set; }
            /// <summary>Its name.</summary>
            public string Name { get; internal set; }
            /// <summary>Its folder.</summary>
            public string Folder { get; internal set; }
            /// <summary>Overrides read from its files.</summary>
            public int Overrides { get; internal set; }
            /// <summary>Overrides written to at least one object so far this session.</summary>
            public int Applied { get; internal set; }
            /// <summary>True when some of them change private members of the game's scripts.</summary>
            public bool UsesPrivate { get; internal set; }
            /// <summary>Files or rows that could not be read.</summary>
            public IReadOnlyList<string> Problems { get; internal set; }
        }

        internal static List<OverrideFiles.Mod> Loaded = new List<OverrideFiles.Mod>();

        /// <summary>Every overrides mod found at startup (or at the last <see cref="Reload"/>).</summary>
        public static IReadOnlyList<ModReport> Mods => Loaded.Select(m => new ModReport
        {
            Guid = m.Guid,
            Name = m.Name,
            Folder = m.Folder,
            Overrides = m.Overrides.Count,
            Applied = m.Overrides.Count(o => OverrideApplier.AppliedCount.ContainsKey(o)),
            UsesPrivate = m.UsesPrivate,
            Problems = m.Problems.ToList(),
        }).ToList();

        /// <summary>
        /// Puts back every value the overrides changed, reads the files again and
        /// applies them: for someone editing an overrides file while the game
        /// runs. Returns a line for the log.
        /// </summary>
        public static string Reload() => OverridesPlugin.Instance != null ? OverridesPlugin.Instance.Reload() : "The Overrides library is not running.";

        /// <summary>A value as an overrides file writes it (reads back exactly).</summary>
        public static string Serialize(object value) => OverrideValues.Serialize(value);

        /// <summary>True for the member types an override can change.</summary>
        public static bool CanHold(Type type) => type != null && OverrideValues.Supports(type);
    }
}
