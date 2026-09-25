using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DragNWash.Launcher
{
    // What the page posts: {"cmd": ..., and what that command needs}.
    [DataContract]
    internal sealed class PageMessage
    {
        [DataMember(Name = "cmd")] public string Cmd;
        [DataMember(Name = "guid")] public string Guid;
        [DataMember(Name = "on")] public bool On;
        [DataMember(Name = "mods")] public string[] Mods;
        [DataMember(Name = "text")] public string Text;
    }

    // The launcher's window: borderless, 720 × 440, the page from the exe in WebView2.
    // The page draws its own title bar and asks for moving, minimising and closing.
    // It may load nothing but its own files, and it never goes online.
    internal sealed class LauncherWindow : Form
    {
        // A name that never resolves (.invalid is reserved): every request to it is
        // answered from the exe's resources, and every other request is refused.
        private const string Origin = "https://launcher.invalid/";

        private const string Policy = "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'none'; base-uri 'none'; form-action 'none'";

        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".js"] = "text/javascript; charset=utf-8",
            [".png"] = "image/png",
            [".svg"] = "image/svg+xml",
        };

        private readonly WebView2 _view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(24, 32, 37) };
        private readonly TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>();
        private readonly TaskCompletionSource<bool> _faded = new TaskCompletionSource<bool>();
        private readonly string _init;
        private bool _closing;

        // Every message from the page, on the window's thread.
        internal event Action<PageMessage> Message;

        // The window's own close (Alt+F4, the taskbar): the owner decides what it means.
        internal event Action CloseAsked;

        // The intro asks nothing of the player, so it doesn't take the focus from whatever
        // has it; the boards that need an answer do.
        internal bool Quiet;

        protected override bool ShowWithoutActivation => Quiet;

        internal LauncherWindow(string init)
        {
            _init = init;
            Text = "Drag'n Wash ModFramework";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 32, 37);
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = true;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            }
            catch (Exception)
            {
            }
            Controls.Add(_view);
            FormClosing += (s, e) =>
            {
                if (!_closing && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    CloseAsked?.Invoke();
                }
            };
        }

        // True once the page is loaded and has its init; false when it could not be
        // (then the owner starts the game without the window).
        internal Task<bool> Ready => _ready.Task;

        // The window's thread may be busy; this waits for the handle, not for the window.
        internal void Begin(string userData)
        {
            float scale = DeviceDpi / 96f;
            ClientSize = new Size((int)Math.Round(720 * scale), (int)Math.Round(440 * scale));
            CreateHandle();
            Chrome.Round(this);
            _ = Start(userData);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                // Borderless, but still minimised and restored from the taskbar.
                CreateParams p = base.CreateParams;
                p.Style |= 0x00020000; // WS_MINIMIZEBOX
                return p;
            }
        }

        private async Task Start(string userData)
        {
            try
            {
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userData);
                await _view.EnsureCoreWebView2Async(env);
                CoreWebView2 core = _view.CoreWebView2;
                Chrome.LockDown(core.Settings);
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (s, e) => e.Response = Serve(core.Environment, e.Request.Uri);
                core.NavigationStarting += (s, e) =>
                {
                    if (!e.Uri.StartsWith(Origin, StringComparison.Ordinal))
                    {
                        Log.Line("Window: blocked a navigation to " + e.Uri);
                        e.Cancel = true;
                    }
                };
                core.NewWindowRequested += (s, e) => e.Handled = true;
                core.DownloadStarting += (s, e) => e.Cancel = true;
                core.PermissionRequested += (s, e) => e.State = CoreWebView2PermissionState.Deny;
                core.WebMessageReceived += OnMessage;
                core.ProcessFailed += (s, e) =>
                {
                    Log.Line("Window: WebView2 failed: " + e.ProcessFailedKind);
                    _ready.TrySetResult(false);
                };
                core.Navigate(Origin + "index.html");
            }
            catch (Exception ex)
            {
                Log.Line("Window: WebView2 could not start: " + ex.Message);
                _ready.TrySetResult(false);
            }
        }

        private CoreWebView2WebResourceResponse Serve(CoreWebView2Environment env, string uri)
        {
            if (!uri.StartsWith(Origin, StringComparison.Ordinal))
            {
                Log.Line("Window: refused a request for " + uri);
                return env.CreateWebResourceResponse(null, 403, "Forbidden", "");
            }
            string path = uri.Substring(Origin.Length);
            int cut = path.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0)
            {
                path = path.Substring(0, cut);
            }
            Stream resource = path.IndexOf('/') < 0 ? Page(path) : null;
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

        // One of the page's files, from the exe's resources.
        private static Stream Page(string name)
        {
#if DEBUG
            // For working on the page without building again: DNW_LAUNCHER_WEB=<the launcher/web folder>.
            // The shared parts come from the repo's webui folder next to it. Only in Debug builds; a
            // Release build serves nothing but its own resources.
            string folder = Environment.GetEnvironmentVariable("DNW_LAUNCHER_WEB");
            if (!string.IsNullOrEmpty(folder))
            {
                string file = Path.Combine(folder, name);
                if (!File.Exists(file))
                {
                    file = Path.Combine(folder, "..", "..", "webui", name);
                }
                if (!File.Exists(file) && name == "logo-notagames.png")
                {
                    file = Path.Combine(folder, "..", "..", "images", name);
                }
                return File.Exists(file) ? File.OpenRead(file) : null;
            }
#endif
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("web/" + name);
        }

        private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!e.Source.StartsWith(Origin, StringComparison.Ordinal))
            {
                return;
            }
            PageMessage message;
            try
            {
                string json = e.TryGetWebMessageAsString();
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    message = (PageMessage)new DataContractJsonSerializer(typeof(PageMessage)).ReadObject(stream);
                }
            }
            catch (Exception ex)
            {
                Log.Line("Window: a message from the page could not be read: " + ex.Message);
                return;
            }
            switch (message?.Cmd)
            {
                case null:
                    return;
                case "ready":
                    Send(_init);
                    if (!Visible)
                    {
                        Show();
                        if (!Quiet)
                        {
                            Activate();
                        }
                    }
                    _ready.TrySetResult(true);
                    return;
                case "drag":
                    Chrome.Drag(this);
                    return;
                case "minimize":
                    WindowState = FormWindowState.Minimized;
                    return;
                case "gone":
                    // The page has faded out (On: with its animations on, so the window fades too).
                    _faded.TrySetResult(message.On);
                    return;
            }
            Message?.Invoke(message);
        }

        // An event for the page's window.dnw, as JSON. From any thread.
        internal void Send(string json)
        {
            if (_closing || IsDisposed)
            {
                return;
            }
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => Send(json)));
                return;
            }
            try
            {
                _ = _view.CoreWebView2?.ExecuteScriptAsync("window.dnw && window.dnw(" + json + ")");
            }
            catch (Exception ex)
            {
                Log.Line("Window: could not send to the page: " + ex.Message);
            }
        }

        // Closes the window for good: the page fades out, then the window, and then it's gone
        // from the screen and the taskbar. The owner waits for this before it starts the game,
        // and gives up waiting after a moment (the window is hidden then anyway).
        internal async Task FadeOut()
        {
            if (_closing || IsDisposed)
            {
                return;
            }
            bool fade = false;
            if (Visible && WindowState != FormWindowState.Minimized && _ready.Task.IsCompleted && _ready.Task.Result)
            {
                Send(Json.Object("type", "bye"));
                Task<bool> faded = _faded.Task;
                if (await Task.WhenAny(faded, Task.Delay(1000)) == faded)
                {
                    fade = faded.Result;
                }
            }
            _closing = true;
            if (fade && !IsDisposed)
            {
                // What is left is the page's dark background; the window takes it away.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                const double length = 160;
                while (!IsDisposed && clock.ElapsedMilliseconds < length)
                {
                    double t = clock.ElapsedMilliseconds / length;
                    Opacity = Math.Max(0, 1 - t * t);
                    await Task.Delay(15);
                }
            }
            Gone();
        }

        // Off the screen and the taskbar at once, without any fade.
        internal void Gone()
        {
            _closing = true;
            // An owner still waiting for a page that never came up learns it won't.
            _ready.TrySetResult(false);
            if (IsDisposed)
            {
                return;
            }
            try
            {
                TopMost = false;
                Hide();
                Close();
            }
            catch (Exception ex)
            {
                Log.Line("Window: could not be closed: " + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _view.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // The window's frame and the page's settings.
    internal static class Chrome
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

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
    }
}
