using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
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

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<int> _port;
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
            if (Wanted && Server == null) Start();
            else if (!Wanted && Server != null) Stop(_enabled.Value ? "the developer tools were turned off" : "it was switched off");
        }

        private void Start()
        {
            try
            {
                string unused = BridgeToken.Value;   // made now, so the setup can be shown at once
                var server = new BridgeServer(_port.Value);
                server.Start();
                Server = server;
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
            McpProtocol.EndAll();
            Log.LogInfo($"[bridge] Stopped: {why}.");
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
                        return "New token made; every client was disconnected and needs the new setup (the Bridge tab shows it).";
                    }
                    return "bridge token new";
                case "disconnect":
                    int n = McpProtocol.AllSessions().Count;
                    McpProtocol.EndAll();
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
            float content = 360 + (sessions.Count + calls.Count) * 26;
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
            float bx = x;
            if (GUI.Button(new Rect(bx, y, 120, row), Server != null || _enabled.Value ? "Turn off" : "Turn on", _enabled.Value ? s.SelectedButton : s.Button))
            {
                _enabled.Value = !_enabled.Value;
            }
            bx += 128;
            if (GUI.Button(new Rect(bx, y, 130, row), "Copy setup", s.Button))
            {
                GUIUtility.systemCopyBuffer = Setup;
                TW.ShowNotice("The Claude Code setup command, with the token, is on the clipboard.");
            }
            bx += 138;
            if (GUI.Button(new Rect(bx, y, 120, row), "New token", s.Button))
            {
                BridgeToken.Renew();
                McpProtocol.EndAll();
                TW.ShowNotice("New token made; every client was disconnected and needs the new setup.");
            }
            bx += 128;
            if (GUI.Button(new Rect(bx, y, 150, row), "Disconnect all", s.Button))
            {
                McpProtocol.EndAll();
            }
            y += row + 10;
            if (_note.Length > 0)
            {
                GUI.Label(new Rect(x, y, inner, 44), _note, s.WrappedLabel);
                y += 48;
            }
            GUI.Label(new Rect(x, y, inner, 60), "Setup for Claude Code (Copy setup puts it on the clipboard with the token): claude mcp add --transport http dragnwash http://127.0.0.1:" + _port.Value + "/mcp --header \"Authorization: Bearer <token>\". VS Code and Cursor take the same URL and header. The token is kept in your user profile, not in the game folder.", s.WrappedLabel);
            y += 72;
            GUI.Label(new Rect(x, y, inner, 26), $"CLIENTS ({sessions.Count})", s.Label);
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
