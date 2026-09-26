using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragNWash.Installer.Tests
{
    // Checks of installer/Core.cs's uninstall side, on made-up game folders under
    // %TEMP%: that UninstallSteps lists only the steps a run will really do, in the
    // order Uninstall goes through them, and that Uninstall's own progress reports
    // match that order exactly (bar LaunchOption, which the caller reports, and Done,
    // which UninstallSteps doesn't list, the same as ProgressSteps leaves out
    // InstallStage.Done on the install side).
    internal static partial class Program
    {
        private static readonly ModManifest TestManifest = new ModManifest
        {
            Schema = 1,
            Name = "Test Mod",
            Version = "1.0.0",
            Plugins = new[] { "TestMod" },
            Keep = new[] { "TestMod/UserData" },
            ConfigFiles = new[] { "com.example.testmod.cfg" },
            Choices = new ModChoice[0],
        };

        // A game folder with this mod installed, the framework, and (unless solo is
        // false) nothing else using either. installerBackup adds a backup folder, as a
        // previous install would have left. bepInEx adds BepInEx\core\BepInEx.dll.
        private static string FakeGame(bool solo, bool installerBackup, bool bepInEx, bool saveHistory)
        {
            string game = Path.Combine(Path.GetTempPath(), "dnw-installer-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, Paths.GameExe), "");
            string plugins = Path.Combine(game, "BepInEx", "plugins");
            Directory.CreateDirectory(Path.Combine(plugins, "TestMod", "UserData"));
            File.WriteAllText(Path.Combine(plugins, "TestMod", "TestMod.dll"), "dll");
            File.WriteAllText(Path.Combine(plugins, "TestMod", "UserData", "save.txt"), "save");
            Directory.CreateDirectory(Path.Combine(game, "BepInEx", "config"));
            File.WriteAllText(Path.Combine(game, "BepInEx", "config", "com.example.testmod.cfg"), "[General]\n");
            Directory.CreateDirectory(Path.Combine(plugins, Paths.FrameworkPrefix));
            File.WriteAllText(Path.Combine(plugins, Paths.FrameworkPrefix, Paths.FrameworkPrefix + ".dll"), "fw");
            if (!solo)
            {
                Directory.CreateDirectory(Path.Combine(plugins, "OtherMod"));
                File.WriteAllText(Path.Combine(plugins, "OtherMod", "OtherMod.dll"), "other");
            }
            Directory.CreateDirectory(Path.Combine(game, "BepInEx", Paths.InstallerFolder));
            File.WriteAllText(Path.Combine(game, "BepInEx", Paths.InstallerFolder, Paths.LauncherExe), "launcher");
            if (installerBackup)
            {
                Directory.CreateDirectory(Path.Combine(game, "BepInEx", Paths.InstallerFolder, "backup", "2026-09-25_1000"));
                File.WriteAllText(Path.Combine(game, "BepInEx", Paths.InstallerFolder, "backup", "2026-09-25_1000", "old.dll"), "old");
            }
            if (bepInEx)
            {
                Directory.CreateDirectory(Path.Combine(game, "BepInEx", "core"));
                File.WriteAllText(Path.Combine(game, "BepInEx", "core", "BepInEx.dll"), "core");
            }
            if (saveHistory)
            {
                Directory.CreateDirectory(Path.Combine(game, "BepInEx", "SaveHistory"));
                File.WriteAllText(Path.Combine(game, "BepInEx", "SaveHistory", "snap.txt"), "snap");
            }
            return game;
        }

        private static void DeleteGame(string game)
        {
            try
            {
                var dir = new DirectoryInfo(game);
                foreach (FileInfo file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    file.Attributes = FileAttributes.Normal;
                }
                dir.Delete(true);
            }
            catch (Exception)
            {
            }
        }

        // Only the steps a run will really do are listed, in the order Uninstall goes
        // through them: Mod, Settings, Framework, Launcher/InstallerBackup, BepInEx.
        private static void UninstallSteps()
        {
            var core = new InstallerCore(TestManifest, "", _ => { });

            string solo = FakeGame(solo: true, installerBackup: true, bepInEx: true, saveHistory: true);
            try
            {
                var steps = core.UninstallSteps(solo, keepData: false, removeBepInEx: true);
                Same("solo, remove everything: stages in order",
                    string.Join(",", steps.Select(s => s.Stage)),
                    "Mod,Settings,Framework,Framework,Launcher,InstallerBackup,BepInEx");
                Check("solo: the mod's step doesn't mention keeping anything",
                    !steps.First(s => s.Stage == UninstallStage.Mod).Text.Contains("keep", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                DeleteGame(solo);
            }

            string soloKeep = FakeGame(solo: true, installerBackup: false, bepInEx: true, saveHistory: true);
            try
            {
                var steps = core.UninstallSteps(soloKeep, keepData: true, removeBepInEx: true);
                Same("solo, keep data: BepInEx's step says so too",
                    string.Join(",", steps.Select(s => s.Stage)),
                    "Mod,Settings,Framework,Launcher,BepInEx");
                Check("keep data: the mod's step names what's kept",
                    steps.First(s => s.Stage == UninstallStage.Mod).Text.Contains("UserData"));
                Check("keep data: SaveHistory's own line is left out (kept, not removed)",
                    steps.Count(s => s.Stage == UninstallStage.Framework) == 1);
                Check("keep data: the checklist's short line for the mod says what's kept",
                    steps.First(s => s.Stage == UninstallStage.Mod).Small?.Contains("UserData") == true);
                Check("every step has a short label for the checklist", steps.All(s => !string.IsNullOrEmpty(s.Step)));
            }
            finally
            {
                DeleteGame(soloKeep);
            }

            // Another mod still uses the framework: the framework and the launcher both
            // stay (only the launch option and this mod's own files go), but the backup
            // still goes, and BepInEx is kept "used" (removeBepInEx is otherwise honoured).
            string shared = FakeGame(solo: false, installerBackup: true, bepInEx: true, saveHistory: false);
            try
            {
                var steps = core.UninstallSteps(shared, keepData: false, removeBepInEx: true);
                Same("shared framework: only this mod and the backup go",
                    string.Join(",", steps.Select(s => s.Stage)),
                    "Mod,Settings,InstallerBackup");
            }
            finally
            {
                DeleteGame(shared);
            }

            // removeBepInEx off: no BepInEx step even solo.
            string keepBep = FakeGame(solo: true, installerBackup: false, bepInEx: true, saveHistory: false);
            try
            {
                var steps = core.UninstallSteps(keepBep, keepData: false, removeBepInEx: false);
                Check("BepInEx not asked for: no BepInEx step", steps.All(s => s.Stage != UninstallStage.BepInEx));
            }
            finally
            {
                DeleteGame(keepBep);
            }

            // The launch option going is the caller's own step, shown where Uninstall's
            // checklist has room for it (LaunchOption is never in what Uninstall reports).
            string launchGame = FakeGame(solo: true, installerBackup: false, bepInEx: false, saveHistory: false);
            try
            {
                var steps = core.UninstallSteps(launchGame, keepData: false, removeBepInEx: false, launch: LaunchOptionChange.Remove);
                Check("launch option removed is in the checklist", steps.Any(s => s.Stage == UninstallStage.LaunchOption));
            }
            finally
            {
                DeleteGame(launchGame);
            }
        }

        // Uninstall's own progress reports, in the order they arrive, match UninstallSteps
        // exactly (LaunchOption never comes from Uninstall; Done is Uninstall's own last word).
        private static void UninstallProgressOrder()
        {
            CheckProgressOrder("solo, remove everything", solo: true, installerBackup: true, bepInEx: true, saveHistory: true, keepData: false, removeBepInEx: true);
            CheckProgressOrder("solo, keep data", solo: true, installerBackup: true, bepInEx: true, saveHistory: true, keepData: true, removeBepInEx: true);
            CheckProgressOrder("shared framework", solo: false, installerBackup: true, bepInEx: true, saveHistory: false, keepData: false, removeBepInEx: true);
            CheckProgressOrder("nothing to remove beyond the mod", solo: true, installerBackup: false, bepInEx: false, saveHistory: false, keepData: false, removeBepInEx: false);
        }

        private static void CheckProgressOrder(string name, bool solo, bool installerBackup, bool bepInEx, bool saveHistory, bool keepData, bool removeBepInEx)
        {
            string game = FakeGame(solo, installerBackup, bepInEx, saveHistory);
            try
            {
                var core = new InstallerCore(TestManifest, "", _ => { });
                // Uninstall reports a stage once when it starts, the same as Install does
                // for its own (several StepSet lines under Settings, one Report): several
                // checklist lines under one stage (the two Framework lines) still make one
                // report, so consecutive duplicates collapse here.
                var listed = core.UninstallSteps(game, keepData, removeBepInEx).Select(s => s.Stage).ToList();
                var expected = new List<UninstallStage>();
                foreach (UninstallStage stage in listed)
                {
                    if (expected.Count == 0 || expected[expected.Count - 1] != stage)
                    {
                        expected.Add(stage);
                    }
                }
                expected.Add(UninstallStage.Done);
                var reported = new List<UninstallStage>();
                // Not System.Progress<T>: it posts to a captured context, which would
                // arrive after Uninstall (and this check) is already done. Heard at once,
                // like UpdateEngine's own IProgress<InstallStage> on the launcher side.
                var progress = new SyncProgress(reported.Add);
                bool keepLauncher = !solo && Directory.Exists(Path.Combine(game, "BepInEx", Paths.InstallerFolder));
                core.Uninstall(game, keepData, removeBepInEx, keepLauncher, progress);
                Same($"{name}: progress matches the checklist's order", string.Join(",", reported), string.Join(",", expected));
            }
            finally
            {
                DeleteGame(game);
            }
        }

        private sealed class SyncProgress : IProgress<UninstallStage>
        {
            private readonly Action<UninstallStage> _report;

            internal SyncProgress(Action<UninstallStage> report)
            {
                _report = report;
            }

            public void Report(UninstallStage value) => _report(value);
        }
    }
}
