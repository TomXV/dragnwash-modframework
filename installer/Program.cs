using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DragNWash.Installer
{
    // Double-click: the window. From a terminal, without a window (how it is tested):
    //
    //   Install.exe --install [--game-dir <folder>] [--choice <id>=<value>]...
    //   Install.exe --uninstall [--game-dir <folder>] [--remove-data] [--remove-bepinex]
    //
    // Other options: --bepinex-zip <file> uses a local BepInEx zip (still checked
    // against the pinned SHA-256), --log <file> appends the log to a file.
    internal static class Program
    {
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
                    MessageBox.Show(Strings.Get(ex.Key) + (ex.Detail == null ? "" : Environment.NewLine + Environment.NewLine + ex.Detail),
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
            string action = null, game = null, zip = null, logFile = null;
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
                    case "--bepinex-zip": zip = Next(); break;
                    case "--log": logFile = Next(); break;
                    case "--remove-data": removeData = true; break;
                    case "--remove-bepinex": removeBepInEx = true; break;
                    case "--choice":
                        string pair = Next();
                        int eq = pair.IndexOf('=');
                        if (eq <= 0) throw new ArgumentException("--choice needs <id>=<value>");
                        choices[pair.Substring(0, eq)] = pair.Substring(eq + 1);
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown option {a}");
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
                    core.Install(game, choices, zip);
                }
                else if (action == "uninstall")
                {
                    core.Uninstall(game, !removeData, removeBepInEx);
                }
                else
                {
                    Console.Error.WriteLine("Pass --install or --uninstall.");
                    return 2;
                }
                return 0;
            }
            catch (InstallerException ex)
            {
                Strings.Current = "en";
                Log("ERROR: " + Strings.Get(ex.Key) + (ex.Detail == null ? "" : " (" + ex.Detail + ")"));
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
