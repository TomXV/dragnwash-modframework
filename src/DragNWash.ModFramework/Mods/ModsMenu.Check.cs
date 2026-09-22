using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // Reading every plugin DLL with Cecil and walking Harmony's patches took
    // long enough to stop the game for a moment when the screen opened. The
    // loaded mods are known at once (BepInEx has them), so they are shown
    // straight away; the files and the patch conflicts are read on a worker
    // thread, with a "Checking mods..." row until they are in. Nothing on that
    // thread touches a Unity object: it reads files, Harmony's lists and
    // reflection, and hands back plain data this side turns into the list.
    internal sealed partial class ModsMenu
    {
        internal const string TextChecking = "Checking mods...";

        private static readonly Color CheckingColor = new Color(1f, 1f, 1f, 0.75f);
        private const string SpinnerFrames = "|/-\\";

        private sealed class Check
        {
            internal readonly ManualResetEvent Finished = new ManualResetEvent(false);
            internal ModCatalog.FileScan Scan;
            internal List<PatchConflicts.Conflict> Conflicts;
            internal Exception Failure;
        }

        private Check _check;
        private readonly List<TMP_Text> _spinners = new List<TMP_Text>();

        // Until the check is in, what other mods need is not known for sure:
        // switching a mod off or uninstalling it waits for it.
        private bool Checking => _check != null;

        // Starts reading the files and the patches for the list in _entries.
        // A check that is already quick (the DLLs were read before and have not
        // changed) is waited for a moment, so reopening the screen does not
        // flash the "Checking" row for one frame.
        private void StartCheck()
        {
            PatchConflicts.Owners owners = PatchConflicts.OwnersOf(_entries);
            var check = new Check();
            _check = check;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    check.Scan = ModCatalog.ScanFiles();
                    check.Conflicts = PatchConflicts.Find(owners);
                }
                catch (Exception ex)
                {
                    check.Failure = ex;
                }
                finally
                {
                    check.Finished.Set();
                }
            });
            if (check.Finished.WaitOne(20))
            {
                FinishCheck(check, false);
            }
        }

        // Every frame while the screen is open (UpdateResultWatcher): turns the
        // spinner, and takes the result once the worker has it.
        internal void PollCheck()
        {
            Check check = _check;
            if (check == null)
            {
                return;
            }
            if (!check.Finished.WaitOne(0))
            {
                Spin();
                return;
            }
            FinishCheck(check, true);
        }

        private void FinishCheck(Check check, bool rebuild)
        {
            if (!ReferenceEquals(check, _check))
            {
                return;
            }
            _check = null;
            check.Finished.Close();
            List<ModCatalog.Entry> entries = _entries;
            Exception failure = check.Failure;
            if (failure == null)
            {
                try
                {
                    entries = ModCatalog.Build(check.Scan);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }
            if (failure != null)
            {
                // The loaded mods are still right; only what the files add is missing.
                ModFramework.Log.LogError($"Could not check the mods' files: {failure}");
            }
            _conflicts = check.Conflicts ?? new List<PatchConflicts.Conflict>();

            // The new list has new entries; keep pointing at the same mods.
            _selected = entries.FirstOrDefault(e => SameMod(e, _selected)) ?? entries.FirstOrDefault();
            _settingsFor = _settingsFor == null ? null : entries.FirstOrDefault(e => SameMod(e, _settingsFor)) ?? _settingsFor;
            _pageFor = _pageFor == null ? null : entries.FirstOrDefault(e => SameMod(e, _pageFor)) ?? _pageFor;
            _entries = entries;
            if (rebuild)
            {
                RebuildKeepingFocus();
            }
        }

        // Drops a check still running when the screen closes; its result would
        // describe a list that is built again at the next opening anyway.
        private void DropCheck()
        {
            _check = null;
            _spinners.Clear();
        }

        // A plain ASCII spinner beside the "Checking" text, about eight steps a second.
        private void Spin()
        {
            _spinners.RemoveAll(s => s == null);
            if (_spinners.Count == 0)
            {
                return;
            }
            string frame = SpinnerFrames[(int)(Time.unscaledTime * 8f) % SpinnerFrames.Length].ToString();
            foreach (TMP_Text spinner in _spinners)
            {
                if (spinner.text != frame)
                {
                    spinner.text = frame;
                }
            }
        }

        // The last row of the list while the check runs: not a mod, and not
        // selectable, so the pad passes over it.
        private GameObject CreateCheckingRow()
        {
            var row = new GameObject("Checking", typeof(RectTransform));
            row.transform.SetParent(Content, false);
            LayoutElement layout = row.AddComponent<LayoutElement>();
            layout.minHeight = RowHeight;
            layout.preferredHeight = RowHeight;
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, RowHeight);
            layout.flexibleWidth = 1f;

            TMP_Text label = UiText.Create(row.transform, "Label", TextChecking, UiText.BodySize);
            CheckingStyle(label);
            var rect = (RectTransform)label.transform;
            rect.offsetMin = new Vector2(ListLeftMargin, 0f);
            AddSpinner(row.transform, label, ListLeftMargin, UiText.BodySize);
            return row;
        }

        private static void CheckingStyle(TMP_Text label)
        {
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.fontStyle |= FontStyles.Italic;
            label.color = CheckingColor;
        }

        // The spinner is its own label, after the text's measured width, so the
        // text stays one translatable phrase.
        private TMP_Text AddSpinner(Transform parent, TMP_Text after, float left, float size)
        {
            after.enableAutoSizing = false;
            after.fontSize = size;
            float width = after.GetPreferredValues(after.text).x;
            TMP_Text spinner = UiText.Create(parent, "Spinner", SpinnerFrames.Substring(0, 1), size);
            spinner.enableAutoSizing = false;
            spinner.fontSize = size;
            spinner.alignment = TextAlignmentOptions.MidlineLeft;
            spinner.textWrappingMode = TextWrappingModes.NoWrap;
            spinner.color = CheckingColor;
            var rect = (RectTransform)spinner.transform;
            RectTransform afterRect = (RectTransform)after.transform;
            rect.anchorMin = afterRect.anchorMin;
            rect.anchorMax = afterRect.anchorMax;
            rect.offsetMin = new Vector2(left + width + 12f, afterRect.offsetMin.y);
            rect.offsetMax = afterRect.offsetMax;
            _spinners.Add(spinner);
            return spinner;
        }

        // A button that cannot be pressed until the check is in: dimmed, and
        // skipped by pad and keyboard navigation.
        private static void Hold(GameObject button)
        {
            Selectable selectable = button.GetComponent<Selectable>();
            if (selectable != null)
            {
                // The group below does the dimming; the button's own disabled
                // tint on top of it would all but hide the colour.
                ColorBlock colors = selectable.colors;
                colors.disabledColor = colors.normalColor;
                selectable.colors = colors;
                selectable.interactable = false;
            }
            CanvasGroup group = button.AddComponent<CanvasGroup>();
            group.alpha = 0.45f;
            group.interactable = false;
        }

        // The list and the details built again with what is now known, keeping
        // what the pad or keyboard had selected. Settings and pages are left
        // alone until the player comes back to the list.
        private void RebuildKeepingFocus()
        {
            if (!isActiveAndEnabled || _page != null || _settingsFor != null)
            {
                return;
            }
            GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            ModCatalog.Entry focusedRow = focused != null ? focused.GetComponent<ModRowSelect>()?.Entry : null;
            string focusName = focused != null && _detailParts.Contains(focused) ? focused.name : null;
            RebuildList();
            RebuildDetails(false);
            if (focusedRow != null && EventSystem.current != null)
            {
                ModRowSelect row = _rows
                    .Where(r => r != null)
                    .Select(r => r.GetComponentInChildren<ModRowSelect>())
                    .FirstOrDefault(s => s != null && SameMod(s.Entry, focusedRow));
                if (row != null)
                {
                    EventSystem.current.SetSelectedGameObject(row.gameObject);
                }
            }
            else if (focusName != null)
            {
                Focus(focusName);
            }
        }
    }
}
