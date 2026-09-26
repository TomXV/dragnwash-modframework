using System;
using System.IO;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;

namespace DragNWash.ModFramework.Assets
{
    /// <summary>
    /// Fonts for text the game's own font assets cannot show (Japanese, Chinese,
    /// Korean, Hebrew, accented Latin, Cyrillic), prepared so that showing it
    /// never crashes Direct3D 12. There is one fallback chain for the whole game,
    /// shared by every mod that uses this library.
    /// </summary>
    //
    // The game's shipped TMP font assets have no CJK glyphs, so translated
    // Japanese/Chinese text renders as tofu boxes. Rather than touching the
    // game's own font assets, we build dynamic TMP font assets straight from
    // OS-installed CJK fonts (Unity 6's AtlasPopulationMode.DynamicOS) and
    // register them as global TMP fallbacks, so any existing text component
    // picks up missing glyphs from them automatically.
    //
    // Why the extra machinery below: a DynamicOS asset starts out holding a 1x1
    // placeholder atlas texture. The first glyph added reinitializes it to full
    // size, each batch of new glyphs calls Texture2D.Apply, and a filled atlas
    // allocates a whole new texture - all at runtime, on whichever frame a
    // character first appears on screen. On Direct3D 12 that stream of texture
    // allocations and uploads is what reaches
    // D3D12ScratchAllocator::ReleaseExcessScratch and kills the process
    // (Unity UUM-140564), which is why Options - a screen dense with text the
    // player has not seen yet - was the reliable trigger.
    //
    // So: keep the fallback chain down to one font per script, and rasterize
    // every character the loaded translations can need during load rather than
    // during gameplay. What was rasterized is kept for the next start
    // (FontAtlasCache), so that load is quick after the first.
    //
    // Split by responsibility into GameFonts.<Part>.cs beside this file:
    // Warming (finding the scripts a text needs, rasterizing along the
    // fallback chain and publishing it to TextMeshPro) and Faces (finding and
    // loading a font face per script). This file keeps the font tables, the
    // state and the public entry points.
    public static partial class GameFonts
    {
        private const int DefaultAtlasPointSize = 64;

        // Ordered by preference; the first one present on the machine wins.
        private static readonly string[] JapaneseCandidates =
        {
            "Yu Gothic UI",
            "Meiryo UI",
            "Meiryo",
            "MS Gothic",
            // macOS
            "Hiragino Sans",
            "Hiragino Kaku Gothic ProN",
            // Linux / Steam Deck (Proton exposes fontconfig fonts)
            "Noto Sans CJK JP",
            "Noto Sans JP",
        };

        private static readonly string[] ChineseCandidates =
        {
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "SimHei",
            "SimSun",
            // macOS
            "PingFang SC",
            "Hiragino Sans GB",
            // Linux / Steam Deck
            "Noto Sans CJK SC",
            "Noto Sans SC",
        };

        // Simplified fonts cover most but not all Traditional characters, and
        // the shapes differ; Taiwan/HK text deserves its own face.
        private static readonly string[] TraditionalCandidates =
        {
            "Microsoft JhengHei UI",
            "Microsoft JhengHei",
            "MingLiU",
            "PMingLiU",
            // macOS
            "PingFang TC",
            // Linux / Steam Deck
            "Noto Sans CJK TC",
            "Noto Sans TC",
        };

        // Thai is in none of the faces above (Segoe UI and Noto Sans have no
        // Thai), so the Thai pack needs its own.
        private static readonly string[] ThaiCandidates =
        {
            "Leelawadee UI",
            "Leelawadee",
            "Tahoma",
            // macOS
            "Thonburi",
            // Linux / Steam Deck
            "Noto Sans Thai",
            "Garuda",
            "Loma",
        };

        // No CJK font carries Hebrew, so the Hebrew pack needs its own.
        private static readonly string[] HebrewCandidates =
        {
            "Segoe UI",
            "Arial",
            "David",
            "Tahoma",
            // macOS
            "Arial Hebrew",
            // Linux / Steam Deck (DejaVu is always present in Steam's runtime)
            "Noto Sans Hebrew",
            "DejaVu Sans",
        };

        // Accented Latin (Esperanto's circumflexes, Polish, Portuguese) and
        // Cyrillic are not always in the game's own font.
        private static readonly string[] WesternCandidates =
        {
            "Segoe UI",
            "Tahoma",
            "Arial",
            // macOS
            "Helvetica Neue",
            // Linux / Steam Deck
            "Noto Sans",
            "DejaVu Sans",
            "Liberation Sans",
        };

        // Hangul is not in the Japanese or Chinese fonts above (Yu Gothic and
        // YaHei have none), so Korean gets its own.
        private static readonly string[] KoreanCandidates =
        {
            "Malgun Gothic",
            "맑은 고딕",
            "Gulim",
            // macOS
            "Apple SD Gothic Neo",
            // Linux / Steam Deck
            "Noto Sans CJK KR",
            "Noto Sans KR",
        };

