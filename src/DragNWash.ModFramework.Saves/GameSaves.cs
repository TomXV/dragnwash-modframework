using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DragNWash.ModFramework.Saves
{
    /// <summary>A copy of a save slot's file, taken when the game wrote it or before a mod changed it.</summary>
    public sealed class SaveSnapshot
    {
        internal SaveSnapshot() { }

        /// <summary>The snapshot file.</summary>
        public string Path { get; internal set; }

        /// <summary>When it was taken, local time.</summary>
        public DateTime Taken { get; internal set; }

        /// <summary>The level index stored in it, or "?".</summary>
        public string Level { get; internal set; }

        /// <summary>"2026-09-14 20:31:05  level 3".</summary>
        public string Label => $"{Taken:yyyy-MM-dd HH:mm:ss}  level {Level}";
    }

    /// <summary>One boolean event flag in a save.</summary>
    public struct SaveFlag
    {
        /// <summary>Flag id, for example "level_1_complete".</summary>
        public string Id;

        /// <summary>Its value.</summary>
        public bool Value;
    }

    /// <summary>
    /// Flags and saves library: reads the game's save slots, changes their level
    /// and flags, and keeps a history of every version of each save so any change
    /// can be undone.
    /// </summary>
    /// <remarks>
    /// The game keeps one save per slot and overwrites it on every save. Since
    /// the game update of 2026-09-14 it writes
    /// <c>&lt;persistentDataPath&gt;/&lt;steamid&gt;/slot&lt;N&gt;/savegame.dgn</c> (inside the
    /// folder Steam Cloud syncs, with a <c>{"version":1}</c> entry first), and
    /// reads the older <c>&lt;steamid&gt;_slot&lt;N&gt;/savegame.dgn</c> only when a slot has
    /// no new save yet. Slots keep their older names here (<c>&lt;steamid&gt;_slot&lt;N&gt;</c>)
    /// so their snapshot history carries on; <see cref="SavePath"/> finds the file
    /// the game reads. The library snapshots the file whenever it
    /// changes and before any edit made through it, into <c>BepInEx/SaveHistory</c>.
    /// Edits only rewrite the file: the game reads it when a slot is loaded, so the
    /// player returns to the title screen and loads the slot for an edit to take
    /// effect. Saving in game afterwards overwrites it again.
    /// <para>
    /// Depend on it with
    /// <c>[BepInDependency(GameSaves.Guid, BepInDependency.DependencyFlags.HardDependency)]</c>.
    /// </para>
    /// </remarks>
    public static class GameSaves
    {
        /// <summary>BepInEx GUID of the flags and saves library.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.saves";

        /// <summary>Library version. Keep in sync with the csproj.</summary>
        public const string Version = "1.1.0";

        /// <summary>The game's save file name inside a slot folder.</summary>
        public const string SaveFileName = "savegame.dgn";

        private const float PollInterval = 2f;
        private static readonly Regex LevelIndex = new Regex("\"levelIndex\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex FlagEntry = new Regex(
            "\"id\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"type\"\\s*:\\s*\"BOOL\"\\s*,\\s*\"boolValue\"\\s*:\\s*(true|false)", RegexOptions.Compiled);

        private static float _nextPoll;
        private static readonly Dictionary<string, DateTime> LastWrite = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> LastContent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Folder the snapshots are kept in, one subfolder per slot.</summary>
        public static string HistoryFolder { get; internal set; }

        /// <summary>Raised on the main thread when the game wrote a slot's save with new content (the slot name).</summary>
        public static event Action<string> SaveWritten;

        /// <summary>Where the game keeps its save slots.</summary>
        public static string SaveFolder => Application.persistentDataPath;

        private static readonly Regex OldSlotName = new Regex(@"^(.+)_slot(\d+)$", RegexOptions.Compiled);
        private static readonly Regex NewSlotFolder = new Regex(@"^slot(\d+)$", RegexOptions.Compiled);

        /// <summary>
        /// Slots that hold a save, in slot order (1, 2, 3), named
        /// <c>&lt;steamid&gt;_slot&lt;N&gt;</c> whichever layout the save is in.
        /// </summary>
        public static List<string> Slots()
        {
            var slots = new List<string>();
            try
            {
                foreach (string dir in Directory.GetDirectories(SaveFolder))
                {
                    string name = System.IO.Path.GetFileName(dir);
                    // The older layout: <steamid>_slot<N>/savegame.dgn.
                    if (File.Exists(System.IO.Path.Combine(dir, SaveFileName)) && !slots.Contains(name))
                    {
                        slots.Add(name);
                    }
                    // The layout since 2026-09-14: <steamid>/slot<N>/savegame.dgn.
                    foreach (string sub in Directory.GetDirectories(dir))
                    {
                        Match m = NewSlotFolder.Match(System.IO.Path.GetFileName(sub));
                        string slot = name + "_slot" + (m.Success ? m.Groups[1].Value : "");
                        if (m.Success && File.Exists(System.IO.Path.Combine(sub, SaveFileName)) && !slots.Contains(slot))
                        {
                            slots.Add(slot);
                        }
                    }
                }
                slots.Sort(CompareSlots);
            }
            catch (Exception ex)
            {
                SavesLibraryPlugin.Log.LogWarning($"Could not list save slots: {ex.Message}");
            }
            return slots;
        }

        // By slot number, then by name, so the tab shows 1, 2, 3 left to right.
        private static int CompareSlots(string a, string b)
        {
            Match ma = OldSlotName.Match(a), mb = OldSlotName.Match(b);
            if (ma.Success && mb.Success && int.TryParse(ma.Groups[2].Value, out int na) && int.TryParse(mb.Groups[2].Value, out int nb) && na != nb)
            {
                return na.CompareTo(nb);
            }
            return string.CompareOrdinal(a, b);
        }

        /// <summary>"slot 1" for "76561198000000000_slot1"; other names as they are.</summary>
        public static string ShortName(string slot)
        {
            int i = slot?.IndexOf("_slot", StringComparison.Ordinal) ?? -1;
            return i >= 0 ? "slot " + slot.Substring(i + 5) : slot;
        }

        /// <summary>
        /// Full path of the save file the game reads for a slot: the newer
        /// <c>&lt;steamid&gt;/slot&lt;N&gt;</c> one when it exists, else the older
        /// <c>&lt;steamid&gt;_slot&lt;N&gt;</c> one, else where the game will write next.
        /// </summary>
        public static string SavePath(string slot)
        {
            Match m = OldSlotName.Match(slot ?? "");
            string older = System.IO.Path.Combine(SaveFolder, slot ?? "", SaveFileName);
            if (!m.Success)
            {
                return older;
            }
            string newer = System.IO.Path.Combine(SaveFolder, m.Groups[1].Value, "slot" + m.Groups[2].Value, SaveFileName);
            return File.Exists(newer) || !File.Exists(older) ? newer : older;
        }

        // The game reads a save in the newer layout only when it starts with {"version":1};
        // a snapshot taken before the update does not, so it gets one on the way back.
        private static string ForPath(string savePath, string content)
        {
            bool newer = NewSlotFolder.IsMatch(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(savePath) ?? ""));
            string trimmed = content.TrimStart();
            if (!newer || content.Contains("\"version\"") || !trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return content;
            }
            string rest = trimmed.Substring(1).TrimStart();
            return "[{\"version\":1}" + (rest.StartsWith("]", StringComparison.Ordinal) ? "" : ",") + rest;
        }

        /// <summary>Snapshots of a slot, newest first.</summary>
        public static List<SaveSnapshot> Snapshots(string slot)
        {
            var list = new List<SaveSnapshot>();
            try
            {
                string dir = System.IO.Path.Combine(HistoryFolder, slot);
                if (!Directory.Exists(dir))
                {
                    return list;
                }
                foreach (string file in Directory.GetFiles(dir, "*.dgn"))
                {
                    list.Add(new SaveSnapshot { Path = file, Taken = File.GetLastWriteTime(file), Level = LevelOfFile(file) });
                }
                list.Sort((a, b) => b.Taken.CompareTo(a.Taken));
            }
            catch (Exception ex)
            {
                SavesLibraryPlugin.Log.LogWarning($"Could not list snapshots for {slot}: {ex.Message}");
            }
            return list;
        }

        /// <summary>True when the snapshot holds exactly what the slot's save holds now.</summary>
        public static bool SnapshotMatchesSave(string slot, SaveSnapshot snapshot)
        {
            try
            {
                string savePath = SavePath(slot);
                return snapshot != null && File.Exists(savePath) && File.ReadAllText(savePath) == File.ReadAllText(snapshot.Path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Puts a snapshot back as the slot's save. The save being replaced is
        /// snapshotted first, so a restore can itself be undone. Returns a message
        /// for the player.
        /// </summary>
        public static string Restore(string slot, SaveSnapshot snapshot)
        {
            try
            {
                string savePath = SavePath(slot);
                if (File.Exists(savePath))
                {
                    string current = File.ReadAllText(savePath);
                    LastContent[slot] = current;
                    TakeSnapshot(slot, savePath, current);
                }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(savePath));
                string restored = ForPath(savePath, File.ReadAllText(snapshot.Path));
                // Same bytes as the plain File.WriteAllText(path, text) this
                // replaces: UTF-8, no byte-order mark.
                SafeFile.Write(savePath, new UTF8Encoding(false), w => w.Write(restored));
                LastWrite[slot] = File.GetLastWriteTimeUtc(savePath);
                LastContent[slot] = File.ReadAllText(savePath);
                SavesLibraryPlugin.Log.LogInfo($"Restored {slot} to {snapshot.Label}.");
                return $"Restored {ShortName(slot)} to {snapshot.Label}. Return to the title screen and load the slot for it to take effect; saving in game will overwrite it again.";
            }
            catch (Exception ex)
            {
                return $"Restore failed: {ex.Message}";
            }
        }

        /// <summary>The slot's level index, or -1 when it cannot be read.</summary>
        public static int ReadLevel(string slot)
        {
            return int.TryParse(LevelOfFile(SavePath(slot)), out int level) ? level : -1;
        }

        /// <summary>The boolean flags stored in the slot's save, in file order.</summary>
        public static List<SaveFlag> ReadFlags(string slot)
        {
            var list = new List<SaveFlag>();
            try
            {
                foreach (Match m in FlagEntry.Matches(File.ReadAllText(SavePath(slot))))
                {
                    list.Add(new SaveFlag { Id = m.Groups[1].Value, Value = m.Groups[2].Value == "true" });
                }
            }
            catch (Exception ex)
            {
                SavesLibraryPlugin.Log.LogWarning($"Could not read the flags of {slot}: {ex.Message}");
            }
            return list;
        }

        /// <summary>Sets the slot's level index. Returns a message for the player.</summary>
        public static string SetLevel(string owner, string slot, int level)
        {
            return Edit(owner, slot, text => LevelIndex.Replace(text, "\"levelIndex\":" + level, 1), $"level set to {level}");
        }

        /// <summary>Sets one flag, adding it when the game never set it. Returns a message for the player.</summary>
        public static string SetFlag(string owner, string slot, string id, bool value)
        {
            return Edit(owner, slot, text => ApplyFlag(text, id, value), $"{id} = {(value ? "true" : "false")}");
        }

        /// <summary>Sets several flags in one edit (one snapshot, one write). Returns a message for the player.</summary>
        public static string SetFlags(string owner, string slot, IEnumerable<KeyValuePair<string, bool>> values, string description)
        {
            List<KeyValuePair<string, bool>> all = values?.ToList() ?? new List<KeyValuePair<string, bool>>();
            return Edit(owner, slot, text =>
            {
                foreach (KeyValuePair<string, bool> kv in all)
                {
                    text = ApplyFlag(text, kv.Key, kv.Value);
                }
                return text;
            }, description ?? $"{all.Count} flag(s) set");
        }

        /// <summary>
        /// Moves snapshots kept somewhere else before (a mod's own history folder,
        /// with one subfolder per slot) into <see cref="HistoryFolder"/>, keeping
        /// files that are already there. Returns how many were moved.
        /// </summary>
        public static int ImportHistory(string folder)
        {
            int moved = 0;
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ||
                    string.Equals(System.IO.Path.GetFullPath(folder).TrimEnd('\\', '/'), System.IO.Path.GetFullPath(HistoryFolder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }
                foreach (string slotDir in Directory.GetDirectories(folder))
                {
                    string target = System.IO.Path.Combine(HistoryFolder, System.IO.Path.GetFileName(slotDir));
                    Directory.CreateDirectory(target);
                    foreach (string file in Directory.GetFiles(slotDir, "*.dgn"))
                    {
                        string to = System.IO.Path.Combine(target, System.IO.Path.GetFileName(file));
                        if (File.Exists(to))
                        {
                            continue;
                        }
                        DateTime taken = File.GetLastWriteTimeUtc(file);
                        File.Move(file, to);
                        File.SetLastWriteTimeUtc(to, taken);
                        moved++;
                    }
                }
                if (moved > 0)
                {
                    SavesLibraryPlugin.Log.LogInfo($"Moved {moved} save snapshot(s) from {folder} to {HistoryFolder}.");
                }
            }
            catch (Exception ex)
            {
                SavesLibraryPlugin.Log.LogWarning($"Could not move save snapshots from {folder}: {ex.Message}");
            }
            return moved;
        }

        // From the plugin's Update.
        internal static void Tick(int keep)
        {
            if (HistoryFolder == null || Time.unscaledTime < _nextPoll)
            {
                return;
            }
            _nextPoll = Time.unscaledTime + PollInterval;
            _keep = Mathf.Max(1, keep);

            foreach (string slot in Slots())
            {
                string savePath = SavePath(slot);
                try
                {
                    DateTime now = File.GetLastWriteTimeUtc(savePath);
                    if (LastWrite.TryGetValue(slot, out DateTime seen) && seen == now)
                    {
                        continue;
                    }
                    LastWrite[slot] = now;

                    // The game writes more often than the state changes; only a
                    // different file is worth a snapshot.
                    string content = File.ReadAllText(savePath);
                    if (LastContent.TryGetValue(slot, out string previous) && previous == content)
                    {
                        continue;
                    }
                    bool firstLook = !LastContent.ContainsKey(slot);
                    LastContent[slot] = content;
                    TakeSnapshot(slot, savePath, content);
                    if (!firstLook)
                    {
                        RaiseSaveWritten(slot);
                    }
                }
                catch (IOException)
                {
                    // Mid-write; next poll.
                    LastWrite.Remove(slot);
                }
                catch (Exception ex)
                {
                    SavesLibraryPlugin.Log.LogWarning($"Snapshot failed for {slot}: {ex.Message}");
                }
            }
        }

        private static int _keep = 30;

        private static void TakeSnapshot(string slot, string savePath, string content)
        {
            string dir = System.IO.Path.Combine(HistoryFolder, slot);
            Directory.CreateDirectory(dir);

            // Not again when the newest snapshot already holds it (after a restart
            // nothing is remembered, but nothing changed either).
            List<SaveSnapshot> existing = Snapshots(slot);
            if (existing.Count > 0 && File.ReadAllText(existing[0].Path) == content)
            {
                return;
            }

            string target = System.IO.Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".dgn");
            File.Copy(savePath, target, overwrite: true);
            SavesLibraryPlugin.Log.LogInfo($"Snapshot of {slot} saved ({existing.Count + 1} kept, level {LevelOfFile(target)}).");

            for (int i = _keep - 1; i < existing.Count; i++)
            {
                try
                {
                    File.Delete(existing[i].Path);
                }
                catch
                {
                }
            }
        }

        // The save is a JSON array: [{"levelIndex":N}, {"id":..,"type":"BOOL",
        // "boolValue":..,"stringValue":..}, ...]. Edits use regexes on the text so
        // the file keeps exactly the game's own layout.
        private static string ApplyFlag(string text, string id, bool value)
        {
            string v = value ? "true" : "false";
            var one = new Regex("(\"id\"\\s*:\\s*\"" + Regex.Escape(id) + "\"\\s*,\\s*\"type\"\\s*:\\s*\"BOOL\"\\s*,\\s*\"boolValue\"\\s*:\\s*)(true|false)(\\s*,\\s*\"stringValue\"\\s*:\\s*)(true|false)");
            if (one.IsMatch(text))
            {
                return one.Replace(text, "${1}" + v + "${3}" + v, 1);
            }

            // The game's registry only stores flags that were set once; append.
            int end = text.LastIndexOf(']');
            if (end < 0)
            {
                return text;
            }
            string entry = "{\"id\":\"" + id.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",\"type\":\"BOOL\",\"boolValue\":" + v + ",\"stringValue\":" + v + "}";
            string before = text.Substring(0, end).TrimEnd();
            string separator = before.EndsWith("[") ? "" : ",";
            return before + separator + entry + text.Substring(end);
        }

        // Snapshot, rewrite, and re-arm change tracking so the edit is not
        // reported as a game write.
        private static string Edit(string owner, string slot, Func<string, string> change, string what)
        {
            try
            {
                string savePath = SavePath(slot);
                string current = File.ReadAllText(savePath);
                LastContent[slot] = current;
                TakeSnapshot(slot, savePath, current);
                string edited = change(current);
                if (edited == current)
                {
                    return $"Nothing changed ({what}).";
                }
                // Same bytes as the plain File.WriteAllText(path, text) this
                // replaces: UTF-8, no byte-order mark.
                SafeFile.Write(savePath, new UTF8Encoding(false), w => w.Write(edited));
                LastWrite[slot] = File.GetLastWriteTimeUtc(savePath);
                LastContent[slot] = edited;
                SavesLibraryPlugin.Log.LogInfo($"{owner ?? "A mod"} changed {slot}: {what}.");
                return $"{ShortName(slot)}: {what}. Return to the title screen and load the slot for it to take effect.";
            }
            catch (Exception ex)
            {
                return $"Edit failed: {ex.Message}";
            }
        }

        private static string LevelOfFile(string file)
        {
            try
            {
                Match m = LevelIndex.Match(File.ReadAllText(file));
                return m.Success ? m.Groups[1].Value : "?";
            }
            catch
            {
                return "?";
            }
        }

        private static void RaiseSaveWritten(string slot)
        {
            if (SaveWritten == null)
            {
                return;
            }
            foreach (Action<string> handler in SaveWritten.GetInvocationList())
            {
                try
                {
                    handler(slot);
                }
                catch (Exception ex)
                {
                    SavesLibraryPlugin.Log.LogError($"A SaveWritten handler threw: {ex}");
                }
            }
        }
    }
}
