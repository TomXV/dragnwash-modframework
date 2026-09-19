using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Member = DragNWash.ModFramework.Inspector.InspectorModel.Member;

namespace DragNWash.ModFramework.Inspector
{
    // Animators, read and driven by reflection like the rigidbodies: the
    // animation module is not referenced, so this library still loads where it
    // is stripped. Experimental.
    //
    // A selected Animator gets its parameters (Float, Int, Bool, Trigger) and
    // its layer weights as rows above its own members, edited like any member
    // and kept in History; what each layer is playing (the clips, their
    // weights, how far through, a blend to the next state); and a pause with a
    // step and a time slider. The game sets many parameters every frame, so an
    // edit to one of those lasts until the game's next write.
    //
    // What Unity keeps only in the editor cannot be shown here: the state
    // machine (states and transitions) and the curves inside a clip. A state
    // is known by its hash only, so the clips it plays stand for its name.
    //
    // The pause sets the Animator's speed to 0 and remembers the speed it had;
    // it is put back on Resume, when the window closes and when developer
    // tools go off.
    internal static class InspectorAnimators
    {
        private sealed class Api
        {
            public Type Type;
            public PropertyInfo Parameters, LayerCount, Speed, Controller;
            public MethodInfo GetFloat, SetFloat, GetInteger, SetInteger, GetBool, SetBool, SetTrigger, ResetTrigger;
            public MethodInfo GetLayerName, GetLayerWeight, SetLayerWeight;
            public MethodInfo CurrentState, NextState, InTransition, CurrentClips, NextClips, Update, Play;
            public PropertyInfo ParamName, ParamType, ParamHash;
            public PropertyInfo StateHash, StateTime, StateLength, StateLoop;
            public PropertyInfo ClipOfInfo, ClipWeight, ClipLength;
        }

        private static Api _api;
        private static bool _looked;

        // Animators paused here, with the speed each had.
        private static readonly Dictionary<Component, float> PausedSpeeds = new Dictionary<Component, float>();

        // Seconds one Step moves a paused animator.
        internal const float StepSeconds = 1f / 30f;

        private static Api Look()
        {
            if (_looked)
            {
                return _api;
            }
            _looked = true;
            Type t = Type.GetType("UnityEngine.Animator, UnityEngine.AnimationModule");
            Type param = Type.GetType("UnityEngine.AnimatorControllerParameter, UnityEngine.AnimationModule");
            Type state = Type.GetType("UnityEngine.AnimatorStateInfo, UnityEngine.AnimationModule");
            Type clipInfo = Type.GetType("UnityEngine.AnimatorClipInfo, UnityEngine.AnimationModule");
            Type clip = Type.GetType("UnityEngine.AnimationClip, UnityEngine.AnimationModule");
            if (t == null || param == null || state == null || clipInfo == null)
            {
                return null;
            }
            Type[] i = { typeof(int) };
            _api = new Api
            {
                Type = t,
                Parameters = t.GetProperty("parameters"),
                LayerCount = t.GetProperty("layerCount"),
                Speed = t.GetProperty("speed"),
                Controller = t.GetProperty("runtimeAnimatorController"),
                GetFloat = t.GetMethod("GetFloat", i),
                SetFloat = t.GetMethod("SetFloat", new[] { typeof(int), typeof(float) }),
                GetInteger = t.GetMethod("GetInteger", i),
                SetInteger = t.GetMethod("SetInteger", new[] { typeof(int), typeof(int) }),
                GetBool = t.GetMethod("GetBool", i),
                SetBool = t.GetMethod("SetBool", new[] { typeof(int), typeof(bool) }),
                SetTrigger = t.GetMethod("SetTrigger", i),
                ResetTrigger = t.GetMethod("ResetTrigger", i),
                GetLayerName = t.GetMethod("GetLayerName", i),
                GetLayerWeight = t.GetMethod("GetLayerWeight", i),
                SetLayerWeight = t.GetMethod("SetLayerWeight", new[] { typeof(int), typeof(float) }),
                CurrentState = t.GetMethod("GetCurrentAnimatorStateInfo", i),
                NextState = t.GetMethod("GetNextAnimatorStateInfo", i),
                InTransition = t.GetMethod("IsInTransition", i),
                CurrentClips = t.GetMethod("GetCurrentAnimatorClipInfo", i),
                NextClips = t.GetMethod("GetNextAnimatorClipInfo", i),
                Update = t.GetMethod("Update", new[] { typeof(float) }),
                Play = t.GetMethod("Play", new[] { typeof(int), typeof(int), typeof(float) }),
                ParamName = param.GetProperty("name"),
                ParamType = param.GetProperty("type"),
                ParamHash = param.GetProperty("nameHash"),
                StateHash = state.GetProperty("fullPathHash"),
                StateTime = state.GetProperty("normalizedTime"),
                StateLength = state.GetProperty("length"),
                StateLoop = state.GetProperty("loop"),
                ClipOfInfo = clipInfo.GetProperty("clip"),
                ClipWeight = clipInfo.GetProperty("weight"),
                ClipLength = clip?.GetProperty("length"),
            };
            return _api;
        }

