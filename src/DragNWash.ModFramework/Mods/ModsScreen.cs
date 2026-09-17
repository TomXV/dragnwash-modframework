using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // Puts a Mods button into the game's Options screen and builds the Mods
    // screen next to it.
    //
    // How the game's menus work (build 9/12/2026_a93aa21a): every screen is a
    // Menu registered with MenuManager under its GameObject name. A button
    // raises an intent named after its GameObject (MenuWithButtons), and the
    // shown menu answers with a transition to another menu by name. So:
    //   - the Mods button is a copy of Options' Back button named "Mods",
    //     painted with the framework's own Mods button (ModsButton0.png and
    //     ModsButton1.png, drawn for the framework by Mister ERIO), or, when
    //     those cannot be used, with the painted label hidden and a
    //     TextMeshPro label instead,
    //   - MenuOptions.OnEvent turns the "Mods" intent into a transition to
    //     "Menu_Mods",
    //   - Menu_Mods is a copy of the Options screen with the settings rows,
    //     Save and Reset removed and a ModsMenu component in place of
    //     MenuOptions, so it keeps the game's panel, scroll view, Back button,
    //     cursor handling and pad input.
    // Options exists in the title scene and in every level, so this runs the
    // first time Options is shown in each scene.
    internal static class ModsScreen
    {
        internal const string ButtonName = "Mods";
        internal const string MenuName = "Menu_Mods";

        private static readonly FieldInfo MenuContainerField = AccessTools.Field(typeof(Menu), "container");
        private static readonly FieldInfo MenuIntentsField = AccessTools.Field(typeof(Menu), "intents");
        private static readonly FieldInfo IntentListField = AccessTools.Field(typeof(MenuIntents), "intents");
        private static bool _available;

        // The Mods button's artwork: normal and selected, the same 420x160 at
        // 100 pixels per unit as the game's menu buttons.
        private static Sprite _buttonNormal;
        private static Sprite _buttonSelected;

        internal static void Install(Harmony harmony)
        {
            // Loaded here, from the plugin's Awake: a texture made later can
            // crash Direct3D 12.
            LoadButtonArt();
            try
            {
                MethodInfo onShow = AccessTools.Method(typeof(MenuOptions), "OnShow");
                MethodInfo onEvent = AccessTools.Method(typeof(MenuOptions), nameof(MenuOptions.OnEvent));
                if (onShow == null || onEvent == null || MenuContainerField == null || MenuIntentsField == null || IntentListField == null)
                {
                    ModFramework.Log.LogWarning("The game's menus have changed shape; the Mods screen is unavailable on this build.");
                    return;
                }
                harmony.Patch(onShow, postfix: new HarmonyMethod(typeof(ModsScreen), nameof(AfterOptionsShown)));
                harmony.Patch(onEvent, prefix: new HarmonyMethod(typeof(ModsScreen), nameof(BeforeOptionsEvent)));
                _available = true;
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not add the Mods screen: {ex}");
            }
        }

        private static bool BeforeOptionsEvent(MenuOptions __instance, MenuEvent e, ref MenuResponse __result)
        {
            if (!_available || !(e is MenuEventUserIntent intent) || intent.name != ButtonName)
            {
                return true;
            }

            // Never hand MenuManager a menu that does not exist: it throws. If
            // building the screen failed earlier, try once more now.
            GameObject options = ((Component)__instance).gameObject;
            Transform parent = options.transform.parent;
            if (parent != null && parent.Find(MenuName) == null)
            {
                try
                {
                    BuildModsMenu(__instance, options);
                }
                catch (Exception ex)
                {
                    ModFramework.Log.LogWarning($"Could not build the Mods screen: {ex}");
                }
            }
            __result = parent != null && parent.Find(MenuName) != null
                ? new MenuResponseTransition(MenuName, "Player opened the Mods screen.")
                : (MenuResponse)new MenuResponseIgnored();
            return false;
        }

        private static void AfterOptionsShown(MenuOptions __instance)
        {
            try
            {
                GameObject options = ((Component)__instance).gameObject;
                Transform leftButtons = options.transform.Find("Container/Panel/LeftButtons");
                Transform back = leftButtons != null ? leftButtons.Find("Back") : null;
                if (back == null)
                {
                    return;
                }

                Transform parent = options.transform.parent;
                if (parent != null && parent.Find(MenuName) == null)
                {
                    BuildModsMenu(__instance, options);
                }

                if (leftButtons.Find(ButtonName) == null)
                {
                    AddModsButton(options, leftButtons, back);
                }

                // The game hides Save whenever Options is shown, even with unsaved
                // changes still pending; coming back from the Mods screen would
                // leave no way to save them.
                if (SaveButtonOnlyAppearOnChanges.hasChanges)
                {
                    Transform save = leftButtons.Find("Save");
                    if (save != null && save.childCount > 0)
                    {
                        save.GetChild(0).gameObject.SetActive(true);
                    }
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not set up the Mods screen: {ex}");
            }
        }

        private static void AddModsButton(GameObject options, Transform leftButtons, Transform back)
        {
            GameObject copy = UnityEngine.Object.Instantiate(back.gameObject, leftButtons, false);
            copy.name = ButtonName;
            // Above Save: the game keeps Save's slot even while Save is hidden,
            // and the Mods button should not float below an empty gap.
            Transform save = leftButtons.Find("Save");
            copy.transform.SetSiblingIndex(save != null ? save.GetSiblingIndex() : leftButtons.childCount - 1);
            Transform inner = copy.transform.childCount > 0 ? copy.transform.GetChild(0) : copy.transform;
            inner.name = ButtonName;

            // Selecting Back first stays the game's job.
            RemoveComponent(inner.gameObject, "SelectOnEnable");

            Button button = inner.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
            }

            // The painted "Back" has to go: the Mods artwork takes its place,
            // or, without it, the graphics are disabled (not the objects, which
            // keeps the button's animator happy; the click area is PointTarget,
            // which stays) and a text label is laid over.
            Transform images = inner.Find("Images");
            bool painted = images != null && PaintButton(images.GetComponentsInChildren<Image>(true));
            if (!painted)
            {
                if (images != null)
                {
                    foreach (Image image in images.GetComponentsInChildren<Image>(true))
                    {
                        image.enabled = false;
                    }
                }
                TMP_Text label = UiText.Create(inner, "Label", ButtonName, UiText.ButtonSize);
                label.alignment = TextAlignmentOptions.Center;
            }

            // MenuWithButtons wires its buttons when enabled; run that again so
            // the new button raises the "Mods" intent like the game's own.
            RewireButtons(options);
            ModFramework.Log.LogInfo("Added the Mods button to the Options screen.");
        }

        private static void BuildModsMenu(MenuOptions source, GameObject options)
        {
            // Instantiated under an inactive holder, the copy does not run Awake
            // or OnEnable until it has been changed into the Mods screen.
            var holder = new GameObject("ModFramework.MenuHolder");
            holder.SetActive(false);
            try
            {
                GameObject copy = UnityEngine.Object.Instantiate(options, holder.transform, false);
                copy.name = MenuName;

                MenuOptions copiedOptions = copy.GetComponent<MenuOptions>();
                object container = MenuContainerField.GetValue(copiedOptions);
                var intents = new MenuIntents();
                object sourceIntents = MenuIntentsField.GetValue(source);
                if (sourceIntents != null)
                {
                    IntentListField.SetValue(intents, IntentListField.GetValue(sourceIntents));
                }

                UnityEngine.Object.DestroyImmediate(copiedOptions);
                foreach (string typeName in new[] { "OptionsBackButton", "SaveButtonOnlyAppearOnChanges", "UnityScriptableSettings.ScriptableSettingSpawner", "ResetToDefaults" })
                {
                    RemoveComponentsInChildren(copy, typeName);
                }

                Transform panel = copy.transform.Find("Container/Panel");
                Transform leftButtons = panel.Find("LeftButtons");
                foreach (string name in new[] { "ResetToDefaults", "Save", ButtonName })
                {
                    Transform t = leftButtons.Find(name);
                    if (t != null)
                    {
                        UnityEngine.Object.DestroyImmediate(t.gameObject);
                    }
                }

                Transform content = panel.Find("Scroll View/Viewport/Content");
                for (int i = content.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
                }

                // The OPTIONS plaque is painted; show the title as text instead.
                Transform title = panel.Find("TitleImage");
                if (title != null)
                {
                    Image plaque = title.GetComponent<Image>();
                    if (plaque != null)
                    {
                        plaque.enabled = false;
                    }
                    UiText.Create(title, "Title", ButtonName, UiText.TitleSize).alignment = TextAlignmentOptions.Center;
                }

                ModsMenu menu = copy.AddComponent<ModsMenu>();
                MenuContainerField.SetValue(menu, container);
                MenuIntentsField.SetValue(menu, intents);
                menu.Content = (RectTransform)content;
                menu.Details = SplitForDetails(panel);
                menu.Details.gameObject.AddComponent<DetailsResizeWatcher>().Menu = menu;
                menu.gameObject.AddComponent<PadSupport>().Menu = menu;
                menu.gameObject.AddComponent<UpdateResultWatcher>().Menu = menu;

                int index = options.transform.GetSiblingIndex();
                copy.transform.SetParent(options.transform.parent, false);
                copy.transform.SetSiblingIndex(index + 1);
                ModFramework.Log.LogInfo("Built the Mods screen.");
            }
            finally
            {
                UnityEngine.Object.Destroy(holder);
            }
        }

        // The Options scroll view spans the panel right of the buttons. Keep its
        // left part for the list of mods and put the details panel beside it.
        // Both go into a holder that takes the scroll view's place, and are
        // anchored by fractions of it, so they keep their shares when the window
        // is resized (fixed offsets measured at build time did not).
        private static RectTransform SplitForDetails(Transform panel)
        {
            var scroll = (RectTransform)panel.Find("Scroll View");
            Transform horizontal = scroll.Find("Scrollbar Horizontal");
            if (horizontal != null)
            {
                horizontal.gameObject.SetActive(false);
            }

            var split = (RectTransform)new GameObject("Split", typeof(RectTransform)).transform;
            split.SetParent(panel, false);
            split.SetSiblingIndex(scroll.GetSiblingIndex());
            split.anchorMin = scroll.anchorMin;
            split.anchorMax = scroll.anchorMax;
            split.pivot = scroll.pivot;
            split.anchoredPosition = scroll.anchoredPosition;
            split.sizeDelta = scroll.sizeDelta;

            scroll.SetParent(split, false);
            Fill(scroll, 0f, 0.45f);

            var details = (RectTransform)new GameObject("Details", typeof(RectTransform)).transform;
            details.SetParent(split, false);
            Fill(details, 0.47f, 1f);
            return details;
        }

        private static void Fill(RectTransform rect, float left, float right)
        {
            rect.anchorMin = new Vector2(left, 0f);
            rect.anchorMax = new Vector2(right, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void LoadButtonArt()
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(ModsScreen).Assembly.Location) ?? "";
                _buttonNormal = SpriteOf(IconLoader.Load(Path.Combine(dir, "ModsButton0.png")));
                _buttonSelected = SpriteOf(IconLoader.Load(Path.Combine(dir, "ModsButton1.png")));
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"The Mods button artwork could not be loaded; the button gets a text label: {ex.Message}");
            }
        }

        private static Sprite SpriteOf(Texture2D texture)
        {
            if (texture == null)
            {
                return null;
            }
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        // The game draws each menu button as two sprites, MenuButtonsNNNN with
        // an odd number for the normal look and the next even number for the
        // selected one (Back is 0011 and 0012). Each image of the copied Back
        // button gets the Mods artwork of the same state. Nothing is changed
        // unless every image can be told apart that way.
        private static bool PaintButton(Image[] images)
        {
            if (_buttonNormal == null || _buttonSelected == null || images.Length == 0)
            {
                return false;
            }
            var chosen = new Sprite[images.Length];
            for (int i = 0; i < images.Length; i++)
            {
                string name = images[i].sprite != null ? images[i].sprite.name : "";
                int digits = 0;
                while (digits < name.Length && char.IsDigit(name[name.Length - 1 - digits]))
                {
                    digits++;
                }
                if (digits == 0 || !int.TryParse(name.Substring(name.Length - digits), out int number))
                {
                    ModFramework.Log.LogInfo($"The Back button's image \"{images[i].name}\" shows \"{name}\"; the Mods button gets a text label instead.");
                    return false;
                }
                chosen[i] = number % 2 == 1 ? _buttonNormal : _buttonSelected;
            }
            for (int i = 0; i < images.Length; i++)
            {
                images[i].sprite = chosen[i];
            }
            ModFramework.Log.LogInfo("The Mods button uses its artwork (by Mister ERIO).");
            return true;
        }

        private static void RewireButtons(GameObject menu)
        {
            MenuWithButtons wiring = menu.GetComponent<MenuWithButtons>();
            if (wiring == null || !wiring.isActiveAndEnabled)
            {
                return;
            }
            AccessTools.Method(typeof(MenuWithButtons), "OnDisable")?.Invoke(wiring, null);
            AccessTools.Method(typeof(MenuWithButtons), "OnEnable")?.Invoke(wiring, null);
        }

        private static void RemoveComponent(GameObject go, string typeName)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                return;
            }
            Component c = go.GetComponent(type);
            if (c != null)
            {
                UnityEngine.Object.DestroyImmediate(c);
            }
        }

        private static void RemoveComponentsInChildren(GameObject root, string typeName)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                return;
            }
            foreach (Component c in root.GetComponentsInChildren(type, true))
            {
                UnityEngine.Object.DestroyImmediate(c);
            }
        }
    }
}
