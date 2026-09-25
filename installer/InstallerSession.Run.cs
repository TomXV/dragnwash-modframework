using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DragNWash.Installer
{
    // Install, Update and Uninstall from the page: the Steam and download questions
    // (sheets over the board), then the run with its checklist, and how it ended.
    internal sealed partial class InstallerSession
    {
        // One line of the checklist while it runs: the stage that does it, as the page shows it.
        private sealed class Row
        {
            internal int Stage;
            internal string Text;
            internal string Small;
            // Uninstall: the plan's own line, for the done board's list.
            internal string Long;
            internal string Pic;
            internal bool BepInEx;
            internal string Mark = "todo";
        }

        // The extra step after Install's own: the launch options, which the window sets.
        private const int LaunchStage = 50;

        private bool _busy;
        private bool _broken;
        private bool _closeWhenDone;
        private CancellationTokenSource _cancel;
        private bool _canStop;

        // The run being asked for or running, and how its questions were answered.
        private InstallRun.Request _request;
        private bool _update;
        private List<Row> _rows = new List<Row>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private bool _downloads;
        private int _pct;

        // The open sheet: "steam", "consent" or null.
        private string _sheet;
        private System.Windows.Forms.Timer _steamPoll;
        private DateTime? _askedAt;
        private FrameworkPlan _consentPlan;

        // How the last run ended, for the done or failed board (and a new language).
        private InstallResult _result;
        // Uninstall's plan as the setup board showed it, in every language, taken just before
        // the run (the folder has changed after it): the done board's list, which follows a
        // new language. _runLang: the language the run's checklist is in.
        private Dictionary<string, List<string>> _uninstallPlans;
        private string _runLang;
        private Exception _error;
        private string _errorDetails;

        // ---- the questions before anything runs ----

        // options: null to ask before downloading ModFramework; set when trying again or
        // after a choice on the failed board.
        private void Begin(bool install, InstallOptions options)
        {
            string game = _game.Trim();
            if (!_found || _sheet != null || install && OtherLoader().Found != null)
            {
                // Greyed out on the page; another loader stops Install anyway.
                return;
            }
            _request = new InstallRun.Request
            {
                Install = install,
                Game = game,
                Choices = new Dictionary<string, string>(_choices),
                Options = options,
                Launch = LaunchChange(install),
                KeepData = _keepData,
                AlsoBepInEx = _alsoBepInEx,
            };
            // Steam first, before the download question and before anything changes.
            if ((_request.Launch == LaunchOptionChange.Add || _request.Launch == LaunchOptionChange.Remove) && Steam.Running())
            {
                _sheet = "steam";
                _askedAt = null;
                _window.Send(Json.Object("type", "sheet", "sheet", "steam", "remove", _request.Launch == LaunchOptionChange.Remove,
                    "slow", Strings.Get(Strings.Key.SteamSlow, Steam.ExitSeconds)));
                _steamPoll = new System.Windows.Forms.Timer { Interval = 500 };
                _steamPoll.Tick += (s, e) => SteamTick();
                _steamPoll.Start();
                return;
            }
            AfterSteam();
        }

        private void SteamAnswer(string answer)
        {
            if (_sheet != "steam")
            {
                return;
            }
            switch (answer)
            {
                case "close":
                    try
                    {
                        string exe = Steam.AskToExit(AppendLog);
                        if (exe != null)
                        {
                            _request.ClosedSteam = exe;
                            _askedAt = DateTime.UtcNow;
                            _window.Send(Json.Object("type", "steamWait", "phase", "closing"));
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Steam: could not ask it to exit: {ex.Message}");
                    }
                    // Nothing to ask: the player closes it.
                    _window.Send(Json.Object("type", "steamWait", "phase", "waiting"));
                    break;
                case "self":
                    _window.Send(Json.Object("type", "steamWait", "phase", "waiting"));
                    break;
                case "skip":
                    AppendLog("Steam launch option: skipped this time (Steam is running)");
                    _request.Skipped = true;
                    _request.Launch = LaunchOptionChange.None;
                    StopSteamWait();
                    AfterSteam();
                    break;
                default:
                    // × and Esc: the whole run stops, with nothing changed.
                    StopSteamWait();
                    _sheet = null;
                    _request = null;
                    _window.Send(Json.Object("type", "sheetClose"));
                    break;
            }
        }

        // Steam gone, however it went: carry on.
        private void SteamTick()
        {
            if (!Steam.Running())
            {
                StopSteamWait();
                AppendLog("Steam: closed");
                AfterSteam();
            }
            else if (_askedAt != null && DateTime.UtcNow - _askedAt.Value > TimeSpan.FromSeconds(Steam.ExitSeconds))
            {
                _askedAt = null;
                AppendLog($"Steam: still running {Steam.ExitSeconds} s after it was asked to exit");
                _window.Send(Json.Object("type", "steamWait", "phase", "slow"));
            }
        }

        private void StopSteamWait()
        {
            _steamPoll?.Dispose();
            _steamPoll = null;
            _askedAt = null;
        }

        // The download question, before the first connection, and only when Install would
        // download ModFramework. Not remembered: asked every time.
        private void AfterSteam()
        {
            if (_request.Install && _request.Options == null)
            {
                _request.Options = new InstallOptions();
                FrameworkPlan plan = null;
                try
                {
                    // With another loader, Install stops before any download and says why.
                    if (InstallerCore.IsGameFolder(_request.Game) && InstallerCore.OtherLoader(_request.Game).Found == null)
                    {
                        plan = _core.PlanFramework(_request.Game);
                    }
                }
                catch (Exception)
                {
                }
                if (plan != null && plan.Download)
                {
                    _consentPlan = plan;
                    bool swap = _sheet != null;
                    _sheet = "consent";
                    _window.Send(Json.Object("type", "sheet", "sheet", "consent", "swap", swap, "consent", ConsentJson(plan, !InstallerCore.HasBepInEx(_request.Game))));
                    return;
                }
            }
            Go();
        }

        private void ConsentAnswer(string answer)
        {
            if (_sheet != "consent")
            {
                return;
            }
            switch (answer)
            {
                case "download":
                    Go();
                    break;
                case "zip":
                    string file = ConsentDialog.ChooseZip(_window, _consentPlan.Pin.ZipName);
                    if (file != null)
                    {
                        _request.Options.FrameworkZip = file;
                        Go();
                    }
                    break;
                default:
                    _sheet = null;
                    _request = null;
                    _window.Send(Json.Object("type", "sheetClose"));
                    break;
            }
        }

        // ConsentDialog's words: where the installer connects, why, what it sends and how the file is checked.
        private static Json.RawJson ConsentJson(FrameworkPlan plan, bool bepInEx)
        {
            FrameworkPin pin = plan.Pin;
            string and = Strings.Get(Strings.Key.ListAnd);
            string need = plan.Core == null ? Strings.Get(Strings.Key.ConsentNeedMissing, pin.Version)
                : plan.Core < pin.Pinned ? Strings.Get(Strings.Key.ConsentNeedOlder, pin.Version, InstallerCore.ShortVersion(plan.Core))
                : Strings.Get(Strings.Key.ConsentNeedParts, pin.Version);
            string files = $"{pin.ZipName} ({Kilobytes(pin.Size)} KB)" +
                           (bepInEx ? and + $"{Path.GetFileName(new Uri(Paths.BepInExUrl).AbsolutePath)} ({Kilobytes(Paths.BepInExSize)} KB)" : "");
            return Json.Obj(
                "need", need,
                "facts", new[]
                {
                    new[] { Strings.Get(Strings.Key.ConsentWhere), Strings.Get(Strings.Key.ConsentWhereText, Paths.FrameworkRepo + (bepInEx ? and + Paths.BepInExRepo : ""), Paths.ReleaseAssetsHost) },
                    new[] { Strings.Get(Strings.Key.ConsentWhy), Strings.Get(Strings.Key.ConsentWhyText, files) },
                    new[] { Strings.Get(Strings.Key.ConsentSent), Strings.Get(Strings.Key.ConsentSentText, Downloader.UserAgent) },
                    new[] { Strings.Get(Strings.Key.ConsentCheck), Strings.Get(bepInEx ? Strings.Key.ConsentCheckTextBoth : Strings.Key.ConsentCheckText) },
                },
                "note", Strings.Get(bepInEx ? Strings.Key.ConsentNoApiBepInEx : Strings.Key.ConsentNoApi));
        }

        private static long Kilobytes(long bytes) => (bytes + 512) / 1024;

        // ---- the run ----

        private void Go()
        {
            InstallRun.Request run = _request;
            _sheet = null;
            _busy = true;
            _board = "run";
            _closeWhenDone = false;
            _result = null;
            _error = null;
            _errorDetails = null;
            _update = run.Install && _installed != null;
            _cancel = new CancellationTokenSource();
            CancellationToken cancel = _cancel.Token;
            lock (_lines)
            {
                _lines.Clear();
            }
            _rows = run.Install ? InstallRows(run) : UninstallRows(run);
            _runLang = Strings.Current;
            _uninstallPlans = run.Install ? null : UninstallPlans(run);
            _seen.Clear();
            _pct = 0;
            _canStop = run.Install;
            string kind = !run.Install ? "uninstall" : _update ? "update" : "install";
            _window.Send(Json.Object(
                "type", "start",
                "kind", kind,
                "state", Strings.Get(!run.Install ? Strings.Key.WebStateUninstalling : _update ? Strings.Key.WebStateUpdating : Strings.Key.WebStateInstalling),
                "title", Strings.Get(!run.Install ? Strings.Key.WebStateUninstalling : _update ? Strings.Key.WebStateUpdating : Strings.Key.WebStateInstalling),
                "lead", !run.Install ? Strings.Get(Strings.Key.WebWorkUninstall, _manifest.Name)
                    : _update ? Strings.Get(Strings.Key.WebWorkUpdate, _manifest.Name, _manifest.Version)
                    : Strings.Get(Strings.Key.WebWorkInstall, _manifest.Name, _manifest.Version),
                // Uninstalling: the icon from the game folder, read before anything is deleted.
                "icon", Icon(run.Install ? _payload : run.Game),
                "steps", _rows.Select(r => Json.Obj("text", r.Text, "small", r.Small ?? "", "pic", r.Pic ?? "")),
                "cancel", Plain(Strings.Get(Strings.Key.CancelPlain))));
            if (run.Install)
            {
                Show(InstallProgress.At(InstallStage.Check));
            }
            else
            {
                ShowUninstall(run.Launch == LaunchOptionChange.Remove ? UninstallStage.LaunchOption : UninstallStage.Mod);
            }
            var progress = new Progress<InstallProgress>(Show);
            var removing = new Progress<UninstallStage>(ShowUninstall);
            Action launching = () => _window.BeginInvoke((Action)(() => ShowLaunch()));
            Task.Run(() => InstallRun.Execute(_core, run, progress, cancel, AppendLog, launching, removing)).ContinueWith(t => Ended(t.Result),
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        private List<Row> InstallRows(InstallRun.Request run)
        {
            var rows = new List<Row>();
            List<(InstallStage Stage, string Text)> steps;
            FrameworkPlan plan = null;
            try
            {
                steps = _core.ProgressSteps(run.Game, run.Choices, run.Options);
                plan = _core.PlanFramework(run.Game);
            }
            catch (Exception)
            {
                steps = new List<(InstallStage, string)>();
            }
            string no = Strings.Get(Strings.Key.No);
            string host = new Uri(Paths.BepInExUrl).Host;
            bool backup = false;
            foreach (var (stage, text) in steps)
            {
                var row = new Row { Stage = (int)stage, Text = text };
                switch (stage)
                {
                    case InstallStage.Check:
                        Version framework = InstalledFrameworkOrNull(run.Game);
                        string bepInEx = InstallerCore.HasBepInEx(run.Game) ? InstallerCore.DllVersion(Path.Combine(run.Game, "BepInEx", "core", "BepInEx.dll"))?.ToString() : null;
                        row.Text = Strings.Get(Strings.Key.WebStepCheck);
                        row.Small = Strings.Get(Strings.Key.WebStepCheckSmall, framework == null ? no : InstallerCore.ShortVersion(framework), bepInEx ?? no);
                        row.Pic = "scan";
                        break;
                    case InstallStage.DownloadFramework:
                        row.Text = Strings.Get(Strings.Key.WebStepDownload, "ModFramework " + plan?.Pin.Version);
                        row.Small = Strings.Get(Strings.Key.WebStepFrom, host);
                        row.Pic = "dl";
                        _downloads = true;
                        break;
                    case InstallStage.DownloadBepInEx:
                        row.Text = Strings.Get(Strings.Key.WebStepDownload, "BepInEx " + Paths.BepInExVersion);
                        row.Small = Strings.Get(Strings.Key.WebStepFrom, host);
                        row.Pic = "dl";
                        _downloads = true;
                        break;
                    case InstallStage.Verify:
                        row.Small = Strings.Get(Strings.Key.WebStepHashSmall);
                        row.Pic = "chk";
                        break;
                    case InstallStage.Backup:
                        if (!backup)
                        {
                            row.Text = Strings.Get(Strings.Key.WebStepBackup);
                            row.Small = text;
                            row.Pic = "bak";
                            backup = true;
                        }
                        else
                        {
                            // Putting BepInEx in, which Install does in its backup stage.
                            row.BepInEx = true;
                            row.Pic = "loader";
                        }
                        break;
                    case InstallStage.Put:
                        row.Small = Strings.Get(Strings.Key.WebStepPutSmall);
                        row.Pic = _update ? "modup" : "modin";
                        break;
                    case InstallStage.Settings:
                        row.Pic = "set";
                        break;
                }
                rows.Add(row);
            }
            _downloads = rows.Any(r => r.Pic == "dl");
            if (!run.Skipped && (run.Launch == LaunchOptionChange.Add || run.Launch == LaunchOptionChange.Remove))
            {
                bool add = run.Launch == LaunchOptionChange.Add;
                rows.Add(new Row
                {
                    Stage = LaunchStage,
                    Text = Strings.Get(add ? Strings.Key.WebStepLaunchAdd : Strings.Key.WebStepLaunchRemove),
                    Small = Strings.Get(add ? Strings.Key.WebStepLaunchAddSmall : Strings.Key.WebStepLaunchRemoveSmall),
                    Pic = add ? "opt" : "optx",
                });
            }
            return rows;
        }

        // Uninstall's own steps, and first the launch option, which the window takes out
        // before Uninstall starts.
        private List<Row> UninstallRows(InstallRun.Request run)
        {
            List<(UninstallStage Stage, string Text, string Step, string Small)> steps;
            try
            {
                steps = _core.UninstallSteps(run.Game, run.KeepData, run.AlsoBepInEx, run.Launch);
            }
            catch (Exception)
            {
                steps = new List<(UninstallStage, string, string, string)>();
            }
            return steps.OrderBy(s => s.Stage == UninstallStage.LaunchOption ? 0 : 1).Select(s => new Row
            {
                Stage = (int)s.Stage,
                Text = s.Step,
                Small = s.Small,
                Long = s.Text,
                Pic = UninstallPic(s.Stage),
            }).ToList();
        }

        private Dictionary<string, List<string>> UninstallPlans(InstallRun.Request run)
        {
            var plans = new Dictionary<string, List<string>>();
            try
            {
                foreach (string lang in Strings.Languages)
                {
                    Strings.Current = lang;
                    plans[lang] = _core.UninstallPlan(run.Game, run.KeepData, run.AlsoBepInEx, run.Launch);
                }
            }
            catch (Exception)
            {
                plans = null;
            }
            finally
            {
                Strings.Current = _runLang;
            }
            return plans;
        }

        private static string UninstallPic(UninstallStage stage)
        {
            switch (stage)
            {
                case UninstallStage.LaunchOption: return "optx";
                case UninstallStage.Mod: return "modout";
                case UninstallStage.Settings: return "cfgx";
                case UninstallStage.Framework: return "fwx";
                case UninstallStage.Launcher: return "lnx";
                case UninstallStage.InstallerBackup: return "bakx";
                default: return null;
            }
        }

        // Install's progress: the checklist, the card and the footer.
        private void Show(InstallProgress progress)
        {
            if (!_busy || _request == null || !_request.Install)
            {
                return;
            }
            _canStop = progress.CanStop && !_cancel.IsCancellationRequested;
            int stage = (int)progress.Stage;
            bool done = progress.Stage == InstallStage.Done;
            if (done && _rows.Any(r => r.Stage == LaunchStage) && !_seen.Contains(LaunchStage))
            {
                // Install is done and the launch option comes straight after: ShowLaunch moves the list on, so
                // the page never shows its label over the last line's picture.
                return;
            }
            foreach (Row row in _rows)
            {
                row.Mark = done || row.Stage < stage ? "done" : row.Stage == stage ? "now" : "todo";
            }
            if (!done && !_rows.Any(r => r.Mark == "now"))
            {
                // A stage this run has no line for (checking downloads when nothing was downloaded).
                return;
            }
            // Putting BepInEx in is part of the backup stage: its line goes on once the backup's has had a moment.
            Row bep = _rows.FirstOrDefault(r => r.BepInEx);
            if (bep != null && progress.Stage == InstallStage.Backup)
            {
                bep.Mark = "todo";
                var later = new System.Windows.Forms.Timer { Interval = 900 };
                later.Tick += (s, e) =>
                {
                    later.Dispose();
                    if (_busy && _rows.Contains(bep) && bep.Mark == "todo" && _rows.Any(r => r.Stage == (int)InstallStage.Backup && r.Mark == "now"))
                    {
                        foreach (Row row in _rows.Where(r => r.Stage == (int)InstallStage.Backup && !r.BepInEx))
                        {
                            row.Mark = "done";
                        }
                        bep.Mark = "now";
                        Send(Strings.Get(Strings.Key.WebPhasePut), SubGame(), null);
                    }
                };
                later.Start();
            }
            string label, sub;
            double fraction = 0;
            if (progress.Downloading)
            {
                label = Strings.Get(Strings.Key.WebPhaseDownload);
                sub = Strings.Get(Strings.Key.StepDownloading, progress.Index, progress.Count, progress.What, (progress.Done + 512) / 1024, (progress.Total + 512) / 1024);
                fraction = progress.Percent >= 0 ? progress.Percent / 100.0 : 0;
            }
            else
            {
                switch (progress.Stage)
                {
                    case InstallStage.Check:
                        label = Strings.Get(Strings.Key.WebPhaseCheck);
                        sub = SubGame();
                        break;
                    case InstallStage.Verify:
                        label = Strings.Get(Strings.Key.WebPhaseVerify);
                        sub = Strings.Get(Strings.Key.WebStepHashSmall);
                        break;
                    case InstallStage.Backup:
                        label = Strings.Get(Strings.Key.WebPhaseBackup);
                        sub = @"BepInEx\" + Paths.InstallerFolder + @"\backup";
                        break;
                    case InstallStage.Put:
                        label = Strings.Get(Strings.Key.WebPhasePut);
                        sub = SubGame();
                        break;
                    case InstallStage.Settings:
                        label = Strings.Get(Strings.Key.WebPhaseSet);
                        sub = SubGame();
                        break;
                    default:
                        label = Strings.Get(Strings.Key.WebPhaseDone);
                        sub = SubGame();
                        break;
                }
            }
            Send(label, sub, fraction);
        }

        private void ShowLaunch()
        {
            if (!_busy)
            {
                return;
            }
            _seen.Add(LaunchStage);
            foreach (Row row in _rows)
            {
                row.Mark = row.Stage < LaunchStage ? "done" : row.Stage == LaunchStage ? "now" : "todo";
            }
            Send(Strings.Get(Strings.Key.WebPhaseLaunch), SubGame(), 0);
        }

        // Uninstall's progress. The stages come in Uninstall's order, which the list may not
        // follow line by line, so a line is done once its stage has come and gone.
        private void ShowUninstall(UninstallStage stage)
        {
            if (!_busy || _request == null || _request.Install)
            {
                return;
            }
            _canStop = false;
            bool done = stage == UninstallStage.Done;
            // Two lines of one stage (the framework and the save history): the first runs, the second waits its turn.
            bool first = true;
            foreach (Row row in _rows)
            {
                bool runs = row.Stage == (int)stage && first;
                first &= !runs;
                row.Mark = done || _seen.Contains(row.Stage) && row.Stage != (int)stage ? "done" : runs ? "now" : row.Mark == "now" ? "done" : row.Mark;
            }
            _seen.Add((int)stage);
            Send(Strings.Get(done ? Strings.Key.WebPhaseRemoved : Strings.Key.WebPhaseRemove), SubGame(), 0);
        }

        private string SubGame() => Strings.Get(Strings.Key.WebSubGame, Path.GetFileName(_request.Game.TrimEnd('\\', '/')));

        // The page's progress event. fraction: of the current line (a download), or null to keep the percent.
        private void Send(string label, string sub, double? fraction)
        {
            int now = _rows.FindIndex(r => r.Mark == "now");
            bool all = _rows.Count > 0 && _rows.All(r => r.Mark == "done");
            if (fraction != null)
            {
                int pct = _rows.Count == 0 ? 0 : all ? 100 : (int)Math.Round(100.0 * ((now < 0 ? _rows.Count(r => r.Mark == "done") : now) + fraction.Value) / _rows.Count);
                _pct = Math.Max(_pct, Math.Min(100, pct));
            }
            string net = !_request.Install ? Strings.Get(Strings.Key.WebNetUninstall)
                : _cancel != null && _cancel.IsCancellationRequested ? Strings.Get(Strings.Key.WebNetStopping)
                : _canStop ? Strings.Get(_downloads ? Strings.Key.DownloadHint : Strings.Key.WebNetNotYet)
                : Strings.Get(Strings.Key.WebNetWriting);
            _window.Send(Json.Object(
                "type", "progress",
                "marks", _rows.Select(r => r.Mark),
                "pic", now >= 0 ? _rows[now].Pic ?? "" : "",
                "label", label,
                "sub", sub,
                "pct", _pct,
                "canStop", _canStop && _request.Install,
                "net", net));
        }

        // Log lines that the page shows as a picture: Install putting everything back.
        private void Watch(string line)
        {
            if (_busy && line.StartsWith("Copying failed, putting everything back", StringComparison.Ordinal))
            {
                _window?.Send(Json.Object("type", "rollback", "label", Strings.Get(Strings.Key.WebPhaseRollback)));
            }
        }

        // True when a download was stopped; false when the game folder is being changed and it cannot be.
        private bool StopDownload()
        {
            if (_cancel == null || !_canStop || _cancel.IsCancellationRequested)
            {
                return false;
            }
            AppendLog("Window: Cancel pressed");
            _cancel.Cancel();
            _canStop = false;
            _window.Send(Json.Object("type", "stopping"));
            return true;
        }

        private void Ended(InstallRun.Outcome outcome)
        {
            _busy = false;
            _cancel?.Dispose();
            _cancel = null;
            if (_broken)
            {
                GiveUp("WebView2 stopped working during the run");
                return;
            }
            if (_closeWhenDone)
            {
                Finish();
                return;
            }
            bool install = _request.Install;
            if (outcome.Cancelled)
            {
                Refresh();
                SendSetup(Strings.Get(Strings.Key.Cancelled));
                return;
            }
            if (outcome.Error == null)
            {
                _result = outcome.Result;
                Refresh();
                SendDone(false);
                return;
            }
            _error = outcome.Error;
            _errorDetails = outcome.Details + Environment.NewLine + InstallRun.AboutThisRun(_manifest);
            Refresh();
            SendFailed(false);
        }

        private void SendDone(bool relang)
        {
            _board = "done";
            bool install = _request.Install;
            List<(bool Done, string Text)> summary = install ? _core.Summary(_result) : UninstallSummary();
            // The launcher shows first when the game starts, if it's in the launch options now.
            bool launcher = install && _found && Steam.State(_request.Game).AnyHas;
            string[] lines;
            lock (_lines)
            {
                lines = _lines.ToArray();
            }
            _window.Send(Json.Object(
                "type", "done",
                "relang", relang,
                "kind", !install ? "uninstall" : _update ? "update" : "install",
                "state", Strings.Get(!install ? Strings.Key.WebStateUninstalled : _update ? Strings.Key.WebStateUpdated : Strings.Key.WebStateInstalled),
                "title", Strings.Get(!install ? Strings.Key.WebDoneUninstall : _update ? Strings.Key.WebDoneUpdate : Strings.Key.WebDoneInstall),
                "lead", Strings.Get(install ? Strings.Key.WebDoneLead : Strings.Key.WebDoneLeadUninstall),
                "summary", summary.Select(l => Json.Obj("done", l.Done, "text", l.Text)),
                "card", Strings.Get(install ? Strings.Key.WebReady : Strings.Key.WebUninstalledCard),
                "cardSmall", install ? Strings.Get(launcher ? Strings.Key.WebReadyLauncher : Strings.Key.WebReadySteam) : Strings.Get(Strings.Key.WebUninstalledSmall),
                "label", Strings.Get(install ? Strings.Key.WebPhaseDone : Strings.Key.WebPhaseRemoved),
                "sub", SubGame(),
                "log", lines));
        }

        // Uninstall's plan as it was shown, in the page's language now: each line done (the
        // run had it in its checklist) or not (what was kept).
        private List<(bool Done, string Text)> UninstallSummary()
        {
            List<string> ran = null, now = null;
            if (_uninstallPlans?.TryGetValue(_runLang, out ran) != true)
            {
                return _rows.Select(r => (true, r.Long)).ToList();
            }
            if (_uninstallPlans.TryGetValue(Strings.Current, out now) != true || now.Count != ran.Count)
            {
                now = ran;
            }
            return ran.Select((line, i) => (_rows.Any(r => r.Long == line), now[i])).ToList();
        }

        private void SendFailed(bool relang)
        {
            _board = "failed";
            _window.Send(Json.Object("type", "failed", "relang", relang, "failed", FailedJson(_error, _errorDetails, _request.Install, false)));
        }

        // The failed board: ErrorDialog's headline and advice, the mark, what became of the
        // game folder, the framework's way by hand, and the details (English).
        private Json.RawJson FailedJson(Exception error, string details, bool install, bool start)
        {
            var (headline, help) = ErrorDialog.Describe(error);
            var known = error as InstallerException;
            Strings.Key? key = known?.Key;
            string pill = null, pillKind = null;
            if (key == Strings.Key.RolledBack)
            {
                pill = Strings.Get(Strings.Key.WebPillBack, known.Args.Length > 0 ? known.Args[0] : "");
                pillKind = "ok";
            }
            else if (key == Strings.Key.RolledBackPartly)
            {
                pill = Strings.Get(Strings.Key.WebPillPartly, known.Args.Length > 0 ? known.Args[0] : "");
                pillKind = "warn";
            }
            else if (!start && (install ? !(ErrorDialog.Describe(error).Headline == Strings.Get(Strings.Key.SomethingWrong) && known == null)
                                        : key == Strings.Key.Running || key == Strings.Key.NotFound))
            {
                // Install changes nothing until it has everything, and puts everything back
                // when copying fails; Uninstall only before it starts.
                pill = Strings.Get(Strings.Key.WebPillSame);
                pillKind = "ok";
            }
            FrameworkHelp framework = known?.Framework;
            return Json.Obj(
                "state", Strings.Get(start ? Strings.Key.WebStateCantStart : Strings.Key.WebStateFailed),
                "mark", Mark(error),
                "headline", headline,
                "help", help ?? "",
                "pill", pill,
                "pillKind", pillKind,
                "fw", framework == null ? null : Json.Obj(
                    "manual", Strings.Get(Strings.Key.FwManualHelp),
                    "release", Strings.Get(Strings.Key.FwReleaseLink, framework.ReleasePage.Substring("https://".Length)),
                    "keep", framework.KeepVersion == null ? null : Strings.Get(Strings.Key.FwKeepLink, framework.KeepVersion)),
                "details", details ?? "",
                "start", start);
        }

        // The failure mark for the page (webui/pics.js): the GitHub mark with its × when
        // the internet can't be reached, a picture of what went wrong, or the "!".
        private static string Mark(Exception error)
        {
            switch ((error as InstallerException)?.Key)
            {
                case Strings.Key.NetOffline: return "gh";
                case Strings.Key.NetBusy:
                case Strings.Key.NetTls:
                case Strings.Key.DownloadFailed:
                case Strings.Key.FwDownloadFailed: return "busy";
                case Strings.Key.NetLimited:
                case Strings.Key.NetLimitedLater: return "limited";
                case Strings.Key.FwNotPublished: return "notfound";
                case Strings.Key.FwHash:
                case Strings.Key.FwSize:
                case Strings.Key.FwLocalMismatch:
                case Strings.Key.BepInExHash: return "mismatch";
                case Strings.Key.RolledBack: return "rolled";
                case Strings.Key.RolledBackPartly: return "partly";
                case Strings.Key.Running: return "running";
                case Strings.Key.OtherLoader:
                case Strings.Key.OtherLoaderNamed: return "otherloader";
                case Strings.Key.CannotWrite: return "lock";
                case Strings.Key.BadZip:
                case Strings.Key.FwZipLacks:
                case Strings.Key.FwZipTooOld:
                case Strings.Key.NoPayload:
                case Strings.Key.BadManifest: return "broken";
                case null:
                    return error is System.Net.Http.HttpRequestException || error is System.Net.WebException ? "busy"
                        : error is UnauthorizedAccessException || error is IOException ? "lock"
                        : error is InvalidDataException ? "broken"
                        : "!";
                default: return "!";
            }
        }

        // Try again, Choose zip... and "Just install the mod" on the failed board.
        private void Again(string how)
        {
            if (_request == null || _error == null)
            {
                return;
            }
            InstallOptions options = _request.Options;
            FrameworkHelp framework = (_error as InstallerException)?.Framework;
            switch (how)
            {
                case "zip":
                    if (framework == null)
                    {
                        return;
                    }
                    string file = ConsentDialog.ChooseZip(_window, framework.ZipName);
                    if (file == null)
                    {
                        return;
                    }
                    // That zip instead of the download.
                    options = (options ?? new InstallOptions()).Copy();
                    options.FrameworkZip = file;
                    options.KeepFramework = false;
                    break;
                case "keep":
                    if (framework?.KeepVersion == null)
                    {
                        return;
                    }
                    // The installed framework stays as it is.
                    options = (options ?? new InstallOptions()).Copy();
                    options.KeepFramework = true;
                    options.FrameworkZip = null;
                    break;
            }
            Begin(_request.Install, options);
        }
    }
}
