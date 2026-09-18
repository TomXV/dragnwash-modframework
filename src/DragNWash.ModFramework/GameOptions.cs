using System;

namespace DragNWash.ModFramework
{
    /// <summary>Which section of the game's Options screen a row joins.</summary>
    public enum OptionsSection
    {
        /// <summary>Gameplay settings.</summary>
        Gameplay,
        /// <summary>Audio settings.</summary>
        Audio,
        /// <summary>Graphics settings.</summary>
        Graphics,
    }

    /// <summary>
    /// A dropdown row in the game's own Options screen. It follows the game's
    /// flow: picking a choice previews it and brings up the game's Save button,
    /// Save keeps it, and Back without saving returns to the saved choice.
    /// </summary>
    public sealed class OptionsChoice
    {
        /// <summary>Unique id, for example "com.example.mymod.difficulty". Required.</summary>
        public string Id { get; set; }

        /// <summary>Row label, in English; translation mods translate it like any UI text.</summary>
        public string Label { get; set; }

        /// <summary>The choices, in English. Required, at least two.</summary>
        public string[] Choices { get; set; }

        /// <summary>Section the row joins. Defaults to Gameplay.</summary>
        public OptionsSection Section { get; set; } = OptionsSection.Gameplay;

        /// <summary>Index chosen by the game's "Set Default" button. -1 leaves the row alone.</summary>
        public int DefaultIndex { get; set; } = -1;

        /// <summary>Returns the index that is currently saved. Required.</summary>
        public Func<int> GetSaved { get; set; }

        /// <summary>Called with the saved index when the player presses Save. Required.</summary>
        public Action<int> Save { get; set; }

        /// <summary>
        /// Optional: called whenever the shown choice changes, including going back
        /// to the saved one, so a mod can apply it at once as a preview.
        /// </summary>
        public Action<int> Preview { get; set; }
    }

    /// <summary>
    /// A slider row in the game's own Options screen, built by the game with its
    /// own slider (the minimum and maximum at its ends, the value shown while
    /// dragging). It follows the game's flow like <see cref="OptionsChoice"/>:
    /// moving the slider previews the value, Save keeps it, Back returns to the
    /// saved value.
    /// </summary>
    public sealed class OptionsSlider
    {
        /// <summary>Unique id, for example "com.example.mymod.speed". Required.</summary>
        public string Id { get; set; }

        /// <summary>Row label, in English; translation mods translate it like any UI text.</summary>
        public string Label { get; set; }

        /// <summary>Smallest value. Required to be below <see cref="Max"/>.</summary>
        public float Min { get; set; }

        /// <summary>Largest value.</summary>
        public float Max { get; set; } = 1f;

        /// <summary>Values snap to Min + n × Step. 0 (the default) is continuous.</summary>
        public float Step { get; set; }

        /// <summary>Section the row joins. Defaults to Gameplay.</summary>
        public OptionsSection Section { get; set; } = OptionsSection.Gameplay;

        /// <summary>Value set by the game's "Set Default" button. Null leaves the row alone.</summary>
        public float? DefaultValue { get; set; }

        /// <summary>Returns the value that is currently saved. Required.</summary>
        public Func<float> GetSaved { get; set; }

        /// <summary>Called with the saved value when the player presses Save. Required.</summary>
        public Action<float> Save { get; set; }

        /// <summary>
        /// Optional: called whenever the shown value changes, including going back
        /// to the saved one, so a mod can apply it at once as a preview.
        /// </summary>
        public Action<float> Preview { get; set; }
    }

    /// <summary>Adds rows to the game's own Options screen.</summary>
    public static class GameOptions
    {
        /// <summary>
        /// Adds a dropdown row. Can be called at any time; the row appears once the
        /// game's settings exist. Adding the same id again is ignored.
        /// </summary>
        public static void AddChoice(OptionsChoice choice)
        {
            if (choice == null || string.IsNullOrEmpty(choice.Id) || string.IsNullOrEmpty(choice.Label) ||
                choice.Choices == null || choice.Choices.Length < 2 || choice.GetSaved == null || choice.Save == null)
            {
                throw new ArgumentException("OptionsChoice needs Id, Label, at least two Choices, GetSaved and Save.", nameof(choice));
            }
            Options.OptionsRows.Add(choice);
        }

        /// <summary>
        /// Adds an Off/On row. <paramref name="save"/> receives the saved value and
        /// <paramref name="preview"/>, if given, the value being shown.
        /// </summary>
        public static void AddToggle(string id, string label, Func<bool> getSaved, Action<bool> save,
            Action<bool> preview = null, OptionsSection section = OptionsSection.Gameplay, bool? defaultValue = null)
        {
            AddChoice(new OptionsChoice
            {
                Id = id,
                Label = label,
                Choices = new[] { "Off", "On" },
                Section = section,
                DefaultIndex = defaultValue.HasValue ? (defaultValue.Value ? 1 : 0) : -1,
                GetSaved = () => getSaved() ? 1 : 0,
                Save = i => save(i == 1),
                Preview = preview == null ? (Action<int>)null : i => preview(i == 1),
            });
        }

        /// <summary>
        /// Experimental. Adds a slider row. Can be called at any time; the row
        /// appears once the game's settings exist. Adding the same id again
        /// replaces its callbacks (a reloaded mod) and keeps its place.
        /// </summary>
        public static void AddSlider(OptionsSlider slider)
        {
            if (slider == null || string.IsNullOrEmpty(slider.Id) || string.IsNullOrEmpty(slider.Label) ||
                !(slider.Min < slider.Max) || slider.Step < 0f || slider.GetSaved == null || slider.Save == null ||
                float.IsNaN(slider.Min) || float.IsInfinity(slider.Min) || float.IsNaN(slider.Max) || float.IsInfinity(slider.Max))
            {
                throw new ArgumentException("OptionsSlider needs Id, Label, Min below Max, a Step of 0 or more, GetSaved and Save.", nameof(slider));
            }
            Options.OptionsRows.Add(slider);
        }

        /// <summary>
        /// Experimental. Adds a slider row from <paramref name="min"/> to
        /// <paramref name="max"/> in steps of <paramref name="step"/> (0 for
        /// continuous). <paramref name="save"/> receives the saved value and
        /// <paramref name="preview"/>, if given, the value being shown.
        /// </summary>
        /// <example>
        /// <code>
        /// GameOptions.AddSlider("com.example.mymod.speed", "Speed", 0.1f, 0.1f, 10f,
        ///     getSaved: () => _speed.Value, save: value => _speed.Value = value);
        /// </code>
        /// </example>
        public static void AddSlider(string id, string label, float step, float min, float max, Func<float> getSaved, Action<float> save,
            Action<float> preview = null, OptionsSection section = OptionsSection.Gameplay, float? defaultValue = null)
        {
            AddSlider(new OptionsSlider
            {
                Id = id,
                Label = label,
                Step = step,
                Min = min,
                Max = max,
                GetSaved = getSaved,
                Save = save,
                Preview = preview,
                Section = section,
                DefaultValue = defaultValue,
            });
        }

        /// <summary>
        /// Shows the saved value again after a mod changed it elsewhere (its own
        /// menu, a config file edit).
        /// </summary>
        public static void Refresh(string id)
        {
            Options.OptionsRows.Refresh(id);
        }
    }
}
