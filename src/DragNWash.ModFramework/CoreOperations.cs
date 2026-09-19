using System.Collections.Generic;
using System.IO;
using System.Linq;
using DragNWash.ModFramework.Mods;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DragNWash.ModFramework
{
    // The core's operations (docs/API_PLAN.md, stage 1): all read.
    internal static class CoreOperations
    {
        internal static void Register()
        {
            string g = ModFramework.Guid;
            Operations.Register(g, "mods.list", "The mods on the Mods screen: loaded or not, libraries, versions, authors.", OperationKind.Read,
                "a list of { guid, name, version, loaded, on_next_launch, library, authors, description }", args =>
                {
                    return ModCatalog.Build().Where(e => !e.IsPatcher).Select(e => (object)new Dictionary<string, object>
                    {
                        ["guid"] = e.Guid,
                        ["name"] = e.DisplayName,
                        ["version"] = e.Version,
                        ["loaded"] = e.Loaded,
                        ["on_next_launch"] = e.WantOn,
                        ["library"] = e.IsLibrary,
                        ["authors"] = e.Authors,
                        ["description"] = e.Description,
                    }).ToList();
                });
            Operations.Register(g, "mods.network", "Where mods connected this session (the framework's network watch), and whether each host is declared.", OperationKind.Read,
                "a list of { mod, host, via, count, declared }", args =>
                {
                    return NetworkWatch.All().Select(c => (object)new Dictionary<string, object>
                    {
                        ["mod"] = c.Guid,
                        ["host"] = c.Host,
                        ["via"] = c.Via,
                        ["count"] = c.Count,
                        ["declared"] = c.Declared,
                    }).ToList();
                });
            Operations.Register(g, "game.info", "The game and the framework: versions, graphics, screen, developer tools.", OperationKind.Read,
                "{ framework, unity, graphics, direct3d12, os, screen, developer_tools }", args => new Dictionary<string, object>
                {
                    ["framework"] = ModFramework.Version,
                    ["unity"] = GameInfo.UnityVersion,
                    ["graphics"] = GameInfo.GraphicsApi.ToString(),
                    ["direct3d12"] = GameInfo.IsDirect3D12,
                    ["os"] = SystemInfo.operatingSystem,
                    ["screen"] = $"{Screen.width}x{Screen.height} {Screen.fullScreenMode}",
                    ["developer_tools"] = DeveloperTools.Enabled,
                });
            Operations.Register(g, "scene.list", "The scenes loaded now, and every scene in the game's build.", OperationKind.Read,
                "{ active, loaded: [ { name, build_index, roots } ], build: [ names ] }", args =>
                {
                    var loaded = new List<object>();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        Scene s = SceneManager.GetSceneAt(i);
                        loaded.Add(new Dictionary<string, object> { ["name"] = s.name, ["build_index"] = s.buildIndex, ["roots"] = s.isLoaded ? s.rootCount : 0 });
                    }
                    var build = new List<object>();
                    for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                    {
                        build.Add(Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i)));
                    }
                    return new Dictionary<string, object> { ["active"] = SceneManager.GetActiveScene().name, ["loaded"] = loaded, ["build"] = build };
                });
        }
    }
}