        internal static bool Available => Look() != null;

        internal static bool IsAnimator(object o) => o is Component c && Look() is Api api && api.Type.IsInstanceOfType(c);

        private static object Call(MethodInfo m, Component c, params object[] args)
        {
            if (m == null || c == null) return null;
            try { return m.Invoke(c, args); }
            catch { return null; }
        }

        private static object Get(PropertyInfo p, object o)
        {
            if (p == null || o == null) return null;
            try { return p.GetValue(o, null); }
            catch { return null; }
        }

        // ---- rows ----------------------------------------------------------------

        // The parameters and the layer weights, as rows for the members pane.
        internal static List<Member> Members(Component animator)
        {
            var rows = new List<Member>();
            Api api = Look();
            if (api == null || animator == null)
            {
                return rows;
            }
            if (Get(api.Parameters, animator) is Array parameters)
            {
                foreach (object p in parameters)
                {
                    string name = Get(api.ParamName, p) as string ?? "?";
                    int hash = Get(api.ParamHash, p) is int h ? h : StringToHash(name);
                    string kind = Get(api.ParamType, p)?.ToString() ?? "";
                    switch (kind)
                    {
                        case "Float":
                            rows.Add(new Member { Name = $"parameter  {name}", Type = typeof(float), CanWrite = true,
                                Get = o => Call(api.GetFloat, (Component)o, hash) ?? 0f,
                                Set = (o, v) => Call(api.SetFloat, (Component)o, hash, Convert.ToSingle(v)) });
                            break;
                        case "Int":
                            rows.Add(new Member { Name = $"parameter  {name}", Type = typeof(int), CanWrite = true,
                                Get = o => Call(api.GetInteger, (Component)o, hash) ?? 0,
                                Set = (o, v) => Call(api.SetInteger, (Component)o, hash, Convert.ToInt32(v)) });
                            break;
                        case "Bool":
                            rows.Add(new Member { Name = $"parameter  {name}", Type = typeof(bool), CanWrite = true,
                                Get = o => Call(api.GetBool, (Component)o, hash) ?? false,
                                Set = (o, v) => Call(api.SetBool, (Component)o, hash, (bool)v) });
                            break;
                        case "Trigger":
                            // True while set and not yet used by a transition.
                            rows.Add(new Member { Name = $"trigger  {name}", Type = typeof(bool), CanWrite = true,
                                Get = o => Call(api.GetBool, (Component)o, hash) ?? false,
                                Set = (o, v) => Call((bool)v ? api.SetTrigger : api.ResetTrigger, (Component)o, hash) });
                            break;
                    }
                }
            }
            int layers = Get(api.LayerCount, animator) is int n ? n : 0;
            for (int layer = 1; layer < layers; layer++)
            {
                int l = layer;
                string name = Call(api.GetLayerName, animator, l) as string ?? l.ToString();
                rows.Add(new Member { Name = $"layer weight  {name}", Type = typeof(float), CanWrite = true,
                    Get = o => Call(api.GetLayerWeight, (Component)o, l) ?? 0f,
                    Set = (o, v) => Call(api.SetLayerWeight, (Component)o, l, Mathf.Clamp01(Convert.ToSingle(v))) });
            }
            return rows;
        }

        // Animator.StringToHash, for a parameter whose hash could not be read.
        private static int StringToHash(string name)
        {
            MethodInfo m = Look()?.Type.GetMethod("StringToHash", new[] { typeof(string) });
            try { return m != null ? (int)m.Invoke(null, new object[] { name }) : 0; }
            catch { return 0; }
        }

        // ---- what is playing -------------------------------------------------------

        internal static int LayerCount(Component animator) => Get(Look()?.LayerCount, animator) is int n ? n : 0;

        internal static string LayerName(Component animator, int layer) => Call(Look()?.GetLayerName, animator, layer) as string ?? layer.ToString();

        internal static string ControllerName(Component animator)
        {
            return Get(Look()?.Controller, animator) is UnityEngine.Object controller && controller != null ? controller.name : "no controller";
        }

