using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;

namespace DragNWash.ModFramework.Text
{
    // The text library's BepInEx entry point: checks the TextMeshPro methods it
    // hooks, installs the hooks, and tells the Mods screen what it is.
    [BepInPlugin(GameText.Guid, "DragNWash.ModFramework.Text", GameText.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    internal sealed class TextLibraryPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private const string Feature = "Text events";

        private void Awake()
        {
            Log = Logger;
            ModReload.Unloading += GameText.RemoveOwned;
            TextOperations.Register();
            ModFramework.Register(new ModInfo
            {
                Guid = GameText.Guid,
                DisplayName = "Drag'n Wash ModFramework: Text",
                Description = "Lets mods see and replace text before the game shows it.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
            });

            MethodInfo setter = AccessTools.PropertySetter(typeof(TMP_Text), nameof(TMP_Text.text));
            MethodInfo setText = AccessTools.Method(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string) });
            MethodInfo setTextBool = AccessTools.Method(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string), typeof(bool) });
            MethodInfo uguiEnable = AccessTools.Method(typeof(TextMeshProUGUI), "OnEnable");
            MethodInfo worldEnable = AccessTools.Method(typeof(TextMeshPro), "OnEnable");

            bool ok = GameHooks.Require(GameText.Guid, Feature, setter != null, "TMP_Text.text setter")
                      & GameHooks.Require(GameText.Guid, Feature, setText != null, "TMP_Text.SetText(string)")
                      & GameHooks.Require(GameText.Guid, Feature, uguiEnable != null, "TextMeshProUGUI.OnEnable");
            if (!ok)
            {
                return;
            }

            try
            {
                var harmony = new Harmony(GameText.Guid);
                // First, so rewriters see the text the game set even when another
                // mod patches the same methods.
                var prefix = new HarmonyMethod(typeof(TextLibraryPlugin), nameof(BeforeSet)) { priority = Priority.First };
                harmony.Patch(setter, prefix: prefix);
                harmony.Patch(setText, prefix: prefix);
                // In the shipped TMP both overloads write their own buffers and
                // neither calls the other or the setter.
                if (setTextBool != null)
                {
                    harmony.Patch(setTextBool, prefix: prefix);
                }
                var enabled = new HarmonyMethod(typeof(TextLibraryPlugin), nameof(AfterEnable)) { priority = Priority.First };
                harmony.Patch(uguiEnable, postfix: enabled);
                if (worldEnable != null)
                {
                    harmony.Patch(worldEnable, postfix: enabled);
                }
                GameText.IsAvailable = true;
            }
            catch (Exception ex)
            {
                GameHooks.Require(GameText.Guid, Feature, false, ex.Message);
            }
        }

        private static void BeforeSet(TMP_Text __instance, ref string __0)
        {
            try
            {
                GameText.OnSet(__instance, ref __0);
            }
            catch (Exception ex)
            {
                Log.LogError($"Text hook failed: {ex}");
            }
        }

        private static void AfterEnable(TMP_Text __instance)
        {
            try
            {
                GameText.OnEnabled(__instance);
            }
            catch (Exception ex)
            {
                Log.LogError($"Text hook failed: {ex}");
            }
        }
    }
}
