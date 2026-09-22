using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // Pages other mods add to their entry on the Mods screen (ModsScreenPage).
    // The details panel is handed over to the page; Back returns to the list.
    internal sealed partial class ModsMenu
    {
        internal const string TextPageFailed = "This page could not be shown. See BepInEx/LogOutput.log.";
        internal const string TextTryAgain = "Try again";

        private ModsScreenPage _page;
        private ModCatalog.Entry _pageFor;

        private void OpenPage(ModCatalog.Entry entry, ModsScreenPage page)
        {
            _page = page;
            _pageFor = entry;
            RebuildDetails(false);
        }

        private void ClosePage()
        {
            _page = null;
            _selected = _pageFor ?? _selected;
            _pageFor = null;
            RebuildDetails(false);
            Focus("Page0", "Page1", "Settings", "Switch");
        }

        private void BuildPage()
        {
            GameObject band = Part("Band", 0f, 1f, 0f, 1f);
            Image bandImage = band.AddComponent<Image>();
            bandImage.color = BandColor;
            bandImage.raycastTarget = false;

            Label("Mod", Escape(_pageFor?.DisplayName), UiText.BodySize, 0.91f, 0.98f, false);
            Label("Title", _page.Title, UiText.TitleSize * 0.55f, 0.81f, 0.91f, false);

            GameObject root = Part("PageContent", 0f, 1f, 0f, 0.8f);
            var rect = (RectTransform)root.transform;
            rect.offsetMin = new Vector2(28f, 20f);
            rect.offsetMax = new Vector2(-28f, 0f);
            try
            {
                _page.Build(rect);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"The page \"{_page.Title}\" of {_page.Guid} threw while building: {ex}");
                // Whatever the page built before it threw is hidden, so half a
                // page does not sit there looking like the whole of it. Some of
                // what a page shows can be missing for a moment only, so one
                // press builds it again rather than a restart.
                root.SetActive(false);
                TMP_Text failed = Label("PageFailed", TextPageFailed, UiText.BodySize, 0.5f, 0.76f, true);
                failed.alignment = TextAlignmentOptions.TopLeft;
                failed.color = WarnColor;
                MakeButton("TryAgain", TextTryAgain, 0.04f, 0.4f, 0.04f, 0.16f, StepColor, () =>
                {
                    RebuildDetails(false);
                    Focus("TryAgain");
                });
                Focus("TryAgain");
            }
        }
    }
}