        private static readonly List<TMP_FontAsset> Registered = new List<TMP_FontAsset>();
        // Which face serves each script. Two scripts can share a face - Segoe UI
        // covers both Hebrew and Latin/Cyrillic - and then share its atlas too.
        private static readonly Dictionary<string, TMP_FontAsset> FaceOfGroup =
            new Dictionary<string, TMP_FontAsset>(StringComparer.Ordinal);
        // One face per font source (family name, or file path and face index).
        private static readonly Dictionary<string, TMP_FontAsset> FaceBySource =
            new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> LoadedGroups = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<char> Warmed = new HashSet<char>();

        // What each face turned out to have and to lack. A character is only
        // ever asked of a face once; "lacks" means the font file has no glyph,
        // so TMP will not try to rasterize it there at runtime either.
        private static readonly Dictionary<TMP_FontAsset, HashSet<char>> HasOfAsset =
            new Dictionary<TMP_FontAsset, HashSet<char>>();
        private static readonly Dictionary<TMP_FontAsset, HashSet<char>> LacksOfAsset =
            new Dictionary<TMP_FontAsset, HashSet<char>>();

        // Scripts each locale was found to use, in the order its chain wants them.
        private static readonly Dictionary<string, List<string>> GroupsOfLocale =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private static string _currentLocale = string.Empty;

        private const string GroupJapanese = "Japanese";
        private const string GroupSimplified = "Simplified Chinese";
        private const string GroupTraditional = "Traditional Chinese";
        private const string GroupKorean = "Korean";
        private const string GroupHebrew = "Hebrew";
        private const string GroupThai = "Thai";
        private const string GroupWestern = "Latin and Cyrillic";

        // The order faces follow each other in, after the ones the current
        // locale puts first. Warming and the runtime chain must agree on it.
        private static readonly string[] CanonicalOrder =
        {
            GroupJapanese, GroupSimplified, GroupTraditional, GroupKorean, GroupHebrew, GroupThai, GroupWestern,
        };

        /// <summary>BepInEx GUID of the assets library.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.assets";

        /// <summary>Library version. Keep in sync with the csproj.</summary>
        public const string Version = "1.6.0";

        private static readonly List<string> FontFolders = new List<string>();

        /// <summary>
        /// False on Direct3D 12, where loading a font face or rasterizing into one
        /// while the game runs uploads atlas textures and can crash the game
        /// (Unity UUM-140564). There, prepare every language a mod may show at
        /// startup. Other graphics APIs handle runtime uploads.
        /// </summary>
        public static bool RuntimeUploadsAreSafe =>
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12;

        /// <summary>The language the fallback chain is ordered for, as last set with <see cref="SetLanguage"/>.</summary>
        public static string Language => _currentLocale;

        /// <summary>Raised after <see cref="Prepare"/> rasterized new characters or loaded a new face.</summary>
        public static event Action CharactersPrepared;

        /// <summary>
        /// Makes sure every non-ASCII character in <paramref name="texts"/> can be
        /// shown in <paramref name="language"/> (a locale code such as "ja",
        /// "zh-Hans", "ko", "he"): loads a system font face for each script it
        /// needs and rasterizes the characters now. Call from Awake or Update,
        /// never while a frame is being drawn. Characters already prepared cost
        /// nothing.
        /// </summary>
        public static void Prepare(string language, IEnumerable<string> texts)
        {
            int before = Warmed.Count;
            int facesBefore = Registered.Count;
            PrepareLocale(language ?? string.Empty, texts);
            PublishFallbacks(_currentLocale);
            if (Warmed.Count != before || Registered.Count != facesBefore)
            {
                Raise();
            }
        }

        /// <summary>
        /// Orders TextMeshPro's global fallback fonts for <paramref name="language"/>:
        /// its own scripts first (so Chinese text uses a Chinese face rather than
        /// the Japanese one), then every other prepared face. Fallback fonts other
        /// mods or the game registered stay after these.
        /// </summary>
        public static void SetLanguage(string language)
        {
            string previous = _currentLocale;
            _currentLocale = language ?? string.Empty;
            PublishFallbacks(_currentLocale);
            NoteLanguage(_currentLocale);
            if (!string.Equals(previous, _currentLocale, StringComparison.OrdinalIgnoreCase))
            {
                // Texture replacements per language follow the language set here.
                AssetReplacements.OnLanguageSet(_currentLocale);
            }
        }

        // The crash report window speaks the language the player chose in the
        // game, read from the last of these notes in the session record.
        private static void NoteLanguage(string language)
        {
            try
            {
                NoteInCrashReports(language);
            }
            catch (MissingMemberException)
            {
                // A core older than crash reports.
            }
            catch (TypeLoadException)
            {
            }
        }

        private static void NoteInCrashReports(string language) => CrashReports.Note(Guid, "language", string.IsNullOrEmpty(language) ? "-" : language);

        /// <summary>How many different non-ASCII characters in <paramref name="texts"/> were never prepared.</summary>
        public static int CountUnprepared(IEnumerable<string> texts)
        {
            int count = 0;
            var seen = new HashSet<char>();
            if (texts == null)
            {
                return 0;
            }
            foreach (string text in texts)
            {
                if (string.IsNullOrEmpty(text)) continue;
                foreach (char c in text)
                {
                    if (c > 0x7F && !Warmed.Contains(c) && seen.Add(c)) count++;
                }
            }
            return count;
        }

