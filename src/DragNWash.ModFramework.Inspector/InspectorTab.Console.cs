using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Node = DragNWash.ModFramework.Inspector.InspectorModel.Node;
using Member = DragNWash.ModFramework.Inspector.InspectorModel.Member;

namespace DragNWash.ModFramework.Inspector
{
    // The "inspect" console command: with no arguments the tree, "pick" to
    // start pick mode, "object"/"set" and a path/component/member lookup,
    // and completion for all of the above.
    internal static partial class InspectorTab
    {
        private static string Command(string[] args)
        {
            if (args.Length == 0)
            {
                var sb = new StringBuilder();
                foreach (Node n in InspectorModel.BuildTree(new HashSet<int>()))
                {
                    sb.Append(n.Transform == null ? n.Name.ToUpperInvariant() : "  " + n.Name + (n.HasChildren ? "/" : "")).Append('\n');
                }
                return sb.ToString().TrimEnd();
            }
            if (args[0].Equals("pick", StringComparison.OrdinalIgnoreCase))
            {
                TW.Open(Title);
                InspectorPick.Begin();
                return "Click an object in the game; Escape cancels.";
            }
            if (args[0].Equals("object", StringComparison.OrdinalIgnoreCase))
            {
                return InspectObjectCommand(args);
            }
            if (args[0].Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 5)
                {
                    return "inspect set <path> <component> <member> <value>";
                }
                GameObject go = InspectorModel.Find(args[1]);
                if (go == null) return $"No object at \"{args[1]}\".";
                object target = FindComponent(go, args[2]);
                if (target == null) return $"{go.name} has no component \"{args[2]}\".";
                Member member = null;
                foreach (Member m in target is Material mm ? InspectorModel.MaterialMembers(mm) : InspectorModel.MembersOf(target.GetType()))
                {
                    if (m.Name.Equals(args[3], StringComparison.OrdinalIgnoreCase)) { member = m; break; }
                }
                if (member == null) return $"{args[2]} has no member \"{args[3]}\".";
                if (!member.CanWrite) return $"{args[3]} is read-only.";
                string valueText = string.Join(" ", args, 4, args.Length - 4);
                object parsed = InspectorModel.Parse(member.Type, valueText, out string error);
                if (error != null) return $"Not accepted: {error}.";
                try
                {
                    member.Set(target, parsed);
                }
                catch (Exception ex)
                {
                    return $"{args[3]}: {(ex.InnerException ?? ex).Message}";
                }
                return $"{InspectorModel.PathOf(go.transform)} {args[2]}.{member.Name} = {InspectorModel.Format(member.Get(target))}";
            }
            GameObject found = InspectorModel.Find(args[0]);
            if (found == null)
            {
                return $"No object named or at \"{args[0]}\".";
            }
            SelectObject(found);
            _page = 1;
            if (args.Length > 1)
            {
                object c = FindComponent(found, args[1]);
                if (c == null) return $"{found.name} has no component \"{args[1]}\".";
                SetTarget(c);
                _page = 2;
                TW.Open(Title);
                var lines = new StringBuilder();
                foreach (Member m in MembersOfTarget())
                {
                    if (m.IsPrivate) continue;
                    lines.Append(m.Name).Append(" = ").Append(InspectorModel.Format(SafeGet(m, () => m.Get(_target)))).Append('\n');
                }
                return lines.ToString().TrimEnd();
            }
            TW.Open(Title);
            var names = new List<string>();
            foreach (Component c in found.GetComponents<Component>())
            {
                names.Add(c == null ? "(missing)" : c.GetType().Name);
            }
            return $"{InspectorModel.PathOf(found.transform)}: {string.Join(", ", names)}";
        }

        private static object FindComponent(GameObject go, string name)
        {
            if (name.Equals("GameObject", StringComparison.OrdinalIgnoreCase))
            {
                return go;
            }
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c != null && c.GetType().Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }
            foreach (Renderer r in go.GetComponents<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null && m.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return m;
                    }
                }
            }
            return null;
        }

        private static IEnumerable<string> Complete(string[] args)
        {
            if (args.Length == 1)
            {
                var names = new List<string> { "set", "pick", "object" };
                string partial = args[0];
                if (partial.Length >= 2)
                {
                    foreach (Node n in InspectorModel.Search(partial, 30))
                    {
                        names.Add(n.Path.IndexOf(' ') >= 0 ? "\"" + n.Path + "\"" : n.Path);
                    }
                }
                return names;
            }
            if (args[0].Equals("object", StringComparison.OrdinalIgnoreCase))
            {
                return ObjectsComplete(args);
            }
            bool set = args[0].Equals("set", StringComparison.OrdinalIgnoreCase);
            int pathIndex = set ? 1 : 0;
            if (args.Length == pathIndex + 1 && set)
            {
                var names = new List<string>();
                if (args[1].Length >= 2)
                {
                    foreach (Node n in InspectorModel.Search(args[1], 30))
                    {
                        names.Add(n.Path.IndexOf(' ') >= 0 ? "\"" + n.Path + "\"" : n.Path);
                    }
                }
                return names;
            }
            if (args.Length == pathIndex + 2)
            {
                GameObject go = InspectorModel.Find(args[pathIndex]);
                var names = new List<string>();
                if (go != null)
                {
                    names.Add("GameObject");
                    foreach (Component c in go.GetComponents<Component>())
                    {
                        if (c != null) names.Add(c.GetType().Name);
                    }
                }
                return names;
            }
            if (set && args.Length == 4)
            {
                GameObject go = InspectorModel.Find(args[1]);
                object target = go != null ? FindComponent(go, args[2]) : null;
                var names = new List<string>();
                if (target != null)
                {
                    foreach (Member m in target is Material mm ? InspectorModel.MaterialMembers(mm) : InspectorModel.MembersOf(target.GetType()))
                    {
                        if (m.CanWrite && !m.IsPrivate) names.Add(m.Name);
                    }
                }
                return names;
            }
            return new string[0];
        }
    }
}
