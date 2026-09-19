using System;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Microsoft.Web.WebView2.WinForms;

namespace DragNWash.CodeGraph
{
    // The window: the Bridge's page in WebView2. It signs in with the Bridge's
    // token (POST /page/api/code gives it a one-time code, as the Graph button
    // does for the browser), waits while the game is not running, and signs in
    // again when the game comes back, at the method it was showing.
    internal sealed class GraphForm : Form
    {
        private static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragNWash ModFramework");
        private static readonly string TokenFile = Path.Combine(Home, "bridge-token.txt");
        private static readonly string BoundsFile = Path.Combine(Home, "CodeGraph", "window.txt");

        private readonly int _port;
        private readonly WebView2 _view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(14, 18, 26) };
        private readonly System.Windows.Forms.Timer _retry = new System.Windows.Forms.Timer { Interval = 2000 };
        private string _focus;
        private bool _connecting;
        private bool _waiting;
        private bool _pageShown;

        internal GraphForm(int port, string focus)
        {
            _port = port;
            _focus = Valid(focus) ? focus : null;
            Text = "Drag'n Wash Code Graph";
            BackColor = Color.FromArgb(14, 18, 26);
            StartPosition = FormStartPosition.Manual;
            Bounds = SavedBounds();
            Controls.Add(_view);
            _retry.Tick += (s, e) => { _retry.Stop(); Connect(); };
            Load += async (s, e) => await Start();
            FormClosing += (s, e) => SaveBounds();
            HandleCreated += (s, e) => DarkTitleBar();
            var listener = new Thread(Listen) { IsBackground = true, Name = "CodeGraph pipe" };
            listener.Start();
        }

        private string Origin => "http://127.0.0.1:" + _port;

        private async Task Start()
        {
            try
            {
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Home, "CodeGraph", "WebView2"));
                await _view.EnsureCoreWebView2Async(env);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(this, "This window needs the Microsoft Edge WebView2 Runtime, which comes with Windows 10 and 11 but is missing here.\n\nSet [Bridge] OpenPageIn to Browser in the game's Mods screen to use the browser instead, or install the WebView2 Runtime from Microsoft.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                return;
            }
            CoreWebView2 core = _view.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            // Only the Bridge's page: any other address stays out of this window.
            core.NavigationStarting += (s, e) =>
            {
                if (!e.Uri.StartsWith(Origin + "/page", StringComparison.Ordinal) && !e.Uri.StartsWith("data:", StringComparison.Ordinal) && e.Uri != "about:blank") e.Cancel = true;
            };
            core.NewWindowRequested += (s, e) => e.Handled = true;
            core.WebMessageReceived += OnMessage;
            Connect();
        }

        // The page tells where it is ("focus:<f>") and when it lost the game ("lost").
        private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!e.Source.StartsWith(Origin + "/page", StringComparison.Ordinal)) return;
            string message;
            try { message = e.TryGetWebMessageAsString(); } catch { return; }
            if (message.StartsWith("focus:", StringComparison.Ordinal))
            {
                string f = message.Substring(6);
                if (Valid(f)) _focus = f;
            }
            else if (message == "lost")
            {
                _pageShown = false;
                Connect();
            }
        }

        private async void Connect()
        {
            if (_connecting || _view.CoreWebView2 == null) return;
            _connecting = true;
            try
            {
                string code = await Task.Run(() => RequestCode(out _));
                if (code != null)
                {
                    _waiting = false;
                    _pageShown = true;
                    string url = Origin + "/page#code=" + code + (_focus != null ? "&focus=" + Uri.EscapeDataString(_focus) : "");
                    _view.CoreWebView2.Navigate(url);
                    return;
                }
                if (!_waiting)
                {
                    _waiting = true;
                    _view.CoreWebView2.NavigateToString(WaitingPage);
                }
                _retry.Start();
            }
            finally
            {
                _connecting = false;
            }
        }

        // A one-time code from the Bridge, with its token; null while the game (or the Bridge) is not there.
        private string RequestCode(out string why)
        {
            why = null;
            try
            {
                string token = File.Exists(TokenFile) ? File.ReadAllText(TokenFile).Trim() : null;
                if (string.IsNullOrEmpty(token)) { why = "no token yet"; return null; }
                var request = (HttpWebRequest)WebRequest.Create(Origin + "/page/api/code");
                request.Method = "POST";
                request.Timeout = 3000;
                request.ContentLength = 0;
                request.Proxy = null;
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    Match m = Regex.Match(reader.ReadToEnd(), "\"code\"\\s*:\\s*\"([A-Za-z0-9_-]+)\"");
                    return m.Success ? m.Groups[1].Value : null;
                }
            }
            catch (Exception ex)
            {
                why = ex.Message;
                return null;
            }
        }

        // Another start of CodeGraph.exe hands its focus over through the pipe.
        private void Listen()
        {
            while (true)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(Program.PipeName, PipeDirection.In, 1))
                    {
                        pipe.WaitForConnection();
                        string line;
                        using (var reader = new StreamReader(pipe, Encoding.UTF8)) line = reader.ReadLine();
                        BeginInvoke((Action)(() => Show(line)));
                    }
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        }

        private async void Show(string focus)
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            if (!Valid(focus)) return;
            _focus = focus;
            if (_pageShown && _view.CoreWebView2 != null)
            {
                await _view.CoreWebView2.ExecuteScriptAsync("window.dnwOpen && window.dnwOpen(" + JsString(focus) + ")");
            }
            else
            {
                Connect();
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // A dark title bar when Windows' apps are dark, like the page (Windows 10 20H1 and later; ignored before).
        private void DarkTitleBar()
        {
            try
            {
                object light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                if (!(light is int l) || l != 0) return;
                int on = 1;
                DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
            }
            catch
            {
            }
        }

        private static bool Valid(string focus) => !string.IsNullOrEmpty(focus) && focus.Length < 2000 && (focus.StartsWith("m:", StringComparison.Ordinal) || focus.StartsWith("t:", StringComparison.Ordinal));

        private static string JsString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ' || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        private Rectangle SavedBounds()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            var fallback = new Rectangle(area.X + area.Width / 10, area.Y + area.Height / 10, area.Width * 8 / 10, area.Height * 8 / 10);
            try
            {
                string[] parts = File.ReadAllText(BoundsFile).Split(',');
                var r = new Rectangle(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
                foreach (Screen s in Screen.AllScreens)
                {
                    if (s.WorkingArea.IntersectsWith(r) && r.Width >= 480 && r.Height >= 320) return r;
                }
            }
            catch
            {
            }
            return fallback;
        }

        private void SaveBounds()
        {
            try
            {
                Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                Directory.CreateDirectory(Path.GetDirectoryName(BoundsFile));
                File.WriteAllText(BoundsFile, $"{r.X},{r.Y},{r.Width},{r.Height}");
            }
            catch
            {
            }
        }

        private const string WaitingPage = @"<!doctype html><html><head><meta charset=""utf-8""><title>Drag'n Wash Code Graph</title>
<style>html,body{height:100%;margin:0}body{display:grid;place-items:center;background:#0e121a;color:#99a8ba;font:15px/1.5 system-ui,'Segoe UI',sans-serif}
div{max-width:46ch;text-align:center}b{color:#52c7b8;letter-spacing:.08em;font-size:13px;text-transform:uppercase}p{margin:.6em 0}</style></head>
<body><div><b>Drag'n Wash code graph</b><p>Waiting for the game.</p>
<p>Start Drag'n Wash with the developer tools and the Bridge on (F1 → Bridge). This window signs in by itself when the game is there.</p></div></body></html>";
    }
}
