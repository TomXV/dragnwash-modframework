using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using DragNWash.Installer;

namespace DragNWash.Launcher
{
    // What the window hears from an update while it runs. Called on the engine's thread.
    internal interface IUpdateEvents
    {
        // "dl", "chk", "bak", "ins": the step the engine has started.
        void Step(string step);

        void Download(int index, int count, ModUpdate mod, long done, long total, long allDone, long allTotal);

        // One zip passed its check: its size, and its SHA-256 when GitHub gave one.
        void Verified(int index, int count, ModUpdate mod, bool sha);

        // Every zip passed. The engine then waits for Proceed before it writes anything.
        void Checked();

        // Which mod's files are starting to go in, once per mod, while Step keeps
        // saying "ins" only the once for the whole run (see Run): the page shows that
        // mod's icon for as long as this one is going in.
        void Installing(int index, int count, ModUpdate mod);
    }

    // Why an update stopped, in the terms the failure screen explains.
    internal sealed class UpdateFailure
    {
        internal string Kind = "other"; // offline, busy, limited, notfound, mismatch, install, running, otherloader, other
        internal string Changed = "none"; // none, restored, partly
        internal int Files;
        internal int Minutes;
        internal ModUpdate Mod;
        internal readonly List<string> Detail = new List<string>();
    }

    internal sealed class UpdateOutcome
    {
        internal bool Cancelled;
        internal UpdateFailure Failure;
        internal string Backup = "";
        internal readonly List<string> Updated = new List<string>();
    }

    // Downloads the chosen mods' zips, checks each against the size and SHA-256 the game
    // got from GitHub, and installs them one by one with Install.exe's own code: backup
    // first, and everything put back if copying fails. Nothing in the game folder changes
    // before every zip has passed and the window said to go on, so Cancel until then
    // leaves the game as it was.
    internal sealed class UpdateEngine
    {
        // How long the engine waits for the window's go-ahead after the checks. The window
        // gives it once its check pictures are done; without it, the update goes on.
        private static readonly TimeSpan ProceedWait = TimeSpan.FromSeconds(30);

        private readonly string _game;
        private readonly IUpdateEvents _events;
        private readonly ManualResetEventSlim _proceed = new ManualResetEventSlim(false);

        internal UpdateEngine(string game, IUpdateEvents events)
        {
            _game = game;
            _events = events;
        }

        internal void Proceed() => _proceed.Set();

