using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DragNWash.ModFramework.Bridge
{
    // A small HTTP/1.1 server on the loopback address: one endpoint, /mcp.
    // Every request is checked in this order: Host (DNS rebinding), Origin
    // (web pages), the token, the path, the method; then the MCP layer answers.
    // TcpListener rather than HttpListener, whose Mono version differs between
    // Windows and Proton. One request per connection (Connection: close).
    internal sealed class BridgeServer
    {
        private const int MaxConnections = 8;
        private const int MaxBody = 1024 * 1024;
        private const int MaxHeaderLines = 100;

        private readonly int _port;
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _stopping;
        private int _open;

        internal BridgeServer(int port)
        {
            _port = port;
        }

        internal int Port => _port;

        internal void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "DragNWash Bridge" };
            _thread.Start();
        }

        internal void Stop()
        {
            _stopping = true;
            try { _listener?.Stop(); } catch { }
        }

        private void AcceptLoop()
        {
            while (!_stopping)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch
                {
                    if (_stopping) return;
                    continue;
                }
                if (Interlocked.Increment(ref _open) > MaxConnections)
                {
                    Interlocked.Decrement(ref _open);
                    try { using (client) Respond(client.GetStream(), 503, "text/plain", "Too many connections.", null); } catch { }
                    continue;
                }
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Serve(client); }
                    finally { Interlocked.Decrement(ref _open); }
                });
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 10000;
                    client.SendTimeout = 10000;
                    NetworkStream stream = client.GetStream();
                    var reader = new StreamReader(stream, new UTF8Encoding(false), false, 8192, true);
                    string requestLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(requestLine)) return;
                    string[] parts = requestLine.Split(' ');
                    if (parts.Length < 3) { Respond(stream, 400, "text/plain", "Bad request line.", null); return; }
                    string method = parts[0];
                    string path = parts[1];
                    int query = path.IndexOf('?');
                    if (query >= 0) path = path.Substring(0, query);

                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string line;
                    int count = 0;
                    while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                    {
                        if (++count > MaxHeaderLines) { Respond(stream, 431, "text/plain", "Too many headers.", null); return; }
                        int colon = line.IndexOf(':');
                        if (colon > 0) headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                    }

                    // 1. Host: a site pointing its own name at 127.0.0.1 sends its own name.
                    headers.TryGetValue("Host", out string host);
                    if (host != $"127.0.0.1:{_port}" && host != $"localhost:{_port}")
                    {
                        Respond(stream, 403, "text/plain", "Only this computer's address may call the Bridge.", null);
                        return;
                    }
                    // 2. Origin: only browsers send one; no web page may call.
                    if (headers.TryGetValue("Origin", out string origin) && origin != "null")
                    {
                        Respond(stream, 403, "text/plain", "Web pages may not call the Bridge.", null);
                        return;
                    }
                    // 3. The token.
                    headers.TryGetValue("Authorization", out string auth);
                    string given = auth != null && auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth.Substring(7).Trim() : null;
                    if (!BridgeToken.Matches(given))
                    {
                        Respond(stream, 401, "text/plain", "The Bridge needs its token: Authorization: Bearer <token> (the Bridge tab in the F1 window shows the setup).", null);
                        return;
                    }
                    if (path != "/mcp")
                    {
                        Respond(stream, 404, "text/plain", "The Bridge answers at /mcp.", null);
                        return;
                    }

                    headers.TryGetValue("Mcp-Session-Id", out string sessionId);
                    headers.TryGetValue("MCP-Protocol-Version", out string protocolVersion);
                    switch (method)
                    {
                        case "POST":
                        {
                            headers.TryGetValue("Content-Length", out string lengthText);
                            if (!int.TryParse(lengthText, out int length) || length < 0) { Respond(stream, 411, "text/plain", "Content-Length is required.", null); return; }
                            if (length > MaxBody) { Respond(stream, 413, "text/plain", "The request is larger than 1 MB.", null); return; }
                            var body = new char[length];
                            int read = 0;
                            while (read < length)
                            {
                                int got = reader.Read(body, read, length - read);
                                if (got <= 0) break;
                                read += got;
                            }
                            McpAnswer answer = McpProtocol.Handle(new string(body, 0, read), sessionId, protocolVersion);
                            Respond(stream, answer.Status, answer.Body != null ? "application/json" : null, answer.Body, answer.SessionId);
                            return;
                        }
                        case "DELETE":
                            Respond(stream, McpProtocol.EndSession(sessionId) ? 200 : 404, null, null, null);
                            return;
                        default:
                            // GET would open a stream for the server to call the client; the Bridge never does.
                            Respond(stream, 405, "text/plain", "The Bridge answers POST (and DELETE to end a session).", null, "Allow: POST, DELETE");
                            return;
                    }
                }
                catch (IOException)
                {
                    // The client went away.
                }
                catch (Exception ex)
                {
                    BridgePlugin.Log.LogWarning($"[bridge] A request failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static void Respond(Stream stream, int status, string type, string body, string sessionId, string extraHeader = null)
        {
            byte[] bytes = body == null ? new byte[0] : Encoding.UTF8.GetBytes(body);
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
            if (type != null) sb.Append("Content-Type: ").Append(type).Append("; charset=utf-8\r\n");
            if (sessionId != null) sb.Append("Mcp-Session-Id: ").Append(sessionId).Append("\r\n");
            if (extraHeader != null) sb.Append(extraHeader).Append("\r\n");
            sb.Append("Content-Length: ").Append(bytes.Length).Append("\r\nConnection: close\r\n\r\n");
            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(head, 0, head.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 411: return "Length Required";
                case 413: return "Payload Too Large";
                case 429: return "Too Many Requests";
                case 431: return "Request Header Fields Too Large";
                case 503: return "Service Unavailable";
                default: return "Error";
            }
        }
    }
}
