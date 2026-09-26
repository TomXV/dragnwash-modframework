using System;
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
            RegisterDiagnostics(g);
            RegisterEvents(g);
        }

        // Memory and managed stacks (experimental, core 1.7).
        private static void RegisterDiagnostics(string g)
        {
            Operations.Register(g, "diagnostics.memory.get", "The game's memory now (managed heap, Unity's totals), and optionally the readings of the last minutes (taken while Developer tools are on).", OperationKind.Read,
                "{ now: sample, gc_mode, history: [ sample ] } where a sample is { time, gc_used, gc_reserved, collections, gc_ran, unity_allocated, unity_reserved, system_used, audio, video } in bytes (-1: not known)", args =>
                {
                    int seconds = Math.Max(0, Math.Min(Diagnostics.MemoryWatch.Capacity, args.Int("history", 0)));
                    var history = new List<object>();
                    if (seconds > 0)
                    {
                        IReadOnlyList<Diagnostics.MemorySample> h = Diagnostics.MemoryWatch.History;
                        for (int i = Math.Max(0, h.Count - seconds); i < h.Count; i++) history.Add(Sample(h[i]));
                    }
                    return new Dictionary<string, object>
                    {
                        ["now"] = Sample(Diagnostics.MemoryWatch.Now()),
                        ["gc_mode"] = Diagnostics.MemoryWatch.GcMode(),
                        ["history"] = history,
                    };
                },
                Operations.Parameter("history", OperationType.Number, "How many seconds of past readings, newest last (0 to 300; none when left out)."));
            Operations.Register(g, "diagnostics.stacks.get", "Where each managed thread is now, main thread first. Frames are Type.Method, innermost first, with (+IL offset) where Mono knows it.", OperationKind.Read,
                "a list of { id, name, main, frames: [ text ] }", args =>
                {
                    string reason;
                    List<Diagnostics.ThreadStack> threads = args.Bool("native") ? Diagnostics.NativeStacks.CaptureAll(out reason) : Diagnostics.ManagedStacks.Capture(out reason);
                    if (threads == null) throw new InvalidOperationException("Stacks could not be read: " + reason);
                    bool mainOnly = (args.String("thread") ?? "all").Equals("main", StringComparison.OrdinalIgnoreCase);
                    int max = Math.Max(0, args.Int("frames", 0));
                    return threads.Where(t => !mainOnly || t.IsMain).Select(t => (object)new Dictionary<string, object>
                    {
                        ["id"] = t.Id,
                        ["name"] = t.Name,
                        ["main"] = t.IsMain,
                        ["frames"] = (max > 0 ? t.Frames.Take(max) : t.Frames).Cast<object>().ToList(),
                    }).ToList();
                },
                Operations.Parameter("thread", OperationType.String, "main for the main thread only; all when left out.", false, "main", "all"),
                Operations.Parameter("frames", OperationType.Number, "At most this many frames per thread; all when left out."),
                Operations.Parameter("native", OperationType.Boolean, "Every thread with native frames too (64-bit Windows only; ids are then Windows thread ids)."));
            Operations.Register(g, "diagnostics.sampler.start", "Starts sampling where the main thread is, 5 or 10 times a second, clearing earlier samples. Each sample stops the main thread for well under a millisecond.", OperationKind.Write,
                "{ running, per_second, native }", args =>
                {
                    Diagnostics.MainThreadSampler.Start(args.Int("per_second", 10), args.Bool("native"));
                    return new Dictionary<string, object> { ["running"] = true, ["per_second"] = Diagnostics.MainThreadSampler.PerSecond, ["native"] = Diagnostics.MainThreadSampler.Native };
                },
                Operations.Parameter("per_second", OperationType.Number, "5 or 10 (10 when left out)."),
                Operations.Parameter("native", OperationType.Boolean, "With native frames (64-bit Windows only)."));
            Operations.Register(g, "diagnostics.sampler.stop", "Stops the main-thread sampler; the samples stay for diagnostics.sampler.report.", OperationKind.Write,
                "{ samples }", args =>
                {
                    Diagnostics.MainThreadSampler.Stop();
                    return new Dictionary<string, object> { ["samples"] = Diagnostics.MainThreadSampler.Count };
                });
            Operations.Register(g, "diagnostics.sampler.report", "Where the main-thread sampler found the main thread most: self (innermost) or total (anywhere on the stack), optionally only mods' code.", OperationKind.Read,
                "{ running, samples, per_second, native, rows: [ { frame, self, total } ] } with self and total as sample counts", args =>
                {
                    var rows = Diagnostics.MainThreadSampler.Report(args.Bool("total"), args.Bool("mods_only"), Math.Max(1, Math.Min(200, args.Int("max", 30))))
                        .Select(r => (object)new Dictionary<string, object> { ["frame"] = r.Frame, ["self"] = r.Self, ["total"] = r.Total }).ToList();
                    return new Dictionary<string, object>
                    {
                        ["running"] = Diagnostics.MainThreadSampler.Running,
                        ["samples"] = Diagnostics.MainThreadSampler.Count,
                        ["per_second"] = Diagnostics.MainThreadSampler.PerSecond,
                        ["native"] = Diagnostics.MainThreadSampler.Native,
                        ["rows"] = rows,
                    };
                },
                Operations.Parameter("total", OperationType.Boolean, "Sort by total instead of self."),
                Operations.Parameter("mods_only", OperationType.Boolean, "Only frames in mods' code (BepInEx/plugins)."),
                Operations.Parameter("max", OperationType.Number, "How many rows (30 when left out, at most 200)."));
            Operations.Register(g, "diagnostics.types.count", "Experimental: counts the managed objects reachable from static fields, by type. The whole game stops while it walks (a tenth of a second to a second).", OperationKind.Write,
                "{ time, objects, bytes, seconds, truncated, types: [ { name, assembly, count, bytes, change } ] }", args =>
                {
                    Diagnostics.HeapCount c = Diagnostics.HeapTypes.Count();
                    bool mods = args.Bool("mods_only");
                    var types = c.Types.Where(t => !mods || Diagnostics.HeapTypes.IsModAssembly(t.Assembly)).Take(Math.Max(1, Math.Min(500, args.Int("top", 50))))
                        .Select(t => (object)new Dictionary<string, object> { ["name"] = t.Name, ["assembly"] = t.Assembly, ["count"] = t.Count, ["bytes"] = t.Bytes, ["change"] = t.Change }).ToList();
                    return new Dictionary<string, object>
                    {
                        ["time"] = c.Time.ToString("HH:mm:ss"),
                        ["objects"] = c.Objects,
                        ["bytes"] = c.Bytes,
                        ["seconds"] = c.Seconds,
                        ["truncated"] = c.Truncated,
                        ["types"] = types,
                    };
                },
                Operations.Parameter("top", OperationType.Number, "How many types, the most bytes first (50 when left out, at most 500)."),
                Operations.Parameter("mods_only", OperationType.Boolean, "Only types from mods' assemblies (BepInEx/plugins)."));
            Operations.Register(g, "diagnostics.snapshot", "Writes a snapshot into BepInEx/CrashReports: a memory dump (Windows), every managed thread's stack, the memory numbers and the loaded modules. Holds the game for a second or two.", OperationKind.Write,
                "{ folder, summary }", args =>
                {
                    string summary = Diagnostics.Snapshot.Write(out string folder);
                    return new Dictionary<string, object> { ["folder"] = folder, ["summary"] = summary };
                });
        }

        private static Dictionary<string, object> Sample(Diagnostics.MemorySample s)
        {
            return new Dictionary<string, object>
            {
                ["time"] = s.Time.ToString("HH:mm:ss"),
                ["gc_used"] = s.GcUsed,
                ["gc_reserved"] = s.GcReserved,
                ["collections"] = s.Collections,
                ["gc_ran"] = s.GcRan,
                ["unity_allocated"] = s.UnityAllocated,
                ["unity_reserved"] = s.UnityReserved,
                ["system_used"] = s.SystemUsed,
                ["audio"] = s.Audio,
                ["video"] = s.Video,
            };
        }

        // The core's events, handed to the registry so anything built on it (a
        // graph of a data mod) can answer them by name. The core hears them
        // through GameEvents like any mod, so a listener that throws is logged
        // and the game's own event is never touched. Quitting is left out: a run
        // cannot wait while the game quits, and nothing it did would be seen.
        private static void RegisterEvents(string g)
        {
            Operations.RegisterEvent(g, "game.started", "Once, when the title screen is first shown.");
            Operations.RegisterEvent(g, "scene.loaded", "After a scene is loaded.",
                Operations.Parameter("scene", OperationType.String, "The scene's name."),
                Operations.Parameter("mode", OperationType.String, "How it was loaded: Single or Additive."));
            Operations.RegisterEvent(g, "scene.unloaded", "After a scene is unloaded.",
                Operations.Parameter("scene", OperationType.String, "The scene's name."));
            GameEvents.OnGameStarted(g, () => Operations.Raise("game.started"));
            GameEvents.OnSceneLoaded(g, (scene, mode) => Operations.Raise("scene.loaded", new Dictionary<string, object>
            {
                ["scene"] = scene.name,
                ["mode"] = mode.ToString(),
            }));
            GameEvents.OnSceneUnloaded(g, scene => Operations.Raise("scene.unloaded", new Dictionary<string, object>
            {
                ["scene"] = scene.name,
            }));
        }
    }
}
