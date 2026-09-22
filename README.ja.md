<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="images/logo-notagames.png"><img src="images/logo-notagames-panel.png" alt="Drag'n Wash ModFramework" width="420"></picture></p>

# Drag'n Wash ModFramework

[English](README.md)

[Drag'n Wash](https://store.steampowered.com/app/4739660/) 用の前提 Mod（BepInEx 5）です。ゲームに入り込むためのコードを 1 か所にまとめた小さな中核で、ほかの Mod や、その上に乗るライブラリ（前提 Mod の上の前提 Mod）に安定した API を提供します。

- ゲームの Options 画面から開く Mods 画面（Minecraft Forge の Mod 一覧のようなもので、Mod のオン・オフもできる）
- ゲームの Options 画面への設定の追加
- テキストや会話のイベント
- Direct3D 12 で安全なアセットの読み込み
- など

ゲームがアップデートされても、追従が必要なのはフレームワークだけになります。

> [!NOTE]
> 最新のリリースは **1.4.3** です。バージョンごとの変更は下の[リリース](#リリース)に、細かいところまでは [CHANGELOG.md](CHANGELOG.md) にあります。これからの予定：[docs/ROADMAP.ja.md](docs/ROADMAP.ja.md)。

## どこを読むか

| 知りたいこと | 読むところ |
|---|---|
| Mod を入れて遊ぶ、はじめての Mod、ライブラリを調べる | [Wiki](https://github.com/TomXV/dragnwash-modframework/wiki/Home-ja)：プレイヤー向けのページ、はじめての Mod の手順、ライブラリごとのリファレンス |
| この上で Mod を作る | [Playing well with others (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Playing-well-with-others-ja) |
| 目標と作業の順番 | [docs/DESIGN.ja.md](docs/DESIGN.ja.md) |
| 確認したゲームのビルド | [docs/GAME_BUILDS.md](docs/GAME_BUILDS.md) |

## 中身

| プラグイン | GUID | Mod が使えるもの |
|---|---|---|
| **Drag'n Wash ModFramework**（中核） | `com.tomxv.dragnwash.modframework` | オン・オフ、設定ページ、アイコン付きの Mods 画面（`ModFramework.Register`）、ゲームの Options 画面への行の追加（`GameOptions`）、サービスの登録（`Services`）、動作チェック（`GameHooks`）、`GameInfo` |
| **Text** | `com.tomxv.dragnwash.modframework.text` | ゲームが表示する前のテキストを見て置き換える（`GameText`） |
| **Dialogue** | `com.tomxv.dragnwash.modframework.dialogue` | これから表示される台詞や選択肢を、台詞 ID・話者・ノードつきで受け取る（`GameDialogue`） |
| **Tool window** | `com.tomxv.dragnwash.modframework.toolwindow` | 開発ツール用の共通の F1 ウィンドウ（Options → Mods の「Developer tools」をオンにするまで開かない）に、Mod ごとにタブを足す（`ToolWindow`） |
| **Assets** | `com.tomxv.dragnwash.modframework.assets` | どの言語でも表示できるフォント、テクスチャとアセットバンドルの読み込みを、Direct3D 12 でクラッシュさせずに行う（`GameFonts`、`GameAssets`） |
| **Flags and saves** | `com.tomxv.dragnwash.modframework.saves` | セーブスロット、フラグ、すべてのセーブの履歴（`GameSaves`、`GameFlags`） |
| **Inspector**（実験的） | `com.tomxv.dragnwash.modframework.inspector` | F1 の窓の Inspector タブ。読み込まれているシーンとオブジェクト、そのコンポーネントと値、ゲームのコード、読み込まれているものすべてを種類別に（[Inspector](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector-ja)） |
| **Overrides**（実験的） | `com.tomxv.dragnwash.modframework.overrides` | コードのない Mod を動かします。Inspector で作った、ゲームの値を書き換えるだけのフォルダーです（[Overrides](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides-ja)） |
| **Bridge**（実験的） | `com.tomxv.dragnwash.modframework.bridge` | この PC の AI クライアントに読み取り操作を MCP で渡し、コードのグラフのページを出します。既定はオフ（[Bridge](https://github.com/TomXV/dragnwash-modframework/wiki/Bridge-ja)） |
| **Graphs**（実験的） | `com.tomxv.dragnwash.modframework.graphs` | 何かを「する」コードのない Mod を動かします。*これが起きたら、これをする*。作るのは Bridge のページです（[Graphs](https://github.com/TomXV/dragnwash-modframework/wiki/Graphs-ja)） |

ライブラリはそれぞれ独自のバージョンを持つ別のプラグインです。使う Mod が必要とするものを入れてください。バージョンは [CHANGELOG.md](CHANGELOG.md) を参照してください。

### 更新のお知らせ

中核 1.1.0 から、入れている Mod に新しいリリースがあると、Mods 画面とタイトル画面でお知らせします。

- **確認するもの：** GitHub リポジトリを指定している Mod だけで、それぞれ 1 日に 1 回までです。
- **送るもの：** GitHub の公開 API（`api.github.com`）にリポジトリの最新リリースを問い合わせるだけで、あなたやゲーム、ほかの Mod についての情報は送りません。ほかの Web ページを開くときと同じく、GitHub には IP アドレスが伝わります。
- **すること：** ダウンロードやインストールはしません。Mods 画面からリリースページを開けるだけです。
- **止めるには：** **Options → Mods → Drag'n Wash ModFramework → Settings** で **Check for updates** を Off にするか、`BepInEx/config/com.tomxv.dragnwash.modframework.cfg` の `Check for updates = false` にしてください。

## リリース

1.0.0 以降、公開 API の互換性を壊す変更はメジャーバージョンを上げるときだけにします。すべての変更は [CHANGELOG.md](CHANGELOG.md) にあります。

- **1.4.3**（最新）：Bridge タブに **Open page** と **Graphs** のボタンが付き、エディターがひと押しで開きます。
- **1.4.2**：キーに反応するグラフが、同じキーを使っているほかの Mod の名前を出すようになりました（`ModFramework.WhoElseUses`）。
- **1.4.1**：**[Graphs](https://github.com/TomXV/dragnwash-modframework/wiki/Graphs-ja)** 0.1.0（何かを「する」コードのない Mod。*これが起きたら、これをする*）が入りました。次のものまで揃っています。
  - 最初の書き込み操作
  - Bridge のページのブロックとノードのエディター
  - グラフが何を変えるかの Mods 画面での表示、止めたときの巻き戻し、2 つの Mod が同じ値を変えたときの知らせ

  中核と preloader パッチャーは 1.4.1、Overrides は 0.1.1、Bridge は 0.1.1、Inspector は 1.1.1 になり、Inspector の History にはほかの Mod が変えたものも並ぶようになりました。
- **1.4.0**：
  - コードのない Mod（[Overrides](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides-ja)：Inspector で作った、値を書き換えるだけのフォルダー）
  - 各ライブラリができることを登録する[操作の登録簿](https://github.com/TomXV/dragnwash-modframework/wiki/Operations-ja)
  - その読み取り操作を MCP でこの PC の AI クライアントに渡す [Bridge](https://github.com/TomXV/dragnwash-modframework/wiki/Bridge-ja)
  - [コードのグラフ](https://github.com/TomXV/dragnwash-modframework/wiki/Code-graph-ja)
  - [Inspector](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector-ja) のオブジェクトエクスプローラー・Animator・Rigidbody・シーンとレベル

  中核と preloader パッチャーが 1.4.0、Tool window と Dialogue が 1.2.0、Text・Flags and saves・Inspector が 1.1.0、Assets が 1.2.0 です。新しいものはすべて実験的な扱いです。
- **1.3.0**：
  - 何が起きたかをゲームの外のウィンドウで知らせる[クラッシュレポート](https://github.com/TomXV/dragnwash-modframework/wiki/Crash-reports-ja)
  - それで突き止めた Direct3D 12 のクラッシュの修正（フォントのアトラスの転送を 1 フレームに 1 回に）
  - `GameOptions.AddSlider`

  Tool window と Assets は 1.1.1 になりました。
- **1.2.1**：2026 年 9 月 14 日のゲームのアップデート以降に作ったセーブを、Saves タブがまた見つけられるようにしました。
- **1.2.0**（中核）：
  - 開発者ツールのスイッチ（既定はオフ）
  - 設定ページの文字列とキー割り当ての入力欄
  - [`GameEvents`](https://github.com/TomXV/dragnwash-modframework/wiki/Playing-well-with-others-ja) と `SettingMeta`
  - ゲームを動かしたままの Mod のリロード
  - [外と通信する Mod の申告](https://github.com/TomXV/dragnwash-modframework/wiki/Going-online-ja)

  あわせて Tool window、Assets、Dialogue の各ライブラリが 1.1.0 になり（[Console](https://github.com/TomXV/dragnwash-modframework/wiki/Console-ja)、[テクスチャの差し替えとリロード](https://github.com/TomXV/dragnwash-modframework/wiki/Assets-ja)、[安定した行キー](https://github.com/TomXV/dragnwash-modframework/wiki/Dialogue-ja)）、新しい [Inspector](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector-ja) ライブラリ（1.0.0）が加わりました。新しい機能は実験的な扱いで、Inspector は今後も実験的な機能のままです。
- **1.1.2**：フレームワークのアイコンを、手作りのロゴの「Dg」に差し替えました。
- **1.1.1**：フレームワークのアイコンを追加しました。
- **1.1.0**：[更新のお知らせ](#更新のお知らせ)、共通インストーラー、Mods 画面からのアンインストールを追加しました。
- **1.0.0**：最初にこの上で動く Mod である [Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) の v1.0.0 と一緒にリリースしました。

## Mod を作る方へ

`DragNWash.ModFramework.dll`（と使うライブラリの DLL）を参照し、BepInEx が先に読み込むようそれぞれを依存関係として宣言します。

- **何に何を使うか**、Mod 同士を一緒に動かすためのルール：[Playing well with others (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Playing-well-with-others-ja)
- **プレイヤーがワンクリックで入れられるようにするには：** `mod-install.json` と一緒に共通インストーラーを同梱してください。[Installer (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Installer-ja)

```csharp
[BepInPlugin("com.example.mymod", "MyMod", "1.0.0")]
[BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
public class MyMod : BaseUnityPlugin
{
    private void Awake()
    {
        ModFramework.Ready += () =>
        {
            if (GameInfo.IsDirect3D12)
            {
                // フォントやテクスチャは、後回しにせずここで読み込む
            }
        };
    }
}
```

## ビルド

1. .NET SDK を入れ、ゲームに BepInEx 5.4.23.5 を入れておく
2. 自分のゲームから参照アセンブリをコピーする（コミットはしません）

   ```bash
   pwsh tools/copy-libs.ps1
   ```

   ゲームが既定の Steam ライブラリにない場合は `-GamePath` を指定します
3. 中核、プリローダーパッチャー、ライブラリをビルドする

   ```bash
   dotnet build src/DragNWash.ModFramework/DragNWash.ModFramework.csproj -c Release
   ```

   `src/DragNWash.ModFramework.*` の各プロジェクトも同じようにビルドします

> [!TIP]
> Docker があれば、`docker compose run --rm checks` で CI と同じ検査を、`docker compose run --rm build` で中核とライブラリのビルドを、CI と同じイメージの中で回せます（[docs/DOCKER.ja.md](docs/DOCKER.ja.md)）。手元には何も入りません。

DLL はそれぞれのプロジェクトの `bin/Release/` にできます。試すときは次のようにコピーしてください。

- プラグインの DLL は 1 つずつ `<ゲーム>/BepInEx/plugins/<アセンブリ名>/` に
- `DragNWash.ModFramework.Preloader.dll` は `<ゲーム>/BepInEx/patchers/` に

### GitHub でビルドする

Actions の **Build** ワークフローが、リリース用の zip を GitHub 上で作ります。

- **動くとき：** `main` への push、`v*` のタグ、手動実行のとき。PR では動かないので、フォークからトークンには触れません。
- **すること：** 参照アセンブリを非公開リポジトリ（`TomXV/dragnwash-libs`。公開はしません）から `LIBS_TOKEN` シークレットで取り、Windows の runner で `tools/pack.ps1` を回して、`release/DragNWash.ModFramework-<version>.zip` を成果物として残します。
- **タグのとき：** zip を添えた **下書き** のリリースも作ります。ノートを書いて公開するのは人の手です。
- **ゲームが更新されたら：** `tools/copy-libs.ps1` でゲームから取り直して、非公開リポジトリを更新してください。

## このリポジトリのルール

- ゲームのファイル、BepInEx のバイナリ、`libs/` の中身は絶対にコミットしません。プッシュとプルリクエストのたびに自動でチェックします
- ゲーム由来のものは [docs/CONTENT_POLICY.ja.md](docs/CONTENT_POLICY.ja.md) に従います。手で作ったものや手を加えて新しくしたものはよく、ゲームのデータそのままは入れません
- ゲームのクラスに触るコードは `internal` にとどめ、Mod にはフレームワーク自身の型だけを見せます

## 参加について

- **コントリビューション：** [CONTRIBUTING.ja.md](CONTRIBUTING.ja.md)（準備の手順、上の決まりの実際、プルリクエストに書くこと）
- **行動規範：** [docs/CODE_OF_CONDUCT.ja.md](docs/CODE_OF_CONDUCT.ja.md)
- **セキュリティ：** 脆弱性は Issue ではなく非公開で報告してください。[SECURITY.ja.md](SECURITY.ja.md)
- **スポンサー：** [GitHub Sponsors](https://github.com/sponsors/TomXV)。そうしたくて、できるならば。いずれにせよフレームワークは無料のままです。

## 開発者の方へ

本プロジェクトは非公式のファン制作物で、Gator Dragon Games とは無関係です。

- ゲームのアセットやコードを、そのままの形では含みません（[docs/CONTENT_POLICY.ja.md](docs/CONTENT_POLICY.ja.md)）。
- ゲームのファイルを書き換えることもありません（BepInEx が実行時に読み込みます）。

開発チームの方で懸念がある場合は、このリポジトリの Issue かメンテナーへの連絡でお知らせください。ご希望に応じて修正または公開停止します。

## クレジット

- 冒頭の**ロゴ**と、Mods 画面のフレームワークの**アイコン**は、**NotaGames** さん（[@NotaGames](https://github.com/NotaGames)）がゲームのロゴをもとに描いたものです。ゲームの開発元からも問題ないとの返事をいただいています（[#15](https://github.com/TomXV/dragnwash-modframework/issues/15)）。許可を得て使っており、MIT ライセンスの対象外です。
- Options 画面の **Mods ボタン**（`ModsButton0.png`、`ModsButton1.png`）は、**Mister ERIO** さん（[@mistererio](https://github.com/mistererio)）がこのフレームワークのために描き、許可を得て使っています。ゲームの絵ではなく Mister ERIO さんの作品で、MIT ライセンスの対象外です。

## ライセンス

[MIT](LICENSE)。ただし、上の「クレジット」に挙げた絵を除きます。
