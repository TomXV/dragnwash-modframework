using System;
using System.Collections.Generic;
using System.Linq;

namespace DragNWash.Installer.Tests
{
    // Checks of installer/SteamConfig.cs: what the installer writes into Steam's
    // localconfig.vdf, on made-up files. Each check prints one line; any failure makes
    // the exit code 1. Uninstall's progress (UninstallTests.cs) and the mod icon
    // (IconTests.cs) are checked here too, on made-up game folders.
    internal static partial class Program
    {
        private const string App = "4739660";
        private const string L = @"D:\SteamLibrary\steamapps\common\Drag'n Wash\BepInEx\DragNWash.Installer\Launcher.exe";
        private const string Q = "\"" + L + "\"";

        private static int _failed;
        private static int _passed;

        private static int Main()
        {
            Options();
            Files();
            Accounts();
            UninstallSteps();
            UninstallProgressOrder();
            ModIconManifest();
            ModIconDataUrl();
            Console.WriteLine();
            Console.WriteLine(_failed == 0 ? $"All {_passed} checks passed." : $"{_failed} of {_passed + _failed} checks FAILED.");
            return _failed == 0 ? 0 : 1;
        }

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok)
            {
                _passed++;
                Console.WriteLine("ok    " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("FAIL  " + name + (detail == null ? "" : Environment.NewLine + "      " + detail.Replace("\n", "\n      ")));
            }
        }

        private static void Same(string name, string actual, string expected)
        {
            Check(name, actual == expected, $"expected [{expected}]\nactual   [{actual}]");
        }

        // ---- the launch options as text ----

        private static void Options()
        {
            Same("empty: the launcher and %command%", LaunchOption.Add("", L), Q + " %command%");
            Same("empty: taking it out leaves nothing", LaunchOption.Remove(Q + " %command%"), "");
            Same("options without %command% go after it", LaunchOption.Add("-force-d3d11", L), Q + " %command% -force-d3d11");
            Same("and come back as they were", LaunchOption.Remove(Q + " %command% -force-d3d11"), "-force-d3d11");
            Same("several options keep their order and spacing",
                LaunchOption.Add("-force-d3d11  -screen-fullscreen 0", L), Q + " %command% -force-d3d11  -screen-fullscreen 0");
            Same("a wrapper before %command% stays in front",
                LaunchOption.Add("\"C:\\Tools\\wrap.exe\" %command% -x", L), "\"C:\\Tools\\wrap.exe\" " + Q + " %command% -x");
            Same("and taking ours out gives it back",
                LaunchOption.Remove("\"C:\\Tools\\wrap.exe\" " + Q + " %command% -x"), "\"C:\\Tools\\wrap.exe\" %command% -x");
            Same("%command% alone", LaunchOption.Add("%command%", L), Q + " %command%");
            Same("%command% then options", LaunchOption.Add("%command% -x", L), Q + " %command% -x");
            Check("already set is recognised", LaunchOption.IsSet(Q + " %command% -x", L));
            Same("already set: adding changes nothing", LaunchOption.Add(Q + " %command% -x", L), Q + " %command% -x");
            string moved = "\"C:\\Games\\Old\\BepInEx\\DragNWash.Installer\\Launcher.exe\" %command% -x";
            Check("the launcher of a moved game folder is ours", LaunchOption.Has(moved));
            Check("but not set to this folder", !LaunchOption.IsSet(moved, L));
            Same("and is replaced by this folder's", LaunchOption.Add(moved, L), Q + " %command% -x");
            Same("an unquoted launcher path is ours too",
                LaunchOption.Remove("C:\\G\\BepInEx\\DragNWash.Installer\\Launcher.exe %command% -x"), "-x");
            Check("another program called Launcher.exe is not ours", !LaunchOption.Has("\"C:\\Other\\Launcher.exe\" %command%"));
            Same("taking out what is not there changes nothing", LaunchOption.Remove("-force-d3d11"), "-force-d3d11");
            Same("a launcher written twice goes twice", LaunchOption.Remove(Q + " " + Q + " %command% -x"), "-x");
            Same("%command% written twice stays", LaunchOption.Remove(Q + " %command% -x %command%"), "%command% -x %command%");
        }

