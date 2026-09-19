using System;
using System.Collections.Generic;
using System.Linq;

namespace DragNWash.ModFramework.Assets
{
    // The Assets library's operations (docs/API_PLAN.md, stage 1): all read.
    internal static class AssetsOperations
    {
        internal static void Register()
        {
            string g = GameFonts.Guid;
            OperationParameter filter = Operations.Parameter("filter", OperationType.String, "Only names that contain this.");
            OperationParameter max = Operations.Parameter("max", OperationType.Number, "At most this many (1 to 1000; 200 when left out).");

            Operations.Register(g, "assets.textures.list", "The textures loaded in memory, by name, with their size.", OperationKind.Read,
                "a list of { name, width, height }", args =>
                    Take(AssetCatalog.Textures().Where(t => Match(t.Name, args)), args)
                        .Select(t => (object)new Dictionary<string, object> { ["name"] = t.Name, ["width"] = t.Width, ["height"] = t.Height }).ToList(),
                filter, max);

            Operations.Register(g, "assets.materials.list", "The materials loaded in memory, with their shader and the textures they use.", OperationKind.Read,
                "a list of { name, shader, textures: { property: texture } }", args =>
                    Take(AssetCatalog.Materials().Where(m => Match(m.Name, args)), args)
                        .Select(m => (object)new Dictionary<string, object>
                        {
                            ["name"] = m.Name,
                            ["shader"] = m.Shader,
                            ["textures"] = m.Textures.GroupBy(kv => kv.Key).ToDictionary(k => k.Key, k => (object)k.First().Value),
                        }).ToList(),
                filter, max);

            Operations.Register(g, "assets.meshes.list", "The meshes loaded in memory, with their vertex and sub-mesh counts.", OperationKind.Read,
                "a list of { name, vertices, submeshes }", args =>
                    Take(AssetCatalog.Meshes().Where(m => Match(m.Name, args)), args)
                        .Select(m => (object)new Dictionary<string, object> { ["name"] = m.Name, ["vertices"] = m.Vertices, ["submeshes"] = m.SubMeshes }).ToList(),
                filter, max);

            Operations.Register(g, "assets.replacements.list", "The texture replacements mods ship: which game texture, from which mod, for which language, and the file.", OperationKind.Read,
                "a list of { name, mod, language, file }", args =>
                    AssetReplacements.AllRead().Where(r => Match(r.Name, args))
                        .Select(r => (object)new Dictionary<string, object> { ["name"] = r.Name, ["mod"] = r.Mod, ["language"] = r.Language, ["file"] = r.Path }).ToList(),
                filter);

            Operations.Register(g, "assets.fonts.language", "The language the fonts are prepared for (what mods set with GameFonts.SetLanguage).", OperationKind.Read,
                "{ language }", args => new Dictionary<string, object> { ["language"] = GameFonts.Language });
        }

        private static bool Match(string name, OperationArgs args)
        {
            string f = args.String("filter");
            return f == null || (name ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<T> Take<T>(IEnumerable<T> items, OperationArgs args)
        {
            return items.Take(Math.Max(1, Math.Min(1000, args.Int("max", 200))));
        }
    }
}
