using System;
using BepInEx;
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
    /// Experimental; see docs/INSPECTOR.md.
    /// </summary>
    public static class Inspector
    {
        /// <summary>BepInEx GUID of the Inspector library.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.inspector";

        /// <summary>Library version. Keep in sync with the csproj.</summary>
        public const string Version = "1.0.0";

        /// <summary>
        /// Selects <paramref name="target"/> (a GameObject, a Component or a
        /// Material) in the Inspector tab and opens the window on it. Anything
        /// else is refused with a note in the tab. Nothing happens while
        /// developer tools are off.
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
    internal sealed class InspectorPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private IDisposable _overlay;

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
                TW.OpenChanged += open => { if (!open) { InspectorPick.End(); if (InspectorBodies.Paused) InspectorBodies.Resume(); InspectorAnimators.ResumeAll(); } };
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

        private void OnDestroy()
        {
            InspectorFreeCamera.Stop();
            if (InspectorBodies.Paused) InspectorBodies.Resume();
            TW.BlockGameInput(Inspector.Guid, false);
            _overlay?.Dispose();
        }
    }
}