        // ---- localconfig.vdf ----

        private const string Head =
            "\"UserLocalConfigStore\"\n" +
            "{\n" +
            "\t\"Broadcast\"\n" +
            "\t{\n" +
            "\t\t\"Permissions\"\t\t\"1\"\n" +
            "\t}\n" +
            "\t\"Software\"\n" +
            "\t{\n" +
            "\t\t\"Valve\"\n" +
            "\t\t{\n" +
            "\t\t\t\"Steam\"\n" +
            "\t\t\t{\n" +
            "\t\t\t\t\"SourceModInstallPath\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\\\\steamapps\\\\sourcemods\"\n";

        private const string Tail =
            "\t\t\t}\n" +
            "\t\t}\n" +
            "\t}\n" +
            "\t\"friends\"\n" +
            "\t{\n" +
            "\t\t\"PersonaName\"\t\t\"Tom \\\"the\\\" tester\"\n" +
            "\t}\n" +
            "}\n";

        private static string Apps(string body)
        {
            return "\t\t\t\t\"apps\"\n\t\t\t\t{\n" +
                   "\t\t\t\t\t\"730\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LastPlayed\"\t\t\"1700000000\"\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-novid\"\n\t\t\t\t\t}\n" +
                   body +
                   "\t\t\t\t}\n";
        }

        private static string Game(string inside)
        {
            return "\t\t\t\t\t\"" + App + "\"\n\t\t\t\t\t{\n" + inside + "\t\t\t\t\t}\n";
        }

        // How the launcher looks in the file: backslashes and quotes escaped.
        private static readonly string QEscaped = VdfFile.Escape(Q);

        private static void Files()
        {
            // No apps block at all.
            string none = Head + Tail;
            string added = LocalConfig.Change(none, App, L, true);
            Same("no apps block: the blocks are made in Steam's layout", added,
                Head +
                "\t\t\t\t\"apps\"\n\t\t\t\t{\n" +
                "\t\t\t\t\t\"" + App + "\"\n\t\t\t\t\t{\n" +
                "\t\t\t\t\t\t\"LaunchOptions\"\t\t\"" + QEscaped + " %command%\"\n" +
                "\t\t\t\t\t}\n\t\t\t\t}\n" +
                Tail);
            Same("no apps block: read back", LocalConfig.Options(new VdfFile(added), App), Q + " %command%");
            Same("no apps block: the rest is read as before", new VdfFile(added).Get("UserLocalConfigStore", "friends", "PersonaName"), "Tom \"the\" tester");

            // The game's block without LaunchOptions.
            string noOptions = Head + Apps(Game("\t\t\t\t\t\t\"LastPlayed\"\t\t\"1758000000\"\n")) + Tail;
            Same("game without LaunchOptions: the key is added in the game's block",
                LocalConfig.Change(noOptions, App, L, true),
                Head + Apps(Game("\t\t\t\t\t\t\"LastPlayed\"\t\t\"1758000000\"\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"" + QEscaped + " %command%\"\n")) + Tail);
            Check("game without LaunchOptions: nothing to take out", LocalConfig.Change(noOptions, App, L, false) == null);

            // Options the player already has.
            string own = Head + Apps(Game("\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-force-d3d11\"\n\t\t\t\t\t\t\"LastPlayed\"\t\t\"1758000000\"\n")) + Tail;
            string withOurs = LocalConfig.Change(own, App, L, true);
            Same("existing options: only the value changes", withOurs, own.Replace("\"-force-d3d11\"", "\"" + QEscaped + " %command% -force-d3d11\""));
            Check("existing options: already set, nothing to write", LocalConfig.Change(withOurs, App, L, true) == null);
            Same("existing options: taking it out gives the file back byte for byte", LocalConfig.Change(withOurs, App, L, false), own);
            Same("another game's options are left alone", LocalConfig.Options(new VdfFile(withOurs), "730"), "-novid");

            // Quotes and backslashes in what the player has.
            string quoted = Head + Apps(Game("\t\t\t\t\t\t\"LaunchOptions\"\t\t\"\\\"C:\\\\Tools\\\\wrap.exe\\\" %command% -name \\\"a b\\\"\"\n")) + Tail;
            Same("escaped quotes are read", LocalConfig.Options(new VdfFile(quoted), App), "\"C:\\Tools\\wrap.exe\" %command% -name \"a b\"");
            string quotedOurs = LocalConfig.Change(quoted, App, L, true);
            Same("escaped quotes: ours goes before %command%", LocalConfig.Options(new VdfFile(quotedOurs), App),
                "\"C:\\Tools\\wrap.exe\" " + Q + " %command% -name \"a b\"");
            Same("escaped quotes: and out again, byte for byte", LocalConfig.Change(quotedOurs, App, L, false), quoted);

            // Windows line ends, a comment, an unquoted token and a condition tag.
            string crlf = (Head + "\t\t\t\t// a comment\n\t\t\t\tSomeFlag 1 [$WIN32]\n" + Tail).Replace("\n", "\r\n");
            string crlfAdded = LocalConfig.Change(crlf, App, L, true);
            Check("CRLF: the added lines end in CRLF too", crlfAdded != null && !crlfAdded.Replace("\r\n", "").Contains("\n"));
            Same("CRLF: comment and unquoted value are kept", new VdfFile(crlfAdded).Get("UserLocalConfigStore", "Software", "Valve", "Steam", "SomeFlag"), "1");
            Check("CRLF: everything before the new block is unchanged", crlfAdded.StartsWith(crlf.Substring(0, crlf.IndexOf("\t\t\t}", StringComparison.Ordinal)), StringComparison.Ordinal));

            // Key case, as Steam matches keys.
            string lower = (Head + Apps(Game("\t\t\t\t\t\t\"launchoptions\"\t\t\"-x\"\n")) + Tail).Replace("\"Software\"", "\"software\"");
            Same("keys in another case are the same key", LocalConfig.Options(new VdfFile(lower), App), "-x");
            Check("and are changed in place, not added again",
                LocalConfig.Change(lower, App, L, true) == lower.Replace("\"-x\"", "\"" + QEscaped + " %command% -x\""));

            // Files that are not what they should be.
            Check("a file without UserLocalConfigStore is refused", Throws(() => LocalConfig.Change("\"Other\"\n{\n}\n", App, L, true)));
            Check("a file cut off halfway is refused", Throws(() => LocalConfig.Change(Head, App, L, true)));
        }

