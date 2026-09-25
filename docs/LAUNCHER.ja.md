# ランチャー：更新の確認が残しておくもの

[English](LAUNCHER.md)

> **第 1 段階を作りました**。下のファイルです。ランチャー本体（Steam の起動オプションから、ゲームの前に動く小さなプログラム）は、このあとです。

ランチャーは、更新を探すために自分でネットにつなぎません。それはゲームが 1 日 1 回もうやっているので（[中核の Updates](../src/DragNWash.ModFramework/Updates/UpdateCheck.cs)）、ランチャーはその結果を読みます。GitHub に聞くことは前と同じ 1 回だけで、答えのうち残す部分が増えただけです。リリースノート、ページ、公開日時、ファイルとそのサイズと SHA-256 です。

## 場所とタイミング

`BepInEx/cache/DragNWash.ModFramework/updates.json`。UTF-8 で、BOM はありません。

ゲームが書くのは次のときです。

- 起動して約 5 秒後、Mod の登録が済んでから。確認するものが何もなくても、入っているバージョンはこのセッションのものになります。
- 確認の結果が届いたとき。
- **Check for updates** をオンかオフにしたとき。

まず隣の一時ファイルに書いてから置き換えるので、読む側が書きかけのファイルを見ることはありません。

**Check for updates** がオフのときもファイルは書きます。`"checking": false` で `mods` は空なので、ランチャーには何も出ません。

## 形式

```json
{
  "schema": 1,
  "written": "2026-09-25T01:55:48.7880680Z",
  "checking": true,
  "mods": [
    {
      "guid": "com.tomxv.dragnwash.modframework",
      "name": "Drag'n Wash ModFramework",
      "installedVersion": "1.4.3",
      "repository": "TomXV/dragnwash-modframework",
      "pluginFolder": "DragNWash.ModFramework",
      "installManifest": false,
      "manifestName": "",
      "manifestVersion": "",
      "latest": {
        "tag": "v1.5.0",
        "version": "1.5.0",
        "htmlUrl": "https://github.com/TomXV/dragnwash-modframework/releases/tag/v1.5.0",
        "publishedAt": "2026-09-23T13:03:06Z",
        "body": "## Drag'n Wash ModFramework 1.5.0\r\n\r\nA new look for everything you see: ...",
        "bodyTruncated": false,
        "assets": [
          {
            "name": "DragNWash.ModFramework-1.5.0.zip",
            "size": 1010034,
            "url": "https://github.com/TomXV/dragnwash-modframework/releases/download/v1.5.0/DragNWash.ModFramework-1.5.0.zip",
            "sha256": "c49e0f0b89fd770903c0041c8fbf5a93703f75dbc6f1fc7af06ef1def749b00d"
          }
        ],
        "checkedUtc": "2026-09-25T01:55:12.7326832Z"
      },
      "newer": true
    }
  ]
}
```

GitHub のリポジトリを指定している（`ModInfo.UpdateRepository`）、入っている Mod ごとに 1 つずつです。フレームワーク自身も入ります。

| 項目 | 中身 |
|---|---|
| `schema` | 1。古いランチャーが読めなくなる変更のときだけ上がります。項目が増えるだけなら上がりません。 |
| `written` | ゲームがファイルを書いた時刻。UTC、ISO 8601。 |
| `checking` | **Check for updates** がオンかどうか。 |
| `guid`、`name` | Mod の BepInEx の GUID と、Mods 画面に出る名前。 |
| `installedVersion` | このセッションで BepInEx が読み込んだバージョン。 |
| `repository` | GitHub の `owner/name`。 |
| `pluginFolder` | `BepInEx/plugins` の直下にある Mod のフォルダー。DLL が `plugins` にじかに置いてあるときは `""`。 |
| `installManifest` | そのフォルダーに、インストーラーが書く `mod-install.json` があれば true。このときだけランチャーが自分で Mod を更新できます。なければ、リリースページを開くことしかできません。 |
| `manifestName`、`manifestVersion` | その `mod-install.json` の `name` と `version`。ないときや読めないときは `""`。 |
| `latest` | 最新のリリース。まだ確認していないときや、リリースがないときは `null`。 |
| `latest.tag` | リリースのタグ。 |
| `latest.version` | タグをバージョンにしたもの（`v1.2` なら `1.2.0`）。タグがバージョン番号でなければ `null`。 |
| `latest.htmlUrl` | リリースページ。 |
| `latest.publishedAt` | GitHub が公開した日時。GitHub が返したままの形か、`""`。 |
| `latest.body` | リリースノート（Markdown）。65,536 文字で切ります。切ったときは `bodyTruncated` が true です。 |
| `latest.assets` | リリースのファイル。最大 50 個。`name`、バイト単位の `size`、`url`、`sha256`（小文字の 16 進。GitHub が digest を返さないとき、たとえば digest が付く前に上げたファイルは `""`）。 |
| `latest.checkedUtc` | ゲームが GitHub に聞いた時刻。UTC、ISO 8601。 |
| `newer` | `latest.version` が `installedVersion` より新しければ true。Mods 画面と同じ比べ方です。 |

### .NET Framework で読むとき

ランチャーは .NET Framework 4.7.2 で、`DataContractJsonSerializer` で読みます。それで読めるよう、ふつうの JSON にしてあります。

- `latest` は、リリースがないとき本当の `null` です。タグがバージョンでないときの `version` も同じです。ほかの文字列は、項目ごと消さずに `""` にしてあり、`assets` も `[]` にしてあります。
- 時刻は ISO 8601 の文字列です。`string` で受けて、自分で読んでください。`DataContractJsonSerializer` は `DateTime` に独自の `/Date(...)/` 形式を求めます。
- `size` は `long` に入ります。
- ランチャーが宣言していない項目は無視されるので、項目が増えても壊れません。

### ランチャーがまだ確かめること

ゲームは、`htmlUrl` には `https://github.com/` のアドレスだけを、`assets` には `https://github.com/<repository>/releases/download/` の下のファイルだけを残します。ただ、ファイルはゲームのフォルダーにあるので、そこに書ける人なら誰でも変えられます。何かを入れる前に、ランチャーはアドレスをもう一度確かめ、ダウンロードしたもののサイズと SHA-256 をこの項目と比べる必要があります。`sha256` がないファイルは、この方法では確かめられません。

## 1.5.0 からの更新

1.5.0 は、リリースのタグだけを `BepInEx/config/com.tomxv.dragnwash.modframework.updates.txt` に残していました。このファイルはそのままです。細かい情報は、起動時に `updates.json` から読み戻します。txt のタグと同じタグのリリースだけです。txt にはあって `updates.json` にないリリース（1.5.0 から更新して最初の起動や、cache フォルダーを消したあと）は、次の日を待たずにすぐもう一度確認し、そのあとはまた 1 日 1 回に戻ります。
