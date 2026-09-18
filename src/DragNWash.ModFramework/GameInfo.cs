using UnityEngine;
using UnityEngine.Rendering;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// Facts about the running game that mods commonly need to decide how to behave.
    /// </summary>
    public static class GameInfo
    {
        /// <summary>Unity version the game was built with, e.g. 6000.3.14f1.</summary>
        public static string UnityVersion => Application.unityVersion;

        /// <summary>Graphics API in use.</summary>
        public static GraphicsDeviceType GraphicsApi => SystemInfo.graphicsDeviceType;

        /// <summary>
        /// True on Direct3D 12. This Unity version crashes in D3D12ScratchAllocator
        /// when textures or font atlases are uploaded at the wrong moment
        /// (Unity issue UUM-140564), so mods should load such assets at startup
        /// there instead of lazily.
        /// </summary>
        public static bool IsDirect3D12 => GraphicsApi == GraphicsDeviceType.Direct3D12;

        /// <summary>
        /// Experimental. True when the core batches font atlas uploads to once per
        /// frame on Direct3D 12 ([Direct3D12] BatchFontAtlasUploads). Then adding
        /// glyphs while the game runs, which uploads the atlas, no longer piles
        /// up into the upload burst that crashes Direct3D 12 (UUM-140564), and a
        /// window may draw text it did not prepare in advance.
        /// </summary>
        public static bool FontAtlasUploadsBatched { get; internal set; }

        /// <summary>Operating system family (Windows, Linux for the Steam Deck, macOS).</summary>
        public static OperatingSystemFamily OperatingSystem => SystemInfo.operatingSystemFamily;
    }
}