        internal UpdateOutcome Run(IList<ModUpdate> mods, CancellationToken cancel)
        {
            var outcome = new UpdateOutcome();
            string step = "dl";
            ModUpdate current = null;
            // A folder of this run's own, as Install.exe makes one.
            string temp = Path.Combine(Path.GetTempPath(), Paths.InstallerFolder, Guid.NewGuid().ToString("N"));
            try
            {
                Log.Line($"Launcher: {mods.Count} {(mods.Count == 1 ? "update" : "updates")} selected: {string.Join(", ", mods.Select(m => m.ShownName + " " + m.Latest.Tag))}");
                Directory.CreateDirectory(temp);
                long allTotal = mods.Sum(m => m.Zip.Size), before = 0;
                var zips = new string[mods.Count];

                _events.Step("dl");
                for (int i = 0; i < mods.Count; i++)
                {
                    current = mods[i];
                    ModUpdate mod = current;
                    int index = i + 1;
                    long start = before;
                    zips[i] = Path.Combine(temp, i.ToString(System.Globalization.CultureInfo.InvariantCulture), mod.Zip.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(zips[i]));
                    Log.Line($"{mod.ShownName} {mod.ShownVersion}: {mod.Zip.Download.Substring("https://".Length)}");
                    _events.Download(index, mods.Count, mod, 0, mod.Zip.Size, start, allTotal);
                    Fetch(mod, zips[i], done => _events.Download(index, mods.Count, mod, done, mod.Zip.Size, start + done, allTotal), cancel);
                    Log.Line($"{mod.ShownName} {mod.ShownVersion}: {mod.Zip.Size} bytes");
                    before += mod.Zip.Size;
                }

                step = "chk";
                _events.Step("chk");
                var payloads = new (ModManifest Manifest, string Folder)[mods.Count];
                for (int i = 0; i < mods.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    current = mods[i];
                    int index = i + 1;
                    ModUpdate mod = current;
                    payloads[i] = CheckZip(mod, zips[i], Path.Combine(temp, i.ToString(System.Globalization.CultureInfo.InvariantCulture), "payload"),
                        () => _events.Verified(index, mods.Count, mod, mod.Zip.Sha256.Length > 0));
                }
                current = null;

                _events.Checked();
                WaitHandle.WaitAny(new[] { _proceed.WaitHandle, cancel.WaitHandle }, ProceedWait);
                cancel.ThrowIfCancellationRequested();

                // From here on the game folder changes; each mod is one install with its own
                // backup and roll-back, exactly as Install.exe --install does it.
                step = "bak";
                bool backupShown = false, installShown = false;
                for (int i = 0; i < mods.Count; i++)
                {
                    current = mods[i];
                    ModUpdate mod = current;
                    int index = i + 1;
                    bool installingShown = false;
                    var progress = new Progress(stage =>
                    {
                        if (stage == InstallStage.Backup && !backupShown)
                        {
                            backupShown = true;
                            _events.Step("bak");
                        }
                        else if (stage == InstallStage.Put || stage == InstallStage.Settings)
                        {
                            if (!installShown)
                            {
                                installShown = true;
                                _events.Step("ins");
                            }
                            // Which mod is going in now, every time (Step("ins") above only
                            // says so once for the whole run): the page shows this mod's
                            // icon for as long as its files are being put in place.
                            if (!installingShown)
                            {
                                installingShown = true;
                                _events.Installing(index, mods.Count, mod);
                            }
                        }
                    });
                    var (manifest, folder) = payloads[i];
                    var core = new InstallerCore(manifest, folder, Log.Line);
                    var choices = new Dictionary<string, string>();
                    foreach (ModChoice choice in manifest.Choices)
                    {
                        choices[choice.Id] = choice.DefaultValue(core.ReadConfig(_game, choice.Config));
                    }
                    InstallResult result = core.Install(_game, choices, new InstallOptions(), progress, CancellationToken.None);
                    outcome.Updated.Add(current.ShownName);
                    outcome.Backup = result.Backup ?? outcome.Backup;
                }
                current = null;
                Log.Line($"Launcher: done, {outcome.Updated.Count} updated");
            }
            catch (OperationCanceledException)
            {
                Log.Line("Launcher: cancelled; the game folder was not changed");
                outcome.Cancelled = true;
            }
            catch (Exception ex)
            {
                outcome.Failure = Describe(ex, step, current, outcome.Updated.Count > 0);
                Log.Line("Launcher: update failed: " + outcome.Failure.Detail[0]);
            }
            finally
            {
                try
                {
                    InstallJournal.DeleteTree(temp);
                    Directory.Delete(Path.GetDirectoryName(temp)); // only when no other run's folder is in it
                }
                catch (Exception)
                {
                }
            }
            return outcome;
        }

        // The zip into file, from github.com (the address built from the repository and the tag).
        private static void Fetch(ModUpdate mod, string file, Action<long> bytes, CancellationToken cancel)
        {
#if DEBUG
            // For testing without GitHub: DNW_LAUNCHER_LOCAL_RELEASES=<folder with the zips>,
            // and DNW_LAUNCHER_FAIL=offline|busy|limited|notfound to fail the download.
            // Only in Debug builds; a Release build always downloads.
            string local = Environment.GetEnvironmentVariable("DNW_LAUNCHER_LOCAL_RELEASES");
            if (!string.IsNullOrEmpty(local))
            {
                LocalFetch(Path.Combine(local, mod.Zip.Name), file, mod.Zip.Size, bytes, cancel);
                return;
            }
#endif
            Downloader.Fetch(mod.Zip.Download, file, mod.Zip.Size, bytes, Log.Line, mod.ShownName, cancel);
        }

#if DEBUG
        private static void LocalFetch(string source, string file, long size, Action<long> bytes, CancellationToken cancel)
        {
            switch (Environment.GetEnvironmentVariable("DNW_LAUNCHER_FAIL"))
            {
                case "offline": throw new DownloadFailure(DownloadProblem.Offline, "no connection (NameResolutionFailure): The remote name could not be resolved: 'github.com'");
                case "busy": throw new DownloadFailure(DownloadProblem.Busy, "GitHub answered 503 Service Unavailable");
                case "limited": throw new DownloadFailure(DownloadProblem.Limited, "GitHub answered 429 Too Many Requests, try again in 14 minutes", minutes: 14);
                case "notfound": throw new DownloadFailure(DownloadProblem.NotFound, "GitHub answered 404 Not Found");
            }
            Log.Line($"(debug) copying {source} instead of downloading");
            using (var from = File.OpenRead(source))
            using (var to = File.Create(file))
            {
                var buffer = new byte[Math.Max(4096, (int)Math.Min(int.MaxValue, from.Length / 20 + 1))];
                long done = 0;
                int read;
                while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    to.Write(buffer, 0, read);
                    done += read;
                    if (done > size)
                    {
                        throw new DownloadFailure(DownloadProblem.Size, $"more than the {size} bytes expected");
                    }
                    bytes(done);
                    Thread.Sleep(60);
                }
                if (done != size)
                {
                    throw new DownloadFailure(DownloadProblem.Size, $"{done} bytes, {size} expected");
                }
            }
        }
#endif

