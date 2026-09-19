using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using Yarn.Unity;

namespace DragNWash.ModFramework.Dialogue
{
    // The dialogue library's BepInEx entry point.
    //
    // Yarn Spinner 3's LinePresenter receives a LocalizedLine, with its ID and
    // speaker, just before its typewriter writes the text into a TMP component;
    // an OptionItem receives a DialogueOption in its Option setter just before it
    // sets its text. Hooking those two moments is how a line's identity reaches
    // text that TMP otherwise only sees as a string. Each hook is checked and
    // installed on its own, so a Yarn change costs only the feature it breaks.
    [BepInPlugin(GameDialogue.Guid, "DragNWash.ModFramework.Dialogue", GameDialogue.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    internal sealed class DialogueLibraryPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private static FieldInfo _optionText;

        private void Awake()
        {
            Log = Logger;
            // A reloaded mod's old handlers go (ModReload); the new build subscribes again.
            ModReload.Unloading += (guid, assembly) =>
            {
                ModReload.PruneEvent(typeof(GameDialogue), nameof(GameDialogue.NodeStarted), assembly);
                ModReload.PruneEvent(typeof(GameDialogue), nameof(GameDialogue.LineShowing), assembly);
                ModReload.PruneEvent(typeof(GameDialogue), nameof(GameDialogue.OptionShowing), assembly);
            };
            DialogueOperations.Register();
            ModFramework.Register(new ModInfo
            {
                Guid = GameDialogue.Guid,
                DisplayName = "Drag'n Wash ModFramework: Dialogue",
                Description = "Tells mods which line of dialogue or option is about to be shown, with its line ID and speaker.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
            });

            var harmony = new Harmony(GameDialogue.Guid);

            MethodInfo runLine = AccessTools.Method(typeof(LinePresenter), nameof(LinePresenter.RunLineAsync));
            if (GameHooks.Require(GameDialogue.Guid, "Dialogue line events", runLine != null, "LinePresenter.RunLineAsync"))
            {
                GameDialogue.LinesAvailable = TryPatch(harmony, runLine, nameof(BeforeRunLine), "Dialogue line events");
            }

            MethodInfo optionSetter = AccessTools.PropertySetter(typeof(OptionItem), nameof(OptionItem.Option));
            _optionText = AccessTools.Field(typeof(OptionItem), "text");
            if (GameHooks.Require(GameDialogue.Guid, "Dialogue option events", optionSetter != null && _optionText != null, "OptionItem.Option and its text field"))
            {
                GameDialogue.OptionsAvailable = TryPatch(harmony, optionSetter, nameof(BeforeSetOption), "Dialogue option events");
            }

            MethodInfo nodeStarted = AccessTools.Method(typeof(DialogueRunner), "OnNodeStarted");
            if (GameHooks.Require(GameDialogue.Guid, "Current node", nodeStarted != null, "DialogueRunner.OnNodeStarted"))
            {
                TryPatch(harmony, nodeStarted, nameof(BeforeNodeStarted), "Current node");
            }
        }

        private static bool TryPatch(Harmony harmony, MethodInfo target, string prefix, string feature)
        {
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(DialogueLibraryPlugin), prefix) { priority = Priority.First });
                return true;
            }
            catch (Exception ex)
            {
                GameHooks.Require(GameDialogue.Guid, feature, false, ex.Message);
                return false;
            }
        }

        private static void BeforeRunLine(LinePresenter __instance, LocalizedLine line)
        {
            try
            {
                if (line != null)
                {
                    GameDialogue.OnLine(__instance.lineText, Describe(line, __instance.lineText, isOption: false, available: true));
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Dialogue line hook failed: {ex}");
            }
        }

        private static void BeforeSetOption(OptionItem __instance, DialogueOption value)
        {
            try
            {
                if (value?.Line != null)
                {
                    var component = _optionText.GetValue(__instance) as TMP_Text;
                    GameDialogue.OnLine(component, Describe(value.Line, component, isOption: true, available: value.IsAvailable));
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Dialogue option hook failed: {ex}");
            }
        }

        private static void BeforeNodeStarted(string startedNodeName)
        {
            try
            {
                GameDialogue.OnNodeStarted(startedNodeName);
            }
            catch (Exception ex)
            {
                Log.LogError($"Dialogue node hook failed: {ex}");
            }
        }

        private static DialogueLine Describe(LocalizedLine line, TMP_Text component, bool isOption, bool available)
        {
            return new DialogueLine
            {
                LineId = line.TextID,
                Speaker = line.CharacterName,
                Text = line.TextWithoutCharacterName.Text,
                FullText = line.Text.Text,
                Metadata = line.Metadata ?? new string[0],
                Node = GameDialogue.CurrentNode,
                Component = component,
                IsOption = isOption,
                IsAvailable = available,
            };
        }
    }
}
