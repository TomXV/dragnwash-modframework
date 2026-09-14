using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace DragNWash.ModFramework
{
    // BepInEx preloader patcher. It patches no assembly; BepInEx runs patchers
    // before loading any plugin, which is the one moment plugin DLLs can be
    // renamed or removed safely. See DisabledMods and PendingUninstalls.
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs => new string[0];

        public static void Initialize()
        {
            ManualLogSource log = Logger.CreateLogSource("ModFramework.Preloader");
            try
            {
                // Uninstalls first: a removed mod has nothing left to switch.
                PendingUninstalls.Apply(Paths.PluginPath, Paths.ConfigPath, message => log.LogInfo(message));
            }
            catch (Exception ex)
            {
                log.LogError($"Could not apply uninstalled mods: {ex}");
            }
            try
            {
                DisabledMods.Apply(Paths.PluginPath, Paths.ConfigPath, message => log.LogInfo(message));
            }
            catch (Exception ex)
            {
                // Never stop the game from starting.
                log.LogError($"Could not apply switched-off mods: {ex}");
            }
        }

        public static void Patch(AssemblyDefinition assembly)
        {
        }
    }
}
