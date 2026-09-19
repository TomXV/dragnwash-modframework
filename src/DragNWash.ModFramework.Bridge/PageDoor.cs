using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using DragNWash.ModFramework.Overrides;

namespace DragNWash.ModFramework.Bridge
{
    internal sealed class PageAnswer
    {
        public int Status;
        public string Type;
        public string Body;
        public string Headers;
    }

    // The page's door (docs/CODE_GRAPH.md), apart from MCP's: the page is a web
    // page from the Bridge's own address, which MCP's door refuses. The F1
    // window's button makes a one-time code and opens the page with it after
    // '#'; the page trades it for a cookie (HttpOnly, SameSite=Strict); every
    // call after that needs the cookie and an Origin equal to the Bridge's own.
    internal static class PageDoor
    {
        private const string Cookie = "dnw_page";
        private static readonly TimeSpan CodeLife = TimeSpan.FromSeconds(60);
        private const int MaxSessions = 8;
        private const int CallsPerSecond = 40;
        private const int CallTimeoutMs = 10000;

        private static readonly Dictionary<string, DateTime> Codes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly List<string> Sessions = new List<string>();
        private static readonly Queue<DateTime> Recent = new Queue<DateTime>();
        private static string _html;

        private const string Common = "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nX-Frame-Options: DENY";
        private const string PagePolicy = "Content-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

        internal static int SignedIn
        {
            get { lock (Sessions) return Sessions.Count; }
        }

        // A code for one sign-in, good for 60 seconds.
        internal static string NewCode()
        {
            string code = Random(32);
            lock (Codes)
            {
                foreach (string old in Codes.Where(kv => kv.Value < DateTime.Now).Select(kv => kv.Key).ToList()) Codes.Remove(old);
                Codes[code] = DateTime.Now + CodeLife;
            }
            return code;
        }

        // New token, Disconnect all, the Bridge turned off: the page signs in again.
        internal static void EndAll()
        {
            lock (Codes) Codes.Clear();
            lock (Sessions) Sessions.Clear();
        }

        internal static PageAnswer Handle(string method, string path, Dictionary<string, string> headers, string body, int port)
        {
            if (path == "/page" || path == "/page/")
            {
                if (method != "GET") return Text(405, "The page is read with GET.");
                return new PageAnswer { Status = 200, Type = "text/html", Body = Html(), Headers = Common + "\r\n" + PagePolicy };
            }
            if (method != "POST") return Text(405, "The page's calls are POST.");
            // The Code Graph app (CodeGraph.exe) signs in with the Bridge's token instead of a
            // button press. Programs send no Origin; a browser always does on POST, so no web
            // page can use this door even if it knew the token.
            if (path == "/page/api/code")
            {
                if (headers.ContainsKey("Origin")) return Text(403, "Web pages may not ask for a sign-in code.");
                headers.TryGetValue("Authorization", out string auth);
                string given = auth != null && auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth.Substring(7).Trim() : null;
                if (!BridgeToken.Matches(given)) return Text(401, "The Bridge needs its token.");
                return JsonAnswer(200, new Dictionary<string, object> { ["code"] = NewCode() });
            }
            // Only the page itself: a browser sends the page's own address as Origin.
            headers.TryGetValue("Origin", out string origin);
            headers.TryGetValue("Host", out string host);
            if (origin == null || origin != "http://" + host) return Text(403, "Only the Bridge's own page may call here.");

            if (path == "/page/api/login") return Login(body);
            if (!SignedInWith(headers)) return JsonAnswer(401, new Dictionary<string, object> { ["ok"] = false, ["error"] = "Not signed in: open the page again from the F1 window (Inspector → Code → Graph)." });
            if (path == "/page/api/op") return Call(body);
            return Text(404, "No such call.");
        }

