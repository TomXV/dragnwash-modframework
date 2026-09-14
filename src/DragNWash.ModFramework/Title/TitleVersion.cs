using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace DragNWash.ModFramework.Title
{
    // Like Minecraft Forge's title screen: a line above the game's build id in the
    // corner of the title screen says the framework is running and how many mods
    // loaded, so a player can tell at a glance that mods are in.
    //
    // The game writes its build id in VersionNumber.Start. The label is a copy of
    // that text (same font, size, colour and anchoring) placed just above it.
    internal static class TitleVersion
    {
        private const string Feature = "Version on the title screen";
        private const string LabelName = "ModFrameworkVersion";

        private static TMP_Text _label;
        private static int _shownRevision = -1;

        internal static void Install(Harmony harmony)
        {
            if (!GameHooks.Require(ModFramework.Guid, Feature, "VersionNumber", "Start"))
            {
                return;
            }
            try
            {
                MethodInfo start = AccessTools.Method(AccessTools.TypeByName("VersionNumber"), "Start");
                harmony.Patch(start, postfix: new HarmonyMethod(typeof(TitleVersion), nameof(AfterStart)));
            }
            catch (Exception ex)
            {
                GameHooks.Require(ModFramework.Guid, Feature, false, ex.Message);
            }
        }

        private static void AfterStart(MonoBehaviour __instance)
        {
            try
            {
                Add(__instance);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not show the framework version on the title screen: {ex.Message}");
            }
        }

        internal static string Text()
        {
            int mods = Chainloader.PluginInfos.Count;
            string text = $"Drag'n Wash ModFramework {ModFramework.Version}\n{mods} {(mods == 1 ? "mod" : "mods")} loaded";
            // Most players never open the Mods screen, so say it here too.
            int updates = Updates.UpdateCheck.NewerCount();
            if (updates > 0)
            {
                text += $"\n{updates} {(updates == 1 ? "update" : "updates")} available in Mods";
            }
            return text;
        }

        // From Plugin.Update: an update check finished while the title screen shows.
        internal static void Tick()
        {
            if (_label == null || _shownRevision == Updates.UpdateCheck.Revision)
            {
                return;
            }
            _shownRevision = Updates.UpdateCheck.Revision;
            try
            {
                SetText(_label, Text());
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not update the title screen line: {ex.Message}");
                _label = null;
            }
        }

        // The label is pinned by its bottom-right corner, so it grows upwards.
        private static void SetText(TMP_Text label, string text)
        {
            label.text = text;
            Vector2 size = label.GetPreferredValues(text);
            ((RectTransform)label.transform).sizeDelta = new Vector2(size.x + 4f, size.y);
        }

        private static void Add(MonoBehaviour versionNumber)
        {
            var original = versionNumber.GetComponent<TMP_Text>();
            Transform parent = versionNumber.transform.parent;
            if (original == null || parent == null || parent.Find(LabelName) != null)
            {
                return;
            }

            GameObject copy = UnityEngine.Object.Instantiate(versionNumber.gameObject, parent, false);
            copy.name = LabelName;
            // The copy must not write the build id over our text when it starts.
            UnityEngine.Object.DestroyImmediate(copy.GetComponent(versionNumber.GetType()));

            var label = copy.GetComponent<TMP_Text>();
            label.enableAutoSizing = false;
            label.fontSize = original.fontSize;
            label.textWrappingMode = TextWrappingModes.NoWrap;

            // Right-aligned to where the build id's text ends, sitting on top of the
            // text itself rather than its (taller, wider) rectangle, so the lines
            // stay on screen in the corner and close to the build id.
            original.ForceMeshUpdate();
            Bounds text = original.textBounds;
            var from = (RectTransform)versionNumber.transform;
            var to = (RectTransform)copy.transform;
            label.alignment = TextAlignmentOptions.BottomRight;
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = new Vector2(1f, 0f);
            _shownRevision = Updates.UpdateCheck.Revision;
            SetText(label, Text());
            to.localPosition = new Vector3(from.localPosition.x + text.max.x, from.localPosition.y + text.max.y + 2f, from.localPosition.z);
            to.SetSiblingIndex(from.GetSiblingIndex() + 1);
            _label = label;
        }
    }
}
