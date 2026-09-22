using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Member = DragNWash.ModFramework.Inspector.InspectorModel.Member;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's members pane: the selected object's component strip,
    // then the selected component's (or material's) members, one row each -
    // reading, editing (plain fields, composites, drag-to-change numbers), the
    // row menu's Reset/Apply and the draft/frozen-value bookkeeping behind it.
    internal static partial class InspectorTab
    {
        // Members pane.
        private static bool _showPrivate;
        private static bool _freeze;
        private static readonly Dictionary<string, string> Frozen = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> Drafts = new Dictionary<string, string>();
        // What each draft started from: leaving a field applies it only when it was typed in,
        // never a stale copy of a value the game moved on from meanwhile.
        private static readonly Dictionary<string, string> DraftStart = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> RowErrors = new Dictionary<string, string>();
        // Press and hold on a number field, then move up or down: the value
        // changes with the mouse (Unity's own inspector does this on the label).
        private static string _pressKey;
        private static Vector2 _pressMouse;
        private static float _pressValue;
        private static bool _numberDragging;
        private static object _dragBefore;
        private static RowInfo _dragRow;

        // A Transform shows its position, rotation and scale first; the rest waits behind a toggle.
        private static bool _allMembers;
        // The Code view (type, methods, patches, listeners, IL) in place of the members.
        private static bool _showCode;
        private static readonly HashSet<string> ExpandedLists = new HashSet<string>();
        private static readonly List<RowInfo> Rows = new List<RowInfo>();
        // The selected object's components (and its renderers' materials) as a
        // strip of buttons above the members; the selected one is highlighted.
        // Returns the height used.
        private static float DrawComponentStrip(Rect pane, GameObject owner, ToolWindowStyles s, float row)
        {
            if (ReferenceEquals(owner, null) || !owner)
            {
                GUI.Label(new Rect(pane.x + 8, pane.y + 4, pane.width - 16, row), "Select an object: Pick one in the game, or open the tree.", s.MutedLabel);
                return row + 4;
            }
            var entries = new List<KeyValuePair<string, object>>();
            entries.Add(new KeyValuePair<string, object>("GameObject", owner));
            foreach (Component c in owner.GetComponents<Component>())
            {
                entries.Add(new KeyValuePair<string, object>(c == null ? "(missing script)" : c.GetType().Name, c));
            }
            foreach (Renderer r in owner.GetComponents<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null)
                    {
                        entries.Add(new KeyValuePair<string, object>("Material: " + m.name, m));
                    }
                }
            }
            float x = pane.x + 4, y = pane.y + 4, w = pane.width - 8;
            float bx = x;
            float lineH = row - 4;
            foreach (KeyValuePair<string, object> e in entries)
            {
                bool selected = ReferenceEquals(e.Value, _target);
                bool enabled = !(e.Value is Behaviour b) || b.enabled;
                string label = Drawable(e.Key);
                float bw = Mathf.Min(w, s.Button.CalcSize(new GUIContent(label)).x + 14);
                if (bx + bw > x + w && bx > x)
                {
                    bx = x;
                    y += lineH + 4;
                }
                GUIStyle style = selected ? s.SelectedButton : s.Button;
                if (GUI.Button(new Rect(bx, y, bw, lineH), label, style) && e.Value != null)
                {
                    SetTarget(e.Value);
                    _page = 1;
                }
                if (!enabled && !selected)
                {
                    TW.Fill(new Rect(bx, y + lineH - 2, bw, 2), TW.MutedColor);
                }
                bx += bw + 4;
            }
            y += lineH + 4;
            GUI.Label(new Rect(x, y, w, row), $"{(owner.activeInHierarchy ? "active" : "inactive")}   tag {owner.tag}   layer {LayerMask.LayerToName(owner.layer)}", _mutedCell);
            y += row;
            return y - pane.y;
        }

        // The selected component's or material's members, one row each.
        private static void DrawMembers(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            float used = _objectsMode ? DrawObjectHeader(pane, s, row) : DrawComponentStrip(pane, SelectedObject, s, row);
            if (_target == null)
            {
                return;
            }
            float x = pane.x + 4, y = pane.y + used + 2, w = pane.width - 8;
            bool readOnly = InspectorObjects.IsOutsideScenes(_target);
            // Header: what is selected, the toggles.
            string heading = _target is Material mat
                ? $"{mat.name}  ({mat.shader?.name})"
                : _target is GameObject go ? go.name + "  (GameObject)"
                : _target is UnityEngine.Object listed && InspectorObjects.IsListed(listed) ? $"{(string.IsNullOrEmpty(listed.name) ? "(no name)" : listed.name)}  ({listed.GetType().Name})"
                : $"{_target.GetType().Name}";
            GUI.Label(new Rect(x, y, w, row), Drawable(heading), _cell);
            y += row;
            if (_target is Material m2)
            {
                if (_rendererUsers < 0 && !_objectsMode)
                {
                    _rendererUsers = InspectorModel.RendererCount(m2);
                }
                if (!_objectsMode) GUI.Label(new Rect(x, y, w, row), $"Shared by {_rendererUsers} renderer(s); a change shows on all of them.", _mutedCell);
                if (!_objectsMode) y += row;
            }
            else if (!(_target is GameObject))
            {
                float bx = x;
                if (GUI.Button(new Rect(bx, y, 120, row), "Show private", _showPrivate ? s.SelectedButton : s.Button))
                {
                    _showPrivate = !_showPrivate;
                    if (_showPrivate) _status = "Private members: setting them is the mod author's own risk.";
                }
                bx += 128;
                if (GUI.Button(new Rect(bx, y, 80, row), "Freeze", _freeze ? s.SelectedButton : s.Button))
                {
                    _freeze = !_freeze;
                    Frozen.Clear();
                }
                bx += 88;
                if (_target is Behaviour beh && !readOnly)
                {
                    if (GUI.Button(new Rect(bx, y, 90, row), beh.enabled ? "Enabled" : "Disabled", beh.enabled ? s.SelectedButton : s.Button))
                    {
                        beh.enabled = !beh.enabled;
                    }
                    bx += 98;
                }
                if (GUI.Button(new Rect(bx, y, 70, row), "Code", _showCode ? s.SelectedButton : s.Button))
                {
                    _showCode = !_showCode;
                }
                y += row + 4;
            }
            if (_showCode && !(_target is Material) && !(_target is GameObject))
            {
                InspectorCode.Draw(new Rect(x, y, w, pane.yMax - y - 2), _target, s, row);
                return;
            }

            if (_target is Component body && InspectorBodies.IsBody(body) && !readOnly)
            {
                y = DrawBodyControls(body, x, y, w, s, row);
            }
            if (_target is Component animator && InspectorAnimators.IsAnimator(animator) && !readOnly)
            {
                y = DrawAnimatorControls(animator, x, y, w, s, row);
            }

            // The rows. Values are read on Repaint only, and kept while frozen.
            List<Member> members = MembersOfTarget();
            bool compactTransform = _target is Transform && !_allMembers;
            if (_target is Transform)
            {
                if (GUI.Button(new Rect(x, y, 130, row), _allMembers ? "Fewer members" : "All members", _allMembers ? s.SelectedButton : s.Button))
                {
                    _allMembers = !_allMembers;
                }
                y += row + 4;
            }
            Rows.Clear();
            foreach (Member m in members)
            {
                if (m.IsPrivate && !_showPrivate)
                {
                    continue;
                }
                if (compactTransform && Array.IndexOf(CompactTransformMembers, m.Name) < 0)
                {
                    continue;
                }
                object tgt = _target;
                Rows.Add(new RowInfo { Member = m, Key = ControlPrefix + m.Name, Getter = () => m.Get(tgt), Setter = m.CanWrite && !readOnly ? (Action<object>)(v => m.Set(tgt, v)) : null, Label = m.Name, Type = m.Type });
                if (InspectorModel.IsList(m.Type) && ExpandedLists.Contains(m.Name))
                {
                    object listValue = SafeGet(m, () => m.Get(tgt));
                    if (listValue is IList list)
                    {
                        Type element = m.Type.IsArray ? m.Type.GetElementType() : (m.Type.IsGenericType ? m.Type.GetGenericArguments()[0] : typeof(object));
                        int count = Mathf.Min(list.Count, 200);
                        for (int i = 0; i < count; i++)
                        {
                            int index = i;
                            Rows.Add(new RowInfo
                            {
                                Member = m, Key = ControlPrefix + m.Name + "[" + i + "]", Label = $"    [{i}]", Type = element,
                                Getter = () => list[index], Setter = list.IsReadOnly || readOnly ? null : (Action<object>)(v => list[index] = v),
                            });
                        }
                        if (list.Count > count)
                        {
                            Rows.Add(new RowInfo { Member = m, Key = ControlPrefix + m.Name + "[more]", Label = $"    ... {list.Count - count} more", Type = typeof(string), Getter = () => "" });
                        }
                    }
                }
            }
            // The colour picker sits at the bottom of the pane while it is open.
            float pickerHeight = InspectorColorPicker.Open ? InspectorColorPicker.Height + 4 : 0;
            var view = new Rect(x, y, w, pane.yMax - y - 2 - pickerHeight);
            float inner = view.width - 20;
            float nameWidth = Mathf.Clamp(inner * 0.34f, 110, 300);
            float total = 0;
            foreach (RowInfo r in Rows)
            {
                total += RowHeight(r, inner, nameWidth, row);
            }
            TW.ApplyScroll(view, ref _scrollMembers);
            _scrollMembers = GUI.BeginScrollView(view, _scrollMembers, new Rect(0, 0, inner, Mathf.Max(view.height, total)), false, false);
            float ry = 0;
            foreach (RowInfo r in Rows)
            {
                float h = RowHeight(r, inner, nameWidth, row);
                if (ry + h >= _scrollMembers.y && ry <= _scrollMembers.y + view.height)
                {
                    DrawRow(r, new Rect(0, ry, inner, row), nameWidth, s, row);
                }
                ry += h;
            }
            GUI.EndScrollView();
            if (InspectorColorPicker.Open)
            {
                InspectorColorPicker.Draw(new Rect(x, view.yMax + 4, w, InspectorColorPicker.Height), s);
            }
        }

        // Fields narrower than this are unreadable; the composite then goes on
        // a second line across the whole width.
        private const float MinFieldWidth = 68f;

        private static bool Stacked(RowInfo r, float inner, float nameWidth)
        {
            if (!InspectorModel.IsComposite(r.Type) || r.Setter == null)
            {
                return false;
            }
            int n = InspectorModel.ComponentLabels(r.Type).Length;
            bool isColor = r.Type == typeof(Color) || r.Type == typeof(Color32);
            float extras = 8 + (isColor ? TW.RowHeight : 0) + (InspectorHistory.TryOriginal(_target, MemberId(r), out _) ? 60 : 0);
            return (inner - nameWidth - extras - n * 18) / n < MinFieldWidth;
        }

        private static float RowHeight(RowInfo r, float inner, float nameWidth, float row)
        {
            float h = Stacked(r, inner, nameWidth) ? row * 2 : row;
            return RowErrors.ContainsKey(r.Key) ? h + row : h;
        }

        private static readonly string[] CompactTransformMembers = { "localPosition", "localEulerAngles", "localScale", "position", "eulerAngles", "parent" };

        // Press-and-drag on a number field. Returns true when the value should
        // change to `next` this event. The press itself is not used, so a plain
        // click still focuses the field for typing; the drag takes over once the
        // pointer has moved four pixels up or down.
        private static bool DragNumber(RowInfo r, string key, Rect field, float current, out float next)
        {
            next = current;
            Event ev = Event.current;
            int control = GUIUtility.GetControlID(key.GetHashCode(), FocusType.Passive);
            if (ev.type == EventType.MouseDown && ev.button == 0 && field.Contains(ev.mousePosition))
            {
                _pressKey = key;
                _pressMouse = ev.mousePosition;
                _pressValue = current;
                _numberDragging = false;
                return false;
            }
            if (_pressKey != key)
            {
                return false;
            }
            if (ev.type == EventType.MouseDrag)
            {
                if (!_numberDragging)
                {
                    if (Mathf.Abs(ev.mousePosition.y - _pressMouse.y) < 4f)
                    {
                        return false;
                    }
                    _numberDragging = true;
                    _dragRow = r;
                    _dragBefore = SafeGet(r.Member, r.Getter);
                    GUIUtility.hotControl = control;
                    GUIUtility.keyboardControl = 0;
                    Drafts.Remove(key);
                }
                // Up increases. A pixel moves the value by a hundredth of its size, at least 0.01.
                float step = Mathf.Max(0.01f, Mathf.Abs(_pressValue) * 0.01f);
                next = _pressValue + (_pressMouse.y - ev.mousePosition.y) * step;
                ev.Use();
                return true;
            }
            if (ev.type == EventType.MouseUp)
            {
                if (_numberDragging)
                {
                    if (GUIUtility.hotControl == control)
                    {
                        GUIUtility.hotControl = 0;
                    }
                    object after = SafeGet(r.Member, r.Getter);
                    if (_dragBefore != null && after != null && !Equals(_dragBefore, after))
                    {
                        InspectorHistory.Record(_target, WhereLabel(), MemberId(r), _dragBefore, after, r.Getter, r.Setter);
                    }
                    ev.Use();
                }
                _pressKey = null;
                _numberDragging = false;
                _dragRow = null;
            }
            return false;
        }

        // Sets without a history entry: the drag records once, at its end.
        private static bool SetRaw(RowInfo r, object value)
        {
            try
            {
                r.Setter(value);
                RowErrors.Remove(r.Key);
                Frozen.Remove(r.Key);
                NoteSharedEdit();
                return true;
            }
            catch (Exception ex)
            {
                RowErrors[r.Key] = (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private sealed class RowInfo
        {
            public Member Member;
            public string Key;
            public string Label;
            public Type Type;
            public Func<object> Getter;
            public Action<object> Setter;
        }

        private static object SafeGet(Member m, Func<object> get)
        {
            try
            {
                object v = get();
                m.Failure = null;
                return v;
            }
            catch (Exception ex)
            {
                m.Failure = (ex.InnerException ?? ex).Message;
                return null;
            }
        }

        private static void DrawRow(RowInfo r, Rect rect, float nameWidth, ToolWindowStyles s, float row)
        {
            Event ev = Event.current;
            string label = r.Label + (r.Member.IsPrivate && r.Label == r.Member.Name ? "  (private)" : "");
            GUI.Label(new Rect(rect.x, rect.y, nameWidth - 6, row), Drawable(label), r.Member.IsPrivate ? _mutedCell : _cell);
            var valueRect = new Rect(rect.x + nameWidth, rect.y, rect.width - nameWidth, row);

            // The value: frozen text, or read now.
            string key = r.Key;
            object value;
            bool haveValue;
            bool frozen = _freeze && Frozen.ContainsKey(key);
            if (frozen)
            {
                value = null;
                haveValue = false;
            }
            else
            {
                value = SafeGet(r.Member, r.Getter);
                haveValue = r.Member.Failure == null;
                if (_freeze && haveValue)
                {
                    Frozen[key] = InspectorModel.Format(value);
                }
            }
            string shown = r.Member.Failure != null ? "error: " + r.Member.Failure : frozen ? Frozen[key] : InspectorModel.Format(value);
            Type t = r.Type;
            bool editable = r.Setter != null && haveValue && InspectorModel.IsEditableType(t);

            // Reset, when the row was edited: the value it held before the first edit.
            float right = valueRect.xMax;
            if (InspectorHistory.TryOriginal(_target, MemberId(r), out object original))
            {
                if (GUI.Button(new Rect(right - 54, valueRect.y + 2, 54, row - 4), "Reset", s.Button))
                {
                    // A picker open on this row records what it changed so far and
                    // stays open; the reset colour becomes its new baseline.
                    if (InspectorColorPicker.Open && InspectorColorPicker.Key == key)
                    {
                        InspectorColorPicker.CommitPending();
                    }
                    if (TrySet(r, original))
                    {
                        InspectorHistory.ForgetOriginal(_target, MemberId(r));
                        Drafts.Remove(key);
                    }
                }
                right -= 60;
            }
            var control = new Rect(valueRect.x, valueRect.y, right - valueRect.x, row);

            if (r.Member.Failure != null)
            {
                GUI.Label(control, Drawable(shown), _errorCell);
            }
            else if (frozen)
            {
                GUI.Label(control, Drawable(shown), _mutedCell);
            }
            else if (t == typeof(bool) && editable)
            {
                bool b = value is bool bb && bb;
                if (GUI.Button(new Rect(control.x, control.y + 2, 80, row - 4), b ? "true" : "false", b ? s.SelectedButton : s.Button))
                {
                    TrySet(r, !b);
                }
            }
            else if (t != null && t.IsEnum && editable)
            {
                if (GUI.Button(new Rect(control.x, control.y + 2, Mathf.Min(control.width, 220), row - 4), Drawable(shown) + "  >", s.Button))
                {
                    TrySet(r, InspectorModel.NextEnum(t, value));
                }
            }
            else if (editable && InspectorModel.IsComposite(t))
            {
                if (Stacked(r, rect.width, nameWidth))
                {
                    // Second line, full width, under the name.
                    DrawComposite(r, new Rect(rect.x + 12, rect.y + row, right - rect.x - 12, row), value, s, row);
                }
                else
                {
                    DrawComposite(r, control, value, s, row);
                }
            }
            else if (editable)
            {
                // A text field with a draft while it has focus; Enter or leaving the field applies.
                float fieldWidth = control.width;
                var fieldRect = new Rect(control.x, control.y, Mathf.Max(60, fieldWidth), row);
                if (InspectorModel.IsNumber(t) && value != null && DragNumber(r, key, fieldRect, Convert.ToSingle(value, CultureInfo.InvariantCulture), out float dragged))
                {
                    bool whole = t != typeof(float) && t != typeof(double) && t != typeof(decimal);
                    object v = InspectorModel.Parse(t, (whole ? Mathf.Round(dragged) : dragged).ToString(whole ? "0" : "0.####", CultureInfo.InvariantCulture), out string err);
                    if (err == null && SetRaw(r, v))
                    {
                        value = v;
                        shown = InspectorModel.Format(v);
                    }
                }
                string text = FieldText(key, shown, ev);
                GUI.SetNextControlName(key);
                string after = GUI.TextField(fieldRect, text ?? "", s.TextField);
                if (_focusedControl == key && after != text)
                {
                    Drafts[key] = after;
                }
                TW.Underline(fieldRect);
            }
            else if (value is UnityEngine.Object uo && uo)
            {
                GUI.Label(new Rect(control.x, control.y, control.width - (uo is Texture2D && AssetsTabAvailable ? 118 : 50), row), Drawable(shown), _mutedCell);
                // Go for every kind: a scene's object in Scene, anything else in
                // Objects. A texture also keeps Assets, the Assets tab on it.
                bool assets = uo is Texture2D && AssetsTabAvailable;
                if (assets && GUI.Button(new Rect(control.xMax - 112, control.y + 2, 64, row - 4), "Assets", s.Button))
                {
                    ShowInAssetsTab(uo);
                }
                if (GUI.Button(new Rect(control.xMax - 44, control.y + 2, 44, row - 4), "Go", s.Button))
                {
                    Select(uo);
                }
            }
            else if (t != null && InspectorModel.IsList(t) && haveValue && value != null && r.Label == r.Member.Name)
            {
                bool open = ExpandedLists.Contains(r.Member.Name);
                GUI.Label(new Rect(control.x, control.y, control.width - 80, row), Drawable(shown), _mutedCell);
                if (GUI.Button(new Rect(control.xMax - 74, control.y + 2, 74, row - 4), open ? "Collapse" : "Expand", s.Button))
                {
                    if (open) ExpandedLists.Remove(r.Member.Name); else ExpandedLists.Add(r.Member.Name);
                }
            }
            else
            {
                GUI.Label(control, Drawable(shown), _mutedCell);
            }

            if (RowErrors.TryGetValue(key, out string error))
            {
                float errorY = rect.y + (Stacked(r, rect.width, nameWidth) ? row * 2 : row);
                GUI.Label(new Rect(rect.x + nameWidth, errorY, rect.width - nameWidth, row), Drawable(error), _errorCell);
            }
            // Right click anywhere else on the row: the row's menu. A component
            // field's own right click was used above, so it is not overridden here.
            float rowHeight = Stacked(r, rect.width, nameWidth) ? row * 2 : row;
            if (ev.type == EventType.MouseDown && ev.button == 1 && new Rect(rect.x, rect.y, rect.width, rowHeight).Contains(ev.mousePosition))
            {
                _menuRow = r;
                _menuComponent = -1;
                _menuAt = GUIUtility.GUIToScreenPoint(ev.mousePosition) - _tabScreenOrigin;
                ev.Use();
            }
        }

        // One small field per component (x, y, z ...), and for colours a swatch
        // that opens the picker.
        private static void DrawComposite(RowInfo r, Rect control, object value, ToolWindowStyles s, float row)
        {
            Event ev = Event.current;
            Type t = r.Type;
            bool isColor = t == typeof(Color) || t == typeof(Color32);
            string[] labels = InspectorModel.ComponentLabels(t);
            float[] parts = InspectorModel.Components(value);
            float swatch = isColor ? row : 0;
            float setWidth = 0;
            float labelWidth = 14;
            float available = control.width - swatch - setWidth - 8 - labels.Length * (labelWidth + 4);
            float fieldWidth = Mathf.Max(36, available / labels.Length);
            float bx = control.x;
            for (int i = 0; i < labels.Length; i++)
            {
                string key = r.Key + "#" + i;
                GUI.Label(new Rect(bx, control.y, labelWidth, row), labels[i], _mutedCell);
                bx += labelWidth;
                var fieldRect = new Rect(bx, control.y, fieldWidth, row);
                if (ev.type == EventType.MouseDown && ev.button == 1 && fieldRect.Contains(ev.mousePosition))
                {
                    _menuRow = r;
                    _menuComponent = i;
                    _menuAt = GUIUtility.GUIToScreenPoint(ev.mousePosition) - _tabScreenOrigin;
                    ev.Use();
                }
                if (i < parts.Length && DragNumber(r, key, fieldRect, parts[i], out float dragged))
                {
                    parts[i] = dragged;
                    object composed = InspectorModel.Compose(t, parts);
                    if (composed != null && SetRaw(r, composed))
                    {
                        value = composed;
                    }
                }
                string shown = i < parts.Length ? Fmt(t, parts[i]) : "";
                string text = FieldText(key, shown, ev);
                GUI.SetNextControlName(key);
                string after = GUI.TextField(fieldRect, text ?? "", s.TextField);
                if (_focusedControl == key && after != text)
                {
                    Drafts[key] = after;
                }
                TW.Underline(fieldRect);
                bx += fieldWidth + 4;
            }
            if (isColor)
            {
                Color c = value is Color cc ? cc : value is Color32 c32 ? (Color)c32 : Color.clear;
                var swatchRect = new Rect(bx + 2, control.y + 4, row - 8, row - 8);
                TW.Fill(swatchRect, c);
                if (GUI.Button(swatchRect, "", s.MutedLabel))
                {
                    if (InspectorColorPicker.Open && InspectorColorPicker.Key == r.Key)
                    {
                        InspectorColorPicker.Close();
                    }
                    else
                    {
                        RowInfo row2 = r;
                        bool as32 = t == typeof(Color32);
                        InspectorColorPicker.Show(r.Key, c,
                            picked => SetRaw(row2, as32 ? (object)(Color32)picked : picked),
                            (before, after) => InspectorHistory.Record(_target, WhereLabel(), MemberId(row2),
                                as32 ? (object)(Color32)before : before, as32 ? (object)(Color32)after : after, row2.Getter, row2.Setter));
                    }
                }
                if (InspectorColorPicker.Open && InspectorColorPicker.Key == r.Key)
                {
                    InspectorColorPicker.Sync(c);
                }
                bx += row;
            }
        }

        private static string Fmt(Type t, float f)
        {
            if (t == typeof(Vector2Int) || t == typeof(Vector3Int) || t == typeof(Color32))
            {
                return ((int)f).ToString(CultureInfo.InvariantCulture);
            }
            return InspectorModel.Fmt(f);
        }

        // The text a field shows: its draft while focused, else the live value
        // (and a stale draft is dropped on Repaint).
        private static string FieldText(string key, string shown, Event ev)
        {
            if (_focusedControl == key)
            {
                if (!Drafts.TryGetValue(key, out string text))
                {
                    text = shown;
                    Drafts[key] = text;
                    DraftStart[key] = text;
                }
                return text;
            }
            if (Drafts.ContainsKey(key) && ev.type == EventType.Repaint)
            {
                // Focus left the field: a draft the person typed in is applied, then dropped.
                bool typed = !DraftStart.TryGetValue(key, out string start) || Drafts[key] != start;
                if (typed && !_numberDragging)
                {
                    Apply(RowKeyOf(key));
                }
                Drafts.Remove(key);
                DraftStart.Remove(key);
            }
            return shown;
        }

        private static string RowKeyOf(string control)
        {
            int hash = control.IndexOf('#');
            return hash < 0 ? control : control.Substring(0, hash);
        }

        // Applies a row's draft(s): the single field, or the composite's
        // components, drafts where they exist and the live value elsewhere.
        private static void Apply(string rowKey)
        {
            RowInfo r = Rows.Find(x => x.Key == rowKey);
            if (r == null || r.Setter == null)
            {
                return;
            }
            object parsed;
            string error = null;
            if (InspectorModel.IsComposite(r.Type))
            {
                object current = SafeGet(r.Member, r.Getter);
                if (r.Member.Failure != null)
                {
                    return;
                }
                float[] parts = InspectorModel.Components(current);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (Drafts.TryGetValue(rowKey + "#" + i, out string draft))
                    {
                        if (!float.TryParse(draft.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                        {
                            error = $"{InspectorModel.ComponentLabels(r.Type)[i]}: not a number";
                            break;
                        }
                        parts[i] = f;
                    }
                }
                parsed = error == null ? InspectorModel.Compose(r.Type, parts) : null;
            }
            else
            {
                if (!Drafts.TryGetValue(rowKey, out string text))
                {
                    return;
                }
                parsed = InspectorModel.Parse(r.Type, text, out error);
            }
            if (error != null)
            {
                RowErrors[rowKey] = "Not accepted: " + error;
                return;
            }
            if (TrySet(r, parsed))
            {
                Drafts.Remove(rowKey);
                for (int i = 0; i < 6; i++)
                {
                    Drafts.Remove(rowKey + "#" + i);
                }
            }
        }

        private static bool TrySet(RowInfo r, object value)
        {
            try
            {
                object before = SafeGet(r.Member, r.Getter);
                bool haveBefore = r.Member.Failure == null;
                r.Setter(value);
                if (haveBefore)
                {
                    object target = _target;
                    Func<object> get = r.Getter;
                    Action<object> set = r.Setter;
                    InspectorHistory.Record(target, WhereLabel(), MemberId(r), before, value, get, set);
                }
                RowErrors.Remove(r.Key);
                Frozen.Remove(r.Key);
                NoteSharedEdit();
                return true;
            }
            catch (Exception ex)
            {
                RowErrors[r.Key] = (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }
    }
}
