using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using DragNWash.ModFramework.ToolWindow;
using TW = DragNWash.ModFramework.ToolWindow.ToolWindow;

namespace DragNWash.ModFramework.Bridge
{
    // The Bridge library's BepInEx entry point: listens while [Bridge] Enabled
    // and the developer tools are both on, declares itself on the Mods screen
    // (an incoming connection on this computer is still a connection), and has
    // a tab in the F1 window and a console command.
    [BepInPlugin(Bridge.Guid, "DragNWash.ModFramework.Bridge", Bridge.Version)]
    [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(TW.Guid, BepInDependency.DependencyFlags.HardDependency)]
    internal sealed class BridgePlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static BridgeServer Server;
        private static bool? _runInBackground;   // the game's own setting, while the Bridge overrides it

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<int> _port;
        private ConfigEntry<string> _openIn;
        private string _note = "";
        private Vector2 _scroll;

        private void Awake()
        {
            Log = Logger;
            _enabled = Config.Bind("Bridge", "Enabled", false,
                new ConfigDescription("Let AI clients on this computer (Claude Code, VS Code, Cursor) read the running game over MCP. Only while the developer tools are on. Read-only.",
                    null, new SettingMeta { DisplayName = "Let AI clients read the game (MCP)" }));
            _port = Config.Bind("Bridge", "Port", 47821,
                new ConfigDescription("The port on 127.0.0.1 the Bridge listens on.", new AcceptableValueRange<int>(1024, 65535), new SettingMeta { Advanced = true }));

            _openIn = Config.Bind("Bridge", "OpenPageIn", "App",
                new ConfigDescription("Where the code graph opens: App (a window of its own, CodeGraph.exe, on Windows) or Browser. Without the app, and off Windows, it opens in the browser.",
                    new AcceptableValueList<string>("App", "Browser"), new SettingMeta { DisplayName = "Open the code graph in" }));

            ModFramework.Register(new ModInfo
            {
                Guid = Bridge.Guid,
                DisplayName = "Drag'n Wash ModFramework: Bridge",
                Description = "Lets AI clients on this computer read the running game (objects, values, the log, mods, saves, dialogue) over MCP. Read-only, off by default, only with the developer tools on. Experimental.",
                Authors = new[] { "TomXV" },
                Website = "https://github.com/TomXV/dragnwash-modframework",
                IsLibrary = true,
                Network = new[]
                {
                    new NetworkUse
                    {
                        Host = "127.0.0.1 (this computer only; incoming)",
                        Purpose = "When switched on, waits for AI clients on this computer (Claude Code and the like) and answers their questions about the running game. Nothing leaves the computer.",
                        Sends = "What the client asks for: objects and their values, the log, the mods, the saves, the dialogue. Only to programs on this computer that know the token.",
                        TurnOff = "Mods → Drag'n Wash ModFramework: Bridge → Settings → Let AI clients read the game (it is off unless you turn it on)",
                    },
                },
            });

            _enabled.SettingChanged += (s, e) => Apply();
            _port.SettingChanged += (s, e) => { Stop("the port changed"); Apply(); };
            DeveloperTools.Changed += Apply;

            TW.AddTab(Bridge.Guid, "Bridge", DrawTab, 150);
            RegisterPageOperation();
            TW.AddCommand(Bridge.Guid, "bridge", "bridge | bridge on | bridge off | bridge token new | bridge disconnect  (AI clients over MCP; read-only)", Command);
            Apply();
        }

        private void OnDestroy()
        {
            Stop("the Bridge was unloaded");
        }

        private bool Wanted => _enabled.Value && DeveloperTools.Enabled;

        private void Apply()
        {
            if (Wanted && Server == null) Listen();
            else if (!Wanted && Server != null) Stop(_enabled.Value ? "the developer tools were turned off" : "it was switched off");
        }

        // Not named Start: Unity calls a MonoBehaviour's Start() by itself after Awake.
        private void Listen()
        {
            try
            {
                string unused = BridgeToken.Value;   // made now, so the setup can be shown at once
                var server = new BridgeServer(_port.Value);
                server.Start();
                Server = server;
                // The game stops when its window is not in front, and a client (the
                // browser, above all) takes the front: keep answering while listening.
                if (_runInBackground == null) _runInBackground = Application.runInBackground;
                Application.runInBackground = true;
                _note = "";
                Log.LogInfo($"[bridge] Listening on http://127.0.0.1:{_port.Value}/mcp (read-only, this computer only).");
            }
            catch (Exception ex)
            {
                Server = null;
                _note = $"Could not listen on port {_port.Value}: {ex.Message}. Another program may use it; change [Bridge] Port.";
                Log.LogWarning("[bridge] " + _note);
            }
        }

