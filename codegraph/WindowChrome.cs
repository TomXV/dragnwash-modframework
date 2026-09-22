using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace DragNWash.CodeGraph
{
    // What both Code Graph windows set up the same way: this app's GraphForm and
    // CodeGraphStandalone.exe's ViewerForm, which builds this same file, so the
    // two cannot drift apart.
    internal static class WindowChrome
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // A dark title bar when Windows' apps are dark, like the page (Windows 10 20H1 and later; ignored before).
        internal static void DarkTitleBar(Form window)
        {
            try
            {
                object light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                if (!(light is int l) || l != 0) return;
                int on = 1;
                DwmSetWindowAttribute(window.Handle, 20, ref on, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
            }
            catch
            {
            }
        }

        // The page gets no dev tools, no status bar and no host objects.
        internal static void LockDown(CoreWebView2Settings settings)
        {
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreHostObjectsAllowed = false;
        }
    }
}
