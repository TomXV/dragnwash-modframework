using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DragNWash.Installer
{
    // Double-click: the window. From a terminal, without a window (how it is tested):
    // see Usage below. On the command line, --install is the consent to download what
    // is missing; the window asks first.
    internal static class Program
    {
        private const string Usage =
            "Install.exe                    the window\n" +
            "Install.exe --install [--game-dir <folder>] [--choice <id>=<value>]...\n" +
            "            [--bepinex-zip <file>] [--framework-zip <file>] [--no-download] [--keep-framework]\n" +
            "            [--launch-option on|off|keep]\n" +
            "Install.exe --uninstall [--game-dir <folder>] [--remove-data] [--remove-bepinex] [--launch-option keep]\n" +
            "\n" +
            "  --bepinex-zip <file>    use this BepInEx zip instead of downloading it (checked against the pinned SHA-256)\n" +
            "  --framework-zip <file>  use this ModFramework zip instead of downloading it (checked against the size\n" +
            "                          and SHA-256 in mod-install.json; no network)\n" +
            "  --no-download           never go online; stop, with nothing changed, when BepInEx or ModFramework\n" +
            "                          would have to be downloaded\n" +
            "  --keep-framework        keep the installed ModFramework when it meets the mod's minimums, and install\n" +
            "                          only the mod\n" +
            "  --launch-option on|off|keep\n" +
            "                          the update launcher in the game's launch options in Steam. on (the default\n" +
            "                          for --install) puts it in front of %command% and keeps what is there, off\n" +
            "                          takes it out, keep leaves the launch options alone. --uninstall takes it out\n" +
            "                          when ModFramework goes, unless keep. Steam must be closed: while it runs, the\n" +
            "                          launch options are left as they are and the log says so. Steam is never closed\n" +
            "                          from the command line.\n" +
            "  --log <file>            also append the log to a file\n" +
            "  --help                  this text\n";

        [STAThread]
        private static int Main(string[] args)
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            if (args.Length == 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                ModManifest manifest;
                try
                {
                    manifest = LoadManifest(here);
                }
                catch (InstallerException ex)
                {
                    MessageBox.Show(ex.Text() + (ex.Detail == null ? "" : Environment.NewLine + Environment.NewLine + ex.Detail),
                        "Drag'n Wash Mod Installer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                Application.Run(new MainForm(manifest, here));
                return 0;
            }
            return RunCommandLine(args, here);
        }

        internal static ModManifest LoadManifest(string folder)
        {
            string path = Path.Combine(folder, ModManifest.FileName);
            if (!File.Exists(path))
            {
                throw new InstallerException(Strings.Key.NoPayload);
            }
            try
            {
                return ModManifest.Load(path);
            }
            catch (Exception ex) when (!(ex is InstallerException))
            {
                throw new InstallerException(Strings.Key.BadManifest, ex.Message);
            }
        }

        private static int RunCommandLine(string[] args, string here)
        {
            string action = null, game = null, logFile = null, launchOption = null;
            var options = new InstallOptions();
            bool removeData = false, removeBepInEx = false;
            var choices = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
                switch (a)
                {
                    case "--install": action = "install"; break;
                    case "--uninstall": action = "uninstall"; break;
                    case "--game-dir": game = Next(); break;
                    case "--bepinex-zip": options.BepInExZip = Next(); break;
                    case "--framework-zip": options.FrameworkZip = Next(); break;
                    case "--no-download": options.NoDownload = true; break;
                    case "--keep-framework": options.KeepFramework = true; break;
                    case "--help":
                    case "-h":
                    case "/?":
                        Console.Out.Write(Usage.Replace("\n", Environment.NewLine));
                        return 0;
                    case "--log": logFile = Next(); break;
                    case "--remove-data": removeData = true; break;
                    case "--remove-bepinex": removeBepInEx = true; break;
                    case "--launch-option":
                        launchOption = Next();
                        if (launchOption != "on" && launchOption != "off" && launchOption != "keep")
                        {
                            Console.Error.WriteLine("--launch-option takes on, off or keep");
                            return 2;
                        }
                        break;
                    case "--choice":
                        string pair = Next();
                        int eq = pair.IndexOf('=');
                        if (eq <= 0) throw new ArgumentException("--choice needs <id>=<value>");
                        choices[pair.Substring(0, eq)] = pair.Substring(eq + 1);
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown option {a}");
                        Console.Error.Write(Usage.Replace("\n", Environment.NewLine));
                        return 2;
                }
            }

            void Log(string line)
            {
                Console.Out.WriteLine(line);
                if (logFile != null)
                {
                    File.AppendAllText(logFile, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }

            try
            {
                ModManifest manifest = LoadManifest(here);
                var core = new InstallerCore(manifest, here, Log);
                game = game ?? InstallerCore.FindGame();
                if (game == null)
                {
                    throw new InstallerException(Strings.Key.NotFound);
                }
                if (action == "install")
                {
                    foreach (ModChoice choice in manifest.Choices)
                    {
                        if (!choices.ContainsKey(choice.Id))
                        {
                            choices[choice.Id] = choice.DefaultValue(core.ReadConfig(game, choice.Config));
                        }
                    }
                    core.Install(game, choices, options);
                    // Never waits for Steam or closes it: while it runs, the launch options stay as they are.
                    if (launchOption == "keep")
                    {
                        Log("Steam launch option: left alone (--launch-option keep)");
                    }
                    else
                    {
                        Steam.Apply(game, launchOption != "off", Log);
                    }
                }
                else if (action == "uninstall")
                {
                    if (launchOption == "on")
                    {
                        Console.Error.WriteLine("--launch-option on is for --install; --uninstall takes off or keep");
                        return 2;
                    }
                    // Out of the launch options before the launcher goes; the launcher stays
                    // while they still start it (Steam running, or keep).
                    InstallerCore.CheckReady(game);
                    bool framework = core.RemovesFramework(game);
                    if (framework && launchOption != "keep")
                    {
                        Steam.Apply(game, false, Log);
                    }
                    core.Uninstall(game, !removeData, removeBepInEx, framework && Steam.State(game).AnyHas);
                }
                else
                {
                    Console.Error.WriteLine("Pass --install or --uninstall.");
                    Console.Error.Write(Usage.Replace("\n", Environment.NewLine));
                    return 2;
                }
                return 0;
            }
            catch (InstallerException ex)
            {
                Strings.Current = "en";
                Log("ERROR: " + (ex.LogText ?? ex.Text() + (ex.Detail == null ? "" : " (" + ex.Detail + ")")));
                return 1;
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex);
                return 1;
            }
        }
    }
}
