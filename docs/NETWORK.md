# Mods that go online

[日本語](NETWORK.ja.md)

> **Experimental.** Core 1.2.0, on the `experimental/network-disclosure` branch, not in a release. Names and wording may still change.

A mod runs with the same rights as the game, so it can send anything anywhere. Players cannot see that happen, and most never read a mod's source. The framework's rule is simple: **a mod that connects to the internet says so, and players can see it on the Mods screen before anything surprises them.**

## Why

A mod that talks to the internet is also where malware would hide: a mod can send a player's files away, or fetch and run code, and look like any other mod while doing it. The framework cannot stop a mod that is written to deceive (see [What it is not](#what-it-is-not)). What it can do is make going online something every mod has to state openly, so that:

- players can see, before and while they play, which mods talk to which hosts and why;
- a mod that connects somewhere it never mentioned stands out, on the Mods screen and in the log, where players, reviewers and other mod makers will notice it;
- mod makers who are honest have a plain, standard way to say what they do.

Three parts:

1. **A rule** for mod makers: GUIDE rule 11.
2. **A declaration** in `ModInfo.Network`, shown on the Mods screen.
3. **A watch** that notes which mods actually connected, and marks the ones that did not say so.

## 1. The rule (GUIDE rule 11)

A mod that connects to the internet lists every host in `ModInfo.Network`, with what for, what it sends and how to turn it off. Anything about the player (their name, their save, what they typed, an ID that follows them) is sent only after they turn it on; checks that send nothing about the player may be on by default, like the framework's update check.

## 2. The declaration

```csharp
ModFramework.Register(new ModInfo
{
    Guid = MyMod.Guid,
    DisplayName = "My Mod",
    Network = new[]
    {
        new NetworkUse
        {
            Host = "api.example.com",          // or "*.example.com"
            Purpose = "Downloads the latest word list once a day.",
            Sends = "The language you chose. Nothing that identifies you.",
            TurnOff = "Mods → My Mod → Settings → Online word list",
        },
    },
});
```

On the Mods screen:

- The list shows an **Online** tag on every mod that declares a host or was seen connecting.
- The details show **Uses the internet:** with the hosts.
- An **Internet** page (a button above Settings) lists each host with *What for*, *What is sent* and *How to turn it off*, then what was seen this session.

A mod that sets `UpdateRepository` gets an entry for `api.github.com` from the framework, because the framework makes that request on the mod's behalf. The framework declares its own update check the same way. When the player turns update checks off, those entries go away.

`mods network` in the Console prints the same, for every loaded plugin.

## 3. The watch

`NetworkWatch` (setting `[Network] Watch connections`, on by default) puts a prefix on:

| API | Hooked |
|---|---|
| `UnityWebRequest` | `SendWebRequest()` |
| `WebRequest` (and so `WebClient`) | `Create`, `CreateDefault`, `CreateHttp` |
| `HttpClient` | `SendAsync(request, option, token)`, which every Get/Post/Send ends in; hooked when `System.Net.Http` loads |
| `Socket` | `Connect(EndPoint)`, `Connect(host, port)`, `BeginConnect(EndPoint, ...)`, `BeginConnect(host, port, ...)`, `ConnectAsync(SocketAsyncEventArgs)`, `SendTo(..., EndPoint)` |
| `TcpClient` | `Connect(host, port)` |

For each call it finds the first plugin on the calling stack and records *mod, host, API*. The first time a mod reaches a host:

- declared: an info line in the log;
- not declared: a **warning** in the log, the **Online** tag and a *Went online without saying so:* line in warning colour on the Mods screen, and the host marked *not declared by the mod* on the Internet page.

Details:

- **Only the outermost call counts.** `CreateHttp` calls `Create`, `Connect("host")` calls `Connect(address)`; a thread-local depth keeps one entry, with the host name. A socket opened inside `HttpWebRequest`, `HttpClient` or TLS is left to the call that started it.
- **Not counted:** `file:`, `jar:` and other non-network URLs; `localhost` and loopback addresses.
- **Declared hosts** match exactly, or `*.example.com` matches `example.com` and everything under it. A mod that connects to a bare address must declare that address.
- **Credit goes to the first plugin on the stack.** Work a library does for a mod counts as the library's. Code that runs later on another thread with no mod on its stack (an async continuation) is not credited to anyone and not recorded; the call that started it already was.
- **Cost:** a stack walk per watched call. Connections are rare, so it does not matter.

### What it is not

It **only watches**. Nothing is blocked, delayed or changed, and a failure inside the watch is swallowed so the mod's request still runs.

It is **not a security boundary**. Native code, a mod's own socket library, Harmony tricks that remove the hooks, or code that hides its calling stack all get past it. It catches honest mistakes and makes the common case visible; it cannot prove a mod is safe. The Internet page says so in one line.

## Open questions

- The Mods screen has room for two page buttons. The Internet page comes first, so a mod with two pages of its own loses the second button's slot.
- The new Mods screen phrases need rows in the translation packs (Localization) before this ships.
- `Dns` lookups are not watched: a lookup alone sends only a name to the resolver.
