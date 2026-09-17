namespace DragNWash.ModFramework
{
    /// <summary>
    /// One way a mod uses the internet, as players should hear it: where it
    /// connects, what for, what it sends and how to stop it. List them in
    /// <see cref="ModInfo.Network"/>; the Mods screen shows them on the mod's
    /// Internet page. See GUIDE rule 11. Experimental, since 1.2.0.
    /// </summary>
    public sealed class NetworkUse
    {
        /// <summary>
        /// The host connected to, e.g. <c>"api.example.com"</c>, or
        /// <c>"*.example.com"</c> for any host under that domain. Required.
        /// </summary>
        public string Host { get; set; }

        /// <summary>What for and how often, e.g. "Downloads the latest word list once a day".</summary>
        public string Purpose { get; set; }

        /// <summary>
        /// What leaves the player's computer, e.g. "The language you chose. Nothing
        /// that identifies you." Say "Nothing about you or your game" only when true.
        /// </summary>
        public string Sends { get; set; }

        /// <summary>
        /// How a player stops it, e.g. "Mods → My Mod → Settings → Online word list".
        /// Leave empty only when it cannot be turned off, and say why in Purpose.
        /// </summary>
        public string TurnOff { get; set; }
    }
}