        private static void Stop(string why)
        {
            if (Server == null) return;
            Server.Stop();
            Server = null;
            if (_runInBackground != null)
            {
                Application.runInBackground = _runInBackground.Value;
                _runInBackground = null;
            }
            McpProtocol.EndAll();
            PageDoor.EndAll();
            Log.LogInfo($"[bridge] Stopped: {why}.");
        }

        // Opens the page on this computer (docs/CODE_GRAPH.md) with a one-time
        // code after '#', which browsers never send to a server. A write
        // operation, so MCP never offers it; the Inspector's Graph button and
        // the console call it.
        private void RegisterPageOperation()
        {
            Operations.Register(Bridge.Guid, "bridge.page.open", "Opens the Bridge's page on this computer in the browser, signed in, optionally at a method or type of the game's code.", OperationKind.Write,
                "a line saying it opened",
                args =>
                {
                    if (Server == null) throw new InvalidOperationException(_enabled.Value ? "The Bridge is not listening (the developer tools are off, or the port is taken)." : "The Bridge is off: turn it on in the Bridge tab of the F1 window.");
                    string focus = args.String("focus");
                    if (OpenInApp(focus)) return "Opened the code graph in its window.";
                    string url = $"http://127.0.0.1:{_port.Value}/page#code={PageDoor.NewCode()}";
                    if (!string.IsNullOrEmpty(focus)) url += "&focus=" + Uri.EscapeDataString(focus);
                    Application.OpenURL(url);
                    return "Opened the page in the browser (the link works once, within a minute).";
                },
                Operations.Parameter("focus", OperationType.String, "What to show: m:<method id>, t:<type name>, or v:graphs for the graphs editor; the search when left out."));
            // The Inspector's Graph buttons and the console open the page; a graph
            // of a data mod has no business opening windows on this computer.
            Operation open = Operations.Find("bridge.page.open");
            if (open != null) open.Audience = OperationAudience.Console | OperationAudience.Page;
        }

        private void OpenPage(string focus)
        {
            var args = focus == null ? null : new Dictionary<string, object> { ["focus"] = focus };
            OperationResult opened = Operations.CallNow("bridge.page.open", args, "bridge tab");
            if (opened.Ok) TW.ShowNotice(Convert.ToString(opened.Value), NoticeKind.Info, 8f);
            else TW.ShowNotice(opened.Error, NoticeKind.Error);
        }

