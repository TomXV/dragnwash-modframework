using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using DragNWash.ModFramework.Overrides;

namespace DragNWash.ModFramework.Bridge
{
    internal sealed class McpAnswer
    {
        public int Status;
        public string Body;
        public string SessionId;
    }

    // The Model Context Protocol over the Bridge (Streamable HTTP, JSON answers
    // only): initialize, notifications, ping, tools/list and tools/call, with
    // the registry's read operations as the tools. Runs on the server's threads;
    // operations themselves run on the main thread through Operations.Call.
    internal static class McpProtocol
    {
        internal static readonly string[] Versions = { "2025-06-18", "2025-03-26" };
        private const int MaxSessions = 4;
        private static readonly TimeSpan IdleEnd = TimeSpan.FromMinutes(30);
        private const int CallsPerSecond = 20;
        private const int CallTimeoutMs = 10000;

        internal sealed class Session
        {
            public string Id;
            public string Client = "client";
            public string Version;
            public DateTime Started = DateTime.Now;
            public DateTime LastUsed = DateTime.Now;
            public readonly Queue<DateTime> Recent = new Queue<DateTime>();
            public int Calls;
        }

        internal sealed class CallRecord
        {
            public DateTime Time;
            public string Client;
            public string Tool;
            public bool Ok;
        }

        private static readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        private static readonly List<CallRecord> LastCalls = new List<CallRecord>();

        internal static List<Session> AllSessions()
        {
            lock (Sessions) return Sessions.Values.ToList();
        }

        internal static List<CallRecord> RecentCalls()
        {
            lock (LastCalls) return LastCalls.ToList();
        }

        internal static void EndAll()
        {
            lock (Sessions) Sessions.Clear();
        }

        internal static bool EndSession(string id)
        {
            if (id == null) return false;
            lock (Sessions) return Sessions.Remove(id);
        }

        // MCP tool names are letters, digits, '_' and '-'.
        internal static string ToolName(string operation) => operation.Replace('.', '_');

        internal static McpAnswer Handle(string body, string sessionId, string protocolVersion)
        {
            Dictionary<string, object> message;
            try
            {
                message = Json.Parse(body) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                return Error(400, null, -32700, "Not JSON: " + ex.Message);
            }
            if (message == null) return Error(400, null, -32600, "One JSON-RPC message per request.");
            message.TryGetValue("id", out object id);
            string method = Json.String(message, "method");
            bool isRequest = method != null && message.ContainsKey("id");

            if (protocolVersion != null && !Versions.Contains(protocolVersion))
            {
                return Error(400, id, -32600, $"MCP-Protocol-Version {protocolVersion} is not supported; the Bridge speaks {string.Join(", ", Versions)}.");
            }
            if (method == "initialize")
            {
                return Initialize(id, message);
            }

            Session session;
            lock (Sessions)
            {
                ExpireIdle();
                if (sessionId == null) return Error(400, id, -32600, "Send the Mcp-Session-Id from initialize.");
                if (!Sessions.TryGetValue(sessionId, out session)) return new McpAnswer { Status = 404 };
                session.LastUsed = DateTime.Now;
            }
            if (!isRequest)
            {
                return new McpAnswer { Status = 202 };   // a notification, or a response to a request the Bridge never sends
            }
            switch (method)
            {
                case "ping":
                    return Result(id, new Dictionary<string, object>());
                case "tools/list":
                    return Result(id, new Dictionary<string, object> { ["tools"] = Tools() });
                case "tools/call":
                    return Call(id, message, session);
            }
            return Error(200, id, -32601, $"The Bridge does not answer {method}.");
        }

        private static McpAnswer Initialize(object id, Dictionary<string, object> message)
        {
            var p = message.TryGetValue("params", out object raw) ? raw as Dictionary<string, object> : null;
            string asked = Json.String(p, "protocolVersion");
            string client = p != null && p.TryGetValue("clientInfo", out object info) ? Json.String(info as Dictionary<string, object>, "name") : null;
            var session = new Session
            {
                Id = NewId(),
                Client = string.IsNullOrEmpty(client) ? "client" : client,
                Version = Versions.Contains(asked) ? asked : Versions[0],
            };
            lock (Sessions)
            {
                ExpireIdle();
                while (Sessions.Count >= MaxSessions)
                {
                    Sessions.Remove(Sessions.Values.OrderBy(s => s.LastUsed).First().Id);
                }
                Sessions[session.Id] = session;
            }
            BridgePlugin.Log.LogInfo($"[bridge] {session.Client} connected (MCP {session.Version}).");
            McpAnswer answer = Result(id, new Dictionary<string, object>
            {
                ["protocolVersion"] = session.Version,
                ["capabilities"] = new Dictionary<string, object> { ["tools"] = new Dictionary<string, object> { ["listChanged"] = false } },
                ["serverInfo"] = new Dictionary<string, object> { ["name"] = "Drag'n Wash ModFramework", ["version"] = Bridge.Version },
                ["instructions"] = "Reads the running game Drag'n Wash: objects and their values, the log, mods, saves, dialogue and text. Read-only: nothing can be changed through these tools.",
            });
            answer.SessionId = session.Id;
            return answer;
        }