        internal static float Speed(Component animator) => Get(Look()?.Speed, animator) is float f ? f : 0f;

        // How far through the current state (0 to 1 for one pass; more when it loops).
        internal static float NormalizedTime(Component animator, int layer)
        {
            object state = Call(Look()?.CurrentState, animator, layer);
            return Get(Look()?.StateTime, state) is float f ? f : 0f;
        }

        // One line per layer: its weight, the clips it plays with their
        // weights, how far through, and the blend to the next state.
        internal static string Describe(Component animator, int layer)
        {
            Api api = Look();
            if (api == null) return "";
            float weight = layer == 0 ? 1f : Call(api.GetLayerWeight, animator, layer) is float w ? w : 0f;
            object state = Call(api.CurrentState, animator, layer);
            float time = Get(api.StateTime, state) is float t ? t : 0f;
            float length = Get(api.StateLength, state) is float len ? len : 0f;
            bool loop = Get(api.StateLoop, state) is bool lp && lp;
            string text = $"{LayerName(animator, layer)} (weight {weight:0.00}): {Clips(Call(api.CurrentClips, animator, layer))}, {Pass(time, loop)} of {length:0.00} s";
            if (Call(api.InTransition, animator, layer) is bool blending && blending)
            {
                text += $"  -> {Clips(Call(api.NextClips, animator, layer))}";
            }
            return text;
        }

        private static string Pass(float time, bool loop)
        {
            if (!loop) return $"{Mathf.Clamp01(time) * 100f:0}%";
            int passes = Mathf.FloorToInt(time);
            return $"{(time - passes) * 100f:0}% (pass {passes + 1})";
        }

        private static string Clips(object infos)
        {
            Api api = Look();
            if (!(infos is Array array) || array.Length == 0) return "no clip";
            var parts = new List<string>();
            foreach (object info in array)
            {
                UnityEngine.Object clip = Get(api.ClipOfInfo, info) as UnityEngine.Object;
                float w = Get(api.ClipWeight, info) is float f ? f : 0f;
                string name = clip != null ? clip.name : "?";
                parts.Add(array.Length > 1 ? $"{name} {w:0.00}" : name);
            }
            return string.Join(" + ", parts.ToArray());
        }

        // ---- pause, step, time --------------------------------------------------------

        internal static bool IsPaused(Component animator) => animator != null && PausedSpeeds.ContainsKey(animator);

        internal static int PausedCount => PausedSpeeds.Count;

        internal static string Pause(Component animator)
        {
            Api api = Look();
            if (api?.Speed == null || animator == null || PausedSpeeds.ContainsKey(animator)) return "";
            float speed = Speed(animator);
            try
            {
                api.Speed.SetValue(animator, 0f, null);
            }
            catch (Exception ex)
            {
                return "Could not pause: " + ex.Message;
            }
            PausedSpeeds[animator] = speed;
            return $"Paused {animator.name}'s animator (speed {speed:0.##} is put back on Resume).";
        }

        internal static string Resume(Component animator)
        {
            if (animator == null || !PausedSpeeds.TryGetValue(animator, out float speed)) return "";
            PausedSpeeds.Remove(animator);
            try
            {
                Look()?.Speed?.SetValue(animator, speed, null);
            }
            catch
            {
            }
            return $"Resumed {animator.name}'s animator.";
        }

        internal static void ResumeAll()
        {
            foreach (Component animator in new List<Component>(PausedSpeeds.Keys))
            {
                if (animator != null) Resume(animator);
            }
            PausedSpeeds.Clear();
        }

        // Moves a paused animator on by one step at the speed it had.
        internal static string Step(Component animator)
        {
            Api api = Look();
            if (api?.Update == null || !PausedSpeeds.TryGetValue(animator, out float speed)) return "";
            try
            {
                api.Speed.SetValue(animator, speed, null);
                api.Update.Invoke(animator, new object[] { StepSeconds });
            }
            catch (Exception ex)
            {
                return "Could not step: " + (ex.InnerException ?? ex).Message;
            }
            finally
            {
                try { api.Speed.SetValue(animator, 0f, null); } catch { }
            }
            return $"Stepped {StepSeconds * 1000f:0} ms.";
        }

        // Puts a layer's current state at a point in its pass (0 to 1).
        internal static void SetTime(Component animator, int layer, float normalized)
        {
            Api api = Look();
            if (api?.Play == null || api.Update == null) return;
            object state = Call(api.CurrentState, animator, layer);
            if (!(Get(api.StateHash, state) is int hash)) return;
            Call(api.Play, animator, hash, layer, normalized);
            Call(api.Update, animator, 0f);
        }
    }
}
