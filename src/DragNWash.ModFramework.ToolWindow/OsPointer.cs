using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    // Real mouse and keyboard input from the system, for the window's pad and
    // trackpad use. On the Steam Deck, Steam Input turns the trackpad click into
    // a gamepad stick press and sends no wheel at all, so IMGUI never sees a
    // mouse button: only buttons could be made to work (VirtualClick), not
    // fields, drags, sliders, scroll bars or anything else. Sent back to the
    // system as a real left button and wheel, every control in the window works
    // as with a mouse. The d-pad goes the same way as real arrow keys (Key), so
    // the lists the arrow keys move through are usable with no keyboard at all.
    // X11 through XTest on Linux (the Deck runs the game's Linux build under
    // Xwayland), SendInput on Windows. Where neither works, Available is false
    // and VirtualClick keeps its button-only way.
    internal static class OsPointer
    {
        private enum Backend { None, X11, Windows }

        private static Backend _backend;
        private static bool _tried;
        private static IntPtr _display;
        private static bool _down;

        internal static bool Available
        {
            get
            {
                if (!_tried) Open();
                return _backend != Backend.None;
            }
        }

        internal static bool IsDown => _down;

        private static void Open()
        {
            _tried = true;
            try
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.LinuxPlayer:
                        _display = XOpenDisplay(IntPtr.Zero);
                        if (_display == IntPtr.Zero)
                        {
                            ToolWindowPlugin.Log.LogInfo("[pointer] No X display; pad and trackpad presses stay button-only.");
                            return;
                        }
                        if (!XTestQueryExtension(_display, out _, out _, out _, out _))
                        {
                            ToolWindowPlugin.Log.LogInfo("[pointer] The X server has no XTest; pad and trackpad presses stay button-only.");
                            return;
                        }
                        _backend = Backend.X11;
                        break;
                    case RuntimePlatform.WindowsPlayer:
                        _backend = Backend.Windows;
                        break;
                }
                if (_backend != Backend.None) ToolWindowPlugin.Log.LogInfo($"[pointer] Pad and trackpad presses become real mouse and key input ({_backend}).");
            }
            catch (Exception ex)
            {
                _backend = Backend.None;
                ToolWindowPlugin.Log.LogInfo($"[pointer] Real mouse input unavailable ({ex.GetType().Name}: {ex.Message}); pad and trackpad presses stay button-only.");
            }
        }

        internal static void Down()
        {
            if (_down || !Available) return;
            if (Send(Button.LeftDown)) _down = true;
        }

        internal static void Up()
        {
            if (!_down) return;
            _down = false;
            Send(Button.LeftUp);
        }

        // One wheel step: positive up (away from the player), negative down.
        internal static void Wheel(int steps)
        {
            if (steps == 0 || !Available) return;
            Send(steps > 0 ? Button.WheelUp : Button.WheelDown, Math.Abs(steps));
        }

        // One press and release of an arrow key, for the d-pad's use of the
        // window's lists. Only the four arrows are mapped: they are what the
        // lists move with. The key goes wherever the keyboard goes, which is the
        // game while its window is in front, and the window is open to take it.
        internal static void Key(KeyCode key)
        {
            if (!Available) return;
            ulong keysym;
            ushort vk;
            if (!Arrow(key, out keysym, out vk)) return;
            SendKey(keysym, vk);
        }

        private static bool Arrow(KeyCode key, out ulong keysym, out ushort vk)
        {
            switch (key)
            {
                case KeyCode.LeftArrow: keysym = 0xff51; vk = 0x25; return true;    // XK_Left / VK_LEFT
                case KeyCode.UpArrow: keysym = 0xff52; vk = 0x26; return true;      // XK_Up / VK_UP
                case KeyCode.RightArrow: keysym = 0xff53; vk = 0x27; return true;   // XK_Right / VK_RIGHT
                case KeyCode.DownArrow: keysym = 0xff54; vk = 0x28; return true;    // XK_Down / VK_DOWN
            }
            keysym = 0;
            vk = 0;
            return false;
        }

        private static bool SendKey(ulong keysym, ushort vk)
        {
            try
            {
                if (_backend == Backend.X11)
                {
                    byte code = XKeysymToKeycode(_display, keysym);
                    if (code == 0) return false;   // this keymap does not have it
                    XTestFakeKeyEvent(_display, code, true, 0);
                    XTestFakeKeyEvent(_display, code, false, 0);
                    XFlush(_display);
                    return true;
                }
                if (_backend == Backend.Windows)
                {
                    // The arrows are extended keys, so that flag goes on both halves.
                    var down = new INPUT { type = 1 };   // INPUT_KEYBOARD
                    down.u.ki.wVk = vk;
                    down.u.ki.dwFlags = 0x0001;          // KEYEVENTF_EXTENDEDKEY
                    INPUT up = down;
                    up.u.ki.dwFlags = 0x0001 | 0x0002;   // | KEYEVENTF_KEYUP
                    return SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT))) == 2;
                }
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogWarning($"[pointer] Sending key input failed ({ex.Message}); back to button-only.");
                _backend = Backend.None;
                _down = false;
            }
            return false;
        }

        private enum Button { LeftDown, LeftUp, WheelUp, WheelDown }

        private static bool Send(Button b, int count = 1)
        {
            try
            {
                if (_backend == Backend.X11)
                {
                    for (int i = 0; i < count; i++)
                    {
                        switch (b)
                        {
                            case Button.LeftDown: XTestFakeButtonEvent(_display, 1, true, 0); break;
                            case Button.LeftUp: XTestFakeButtonEvent(_display, 1, false, 0); break;
                            case Button.WheelUp: XTestFakeButtonEvent(_display, 4, true, 0); XTestFakeButtonEvent(_display, 4, false, 0); break;
                            case Button.WheelDown: XTestFakeButtonEvent(_display, 5, true, 0); XTestFakeButtonEvent(_display, 5, false, 0); break;
                        }
                    }
                    XFlush(_display);
                    return true;
                }
                if (_backend == Backend.Windows)
                {
                    var input = new INPUT { type = 0 };   // INPUT_MOUSE
                    switch (b)
                    {
                        case Button.LeftDown: input.u.mi.dwFlags = 0x0002; break;   // MOUSEEVENTF_LEFTDOWN
                        case Button.LeftUp: input.u.mi.dwFlags = 0x0004; break;     // MOUSEEVENTF_LEFTUP
                        case Button.WheelUp: input.u.mi.dwFlags = 0x0800; input.u.mi.mouseData = 120 * count; break;    // MOUSEEVENTF_WHEEL
                        case Button.WheelDown: input.u.mi.dwFlags = 0x0800; input.u.mi.mouseData = -120 * count; break;
                    }
                    return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
                }
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogWarning($"[pointer] Sending mouse input failed ({ex.Message}); back to button-only.");
                _backend = Backend.None;
                _down = false;
            }
            return false;
        }

        // ---- X11 ----

        [DllImport("libX11.so.6")]
        private static extern IntPtr XOpenDisplay(IntPtr name);

        [DllImport("libX11.so.6")]
        private static extern int XFlush(IntPtr display);

        [DllImport("libXtst.so.6")]
        private static extern bool XTestQueryExtension(IntPtr display, out int eventBase, out int errorBase, out int major, out int minor);

        [DllImport("libX11.so.6")]
        private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

        [DllImport("libXtst.so.6")]
        private static extern int XTestFakeButtonEvent(IntPtr display, uint button, bool isPress, ulong delay);

        [DllImport("libXtst.so.6")]
        private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, bool isPress, ulong delay);

        // ---- Windows ----

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // The two members of the union this sends. MOUSEINPUT is the larger, so
        // it sets the size of the union, and of INPUT with it.
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }
}
