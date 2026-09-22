using System;
using System.Drawing;
using System.Globalization;
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
        private static readonly string OnTopFile = Path.Combine(Home, "CodeGraph", "on-top.txt");

        private readonly int _port;
        private readonly WebView2 _view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(14, 18, 26) };
        private readonly System.Windows.Forms.Timer _retry = new System.Windows.Forms.Timer { Interval = 2000 };
        private readonly System.Windows.Forms.Timer _alive = new System.Windows.Forms.Timer { Interval = 3000 };
        private string _focus;
        private bool _connecting;
        private bool _waiting;
        private bool _pageShown;

        // Why the last sign-in failed, as the waiting page tells it.
        private enum Why { NotRunning, NoToken, Refused, Other }

        private Why _why;
        private string _detail;
        private int _tries;
        private DateTime _last;
        private bool _trying;
        // Said once, on the first page this window shows: the window that was open did not answer.
        private string _notice;

        internal GraphForm(int port, string focus, bool tookOver)
        {
            _port = port;
            _focus = Valid(focus) ? focus : null;
            _notice = tookOver ? Strings.Get(Strings.Key.TookOver) : null;
            Text = Strings.Get(Strings.Key.Title);
            BackColor = Color.FromArgb(14, 18, 26);
            StartPosition = FormStartPosition.Manual;
            Bounds = SavedBounds();
            try { TopMost = File.Exists(OnTopFile) && File.ReadAllText(OnTopFile).Trim() == "true"; } catch { }
            Controls.Add(_view);
            _retry.Tick += (s, e) => { _retry.Stop(); Connect(); };
            _alive.Tick += async (s, e) =>
            {
                // The page only notices the game is gone when it calls it; this notices sooner.
                if (!_pageShown || _connecting) return;
                if (!await Task.Run(() => Answers())) { _pageShown = false; Connect(); }
            };
            _alive.Start();
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
                MessageBox.Show(this, Strings.Get(Strings.Key.NoWebView2), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            // The page shows its Keep on top button once it knows it is in this window, and in which state.
            core.NavigationCompleted += async (s, e) =>
            {
                if (!_pageShown) return;
                await core.ExecuteScriptAsync("window.dnwPinned && window.dnwPinned(" + (TopMost ? "true" : "false") + ")");
                if (_notice != null && e.IsSuccess)
                {
                    string notice = _notice;
                    _notice = null;
                    await core.ExecuteScriptAsync(NoticeScript(notice));
                }
            };
            Connect();
        }

        // The page tells where it is ("focus:<f>") and when it lost the game ("lost");
        // the waiting page, that Retry now was pressed ("retry").
        private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message;
            try { message = e.TryGetWebMessageAsString(); } catch { return; }
            if (!e.Source.StartsWith(Origin + "/page", StringComparison.Ordinal))
            {
                // The waiting page comes from NavigateToString, so it has no address of its own.
                if (message == "retry" && _waiting && !_pageShown) Connect();
                return;
            }
            if (message.StartsWith("focus:", StringComparison.Ordinal))
            {
                string f = message.Substring(6);
                if (Valid(f)) _focus = f;
            }
            else if (message == "pin:true" || message == "pin:false")
            {
                TopMost = message == "pin:true";
                try { Directory.CreateDirectory(Path.GetDirectoryName(OnTopFile)); File.WriteAllText(OnTopFile, TopMost ? "true" : "false"); } catch { }
                _ = _view.CoreWebView2.ExecuteScriptAsync("window.dnwPinned && window.dnwPinned(" + (TopMost ? "true" : "false") + ")");
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
            _retry.Stop();
            try
            {
                if (_waiting)
                {
                    _trying = true;
                    ShowWaiting();
                }
                var (code, why, detail) = await Task.Run(() => RequestCode());
                _trying = false;
                if (code != null)
                {
                    if (_waiting) _notice = null;   // the waiting page has said it
                    _waiting = false;
                    _tries = 0;
                    _pageShown = true;
                    string url = Origin + "/page#code=" + code + (_focus != null ? "&focus=" + Uri.EscapeDataString(_focus) : "");
                    _view.CoreWebView2.Navigate(url);
                    return;
                }
                _tries++;
                _last = DateTime.Now;
                _why = why;
                _detail = detail;
                // A refused token stays refused however long this waits: only Retry now, or
                // Graph pressed again in the game, tries again.
                if (why != Why.Refused) _retry.Start();
                ShowWaiting();
            }
            finally
            {
                _connecting = false;
            }
        }

        // A one-time code from the Bridge, with its token; else why there is none.
        private (string code, Why why, string detail) RequestCode()
        {
            try
            {
                string token = File.Exists(TokenFile) ? File.ReadAllText(TokenFile).Trim() : null;
                if (string.IsNullOrEmpty(token)) return (null, Why.NoToken, null);
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
                    return m.Success ? (m.Groups[1].Value, Why.Other, null) : (null, Why.Other, Strings.Get(Strings.Key.NoCode));
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse answer)
            {
                using (answer)
                {
                    int status = (int)answer.StatusCode;
                    return (null, status == 401 ? Why.Refused : Why.Other, status + " " + Said(answer));
                }
            }
            catch (WebException ex) when (ex.Status == WebExceptionStatus.ConnectFailure)
            {
                return (null, Why.NotRunning, null);
            }
            catch (Exception ex)
            {
                return (null, Why.Other, Clause(ex.Message));
            }
        }

        // The first line of what the Bridge said with its refusal ("The Bridge needs its token."), else the status's name.
        private static string Said(HttpWebResponse answer)
        {
            string text = "";
            try
            {
                using (var reader = new StreamReader(answer.GetResponseStream(), Encoding.UTF8))
                {
                    var buffer = new char[300];
                    text = new string(buffer, 0, reader.Read(buffer, 0, buffer.Length));
                }
            }
            catch
            {
            }
            text = text.Split('\n')[0].Trim();
            if (text.Length == 0 || text.StartsWith("{", StringComparison.Ordinal)) text = answer.StatusDescription;
            return Clause(text);
        }

        // A message to go inside a sentence of the waiting page: one line, without a full stop of its own.
        private static string Clause(string text) => (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim().TrimEnd('.');

        // The waiting page the first time, and after that only what it says (so Retry now keeps its focus).
        private void ShowWaiting()
        {
            if (!_waiting)
            {
                _waiting = true;
                _view.CoreWebView2.NavigateToString(WaitingPage());
            }
            else
            {
                _ = _view.CoreWebView2.ExecuteScriptAsync("window.dnwWait && window.dnwWait(" + WaitingState() + ")");
            }
        }

        // Whether the Bridge answers at all (its page needs no sign-in to be fetched).
        private bool Answers()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(Origin + "/page");
                request.Method = "HEAD";
                request.Timeout = 2000;
                request.Proxy = null;
                using (request.GetResponse()) return true;
            }
            catch (WebException ex) when (ex.Response != null)
            {
                ex.Response.Dispose();
                return true;   // it answered, if only to say no
            }
            catch
            {
                return false;
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
                else if (c < ' ' || c == '<' || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
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

        // What the waiting page says now, for window.dnwWait: why this window is not signed in,
        // how often it tried, and when it tries next (in seconds; 0 when it waits for Retry now).
        private string WaitingState()
        {
            string why;
            switch (_why)
            {
                case Why.NotRunning: why = Strings.Get(Strings.Key.NotRunning, _port); break;
                case Why.NoToken: why = Strings.Get(Strings.Key.NoToken); break;
                case Why.Refused: why = Strings.Get(Strings.Key.TokenRefused, _detail); break;
                default: why = Strings.Get(Strings.Key.OtherReason, _detail); break;
            }
            bool refused = _why == Why.Refused;
            string head = Strings.Get(refused ? Strings.Key.NotSignedIn : Strings.Key.Waiting);
            string how = Strings.Get(refused ? Strings.Key.HowToken : Strings.Key.HowStart);
            string tries = Strings.Get(_tries == 1 ? Strings.Key.TriedOne : Strings.Key.TriedMany, _tries, _last.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            int next = _retry.Enabled ? _retry.Interval / 1000 : 0;
            return "{\"head\":" + JsString(head) + ",\"why\":" + JsString(why) + ",\"how\":" + JsString(how)
                + ",\"notice\":" + (_notice != null ? JsString(_notice) : "null") + ",\"tries\":" + JsString(tries)
                + ",\"trying\":" + (_trying ? "true" : "false") + ",\"next\":" + next + "}";
        }

        private string WaitingPage() => @"<!doctype html><html lang=""" + Strings.Current + @"""><head><meta charset=""utf-8""><title>" + Html(Strings.Get(Strings.Key.Title)) + @"</title>
<style>html{height:100%}body{min-height:100%;margin:0;display:grid;place-items:center;background:#0e121a;color:#99a8ba;font:15px/1.5 " + Strings.Fonts + @"}
main{max-width:52ch;padding:16px;text-align:center;display:flex;flex-direction:column;align-items:center}
b{color:#52c7b8;letter-spacing:.08em;font-size:13px;text-transform:uppercase}p{margin:.6em 0;text-wrap:pretty}:lang(ja) p{word-break:auto-phrase}
#head{margin:.6em 0 .2em;color:#e8eff7;font-size:18px;text-wrap:balance}#why{margin:.2em 0 .6em;color:#e8eff7}#notice{margin:.2em 0 .6em;color:#52c7b8}
.row{display:flex;flex-wrap:wrap;justify-content:center;gap:12px;align-items:center;margin:.8em 0}#tries{font-size:12px;text-align:left;font-variant-numeric:tabular-nums}
button{font:inherit;color:#e8eff7;background:#0b0e14;border:1px solid #2a3342;border-radius:6px;padding:5px 10px;cursor:pointer;white-space:nowrap}
button:hover:not([aria-disabled=true]){border-color:#52c7b8}button:focus-visible{outline:2px solid #52c7b8;outline-offset:2px}button[aria-disabled=true]{cursor:progress}button.pressed{opacity:.5}
#local{margin:1.4em 0 0;font-size:12px;border-top:1px solid #2a3342;padding-top:10px}</style></head>
<body><main>
<b>" + Html(Strings.Get(Strings.Key.Title)) + @"</b><p id=""head""></p><p id=""why"" role=""status"" aria-live=""polite""></p><p id=""notice"" hidden></p><p id=""how""></p>
<div class=""row""><button type=""button"" id=""retry"">" + Html(Strings.Get(Strings.Key.RetryNow)) + @"</button><span id=""tries""></span></div>
<p id=""local"">" + Html(Strings.Get(Strings.Key.NothingLeaves)) + @"</p>
</main><script>(function () {
var nextIn = " + JsString(Strings.Get(Strings.Key.NextIn)) + ", tryingNow = " + JsString(Strings.Get(Strings.Key.TryingNow)) + ", waits = " + JsString(Strings.Get(Strings.Key.WaitsForRetry)) + @", tick, busy, wide = 0;
var $ = function (id) { return document.getElementById(id); };
function put(id, text) { var e = $(id); if (e.textContent !== text) e.textContent = text; }
window.dnwWait = function (s) {
  put('head', s.head); put('why', s.why); put('how', s.how);
  $('notice').hidden = !s.notice; put('notice', s.notice || '');
  busy = s.trying; $('retry').setAttribute('aria-disabled', busy ? 'true' : 'false');
  if (!busy) $('retry').classList.remove('pressed');
  clearInterval(tick);
  var left = s.next;
  var tail = function () {
    put('tries', s.tries + ' · ' + (busy ? tryingNow : left > 0 ? nextIn.replace('{0}', left) : waits));
    var w = $('tries').getBoundingClientRect().width;
    if (w > wide) { wide = w; $('tries').style.minWidth = w + 'px'; }
  };
  tail();
  if (!busy && left > 0) tick = setInterval(function () { if (left > 1) { left--; tail(); } }, 1000);
};
$('retry').onclick = function () { if (!busy && window.chrome && window.chrome.webview) { this.classList.add('pressed'); window.chrome.webview.postMessage('retry'); } };
addEventListener('resize', function () { wide = 0; $('tries').style.minWidth = ''; });
window.dnwWait(" + WaitingState() + @");
})();</script></body></html>";

        private static string Html(string text) => WebUtility.HtmlEncode(text);

        // The Bridge page's status line says the notice. The page's first load sets that line itself
        // (Loading..., then where the code comes from), so for 10 s the notice is said again each time
        // the line has settled, unless the page reported an error there (in red), which matters more.
        private static string NoticeScript(string notice) =>
            "(function (t) { var s = document.getElementById('status'); if (!s || !window.dnwStatus) return; window.dnwStatus(t);"
            + " var w, o = new MutationObserver(function () { if (s.textContent === t) return; clearTimeout(w); if (s.style.color) { o.disconnect(); return; }"
            + " w = setTimeout(function () { window.dnwStatus(t); }, 500); });"
            + " o.observe(s, { childList: true, characterData: true, subtree: true }); setTimeout(function () { o.disconnect(); clearTimeout(w); }, 10000); })(" + JsString(notice) + ")";
    }
}