        // The zip must be the release's, byte for byte, and hold the mod's own installer
        // manifest for the folder the game has it in, before anything is taken from it.
        // verified hears the moment the size and SHA-256 are known to be right, before the log says so.
        private (ModManifest, string) CheckZip(ModUpdate mod, string zip, string payload, Action verified)
        {
            string name = $"{mod.ShownName} {mod.ShownVersion}";
            long size = new FileInfo(zip).Length;
            if (size != mod.Zip.Size)
            {
                throw new CheckFailure($"{name}: {size} bytes, {mod.Zip.Size} expected; the file was deleted");
            }
            if (mod.Zip.Sha256.Length > 0)
            {
                string hash = Downloader.Sha256Of(zip);
                if (hash != mod.Zip.Sha256)
                {
                    TryDelete(zip);
                    throw new CheckFailure($"{name}: SHA-256 mismatch, expected {mod.Zip.Sha256}, got {hash}; the file was deleted");
                }
                verified();
                Log.Line($"{name}: size OK, SHA-256 OK ({hash.Substring(0, 8)}...{hash.Substring(56)})");
            }
            else
            {
                verified();
                Log.Line($"{name}: size OK; GitHub gives no SHA-256 for this file, so only the size was checked");
            }

            Unpack(zip, payload);
            string folder = FindPayload(payload);
            if (folder == null)
            {
                throw new ZipFailure($"{name}: the zip has no {ModManifest.FileName} next to a BepInEx folder");
            }
            ModManifest manifest;
            try
            {
                manifest = ModManifest.Load(Path.Combine(folder, ModManifest.FileName));
            }
            catch (Exception ex)
            {
                throw new ZipFailure($"{name}: {ModManifest.FileName} in the zip can't be used: {ex.Message}");
            }
            if (!manifest.Plugins.Contains(mod.PluginFolder, StringComparer.OrdinalIgnoreCase))
            {
                throw new ZipFailure($"{name}: the zip installs {string.Join(", ", manifest.Plugins)}, not {mod.PluginFolder}; not used");
            }
            if (mod.Latest.Version != null && manifest.Version != null && GameFiles.Numeric(manifest.Version) != GameFiles.Numeric(mod.Latest.Version))
            {
                Log.Line($"{name}: its {ModManifest.FileName} says version {manifest.Version}");
            }
            Log.Line($"{name}: {ModManifest.FileName} OK ({manifest.Name}, {string.Join(", ", manifest.Plugins)})");
            return (manifest, folder);
        }

        // Every entry that stays inside dir; the rest are skipped, as Install.exe unpacks.
        private static void Unpack(string zip, string dir)
        {
            string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            Directory.CreateDirectory(root);
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
        }

        // Where Install.exe would sit in the unpacked zip: the folder with mod-install.json
        // and BepInEx/, at the top or one folder down.
        private static string FindPayload(string root)
        {
            bool Is(string d) => File.Exists(Path.Combine(d, ModManifest.FileName)) && Directory.Exists(Path.Combine(d, "BepInEx"));
            if (Is(root))
            {
                return root;
            }
            string[] inside = Directory.GetDirectories(root).Where(Is).ToArray();
            return inside.Length == 1 ? inside[0] : null;
        }

