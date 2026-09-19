namespace DragNWash.ModFramework.Bridge
{
    /// <summary>
    /// The Bridge: the read operations of the registry offered to AI clients on
    /// this computer over the Model Context Protocol (Streamable HTTP at
    /// http://127.0.0.1:47821/mcp, with a token). Off by default, and only while
    /// developer tools are on. Experimental. See docs/BRIDGE.md.
    /// </summary>
    public static class Bridge
    {
        /// <summary>The library's BepInEx GUID.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.bridge";

        /// <summary>The library's version.</summary>
        public const string Version = "0.1.0";

        /// <summary>True while the Bridge is listening.</summary>
        public static bool Listening => BridgePlugin.Server != null;
    }
}
