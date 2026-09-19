using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace DragNWash.ModFramework.Assets
{
    // The assets library's BepInEx entry point.
    [BepInPlugin(GameFonts.Guid, "DragNWash.ModFramework.Assets", GameFonts.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(global::DragNWash.ModFramework.ToolWindow.ToolWindow.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    internal sealed class AssetsLibraryPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<int> AtlasPointSize;
        internal static ConfigEntry<bool> AllowReload;
        internal static ConfigEntry<bool> WatchFiles;

        // Reached only through AssetCatalog.ShowInToolWindow: the Tool window
        // types are touched inside, so this library still loads without it.
        internal static void ShowTexture(string textureName)
        {
            if (!TabReady)
            {
                return;
            }
            AssetsTab.ShowTexture(textureName);
        }

        private void Awake()
        {
            Log = Logger;
            // A reloaded mod's old handlers go (ModReload); the new build subscribes again.
            ModReload.Unloading += (guid, assembly) => ModReload.PruneEvent(typeof(GameFonts), nameof(GameFonts.CharactersPrepared), assembly);
            AssetsOperations.Register();
            ModFramework.Register(new ModInfo
            {
                Guid = GameFonts.Guid,
                DisplayName = "Drag'n Wash ModFramework: Assets",
                Description = "Fonts for text the game's own fonts cannot show, and loading of textures and asset bundles, done so they do not crash Direct3D 12.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
            });

            AtlasPointSize = Config.Bind("Fonts", "AtlasPointSize", 80,
                new ConfigDescription("Point size glyphs are rasterized at for the fallback fonts. Higher is sharper. Glyphs are rasterized when a mod prepares its text, usually at startup, so raising this costs loading time rather than performance during play.",
                    new AcceptableValueRange<int>(24, 160), new SettingMeta { DisplayName = "Font atlas point size", Advanced = true, RequiresRestart = true }));

            GameFonts.AddFontFolder(Path.Combine(Path.GetDirectoryName(Info.Location) ?? "", "fonts"));

            // Experimental: texture replacements from every mod's assets/textures folder,
            // read now while uploads are safe, and the Assets tab when the Tool window is there.
            AssetReplacements.LoadAll();
            SetUpReload();
            TryInstallTab();

            if (!GameFonts.RuntimeUploadsAreSafe)
            {
                Logger.LogInfo($"Direct3D 12 on Unity {Application.unityVersion}: fonts, textures and asset bundles should be loaded at startup (Unity issue UUM-140564). If the game still crashes, add -force-d3d11 to its Steam launch options.");
            }
        }

        // Reloading uploads textures while the game runs, which Direct3D 12 can
        // crash on; the guard notices a crash at the next start and switches
        // reloading off until the player turns it back on.
        private const string ToolsOffReason = "developer tools are off (Options > Mods > Drag'n Wash ModFramework)";

        private void SetUpReload()
        {
            AllowReload = Config.Bind("Reload", "AllowReload", true,
                "Lets the Assets tab reload texture replacements from disk while the game runs. Switched off by itself when the game crashed during a reload; turn it back on to try again. On Direct3D 12 a reload can crash the game (Unity issue UUM-140564); -force-d3d11 in the Steam launch options avoids that.");
            WatchFiles = Config.Bind("Reload", "WatchFiles", false,
                new ConfigDescription("Reloads a texture replacement by itself when its PNG changes on disk. Never active on Direct3D 12.",
                    null, new SettingMeta { DisplayName = "Watch texture files", Advanced = true }));

            bool crashed = ReloadGuard.CheckAtStartup();
            if (crashed)
            {
                Logger.LogError($"The game did not come back from the last texture reload ({ReloadGuard.LastCrash}). Reloading is switched off; set [Reload] AllowReload to true to try again" +
                                (GameFonts.RuntimeUploadsAreSafe ? "." : ", or add -force-d3d11 to the game's launch options and work there."));
                AllowReload.Value = false;
            }
            bool refused = !GameFonts.RuntimeUploadsAreSafe && ReloadGuard.CrashCount >= 2;
            if (!AllowReload.Value)
            {
                AssetReplacements.ReloadDisabled = true;
                AssetReplacements.ReloadDisabledReason = crashed ? "switched off: the game crashed during the last reload" : "switched off in the config ([Reload] AllowReload)";
            }
            else if (refused)
            {
                AssetReplacements.ReloadDisabled = true;
                AssetReplacements.ReloadDisabledReason = $"refused on Direct3D 12 after {ReloadGuard.CrashCount} crashes; use -force-d3d11";
            }
            if (AssetReplacements.ReloadDisabled)
            {
                GameHooks.Unavailable(GameFonts.Guid, "Texture reload", AssetReplacements.ReloadDisabledReason);
            }
            else if (!DeveloperTools.Enabled)
            {
                // Not a fault, so nothing on the Mods screen: a player who never
                // turned the tools on simply has no reloading.
                AssetReplacements.ReloadDisabled = true;
                AssetReplacements.ReloadDisabledReason = ToolsOffReason;
            }
            if (!AssetReplacements.ReloadDisabled && WatchFiles.Value)
            {
                AssetReplacements.WatchFiles();
            }
            DeveloperTools.Changed += () =>
            {
                if (DeveloperTools.Enabled)
                {
                    if (AssetReplacements.ReloadDisabled && AssetReplacements.ReloadDisabledReason == ToolsOffReason)
                    {
                        AssetReplacements.ReloadDisabled = false;
                        AssetReplacements.ReloadDisabledReason = null;
                        if (WatchFiles.Value)
                        {
                            AssetReplacements.WatchFiles();
                        }
                    }
                }
                else if (!AssetReplacements.ReloadDisabled)
                {
                    AssetReplacements.ReloadDisabled = true;
                    AssetReplacements.ReloadDisabledReason = ToolsOffReason;
                }
            };
            // Turning it back on from the Mods screen takes effect at once and forgets the crashes.
            AllowReload.SettingChanged += (sender, args) =>
            {
                if (AllowReload.Value)
                {
                    ReloadGuard.ResetCount();
                    if (!DeveloperTools.Enabled)
                    {
                        AssetReplacements.ReloadDisabled = true;
                        AssetReplacements.ReloadDisabledReason = ToolsOffReason;
                        return;
                    }
                    AssetReplacements.ReloadDisabled = false;
                    AssetReplacements.ReloadDisabledReason = null;
                    if (WatchFiles.Value)
                    {
                        AssetReplacements.WatchFiles();
                    }
                    Logger.LogMessage("Texture reload switched back on.");
                }
                else
                {
                    AssetReplacements.ReloadDisabled = true;
                    AssetReplacements.ReloadDisabledReason = "switched off in the config ([Reload] AllowReload)";
                }
            };
        }

        private void Update()
        {
            AssetReplacements.Tick();
            // The Tool window may load after this library; keep trying until it is there.
            if (!_tabInstalled && !_tabGivenUp && Time.frameCount % 60 == 0)
            {
                TryInstallTab();
            }
        }

        private bool _tabInstalled, _tabGivenUp;
        private static bool TabReady;

        // The Assets tab needs the Tool window library. Its assembly is looked
        // up by name rather than through the chainloader, whose plugin list is
        // not filled in the same order on every load.
        private void TryInstallTab()
        {
            bool present = false;
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "DragNWash.ModFramework.ToolWindow")
                {
                    present = true;
                    break;
                }
            }
            if (!present)
            {
                if (Time.realtimeSinceStartup > 30f)
                {
                    _tabGivenUp = true;
                    Logger.LogInfo("No Tool window library: the Assets tab and the assets command are not available.");
                }
                return;
            }
            _tabInstalled = InstallTab();
            TabReady = _tabInstalled;
            _tabGivenUp = !_tabInstalled;
        }

        // In its own method so the Tool window types are only loaded when it is installed.
        private static bool InstallTab()
        {
            try
            {
                AssetsTab.Install();
                Log.LogInfo("Assets tab and the assets command added to the Tool window.");
                return true;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"The Assets tab could not be added: {ex}");
                return false;
            }
        }
    }
}
