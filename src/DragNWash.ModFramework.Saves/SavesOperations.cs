using System;
using System.Collections.Generic;
using System.Linq;

namespace DragNWash.ModFramework.Saves
{
    // The flags and saves library's operations (docs/API_PLAN.md, stage 1): all read.
    internal static class SavesOperations
    {
        internal static void Register()
        {
            string g = GameSaves.Guid;
            Operations.Register(g, "saves.list", "The game's save slots, with the level each is at and how many copies the history keeps.", OperationKind.Read,
                "a list of { slot, name, level, copies }", args =>
                    GameSaves.Slots().Select(slot => (object)new Dictionary<string, object>
                    {
                        ["slot"] = slot,
                        ["name"] = GameSaves.ShortName(slot),
                        ["level"] = GameSaves.ReadLevel(slot),
                        ["copies"] = GameSaves.Snapshots(slot).Count,
                    }).ToList());

            Operations.Register(g, "saves.flags.list", "The event flags in a save slot, with what the flag catalog says about each.", OperationKind.Read,
                "a list of { id, value, group, set_by, description }", args =>
                {
                    string slot = Slot(args.String("slot"));
                    string filter = args.String("filter");
                    return GameSaves.ReadFlags(slot)
                        .Where(f => filter == null || f.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(f => (object)Describe(f.Id, f.Value)).ToList();
                },
                Operations.Parameter("slot", OperationType.String, "The slot (saves.list gives them; a number 1 to 3 works too).", true),
                Operations.Parameter("filter", OperationType.String, "Only flags whose id contains this."));

            Operations.Register(g, "saves.flags.get", "One event flag in a save slot.", OperationKind.Read,
                "{ id, value, group, set_by, description }", args =>
                {
                    string slot = Slot(args.String("slot"));
                    string id = args.String("id");
                    List<SaveFlag> flags = GameSaves.ReadFlags(slot);
                    int at = flags.FindIndex(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (at < 0) throw new InvalidOperationException($"{GameSaves.ShortName(slot)} has no flag {id}.");
                    return Describe(flags[at].Id, flags[at].Value);
                },
                Operations.Parameter("slot", OperationType.String, "The slot (saves.list gives them; a number 1 to 3 works too).", true),
                Operations.Parameter("id", OperationType.String, "The flag's id.", true));
        }

        private static Dictionary<string, object> Describe(string id, bool value)
        {
            FlagInfo info = GameFlags.Find(id);
            return new Dictionary<string, object>
            {
                ["id"] = id,
                ["value"] = value,
                ["group"] = info?.Group,
                ["set_by"] = info?.SetBy,
                ["description"] = info?.Description,
            };
        }

        // A slot by its name, or by its number.
        private static string Slot(string given)
        {
            List<string> slots = GameSaves.Slots();
            string found = slots.FirstOrDefault(s => string.Equals(s, given, StringComparison.OrdinalIgnoreCase))
                ?? slots.FirstOrDefault(s => s.EndsWith("slot" + given, StringComparison.OrdinalIgnoreCase));
            if (found == null) throw new InvalidOperationException($"No save slot \"{given}\". There are: {string.Join(", ", slots.ToArray())}.");
            return found;
        }
    }
}
