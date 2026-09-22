using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using DragNWash.ModFramework.CodeGraph;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DragNWash.CodeGraph.Standalone
{
    // The window: the Bridge's page in WebView2, at an address that exists only
    // inside this window. WebView2 hands every request for it to this process
    // (WebResourceRequested), which answers the page and its calls from the
    // opened assemblies: no port is opened, nothing reaches the network, and no
    // other program or page can call it.
    internal sealed class ViewerForm : Form
    {
        private const string Origin = "https://codegraph.example";
        private static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragNWash ModFramework", "CodeGraphStandalone");
        private const string Headers = "Content-Type: {0}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer";
        private const string PagePolicy = "\r\nContent-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

        private readonly WebView2 _view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(14, 18, 26) };
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly bool _all;
        private IList<string> _paths;
        private Opened _opened;
        private string _focus;
        private string _error;
        private string _hostScript;
        private string _html;

        internal ViewerForm(IList<string> paths, string focus, bool all)
        {
            _paths = paths;
            _focus = focus;
            _all = all;
            Text = "Code Graph";
            BackColor = Color.FromArgb(14, 18, 26);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(area.X + area.Width / 10, area.Y + area.Height / 10, area.Width * 8 / 10, area.Height * 8 / 10);
            Controls.Add(_view);
            Load += async (s, e) => await Start();
            HandleCreated += (s, e) => WindowChrome.DarkTitleBar(this);
        }

        private async Task Start()
        {
            try
            {
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Home, "WebView2"));
                await _view.EnsureCoreWebView2Async(env);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(this, "This window needs the Microsoft Edge WebView2 Runtime, which comes with Windows 10 and 11 but is missing here.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                return;
            }
            CoreWebView2 core = _view.CoreWebView2;
            WindowChrome.LockDown(core.Settings);
            core.Settings.IsWebMessageEnabled = true;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += (s, e) => e.Handled = true;
            core.WebMessageReceived += OnMessage;
            core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnRequest;
            await OpenPaths(_paths);
        }

        // ---- opening -----------------------------------------------------------------

        private async Task OpenPaths(IList<string> paths)
        {
            // Read again keeps the method shown; other assemblies start empty.
            if (_opened != null && (_paths == null || !paths.SequenceEqual(_paths))) _focus = null;
            _paths = paths;
            _error = null;
            if (paths != null && paths.Count > 0)
            {
                Text = "Code Graph: reading…";
                UseWaitCursor = true;
                try
                {
                    _opened = await Task.Run(() => Opened.Open(paths, _all));
                }
                catch (Exception ex)
                {
                    _error = ex.Message;
                }
                UseWaitCursor = false;
            }
            Text = _opened == null ? "Code Graph" : "Code Graph: " + _opened.Label;
            await ShowPage();
        }

        // The page, with this app's words (window.dnwHost, set before the page's script runs).
        private async Task ShowPage()
        {
            CoreWebView2 core = _view.CoreWebView2;
            if (_hostScript != null) core.RemoveScriptToExecuteOnDocumentCreated(_hostScript);
            string source = _opened == null
                ? "Nothing open yet."
                : $"Structure only: read from {_opened.Label} ({_opened.Model.Methods.Count} methods, {_opened.Ms} ms) on this computer. Nothing is sent anywhere.";
            if (_error != null) source = _error;
            else if (_opened != null && _opened.NotDotNet.Count > 0) source += $" Not .NET, left out: {_opened.NotDotNet.Count}.";
            var host = new Dictionary<string, object>
            {
                ["name"] = "",
                ["source"] = source,
                ["hint"] = "Open a .NET assembly with Open… above, or drop a DLL on this window. A folder, or a Unity game's folder built with Mono, opens every assembly in it that is not .NET's or Unity's own.",
                ["open"] = true,
            };
            _hostScript = await core.AddScriptToExecuteOnDocumentCreatedAsync("window.dnwHost = " + _json.Serialize(host) + ";" + DropScript);
            core.Navigate(Origin + "/page" + (_focus != null ? "#focus=" + Uri.EscapeDataString(_focus) : ""));
        }

        // Files dropped on the page go to this app as WebView2 file objects, which carry their paths.
        private const string DropScript = @"
