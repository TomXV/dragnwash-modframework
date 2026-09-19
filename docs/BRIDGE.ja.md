# Bridge：操作を AI クライアントに、MCP で

[English](BRIDGE.md)

> **Bridge 0.1.0 を作りました**（実験的）。[API の計画](API_PLAN.ja.md)の第 2 段階です。調査は終わりました（[作る前の調査](#作る前の調査)）。Proton はまだです。

[操作の登録簿](API_PLAN.ja.md)によって、ライブラリにできることを名前で呼べるようになりました。Bridge は、そのうち**読む**操作を、[Model Context Protocol](https://modelcontextprotocol.io/specification/2025-06-18) で AI クライアント（Claude Code、VS Code、Cursor など）に見せます。AI クライアントは、動いているゲームを見られるようになります。オブジェクトとその値、ログ、Mod、セーブ、会話です。何かを変えることはできません。

## 決めたこと（2026-09-19）

- **読むだけ。** MCP のツールになるのは、*read* として登録された操作だけです。書き換えは、あとで改めてそれだけを決めます。
- **この PC の中だけ、既定はオフ、開発者ツールがオンのときだけ。** Mod を入れただけの遊ぶ人の環境では、動きません。
- **まず HTTP。** Streamable HTTP の方式で、Claude Code、VS Code、Cursor はそのまま登録できます。ローカルのプログラムを起動する形しか使えないクライアント（Claude Desktop）向けの、小さな stdio の仲立ちは、必要な人が出てきたら作ります。
- **トークンは 1 つを使い続け、作り直すボタンを付ける。** クライアントの設定は一度で済みます。漏れたかもしれないときは、ボタン 1 つで新しくできます。

## 置き場所

新しいライブラリ **Bridge**（`DragNWash.ModFramework.Bridge`）です。中核から分けてあるので、Mod のリリースから外せますし、遊ぶ人が自分で足さない限り入りません。依存するのは中核だけです。

## 待ち受け

- `http://127.0.0.1:<ポート>/mcp`。ポートの既定は **47821**（`[Bridge] Port`）です。ループバックのアドレスだけで待ち受けるので、ほかのコンピューターからはつながりません。Windows のファイアウォールの許可を求める画面も出ません。
- **`[Bridge] Enabled`** をオンにするまで、そしてフレームワークの開発者ツールがオンの間しか動きません。どちらかをオフにすると、ポートを閉じ、すべてのクライアントを切ります。
- `HttpListener` ではなく、`TcpListener` の上の小さな HTTP/1.1 サーバーにします（Mono の `HttpListener` は Windows と Proton で振る舞いが違うため。調査を参照）。読むのは、`Content-Length` つきの `POST`、`GET`、`DELETE` だけです。

## 安全策

MCP の通信方式の安全のきまりに沿います。

1. **Host**：`Host` ヘッダーは `127.0.0.1:<ポート>` か `localhost:<ポート>` でなければなりません。自分の名前を 127.0.0.1 に向けるウェブサイト（DNS リバインディング）は、自分の名前を送ってくるので断れます。
2. **Origin**：`Origin` ヘッダーのある要求（送るのはブラウザーだけです）は、`null` でなければ断ります。どのウェブページからも、Bridge は呼べません。
3. **トークン**：すべての要求に `Authorization: Bearer <トークン>` が要ります。比べるときは、かかる時間が一定になる比べ方をします。
   - トークンは 32 バイトの乱数（base64url）で、最初の起動で作ります。
   - 置き場所は `%LOCALAPPDATA%/DragNWash ModFramework/bridge-token.txt` です。利用者自身のプロファイルで、PC のほかのアカウントも読めるかもしれないゲームのフォルダーではありません。Proton では、Wine のプレフィックスの利用者フォルダーです。
   - Bridge のタブの **New token**（と Console の `bridge token new`）で作り直すと、すべてのクライアントを切ります。
4. **上限**：1 MB を超える要求、8 本を超える接続、1 つのセッションから 1 秒に 20 回を超える呼び出しは断ります。20 万文字を超える結果はエラーです（登録簿の上限）。メインスレッドで 10 秒を超える操作は、時間切れのエラーを返します。
5. **それ以外はしない**：クライアントのためにファイルを読み書きすることはありません。返すのは、操作が返すものだけです。

## 通信の中身

- **版**：`2025-06-18` と `2025-03-26`。クライアントの版がこのどちらかならそれを使い、違えば新しいほうを使います。
- **メソッド**：
  - `initialize`：サーバー名「Drag'n Wash ModFramework」、バージョン、機能は `tools` だけ。
  - `notifications/initialized`：202 を返します。
  - `ping`、`tools/list`、`tools/call`。
  - それ以外は、JSON-RPC のエラー -32601 です。
- **セッション**：`initialize` で `Mcp-Session-Id`（乱数）を渡します。
  - そのあと、これのない要求には 400、知らないものには 404 を返します。
  - セッションは同時に 4 つまでで、30 分使われないと終わります。
  - `DELETE /mcp` でセッションを終えます。
- **応答**：`application/json` で、1 つの要求に 1 つの JSON-RPC 応答を返します。`GET /mcp` には 405 を返します。Bridge から、クライアントを呼ぶことはありません。
- **ツール**：読む操作 1 つにつき 1 つです。
  - **名前**：MCP のクライアントが受け付けるのは英数字と `_` `-` なので、`inspector.member.get` は `inspector_member_get` にします。
  - **説明**：操作の説明に、返すものを添えます。
  - **`inputSchema`**：引数から作ります。`string`、`number`、`boolean` のどれかで、選べる値は `enum`、必須は `required` にします。
  - **`annotations`**：`readOnlyHint: true` と、名前から作った `title` です。
  - **一覧**：ツールの一覧は、セッションの始まりで決まります（`listChanged: false`）。
- **結果**：
  - `content` は、結果を JSON にしたテキスト 1 つです。
  - `structuredContent` は、結果がオブジェクトならそのまま、一覧なら `{ "items": [...] }` にします。
  - 操作が失敗したときは、`isError: true` とエラーのテキストを返します。知らないツールや間違った引数は、JSON-RPC のエラー（-32602）です。
- **呼び出し**は `Operations.Call` を通してメインスレッドで動き、ログには `mcp:<クライアント名>` と残ります。

## 遊ぶ人から見えるもの

- **Mods 画面**：Bridge は自分を `ModInfo.Network` で申告します（ホストは 127.0.0.1、受ける側、「この PC の AI クライアントがゲームを読めるようにする」）。なので、ネットワークを使うほかの Mod と同じく、Online の印とページに出ます（[NETWORK.ja.md](NETWORK.ja.md)）。
- **Bridge のタブ**（F1 の窓。Bridge は開発者ツールがオンのときだけ動き、開発者ツールの画面は F1 の窓なので）：
  - 状態：オフ、またはどのポートで待ち受けているか。
  - つながっているクライアント（クライアントが名乗った名前、`clientInfo`）と、最近の呼び出し。
  - ボタン：**Disconnect all**、**New token**、**Copy setup**（下の設定のコマンドをクリップボードに入れる）。
- **Console**：`bridge`（状態とクライアント）、`bridge token new`、`bridge disconnect`。

## クライアントの設定

Claude Code：

```
claude mcp add --transport http dragnwash http://127.0.0.1:47821/mcp --header "Authorization: Bearer <トークン>"
```

VS Code（`.vscode/mcp.json`）と Cursor（`mcp.json`）は、HTTP のサーバーの項目に、同じ URL とヘッダーを書きます。Bridge のタブの **Copy setup** が、トークンを埋めてくれます。

## まだしないこと

書き換えの操作、resources、prompts、サーバーからのイベント（SSE）、ほかのコンピューターからの接続、stdio の仲立ち。ノードエディタのページ（第 3・第 4 段階）は、のちに同じ Bridge が出します。

## 作る前の調査

2026-09-19 に、Windows 11（Direct3D 12）で行いました。調査用の小さな Mod（公開しません）で、次のことを確かめました。
- 127.0.0.1:47821 で待ち受け、登録簿を通して `initialize`、`tools/list`、`tools/call` に答える。
- Host、Origin、トークンを確かめる。
- トークンのファイルを書く。

1. **ゲームの中の TcpListener：Windows では動く。** Awake で `TcpListener(IPAddress.Loopback, 47821)` を始め、裏のスレッドで接続を受け、呼び出しは `Operations.Call` でメインスレッドで動きました。`netstat` では、127.0.0.1:47821 だけで待ち受けています。**Proton（Steam Deck）はまだ確かめていません。**
2. **ファイアウォール：画面は出ない。** ループバックのアドレスで待ち受けても、Windows Defender ファイアウォールの画面は出ませんでした。
3. **NetworkWatch：数えない。** 十数本の接続を受けても、「ネットにつないだ」という記録は出ませんでした。見張っているのは、外へ出る接続だけです。
4. **クライアント**
   - curl：トークンなしは 401、違う Host は 403、ウェブページの Origin は 403、GET は 405、通知は 202 でした。`initialize` は `Mcp-Session-Id` つきで答え、`tools/list` は 22 の読む操作を `game_info`、`inspector_member_get` … として並べ、`tools/call` は結果を返しました。
   - Claude Code（HTTP、ヘッダーつき）：2 回つながりました。`initialize`、`notifications/initialized`、405 で断った `GET`、`tools/list` の順です。
   - Claude Code からツールを呼ぶところは、作った Bridge で確かめます（この PC のコマンドラインのクライアントは、ログインのやり直しが必要でした。それは利用者の操作です）。
5. **トークンのファイル**：`LocalApplicationData` は `C:/Users/<利用者>/AppData/Local` です。そこのファイルを読めるのは、SYSTEM、Administrators、その利用者だけで、ほかのアカウントは読めません。

調査の結果で、設計を変えるところはありませんでした。

## 作業の順番

1. 調査（上）。
2. Bridge ライブラリ：待ち受け、安全の確認、セッション、登録簿の上の `initialize`、`tools/list`、`tools/call`。
3. Bridge のタブ、Console のコマンド、`ModInfo.Network`。
4. ドキュメント：Claude Code、VS Code、Cursor の設定のページ。
