using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace DragNWash.ModFramework.ToolWindow
{
    // The window's font. On Unity 6, IMGUI does not draw from this font's own
    // texture: it makes a TextCore font asset from it, with an atlas of its
    // own that fills glyph by glyph as text is drawn, and on Direct3D 12 the
    // core uploads that atlas once per frame (FontAtlasUploads), which keeps
    // it clear of the UUM-140564 crash. This font is still the key IMGUI finds
    // that asset by. The asset is kept too (TextCoreAsset), and MenuText asks
    // it, rather than this font, whether a character has a glyph.
    //
    // Choosing it reads the OS font table, tens of milliseconds and up to a
    // second and a half on a cold disk, so it is made the first time the
    // window's key is pressed or ToolWindow.Open is called (from Update; the
    // window, or the notice that the developer tools are off, shows on the
    // next frame) or ToolWindow.Font or CanDraw is read, rather than at
    // startup, and the
    // characters mods prepare before then are only noted. Making it has
    // MenuText check them all in one batch, with the printable ASCII, which
    // on Unity 6 puts their glyphs into the TextCore atlas before the window
    // draws; from then on each is checked as it is prepared (see Request).
    // On Direct3D 12 without the core's batching it is made at startup
    // (from Awake) instead, and characters are prepared from Update, never
    // from OnGUI.
    //
    // IMGUI can only use fonts Unity itself knows: OS fonts by name, or Font
    // assets. Inside Steam's Linux runtime (Steam Deck) no OS font has CJK
    // glyphs, so a Font asset baked into an AssetBundle
    // (dragnwash-menufont.bundle, Noto Sans JP) is used when one is installed
    // anywhere under BepInEx/plugins.
    internal static class MenuFont
    {
        internal const int Size = 14;
        internal const string BundleFileName = "dragnwash-menufont.bundle";

        internal static Font Font { get; private set; }

        // The bundled Noto Sans JP comes out thin in IMGUI; Unity's synthetic
        // bold gives it the weight of the OS fonts.
        internal static bool Bold { get; private set; }

        // The TextCore font asset IMGUI draws Font with, and the settings
        // (RuntimeTextSettings.defaultTextSettings) its text generator looks
        // fallbacks up in, for MenuText to ask which characters are drawn.
        // Null for the skin's font, where IMGUI has no TextCore, or when
        // asking failed; MenuText then asks Font.
        internal static UnityEngine.Object TextCoreAsset { get; private set; }
        internal static object TextCoreSettings { get; private set; }

        // True once Create has run, whatever it found (the skin's font included).
        internal static bool Ready { get; private set; }

        // Made on an earlier frame, so the window's first text and the making
        // of its font never land on the same frame.
        internal static bool Usable => Ready && Time.frameCount > _madeOnFrame;

        // Asked for from OnGUI before it was made: the next Update makes it.
        internal static bool Wanted { get; private set; }

        private static readonly StringBuilder Queued = new StringBuilder();
        private static AssetBundle _bundle;
        private static string _mode;
        private static bool _eager;
        private static int _madeOnFrame = -1;

        // From Awake: what to make later, or, on Direct3D 12 without the
        // core's batching, the font itself.
        internal static void Configure(string mode)
        {
            _mode = mode;
            _eager = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 && !UploadsBatched();
            if (_eager)
            {
                Create();
                _madeOnFrame = -1;
            }
        }

        // For callers outside the window (ToolWindow.Font, CanDraw): made on
        // the spot from Awake or Update; from OnGUI, on the next Update.
        internal static Font Needed()
        {
            if (!Ready && _mode != null)
            {
                if (!MenuText.InOnGUI)
                {
                    Create();
                }
                else
                {
                    Wanted = true;
                }
            }
            return Font;
        }

        // Its own method, so a core without the property (older than this Tool
        // window) is caught here instead of failing the whole class.
        internal static bool UploadsBatched()
        {
            try
            {
                return BatchedFromCore();
            }
            catch (MissingMemberException)
            {
                return false;
            }
        }

        private static bool BatchedFromCore() => GameInfo.FontAtlasUploadsBatched;

        // From Update or Awake, never from OnGUI. Also settles what cut text
        // and More end in, which depends on the font.
        internal static void Create()
        {
            if (Ready || _mode == null)
            {
                return;
            }
            Ready = true;
            Wanted = false;
            _madeOnFrame = Time.frameCount;
            long started = Stopwatch.GetTimestamp();
            Choose(_mode);
            KeepTextCoreAsset();
            // Cut text ends in an ellipsis, and More has its triangle, where the
            // window draws them. The box is what TextCore draws for a character
            // no font has (a control character the swap lets through, say), so
            // without the core's batching it too is in the atlas before the
            // first draw.
            Request("\u2026\u25BE\u25A1", _eager);
            CheckPrepared();
            ToolWindow.Ellipsis = MenuText.CanDraw(Font, Size, "\u2026") ? "\u2026" : "...";
            ToolWindow.DownArrow = MenuText.CanDraw(Font, Size, "\u25BE") ? "\u25BE" : "v";
            ToolWindowPlugin.Log.LogDebug($"Window font made in {(Stopwatch.GetTimestamp() - started) * 1000 / Stopwatch.Frequency} ms.");
        }

        private static void Choose(string mode)
        {
            try
            {
                if (mode == "skin")
                {
                    ToolWindowPlugin.Log.LogInfo("Window font: the IMGUI skin's (config).");
                    return;
                }
                if (mode == "builtin")
                {
                    Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                else
                {
                    // Unity hands back a Font even for a family the OS does not
                    // have, which then draws nothing (seen in Steam's Linux
                    // runtime), so skip names the OS does not list and check that
                    // a glyph really renders.
                    // Arial Unicode MS and Apple SD Gothic Neo are for macOS,
                    // where Hiragino Sans renders but TextCore cannot load it
                    // (see TextCoreLoads) and PingFang is not where Unity looks.
                    // Arial Unicode MS has every symbol the window draws;
                    // Apple SD Gothic Neo lacks ▾ and 6 of the Inspector's 13
                    // toolbar icons, so it comes last.
                    var installed = new HashSet<string>(Font.GetOSInstalledFontNames() ?? new string[0], StringComparer.OrdinalIgnoreCase);
                    foreach (string name in new[] { "Yu Gothic UI", "Meiryo UI", "Hiragino Sans", "Arial Unicode MS", "PingFang SC", "Noto Sans CJK JP", "Noto Sans CJK SC", "Apple SD Gothic Neo" })
                    {
                        if (installed.Count > 0 && !installed.Contains(name))
                        {
                            continue;
                        }
                        // With a symbol face behind it, where the OS has one: before
                        // Unity 6 the Inspector's toolbar glyphs come from there.
                        // TextCore (Unity 6) reads only the first name, and MenuText
                        // asks it instead.
                        var names = new List<string> { name };
                        foreach (string symbols in new[] { "Segoe UI Symbol", "Apple Symbols", "Noto Sans Symbols2", "DejaVu Sans" })
                        {
                            if (installed.Contains(symbols))
                            {
                                names.Add(symbols);
                            }
                        }
                        Font candidate = Font.CreateDynamicFontFromOSFont(names.ToArray(), Size);
                        if (candidate == null)
                        {
                            continue;
                        }
                        if (Renders(candidate) && TextCoreLoads(candidate, name))
                        {
                            Font = candidate;
                            ToolWindowPlugin.Log.LogInfo($"Window font: {string.Join(" + ", names)}");
                            break;
                        }
                        UnityEngine.Object.Destroy(candidate);
                    }
                    if (Font == null)
                    {
                        Font bundled = LoadBundle();
                        if (bundled != null && Renders(bundled) && TextCoreLoads(bundled, bundled.name))
                        {
                            Font = bundled;
                            Bold = true;
                        }
                    }
                    if (Font == null)
                    {
                        Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                        ToolWindowPlugin.Log.LogInfo("Window font: no OS or bundled font can be drawn here; using Unity's built-in font (on Unity 6, other writing systems only where the OS fallback fonts have them).");
                    }
                }
                if (Font == null)
                {
                    return;
                }
                // Noted with the rest, so CheckPrepared puts them into the
                // TextCore atlas too.
                var ascii = new StringBuilder();
                for (char c = ' '; c <= '~'; c++)
                {
                    ascii.Append(c);
                }
                Request(ascii.ToString(), true);
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogWarning($"Could not prepare the window font: {ex.Message}");
                Font = null;
            }
        }

        internal static void Prepare(string characters)
        {
            if (string.IsNullOrEmpty(characters))
            {
                return;
            }
            // Inside OnGUI or a window's function: wait for Update.
            if (MenuText.InOnGUI)
            {
                lock (Queued)
                {
                    Queued.Append(characters);
                }
                return;
            }
            Request(characters, _eager);
        }

        // From Update.
        internal static void FlushQueued()
        {
            string pending;
            lock (Queued)
            {
                if (Queued.Length == 0)
                {
                    return;
                }
                pending = Queued.ToString();
                Queued.Length = 0;
            }
            Request(pending, _eager);
        }

        // Every character prepared so far, so a caller that draws arbitrary
        // text (the console) can tell what is safe to draw this frame.
        private static readonly HashSet<char> Requested = new HashSet<char>();

        // Set by Create once the font and its TextCore asset are settled: an
        // answer asked of them earlier would be kept for good.
        private static bool _checksPrepared;

        // From Create. What mods prepared before the font was made (from
        // Awake, say), with the printable ASCII Choose noted and the
        // ellipsis and triangle, asked about in one batch, one atlas upload,
        // so the window draws it from the first frame that shows it; after
        // this, Request asks as it goes.
        private static void CheckPrepared()
        {
            if (Font == null)
            {
                return;
            }
            char[] prepared;
            lock (Requested)
            {
                prepared = new char[Requested.Count];
                Requested.CopyTo(prepared);
            }
            _checksPrepared = true;
            MenuText.CheckNow(prepared);
        }

        internal static bool IsPrepared(char c)
        {
            lock (Requested)
            {
                return Requested.Contains(c);
            }
        }

        // Notes the characters; once the font is made, has MenuText check the
        // new ones (always from Update or Awake: Prepare waits for Update when
        // called from OnGUI), which on Unity 6 adds them to the TextCore atlas
        // IMGUI draws from, as drawing them would; and rasterises them into
        // this font when asked to.
        private static void Request(string characters, bool rasterise)
        {
            // The skin's font, or none could be made: nothing to prepare.
            if (Font == null && (Ready || _mode == "skin"))
            {
                return;
            }
            try
            {
                lock (Requested)
                {
                    foreach (char c in characters)
                    {
                        Requested.Add(c);
                    }
                }
                if (_checksPrepared)
                {
                    MenuText.CheckNow(characters);
                }
                if (!rasterise || Font == null)
                {
                    return;
                }
                Font.RequestCharactersInTexture(characters, Size, FontStyle.Normal);
                if (Bold)
                {
                    Font.RequestCharactersInTexture(characters, Size, FontStyle.Bold);
                }
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogWarning($"Could not prepare window font characters: {ex.Message}");
            }
        }

        private static bool Renders(Font font)
        {
            try
            {
                font.RequestCharactersInTexture("A", Size, FontStyle.Normal);
                return font.HasCharacter('A')
                       && font.GetCharacterInfo('A', out CharacterInfo info, Size, FontStyle.Normal)
                       && info.advance > 0;
            }
            catch
            {
                return false;
            }
        }

        // Unity 6's IMGUI draws with the TextCore font asset it makes from the
        // window's Font (RuntimeTextSettings.defaultTextSettings
        // .GetCachedFontAsset). For a Font made from OS names that asset is
        // looked up again by the Font's first name and the style "Regular";
        // when nothing matches, IMGUI draws no text at all and tries again for
        // every string, two lines in Player.log each time. On macOS Hiragino
        // Sans has only W0 to W9, so the window stayed blank while Renders
        // passed. Asking the same method here, from Update or Awake, settles
        // it before the font is chosen: an asset it makes is kept for this
        // Font, so IMGUI uses it as is, and a failure costs its two lines
        // once. Where the method is not there (IMGUI before TextCore) or
        // reflection fails, the font is used as before.
        private static bool TextCoreLoads(Font font, string name)
        {
            try
            {
                if (!AskTextCore(font, out _, out UnityEngine.Object asset) || asset != null)
                {
                    return true;
                }
                ToolWindowPlugin.Log.LogInfo($"Window font: skipped {name}; Unity's text engine (TextCore) cannot load it, and the window would draw no text with it.");
                return false;
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogDebug($"Window font: could not ask TextCore about {name} ({ex.GetBaseException().Message}); using it as before.");
                return true;
            }
        }

        // Once the font is chosen. For an OS or bundled font TextCoreLoads made
        // the asset and this finds it cached; for Unity's built-in font it is
        // made here, from Update or Awake, instead of in the window's first
        // draw.
        private static void KeepTextCoreAsset()
        {
            TextCoreAsset = null;
            TextCoreSettings = null;
            if (Font == null)
            {
                return;
            }
            try
            {
                if (AskTextCore(Font, out object settings, out UnityEngine.Object asset) && asset != null)
                {
                    TextCoreAsset = asset;
                    TextCoreSettings = settings;
                }
            }
            catch (Exception ex)
            {
                // Said at Warning, as MenuText says its own failures: the font's
                // own check is the one Unity 6 gets wrong.
                MenuText.FellBack(ex, "from now on", $"getting its asset for {Font.name}");
            }
        }

        // False where IMGUI has no TextCore settings to ask. Otherwise the
        // asset IMGUI draws font with (made now if it was not cached yet), or
        // null when TextCore cannot load font. Reflection errors are the
        // caller's to catch.
        private static bool AskTextCore(Font font, out object settings, out UnityEngine.Object asset)
        {
            asset = null;
            settings = typeof(GUIStyle).Assembly.GetType("UnityEngine.RuntimeTextSettings")
                ?.GetProperty("defaultTextSettings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null, null);
            MethodInfo getAsset = settings?.GetType().GetMethod("GetCachedFontAsset",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Font) }, null);
            if (getAsset == null)
            {
                return false;
            }
            // Unity's null too: a destroyed asset counts as none.
            var made = getAsset.Invoke(settings, new object[] { font }) as UnityEngine.Object;
            asset = made != null ? made : null;
            return true;
        }

        private static Font LoadBundle()
        {
            try
            {
                string path = FindBundle();
                if (path == null)
                {
                    return null;
                }
                _bundle = _bundle ?? AssetBundle.LoadFromFile(path);
                Font[] fonts = _bundle != null ? _bundle.LoadAllAssets<Font>() : null;
                if (fonts == null || fonts.Length == 0)
                {
                    ToolWindowPlugin.Log.LogWarning($"The window font bundle holds no font: {path}");
                    return null;
                }
                ToolWindowPlugin.Log.LogInfo($"Window font: {fonts[0].name} from {path}");
                return fonts[0];
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogWarning($"Could not load the window font bundle: {ex.Message}");
                return null;
            }
        }

        // Next to this library first, then anywhere under plugins (the
        // localization mod ships one).
        private static string FindBundle()
        {
            string own = Path.Combine(Path.GetDirectoryName(typeof(MenuFont).Assembly.Location) ?? "", BundleFileName);
            if (File.Exists(own))
            {
                return own;
            }
            try
            {
                string[] found = Directory.GetFiles(Paths.PluginPath, BundleFileName, SearchOption.AllDirectories);
                Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                return found.Length > 0 ? found[0] : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
