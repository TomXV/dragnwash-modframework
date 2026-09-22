using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace DragNWash.ModFramework
{
    // Where the first frame after the first scene goes. When that scene has
    // loaded, this patches, for that one frame only, each loaded plugin's
    // Start (a coroutine Start up to its first yield), Update, LateUpdate and
    // OnGUI, and each sceneLoaded handler a mod subscribed after the core's,
    // with a timing prefix and postfix. The frame over, the patches come off
    // again, so nothing of it runs after that. What the patched methods do
    // not account for is the game's own and Unity's.
    internal static partial class StartupTiming
    {
        private static readonly string[] Messages = { "Start", "Update", "LateUpdate", "OnGUI" };
        private const string StartToYield = "Start (to its first yield)";

        private sealed class Share
        {
            internal string Label;
            internal double Ms;
            internal int Calls;
            internal bool ShowCalls;
        }

        private static Harmony _probe;
        private static bool _recording, _probeFailed;
        private static int _mainThread;
        // Main thread only while recording; the maps are only read then.
        private static readonly Dictionary<string, Share> Shares = new Dictionary<string, Share>();
        private static readonly Dictionary<object, string> PluginOf = new Dictionary<object, string>();
        private static readonly Dictionary<RuntimeMethodHandle, string> MessageLabel = new Dictionary<RuntimeMethodHandle, string>();
        private static readonly Dictionary<RuntimeMethodHandle, string> StepOwner = new Dictionary<RuntimeMethodHandle, string>();
        private static readonly Dictionary<RuntimeMethodHandle, string> HandlerLabel = new Dictionary<RuntimeMethodHandle, string>();
        private static readonly HashSet<RuntimeMethodHandle> Probed = new HashSet<RuntimeMethodHandle>();
        private static readonly HashSet<object> SeenSteps = new HashSet<object>();

        // The result, kept for the block written 10 s after the scene.
        private static List<Share> _firstFrame;
        private static long _probeStart, _probeEnd, _unprobeStart, _unprobeEnd;
        private static double _ownInFrameMs;
        private static int _untimed, _handlersBefore;
        private static string _handlersNote;

        // In the core's sceneLoaded handler for the first scene, before its first frame.
        private static void StartProbes()
        {
            _probeStart = Stopwatch.GetTimestamp();
            try
            {
                _probe = new Harmony(ModFramework.Guid + ".startuptiming");
                var ours = new Dictionary<Assembly, string>();
                foreach (PluginInfo info in Chainloader.PluginInfos.Values)
                {
                    BaseUnityPlugin instance = info?.Instance;
                    if (instance == null) continue;
                    string name = info.Metadata.Name + " " + info.Metadata.Version;
                    Type type = instance.GetType();
                    PluginOf[instance] = name;
                    if (!ours.ContainsKey(type.Assembly)) ours[type.Assembly] = name;
                    foreach (string message in Messages)
                    {
                        MethodInfo method = FindMessage(type, message);
                        if (method == null) continue;
                        bool coroutine = message == "Start" && method.ReturnType == typeof(IEnumerator);
                        MethodInfo step = coroutine ? FirstStep(method) : null;
                        string label = step != null ? StartToYield : coroutine ? "Start (a coroutine; only until it returns)" : message;
                        MessageLabel[method.MethodHandle] = label;
                        Probe(method, nameof(AfterMessage));
                        if (step != null)
                        {
                            StepOwner[step.MethodHandle] = name;
                            Probe(step, nameof(AfterStep));
                        }
                    }
                }
                ProbeSceneLoadedHandlers(ours);
                _recording = true;
            }
            catch (Exception ex)
            {
                StopProbes("could not start: " + ex.GetType().Name + ": " + ex.Message);
            }
            _probeEnd = Stopwatch.GetTimestamp();
        }

        // Unity calls the most derived method of that name, whatever its access.
        private static MethodInfo FindMessage(Type type, string name)
        {
            for (Type t = type; t != null && t != typeof(BaseUnityPlugin); t = t.BaseType)
            {
                MethodInfo method = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method != null) return method.IsAbstract || method.ContainsGenericParameters ? null : method;
            }
            return null;
        }

        // The iterator's MoveNext: its first call runs Start up to the first yield.
        private static MethodInfo FirstStep(MethodInfo start)
        {
            Type machine = start.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            return machine?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
        }

        // The handlers after the core's in the list run after this; those before
        // it have run already and stay in the rest. Only reads the event's field:
        // the handlers themselves are left as they are, so -= keeps working.
        private static void ProbeSceneLoadedHandlers(Dictionary<Assembly, string> ours)
        {
            FieldInfo field = typeof(SceneManager).GetField("sceneLoaded", BindingFlags.Static | BindingFlags.NonPublic);
            if (!(field?.GetValue(null) is Delegate all))
            {
                _handlersNote = "the sceneLoaded handlers could not be read";
                return;
            }
            Delegate[] list = all.GetInvocationList();
            RuntimeMethodHandle mine = ((Action<Scene, LoadSceneMode>)OnSceneLoaded).Method.MethodHandle;
            int at = Array.FindIndex(list, d => d.Target == null && d.Method.MethodHandle.Equals(mine));
            if (at < 0)
            {
                _handlersNote = "the core's sceneLoaded handler was not in the list, so the others were not timed";
                return;
            }
            for (int i = 0; i < list.Length; i++)
            {
                if (i == at) continue;
                MethodInfo method = list[i].Method;
                Type declaring = method.DeclaringType;
                string owner = declaring == null ? null : OwnerOf(declaring.Assembly, ours);
                if (owner == null) continue;
                if (i < at) { _handlersBefore++; continue; }
                if (method.ContainsGenericParameters) { _untimed++; continue; }
                HandlerLabel[method.MethodHandle] = owner + ": sceneLoaded -> " + ShortName(declaring) + "." + method.Name;
                Probe(method, nameof(AfterHandler));
            }
        }

        // A plugin's, or any other assembly loaded from the BepInEx folder; null for the game's and Unity's.
        private static string OwnerOf(Assembly assembly, Dictionary<Assembly, string> ours)
        {
            if (ours.TryGetValue(assembly, out string name)) return name;
            try
            {
                if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)) return null;
                string root = Path.GetFullPath(Paths.BepInExRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(assembly.Location).StartsWith(root, StringComparison.OrdinalIgnoreCase) ? assembly.GetName().Name : null;
            }
            catch
            {
                return null;
            }
        }

        // A lambda's compiler-made class goes by the class it was written in.
        private static string ShortName(Type type)
        {
            while (type.IsNested && type.Name.StartsWith("<", StringComparison.Ordinal)) type = type.DeclaringType;
            return type.Name;
        }

        private static void Probe(MethodInfo target, string after)
        {
            if (!Probed.Add(target.MethodHandle)) return;
            try
            {
                _probe.Patch(target, prefix: new HarmonyMethod(typeof(StartupTiming), nameof(Before)), postfix: new HarmonyMethod(typeof(StartupTiming), after));
            }
            catch
            {
                _untimed++;
            }
        }

        // ---- the patches: main thread, while recording, never let anything out ----

        private static void Before(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void AfterMessage(long __state, object __instance, MethodBase __originalMethod)
        {
            long now = Stopwatch.GetTimestamp();
            if (!_recording) return;
            try
            {
                if (Thread.CurrentThread.ManagedThreadId != _mainThread || __instance == null) return;
                // Another component of a plugin's type is not the plugin.
                if (!PluginOf.TryGetValue(__instance, out string plugin)) return;
                string label = MessageLabel.TryGetValue(__originalMethod.MethodHandle, out string l) ? l : __originalMethod.Name;
                Count(plugin + ": " + label, Ms(__state, now), !label.StartsWith("Start", StringComparison.Ordinal));
            }
            catch
            {
                _probeFailed = true;
            }
        }

        private static void AfterStep(long __state, object __instance, MethodBase __originalMethod)
        {
            long now = Stopwatch.GetTimestamp();
            if (!_recording) return;
            try
            {
                if (Thread.CurrentThread.ManagedThreadId != _mainThread || __instance == null || !SeenSteps.Add(__instance)) return;
                if (StepOwner.TryGetValue(__originalMethod.MethodHandle, out string plugin)) Count(plugin + ": " + StartToYield, Ms(__state, now), false);
            }
            catch
            {
                _probeFailed = true;
            }
        }

        private static void AfterHandler(long __state, MethodBase __originalMethod)
        {
            long now = Stopwatch.GetTimestamp();
            if (!_recording) return;
            try
            {
                if (Thread.CurrentThread.ManagedThreadId != _mainThread) return;
                if (HandlerLabel.TryGetValue(__originalMethod.MethodHandle, out string label)) Count(label, Ms(__state, now), true);
            }
            catch
            {
                _probeFailed = true;
            }
        }

        private static void Count(string label, double ms, bool showCalls)
        {
            if (!Shares.TryGetValue(label, out Share share)) Shares[label] = share = new Share { Label = label, ShowCalls = showCalls };
            share.Ms += ms;
            share.Calls++;
        }

        // ---- ending --------------------------------------------------------------

        // At the end of the first frame: keeps what was counted and takes the patches off.
        private static void EndProbes()
        {
            if (!_recording) return;
            _recording = false;
            if (_probeFailed)
            {
                StopProbes("a timing patch failed");
                return;
            }
            _firstFrame = Shares.Values.OrderByDescending(s => s.Ms).ToList();
            StopProbes(null);
        }

        // Takes every timing patch off; with a reason, the breakdown is dropped and says so once.
        private static void StopProbes(string reason)
        {
            _recording = false;
            if (reason != null) _firstFrame = null;
            try
            {
                if (_probe != null)
                {
                    _unprobeStart = Stopwatch.GetTimestamp();
                    _probe.UnpatchSelf();
                    _unprobeEnd = Stopwatch.GetTimestamp();
                }
            }
            catch (Exception ex)
            {
                reason = reason ?? "the timing patches could not all be removed: " + ex.GetType().Name + ": " + ex.Message;
                _firstFrame = null;
            }
            _probe = null;
            Shares.Clear();
            PluginOf.Clear();
            MessageLabel.Clear();
            StepOwner.Clear();
            HandlerLabel.Clear();
            Probed.Clear();
            SeenSteps.Clear();
            if (reason != null)
            {
                try
                {
                    _log?.LogDebug(Prefix + "First-frame breakdown stopped: " + reason);
                }
                catch
                {
                }
            }
        }

        private static void AddFirstFrame(StringBuilder sb, long frameEnd)
        {
            if (_firstFrame == null) return;
            double frame = Ms(_sceneLoaded, frameEnd);
            Add(sb, $"The first frame, {Format(frame)} ms from the scene load to the next frame's coroutines, by where it went (each plugin's Start, Update, LateUpdate and OnGUI, and the sceneLoaded handlers mods added):");
            double accounted = 0;
            int small = 0;
            double smallMs = 0;
            foreach (Share share in _firstFrame)
            {
                accounted += share.Ms;
                if (share.Ms < 1) { small++; smallMs += share.Ms; continue; }
                string calls = share.ShowCalls && share.Calls > 1 ? $" ({share.Calls} calls)" : "";
                Add(sb, $"  {Format(share.Ms),6} ms  {share.Label}{calls}");
            }
            if (small > 0) Add(sb, $"  {Format(smallMs),6} ms  {small} more under 1 ms each");
            double patching = Ms(_probeStart, _probeEnd);
            double own = patching + _ownInFrameMs;
            string block = _ownInFrameMs >= 0.5 ? $", writing the chainloader block {Format(_ownInFrameMs)} ms" : "";
            Add(sb, $"  {Format(own),6} ms  this timing itself (putting on its patches {Format(patching)} ms{block})");
            Add(sb, $"  {Format(Math.Max(0, frame - accounted - own)),6} ms  the rest: the game's own and Unity's (its scripts and sceneLoaded handlers, scene start-up, rendering)");
            if (_unprobeEnd != 0) Add(sb, $"Taking the patches off again took {Format(Ms(_unprobeStart, _unprobeEnd))} ms, at {At(_unprobeEnd)}.");
            if (_handlersBefore > 0) Add(sb, $"({_handlersBefore} sceneLoaded handler{(_handlersBefore == 1 ? "" : "s")} from mods ran before the core's, so before this could time them; they are in the rest.)");
            if (_untimed > 0) Add(sb, $"({_untimed} method{(_untimed == 1 ? "" : "s")} could not be timed; they are in the rest.)");
            if (_handlersNote != null) Add(sb, $"({_handlersNote}; they are in the rest.)");
        }
    }
}
