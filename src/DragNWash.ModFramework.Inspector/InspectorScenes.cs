using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DragNWash.ModFramework.Inspector
{
    // Scenes and the game's levels. Experimental.
    //
    // The game has a few scenes (the title, PlayGame, the scenes after certain
    // levels, the credits) and plays its levels inside PlayGame: a LevelFlow
    // gives each level number its dragon, weather and dialogue, and
    // WalkNWashSceneState runs the current one. All of it is read by
    // reflection from the game's own types, so a game update that renames
    // them turns these tools off instead of breaking the library.
    //
    // Scene changes go through the game's loading menu, as the game's own
    // menus do. Skip and Clean call the game's own cheats (shown in its
    // development builds only). Start level sets the level number and starts
    // that level in place; nothing is written to the save until the level is
    // finished, and the framework's Saves library keeps a copy of every save
    // the game writes.
    internal static class InspectorScenes
    {
        private static bool _looked;
        private static Type _state, _flow, _menuManager, _transition, _levelLoad, _sceneLoader, _loadDescription;
        private static FieldInfo _stateInstance, _currentLevel, _levelFlow, _menuInstance;
        private static MethodInfo _levelCount, _dragonOf, _weatherOf, _dragonState, _cleanPercentage, _skip, _sparkle, _startLevel, _processTransition, _loadScene;

        private static void Look()
        {
            if (_looked) return;
            _looked = true;
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            _state = Type.GetType("WalkNWashSceneState, Assembly-CSharp");
            _flow = Type.GetType("LevelFlow, Assembly-CSharp");
            _menuManager = Type.GetType("MenuManager, Assembly-CSharp");
            _transition = Type.GetType("MenuResponseTransition, Assembly-CSharp");
            _levelLoad = Type.GetType("MenuTransitionDataLevelLoad, Assembly-CSharp");
            _sceneLoader = Type.GetType("SceneLoader, Assembly-CSharp");
            _loadDescription = _sceneLoader?.GetNestedType("SceneLoadDescrption", Any);
            if (_state != null)
            {
                _stateInstance = _state.GetField("instance", Any);
                _currentLevel = _state.GetField("currentLevel", Any);
                _levelFlow = _state.GetField("levelFlow", Any);
                _dragonState = _state.GetMethod("GetDragonState", Any, null, Type.EmptyTypes, null);
                _cleanPercentage = _state.GetMethod("GetCleanPercentage", Any, null, Type.EmptyTypes, null);
                _skip = _state.GetMethod("SkipLevel", Any, null, Type.EmptyTypes, null);
                _sparkle = _state.GetMethod("SparkleClean", Any, null, Type.EmptyTypes, null);
                _startLevel = _state.GetMethod("StartCurrentLevel", Any, null, Type.EmptyTypes, null);
            }
            if (_flow != null)
            {
                _levelCount = _flow.GetMethod("GetLevelCount", Any, null, Type.EmptyTypes, null);
                _dragonOf = _flow.GetMethod("GetDragonDescriptor", Any, null, new[] { typeof(int) }, null);
                _weatherOf = _flow.GetMethod("GetWeatherState", Any, null, new[] { typeof(int) }, null);
            }
            if (_menuManager != null)
            {
                _menuInstance = _menuManager.GetField("_instance", Any);
                _processTransition = _menuManager.GetMethod("ProcessTransition", Any);
            }
            if (_sceneLoader != null && _loadDescription != null)
            {
                _loadScene = _sceneLoader.GetMethod("LoadScene", Any, null, new[] { _loadDescription }, null);
            }
        }

        private static object Invoke(MethodInfo m, object target, params object[] args)
        {
            if (m == null) return null;
            try { return m.Invoke(target, args); }
            catch { return null; }
        }

        // ---- scenes --------------------------------------------------------------

        internal static List<string> LoadedScenes()
        {
            var list = new List<string>();
            Scene active = SceneManager.GetActiveScene();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                string name = string.IsNullOrEmpty(scene.name) ? "(unnamed)" : scene.name;
                list.Add($"{name}  #{scene.buildIndex}, {scene.rootCount} root object(s){(scene == active ? ", active" : "")}{(scene.isLoaded ? "" : ", loading")}");
            }
            return list;
        }

        // The scenes in the build, by name, in build order.
        internal static List<string> BuildScenes()
        {
            var list = new List<string>();
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                list.Add(string.IsNullOrEmpty(path) ? "#" + i : Path.GetFileNameWithoutExtension(path));
            }
            return list;
        }

        internal static string ActiveScene => SceneManager.GetActiveScene().name;

        // Loads a scene the way the game's menus do: through its loading menu,
        // which fades and hands the load to its SceneLoader. When the menu
        // cannot take it, the SceneLoader is called directly.
        internal static string LoadScene(string name)
        {
            Look();
            if (string.IsNullOrEmpty(name)) return "";
            object menus = _menuInstance?.GetValue(null);
            if (menus != null && _processTransition != null && _transition != null && _levelLoad != null)
            {
                try
                {
                    object data = Activator.CreateInstance(_levelLoad, name);
                    object transition = Activator.CreateInstance(_transition, "Menu_Loading", "Inspector: load " + name, data);
                    _processTransition.Invoke(menus, new[] { transition });
                    InspectorPlugin.Log.LogInfo($"[scenes] Loading {name} through the game's loading menu.");
                    return $"Loading {name}...";
                }
                catch (Exception ex)
                {
                    InspectorPlugin.Log.LogWarning($"[scenes] The loading menu did not take {name}: {(ex.InnerException ?? ex).Message}; calling the SceneLoader.");
                }
            }
            if (_loadScene != null)
            {
                try
                {
                    object description = Activator.CreateInstance(_loadDescription, name);
                    _loadScene.Invoke(null, new[] { description });
                    InspectorPlugin.Log.LogInfo($"[scenes] Loading {name} through the game's SceneLoader.");
                    return $"Loading {name}...";
                }
                catch (Exception ex)
                {
                    return "Could not load: " + (ex.InnerException ?? ex).Message;
                }
            }
            return "The game's scene loader was not found; scenes cannot be loaded on this game version.";
        }

        // ---- levels --------------------------------------------------------------

        private static object State
        {
            get
            {
                Look();
                object s = _stateInstance?.GetValue(null);
                return s is UnityEngine.Object o && o == null ? null : s;
            }
        }

        // False outside PlayGame, where no level runs.
        internal static bool InLevel => State != null;

        internal static int CurrentLevel => State != null && _currentLevel?.GetValue(State) is int n ? n : -1;

        private static object Flow => State != null ? _levelFlow?.GetValue(State) : null;

        internal static int LevelCount => Invoke(_levelCount, Flow) is int n ? n : 0;

        internal static string DragonOf(int level)
        {
            return Invoke(_dragonOf, Flow, level) is UnityEngine.Object dragon && dragon != null ? dragon.name : "?";
        }

        internal static string WeatherOf(int level) => Invoke(_weatherOf, Flow, level)?.ToString() ?? "?";

        internal static string Describe()
        {
            if (!InLevel) return "No level is running (levels run in PlayGame).";
            int level = CurrentLevel;
            string clean = Invoke(_cleanPercentage, null) is float f ? $", clean {f * 100f:0}%" : "";
            return $"Level {level + 1} of {LevelCount}: {DragonOf(level)}, {WeatherOf(level)}, dragon {Invoke(_dragonState, null) ?? "?"}{clean}";
        }

        // The game's own "skip level" cheat: the dragon walks out and the next
        // level starts, as if this one was done (its progress is saved).
        internal static string Skip()
        {
            if (!InLevel || _skip == null) return "No level is running.";
            Invoke(_skip, null);
            InspectorPlugin.Log.LogInfo("[scenes] Skip level (the game's cheat).");
            return "Skipping the level (the next one starts, and is saved as reached).";
        }

        // The game's sparkle clean on the dragon being washed.
        internal static string Clean()
        {
            if (!InLevel || _sparkle == null) return "No level is running.";
            Invoke(_sparkle, null);
            return "Cleaned the dragon.";
        }

        // Starts a level in place of the current one. The flags earlier levels
        // would have set are not set, so dialogue that depends on them may
        // differ; nothing is saved until the level is finished.
        internal static string StartLevel(int level)
        {
            object state = State;
            if (state == null || _currentLevel == null || _startLevel == null) return "No level is running.";
            if (level < 0 || level >= LevelCount) return $"There is no level {level + 1}.";
            try
            {
                _currentLevel.SetValue(state, level);
                _startLevel.Invoke(state, null);
            }
            catch (Exception ex)
            {
                return "Could not start the level: " + (ex.InnerException ?? ex).Message;
            }
            InspectorPlugin.Log.LogInfo($"[scenes] Started level {level + 1} ({DragonOf(level)}).");
            return $"Started level {level + 1} ({DragonOf(level)}). Saved only when it is finished.";
        }
    }
}