        private static UpdateFailure Describe(Exception ex, string step, ModUpdate mod, bool someUpdated)
        {
            var failure = new UpdateFailure { Mod = mod };
            string line;
            switch (ex)
            {
                case DownloadFailure download:
                    failure.Kind = Kind(download.Problem);
                    failure.Minutes = download.Minutes;
                    line = $"{mod?.ShownName} {mod?.ShownVersion}: {download.Message}";
                    break;
                case CheckFailure check:
                    failure.Kind = "mismatch";
                    line = check.Message;
                    break;
                case ZipFailure zip:
                    failure.Kind = "other";
                    line = zip.Message;
                    break;
                case InstallerException installer:
                    line = installer.LogText ?? installer.Text(true) + (installer.Detail == null ? "" : " (" + installer.Detail.Split('\n')[0].Trim() + ")");
                    switch (installer.Key)
                    {
                        case Strings.Key.NetOffline: failure.Kind = "offline"; break;
                        case Strings.Key.NetBusy: failure.Kind = "busy"; break;
                        case Strings.Key.NetLimited:
                            failure.Kind = "limited";
                            failure.Minutes = installer.Args?.Length > 0 && installer.Args[0] is int m ? m : 0;
                            break;
                        case Strings.Key.NetLimitedLater: failure.Kind = "limited"; break;
                        case Strings.Key.FwNotPublished: failure.Kind = "notfound"; break;
                        case Strings.Key.FwSize:
                        case Strings.Key.FwHash:
                        case Strings.Key.BepInExHash: failure.Kind = "mismatch"; break;
                        case Strings.Key.Running: failure.Kind = "running"; break;
                        case Strings.Key.OtherLoader:
                        case Strings.Key.OtherLoaderNamed: failure.Kind = "otherloader"; break;
                        case Strings.Key.RolledBack:
                            failure.Kind = "install";
                            failure.Changed = "restored";
                            failure.Files = installer.Args?.Length > 0 && installer.Args[0] is int undone ? undone : 0;
                            break;
                        case Strings.Key.RolledBackPartly:
                            failure.Kind = "install";
                            failure.Changed = "partly";
                            failure.Files = installer.Args?.Length > 0 && installer.Args[0] is int left ? left : 0;
                            break;
                        default: failure.Kind = step == "bak" ? "install" : "other"; break;
                    }
                    break;
                default:
                    // Install rolls back what it copied before it lets an error out; this is
                    // anything else, such as a file the launcher could not unpack.
                    failure.Kind = step == "bak" ? "install" : "other";
                    line = (mod == null ? "" : $"{mod.ShownName} {mod.ShownVersion}: ") + ex.Message;
                    Log.Line(ex.ToString());
                    break;
            }
            failure.Detail.Add("Error: " + line);
            string where = step == "dl" ? "download" : step == "chk" ? "check" : "install";
            failure.Detail.Add("Step: " + where + (mod == null ? "" : $", {mod.ShownName} {mod.ShownVersion}"));
            failure.Detail.Add("Game folder: " + (failure.Changed == "restored" ? "put back as it was"
                : failure.Changed == "partly" ? "not everything could be put back, see the log"
                : someUpdated ? "the mods before this one were updated, this one was not changed" : "not changed"));
            return failure;
        }

        private static string Kind(DownloadProblem problem)
        {
            switch (problem)
            {
                case DownloadProblem.Offline: return "offline";
                case DownloadProblem.Busy: return "busy";
                case DownloadProblem.Limited: return "limited";
                case DownloadProblem.NotFound: return "notfound";
                case DownloadProblem.Size: return "mismatch";
                default: return "other";
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception)
            {
            }
        }

        // Install's progress, heard at once on the thread that reports it (System.Progress
        // would post it to the window's thread, too late to keep the steps in order).
        private sealed class Progress : IProgress<InstallProgress>
        {
            private readonly Action<InstallStage> _stage;

            internal Progress(Action<InstallStage> stage)
            {
                _stage = stage;
            }

            public void Report(InstallProgress value) => _stage(value.Stage);
        }

        private sealed class CheckFailure : Exception
        {
            internal CheckFailure(string message) : base(message)
            {
            }
        }

        private sealed class ZipFailure : Exception
        {
            internal ZipFailure(string message) : base(message)
            {
            }
        }
    }
}