        private static bool Throws(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (FormatException)
            {
                return true;
            }
        }

        // ---- which accounts ----

        private static void Accounts()
        {
            var played = new VdfFile(Head + Apps(Game("\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-x\"\n")) + Tail);
            var notPlayed = new VdfFile(Head + Tail);
            var ours = new VdfFile(LocalConfig.Change(Head + Tail, App, L, true));
            var list = new List<(string, VdfFile)> { ("111", played), ("222", notPlayed), ("333", notPlayed) };

            Same("on: accounts that played it, and the last signed in",
                string.Join(",", LocalConfig.Accounts(list, App, "333", true)), "111,333");
            Same("on: just those that played it when the last one is not known",
                string.Join(",", LocalConfig.Accounts(list, App, null, true)), "111");
            Same("on: every account when none can be told",
                string.Join(",", LocalConfig.Accounts(new List<(string, VdfFile)> { ("222", notPlayed), ("333", notPlayed) }, App, null, true)), "222,333");
            Same("off: only the accounts that have it",
                string.Join(",", LocalConfig.Accounts(new List<(string, VdfFile)> { ("111", played), ("444", ours) }, App, null, false)), "444");

            var login = new VdfFile(
                "\"users\"\n{\n" +
                "\t\"76561197960265839\"\n\t{\n\t\t\"AccountName\"\t\t\"a\"\n\t\t\"MostRecent\"\t\t\"0\"\n\t}\n" +
                "\t\"76561197960266061\"\n\t{\n\t\t\"AccountName\"\t\t\"b\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t}\n" +
                "}\n");
            Same("loginusers.vdf: the last signed in, as its userdata folder", LocalConfig.LastSignedIn(login), "333");
        }
    }
}
