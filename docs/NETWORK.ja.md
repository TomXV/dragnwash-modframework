# 外と通信する Mod

[English](NETWORK.md)

> **実験的な機能です。** コア 1.2.0、`experimental/network-disclosure` ブランチにあり、まだリリースには含まれていません。名前や文言は変わることがあります。

Mod はゲームと同じ権限で動くので、何をどこへでも送れます。プレイヤーにはそれが見えず、ほとんどの人は Mod のソースを読みません。フレームワークの決まりは単純です。**外と通信する Mod はそのことを申告し、プレイヤーは驚く前に Mods 画面でそれを確かめられる。**

## なぜ必要か

外と通信する Mod は、マルウェアが紛れ込む場所にもなります。Mod はプレイヤーのファイルを外へ送ったり、コードを取ってきて実行したりでき、そうしながら普通の Mod のように見せられます。人をだますように作られた Mod を、フレームワークが止めることはできません（[これができないこと](#これができないこと)）。できるのは、外と通信することを、すべての Mod が公に申告すべきことにすることです。そうすれば、

- プレイヤーは、遊ぶ前にも遊んでいる間にも、どの Mod がどこと何のために通信するかを確かめられる
- 申告していない所へ接続した Mod は、Mods 画面とログで目立ち、プレイヤーやレビューする人、ほかの Mod 作者の目に留まる
- 正直な Mod 作者には、自分の Mod が何をするかを伝える、分かりやすい決まった方法がある

3 つの部分からなります。

1. Mod 作者向けの**ルール**：GUIDE のルール 11
2. `ModInfo.Network` での**申告**。Mods 画面に表示されます
3. どの Mod が実際に通信したかを記録し、申告していない Mod に印を付ける**見張り**

## 1. ルール（GUIDE ルール 11）

インターネットに接続する Mod は、接続先のホストをすべて `ModInfo.Network` に並べ、何のためか・何を送るか・どう止めるかを書きます。プレイヤーについての情報（名前、セーブ、入力した文字、その人を追える ID）は、本人がオンにしてから送ります。プレイヤーについて何も送らない確認は、フレームワークの更新確認のように既定でオンでも構いません。

## 2. 申告

```csharp
ModFramework.Register(new ModInfo
{
    Guid = MyMod.Guid,
    DisplayName = "My Mod",
    Network = new[]
    {
        new NetworkUse
        {
            Host = "api.example.com",          // または "*.example.com"
            Purpose = "Downloads the latest word list once a day.",
            Sends = "The language you chose. Nothing that identifies you.",
            TurnOff = "Mods → My Mod → Settings → Online word list",
        },
    },
});
```

Mods 画面では次のように出ます。

- 一覧：ホストを申告しているか、通信が見られた Mod に **Online** のタグ
- 詳細：**Uses the internet:** とホスト名
- **Internet** ページ（Settings の上のボタン）：ホストごとに *What for*（何のため）・*What is sent*（何を送るか）・*How to turn it off*（止め方）、続けてこのセッションで見られた通信

`UpdateRepository` を設定した Mod には、フレームワークが `api.github.com` の項目を足します。その問い合わせは、フレームワークが Mod の代わりに行うからです。フレームワーク自身の更新確認も同じ形で申告しています。プレイヤーが更新確認をオフにすると、これらの項目は消えます。

Console の `mods network` でも、読み込まれている全プラグインについて同じ内容を出せます。

## 3. 見張り

`NetworkWatch`（設定 `[Network] Watch connections`、既定でオン）は、次のメソッドに Prefix を当てます。

| API | フックする所 |
|---|---|
| `UnityWebRequest` | `SendWebRequest()` |
| `WebRequest`（`WebClient` も経由する） | `Create`・`CreateDefault`・`CreateHttp` |
| `HttpClient` | Get / Post / Send がすべて行き着く `SendAsync(request, option, token)`。`System.Net.Http` が読み込まれた時点でフック |
| `Socket` | `Connect(EndPoint)`・`Connect(host, port)`・`BeginConnect(EndPoint, ...)`・`BeginConnect(host, port, ...)`・`ConnectAsync(SocketAsyncEventArgs)`・`SendTo(..., EndPoint)` |
| `TcpClient` | `Connect(host, port)` |

呼び出しのたびに、呼び出し元のスタックで最初に見つかったプラグインを探し、「Mod・ホスト・API」を記録します。ある Mod があるホストに初めて接続したとき：

- 申告済み：ログに情報を 1 行
- 未申告：ログに**警告**。Mods 画面では **Online** タグと、警告色の *Went online without saying so:*（申告せずに通信した）の行。Internet ページでは、そのホストに *not declared by the mod*（Mod が申告していない）

細かい点：

- **数えるのは一番外側の呼び出しだけ。** `CreateHttp` は `Create` を、`Connect("host")` は `Connect(アドレス)` を呼びます。スレッドごとの深さを数え、ホスト名の入った 1 件だけを残します。`HttpWebRequest`・`HttpClient`・TLS の中で開かれるソケットは、それを始めた呼び出しの側で数えます。
- **数えないもの：** `file:`・`jar:` などネットワークでない URL、`localhost` とループバックのアドレス。
- **申告したホストとの照合：** 完全一致。`*.example.com` は `example.com` とその下すべてに一致します。アドレスへ直接つなぐ Mod は、そのアドレスを申告します。
- **記録はスタック上の最初のプラグインに付きます。** ライブラリが Mod の代わりに通信すれば、ライブラリの分になります。後で別スレッドで動き、スタックに Mod がいないコード（async の続き）は誰にも付けず記録しません。それを始めた呼び出しの方で記録済みです。
- **負荷：** 見張る呼び出しごとにスタックをたどります。通信はまれなので問題になりません。

### これができないこと

**見張るだけ**です。何も止めず、遅らせず、変えません。見張りの中で失敗しても握りつぶし、Mod の通信はそのまま進みます。

**安全を保証する仕組みではありません。** ネイティブコード、Mod が自前で持つソケットのライブラリ、フックを外す Harmony の小細工、呼び出し元を隠すコードは、すり抜けます。見張りが捕まえるのは正直なうっかりで、よくある場合を見えるようにするだけです。Mod が安全だと証明することはできません。Internet ページにもそのことを 1 行で書いています。

## 残った問い

- Mods 画面のページのボタンは 2 つ分しかありません。Internet ページを先に置くので、自前のページを 2 つ持つ Mod は、2 つ目のボタンの場所を失います。
- Mods 画面に増えた文言は、リリース前に翻訳パック（Localization）へ行を足す必要があります。
- `Dns` の名前解決は見張りません。名前解決だけでは、リゾルバーに名前が送られるだけだからです。
