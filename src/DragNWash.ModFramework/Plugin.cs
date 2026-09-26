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
            // First, so it sees every log line from here to the title screen.
            StartupTiming.Install(Logger);
            ModFramework.Initialize(Logger);
            DeveloperTools.Install(Config);
            // Only with developer tools on: it costs a few tens of milliseconds.
            StartupTiming.Begin(this);
            ModReload.Install(Config, this);
            Operations.Install();
            CoreOperations.Register();
            var harmony = new Harmony(ModFramework.Guid);
            // First, so a crash while the rest starts is recorded too.
            CrashReports.Install(Config, harmony);
            // Direct3D 12: one font atlas upload per frame (UUM-140564).
            FontAtlasUploads.Install(Config, harmony, this);
            // Before anything else can go online, the framework's own update check included.
            NetworkWatch.Install(Config, harmony);
            Mods.ModsGlass.Install(Config);
            Mods.ModsScreen.Install(harmony);
            Options.OptionsRows.Install(harmony);
            Title.TitleVersion.Install(harmony);
            Updates.UpdateCheck.Install(Config, this);
            Updates.LauncherUpdate.Install(Config);
        }

        private void Update()
        {
            Options.OptionsRows.Tick();
            Title.TitleVersion.Tick();
            ModReload.Tick();
            Operations.Tick();
            CrashReports.Tick();
        }

        private void OnApplicationFocus(bool focused)
        {
            CrashReports.Focus(focused);
        }
    }
}