        /// <summary>Every character prepared so far, for mods that draw the same text with another font (IMGUI).</summary>
        public static string PreparedCharacters
        {
            get
            {
                var buffer = new char[Warmed.Count];
                Warmed.CopyTo(buffer);
                return new string(buffer);
            }
        }

        /// <summary>
        /// Adds a folder where players can put their own font files (.ttf, .otf,
        /// .ttc), used before the system's. The library's own fonts folder is
        /// always searched.
        /// </summary>
        public static void AddFontFolder(string folder)
        {
            if (!string.IsNullOrEmpty(folder) && !FontFolders.Contains(folder))
            {
                FontFolders.Add(folder);
            }
        }

        private static void Raise()
        {
            if (CharactersPrepared == null) return;
            foreach (Action handler in CharactersPrepared.GetInvocationList())
            {
                try { handler(); }
                catch (Exception ex) { AssetsLibraryPlugin.Log.LogError($"A CharactersPrepared handler threw: {ex}"); }
            }
        }

        // (path, face index inside a .ttc). Face 0 of NotoSansCJK-*.ttc is JP,
        // 1 KR, 2 SC, 3 TC. "~" expands to the home directory; "fonts/" is
        // relative to the plugin folder.
        private static readonly string[] JapaneseFiles =
        {
            "fonts/*jp*.ttf", "fonts/*jp*.otf", "fonts/*.ttc", "fonts/*.ttf", "fonts/*.otf",
            "/run/host/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
            "~/.local/share/fonts/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/truetype/noto/NotoSansJP-Regular.ttf",
            "/System/Library/Fonts/ヒラギノ角ゴシック W3.ttc",
            "/System/Library/Fonts/Hiragino Sans GB.ttc",
            "C:/Windows/Fonts/YuGothM.ttc",
            "C:/Windows/Fonts/msgothic.ttc",
        };

        private static readonly string[] ChineseFiles =
        {
            "fonts/*sc*.ttf", "fonts/*sc*.otf", "fonts/*.ttc",
            "/run/host/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
            "~/.local/share/fonts/NotoSansCJK-Regular.ttc",
            "/System/Library/Fonts/PingFang.ttc",
            "C:/Windows/Fonts/msyh.ttc",
        };

        private static readonly string[] KoreanFiles =
        {
            "fonts/*kr*.ttf", "fonts/*kr*.otf", "fonts/*.ttc",
            "/run/host/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
            "~/.local/share/fonts/NotoSansCJK-Regular.ttc",
            "/System/Library/Fonts/AppleSDGothicNeo.ttc",
            "C:/Windows/Fonts/malgun.ttf",
        };

        private static readonly string[] TraditionalFiles =
        {
            "fonts/*tc*.ttf", "fonts/*tc*.otf", "fonts/*.ttc",
            "/run/host/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
            "~/.local/share/fonts/NotoSansCJK-Regular.ttc",
            "/System/Library/Fonts/PingFang.ttc",
            "C:/Windows/Fonts/msjh.ttc",
        };

        private static readonly string[] WesternFiles =
        {
            "fonts/*latin*.ttf", "fonts/*latin*.otf",
            "/run/host/fonts/TTF/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/noto/NotoSans-Regular.ttf",
            "/System/Library/Fonts/Helvetica.ttc",
            "C:/Windows/Fonts/segoeui.ttf",
            "C:/Windows/Fonts/arial.ttf",
        };

        private static readonly string[] ThaiFiles =
        {
            "fonts/*th*.ttf", "fonts/*th*.otf",
            "/run/host/fonts/noto/NotoSansThai-Regular.ttf",
            "/usr/share/fonts/noto/NotoSansThai-Regular.ttf",
            "/usr/share/fonts/truetype/noto/NotoSansThai-Regular.ttf",
            "/usr/share/fonts/TTF/Garuda.ttf",
            "/usr/share/fonts/truetype/tlwg/Garuda.ttf",
            "/System/Library/Fonts/Thonburi.ttc",
            "C:/Windows/Fonts/LeelawUI.ttf",
            "C:/Windows/Fonts/leelawad.ttf",
            "C:/Windows/Fonts/tahoma.ttf",
        };

        private static readonly string[] HebrewFiles =
        {
            "fonts/*he*.ttf", "fonts/*he*.otf",
            "/run/host/fonts/noto/NotoSansHebrew-Regular.ttf",
            "/usr/share/fonts/noto/NotoSansHebrew-Regular.ttf",
            "/usr/share/fonts/truetype/noto/NotoSansHebrew-Regular.ttf",
            "/run/host/fonts/TTF/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/System/Library/Fonts/Supplemental/Arial Hebrew.ttc",
            "C:/Windows/Fonts/segoeui.ttf",
            "C:/Windows/Fonts/arial.ttf",
        };
    }
}
