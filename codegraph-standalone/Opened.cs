using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DragNWash.ModFramework.CodeGraph;
using Mono.Cecil;

namespace DragNWash.CodeGraph.Standalone
{
    // What the user opened: files, a folder, or a Unity game's folder, read
    // into one CodeGraphModel. Only these paths are read, and nothing is written.
    internal sealed class Opened
    {
        internal CodeGraphModel Model;
        internal readonly List<string> Files = new List<string>();
        internal readonly List<string> NotDotNet = new List<string>();
        internal string Label = "";
        internal long Ms;

        // .NET's and Unity's own assemblies, left out when a folder is opened
        // (--all keeps them): calls into them are listed, not opened.
        private static readonly string[] Framework = { "System", "Microsoft.", "mscorlib", "netstandard", "WindowsBase", "Presentation", "Unity.", "UnityEngine", "Mono." };

        internal static Opened Open(IList<string> paths, bool all)
        {
            var sw = Stopwatch.StartNew();
            var opened = new Opened();
            var files = new List<string>();
            foreach (string path in paths)
            {
                string full = Path.GetFullPath(path);
                if (File.Exists(full)) files.Add(full);
                else if (Directory.Exists(full)) files.AddRange(FromFolder(full, all));
                else throw new InvalidOperationException("Not found: " + path);
            }
            files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0) throw new InvalidOperationException("No .dll or .exe there.");

            var resolver = new DefaultAssemblyResolver();
            foreach (string dir in files.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase)) resolver.AddSearchDirectory(dir);
            var assemblies = new List<AssemblyDefinition>();
            foreach (string file in files)
            {
                try
                {
                    // InMemory: the file is not kept open, so it can be rebuilt while it is shown.
                    assemblies.Add(AssemblyDefinition.ReadAssembly(file, new ReaderParameters { ReadWrite = false, InMemory = true, AssemblyResolver = resolver }));
                    opened.Files.Add(file);
                }
                catch (BadImageFormatException)
                {
                    opened.NotDotNet.Add(Path.GetFileName(file));   // native code: nothing to draw
                }
            }
            if (assemblies.Count == 0) throw new InvalidOperationException("None of these is a .NET assembly (" + string.Join(", ", opened.NotDotNet.Take(5)) + ").");
            opened.Model = new CodeGraphModel(assemblies) { Source = "the opened assemblies" };
            opened.Ms = sw.ElapsedMilliseconds;
            opened.Model.BuildMs = opened.Ms;
            opened.Label = Path.GetFileName(opened.Files[0]) + (opened.Files.Count > 1 ? " and " + (opened.Files.Count - 1) + " more" : "");
            return opened;
        }

        // A folder: its .dll and .exe files. A Unity game's folder (or its _Data
        // folder) means its Managed folder. Without --all, .NET's and Unity's own
        // assemblies are left out, unless nothing else is there.
        private static IEnumerable<string> FromFolder(string folder, bool all)
        {
            string managed = Directory.GetDirectories(folder, "*_Data").Select(d => Path.Combine(d, "Managed")).FirstOrDefault(Directory.Exists);
            if (managed == null && folder.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(folder, "Managed"))) managed = Path.Combine(folder, "Managed");
            if (managed != null) folder = managed;
            else if (File.Exists(Path.Combine(folder, "GameAssembly.dll")))
            {
                throw new InvalidOperationException("This Unity game is built with IL2CPP: its code is native (GameAssembly.dll), so there is no .NET code to read.");
            }
            List<string> files = Directory.GetFiles(folder, "*.dll").Concat(Directory.GetFiles(folder, "*.exe")).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            if (all) return files;
            List<string> own = files.Where(f => !IsFramework(Path.GetFileNameWithoutExtension(f))).ToList();
            return own.Count > 0 ? own : files;
        }

        private static bool IsFramework(string name) =>
            Framework.Any(s => name.Equals(s.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) || name.StartsWith(s, StringComparison.OrdinalIgnoreCase));
    }
}