        private static PageAnswer Login(string body)
        {
            string code = null;
            try { code = Json.String(Json.Parse(body) as Dictionary<string, object>, "code"); } catch { }
            bool ok = false;
            if (!string.IsNullOrEmpty(code))
            {
                lock (Codes)
                {
                    // Compared in constant time against each live code; a code is spent on first use.
                    string match = Codes.Keys.FirstOrDefault(k => Same(k, code));
                    if (match != null)
                    {
                        ok = Codes[match] >= DateTime.Now;
                        Codes.Remove(match);
                    }
                }
            }
            if (!ok) return JsonAnswer(403, new Dictionary<string, object> { ["ok"] = false, ["error"] = "That link has been used or is older than a minute: open the page again from the F1 window." });
            string session = Random(32);
            lock (Sessions)
            {
                Sessions.Add(session);
                while (Sessions.Count > MaxSessions) Sessions.RemoveAt(0);
            }
            BridgePlugin.Log.LogInfo("[bridge] The page signed in.");
            PageAnswer answer = JsonAnswer(200, new Dictionary<string, object> { ["ok"] = true });
            answer.Headers += $"\r\nSet-Cookie: {Cookie}={session}; Path=/page; HttpOnly; SameSite=Strict";
            return answer;
        }

        private static bool SignedInWith(Dictionary<string, string> headers)
        {
            if (!headers.TryGetValue("Cookie", out string cookies)) return false;
            foreach (string part in cookies.Split(';'))
            {
                string p = part.Trim();
                if (!p.StartsWith(Cookie + "=", StringComparison.Ordinal)) continue;
                string value = p.Substring(Cookie.Length + 1);
                lock (Sessions)
                {
                    if (Sessions.Any(s => Same(s, value))) return true;
                }
            }
            return false;
        }

        private static PageAnswer Call(string body)
        {
            lock (Recent)
            {
                DateTime now = DateTime.Now;
                while (Recent.Count > 0 && (now - Recent.Peek()).TotalSeconds > 1) Recent.Dequeue();
                if (Recent.Count >= CallsPerSecond) return JsonAnswer(429, new Dictionary<string, object> { ["ok"] = false, ["error"] = "Too many calls a second." });
                Recent.Enqueue(now);
            }
            Dictionary<string, object> request;
            try { request = Json.Parse(body) as Dictionary<string, object>; }
            catch (Exception ex) { return JsonAnswer(400, new Dictionary<string, object> { ["ok"] = false, ["error"] = "Not JSON: " + ex.Message }); }
            string name = Json.String(request, "name");
            Operation op = Operations.Find(name);
            // The page reads: any read operation, the page-only ones included; nothing that writes.
            if (op == null || op.Kind != OperationKind.Read) return JsonAnswer(200, new Dictionary<string, object> { ["ok"] = false, ["error"] = $"No read operation named {name}." });
            var args = request != null && request.TryGetValue("args", out object a) && a is Dictionary<string, object> d ? d : new Dictionary<string, object>();
            OperationResult result = null;
            var done = new ManualResetEvent(false);
            Operations.Call(op.Name, args, "page", r => { result = r; done.Set(); });
            if (!done.WaitOne(CallTimeoutMs))
            {
                BridgePlugin.Log.LogWarning($"[bridge] The page's call to {op.Name} got no answer within 10 seconds.");
                return JsonAnswer(200, new Dictionary<string, object> { ["ok"] = false, ["error"] = "The game did not answer within 10 seconds (loading?)." });
            }
            if (!result.Ok) BridgePlugin.Log.LogInfo($"[bridge] The page's call to {op.Name} failed: {result.Error}");
            return JsonAnswer(200, result.Ok
                ? new Dictionary<string, object> { ["ok"] = true, ["value"] = result.Value }
                : new Dictionary<string, object> { ["ok"] = false, ["error"] = result.Error });
        }

        private static string Html()
        {
            if (_html != null) return _html;
            using (Stream s = typeof(PageDoor).Assembly.GetManifestResourceStream("page.html"))
            using (var r = new StreamReader(s))
            {
                return _html = r.ReadToEnd();
            }
        }

        private static PageAnswer JsonAnswer(int status, object value) => new PageAnswer { Status = status, Type = "application/json", Body = Operations.ToJson(value), Headers = Common };

        private static PageAnswer Text(int status, string text) => new PageAnswer { Status = status, Type = "text/plain", Body = text, Headers = Common };

        private static bool Same(string a, string b)
        {
            if (a == null || b == null) return false;
            int diff = a.Length ^ b.Length;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ (i < b.Length ? b[i] : 0);
            return diff == 0;
        }

        private static string Random(int bytes)
        {
            var data = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(data);
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
