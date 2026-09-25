using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DragNWash.Installer
{
    // Every change one install makes in the game folder, so a failure halfway can be
    // undone. A file is copied to BepInEx/DragNWash.Installer/backup/<date_time>/
    // before it is first replaced or deleted; a file or folder that was not there is
    // noted as new. RollBack undoes the list backwards; Commit keeps this backup as
    // the only one and clears the staging folder.
    internal sealed class InstallJournal
    {
        private enum Kind
        {
            Replaced,
            Created,
            Folder,
        }

        private readonly string _game;
        private readonly string _root;
        private readonly string _backup;
        private readonly List<(Kind Kind, string Path)> _steps = new List<(Kind, string)>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal InstallJournal(string game)
        {
            _game = Path.GetFullPath(game).TrimEnd('\\');
            _root = Path.Combine(_game, "BepInEx", Paths.InstallerFolder);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
            // A second run in the same minute gets a folder of its own: the first one's is the backup to keep until this one succeeds.
            _backup = Path.Combine(_root, "backup", stamp);
            for (int n = 2; Directory.Exists(_backup); n++)
            {
                _backup = Path.Combine(_root, "backup", stamp + "_" + n);
            }
        }

        // Files copied to the backup so far.
        internal int Replaced { get; private set; }

        // The backup folder relative to the game folder, as the log and the messages show it.
        internal string BackupShown => Relative(_backup);

        // An empty folder under BepInEx/DragNWash.Installer/staging/ to unpack into
        // before anything is put in place. BepInEx does not load anything from there.
        internal string Staging(string name)
        {
            string dir = Path.Combine(_root, "staging", name);
            DeleteTree(dir);
            CreateDirectory(dir);
            return dir;
        }

        internal void CreateDirectory(string dir)
        {
            var missing = new Stack<string>();
            for (string d = Path.GetFullPath(dir); d != null && !Directory.Exists(d); d = Path.GetDirectoryName(d))
            {
                missing.Push(d);
            }
            while (missing.Count > 0)
            {
                string d = missing.Pop();
                Directory.CreateDirectory(d);
                _steps.Add((Kind.Folder, d));
            }
        }

        // Call before a file is written or deleted. Only its first state counts: a file
        // written twice is put back as it was before either.
        internal void Change(string path)
        {
            path = Path.GetFullPath(path);
            if (!path.StartsWith(_game + "\\", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{path} is outside the game folder");
            }
            if (!_seen.Add(path))
            {
                return;
            }
            if (File.Exists(path))
            {
                string copy = BackupOf(path);
                CreateDirectory(Path.GetDirectoryName(copy));
                File.Copy(path, copy, true);
                _steps.Add((Kind.Replaced, path));
                Replaced++;
            }
            else
            {
                CreateDirectory(Path.GetDirectoryName(path));
                _steps.Add((Kind.Created, path));
            }
        }

        internal void CopyFile(string from, string to)
        {
            Change(to);
            File.Copy(from, to, true);
        }

        // For a file that may be running: the launcher and its DLLs, when the launcher
        // itself installs an update. Windows lets a running file be renamed but not
        // written, so it is moved to <name>.old first; the old copy is deleted by the
        // next install (DeleteMovedAside), once it is no longer running.
        internal void CopyFileInUse(string from, string to)
        {
            Change(to);
            try
            {
                File.Copy(from, to, true);
            }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && File.Exists(to))
            {
                string aside = to + MovedAside;
                for (int n = 2; File.Exists(aside); n++)
                {
                    try
                    {
                        File.Delete(aside);
                    }
                    catch (Exception)
                    {
                        aside = to + "." + n + MovedAside;
                    }
                }
                File.Move(to, aside);
                File.Copy(from, to, true);
            }
        }

        internal const string MovedAside = ".old";

        // The copies CopyFileInUse moved aside in dir; one still running stays for next time.
        internal static void DeleteMovedAside(string dir)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(dir, "*" + MovedAside))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception)
                {
                }
            }
        }

        // Copied over what is there: files the target has and the source does not stay.
        internal void CopyTree(string from, string to)
        {
            CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                CopyFile(file, Path.Combine(to, Path.GetFileName(file)));
            }
            foreach (string dir in Directory.GetDirectories(from))
            {
                CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
        }

        internal void WriteAllText(string path, string text)
        {
            Change(path);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        internal void Delete(string path)
        {
            if (File.Exists(path))
            {
                Change(path);
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }

        // Puts every file back as it was, newest change first, then removes the
        // folders this run made if they are empty again, the staging folder and this
        // backup. Returns how many files were put back; failed counts those that could
        // not be, whose backup is then kept.
        internal int RollBack(Action<string> log, out int failed)
        {
            int done = 0;
            failed = 0;
            foreach (var (kind, path) in Enumerable.Reverse(_steps))
            {
                try
                {
                    if (kind == Kind.Replaced)
                    {
                        string copy = BackupOf(path);
                        // A file the failed step never got to open is already as it was.
                        if (!SameBytes(path, copy))
                        {
                            if (File.Exists(path))
                            {
                                File.SetAttributes(path, FileAttributes.Normal);
                            }
                            File.Copy(copy, path, true);
                        }
                        done++;
                    }
                    else if (kind == Kind.Created && File.Exists(path))
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                        done++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    log($"Rollback: could not put back {Relative(path)}: {ex.Message}");
                }
            }
            TryDeleteTree(Path.Combine(_root, "staging"), log);
            if (failed == 0)
            {
                TryDeleteTree(_backup, log);
            }
            foreach (var (_, dir) in Enumerable.Reverse(_steps).Where(s => s.Kind == Kind.Folder))
            {
                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
                catch (Exception)
                {
                    // Left empty; it does no harm.
                }
            }
            log(failed == 0
                ? $"Rollback: {done} files put back, the game folder is as it was"
                : $"Rollback: {done} files put back, {failed} could not be; the replaced files are in {BackupShown}");
            return done;
        }

        // After a successful install: the staging folder goes, and so does every older
        // backup, so the one of this run is the only one. A run that replaced nothing
        // leaves none.
        internal void Commit(Action<string> log)
        {
            TryDeleteTree(Path.Combine(_root, "staging"), log);
            string backups = Path.Combine(_root, "backup");
            if (Directory.Exists(backups))
            {
                foreach (string older in Directory.GetDirectories(backups))
                {
                    if (Replaced == 0 || !string.Equals(older, _backup, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDeleteTree(older, log);
                    }
                }
            }
            foreach (string dir in new[] { backups, _root })
            {
                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
                catch (Exception)
                {
                    // Left empty; it does no harm.
                }
            }
            log(Replaced == 0 ? "Backup: nothing was replaced" : $"Backup: {Replaced} files -> {BackupShown}");
        }

        // Deletes a folder with everything in it, read-only files included.
        internal static void DeleteTree(string dir)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(dir, true);
        }

        private static void TryDeleteTree(string dir, Action<string> log)
        {
            try
            {
                DeleteTree(dir);
            }
            catch (Exception ex)
            {
                log($"Could not remove {dir}: {ex.Message}");
            }
        }

        private string BackupOf(string path)
        {
            return Path.Combine(_backup, Relative(path));
        }

        private string Relative(string path)
        {
            return path.StartsWith(_game + "\\", StringComparison.OrdinalIgnoreCase) ? path.Substring(_game.Length + 1) : path;
        }

        // Whether two files hold the same bytes; false when either is missing.
        internal static bool SameBytes(string a, string b)
        {
            if (!File.Exists(a) || !File.Exists(b))
            {
                return false;
            }
            using (var x = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var y = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (x.Length != y.Length)
                {
                    return false;
                }
                var bx = new byte[81920];
                var by = new byte[81920];
                while (true)
                {
                    int n = x.Read(bx, 0, bx.Length);
                    if (n == 0)
                    {
                        return true;
                    }
                    int m = 0;
                    while (m < n)
                    {
                        int r = y.Read(by, m, n - m);
                        if (r == 0)
                        {
                            return false;
                        }
                        m += r;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        if (bx[i] != by[i])
                        {
                            return false;
                        }
                    }
                }
            }
        }
    }
}