        // CodeGraph.exe, next to this DLL, on Windows: it signs in with the token by itself,
        // and a window already open takes the focus instead of a second one opening.
        private bool OpenInApp(string focus)
        {
            if (_openIn.Value != "App" || Application.platform != RuntimePlatform.WindowsPlayer) return false;
            string exe = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BridgePlugin).Assembly.Location) ?? "", "CodeGraph", "CodeGraph.exe");
            if (!System.IO.File.Exists(exe)) return false;
            try
            {
                string arguments = $"--from-game --port {_port.Value}" + (string.IsNullOrEmpty(focus) ? "" : " --focus \"" + focus.Replace("\"", "") + "\"");
                // Through the shell: a child started directly inherits the game's handles, the
                // Bridge's listening socket among them, and would keep the port after the game exits.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, arguments) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(exe) });
                return true;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[bridge] Could not start CodeGraph.exe ({ex.Message}); opening the browser instead.");
                return false;
            }
        }

        private const string NewTokenId = "bridge.token.new", DisconnectId = "bridge.disconnect";

        private static void NewToken(string disconnected)
        {
            BridgeToken.Renew();
            McpProtocol.EndAll();
            PageDoor.EndAll();
            TW.ShowNotice(disconnected == null
                ? "New token made; Copy setup has the new one."
                : $"New token made. {disconnected} {(disconnected.Contains(" and ") || disconnected.EndsWith(" clients") ? "were" : "was")} disconnected; Copy setup has the new one.", NoticeKind.Info, 8f);
        }

        // Who is connected, for the question and the notice: "claude-code",
        // "claude-code and the page", "3 clients and the page"; null for nobody.
        private static string Connected(List<McpProtocol.Session> sessions)
        {
            var names = new List<string>();
            foreach (McpProtocol.Session c in sessions)
            {
                string name = string.IsNullOrEmpty(c.Client) ? "a client" : c.Client;
                if (!names.Contains(name)) names.Add(name);
            }
            if (names.Count > 3)
            {
                names = new List<string> { sessions.Count + " clients" };
            }
            if (PageDoor.SignedIn > 0)
            {
                names.Add("the page");
            }
            if (names.Count == 0)
            {
                return null;
            }
            return names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
        }

        private string Setup => $"claude mcp add --transport http dragnwash http://127.0.0.1:{_port.Value}/mcp --header \"Authorization: Bearer {BridgeToken.Value}\"";

        private string Command(string[] args)
        {
            string what = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            switch (what)
            {
                case "on":
                    _enabled.Value = true;
                    return Server != null ? $"Listening on 127.0.0.1:{_port.Value}." : _note.Length > 0 ? _note : "On, but the developer tools are off.";
                case "off":
                    _enabled.Value = false;
                    return "The Bridge is off.";
                case "token":
                    if (args.Length > 1 && args[1].Equals("new", StringComparison.OrdinalIgnoreCase))
                    {
                        BridgeToken.Renew();
                        McpProtocol.EndAll();
                        PageDoor.EndAll();
                        return "New token made; every client was disconnected and needs the new setup (the Bridge tab shows it).";
                    }
                    return "bridge token new";
                case "disconnect":
                    int n = McpProtocol.AllSessions().Count;
                    McpProtocol.EndAll();
                    PageDoor.EndAll();
                    return $"Disconnected {n} client(s).";
            }
            var sb = new StringBuilder();
            sb.Append(Server != null ? $"Listening on http://127.0.0.1:{_port.Value}/mcp" : !_enabled.Value ? "Off ([Bridge] Enabled, or: bridge on)" : "On, but the developer tools are off");
            foreach (McpProtocol.Session s in McpProtocol.AllSessions())
            {
                sb.Append($"\n  {s.Client}: {s.Calls} call(s), last {s.LastUsed:HH:mm:ss}");
            }
            return sb.ToString();
        }

        private void DrawTab(Rect area)
        {
            var s = TW.Styles;
            TW.Fill(area, TW.InsetColor);
            float x = 12, w = area.width - 24, row = TW.RowHeight;
            float inner = w - 20;
            List<McpProtocol.Session> sessions = McpProtocol.AllSessions();
            List<McpProtocol.CallRecord> calls = McpProtocol.RecentCalls();
            float content = 360 + (sessions.Count + calls.Count) * 26 + (TW.IsConfirming(NewTokenId) || TW.IsConfirming(DisconnectId) ? row + 6 : 0);
            TW.ApplyScroll(area, ref _scroll);
            _scroll = GUI.BeginScrollView(area, _scroll, new Rect(0, 0, inner, Mathf.Max(area.height, content)), false, false);
            float y = 8;
            GUI.Label(new Rect(x, y, inner, 26), "BRIDGE: AI CLIENTS OVER MCP (READ-ONLY)", s.Label);
            y += 32;
            string status = Server != null ? $"Listening on http://127.0.0.1:{_port.Value}/mcp. Clients can read the game; nothing can be changed."
                : !_enabled.Value ? "Off. AI clients on this computer cannot reach the game."
                : "On, but the developer tools are off, so it is not listening.";
            GUI.Label(new Rect(x, y, inner, 44), status, s.WrappedLabel);
            y += 48;
            // The row wraps: six buttons do not fit a narrow window, and the
            // last of them was walking off the edge.
            float bx = x, by = y;
            bool Button(string label, float width, bool lit = false)
            {
                if (bx > x && bx + width > x + inner)
                {
                    bx = x;
                    by += row + 6;
                }
                bool pressed = GUI.Button(new Rect(bx, by, width, row), label, lit ? s.SelectedButton : s.Button);
                bx += width + 8;
                return pressed;
            }

            if (Button(Server != null || _enabled.Value ? "Turn off" : "Turn on", 120, _enabled.Value))
            {
                _enabled.Value = !_enabled.Value;
            }
            // The page is where graphs are made, so it needs a way in that does
            // not go through the game's code: the Inspector's Graph buttons open
            // it at a method, which is no help to somebody writing a graph.
            if (Button("Open page", 120))
            {
                OpenPage(null);
            }
            // The same page, on the editor: somebody writing a graph has no
            // reason to arrive at the game's code first.
            if (Button("Graphs", 120))
            {
                OpenPage("v:graphs");
            }
            if (Button("Copy setup", 130))
            {
                GUIUtility.systemCopyBuffer = Setup;
                TW.ShowNotice("The Claude Code setup command, with the token, is on the clipboard.", NoticeKind.Info, 8f);
            }
            // Both cut off whoever is connected, so they ask first - and only
            // then: with nobody connected there is nothing to lose.
            string who = Connected(sessions);
            if (Button("New token", 120, TW.IsConfirming(NewTokenId)))
            {
                if (who == null) NewToken(null);
                else TW.AskConfirm(NewTokenId);
            }
            if (Button("Disconnect all", 150, TW.IsConfirming(DisconnectId)))
            {
                if (who == null) TW.ShowNotice("No client is connected.", NoticeKind.Info, 6f);
                else TW.AskConfirm(DisconnectId);
            }
            y = by;
            y += row + 10;
            if (TW.IsConfirming(NewTokenId))
            {
                if (TW.Confirm(new Rect(x, y, inner, row), NewTokenId, who != null ? $"New token? Disconnects {who}." : "New token?", "Yes, new token",
                    "Yes makes the token; Cancel or 5 s keeps the old one. Esc = Cancel."))
                {
                    NewToken(who);
                }
                y += row + 6;
            }
            if (TW.IsConfirming(DisconnectId))
            {
                if (TW.Confirm(new Rect(x, y, inner, row), DisconnectId, who != null ? $"Disconnect {who}?" : "Disconnect every client?", "Yes, disconnect",
                    "Yes disconnects them; they can connect again with the same token. Cancel or 5 s keeps them. Esc = Cancel."))
                {
                    McpProtocol.EndAll();
                    PageDoor.EndAll();
                    TW.ShowNotice(who != null ? $"Disconnected {who}." : "Disconnected every client.", NoticeKind.Info, 8f);
                }
                y += row + 6;
            }
            if (_note.Length > 0)
            {
                GUI.Label(new Rect(x, y, inner, 44), _note, s.WrappedLabel);
                y += 48;
            }
            GUI.Label(new Rect(x, y, inner, 60), "Setup for Claude Code (Copy setup puts it on the clipboard with the token): claude mcp add --transport http dragnwash http://127.0.0.1:" + _port.Value + "/mcp --header \"Authorization: Bearer <token>\". VS Code and Cursor take the same URL and header. The token is kept in your user profile, not in the game folder.", s.WrappedLabel);
            y += 72;
            GUI.Label(new Rect(x, y, inner, 26), $"CLIENTS ({sessions.Count})" + (PageDoor.SignedIn > 0 ? $"   PAGE: {PageDoor.SignedIn} signed in" : ""), s.Label);
            y += 28;
            if (sessions.Count == 0)
            {
                GUI.Label(new Rect(x, y, inner, 26), "None connected.", s.MutedLabel);
                y += 26;
            }
            foreach (McpProtocol.Session c in sessions)
            {
                GUI.Label(new Rect(x, y, inner, 26), $"{c.Client}  (MCP {c.Version}), {c.Calls} call(s), since {c.Started:HH:mm:ss}, last {c.LastUsed:HH:mm:ss}", s.MutedLabel);
                y += 26;
            }
            y += 8;
            GUI.Label(new Rect(x, y, inner, 26), "LAST CALLS", s.Label);
            y += 28;
            if (calls.Count == 0)
            {
                GUI.Label(new Rect(x, y, inner, 26), "None yet.", s.MutedLabel);
            }
            for (int i = calls.Count - 1; i >= 0; i--)
            {
                McpProtocol.CallRecord c = calls[i];
                GUI.Label(new Rect(x, y, inner, 26), $"{c.Time:HH:mm:ss}  {c.Client}: {c.Tool}{(c.Ok ? "" : "  (failed)")}", s.MutedLabel);
                y += 26;
            }
            GUI.EndScrollView();
        }
    }
}
