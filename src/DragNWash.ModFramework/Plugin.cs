using BepInEx;
using HarmonyLib;

namespace DragNWash.ModFramework
{
    // The BepInEx entry point. It only starts the framework; everything other
    // mods use lives in the public static classes (ModFramework, GameInfo, ...)
    // so they never need a reference to this component.
    [BepInPlugin(ModFramework.Guid, ModFramework.Name, ModFramework.Version)]
    internal sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            ModFramework.Initialize(Logger);
            DeveloperTools.Install(Config);
            ModReload.Install(Config, this);
            var harmony = new Harmony(ModFramework.Guid);
            // Before anything else can go online, the framework's own update check included.
            NetworkWatch.Install(Config, harmony);
            Mods.ModsScreen.Install(harmony);
            Options.OptionsRows.Install(harmony);
            Title.TitleVersion.Install(harmony);
            Updates.UpdateCheck.Install(Config, this);
        }

        private void Update()
        {
            Options.OptionsRows.Tick();
            Title.TitleVersion.Tick();
            ModReload.Tick();
        }
    }
}
