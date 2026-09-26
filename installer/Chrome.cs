using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace DragNWash.Installer
{
    // The frame and the page settings of a borderless WebView2 window: the launcher's
    // and Install.exe's. Compiled into both (the launcher links this file).
    internal static class Chrome
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // What the page may load: its own files, pictures as data: URLs, and nothing online.
        internal const string Policy = "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'none'; base-uri 'none'; form-action 'none'";

        internal static readonly Dictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".js"] = "text/javascript; charset=utf-8",
            [".png"] = "image/png",
            [".svg"] = "image/svg+xml",
        };

        // Rounded corners and a dark frame on Windows 11; ignored before.
        internal static void Round(Form window)
        {
            try
            {
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(window.Handle, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
                int dark = 1;
                DwmSetWindowAttribute(window.Handle, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            }
            catch (Exception)
            {
            }
        }

        // The page's title bar was pressed: the window follows the mouse until the button is
        // let go. Followed here rather than handed to Windows (WM_NCLBUTTONDOWN), because the
        // mouse is captured by WebView2's own process, not by this window.
        internal static void Drag(Form window)
        {
            Point start = Cursor.Position;
            Point from = window.Location;
            var follow = new Timer { Interval = 10 };
            follow.Tick += (s, e) =>
            {
                if ((Control.MouseButtons & MouseButtons.Left) == 0 || window.IsDisposed)
                {
                    follow.Dispose();
                    return;
                }
                Point now = Cursor.Position;
                window.Location = new Point(from.X + now.X - start.X, from.Y + now.Y - start.Y);
            };
            follow.Start();
        }

        // No dev tools, menus, zoom, browser keys (reload, find, print) or host objects.
        internal static void LockDown(CoreWebView2Settings settings)
        {
#if !DEBUG
            settings.AreDevToolsEnabled = false;
#endif
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPinchZoomEnabled = false;
            settings.IsSwipeNavigationEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
        }

        // The answer to a request of the page: one of its files (page: a file name without
        // folders, to its stream or null) under origin, with the policy on the HTML. Anything
        // else is refused.
        internal static CoreWebView2WebResourceResponse Serve(CoreWebView2Environment env, string origin, string uri, Func<string, Stream> page, Action<string> log)
        {
            if (!uri.StartsWith(origin, StringComparison.Ordinal))
            {
                log("Window: refused a request for " + uri);
                return env.CreateWebResourceResponse(null, 403, "Forbidden", "");
            }
            string path = uri.Substring(origin.Length);
            int cut = path.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0)
            {
                path = path.Substring(0, cut);
            }
            Stream resource = path.IndexOf('/') < 0 && path.IndexOf('\\') < 0 ? page(path) : null;
            if (resource == null || !Types.TryGetValue(Path.GetExtension(path), out string type))
            {
                resource?.Dispose();
                return env.CreateWebResourceResponse(null, 404, "Not Found", "");
            }
            var copy = new MemoryStream();
            using (resource)
            {
                resource.CopyTo(copy);
            }
            copy.Position = 0;
            string headers = "Content-Type: " + type + "\r\nCache-Control: no-store"
                + (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "\r\nContent-Security-Policy: " + Policy : "");
            return env.CreateWebResourceResponse(copy, 200, "OK", headers);
        }
    }
}
