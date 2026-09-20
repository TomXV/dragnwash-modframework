# Bridge: the operations for AI clients, over MCP

[日本語](BRIDGE.ja.md)

> **Bridge 0.1.0 is built** (experimental). Stage 2 of the [API plan](API_PLAN.md). The research is done (see [Research before building](#research-before-building)); checked on Windows and on the Steam Deck.

The [operations registry](API_PLAN.md) lets anyone call what the libraries can do by name. The Bridge offers the **read** operations to AI clients (Claude Code, VS Code, Cursor and others) through the [Model Context Protocol](https://modelcontextprotocol.io/specification/2025-06-18), so a client can look at the running game: objects and their values, the log, the mods, the saves, the dialogue. It cannot change anything.

## Decisions (2026-09-19)

- **Read only.** Only operations registered as *read* become MCP tools. Writing is a later, separate decision.
- **This computer only, off by default, developer tools only.** A player who installed a mod never runs it.
- **HTTP first.** The Streamable HTTP transport, which Claude Code, VS Code and Cursor register directly. A small stdio relay for clients that can only start a local program (Claude Desktop) comes when someone needs it.
- **One token, kept, with a button to make a new one.** A client is set up once; if the token may have leaked, a new one is one press away.

## Where it lives

A new library, **Bridge** (`DragNWash.ModFramework.Bridge`), separate from the core so a mod's release can leave it out and a player's install never has it unless they add it. It depends on the core only.

## Listening

- `http://127.0.0.1:<port>/mcp`, port **47821** by default (`[Bridge] Port`). Bound to the loopback address only: no other computer can connect, and Windows does not ask to open the firewall for it.
- Off until the player turns on **`[Bridge] Enabled`**, and only while the framework's developer tools are on; turning either off closes the port and drops every client.
- A small HTTP/1.1 server on `TcpListener`, not `HttpListener` (Mono's `HttpListener` differs between Windows and Linux; see research). It reads `POST`, `GET` and `DELETE` with a `Content-Length` body, nothing else.

## Security

Following the transport's security rules:

1. **Host**: the `Host` header must be `127.0.0.1:<port>` or `localhost:<port>`, compared with the port the Bridge listens on. A website that points its own name at 127.0.0.1 (DNS rebinding) sends its own name, and is refused. The page's own door (docs/CODE_GRAPH.md) checks the `Host`, and the `Origin` against those same two addresses, once more for itself.
2. **Origin**: a request with an `Origin` header (only browsers send one) is refused unless it is `null`. No web page can call the Bridge.
3. **Token**: every request needs `Authorization: Bearer <token>`, compared in constant time. The token is 32 random bytes (base64url), made on first start and kept in `%LOCALAPPDATA%/DragNWash ModFramework/bridge-token.txt` (the user's own profile, not the game folder that other accounts on the PC may read; on Linux and the Steam Deck, `~/.local/share/DragNWash ModFramework/`, and under Proton the Wine prefix's user folder). **New token** on the Bridge tab (and `bridge token new` in the console) replaces it and drops every client.
4. **Limits**: a request body over 1 MB, more than 8 connections, or more than 20 calls a second from one session are refused. A result over 200,000 characters is an error (the registry's cap). An operation that takes more than 10 seconds on the main thread returns a time-out error.
5. **Nothing else**: no files are read or written for a client, only what the operations return.

## The protocol

- **Versions**: `2025-06-18` and `2025-03-26`; the client's version if it is one of these, else the newer one.
- **Methods**: `initialize` (server name "Drag'n Wash ModFramework", version, capabilities `tools` only), `notifications/initialized` (202), `ping`, `tools/list`, `tools/call`. Anything else is JSON-RPC error -32601.
- **Sessions**: `initialize` gives an `Mcp-Session-Id` (random); later requests without it get 400, with an unknown one 404. At most 4 sessions; one idle for 30 minutes ends. `DELETE /mcp` ends a session.
- **Answers** are `application/json`, one JSON-RPC response per request. `GET /mcp` answers 405: the Bridge never calls the client.
- **Tools**: one per read operation. MCP clients accept names of letters, digits, `_` and `-`, so `inspector.member.get` becomes `inspector_member_get`. The description is the operation's, with what it returns. The `inputSchema` comes from the parameters: `string`, `number` or `boolean`, `enum` for the accepted choices, `required`. `annotations`: `readOnlyHint: true`, `title` from the name. The tool list is fixed when a session starts (`listChanged: false`).
- **Results**: `content` is one text item with the result as JSON; `structuredContent` is the result when it is an object, or `{ "items": [...] }` for a list. A failed operation is `isError: true` with the error as text; an unknown tool or bad arguments are JSON-RPC errors (-32602).
- **Calls** run through `Operations.Call` on the main thread, logged as `mcp:<client name>`.

## What the player sees

- **Mods screen**: the Bridge declares itself in `ModInfo.Network` (host 127.0.0.1, incoming, "lets AI clients on this computer read the game"), so the Online tag and page show it, as for any mod that uses the network ([Going online (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Going-online)).
- **Bridge tab** (in the F1 window: the Bridge runs only with the developer tools on, and their screen is the F1 window): off / listening on the port; the clients connected, by the name they gave (`clientInfo`); the last calls; buttons **Disconnect all**, **New token**, and **Copy setup** (puts the command below on the clipboard).
- **Console**: `bridge` (status and clients), `bridge token new`, `bridge disconnect`.

## Setting up a client

Claude Code:

```
claude mcp add --transport http dragnwash http://127.0.0.1:47821/mcp --header "Authorization: Bearer <token>"
```

VS Code (`.vscode/mcp.json`) and Cursor (`mcp.json`) take the same URL and header in their HTTP server entry. The Bridge tab's **Copy setup** fills in the token.

## What it does not do (yet)

Write operations, resources, prompts, server-sent events, access from other computers, and the stdio relay. The node editor page (stages 3 and 4) will be served by the same Bridge later.

## Research before building

Done on 2026-09-19 on Windows 11 (Direct3D 12), with a small research mod (never released) that listened on 127.0.0.1:47821, answered `initialize`, `tools/list` and `tools/call` through the registry, applied the Host, Origin and token checks, and wrote a token file.

1. **TcpListener in the game: works on Windows.** `TcpListener(IPAddress.Loopback, 47821)` started in Awake, a background thread accepted, and calls ran through `Operations.Call` on the main thread. `netstat` shows it listening on 127.0.0.1:47821 only.
   - **Steam Deck (2026-09-19): works.** The Deck runs the game's native Linux build (Mono, Vulkan), not Proton. The built Bridge listened on 127.0.0.1:47821 only (`ss`); from curl on the Deck: no token 401, another Host 403, a web page's Origin 403, GET 405, a notification 202; `initialize`, `tools/list` (22 tools) and `tools/call` (`game_info`, `scene_list`) answered. Turning it off closed the port and on again listened on the same port. The token file was made in `~/.local/share/DragNWash ModFramework/`, whose home folder only the user can enter (700). Proton itself was not tried, since the game has a Linux build.
2. **Firewall: no prompt.** Binding the loopback address raised no Windows Defender Firewall window.
3. **NetworkWatch: not counted.** A dozen accepted connections, and no "went online" record; the watch sees outgoing connections only.
4. **Clients.** With curl: no token 401, another Host 403, a web page's Origin 403, GET 405, a notification 202, `initialize` with `Mcp-Session-Id`, `tools/list` with the 22 read operations as `game_info`, `inspector_member_get`..., and `tools/call` returning the result. Claude Code (HTTP transport with the header) connected twice: `initialize`, `notifications/initialized`, a `GET` it took the 405 for, `tools/list`. A tool call from Claude Code is left for the built Bridge (the command-line client on this machine needed a new login, which is the user's to do).
5. **The token file.** `LocalApplicationData` is `C:/Users/<user>/AppData/Local`; the file there is readable by SYSTEM, Administrators and the user only (no other account).

Nothing in the research changes the design.

## Order of work

1. Research (above).
2. The Bridge library: the listener, the security checks, sessions, `initialize`, `tools/list`, `tools/call` over the registry.
3. The Bridge tab, the console commands, `ModInfo.Network`.
4. Docs: a page on setting up Claude Code, VS Code and Cursor.