addEventListener('dragover', e => { if (e.dataTransfer && e.dataTransfer.types.includes('Files')) e.preventDefault(); });
addEventListener('drop', e => {
  if (!e.dataTransfer || !e.dataTransfer.files.length) return;
  e.preventDefault();
  chrome.webview.postMessageWithAdditionalObjects('drop', e.dataTransfer.files);
});";

        private async void ChooseFiles()
        {
            using (var dialog = new OpenFileDialog { Title = "Open .NET assemblies", Filter = ".NET assemblies (*.dll;*.exe)|*.dll;*.exe|All files (*.*)|*.*", Multiselect = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) await OpenPaths(dialog.FileNames);
            }
        }

        private async void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "A folder of assemblies, or a Unity game's folder (built with Mono).", ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) await OpenPaths(new[] { dialog.SelectedPath });
            }
        }

        // ---- the page's requests -----------------------------------------------------

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri.StartsWith(Origin + "/page", StringComparison.Ordinal)) return;
            e.Cancel = true;
            // A file dropped where the page did not take it: WebView2 would show it; open it instead.
            if (e.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string path = new Uri(e.Uri).LocalPath;
                    BeginInvoke((Action)(async () => await OpenPaths(new[] { path })));
                }
                catch
                {
                }
            }
        }

        private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!e.Source.StartsWith(Origin + "/page", StringComparison.Ordinal)) return;
            string message;
            try { message = e.TryGetWebMessageAsString(); } catch { return; }
            if (message.StartsWith("focus:", StringComparison.Ordinal)) _focus = message.Substring(6);
            else if (message == "drop")
            {
                List<string> paths = e.AdditionalObjects.OfType<CoreWebView2File>().Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
                if (paths.Count > 0) BeginInvoke((Action)(async () => await OpenPaths(paths)));
            }
            else if (message == "open")
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add("Assemblies…", null, (s, a) => ChooseFiles());
                menu.Items.Add("Folder…", null, (s, a) => ChooseFolder());
                if (_paths != null && _paths.Count > 0) menu.Items.Add("Read again", null, async (s, a) => await OpenPaths(_paths));
                menu.Show(Cursor.Position);
            }
        }

        private void OnRequest(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            CoreWebView2Environment env = _view.CoreWebView2.Environment;
            var uri = new Uri(e.Request.Uri);
            string status = "OK", type = "text/plain; charset=utf-8", body;
            int code = 200;
            if (uri.AbsolutePath == "/page" && e.Request.Method == "GET")
            {
                type = "text/html; charset=utf-8";
                body = Page();
                e.Response = env.CreateWebResourceResponse(Stream(body), code, status, string.Format(Headers, type) + PagePolicy);
                return;
            }
            if (uri.AbsolutePath == "/page/api/op" && e.Request.Method == "POST")
            {
                type = "application/json; charset=utf-8";
                body = _json.Serialize(Call(Read(e.Request.Content)));
            }
            else
            {
                code = 404;
                status = "Not Found";
                body = "Not here.";
            }
            e.Response = env.CreateWebResourceResponse(Stream(body), code, status, string.Format(Headers, type));
        }

        // The page's operations, as the Inspector registers them for the game (code.graph, code.type,
        // code.callers, code.search, code.stats), answered as the Bridge answers them: { ok, value | error }.
        private Dictionary<string, object> Call(string request)
        {
            try
            {
                var call = _json.DeserializeObject(request) as Dictionary<string, object>;
                string name = call != null && call.TryGetValue("name", out object n) ? n as string : null;
                var args = call != null && call.TryGetValue("args", out object a) ? a as Dictionary<string, object> : null;
                args = args ?? new Dictionary<string, object>();
                string Arg(string key) => args.TryGetValue(key, out object v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null;
                if (_opened == null) throw new InvalidOperationException(_error ?? "Nothing open yet: Open… a .NET assembly first.");
                CodeGraphModel model = _opened.Model;
                object value;
                switch (name)
                {
                    case "code.graph": value = model.Graph(Arg("method"), Arg("stub") == "True" || Arg("stub") == "true"); break;
                    case "code.type": value = model.TypeGraph(Arg("type")); break;
                    case "code.callers": value = model.CallersOf(CodeGraphModel.Id(model.FindMethod(Arg("method")))); break;
                    case "code.search":
                        int max = int.TryParse(Arg("max"), out int m) ? m : 50;
                        value = model.Search(Arg("text"), Math.Max(1, Math.Min(200, max)));
                        break;
                    case "code.stats":
                        value = new Dictionary<string, object>
                        {
                            ["assemblies"] = model.Assemblies.Select(x => (object)x.Name.Name).ToList(),
                            ["methods"] = model.Methods.Count,
                            ["index_ms"] = model.BuildMs,
                        };
                        break;
                    default: throw new InvalidOperationException($"No read operation named {name}.");
                }
                return new Dictionary<string, object> { ["ok"] = true, ["value"] = value };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = ex.Message };
            }
        }

        private string Page()
        {
            if (_html != null) return _html;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("page.html"))
            using (var reader = new StreamReader(s, Encoding.UTF8))
            {
                return _html = reader.ReadToEnd();
            }
        }

        private static string Read(Stream content)
        {
            if (content == null) return "";
            using (var reader = new StreamReader(content, Encoding.UTF8)) return reader.ReadToEnd();
        }

        private static Stream Stream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
