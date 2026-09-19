using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace DragNWash.CodeGraph.Standalone
{
    // The code graph without the game (docs/CODE_GRAPH_STANDALONE.md):
    //
    //   CodeGraphStandalone.exe [file.dll ...|folder] [--focus m:<method id>|t:<type>] [--all]
    //
    // A folder opens its assemblies, leaving out .NET's and Unity's own unless
    // --all; a Unity game's folder (built with Mono) opens its Managed folder.
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var paths = new List<string>();
            string focus = null;
            bool all = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--all") all = true;
                else if (args[i] == "--focus" && i + 1 < args.Length) focus = args[++i];
                else paths.Add(args[i]);
            }
            if (focus != null && !(focus.StartsWith("m:", StringComparison.Ordinal) || focus.StartsWith("t:", StringComparison.Ordinal))) focus = "m:" + focus;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ViewerForm(paths, focus, all));
            return 0;
        }
    }
}
