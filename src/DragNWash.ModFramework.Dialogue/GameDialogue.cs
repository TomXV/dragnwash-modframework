using System;
using System.Collections.Generic;
using TMPro;

namespace DragNWash.ModFramework.Dialogue
{
    /// <summary>A line of dialogue or an option, as the game is about to show it.</summary>
    public sealed class DialogueLine
    {
        internal DialogueLine() { }

        /// <summary>Yarn line ID, e.g. "line:6046bedf". Stable across languages and game updates that only fix typos.</summary>
        public string LineId { get; internal set; }

        /// <summary>
        /// The speaking character's name as written in the script ("Ryan: Hello"), or
        /// null. Drag'n Wash's script mostly names no speaker in the line; see
        /// <see cref="SpeakerGuess"/>.
        /// </summary>
        public string Speaker { get; internal set; }

        /// <summary>
        /// Who says the line: <see cref="Speaker"/> when the script names one, else a
        /// guess from the node's name (the part before the first '_': "Ryan_1_intro" is
        /// Ryan), and "Kobold" (the player) for an option. Null when there is nothing
        /// to go on. <see cref="SpeakerFrom"/> says which. Since Dialogue 1.2.0.
        /// </summary>
        public string SpeakerGuess { get; internal set; }

        /// <summary>Where <see cref="SpeakerGuess"/> came from: "script", "node", "option", or null.</summary>
        public string SpeakerFrom { get; internal set; }

        /// <summary>The text without the character name.</summary>
        public string Text { get; internal set; }

        /// <summary>The text with the character name, as the script has it ("Ryan: Hello").</summary>
        public string FullText { get; internal set; }

        /// <summary>Yarn metadata tags on the line (for example "lastline").</summary>
        public IReadOnlyList<string> Metadata { get; internal set; }

        /// <summary>The Yarn node the line belongs to, when known.</summary>
        public string Node { get; internal set; }

        /// <summary>The TextMeshPro component the text is about to go into.</summary>
        public TMP_Text Component { get; internal set; }

        /// <summary>True for an option the player can pick, false for a spoken line.</summary>
        public bool IsOption { get; internal set; }

        /// <summary>For options: whether the option can be picked (unavailable ones are struck through).</summary>
        public bool IsAvailable { get; internal set; } = true;
    }

    /// <summary>
    /// Dialogue library: tells mods which line of dialogue or option is about to
    /// be shown, with its line ID, speaker and node, and which line a text
    /// component is showing. Combine with the text library to change dialogue
    /// per line.
    /// </summary>
    /// <remarks>
    /// Depend on it with
    /// <c>[BepInDependency(GameDialogue.Guid, BepInDependency.DependencyFlags.HardDependency)]</c>.
    /// </remarks>
    public static class GameDialogue
    {
        /// <summary>BepInEx GUID of the dialogue library.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework.dialogue";

        /// <summary>Library version. Keep in sync with the csproj.</summary>
        public const string Version = "1.6.0";

        private static readonly Dictionary<TMP_Text, DialogueLine> ByComponent = new Dictionary<TMP_Text, DialogueLine>();

        /// <summary>True when the hooks for spoken lines are installed on this game build.</summary>
        public static bool LinesAvailable { get; internal set; }

        /// <summary>True when the hooks for options are installed on this game build.</summary>
        public static bool OptionsAvailable { get; internal set; }

        /// <summary>The Yarn node that started most recently, or null.</summary>
        public static string CurrentNode { get; internal set; }

        /// <summary>Raised when a Yarn node starts, with the node name.</summary>
        public static event Action<string> NodeStarted;

        /// <summary>Raised just before a spoken line starts to appear.</summary>
        public static event Action<DialogueLine> LineShowing;

        /// <summary>Raised just before an option's text is set.</summary>
        public static event Action<DialogueLine> OptionShowing;

        /// <summary>
        /// The line <paramref name="component"/> was last handed, if <paramref name="text"/>
        /// is that line's text (with or without the character name, or struck through
        /// for an unavailable option). Lets a text rewriter tell which line it is
        /// rewriting; anything else the component shows later does not match.
        /// </summary>
        public static bool TryGetLine(TMP_Text component, string text, out DialogueLine line)
        {
            line = null;
            if (component == null || string.IsNullOrEmpty(text) || !ByComponent.TryGetValue(component, out DialogueLine known))
            {
                return false;
            }
            const string StrikeOpen = "<s>", StrikeClose = "</s>";
            if (text.StartsWith(StrikeOpen, StringComparison.Ordinal) && text.EndsWith(StrikeClose, StringComparison.Ordinal))
            {
                text = text.Substring(StrikeOpen.Length, text.Length - StrikeOpen.Length - StrikeClose.Length);
            }
            if (text != known.FullText && text != known.Text)
            {
                return false;
            }
            line = known;
            return true;
        }

        internal static void OnNodeStarted(string node)
        {
            CurrentNode = node;
            Raise(NodeStarted, node);
        }

        internal static void OnLine(TMP_Text component, DialogueLine line)
        {
            Remember(component, line);
            Raise(line.IsOption ? OptionShowing : LineShowing, line);
        }

        private static void Remember(TMP_Text component, DialogueLine line)
        {
            if (component == null)
            {
                return;
            }
            if (ByComponent.Count > 64)
            {
                var dead = new List<TMP_Text>();
                foreach (TMP_Text key in ByComponent.Keys)
                {
                    if (key == null)
                    {
                        dead.Add(key);
                    }
                }
                foreach (TMP_Text key in dead)
                {
                    ByComponent.Remove(key);
                }
            }
            ByComponent[component] = line;
        }

        // Each subscriber runs on its own: one throwing does not stop the others
        // or the game showing its line.
        private static void Raise<T>(Action<T> handlers, T value)
        {
            if (handlers == null)
            {
                return;
            }
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(value);
                }
                catch (Exception ex)
                {
                    DialogueLibraryPlugin.Log.LogError($"A dialogue event handler threw: {ex}");
                }
            }
        }
    }
}
