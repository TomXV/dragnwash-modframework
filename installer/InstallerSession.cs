using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DragNWash.Installer
{
    // The window in WebView2 from start to close: everything MainForm does, with the page
    // drawing it (installer/web). The page gets its words from Strings.cs and the state of
    // the setup board as a whole each time something changes; it sends back what was
    // clicked. Everything here runs on the window's thread, except the run itself
    // (InstallRun.Execute). The Steam and download questions are sheets over the setup
    // board; the run, its end and its failures are boards of their own
    // (InstallerSession.Run.cs).
    internal sealed partial class InstallerSession
    {
        private static readonly string UserData = Path.Combine(WebUi.Folder, "WebView2");

        // How long the page may take to come up before the WinForms window opens instead.
        private static readonly TimeSpan PageWait = TimeSpan.FromSeconds(12);

        private readonly ModManifest _manifest;
        private readonly InstallerException _startError;
        private readonly InstallerCore _core;
        private readonly string _payload;
        private readonly List<string> _lines = new List<string>();
        private InstallerWindow _window;
        private ApplicationContext _context;

        // Why the window couldn't be shown, for the WinForms window's log; null once it was.
        private string _failure;
        private bool _shown;

        // The setup board: the game folder and what was chosen on it.
        private string _game = "";
        private bool _found;
        private string _installed;
        private Steam.LaunchState _steam = new Steam.LaunchState();
        private string _launchDefaultFor;
        private bool _launchTicked = true;
        private bool _uninstall;
        private readonly Dictionary<string, string> _choices = new Dictionary<string, string>();
        private bool _keepData = true;
        private bool _alsoBepInEx;

        // Which board the page shows: setup, run, done or failed (for a new language).
        private string _board = "setup";

        private InstallerSession(ModManifest manifest, InstallerException startError, string payload)
        {
            _manifest = manifest;
            _startError = startError;
            _payload = payload;
            _core = manifest == null ? null : new InstallerCore(manifest, payload, AppendLog);
        }

        // Runs the window until it closes: null. Otherwise why the page didn't come up, and
        // then nothing has been done and the window has gone again.
        internal static string Run(string loader, ModManifest manifest, InstallerException startError, string payload)
        {
            var session = new InstallerSession(manifest, startError, payload);
            return session.Show(loader);
        }

        private string Show(string loader)
        {
            if (_manifest != null)
            {
                _game = InstallerCore.FindGame() ?? "";
                Refresh();
            }
            _window = new InstallerWindow(Title(), Init, AppendLog);
            _window.Message += message =>
            {
                try
                {
                    OnMessage(message);
                }
                catch (Exception ex)
                {
                    AppendLog("Window: " + message.Cmd + " failed: " + ex);
                }
            };
            _window.CloseAsked += () => OnMessage(new PageMessage { Cmd = "close" });
            _window.Broken += () =>
            {
                // The page's own processes went away. Without a run the WinForms window
                // takes over; during one, it does once the run has ended.
                _broken = true;
                if (!_busy)
                {
                    GiveUp("WebView2 stopped working");
                }
            };
            _context = new ApplicationContext();
            var wait = new System.Windows.Forms.Timer { Interval = (int)PageWait.TotalMilliseconds };
            wait.Tick += (s, e) =>
            {
                wait.Dispose();
                if (!_shown)
                {
                    GiveUp("the page did not come up within " + PageWait.TotalSeconds + " s");
                }
            };
            wait.Start();
            _window.Ready.ContinueWith(t =>
            {
                if (t.Result)
                {
                    _shown = true;
                }
                else if (!_shown)
                {
                    GiveUp(_lines.LastOrDefault(l => l.StartsWith("Window:", StringComparison.Ordinal)) ?? "the page did not come up");
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
            _window.Begin(loader, UserData);
            Application.Run(_context);
            wait.Dispose();
            try
            {
                _window.Dispose();
            }
            catch (Exception)
            {
            }
            return _failure;
        }

        // The page can't be had: the window goes and the WinForms one takes over.
        private void GiveUp(string why)
        {
            if (_failure != null)
            {
                return;
            }
            _failure = why;
            _window.Gone();
            _context.ExitThread();
        }

        // Closes the window for good (the page fades out first).
        private async void Finish()
        {
            if (_broken)
            {
                _window.Gone();
            }
            else
            {
                await _window.FadeOut();
            }
            _context.ExitThread();
        }

        private string Title()
        {
            return _manifest == null ? "Drag'n Wash Mod Installer" : Strings.Get(Strings.Key.Title, _manifest.Name);
        }

        private void AppendLog(string line)
        {
            lock (_lines)
            {
                _lines.Add(line);
            }
            _window?.Send(Json.Object("type", "log", "text", line));
            Watch(line);
        }

        // ---- the page's first event ----

        private string Init()
        {
            string lettering = "write", bar = "edge";
            if (_found)
            {
                try
                {
                    string cfg = Path.Combine(_game, "BepInEx", "config", Paths.FrameworkConfigPrefix + ".cfg");
                    lettering = string.Equals(ConfigFile.Get(cfg, "Launcher", "Logo lettering"), "Typewriter", StringComparison.OrdinalIgnoreCase) ? "type" : "write";
                    bar = string.Equals(ConfigFile.Get(cfg, "Launcher", "Progress bar"), "Under text", StringComparison.OrdinalIgnoreCase) ? "text" : "edge";
                }
                catch (Exception)
                {
                    // Unreadable: the defaults, as the launcher does.
                }
            }
            return Json.Object(
                "type", "init",
                "lang", Strings.Current,
                "langs", Strings.Languages.Select(code => Json.Obj("code", code, "name", Strings.LanguageName(code))),
                "texts", Texts(),
                "lettering", lettering,
                "bar", bar,
                "mod", _manifest == null ? null : Json.Obj("name", _manifest.Name, "version", _manifest.Version, "icon", Icon(_payload),
                    "website", Website() != null),
                "found", _found,
                "setup", _manifest == null ? null : SetupJson(),
                "failed", _startError == null ? null : FailedJson(_startError, StartDetails(), false, true));
        }

        // Every text of the installer in its language, without the WinForms' & mnemonics.
        private Json.RawJson Texts()
        {
            var pairs = new List<object>();
            foreach (Strings.Key key in Enum.GetValues(typeof(Strings.Key)))
            {
                pairs.Add(key.ToString());
                pairs.Add(Plain(Strings.Get(key)));
            }
            return Json.Obj(pairs.ToArray());
        }

        // "&Install" and "インストール(&I)" as the page shows them.
        internal static string Plain(string text)
        {
            text = Regex.Replace(text, @"\(&\w\)", "");
            return Regex.Replace(text, "&(.)", "$1");
        }

        // A heading's colon, gone ("Install will:" → "Install will").
        private static string NoColon(string text) => Plain(text).TrimEnd(':', '：', ' ');

        // The mod's icon as a data: URL, or "" (the page then draws the letter tile).
        private string Icon(string root)
        {
            try
            {
                return ModIcon.DataUrl(_core?.ModIconPath(root)) ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private string Website()
        {
            string site = _manifest?.Website;
            return !string.IsNullOrEmpty(site) && site.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? site : null;
        }

        private string StartDetails()
        {
            return _startError.Text(english: true) + (_startError.Detail == null ? "" : Environment.NewLine + _startError.Detail) + Environment.NewLine + InstallRun.AboutThisRun(null);
        }

        // ---- the setup board ----

        // Reads the game folder again: what's there, the Steam launch options, the defaults.
        private void Refresh()
        {
            string game = _game.Trim();
            _found = InstallerCore.IsGameFolder(game);
            _installed = _found ? _core.InstalledVersion(game) : null;
            if (_installed == null)
            {
                _uninstall = false;
            }
            _steam = _found ? Steam.State(game) : new Steam.LaunchState();
            // Only when the folder changes, so it never undoes the player's own click.
            if (_found && !string.Equals(_launchDefaultFor, game, StringComparison.OrdinalIgnoreCase))
            {
                _launchDefaultFor = game;
                _launchTicked = InstallRun.LaunchDefault(_steam, game);
            }
            if (_found)
            {
                foreach (ModChoice choice in _manifest.Choices)
                {
                    if (!_choices.ContainsKey(choice.Id))
                    {
                        string value = choice.DefaultValue(_core.ReadConfig(game, choice.Config));
                        if (choice.Options.Any(o => o.Value == value))
                        {
                            _choices[choice.Id] = value;
                        }
                    }
                }
            }
        }

        private bool LaunchUsable => InstallRun.LaunchUsable(_core, _steam, _found, _game.Trim());

        private LaunchOptionChange LaunchChange(bool install)
        {
            return InstallRun.LaunchChange(_core, _steam, install, _game.Trim(), _launchTicked && LaunchUsable);
        }

        // The other mod loader in the folder: (its name or null, what shows it), or nulls.
        private (string Name, string Found) OtherLoader()
        {
            try
            {
                return _found ? InstallerCore.OtherLoader(_game.Trim()) : (null, null);
            }
            catch (Exception)
            {
                return (null, null);
            }
        }

        private void SendSetup(string banner = null)
        {
            _board = "setup";
            _window.Send(Json.Object("type", "setup", "setup", SetupJson(), "banner", banner));
        }

        private Json.RawJson SetupJson()
        {
            string game = _game.Trim();
            bool install = !_uninstall;
            var (loaderName, loaderFound) = OtherLoader();
            bool blocked = install && loaderFound != null;

            // The plan, with the lines that go online marked.
            var plan = new List<object>();
            bool online = false, asks = false;
            if (_found)
            {
                try
                {
                    LaunchOptionChange launch = LaunchChange(install);
                    List<string> lines = install
                        ? _core.InstallPlan(game, _choices, launch)
                        : _core.UninstallPlan(game, _keepData, _alsoBepInEx, launch);
                    string host = new Uri(Paths.BepInExUrl).Host;
                    FrameworkPlan framework = install && !blocked ? _core.PlanFramework(game) : null;
                    var net = new HashSet<string> { Strings.Get(Strings.Key.PlanDownloadBepInEx, Paths.BepInExVersion, host, Paths.ReleaseAssetsHost) };
                    if (framework != null)
                    {
                        net.Add(Strings.Get(Strings.Key.PlanDownloadFramework, framework.Pin.Version, host, Paths.ReleaseAssetsHost));
                        asks = framework.Download;
                    }
                    string stop = loaderName == null ? Strings.Get(Strings.Key.PlanOtherLoader) : Strings.Get(Strings.Key.PlanOtherLoaderNamed, loaderName);
                    foreach (string line in lines)
                    {
                        bool goesOnline = install && net.Contains(line);
                        online |= goesOnline;
                        plan.Add(Json.Obj("text", line, "net", goesOnline,
                            "cls", blocked && line == stop ? "stop" : line == Strings.Get(Strings.Key.PlanNothingElse) ? "last" : ""));
                    }
                }
                catch (Exception)
                {
                    // A folder that cannot be read; Install or Uninstall will say why.
                }
            }

            string head, lead, button;
            if (!install)
            {
                head = Strings.Get(Strings.Key.WebHeadUninstall, _manifest.Name);
                lead = Strings.Get(Strings.Key.WebLeadUninstall);
                button = Plain(Strings.Get(Strings.Key.UninstallButton));
            }
            else if (_installed != null)
            {
                head = _installed == _manifest.Version ? Strings.Get(Strings.Key.WebHeadReinstall, _manifest.Version)
                    : Strings.Get(Strings.Key.WebHeadUpdate, _installed, _manifest.Version);
                lead = Strings.Get(Strings.Key.WebLeadUpdate);
                button = Plain(Strings.Get(Strings.Key.UpdateButton));
            }
            else
            {
                head = Strings.Get(Strings.Key.WebHeadInstall, _manifest.Name);
                lead = Strings.Get(Strings.Key.WebLeadInstall);
                button = Plain(Strings.Get(Strings.Key.InstallButton));
            }
            if (!_found)
            {
                lead = Strings.Get(Strings.Key.WebLeadNotFound);
            }

            string why = !_found ? "" : _steam.Accounts == 0 ? Strings.Get(Strings.Key.WebLaunchNoSteam)
                : !LaunchUsable ? Strings.Get(Strings.Key.WebLaunchNoLauncher) : "";
            string netText = blocked ? Strings.Get(Strings.Key.WebOtherFoot)
                : !install || !online ? Strings.Get(Strings.Key.WebNetNone)
                : asks ? Strings.Get(Strings.Key.WebNetAsk) : Strings.Get(Strings.Key.WebNetLines);
            Version framework2 = _found ? InstalledFrameworkOrNull(game) : null;
            return Json.Obj(
                "game", game,
                "found", _found,
                "bep", _found && InstallerCore.HasBepInEx(game),
                "fw", framework2 == null ? null : InstallerCore.ShortVersion(framework2),
                "installed", _installed,
                "mode", install ? "install" : "uninstall",
                "canUninstall", _installed != null,
                "head", head,
                "lead", lead,
                "choices", _manifest.Choices.Select(c => Json.Obj(
                    "id", c.Id,
                    "label", c.LabelFor(Strings.Current),
                    "value", _choices.TryGetValue(c.Id, out string v) ? v : "",
                    "options", c.Options.Select(o => Json.Obj("value", o.Value, "name", o.Name ?? o.Value)))),
                "launch", Json.Obj("on", _launchTicked, "usable", LaunchUsable, "why", why),
                "keepData", _keepData,
                "alsoBep", _alsoBepInEx,
                "will", NoColon(Strings.Get(!install ? Strings.Key.UninstallWill : _installed != null ? Strings.Key.WebUpdateWill : Strings.Key.InstallWill)),
                "plan", plan,
                // Installing: why Windows may warn about this file. Uninstalling: the other way to do it.
                "hint", Strings.Get(!install ? Strings.Key.SteamLaunchHint : _core.FetchesFramework ? Strings.Key.SmartScreenHintFramework : Strings.Key.SmartScreenHint),
                "net", netText,
                "blocked", blocked,
                "other", loaderFound == null || !install ? null : Json.Obj(
                    "title", Strings.Get(Strings.Key.WebOtherTitle),
                    "text", loaderName == null ? Strings.Get(Strings.Key.WebOtherText) : Strings.Get(Strings.Key.WebOtherTextNamed, loaderName)),
                "button", button);
        }

        private static Version InstalledFrameworkOrNull(string game)
        {
            try
            {
                return InstallerCore.InstalledFramework(game);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---- what the page sends ----

        private void OnMessage(PageMessage message)
        {
            switch (message.Cmd)
            {
                case "close":
                    Close();
                    return;
                case "lang":
                    if (!_busy && Strings.Languages.Contains(message.Value) && message.Value != Strings.Current)
                    {
                        Strings.Current = message.Value;
                        _window.Text = Title();
                        _window.Send(Json.Object("type", "texts", "lang", Strings.Current, "texts", Texts()));
                        Relang();
                    }
                    return;
                case "open":
                    Open(message.Value);
                    return;
                case "copy":
                    Copy();
                    return;
            }
            if (_manifest == null)
            {
                // Only the failure board: Close and Copy details.
                return;
            }
            if (_busy)
            {
                if (message.Cmd == "cancel")
                {
                    StopDownload();
                }
                return;
            }
            switch (message.Cmd)
            {
                case "browse":
                    Browse();
                    break;
                case "mode":
                    _uninstall = message.Value == "uninstall" && _installed != null;
                    SendSetup();
                    break;
                case "choice":
                    ModChoice choice = _manifest.Choices.FirstOrDefault(c => c.Id == message.Id);
                    if (choice != null && choice.Options.Any(o => o.Value == message.Value))
                    {
                        _choices[choice.Id] = message.Value;
                        SendSetup();
                    }
                    break;
                case "launch":
                    _launchTicked = message.On;
                    SendSetup();
                    break;
                case "keepData":
                    _keepData = message.On;
                    SendSetup();
                    break;
                case "alsoBep":
                    _alsoBepInEx = message.On;
                    SendSetup();
                    break;
                case "run":
                    Begin(!_uninstall, null);
                    break;
                case "steam":
                    SteamAnswer(message.Value);
                    break;
                case "consent":
                    ConsentAnswer(message.Value);
                    break;
                case "retry":
                case "zip":
                case "keep":
                    Again(message.Cmd);
                    break;
                case "back":
                    Refresh();
                    SendSetup();
                    break;
                case "start":
                    StartGame();
                    Finish();
                    break;
            }
        }

        // The page in another language: the board on the screen again, without its entrance.
        private void Relang()
        {
            if (_manifest == null)
            {
                _window.Send(Json.Object("type", "failed", "failed", FailedJson(_startError, StartDetails(), false, true), "relang", true));
                return;
            }
            switch (_board)
            {
                case "done":
                    SendDone(true);
                    break;
                case "failed":
                    SendFailed(true);
                    break;
                default:
                    _window.Send(Json.Object("type", "setup", "setup", SetupJson(), "relang", true));
                    break;
            }
        }

        private void Close()
        {
            if (_busy)
            {
                // Closing mid-download stops it and closes once the game folder is known
                // to be untouched; while it is being changed, it waits.
                _closeWhenDone |= StopDownload();
                return;
            }
            StopSteamWait();
            Finish();
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = Strings.Get(Strings.Key.NotFound), ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(_window) == DialogResult.OK)
                {
                    _game = dialog.SelectedPath;
                    Refresh();
                    SendSetup();
                }
            }
        }

        private void Open(string what)
        {
            string url = what == "website" ? Website()
                : what == "release" && _consentPlan != null ? Paths.FrameworkReleasePage(_consentPlan.Pin.Version)
                : what == "fwRelease" ? (_error as InstallerException)?.Framework?.ReleasePage
                : null;
            if (url != null)
            {
                ConsentDialog.OpenPage(url);
            }
        }

        private void Copy()
        {
            string text = _manifest == null ? StartDetails() : _errorDetails;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            try
            {
                Clipboard.SetText(text);
                _window.Send(Json.Object("type", "copied"));
            }
            catch (Exception ex)
            {
                // Another program holds the clipboard; the text can still be selected in Details.
                AppendLog("Window: could not copy: " + ex.Message);
            }
        }

        // Through steam.exe with a fresh environment, as the launcher starts the game: when
        // the launch option is set, the launcher shows first.
        private void StartGame()
        {
            string address = "steam://rungameid/" + Paths.SteamAppId;
            try
            {
                string steam = Steam.Exe();
                if (steam != null)
                {
                    Steam.StartFresh(steam, address);
                }
                else
                {
                    Process.Start(new ProcessStartInfo(address) { UseShellExecute = true })?.Dispose();
                }
                AppendLog("Game: asked Steam to start it (" + address + ")");
            }
            catch (Exception ex)
            {
                AppendLog("Game: Steam could not be asked to start it: " + ex.Message);
            }
        }
    }
}
