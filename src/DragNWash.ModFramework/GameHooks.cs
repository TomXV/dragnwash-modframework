using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// Checks that the game code a mod or library patches still exists on the
    /// running game build, and remembers what is missing so the Mods screen can
    /// say so. Check before patching, and skip the feature when a check fails.
    /// </summary>
    public static class GameHooks
    {
        private static readonly Dictionary<string, List<string>> Missing = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>
        /// True when <paramref name="typeName"/> exists in the game and, if given, has a
        /// method called <paramref name="methodName"/>. A failure is logged and shown on
        /// the Mods screen under <paramref name="ownerGuid"/> with <paramref name="feature"/>.
        /// </summary>
        /// <param name="ownerGuid">BepInEx GUID of the mod that needs it.</param>
        /// <param name="feature">What stops working, for players, e.g. "Dialogue events".</param>
        /// <param name="typeName">
        /// Full type name, e.g. "Yarn.Unity.LinePresenter", or Harmony's
        /// "Type:Method" form, e.g. "Yarn.Unity.LinePresenter:RunLine", which
        /// the Inspector's Code view copies and AccessTools takes as it is.
        /// </param>
        /// <param name="methodName">Method name, or null to check the type only.</param>
        public static bool Require(string ownerGuid, string feature, string typeName, string methodName = null)
        {
            if (methodName == null && typeName != null)
            {
                // "Type:Method" pasted whole: a type name never holds a colon
                // (a nested type is written with a +), so the split is safe.
                int colon = typeName.LastIndexOf(':');
                if (colon > 0 && colon < typeName.Length - 1)
                {
                    methodName = typeName.Substring(colon + 1);
                    typeName = typeName.Substring(0, colon);
                }
            }
            Type type = AccessTools.TypeByName(typeName);
            bool ok = type != null && (methodName == null || AccessTools.Method(type, methodName) != null);
            if (!ok)
            {
                Report(ownerGuid, feature, methodName == null ? typeName : typeName + "." + methodName);
            }
            return ok;
        }

        /// <summary>
        /// Records a check the mod made itself. Returns <paramref name="passed"/>.
        /// </summary>
        public static bool Require(string ownerGuid, string feature, bool passed, string detail)
        {
            if (!passed)
            {
                Report(ownerGuid, feature, detail);
            }
            return passed;
        }

        /// <summary>
        /// Marks a feature unavailable for a reason that is not a missing game
        /// member: switched off after a crash, refused on this renderer, and so
        /// on. Shown on the Mods screen like a failed <see cref="Require(string, string, bool, string)"/>.
        /// </summary>
        public static void Unavailable(string ownerGuid, string feature, string reason)
        {
            Remember(ownerGuid, feature);
            ModFramework.Log.LogWarning($"{ownerGuid ?? "A mod"}: \"{feature}\" is unavailable: {reason}.");
        }

        // On a mod's reload: its records go, so the new build checks afresh.
        internal static void Forget(string ownerGuid)
        {
            lock (Missing)
            {
                if (ownerGuid != null)
                {
                    Missing.Remove(ownerGuid);
                }
            }
        }

        /// <summary>Features of <paramref name="ownerGuid"/> that failed a check on this game build.</summary>
        public static IReadOnlyList<string> UnavailableFeatures(string ownerGuid)
        {
            lock (Missing)
            {
                return ownerGuid != null && Missing.TryGetValue(ownerGuid, out List<string> list) ? list.ToArray() : new string[0];
            }
        }

        private static void Report(string ownerGuid, string feature, string detail)
        {
            Remember(ownerGuid, feature);
            ModFramework.Log.LogWarning($"{ownerGuid ?? "A mod"}: \"{feature}\" is unavailable on this game build: {detail} was not found.");
        }

        private static void Remember(string ownerGuid, string feature)
        {
            string key = ownerGuid ?? "";
            lock (Missing)
            {
                if (!Missing.TryGetValue(key, out List<string> list))
                {
                    Missing[key] = list = new List<string>();
                }
                if (!list.Contains(feature))
                {
                    list.Add(feature);
                }
            }
        }
    }
}
