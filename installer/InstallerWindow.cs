using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DragNWash.Installer
{
    // What the page posts: {"cmd": ..., and what that command needs}.
    [DataContract]
    internal sealed class PageMessage
    {
        [DataMember(Name = "cmd")] public string Cmd;
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "value")] public string Value;
        [DataMember(Name = "on")] public bool On;
        [DataMember(Name = "text")] public string Text;
    }

    // Install.exe's window in WebView2: borderless, 720 × 540, the page from the exe
    // like the launcher's (the same lock-down, Chrome.cs). The page draws its own title
    // bar and asks for moving, minimising and closing. It never goes online.
    internal sealed class InstallerWindow : Form
    {
        // A name that never resolves (.invalid is reserved): every request to it is
        // answered from the exe's resources, and every other request is refused.
        private const string Origin = "https://installer.invalid/";

        private readonly WebView2 _view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(24, 32, 37) };
        private readonly TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>();
        private readonly Func<string> _init;
        private readonly Action<string> _log;
        private TaskCompletionSource<bool> _faded;
        private bool _closing;

        // Every message from the page, on the window's thread.
        internal event Action<PageMessage> Message;

        // The window's own close (Alt+F4, the taskbar): the owner decides what it means.
        internal event Action CloseAsked;

        // WebView2's own processes failed after the page came up.
        internal event Action Broken;

        // init: the page's first event, built when the page asks for it.
        internal InstallerWindow(string title, Func<string> init, Action<string> log)
        {
            _init = init;
            _log = log;
            Text = title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 32, 37);
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = true;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
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

        // True once the page is loaded and has its init; false when it could not be.
        internal Task<bool> Ready => _ready.Task;

        // loader: the folder with WebView2Loader.dll (WebUi.cs).
        internal void Begin(string loader, string userData)
        {
            float scale = DeviceDpi / 96f;
            ClientSize = new Size((int)Math.Round(720 * scale), (int)Math.Round(540 * scale));
            CreateHandle();
            Chrome.Round(this);
            _ = Start(loader, userData);
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

        private async Task Start(string loader, string userData)
        {
            try
            {
                CoreWebView2Environment.SetLoaderDllFolderPath(loader);
                // Throws, or gives nothing, when the WebView2 runtime isn't installed.
                string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (string.IsNullOrEmpty(version))
                {
                    throw new InvalidOperationException("the WebView2 runtime isn't installed");
                }
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userData);
                await _view.EnsureCoreWebView2Async(env);
                CoreWebView2 core = _view.CoreWebView2;
                Chrome.LockDown(core.Settings);
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (s, e) => e.Response = Chrome.Serve(core.Environment, Origin, e.Request.Uri, Page, _log);
                core.NavigationStarting += (s, e) =>
                {
                    if (!e.Uri.StartsWith(Origin, StringComparison.Ordinal))
                    {
                        _log("Window: blocked a navigation to " + e.Uri);
                        e.Cancel = true;
                    }
                };
                core.NewWindowRequested += (s, e) => e.Handled = true;
                core.DownloadStarting += (s, e) => e.Cancel = true;
                core.PermissionRequested += (s, e) => e.State = CoreWebView2PermissionState.Deny;
                core.WebMessageReceived += OnMessage;
                core.ProcessFailed += (s, e) =>
                {
                    _log("Window: WebView2 failed: " + e.ProcessFailedKind);
                    if (!_ready.TrySetResult(false))
                    {
                        Broken?.Invoke();
                    }
                };
                core.Navigate(Origin + "index.html");
            }
            catch (Exception ex)
            {
                _log("Window: WebView2 could not start: " + ex.Message);
                _ready.TrySetResult(false);
            }
        }

        // One of the page's files, from the exe's resources.
        private static Stream Page(string name)
        {
#if DEBUG
            // For working on the page without building again: DNW_INSTALLER_WEB=<the installer/web folder>;
            // the shared parts are then read from webui/ beside it. Only in Debug builds; a
            // Release build serves nothing but its own resources.
            string folder = Environment.GetEnvironmentVariable("DNW_INSTALLER_WEB");
            if (!string.IsNullOrEmpty(folder))
            {
                foreach (string file in new[] { Path.Combine(folder, name), Path.Combine(folder, "..", "..", "webui", name), Path.Combine(folder, "..", "..", "images", name) })
                {
                    if (File.Exists(file))
                    {
                        return File.OpenRead(file);
                    }
                }
                return null;
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
                _log("Window: a message from the page could not be read: " + ex.Message);
                return;
            }
            switch (message?.Cmd)
            {
                case null:
                    return;
                case "ready":
                    if (_closing)
                    {
                        // Too late: the WinForms window has taken over (Gone), so this one never shows.
                        return;
                    }
                    Send(_init());
                    if (!Visible)
                    {
                        Show();
                        Activate();
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
                    _faded?.TrySetResult(message.On);
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
                _log("Window: could not send to the page: " + ex.Message);
            }
        }

        // Closes the window for good: the page fades out, then the window. Gives up waiting
        // for the page after a moment.
        internal async Task FadeOut()
        {
            if (_closing || IsDisposed)
            {
                return;
            }
            bool fade = false;
            if (Visible && WindowState != FormWindowState.Minimized && _ready.Task.IsCompleted && _ready.Task.Result)
            {
                _faded = new TaskCompletionSource<bool>();
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
            _ready.TrySetResult(false);
            if (IsDisposed)
            {
                return;
            }
            try
            {
                Hide();
                Close();
            }
            catch (Exception ex)
            {
                _log("Window: could not be closed: " + ex.Message);
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
}
