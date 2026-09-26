using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace DragNWash.ModFramework.Text
{
    /// <summary>
    /// What a text rewriter receives: the component, the text the game set and
    /// the text that will be shown, which a rewriter may change.
    /// </summary>
    public sealed class TextContext
    {
        internal TextContext(TMP_Text component, string source, bool fromPrefab, bool refreshing)
        {
            Component = component;
            Source = source;
            Text = source;
            FromPrefab = fromPrefab;
            IsRefresh = refreshing;
        }

        /// <summary>The TextMeshPro component the text goes into.</summary>
        public TMP_Text Component { get; }

        /// <summary>The text the game set, before any rewriter ran.</summary>
        public string Source { get; }

        /// <summary>The text that will be shown. Rewriters may replace it; later rewriters see the change.</summary>
        public string Text { get; set; }

        /// <summary>True for text that was in a prefab and never set by code (menus, labels).</summary>
        public bool FromPrefab { get; }

        /// <summary>True when the text is re-applied by <see cref="GameText.RefreshAll"/>.</summary>
        public bool IsRefresh { get; }
    }

    /// <summary>
    /// Text library: lets mods see and replace every TextMeshPro text before the
    /// game shows it, and apply their changes again later (after a language
    /// switch, for example). Several mods can rewrite text; they run in order.
    /// </summary>
    /// <remarks>
    /// Depend on it with
    /// <c>[BepInDependency(GameText.Guid, BepInDependency.DependencyFlags.HardDependency)]</c>.
    /// </remarks>
    public static class GameText
    {
        /// <summary>BepInEx GUID of the text library.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.text";

        /// <summary>Library version. Keep in sync with the csproj.</summary>
        public const string Version = "1.6.0";

        private sealed class Rewriter
        {
            public string Owner;
            public int Order;
            public Action<TextContext> Action;
        }

        private static readonly List<Rewriter> Rewriters = new List<Rewriter>();

        // Who rewrites text, in the order they run.
        internal static List<KeyValuePair<string, int>> RewriterOwners()
        {
            lock (Rewriters)
            {
                return Rewriters.OrderBy(r => r.Order).Select(r => new KeyValuePair<string, int>(r.Owner, r.Order)).ToList();
            }
        }
        private static Rewriter[] _snapshot = new Rewriter[0];
        private static readonly Dictionary<TMP_Text, string> Sources = new Dictionary<TMP_Text, string>();
        // What the rewriters last put on each component. When the component shows
        // something else, the game changed it through a path the library does not
        // hook (formatted SetText, SetCharArray), and its source is stale.
        private static readonly Dictionary<TMP_Text, string> Shown = new Dictionary<TMP_Text, string>();
        private static int _rewriteDepth;

        /// <summary>True when the library's hooks are installed on this game build.</summary>
        public static bool IsAvailable { get; internal set; }

        /// <summary>
        /// Adds a rewriter. Rewriters run from the lowest <paramref name="order"/> up;
        /// use 0 unless a mod needs to run before or after another. An exception in a
        /// rewriter is logged and the text is left as it was.
        /// </summary>
        /// <param name="ownerGuid">BepInEx GUID of the mod adding it, for logs.</param>
        /// <param name="rewrite">Called for every text; set <see cref="TextContext.Text"/> to change it.</param>
        /// <param name="order">Run order.</param>
        /// <returns>A handle that removes the rewriter when disposed.</returns>
        public static IDisposable AddRewriter(string ownerGuid, Action<TextContext> rewrite, int order = 0)
        {
            if (rewrite == null)
            {
                throw new ArgumentNullException(nameof(rewrite));
            }
            var rewriter = new Rewriter { Owner = ownerGuid, Order = order, Action = rewrite };
            lock (Rewriters)
            {
                Rewriters.Add(rewriter);
                _snapshot = Rewriters.OrderBy(r => r.Order).ToArray();
            }
            return new Removal(rewriter);
        }

        /// <summary>
        /// The text the game last set on <paramref name="component"/>, before rewriters.
        /// False for components the library has not seen.
        /// </summary>
        public static bool TryGetSource(TMP_Text component, out string source)
        {
            if (component != null && Sources.TryGetValue(component, out source) && IsCurrent(component))
            {
                return true;
            }
            source = null;
            return false;
        }

        /// <summary>
        /// Runs every rewriter again on every text on screen, from the text the game
        /// set. Call it on the main thread after something rewriters depend on changed.
        /// </summary>
        public static void RefreshAll()
        {
            var dead = new List<TMP_Text>();
            foreach (KeyValuePair<TMP_Text, string> pair in Sources.ToArray())
            {
                TMP_Text component = pair.Key;
                if (component == null)
                {
                    dead.Add(component);
                    continue;
                }
                if (!IsCurrent(component))
                {
                    dead.Add(component);
                    continue;
                }
                string text = Run(component, pair.Value, fromPrefab: false, refreshing: true);

                // The game's typewriter reveals dialogue by raising
                // maxVisibleCharacters to the current text's length. If the text
                // was fully shown, lift the cap, or a longer string is cut off.
                int shown = component.textInfo != null ? component.textInfo.characterCount : 0;
                bool fullyShown = component.maxVisibleCharacters >= shown;

                _rewriteDepth++;
                try
                {
                    component.text = text;
                }
                finally
                {
                    _rewriteDepth--;
                }
                Shown[component] = text;
                if (fullyShown && component.maxVisibleCharacters != int.MaxValue)
                {
                    component.maxVisibleCharacters = int.MaxValue;
                }
            }
            foreach (TMP_Text component in dead)
            {
                Forget(component);
            }
        }

        // From the setter and SetText hooks.
        internal static void OnSet(TMP_Text component, ref string text)
        {
            if (_rewriteDepth > 0 || component == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(text))
            {
                // The game cleared it: whatever the component showed before (often
                // placeholder text from the prefab) is not to be shown again.
                Forget(component);
                return;
            }
            if (Sources.Count > 4096 && !Sources.ContainsKey(component))
            {
                Prune();
            }
            Sources[component] = text;
            text = Run(component, text, fromPrefab: false, refreshing: false);
            Shown[component] = text;
        }

        // From OnEnable: prefab text is deserialized straight into the component
        // and never passes through the setter.
        internal static void OnEnabled(TMP_Text component)
        {
            if (_rewriteDepth > 0 || component == null || Sources.ContainsKey(component))
            {
                return;
            }
            string source = component.text;
            if (string.IsNullOrEmpty(source))
            {
                return;
            }
            Sources[component] = source;
            string text = Run(component, source, fromPrefab: true, refreshing: false);
            Shown[component] = text;
            if (text == source)
            {
                return;
            }
            _rewriteDepth++;
            try
            {
                component.text = text;
            }
            finally
            {
                _rewriteDepth--;
            }
        }

        // Components destroyed with their scene stay in the dictionary as Unity
        // "null" objects until removed.
        private static void Prune()
        {
            foreach (TMP_Text component in Sources.Keys.Where(k => k == null).ToList())
            {
                Forget(component);
            }
        }

        private static bool IsCurrent(TMP_Text component)
        {
            return !Shown.TryGetValue(component, out string shown) || component.text == shown;
        }

        private static void Forget(TMP_Text component)
        {
            Sources.Remove(component);
            Shown.Remove(component);
        }

        private static string Run(TMP_Text component, string source, bool fromPrefab, bool refreshing)
        {
            Rewriter[] rewriters = _snapshot;
            if (rewriters.Length == 0)
            {
                return source;
            }
            var context = new TextContext(component, source, fromPrefab, refreshing);
            foreach (Rewriter rewriter in rewriters)
            {
                string before = context.Text;
                try
                {
                    rewriter.Action(context);
                    if (context.Text == null)
                    {
                        context.Text = before;
                    }
                }
                catch (Exception ex)
                {
                    context.Text = before;
                    TextLibraryPlugin.Log.LogError($"A text rewriter of {rewriter.Owner ?? "a mod"} threw: {ex}");
                }
            }
            return context.Text;
        }

        // On a mod's reload (ModReload.Unloading): its rewriters go.
        internal static void RemoveOwned(string owner, Assembly assembly)
        {
            lock (Rewriters)
            {
                Rewriters.RemoveAll(r => r.Owner == owner || (assembly != null && r.Action.Method?.DeclaringType?.Assembly == assembly));
                _snapshot = Rewriters.OrderBy(r => r.Order).ToArray();
            }
        }

        private sealed class Removal : IDisposable
        {
            private Rewriter _rewriter;

            public Removal(Rewriter rewriter)
            {
                _rewriter = rewriter;
            }

            public void Dispose()
            {
                if (_rewriter == null)
                {
                    return;
                }
                lock (Rewriters)
                {
                    Rewriters.Remove(_rewriter);
                    _snapshot = Rewriters.OrderBy(r => r.Order).ToArray();
                }
                _rewriter = null;
            }
        }
    }
}
