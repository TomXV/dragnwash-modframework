using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    // ToolWindow.Confirm: a question on one row, where the action was pressed,
    // instead of a dialog. One at a time in the window; it goes on Cancel, on
    // Esc, after a few seconds, when the tab changes and when the window closes.
    internal static class InlineConfirm
    {
        internal const float Seconds = 5f;

        private static string _id;
        private static float _until;
        private static bool _expired;

        internal static bool Active => _id != null;

        internal static void Ask(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }
            _id = id;
            _until = Time.realtimeSinceStartup + Seconds;
            _expired = false;
        }

        internal static bool IsAsking(string id)
        {
            return _id != null && _id == id;
        }

        internal static void Cancel()
        {
            _id = null;
            _expired = false;
        }

        // After a repaint, so a frame's layout and paint agree on whether the
        // row is there: the time ran out.
        internal static void Tick()
        {
            if (_id != null && Time.realtimeSinceStartup >= _until)
            {
                _expired = true;
            }
            if (_expired)
            {
                Cancel();
            }
        }

        internal static bool Draw(Rect row, string id, string question, string yes, string hint, ToolWindowStyles s)
        {
            if (!IsAsking(id))
            {
                return false;
            }
            yes = string.IsNullOrEmpty(yes) ? "Yes" : yes;
            float gap = 8f;
            float yesWidth = Mathf.Max(70f, s.Button.CalcSize(new GUIContent(yes)).x + 14);
            float cancelWidth = 80f;
            int left = Mathf.Max(1, Mathf.CeilToInt(_until - Time.realtimeSinceStartup));
            string countdown = $"auto-cancels in {left} s";
            float countdownWidth = s.MutedLabel.CalcSize(new GUIContent(countdown)).x;
            float room = row.width - yesWidth - cancelWidth - gap * 3 - countdownWidth;
            // In a narrow tab the question matters more than the countdown.
            if (room < 120f)
            {
                countdownWidth = 0f;
                room = row.width - yesWidth - cancelWidth - gap * 2;
            }
            string shown = ToolWindow.Elide(question ?? "", s.Label, Mathf.Max(40f, room));
            float questionWidth = Mathf.Min(s.Label.CalcSize(new GUIContent(shown)).x + 4, Mathf.Max(40f, room));
            float x = row.x;
            GUI.Label(new Rect(x, row.y, questionWidth, row.height), shown, s.Label);
            x += questionWidth + gap;
            bool confirmed = false;
            if (GUI.Button(new Rect(x, row.y, yesWidth, row.height), yes, s.Button))
            {
                confirmed = true;
                Cancel();
            }
            x += yesWidth + gap;
            if (GUI.Button(new Rect(x, row.y, cancelWidth, row.height), "Cancel", s.Button))
            {
                Cancel();
            }
            x += cancelWidth + gap;
            if (countdownWidth > 0f)
            {
                GUI.Label(new Rect(x, row.y, countdownWidth + 4, row.height), countdown, s.MutedLabel);
            }
            if (IsAsking(id))
            {
                WindowFooter.SetHint(string.IsNullOrEmpty(hint) ? $"{yes} goes ahead; Cancel or {Seconds:0} s leaves it as it is. Esc = Cancel." : hint, WindowFooter.HintConfirm);
            }
            return confirmed;
        }
    }
}
