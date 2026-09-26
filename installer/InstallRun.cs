using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DragNWash.Installer
{
    // What the installer's two windows (MainForm, and the page in InstallerSession) share
    // that isn't drawing: the launch option box's rules, what the chosen action does with
    // the game's launch options in Steam, and the run itself once everything is answered.
    internal static class InstallRun
    {
        // A launcher already in the game folder but in no launch option was left out on
        // purpose last time, so the box starts unticked for that folder; otherwise ticked.
        internal static bool LaunchDefault(Steam.LaunchState steam, string game)
        {
            return steam.AnyHas || !File.Exists(Paths.Launcher(game));
        }

        // The box does something only with a Steam account to set it in and a launcher to start.
        internal static bool LaunchUsable(InstallerCore core, Steam.LaunchState steam, bool found, string game)
        {
            return found && steam.Accounts > 0 && core.LauncherAfterInstall(game);
        }

        // What Install or Uninstall is to do with the game's launch options in Steam.
        // Ticked (and usable): put the launcher in, unless it is there already. Unticked:
        // take it out if it is there. Uninstall takes it out when the framework (and so
        // the launcher) goes. When the launcher won't be there after Install, a launch
        // option that starts it is taken out too: the game would not start from Steam otherwise.
        internal static LaunchOptionChange LaunchChange(InstallerCore core, Steam.LaunchState steam, bool install, string game, bool ticked)
        {
            if (steam.Accounts == 0)
            {
                return LaunchOptionChange.None;
            }
            if (!install)
            {
                return steam.AnyHas && core.RemovesFramework(game) ? LaunchOptionChange.Remove : LaunchOptionChange.None;
            }
            if (ticked)
            {
                return steam.AllSet ? LaunchOptionChange.AlreadySet : LaunchOptionChange.Add;
            }
            return steam.AnyHas ? LaunchOptionChange.Remove : LaunchOptionChange.None;
        }

        // The last lines of "Copy details": enough to reproduce a report.
        internal static string AboutThisRun(ModManifest manifest)
        {
            string mod = manifest == null ? "Drag'n Wash Mod Installer" : $"{manifest.Name} {manifest.Version}";
            return $"{mod}, Install.exe {typeof(InstallRun).Assembly.GetName().Version.ToString(3)}" + Environment.NewLine +
                   $"Installer language: {Strings.Current}   Windows {Environment.OSVersion.Version}   {RuntimeInformation.FrameworkDescription}";
        }

        // What the window asked for, once the Steam and download questions are answered.
        internal sealed class Request
        {
            internal bool Install;
            internal string Game;
            internal Dictionary<string, string> Choices;
            internal InstallOptions Options;
            internal LaunchOptionChange Launch;
            // The player chose "Skip this option" while Steam was running.
            internal bool Skipped;
            // The steam.exe the installer asked to exit, to start again at the end; or null.
            internal string ClosedSteam;
            internal bool KeepData;
            internal bool AlsoBepInEx;
        }

        internal sealed class Outcome
        {
            internal bool Cancelled;
            internal Exception Error;
            // "Copy details" for the error, in English (Details below adds the run's own lines).
            internal string Details;
            internal InstallResult Result;
        }

        // The run itself, on a thread of its own. launching: just before the launch options
        // change after Install (the page shows that as a step of its own); removing hears
        // Uninstall's steps.
        internal static Outcome Execute(InstallerCore core, Request run, IProgress<InstallProgress> progress, CancellationToken cancel, Action<string> log,
            Action launching = null, IProgress<UninstallStage> removing = null)
        {
            // .NET's own messages in the log and the error details stay English too.
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            try
            {
                InstallResult result = null;
                string game = run.Game;
                if (run.Install)
                {
                    result = core.Install(game, run.Choices, run.Options, progress, cancel);
                    // Once the files are in place, so the launcher is there to be started.
                    if (run.Launch == LaunchOptionChange.Add || run.Launch == LaunchOptionChange.Remove)
                    {
                        launching?.Invoke();
                    }
                    result.LaunchOption = run.Skipped ? LaunchOptionOutcome.Skipped
                        : run.Launch == LaunchOptionChange.Add ? Steam.Apply(game, true, log)
                        : run.Launch == LaunchOptionChange.Remove ? Steam.Apply(game, false, log)
                        : LaunchOptionOutcome.None;
                }
                else
                {
                    // Out of the launch options before the launcher goes, and the launcher
                    // stays while they still start it.
                    InstallerCore.CheckReady(game);
                    if (run.Launch == LaunchOptionChange.Remove)
                    {
                        Steam.Apply(game, false, log);
                    }
                    bool keepLauncher = core.RemovesFramework(game) && Steam.State(game).AnyHas;
                    core.Uninstall(game, run.KeepData, run.AlsoBepInEx, keepLauncher, removing);
                }
                // Steam, if the installer closed it, is started again once all went well.
                if (run.ClosedSteam != null)
                {
                    Steam.Start(run.ClosedSteam, log);
                }
                return new Outcome { Result = result };
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                log("Cancelled; the game folder was not changed");
                return new Outcome { Cancelled = true };
            }
            catch (InstallerException ex)
            {
                log("ERROR: " + (ex.LogText ?? ex.Message));
                return new Outcome { Error = ex, Details = ErrorDialog.Details(ex) };
            }
            catch (Exception ex)
            {
                log("ERROR: " + ex);
                return new Outcome { Error = ex, Details = ErrorDialog.Details(ex) };
            }
        }
    }
}
