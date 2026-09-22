using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;

namespace DragNWash.ModFramework.Inspector
{
    /// <summary>
    /// Inspector library: the Inspector tab of the Tool window (the scene's
    /// objects, their components and members, materials, a pick mode, a
    /// transform gizmo, a history of edits), for people who make mods. Its own
    /// plugin, so a mod's release can ship the Tool window without it.
    /// Experimental; see https://github.com/TomXV/dragnwash-modframework/wiki/Inspector.
    /// </summary>
    public static class Inspector
    {
        /// <summary>BepInEx GUID of the Inspector library.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.inspector";

        /// <summary>Library version. Keep in sync with the csproj.</summary>
        public const string Version = "1.1.2";

        /// <summary>
        /// Selects <paramref name="target"/> in the Inspector tab and opens the
        /// window on it: a GameObject or Component of a loaded scene in Scene, a
        /// Material in the view that is open, and any other object (a texture,
        /// a mesh, a ScriptableObject, a GameObject no scene holds) in Objects.
        /// Nothing happens while developer tools are off.
        /// </summary>
        public static void Inspect(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }
            InspectorTab.Select(target);
            TW.Open(InspectorTab.Title);
        }
    }

    [BepInPlugin(Inspector.Guid, "DragNWash.ModFramework.Inspector", Inspector.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(TW.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(DragNWash.ModFramework.Overrides.GameOverrides.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    internal sealed class InspectorPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private IDisposable _overlay;
        private ConfigEntry<string> _layout;

        private void Awake()
        {
            Log = Logger;
            ModFramework.Register(new ModInfo
            {
                Guid = Inspector.Guid,
                DisplayName = "Drag'n Wash ModFramework: Inspector (experimental)",
                Description = "Experimental: it may change or go away in a later version, and an edit made with it can break the running session. An Inspector tab in the Tool window (F1): the scene's objects, their components and values, materials, a pick mode and a transform gizmo. For people who make mods; nothing here is needed to play.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
            });
            // Textures and the line material are made here, before any frame is
            // presented: an upload later can crash Direct3D 12 (UUM-140564).
            InspectorColorPicker.CreateTextures();
            InspectorGizmo.CreateMaterial();
            try
            {
                InspectorTab.Install();
                InspectorOperations.Register();
                InspectorCodeGraph.Register();
                // What other mods change through a write operation - a graph,
                // the console, the page - is listed in the History beside the
                // edits made here, with who made it.
                Operations.Written += InspectorHistory.RecordWrite;
                GameEvents.OnSceneLoaded(Inspector.Guid, (scene, mode) => InspectorCodeGraph.OnSceneLoaded());
                // The outline, the pick mode and the gizmo draw in the game's
                // screen space, outside the window.
                _overlay = TW.AddOverlay(Inspector.Guid, window =>
                {
                    InspectorFreeCamera.NoteWindow(window);
                    InspectorDebugView.OnGUI(window, InspectorTab.SelectedObject);
                    InspectorPick.OnGUI(window, InspectorTab.SelectedObject);
                    InspectorMesh.OnGUI(window, InspectorTab.SelectedObject);
                    InspectorBones.OnGUI(window, InspectorTab.SelectedObject);
                    InspectorGizmo.OnGUI(window, InspectorTab.SelectedObject);
                });
                // Scene or Objects, the tree and list shown or not, and the
                // Objects folders left open come back at the next start.
                _layout = Config.Bind("Tab", "Layout", "",
                    new ConfigDescription("How the Inspector tab was left (view, panes, open folders). Saved when the window closes.", null, new HiddenSetting()));
                InspectorTab.Layout = _layout.Value;
                TW.OpenChanged += open => { if (!open) { InspectorPick.End(); if (InspectorBodies.Paused) InspectorBodies.Resume(); InspectorAnimators.ResumeAll(); SaveLayout(); } };
                // The free camera is a developer tool too: off with the switch.
                DeveloperTools.Changed += () => { if (!DeveloperTools.Enabled) { InspectorFreeCamera.Stop(); if (InspectorBodies.Paused) InspectorBodies.Resume(); InspectorAnimators.ResumeAll(); } };
            }
            catch (Exception ex)
            {
                GameHooks.Require(Inspector.Guid, "Inspector tab", false, ex.Message);
            }
        }

        private void Update()
        {
            InspectorFreeCamera.Update();
            // The game's input is held while a pick or a gizmo drag is under way,
            // so the click that selects or moves an object never reaches the player.
            TW.BlockGameInput(Inspector.Guid, InspectorPick.Picking || InspectorGizmo.Dragging || InspectorMesh.Dragging);
        }

        private void SaveLayout()
        {
            if (_layout != null && _layout.Value != InspectorTab.Layout)
            {
                _layout.Value = InspectorTab.Layout;
            }
        }

        // Kept, not chosen: hidden from the Mods screen as BepInEx.ConfigurationManager tags do.
        private sealed class HiddenSetting
        {
            public bool Browsable = false;
        }

        private void OnApplicationQuit()
        {
            SaveLayout();
        }

        private void OnDestroy()
        {
            Operations.Written -= InspectorHistory.RecordWrite;
            InspectorFreeCamera.Stop();
            if (InspectorBodies.Paused) InspectorBodies.Resume();
            TW.BlockGameInput(Inspector.Guid, false);
            _overlay?.Dispose();
        }
    }
}
