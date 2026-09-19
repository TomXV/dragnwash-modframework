using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DragNWash.ModFramework.Inspector
{
    // Every edit made through the Inspector this session: what it was before
    // the first edit (the original), before this edit, and after. Rows and the
    // gizmo record here; the History view lists it; a row's menu and Reset
    // read it back. Keyed by the target object and the member, so a value
    // edited, deselected and selected again still knows its original.
    internal static class InspectorHistory
    {
        internal sealed class Entry
        {
            public string Key;
            public string Label;      // where: object path and component
            public string Member;     // the member (or element) name
            public object Before, After;
            public DateTime Time;
            public Func<object> Get;
            public Action<object> Set;
            public bool Reverted;
            // Where the edit was, in the terms of an overrides file, taken when
            // it was made (the object may be gone by the time it is exported).
            public InspectorExport.Place Where;
            public object Target;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly Dictionary<string, object> Originals = new Dictionary<string, object>(StringComparer.Ordinal);

        internal static IReadOnlyList<Entry> All => Entries;
        internal static int Count => Entries.Count;

        internal static string KeyOf(object target, string member)
        {
            int id = target is UnityEngine.Object uo ? uo.GetInstanceID() : RuntimeHelpers.GetHashCode(target);
            return id + "|" + member;
        }

        internal static void Record(object target, string label, string member, object before, object after, Func<object> get, Action<object> set)
        {
            string key = KeyOf(target, member);
            if (!Originals.ContainsKey(key))
            {
                Originals[key] = before;
            }
            Entries.Add(new Entry { Key = key, Label = label, Member = member, Before = before, After = after, Time = DateTime.Now, Get = get, Set = set, Target = target, Where = InspectorExport.PlaceOf(target, member) });
            if (Entries.Count > 500)
            {
                Entries.RemoveAt(0);
            }
        }

        internal static bool TryOriginal(object target, string member, out object original)
        {
            return Originals.TryGetValue(KeyOf(target, member), out original);
        }

        // What the member held just before its latest edit.
        internal static bool TryPrevious(object target, string member, out object previous)
        {
            string key = KeyOf(target, member);
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].Key == key)
                {
                    previous = Entries[i].Before;
                    return true;
                }
            }
            previous = null;
            return false;
        }

        internal static void ForgetOriginal(object target, string member)
        {
            Originals.Remove(KeyOf(target, member));
        }

        internal static List<Entry> For(object target, string member)
        {
            string key = KeyOf(target, member);
            return Entries.FindAll(e => e.Key == key);
        }

        // Puts back the value before the entry; the entry stays, marked.
        internal static string Revert(Entry e)
        {
            try
            {
                e.Set(e.Before);
                e.Reverted = true;
                return null;
            }
            catch (Exception ex)
            {
                return (ex.InnerException ?? ex).Message;
            }
        }

        internal static string Reapply(Entry e)
        {
            try
            {
                e.Set(e.After);
                e.Reverted = false;
                return null;
            }
            catch (Exception ex)
            {
                return (ex.InnerException ?? ex).Message;
            }
        }

        // The latest edit that still stands.
        internal static string Undo()
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (!Entries[i].Reverted)
                {
                    return Revert(Entries[i]) ?? $"Reverted {Entries[i].Member}.";
                }
            }
            return "Nothing to undo.";
        }

        internal static void Clear()
        {
            Entries.Clear();
            Originals.Clear();
        }
    }
}
