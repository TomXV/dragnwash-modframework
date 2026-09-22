using UnityEngine;
using UnityEngine.InputSystem;
using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;

namespace DragNWash.ModFramework.Inspector
{
    // A free camera for looking around the scene: a copy of the game's main
    // camera that takes its place while it is on, flown as in Unity's scene
    // view (right mouse button held: mouse look, W A S D, Q E down and up,
    // Shift fast, the wheel changes the speed). The game's own camera is
    // disabled meanwhile and put back when the free camera is turned off; the
    // player and the game keep running.
    internal static class InspectorFreeCamera
    {
        internal static bool Active { get; private set; }
        internal static bool Flying { get; private set; }

        private static Camera _original;
        private static GameObject _rig;
        private static Camera _camera;
        private static float _speed = 3f;
        private static float _yaw, _pitch;
        private static Vector2 _lastMouse;
        private static Rect _window;
        private static int _windowFrame = -1;
        private static bool _pressOverWindow;

        internal static void Toggle()
        {
            if (Active) Stop(); else Start();
        }

        internal static void Start()
        {
            Camera main = Camera.main;
            if (main == null)
            {
                // No camera tagged MainCamera: the enabled one drawing last.
                foreach (Camera c in Camera.allCameras)
                {
                    if (c != null && c.enabled && (main == null || c.depth > main.depth)) main = c;
                }
            }
            if (main == null)
            {
                TW.ShowNotice("Free camera: no camera to copy.", NoticeKind.Warning);
                InspectorPlugin.Log.LogWarning("[camera] no enabled camera found");
                return;
            }
            InspectorPlugin.Log.LogInfo($"[camera] free camera from {main.name} (depth {main.depth})");
            _original = main;
            _rig = new GameObject("DragNWash Free Camera");
            _camera = _rig.AddComponent<Camera>();
            _camera.CopyFrom(main);
            _camera.depth = main.depth + 1;
            _rig.transform.position = main.transform.position;
            _rig.transform.rotation = main.transform.rotation;
            Vector3 e = main.transform.rotation.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180 ? e.x - 360 : e.x;
            _rig.tag = "MainCamera";
            main.enabled = false;
            Active = true;
            TW.ShowNotice("Free camera: hold the right mouse button, W A S D move, Q E down and up, Shift fast, wheel speed. C or the button stops it.");
        }

        internal static void Stop()
        {
            if (!Active)
            {
                return;
            }
            Active = false;
            Flying = false;
            TW.BlockGameInput(Inspector.Guid + ".camera", false);
            if (_original != null)
            {
                _original.enabled = true;
            }
            if (_rig != null)
            {
                Object.Destroy(_rig);
            }
            _rig = null;
            _camera = null;
            _original = null;
            TW.ShowNotice("Free camera off.");
        }

        // From the overlay pass, every frame the window is drawn: where it is,
        // so a right press over it flies nothing.
        internal static void NoteWindow(Rect window)
        {
            _window = window;
            _windowFrame = Time.frameCount;
        }

        // OnGUI runs after Update, so the rectangle is a frame or two old.
        private static bool OverWindow(Mouse mouse)
        {
            if (_windowFrame < Time.frameCount - 2 || !TW.IsOpen)
            {
                return false;
            }
            Vector2 p = mouse.position.ReadValue();
            // The window is measured in GUI space: y from the top.
            return _window.Contains(new Vector2(p.x, Screen.height - p.y));
        }

        // From the plugin's Update.
        internal static void Update()
        {
            if (!Active)
            {
                return;
            }
            if (_rig == null || _original == null)
            {
                // The scene changed under the camera: give the game its camera back.
                Stop();
                return;
            }
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (mouse == null || keyboard == null)
            {
                return;
            }
            // A right click in the window belongs to the window (a menu, a
            // field): the camera only flies when the press began over the game,
            // and keeps flying over the window once it has.
            if (!mouse.rightButton.isPressed)
            {
                _pressOverWindow = false;
            }
            else if (!Flying && !_pressOverWindow && OverWindow(mouse))
            {
                _pressOverWindow = true;
            }
            bool held = mouse.rightButton.isPressed && !_pressOverWindow;
            if (held && !Flying)
            {
                _lastMouse = mouse.position.ReadValue();
            }
            Flying = held;
            TW.BlockGameInput(Inspector.Guid + ".camera", held);
            if (!held)
            {
                return;
            }
            Vector2 now = mouse.position.ReadValue();
            Vector2 delta = now - _lastMouse;
            _lastMouse = now;
            _yaw += delta.x * 0.15f;
            _pitch = Mathf.Clamp(_pitch - delta.y * 0.15f, -89f, 89f);
            _rig.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0);

            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                _speed = Mathf.Clamp(_speed * (wheel > 0 ? 1.25f : 0.8f), 0.1f, 100f);
            }
            Vector3 move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += _rig.transform.forward;
            if (keyboard.sKey.isPressed) move -= _rig.transform.forward;
            if (keyboard.dKey.isPressed) move += _rig.transform.right;
            if (keyboard.aKey.isPressed) move -= _rig.transform.right;
            if (keyboard.eKey.isPressed) move += Vector3.up;
            if (keyboard.qKey.isPressed) move -= Vector3.up;
            float speed = _speed * (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? 4f : 1f);
            _rig.transform.position += move * speed * Time.unscaledDeltaTime;
        }

        internal static string Status()
        {
            return Active ? $"Free camera on, speed {_speed:0.#} m/s (wheel). Hold the right mouse button to fly." : "";
        }
    }
}
