using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace DragNWash.ModFramework
{
    // The language the player sees the game in, kept in
    // BepInEx/CrashReports/locale.txt for CrashReporter.exe, whose window then
    // speaks it (when it has words for it) rather than Windows' language.
    //
    // A language a mod set comes first: the last "language" note, which
    // GameFonts.SetLanguage leaves (the localization mod calls it). Then the
    // game's own choice in Unity Localization, but only when the game offers
    // more than one language: with English alone there is no choice, and the
    // window keeps to Windows' language. "-" while neither is known, so a file
    // from an earlier session never speaks for this one.
    internal static class CrashReportLocale
    {
        private static readonly object Gate = new object();
        private static string _mod;
        private static string _game;
        private static string _written;
        private static bool _watching;
        private static bool _gaveUp;
        private static float _nextCheck;

        // From CrashReports.Install once recording started: clear what an
        // earlier session left.
        internal static void Start() => Save();

        // From CrashReports.Note, any thread: a mod said which language it shows.
        internal static void ModLanguage(string language)
        {
            _mod = string.IsNullOrEmpty(language) || language == "-" ? null : language;
            Save();
        }

        // From CrashReports.Tick: look at Unity Localization every few seconds
        // until it has finished starting up, then follow its changes.
        internal static void Tick()
        {
            if (_watching || _gaveUp || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 2f;
            try
            {
                Watch();
            }
            catch (Exception ex)
            {
                // Another version of Unity Localization, or none: the mods' notes still count.
                _gaveUp = true;
                CrashReports.Write("reporter", $"game language not followed: {ex.GetType().Name}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Watch()
        {
            if (!LocalizationSettings.HasSettings)
            {
                _gaveUp = true;
                return;
            }
            // Asking for the selected locale before Unity Localization has started
            // would wait for it on this frame. The handle's type is in
            // Unity.ResourceManager, which the core does not reference.
            object start = typeof(LocalizationSettings).GetProperty("InitializationOperation")?.GetValue(null, null);
            if (!(start?.GetType().GetProperty("IsDone")?.GetValue(start, null) is bool done) || !done) return;
            _watching = true;
            LocalizationSettings.SelectedLocaleChanged += GameLanguage;
            GameLanguage(LocalizationSettings.SelectedLocale);
        }

        private static void GameLanguage(Locale locale)
        {
            try
            {
                int offered = LocalizationSettings.AvailableLocales?.Locales?.Count ?? 0;
                string code = locale != null ? locale.Identifier.Code : null;
                _game = offered > 1 && !string.IsNullOrEmpty(code) ? code : null;
                Save();
            }
            catch
            {
                // A diagnostic must never break the game.
            }
        }

        private static void Save()
        {
            if (!CrashReports.Recording) return;
            string language = _mod ?? _game ?? "-";
            lock (Gate)
            {
                if (language == _written) return;
                try
                {
                    File.WriteAllText(Path.Combine(CrashReports.Folder, Diagnostics.CrashReportWriter.LocaleFile), language + Environment.NewLine, new UTF8Encoding(false));
                    _written = language;
                }
                catch
                {
                }
            }
        }
    }
}
