# ランチャー：更新の確認が残しておくもの

[English](LAUNCHER.md)

> **ここまで作りました**：下のファイルと、Mods 画面の「更新する」ボタンと、それがランチャーに残すファイルです（[Mods 画面から更新する](#mods-画面から更新する)）。ランチャー本体（Steam の起動オプションから、ゲームの前に動く小さなプログラム）は、別のブランチで作っています。

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
      "icon": "BepInEx/cache/DragNWash.ModFramework/icons/com.tomxv.dragnwash.modframework.png",
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
| `icon` | Mods 画面がその Mod に出す絵。ゲームのフォルダーからの相対パス。ないときは項目ごとありません。Mod が `IconPath` を渡していればそのファイル、そうでなければ `Icon` のテクスチャーを、読み出せるときに限って `BepInEx/cache/DragNWash.ModFramework/icons/<guid>.png` へ書き出したものです。 |
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

## Mods 画面から更新する

新しいリリースがある Mod のうち、インストーラーで入れたもの（フォルダーに `mod-install.json` があるもの。上の `installManifest`）は、Mods 画面の「新しいバージョンがあります」の帯に、「リリースページを開く」と並んで「更新する」が出ます。ほかの Mod は今までどおりページのボタンだけです。WebView2 ランタイムがない PC でも、どの Mod もページのボタンだけです。ランチャーはそれがないと何も更新できないからです。

「更新する」は、「アンインストール」と同じように、まず確認します。帯のいちばん上に黄色い帯で「ゲームを終了すること、保存していない進行は失われることがあること」を出し、ボタンは同じ場所で「終了して更新」に変わってフォーカスもそこに残り、「リリースページを開く」は「キャンセル」に変わります。もう一度押すと（パッドなら A を 2 回）進みます。ほかの Mod を選ぶ、画面を出る、戻る（B）で取り消しです。Mod の確認中は押せません。

進むと、ゲームは `BepInEx/cache/DragNWash.ModFramework/update-request.json` を書きます。これも一時ファイルを通して書きます。

```json
{
  "schema": 1,
  "requestedUtc": "2026-09-25T10:00:00Z",
  "gamePid": 1234,
  "mods": [ "com.tomxv.dragnwash.localization" ]
}
```

`gamePid` はゲームのプロセス ID、`mods` はその Mod の GUID 1 つです。そのあと：

- **ランチャーから起動していたとき**：ゲームは終了するだけです。ゲームの「終了」ボタンと同じ終わり方です。ランチャーはゲームの終了を待っているので、このファイルを見つけて Mod を更新し、ゲームを起動し直します。ランチャーは起動するゲームに `DNW_LAUNCHER=1` を渡します。これがなくても、ゲームの親プロセスが `BepInEx/DragNWash.Installer/Launcher.exe` なら、ランチャーから起動したものとして扱います。
- **ほかの起動のしかた**（起動オプションなしで Steam から、または exe から）：ゲームは `Launcher.exe --update-after-exit --wait-pid <pid> --mods "<guid>"` を窓なしで起動してから終了します。ランチャーはゲームが閉じるのを待ってから窓を出し（閉じるのに 10 秒以上かかるときは、そこで出します）、Mod を更新して、Steam からゲームを起動し直します。

`Launcher.exe` がないときは、ゲームは何も書かず、終了もしません。帯で「ランチャーが入っていないこと、インストーラーをもう一度実行すれば入ること」を伝え、「リリースページを開く」はそのまま残ります。ほかのことで失敗したときは、理由が `BepInEx/LogOutput.log` に出て、ページのボタンはそのまま使えます。

ゲーム自体は、今までどおり何もダウンロードしません。ランチャーがネットにつなぐのは、プレイヤーが更新を押したあと（ここか、ランチャーの窓で）だけです。リリースのファイルをダウンロードし、入れる前にサイズと SHA-256 を `updates.json` と比べます。

### ランチャーの設定

ランチャーは起動するときに `BepInEx/config/com.tomxv.dragnwash.modframework.cfg` の `[Launcher]` を読みます。ファイルや行がなければ既定値を使います。この項目はゲームが足すので、Mods 画面のフレームワークの設定にも出ます。その上にある起動オプションのスイッチは、このファイルには入っていません（次の節）。

| キー | 値 | 既定値 |
|---|---|---|
| `Logo lettering` | `Handwriting`（ひと筆ずつ書く）か `Typewriter`（1 文字ずつ打つ） | `Handwriting` |
| `Progress bar` | `Bottom edge`（窓のいちばん下）か `Under text`（文字の下） | `Bottom edge` |

## Mods 画面から起動オプションを切り替える

フレームワークの設定の `[Launcher]` のいちばん上に、もう 1 行あります。「起動時に更新を確認する（Steam の起動オプション）」です。Install.exe のチェックボックスと同じもので、Steam の起動オプションの `%command%` の前にランチャーを入れます。設定ファイルには入っていません。スイッチは、今遊んでいる Steam アカウントの起動オプションに、今ランチャーが入っているかどうかをそのまま映します（Mods 画面を開いたときに、そのアカウントの `localconfig.vdf` を読みます）。なので、変えたしるしの点も、初期値に戻すボタンもありません。

この行が出るのは Windows だけで、`BepInEx/DragNWash.Installer/Launcher.exe` があり、WebView2 ランタイムが入っているときだけです。ランチャーは Windows でしか動かないので、Steam Deck（と Proton）では出ません。

Steam は設定をメモリに持っていて、終了するときにファイルへ書き戻します。なのでゲームが自分で書き換えることはできません。スイッチを押すと、「更新する」と同じように、まず確認します。詳細のいちばん上に黄色い帯で「ゲームを終了して、Steam とゲームを起動し直すこと」を出し、スイッチは「キャンセル」と「再起動して切り替え」に変わります。フォーカスは「再起動して切り替え」に移るので、パッドなら A を 2 回です。ほかの Mod やタブを選ぶ、画面を出る、戻る（B）で取り消しです。まだ何も変わっていないので、「保存しました」は出しません。

「再起動して切り替え」を押すと、ゲームは `Launcher.exe --launch-option on|off --wait-pid <pid>` を窓なしで起動し、ゲームの「終了」ボタンと同じように終了します。ゲームが閉じたら、ランチャーが Steam を閉じ、起動オプションを書き換えて、Steam とゲームを起動し直します（[LAUNCHER_APP.ja.md](LAUNCHER_APP.ja.md)）。ランチャーを起動できなかったときは、ゲームは終了せず、帯でそう伝えます。理由は `BepInEx/LogOutput.log` に出ます。

## 1.5.0 からの更新

1.5.0 は、リリースのタグだけを `BepInEx/config/com.tomxv.dragnwash.modframework.updates.txt` に残していました。このファイルはそのままです。細かい情報は、起動時に `updates.json` から読み戻します。txt のタグと同じタグのリリースだけです。txt にはあって `updates.json` にないリリース（1.5.0 から更新して最初の起動や、cache フォルダーを消したあと）は、次の日を待たずにすぐもう一度確認し、そのあとはまた 1 日 1 回に戻ります。
