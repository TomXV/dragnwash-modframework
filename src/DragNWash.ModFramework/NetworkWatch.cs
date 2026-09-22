using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine.Networking;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// Notes which mods connect to the internet, and whether they said so in
    /// <see cref="ModInfo.Network"/>. The Mods screen shows both; a connection
    /// nobody declared is marked there and logged as a warning.
    /// <para>
    /// It watches only. Nothing is blocked or changed, and it is not a security
    /// boundary: it sees <c>UnityWebRequest</c>, <c>WebRequest</c> (and so
    /// <c>WebClient</c>), <c>HttpClient</c> and managed sockets, not native
    /// code, and a mod that wants to hide can get around it. A connection is
    /// credited to the first mod found on the calling stack, so work a library
    /// does for a mod counts as the library's. Connections to this computer
    /// (localhost) and file URLs are not counted. Experimental, since 1.2.0.
    /// </para>
    /// </summary>
    public static class NetworkWatch
    {
        /// <summary>One host a mod connected to this session.</summary>
        public sealed class Connection
        {
            /// <summary>The mod the connection was credited to.</summary>
            public string Guid { get; internal set; }

            /// <summary>The host name, or the address when only that was given.</summary>
            public string Host { get; internal set; }

            /// <summary>The API it went through: UnityWebRequest, WebRequest, HttpClient or Socket.</summary>
            public string Via { get; internal set; }

            /// <summary>How many times, this session.</summary>
            public int Count { get; internal set; }

            /// <summary>True when the mod lists the host in <see cref="ModInfo.Network"/>.</summary>
            public bool Declared { get; internal set; }

            /// <summary>When it was first seen.</summary>
            public DateTime FirstSeenUtc { get; internal set; }
        }

        internal const string Section = "Network";
        internal const string Key = "Watch connections";
        internal const string KeyDescription = "Notes which mods connect to the internet, and marks on the Mods screen the ones that do so without saying so. It only watches: nothing is blocked, and a mod can get around it.";

        private const string UpdateHost = "api.github.com";
        private const string UpdatesTurnOff = "Mods → Drag'n Wash ModFramework → Settings → Check for updates";

        private static ConfigEntry<bool> _enabled;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Connection> Seen = new Dictionary<string, Connection>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> FailedHooks = new HashSet<string>();
        private static Harmony _harmony;
        private static bool _httpClientPatched;
        private static int _revision;

        [ThreadStatic]
        private static bool _busy;

        // How deep this thread is in watched calls. One API often calls another
        // (CreateHttp calls Create, Connect("host") calls Connect(address)), and
        // only the outermost is counted, with the most telling host.
        [ThreadStatic]
        private static int _depth;

        /// <summary>True while connections are being watched (the player's setting).</summary>
        public static bool Enabled => _enabled != null && _enabled.Value;

        // Goes up when something new is seen, so the Mods screen can show it.
        internal static int Revision => _revision;

        /// <summary>Hosts <paramref name="guid"/> connected to this session.</summary>
        public static IReadOnlyList<Connection> SeenBy(string guid)
        {
            lock (Gate)
            {
                return Seen.Values.Where(c => string.Equals(c.Guid, guid, StringComparison.Ordinal)).OrderBy(c => c.FirstSeenUtc).ToList();
            }
        }

        /// <summary>Every connection seen this session.</summary>
        public static IReadOnlyList<Connection> All()
        {
            lock (Gate)
            {
                return Seen.Values.OrderBy(c => c.FirstSeenUtc).ToList();
            }
        }

        /// <summary>True when <paramref name="guid"/> connected to a host it did not declare.</summary>
        public static bool HasUndeclared(string guid)
        {
            lock (Gate)
            {
                return Seen.Values.Any(c => !c.Declared && string.Equals(c.Guid, guid, StringComparison.Ordinal));
            }
        }

        /// <summary>
        /// What <paramref name="guid"/> says it does online: its
        /// <see cref="ModInfo.Network"/>, plus the framework's update check when
        /// the mod names an update repository and checking is on.
        /// </summary>
        public static IReadOnlyList<NetworkUse> DeclaredBy(string guid)
        {
            var list = new List<NetworkUse>();
            ModInfo info = ModFramework.GetInfo(guid);
            if (info?.Network != null)
            {
                list.AddRange(info.Network.Where(u => u != null && !string.IsNullOrEmpty(u.Host)));
            }
            if (info != null && !string.IsNullOrEmpty(info.UpdateRepository) && guid != ModFramework.Guid && Updates.UpdateCheck.Enabled)
            {
                list.Add(new NetworkUse
                {
                    Host = UpdateHost,
                    Purpose = "The framework checks once a day whether this mod has a newer release (" + info.UpdateRepository + ").",
                    Sends = "The repository's name. Nothing about you or your game.",
                    TurnOff = UpdatesTurnOff,
                });
            }
            return list;
        }

        // ---- installing ------------------------------------------------------------

        internal static void Install(ConfigFile config, Harmony harmony)
        {
            try
            {
                _enabled = config.Bind(Section, Key, true, KeyDescription);
                _enabled.SettingChanged += (_, __) => Interlocked.Increment(ref _revision);
                _harmony = harmony;

                Hook(typeof(UnityWebRequest), "SendWebRequest", Type.EmptyTypes, nameof(UnityWebRequestPrefix));
                Hook(typeof(WebRequest), "Create", new[] { typeof(string) }, nameof(WebRequestPrefix));
                Hook(typeof(WebRequest), "Create", new[] { typeof(Uri) }, nameof(WebRequestPrefix));
                Hook(typeof(WebRequest), "CreateDefault", new[] { typeof(Uri) }, nameof(WebRequestPrefix));
                Hook(typeof(WebRequest), "CreateHttp", new[] { typeof(string) }, nameof(WebRequestPrefix));
                Hook(typeof(WebRequest), "CreateHttp", new[] { typeof(Uri) }, nameof(WebRequestPrefix));
                Hook(typeof(Socket), "Connect", new[] { typeof(string), typeof(int) }, nameof(NamedHostPrefix));
                Hook(typeof(Socket), "BeginConnect", new[] { typeof(string), typeof(int), typeof(AsyncCallback), typeof(object) }, nameof(NamedHostPrefix));
                Hook(typeof(TcpClient), "Connect", new[] { typeof(string), typeof(int) }, nameof(NamedHostPrefix));
                Hook(typeof(Socket), "Connect", new[] { typeof(EndPoint) }, nameof(SocketPrefix));
                Hook(typeof(Socket), "BeginConnect", new[] { typeof(EndPoint), typeof(AsyncCallback), typeof(object) }, nameof(SocketPrefix));
                Hook(typeof(Socket), "ConnectAsync", new[] { typeof(SocketAsyncEventArgs) }, nameof(SocketPrefix));
                Hook(typeof(Socket), "SendTo", new[] { typeof(byte[]), typeof(int), typeof(int), typeof(SocketFlags), typeof(EndPoint) }, nameof(SocketPrefix));

                // HttpClient lives in System.Net.Http, which the game may never load:
                // hooked now if it is there, or as soon as a mod loads it.
                AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
                {
                    if (args.LoadedAssembly.GetName().Name == HttpAssembly)
                    {
                        HookHttpClient(args.LoadedAssembly);
                    }
                };
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == HttpAssembly)
                    {
                        HookHttpClient(assembly);
                    }
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Connection watching could not start: {ex.Message}");
            }
        }

        private const string HttpAssembly = "System.Net.Http";

        // The types are looked for in System.Net.Http itself: AccessTools.TypeByName
        // would go through every type of every loaded assembly, three times, and
        // warn for each type while the game has not loaded that assembly.
        private static void HookHttpClient(Assembly http)
        {
            if (_httpClientPatched)
            {
                return;
            }
            Type client = http.GetType("System.Net.Http.HttpClient", false);
            Type message = http.GetType("System.Net.Http.HttpRequestMessage", false);
            Type option = http.GetType("System.Net.Http.HttpCompletionOption", false);
            if (client == null || message == null || option == null)
            {
                return;
            }
            _httpClientPatched = true;
            // Every SendAsync, GetAsync and PostAsync ends in this one.
            Hook(client, "SendAsync", new[] { message, option, typeof(CancellationToken) }, nameof(HttpClientPrefix));
        }

        private static void Hook(Type type, string name, Type[] parameters, string prefix)
        {
            string label = type.Name + "." + name + "(" + parameters.Length + ")";
            try
            {
                MethodInfo target = AccessTools.Method(type, name, parameters);
                if (target == null)
                {
                    ModFramework.Log.LogDebug($"[network] {label} not found; not watched.");
                    return;
                }
                _harmony.Patch(target, prefix: new HarmonyMethod(typeof(NetworkWatch), prefix), finalizer: new HarmonyMethod(typeof(NetworkWatch), nameof(Leave)));
            }
            catch (Exception ex)
            {
                if (FailedHooks.Add(label))
                {
                    ModFramework.Log.LogWarning($"[network] {label} could not be watched: {ex.Message}");
                }
            }
        }

        // ---- the hooks: each only reads, and never lets anything escape --------------

        // Every prefix enters, every call leaves (a finalizer runs even when the
        // call throws); only the outermost call records.
        private static bool Enter()
        {
            return _depth++ == 0;
        }

        private static void Leave()
        {
            if (_depth > 0)
            {
                _depth--;
            }
        }

        private static void NamedHostPrefix(object[] __args)
        {
            try
            {
                if (!Enter())
                {
                    return;
                }
                string host = __args != null && __args.Length > 0 ? __args[0] as string : null;
                if (string.IsNullOrEmpty(host) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || (IPAddress.TryParse(host, out IPAddress address) && IPAddress.IsLoopback(address)))
                {
                    return;
                }
                Record(host, "Socket", true);
            }
            catch
            {
            }
        }

        private static void UnityWebRequestPrefix(UnityWebRequest __instance)
        {
            try
            {
                if (!Enter())
                {
                    return;
                }
                Record(HostOf(__instance?.url), "UnityWebRequest", false);
            }
            catch
            {
                // Watching must never break the request.
            }
        }

        private static void WebRequestPrefix(object[] __args)
        {
            try
            {
                if (!Enter())
                {
                    return;
                }
                object target = __args != null && __args.Length > 0 ? __args[0] : null;
                string host = target is Uri uri ? HostOf(uri) : HostOf(target as string);
                Record(host, "WebRequest", false);
            }
            catch
            {
            }
        }

        private static void SocketPrefix(object[] __args)
        {
            try
            {
                if (!Enter() || __args == null)
                {
                    return;
                }
                EndPoint endPoint = null;
                foreach (object a in __args)
                {
                    if (a is EndPoint ep) { endPoint = ep; break; }
                    if (a is SocketAsyncEventArgs sa) { endPoint = sa.RemoteEndPoint; break; }
                }
                string host = endPoint is DnsEndPoint dns ? dns.Host
                    : endPoint is IPEndPoint ip ? (IPAddress.IsLoopback(ip.Address) ? null : ip.Address.ToString())
                    : null;
                Record(host, "Socket", true);
            }
            catch
            {
            }
        }

        private static void HttpClientPrefix(object __instance, object[] __args)
        {
            try
            {
                if (!Enter())
                {
                    return;
                }
                object request = __args != null && __args.Length > 0 ? __args[0] : null;
                var uri = request?.GetType().GetProperty("RequestUri")?.GetValue(request, null) as Uri;
                if (uri != null && !uri.IsAbsoluteUri)
                {
                    var baseAddress = __instance?.GetType().GetProperty("BaseAddress")?.GetValue(__instance, null) as Uri;
                    uri = baseAddress != null ? new Uri(baseAddress, uri) : null;
                }
                Record(HostOf(uri), "HttpClient", false);
            }
            catch
            {
            }
        }

        // ---- recording ---------------------------------------------------------------

        private static string HostOf(string url)
        {
            return !string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ? HostOf(uri) : null;
        }

        // Only what goes over a network: file and jar URLs (common for loading
        // a mod's own files) and this computer are left out.
        private static string HostOf(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri)
            {
                return null;
            }
            switch (uri.Scheme.ToLowerInvariant())
            {
                case "http":
                case "https":
                case "ws":
                case "wss":
                case "ftp":
                    break;
                default:
                    return null;
            }
            if (uri.IsLoopback || string.IsNullOrEmpty(uri.Host))
            {
                return null;
            }
            return uri.Host;
        }

        private static void Record(string host, string via, bool socketLevel)
        {
            if (host == null || !Enabled || _busy)
            {
                return;
            }
            _busy = true;
            try
            {
                string guid = Owner(out bool insideHigherLevel);
                // A socket opened by WebRequest or HttpClient was already counted there.
                if (guid == null || (socketLevel && insideHigherLevel))
                {
                    return;
                }
                string key = guid + "|" + host;
                Connection c;
                bool first;
                lock (Gate)
                {
                    first = !Seen.TryGetValue(key, out c);
                    if (first)
                    {
                        c = new Connection
                        {
                            Guid = guid,
                            Host = host,
                            Via = via,
                            Declared = DeclaredBy(guid).Any(u => Matches(u.Host, host)),
                            FirstSeenUtc = DateTime.UtcNow,
                        };
                        Seen[key] = c;
                    }
                    c.Count++;
                }
                if (first)
                {
                    Interlocked.Increment(ref _revision);
                    if (c.Declared)
                    {
                        ModFramework.Log.LogInfo($"[network] {guid} connected to {host} ({via}), as it says it does.");
                    }
                    else
                    {
                        ModFramework.Log.LogWarning($"[network] {guid} connected to {host} ({via}) without saying so in its ModInfo.Network. The Mods screen marks it.");
                    }
                }
            }
            finally
            {
                _busy = false;
            }
        }

        // The first plugin on the calling stack, skipping this class. Also tells
        // whether the call came through a higher-level client (HttpWebRequest,
        // HttpClient, TLS), whose own hook already counted it.
        private static string Owner(out bool insideHigherLevel)
        {
            insideHigherLevel = false;
            Dictionary<Assembly, string> plugins = PluginAssemblies();
            var trace = new StackTrace(2, false);
            for (int i = 0; i < trace.FrameCount; i++)
            {
                Type type = trace.GetFrame(i)?.GetMethod()?.DeclaringType;
                if (type == null || type == typeof(NetworkWatch))
                {
                    continue;
                }
                string full = type.FullName ?? "";
                if (full.StartsWith("System.Net.Http.", StringComparison.Ordinal)
                    || full.StartsWith("System.Net.HttpWebRequest", StringComparison.Ordinal)
                    || full.StartsWith("System.Net.WebConnection", StringComparison.Ordinal)
                    || full.StartsWith("System.Net.Security.", StringComparison.Ordinal)
                    || full.StartsWith("Mono.Net.", StringComparison.Ordinal)
                    || full.StartsWith("Mono.Security.", StringComparison.Ordinal))
                {
                    insideHigherLevel = true;
                }
                if (plugins.TryGetValue(type.Assembly, out string guid))
                {
                    return guid;
                }
            }
            return null;
        }

        private static Dictionary<Assembly, string> PluginAssemblies()
        {
            var map = new Dictionary<Assembly, string>
            {
                [typeof(NetworkWatch).Assembly] = ModFramework.Guid,
            };
            foreach (PluginInfo plugin in Chainloader.PluginInfos.Values)
            {
                Assembly assembly = plugin.Instance != null ? plugin.Instance.GetType().Assembly : null;
                if (assembly != null && !map.ContainsKey(assembly))
                {
                    map[assembly] = plugin.Metadata.GUID;
                }
            }
            return map;
        }

        // "api.example.com" matches itself; "*.example.com" matches example.com
        // and any host under it.
        internal static bool Matches(string pattern, string host)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(host))
            {
                return false;
            }
            pattern = pattern.Trim();
            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                string root = pattern.Substring(2);
                return host.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith("." + root, StringComparison.OrdinalIgnoreCase);
            }
            return host.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
