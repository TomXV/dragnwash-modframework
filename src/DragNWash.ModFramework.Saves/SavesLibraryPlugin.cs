using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace DragNWash.ModFramework.Saves
{
    // The flags and saves library's BepInEx entry point.
    [BepInPlugin(GameSaves.Guid, "DragNWash.ModFramework.Saves", GameSaves.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    internal sealed class SavesLibraryPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private ConfigEntry<bool> _historyEnabled;
        private ConfigEntry<int> _keep;

        private void Awake()
        {
            Log = Logger;
            // A reloaded mod's old handlers go (ModReload); the new build subscribes again.
            ModReload.Unloading += (guid, assembly) => ModReload.PruneEvent(typeof(GameSaves), nameof(GameSaves.SaveWritten), assembly);
            ModFramework.Register(new ModInfo
            {
                Guid = GameSaves.Guid,
                DisplayName = "Drag'n Wash ModFramework: Flags and saves",
                Description = "Reads and changes the game's save slots and event flags, and keeps a copy of every version of each save so any change can be undone.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
            });

            _historyEnabled = Config.Bind("History", "Enabled", true,
                "Keep a copy in BepInEx/SaveHistory every time the game writes a save. Copies are always taken before a mod changes a save.");
            _keep = Config.Bind("History", "Keep", 30,
                "How many copies to keep per save slot.");

            SavesOperations.Register();
            GameSaves.HistoryFolder = Path.Combine(Paths.BepInExRootPath, "SaveHistory");
            GameFlags.AddCatalog(Path.Combine(Path.GetDirectoryName(Info.Location) ?? "", "FlagCatalog.csv"));
        }

        private void Update()
        {
            if (_historyEnabled.Value)
            {
                GameSaves.Tick(_keep.Value);
            }
        }
    }
}
