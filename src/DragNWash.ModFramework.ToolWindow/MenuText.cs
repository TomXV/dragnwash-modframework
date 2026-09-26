using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    // IMGUI can only draw with an OS font, and inside Steam's Linux runtime
    // (Steam Deck) no OS font has CJK glyphs, so Japanese or Chinese text in
    // the menu came out as tofu boxes. While the menu is drawing, every
    // string handed to a GUI or GUILayout method as a string passes through
    // GUIContent.Temp; characters the menu font cannot draw are swapped for
    // '?' there. Text in a GUIContent the caller made (a button with a
    // tooltip, a size measured with CalcSize) does not pass there and is
    // drawn or measured as it is, so callers pass it through
    // ToolWindow.Drawable first. The game's own text is unaffected (it goes
    // through TextMeshPro with the file-loaded fallback fonts).
    //
    // "While the menu is drawing" is between Begin and End. IMGUI runs a
    // window's function after the OnGUI that called GUI.Window has returned
    // (from GUI.EndWindows), so the window's function calls them itself; the
    // plugin's OnGUI calls them for the overlays and the toast.
    //
    // Text fields are left alone: GUI.TextField hands the text it is given
    // to GUIContent.Temp and returns what comes back as the field's new
    // value, so a swap there would write '?' into what the player typed or
    // pasted. While one runs (IntoField to OutOfField), nothing is swapped.
    //
    // On Unity 6 "can draw" is asked of TextCore, which is what IMGUI draws
    // with (see CheckTextCore); elsewhere, of the font itself.
    internal static class MenuText
    {
        private static Font _font;
        private static bool _active;
        private static int _inField;
        private static readonly Dictionary<char, bool> _drawable = new Dictionary<char, bool>();

        // Every method that hands a text field's text to GUIContent.Temp, as
        // Unity 6000.3 has them. GUI's and GUILayout's other PasswordField
        // overloads and GUILayout's TextField and TextArea only pass their
        // arguments on to one of these.
        private static readonly FieldMethod[] TextFields =
        {
            new FieldMethod(typeof(GUI), "TextField", typeof(Rect), typeof(string)),
            new FieldMethod(typeof(GUI), "TextField", typeof(Rect), typeof(string), typeof(int)),
            new FieldMethod(typeof(GUI), "TextField", typeof(Rect), typeof(string), typeof(GUIStyle)),
            new FieldMethod(typeof(GUI), "TextField", typeof(Rect), typeof(string), typeof(int), typeof(GUIStyle)),
            new FieldMethod(typeof(GUI), "TextArea", typeof(Rect), typeof(string)),
            new FieldMethod(typeof(GUI), "TextArea", typeof(Rect), typeof(string), typeof(int)),
            new FieldMethod(typeof(GUI), "TextArea", typeof(Rect), typeof(string), typeof(GUIStyle)),
            new FieldMethod(typeof(GUI), "TextArea", typeof(Rect), typeof(string), typeof(int), typeof(GUIStyle)),
            new FieldMethod(typeof(GUI), "PasswordField", typeof(Rect), typeof(string), typeof(char), typeof(int), typeof(GUIStyle)),
            // Private: measures the field with the text (and what the IME is
            // composing) and then draws it.
            new FieldMethod(typeof(GUILayout), "DoTextField", typeof(string), typeof(int), typeof(bool), typeof(GUIStyle), typeof(GUILayoutOption[])),
            new FieldMethod(typeof(GUILayout), "PasswordField", typeof(string), typeof(char), typeof(int), typeof(GUIStyle), typeof(GUILayoutOption[])),
        };

        private sealed class FieldMethod
        {
            public readonly Type Type;
            public readonly string Name;
            public readonly Type[] Args;

            public FieldMethod(Type type, string name, params Type[] args)
            {
                Type = type;
                Name = name;
                Args = args;
            }

            public override string ToString() => $"{Type.Name}.{Name}({Args.Length})";
        }

        public static void Install(Harmony harmony)
        {
            // The text fields first: once GUIContent.Temp is patched, a field
            // that is not would get '?' written into it. One that fails to
            // patch throws, and Temp is then left alone. One this Unity does
            // not have is never called, so it is only said, once.
            var into = new HarmonyMethod(typeof(MenuText), nameof(IntoField));
            var outOf = new HarmonyMethod(typeof(MenuText), nameof(OutOfField));
            var missing = new List<string>();
            foreach (FieldMethod field in TextFields)
            {
                MethodInfo method = AccessTools.Method(field.Type, field.Name, field.Args);
                if (method == null)
                {
                    missing.Add(field.ToString());
                    continue;
                }
                harmony.Patch(method, prefix: into, finalizer: outOf);
            }
            if (missing.Count > 0)
            {
                ToolWindowPlugin.Log.LogInfo($"Window font: not in this Unity, so not kept clear of the '?' swap: {string.Join(", ", missing)}. If what is typed into the Tool window turns into '?', this is why.");
            }
            MethodInfo temp = AccessTools.Method(typeof(GUIContent), "Temp", new[] { typeof(string) });
            if (temp != null)
                harmony.Patch(temp, prefix: new HarmonyMethod(typeof(MenuText), nameof(BeforeTemp)));
        }

        // Returns whether it was on already, for End to put back: a window's
        // function can run inside the OnGUI that turned it on (IMGUI decides
        // when), and must not turn it off under that OnGUI.
        public static bool Begin(Font menuFont, int size)
        {
            bool was = _active;
            _font = menuFont;
            _size = size;
            _active = menuFont != null;
            // No text field is running when drawing starts. A count left over,
            // should a field ever not get to leave, would stop every swap for
            // good.
            _inField = 0;
            return was;
        }

        public static void End(bool was = false)
        {
            _active = was;
        }

        // Every text field enters, and leaves even when it throws (a
        // finalizer runs then too, ExitGUIException included).
        private static void IntoField()
        {
            _inField++;
        }

        private static void OutOfField()
        {
            if (_inField > 0)
            {
                _inField--;
            }
        }

        public static bool CanDraw(Font font, int size, string text)
        {
            _size = size;
            if (font == null || string.IsNullOrEmpty(text)) return true;
            // Outside OnGUI, the characters not asked about yet are asked all
            // at once, one atlas upload for them all, rather than one by one
            // below.
            if (font == MenuFont.Font) CheckNow(text);
            bool afterCharacter = false;
            for (int i = 0; i < text.Length; i++)
            {
                // Dropped when drawn (see BeforeTemp), so not counted.
                int selector = afterCharacter ? SelectorLength(text, i) : 0;
                if (selector > 0)
                {
                    i += selector - 1;
                    afterCharacter = false;
                    continue;
                }
                afterCharacter = true;
                if (!Drawable(font, text[i])) return false;
            }
            return true;
        }

        // Unity's dynamic fonts answer HasCharacter with true and then draw
        // the fallback's "missing glyph" box, so compare the glyph with the
        // one an unassigned code point (U+0378) gets: same box, not drawable.
        private const char Undefined = '͸';
        private static int _size;

        // Characters asked about inside OnGUI, checked on the next Update.
        // Prepared ones are checked as they are prepared (CheckNow), so they
        // do not wait here.
        private static readonly HashSet<char> _unchecked = new HashSet<char>();

        // Whether this runs inside OnGUI or a window's function, where
        // checking a character must not happen (see Drawable). Event.current
        // cannot tell: in a built game IMGUI's first event sets it and nothing
        // sets it back to null, so Update sees it too (only the editor hides
        // it outside OnGUI). GUIUtility.guiDepth can: it counts the OnGUI
        // calls running, and is above 0 in a window's function too, since GUI
        // methods refuse to run where it is 0 (GUIUtility.CheckOnGUI). It is
        // internal, so its getter is bound once. Where that fails,
        // Event.current answers as before, which takes Update for OnGUI once
        // IMGUI has run: characters are then checked only on the Update after
        // the window first draws them. A thread other than the one the
        // plugin's Awake ran on gets yes too, as does a read that throws:
        // nothing is checked there, and a preparation waits for Update.
        private static int _mainThread;   // 0 until Awake notes it
        private static bool _depthBound;
        private static Func<int> _guiDepth;
        private static bool _saidDepthUnread;

        // From the plugin's Awake.
        internal static void NoteMainThread()
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        internal static bool InOnGUI
        {
            get
            {
                if (_mainThread != 0 && Thread.CurrentThread.ManagedThreadId != _mainThread)
                {
                    return true;
                }
                if (!_depthBound)
                {
                    _guiDepth = BindGuiDepth();
                    _depthBound = true;
                }
                if (_guiDepth == null)
                {
                    return Event.current != null;
                }
                try
                {
                    return _guiDepth() > 0;
                }
                catch (Exception ex)
                {
                    if (!_saidDepthUnread)
                    {
                        _saidDepthUnread = true;
                        ToolWindowPlugin.Log.LogDebug($"Window font: could not read GUIUtility.guiDepth ({ex.GetBaseException().Message}); taken as inside OnGUI, so characters are checked on the next Update.");
                    }
                    return true;
                }
            }
        }

        private static Func<int> BindGuiDepth()
        {
            string reason;
            try
            {
                MethodInfo getter = typeof(GUIUtility).GetProperty("guiDepth", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
                if (getter != null && getter.ReturnType == typeof(int))
                {
                    return (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), getter);
                }
                reason = "not in this Unity";
            }
            catch (Exception ex)
            {
                reason = ex.GetBaseException().Message;
            }
            ToolWindowPlugin.Log.LogDebug($"Window font: cannot read GUIUtility.guiDepth ({reason}); telling OnGUI from Update by Event.current, so prepared characters show as '?' for a frame and cut text may end in \"...\".");
            return null;
        }

        private static bool Drawable(Font font, char c)
        {
            if (c < 128) return true;
            if (_drawable.TryGetValue(c, out bool ok)) return ok;
            if (InOnGUI && (font.dynamic || TextCoreAsset(font) != null))
            {
                // Checking rasterises into the font, or into TextCore's atlas,
                // and doing that while the window draws uploads its texture
                // mid-frame (UUM-140564 on Direct3D 12): '?' this once, as the
                // console does.
                _unchecked.Add(c);
                return false;
            }
            Check(font, new[] { c });
            return _drawable[c];
        }

        // Prepared characters not checked yet (see CheckNow), oldest first.
        private static readonly List<char> _backlog = new List<char>();
        private static readonly HashSet<char> _inBacklog = new HashSet<char>();

        // How long an Update may spend on the backlog. Checking a character adds
        // its glyph to TextCore's atlases, the window font's and the fallbacks'
        // it falls through to, a few milliseconds each on Windows: the 870 the
        // Localization mod prepares took 2.5 s in one Update, a freeze the first
        // time F1 was pressed.
        private const double BacklogMsPerFrame = 6;

        // From the plugin's Update: what OnGUI could not check, all at once
        // (it is on screen), then as much of the backlog as fits in the budget.
        internal static void CheckQueued()
        {
            Font font = MenuFont.Font;
            if (_unchecked.Count > 0)
            {
                var chars = new char[_unchecked.Count];
                _unchecked.CopyTo(chars);
                _unchecked.Clear();
                if (font != null)
                {
                    foreach (char c in chars) if (_inBacklog.Remove(c)) _backlog.Remove(c);
                    Check(font, chars);
                }
            }
            if (font == null || _backlog.Count == 0 || InOnGUI) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            long budget = (long)(BacklogMsPerFrame * System.Diagnostics.Stopwatch.Frequency / 1000);
            int done = 0;
            while (done < _backlog.Count && System.Diagnostics.Stopwatch.GetTimestamp() - started < budget)
            {
                int take = Math.Min(4, _backlog.Count - done);
                var slice = _backlog.GetRange(done, take).ToArray();
                done += take;
                foreach (char c in slice) _inBacklog.Remove(c);
                slice = Array.FindAll(slice, c => !_drawable.ContainsKey(c));
                if (slice.Length > 0) Check(font, slice);
            }
            _backlog.RemoveRange(0, done);
        }

        // From MenuFont as characters are prepared, in Update or Awake, and
        // from CanDraw: the ones not asked about yet are asked now, in one
        // batch, so the first frame that shows them draws them instead of '?'
        // (from OnGUI, Drawable can only queue them for the next Update). On
        // Direct3D 12 with the core's batching, glyphs added in an Update
        // reach the GPU at the end of that frame, so text first shown in the
        // same frame can be blank for it.
        // When MenuFont makes the font, the printable ASCII it prepares is in
        // that batch: on Unity 6 this puts those glyphs, the '?' the swap
        // draws among them, into TextCore's atlas here rather than in the
        // window's first draw. Drawable still passes ASCII without looking.
        // Control characters and variation selectors (see BeforeTemp) are
        // left out. Never from OnGUI, for the reason Drawable gives; asked
        // from there, nothing is done and drawing queues them as before.
        internal static void CheckNow(IEnumerable<char> chars)
        {
            Font font = MenuFont.Font;
            if (font == null || chars == null || InOnGUI) return;
            HashSet<char> fresh = null;
            foreach (char c in chars)
            {
                if (c < ' ' || c == '\u007F' || (c >= '\uFE00' && c <= '\uFE0F') || _drawable.ContainsKey(c)) continue;
                if (fresh == null) fresh = new HashSet<char>();
                fresh.Add(c);
            }
            if (fresh == null) return;
            // Printable ASCII and a handful are asked now; a bigger batch (every
            // character mods prepared, when the font is made) goes to the
            // backlog, which Update works through a few milliseconds at a time.
            // Until then such a character reads as '?' where it is drawn, and
            // drawing it moves it ahead (see CheckQueued).
            var now = new List<char>();
            foreach (char c in fresh)
            {
                if (c < 128 || fresh.Count <= 8) now.Add(c);
                else if (_inBacklog.Add(c)) _backlog.Add(c);
            }
            if (now.Count == 0) return;
            var batch = now.ToArray();
            // Answered here, so not asked again on the next Update.
            _unchecked.ExceptWith(batch);
            _size = MenuFont.Size;
            Check(font, batch);
        }

        // One rasterisation for all of them, so a tab full of new text
        // uploads the font's texture once, not once per character.
        private static void Check(Font font, char[] chars)
        {
            // The answers change, so strings swapped with the old ones go.
            _swapped.Clear();
            if (CheckTextCore(font, chars)) return;
            try
            {
                if (!font.dynamic)
                {
                    foreach (char c in chars) _drawable[c] = font.HasCharacter(c);
                    return;
                }
                font.RequestCharactersInTexture(new string(chars) + Undefined, _size, FontStyle.Normal);
                bool boxed = font.GetCharacterInfo(Undefined, out CharacterInfo none, _size, FontStyle.Normal);
                foreach (char c in chars)
                {
                    _drawable[c] = font.GetCharacterInfo(c, out CharacterInfo info, _size, FontStyle.Normal)
                                   && info.advance > 0
                                   && !(boxed && none.uvBottomLeft == info.uvBottomLeft && none.uvTopRight == info.uvTopRight);
                }
            }
            catch
            {
                foreach (char c in chars) _drawable[c] = false;
            }
        }

        // Unity 6's IMGUI does not draw with the window's Font but with the
        // TextCore font asset made from it (see MenuFont). That asset is
        // made from the Font's first name only, so a character the Font has
        // from a second name (Apple Symbols, Segoe UI Symbol) can still come
        // out as TextCore's missing-glyph box (U+25A1). So where MenuFont
        // kept that asset, TextCore is asked, down the same list its text
        // generator goes (TextGenerator.GetTextElement): the asset and the
        // fallbacks set on it, the settings' fallback assets, the OS
        // fallbacks (made from Font.GetOSFallbacks the first time a
        // character is missing), then the settings' default asset.
        // FontAsset.HasCharacter(c, true, true) covers one asset and its own
        // fallbacks, and adds a glyph it finds to that asset's atlas the way
        // drawing would, so the atlas changes here, from Update, and its
        // upload is started once at the end. False when TextCore cannot be
        // asked; the Font is then asked as before.
        private static bool _textCoreBroken;
        private static MethodInfo _hasCharacter;
        private static PropertyInfo _fallbackAssets, _osFallbackAssets, _defaultAsset;
        private static MethodInfo _uploadAtlases;

        private static UnityEngine.Object TextCoreAsset(Font font)
        {
            UnityEngine.Object asset = MenuFont.TextCoreAsset;
            return !_textCoreBroken && font == MenuFont.Font && asset != null && MenuFont.TextCoreSettings != null ? asset : null;
        }

        private static bool CheckTextCore(Font font, char[] chars)
        {
            UnityEngine.Object asset = TextCoreAsset(font);
            if (asset == null) return false;
            object settings = MenuFont.TextCoreSettings;
            if (_hasCharacter == null)
            {
                try
                {
                    BindTextCore(asset.GetType(), settings.GetType());
                }
                catch (Exception ex)
                {
                    // A member this Unity lacks stays missing, so it is not
                    // tried again: from here on the Font answers, as on older
                    // Unity.
                    _textCoreBroken = true;
                    FellBack(ex, "from now on");
                    return false;
                }
            }
            var found = new bool[chars.Length];
            bool asked = false;
            try
            {
                for (int i = 0; i < chars.Length; i++)
                {
                    char c = chars[i];
                    // The later lists are read only when needed, as the generator
                    // does: the OS fallbacks are made the first time they are read.
                    found[i] = Has(asset, c)
                               || HasAny(_fallbackAssets.GetValue(settings, null) as IList, c)
                               || HasAny(OsFallbacks(settings), c)
                               || Has(_defaultAsset.GetValue(settings, null), c);
                }
                asked = true;
            }
            catch (Exception ex)
            {
                // The members are there but TextCore failed on one of these
                // characters (making an OS fallback asset, say). That can be
                // this batch only, so the Font answers for it and TextCore is
                // asked again for the next.
                FellBack(ex, $"for {chars.Length} character{(chars.Length == 1 ? "" : "s")} only");
            }
            if (asked)
            {
                for (int i = 0; i < chars.Length; i++)
                {
                    _drawable[chars[i]] = found[i];
                }
            }
            try
            {
                // What the glyphs added above changed, a failed batch's too,
                // is uploaded now, outside OnGUI, rather than by the window's
                // next text. Where the core batches uploads on Direct3D 12
                // (FontAtlasUploads, [Direct3D12] BatchFontAtlasUploads on), it
                // moves this to the end of the frame; otherwise the Apply
                // happens here.
                _uploadAtlases?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogDebug($"Could not upload TextCore's atlas after checking characters ({ex.GetBaseException().Message}); the window's next text uploads it.");
            }
            return asked;
        }

        // The window font's own check is the one Unity 6 gets wrong: it says
        // yes to glyphs only a second name has, which TextCore draws as boxes.
        // So falling back to it is said at Warning, which the log and the
        // Console tab show by default (Debug is hidden), and a report of boxes
        // comes with the reason. Once a session; after that, Debug only.
        private static bool _saidFallback;

        internal static void FellBack(Exception ex, string scope, string step = null)
        {
            Exception cause = ex.GetBaseException();
            string reason = (step != null ? step + ", " : "") + $"{cause.GetType().Name}: {cause.Message}";
            string line = $"Window font: could not ask TextCore which characters the window draws ({reason}); using the window font's own check {scope}, which can show boxes on Unity 6.";
            if (_saidFallback)
            {
                ToolWindowPlugin.Log.LogDebug(line);
                return;
            }
            _saidFallback = true;
            ToolWindowPlugin.Log.LogWarning(line);
        }

        // The members as Unity 6000.3 has them. The settings' fallback list is
        // read through its public property, which is what the generator's
        // GetFallbackFontAssets returns for IMGUI's settings. Every list is
        // needed: an answer that skipped one would say '?' for characters
        // TextCore draws, so a missing member, or one of another type (a list
        // that is not an IList would read as empty), means the Font answers
        // instead. The upload is optional.
        private static void BindTextCore(Type assetType, Type settingsType)
        {
            MethodInfo hasCharacter = assetType.GetMethod("HasCharacter", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(char), typeof(bool), typeof(bool) }, null);
            if (hasCharacter == null || hasCharacter.ReturnType != typeof(bool))
            {
                throw new MissingMethodException(assetType.FullName, "HasCharacter(char, bool, bool)");
            }
            _fallbackAssets = Property(settingsType, "fallbackFontAssets", typeof(IList));
            _osFallbackAssets = Property(settingsType, "fallbackOSFontAssets", typeof(IList));
            _defaultAsset = Property(settingsType, "defaultFontAsset", typeof(UnityEngine.Object));
            _uploadAtlases = hasCharacter.DeclaringType?.GetMethod("UpdateAtlasTexturesInQueue", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            _hasCharacter = hasCharacter;
        }

        // Declared on TextSettings, one of the settings object's base types.
        private static PropertyInfo Property(Type type, string name, Type expected)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                PropertyInfo property = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property == null) continue;
                if (property.CanRead && expected.IsAssignableFrom(property.PropertyType)) return property;
                break;
            }
            throw new MissingMemberException(type.FullName, $"{name} ({expected.Name})");
        }

        // Which OS fonts these are depends on the platform and decides what
        // else the window can draw, so they are named in the log, once.
        private static bool _saidOsFallbacks;

        private static IList OsFallbacks(object settings)
        {
            var assets = _osFallbackAssets.GetValue(settings, null) as IList;
            if (!_saidOsFallbacks && assets != null)
            {
                _saidOsFallbacks = true;
                var names = new List<string>();
                foreach (object asset in assets)
                {
                    if (asset is UnityEngine.Object alive && alive != null) names.Add(AssetName(alive));
                }
                ToolWindowPlugin.Log.LogInfo($"Window font: for characters it lacks, Unity's text engine looks in {(names.Count > 0 ? string.Join(", ", names) : "no OS font")}.");
            }
            return assets;
        }

        // The assets TextCore makes at runtime have an empty Object name, the
        // window's own included, so one is named by the face it was made from
        // (FontAsset.faceInfo): the family, and the style when it is not
        // Regular (Hiragino Sans W3).
        private static string AssetName(UnityEngine.Object asset)
        {
            try
            {
                object face = asset.GetType().GetProperty("faceInfo", BindingFlags.Instance | BindingFlags.Public)?.GetValue(asset, null);
                Type faceType = face?.GetType();
                string family = faceType?.GetProperty("familyName", BindingFlags.Instance | BindingFlags.Public)?.GetValue(face, null) as string;
                string style = faceType?.GetProperty("styleName", BindingFlags.Instance | BindingFlags.Public)?.GetValue(face, null) as string;
                if (!string.IsNullOrEmpty(family))
                {
                    return string.IsNullOrEmpty(style) || style == "Regular" ? family : family + " " + style;
                }
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogDebug($"Could not read a TextCore asset's face ({ex.GetBaseException().Message}); naming it by its Object name.");
            }
            return string.IsNullOrEmpty(asset.name) ? "(unnamed)" : asset.name;
        }

        private static bool Has(object asset, char c)
        {
            return asset is UnityEngine.Object alive && alive != null
                   && (bool)_hasCharacter.Invoke(asset, new object[] { c, true, true });
        }

        private static bool HasAny(IList assets, char c)
        {
            if (assets == null) return false;
            foreach (object asset in assets)
            {
                if (Has(asset, c)) return true;
            }
            return false;
        }

        // What strings that needed '?' (or lost a variation selector) became,
        // so text the window keeps showing with a character it cannot draw is
        // not built again at every IMGUI event (Layout, Repaint, input). Only
        // a string every character of which had an answer is kept, and Check,
        // which changes answers, empties it; full, it starts over.
        private const int SwappedMax = 256;
        private static readonly Dictionary<string, string> _swapped = new Dictionary<string, string>(StringComparer.Ordinal);

        private static void BeforeTemp(ref string t)
        {
            if (!_active || _inField > 0 || t == null) return;
            string original = t;
            StringBuilder sb = null;
            bool answered = true;
            bool afterCharacter = false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                // A variation selector right after a character asks for that
                // character's variant form. TextCore draws nothing for the
                // selector itself, without asking for a glyph, but looks the
                // variant up and adds it to its atlas while drawing, in OnGUI.
                // So it is dropped: no '?' for it, and the character keeps
                // its usual form.
                int selector = c >= 128 && afterCharacter ? SelectorLength(t, i) : 0;
                afterCharacter = selector == 0;
                if (selector == 0 && (c < 128 || Drawable(_font, c)))
                {
                    sb?.Append(c);
                    continue;
                }
                if (sb == null)
                {
                    // Every character before this one had an answer, and one
                    // kept here had them all, unchanged since.
                    if (_swapped.TryGetValue(original, out string known))
                    {
                        t = known;
                        return;
                    }
                    sb = new StringBuilder(t.Length);
                    sb.Append(t, 0, i);
                }
                if (selector > 0)
                {
                    i += selector - 1;
                    continue;
                }
                // Not in _drawable: queued for the next Update (see Drawable).
                if (!_drawable.ContainsKey(c)) answered = false;
                // A surrogate pair is one character on screen. It is always
                // '?': TextCore is asked one UTF-16 unit at a time, and has no
                // glyph for half of a pair.
                if (char.IsHighSurrogate(c) && i + 1 < t.Length) i++;
                sb.Append('?');
            }
            if (sb == null) return;
            t = sb.ToString();
            if (!answered) return;
            if (_swapped.Count >= SwappedMax) _swapped.Clear();
            _swapped[original] = t;
        }

        // How long the variation selector at i is: 1 for U+FE00 to U+FE0F, 2
        // for the surrogate pair of U+E0100 to U+E01EF, 0 for anything else.
        // TextCore takes one as a selector only right after a character
        // (TextGenerator.SetArraySizes); anywhere else it is a character of
        // its own.
        private static int SelectorLength(string t, int i)
        {
            char c = t[i];
            if (c >= '\uFE00' && c <= '\uFE0F') return 1;
            if (c == '\uDB40' && i + 1 < t.Length && t[i + 1] >= '\uDD00' && t[i + 1] <= '\uDDEF') return 2;
            return 0;
        }
    }
}