        private static List<object> Tools()
        {
            var tools = new List<object>();
            foreach (Operation op in Operations.All.Where(o => o.Kind == OperationKind.Read))
            {
                var properties = new Dictionary<string, object>();
                var required = new List<object>();
                foreach (OperationParameter p in op.Parameters)
                {
                    var schema = new Dictionary<string, object>
                    {
                        ["type"] = p.Type == OperationType.Number ? "number" : p.Type == OperationType.Boolean ? "boolean" : "string",
                        ["description"] = p.Description ?? "",
                    };
                    if (p.Choices != null) schema["enum"] = p.Choices.Cast<object>().ToList();
                    properties[p.Name] = schema;
                    if (p.Required) required.Add(p.Name);
                }
                var input = new Dictionary<string, object> { ["type"] = "object", ["properties"] = properties };
                if (required.Count > 0) input["required"] = required;
                tools.Add(new Dictionary<string, object>
                {
                    ["name"] = ToolName(op.Name),
                    ["title"] = op.Name,
                    ["description"] = string.IsNullOrEmpty(op.Returns) ? op.Description : $"{op.Description} Returns {op.Returns}.",
                    ["inputSchema"] = input,
                    ["annotations"] = new Dictionary<string, object> { ["title"] = op.Name, ["readOnlyHint"] = true, ["openWorldHint"] = false },
                });
            }
            return tools;
        }

        private static McpAnswer Call(object id, Dictionary<string, object> message, Session session)
        {
            lock (session.Recent)
            {
                DateTime now = DateTime.Now;
                while (session.Recent.Count > 0 && (now - session.Recent.Peek()).TotalSeconds > 1) session.Recent.Dequeue();
                if (session.Recent.Count >= CallsPerSecond) return Error(429, id, -32000, $"More than {CallsPerSecond} calls a second; slow down.");
                session.Recent.Enqueue(now);
            }
            var p = message.TryGetValue("params", out object raw) ? raw as Dictionary<string, object> : null;
            string tool = Json.String(p, "name");
            Operation op = Operations.All.FirstOrDefault(o => o.Kind == OperationKind.Read && ToolName(o.Name) == tool);
            if (op == null) return Error(200, id, -32602, $"Unknown tool: {tool}.");
            var args = p != null && p.TryGetValue("arguments", out object a) && a is Dictionary<string, object> d ? d : new Dictionary<string, object>();

            OperationResult result = null;
            var done = new ManualResetEvent(false);
            Operations.Call(op.Name, args, "mcp:" + session.Client, r => { result = r; done.Set(); });
            bool inTime = done.WaitOne(CallTimeoutMs);
            session.Calls++;
            Record(session.Client, tool, inTime && result.Ok);
            if (!inTime) return ToolResult(id, "The game did not answer within 10 seconds (it may be loading or paused in a way that stops frames).", null, true);
            if (!result.Ok) return ToolResult(id, result.Error, null, true);
            object structured = result.Value is IDictionary<string, object> ? result.Value : new Dictionary<string, object> { ["items"] = result.Value };
            return ToolResult(id, Operations.ToJson(result.Value), structured, false);
        }

        private static void Record(string client, string tool, bool ok)
        {
            lock (LastCalls)
            {
                LastCalls.Add(new CallRecord { Time = DateTime.Now, Client = client, Tool = tool, Ok = ok });
                if (LastCalls.Count > 20) LastCalls.RemoveAt(0);
            }
        }

        private static McpAnswer ToolResult(object id, string text, object structured, bool isError)
        {
            var result = new Dictionary<string, object>
            {
                ["content"] = new List<object> { new Dictionary<string, object> { ["type"] = "text", ["text"] = text ?? "" } },
                ["isError"] = isError,
            };
            if (structured != null) result["structuredContent"] = structured;
            return Result(id, result);
        }

        private static McpAnswer Result(object id, object result)
        {
            return new McpAnswer
            {
                Status = 200,
                Body = Operations.ToJson(new Dictionary<string, object> { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }),
            };
        }

        private static McpAnswer Error(int status, object id, int code, string text)
        {
            return new McpAnswer
            {
                Status = status,
                Body = Operations.ToJson(new Dictionary<string, object>
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["error"] = new Dictionary<string, object> { ["code"] = code, ["message"] = text },
                }),
            };
        }

        private static void ExpireIdle()
        {
            DateTime now = DateTime.Now;
            foreach (string gone in Sessions.Values.Where(s => now - s.LastUsed > IdleEnd).Select(s => s.Id).ToList())
            {
                Sessions.Remove(gone);
            }
        }

        private static string NewId()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
