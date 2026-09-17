<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="images/logo-notagames.png"><img src="images/logo-notagames-panel.png" alt="Drag'n Wash ModFramework" width="420"></picture></p>

# Drag'n Wash ModFramework

[English](README.md)

[Drag'n Wash](https://store.steampowered.com/app/4739660/) 用の前提 Mod（BepInEx 5）です。ゲームに入り込むためのコードを 1 か所にまとめた小さな中核で、ほかの Mod や、その上に乗るライブラリ（前提 Mod の上の前提 Mod）に安定した API を提供します。ゲームの Options 画面から開く Mods 画面（Minecraft Forge の Mod 一覧のようなもので、Mod のオン・オフもできる）、ゲームの Options 画面への設定の追加、テキストや会話のイベント、Direct3D 12 で安全なアセットの読み込みなどです。ゲームがアップデートされても、追従が必要なのはフレームワークだけになります。

> [!NOTE]
> `main` の **中核は 1.2.0** で、まだリリースしていません。最新のリリースは 1.1.2（ライブラリは 1.0.0）です。1.2.0 では開発者ツールのスイッチ（既定はオフ）、設定ページの文字列とキー割り当ての入力欄、[`GameEvents`](docs/GUIDE.ja.md) と `SettingMeta` が加わり、あわせて Tool window、Assets、Dialogue の各ライブラリが 1.1.0 になり、新しい [Inspector](docs/INSPECTOR.ja.md) ライブラリ（1.0.0）が加わります（[Console](docs/CONSOLE.ja.md)、[テクスチャの差し替えとリロード](docs/ASSET_TOOL.ja.md)、[安定した行キー](docs/STABLE_LINE_KEYS.ja.md)）。リリースまでは実験的な扱いで、Inspector はリリース後も実験的な機能のままです。1.1.0 で [更新のお知らせ](#更新のお知らせ)、共通インストーラー、Mods 画面からのアンインストールを追加し、1.1.1 でフレームワークのアイコンを追加、1.1.2 でそのアイコンを手作りのロゴの「Dg」に差し替えました。1.0.0 は、最初にこの上で動く Mod である [Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) の v1.0.0 と一緒にリリースしました。1.0.0 以降、公開 API の互換性を壊す変更はメジャーバージョンを上げるときだけにします。[CHANGELOG.md](CHANGELOG.md) を参照してください。

目標と作業の順番は [docs/DESIGN.ja.md](docs/DESIGN.ja.md)、この上での Mod の作り方は [docs/GUIDE.ja.md](docs/GUIDE.ja.md)、確認したゲームのビルドは [docs/GAME_BUILDS.md](docs/GAME_BUILDS.md) を参照してください。[Wiki](https://github.com/TomXV/dragnwash-modframework/wiki/Home-ja) には、プレイヤー向けのページ、はじめての Mod の手順、ライブラリごとのリファレンスがあります。

## 中身

| プラグイン | GUID | Mod が使えるもの |
|---|---|---|
| **Drag'n Wash ModFramework**（中核） | `com.tomxv.dragnwash.modframework` | オン・オフ、設定ページ、アイコン付きの Mods 画面（`ModFramework.Register`）、ゲームの Options 画面への行の追加（`GameOptions`）、サービスの登録（`Services`）、動作チェック（`GameHooks`）、`GameInfo` |
| **Text** | `com.tomxv.dragnwash.modframework.text` | ゲームが表示する前のテキストを見て置き換える（`GameText`） |
| **Dialogue** | `com.tomxv.dragnwash.modframework.dialogue` | これから表示される台詞や選択肢を、台詞 ID・話者・ノードつきで受け取る（`GameDialogue`） |
| **Tool window** | `com.tomxv.dragnwash.modframework.toolwindow` | 開発ツール用の共通の F1 ウィンドウ（Options → Mods の「Developer tools」をオンにするまで開かない）に、Mod ごとにタブを足す（`ToolWindow`） |
| **Assets** | `com.tomxv.dragnwash.modframework.assets` | どの言語でも表示できるフォント、テクスチャとアセットバンドルの読み込みを、Direct3D 12 でクラッシュさせずに行う（`GameFonts`、`GameAssets`） |
| **Flags and saves** | `com.tomxv.dragnwash.modframework.saves` | セーブスロット、フラグ、すべてのセーブの履歴（`GameSaves`、`GameFlags`） |

ライブラリはそれぞれ独自のバージョンを持つ別のプラグインです。使う Mod が必要とするものを入れてください。バージョンは [CHANGELOG.md](CHANGELOG.md) を参照してください。

### 更新のお知らせ

中核 1.1.0 から、入れている Mod に新しいリリースがあると、Mods 画面とタイトル画面でお知らせします。確認するのは GitHub リポジトリを指定している Mod だけで、それぞれ 1 日に 1 回までです。GitHub の公開 API（`api.github.com`）にリポジトリの最新リリースを問い合わせるだけで、あなたやゲーム、ほかの Mod についての情報は送りません。ほかの Web ページを開くときと同じく、GitHub には IP アドレスが伝わります。ダウンロードやインストールはせず、Mods 画面からリリースページを開けるだけです。止めるには **Options → Mods → Drag'n Wash ModFramework → Settings** で **Check for updates** を Off にするか、`BepInEx/config/com.tomxv.dragnwash.modframework.cfg` の `Check for updates = false` にしてください。

## Mod を作る方へ

`DragNWash.ModFramework.dll`（と使うライブラリの DLL）を参照し、BepInEx が先に読み込むようそれぞれを依存関係として宣言します。何に何を使うか、Mod 同士を一緒に動かすためのルールは [docs/GUIDE.ja.md](docs/GUIDE.ja.md) にあります。プレイヤーがワンクリックで入れられるようにするには、`mod-install.json` と一緒に共通インストーラーを同梱してください：[docs/INSTALLER.ja.md](docs/INSTALLER.ja.md)。

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

DLL はそれぞれのプロジェクトの `bin/Release/` にできます。試すときは、プラグインの DLL を 1 つずつ `<ゲーム>/BepInEx/plugins/<アセンブリ名>/` に、`DragNWash.ModFramework.Preloader.dll` を `<ゲーム>/BepInEx/patchers/` にコピーしてください。

### GitHub でビルドする

Actions の **Build** ワークフローが、リリース用の zip を GitHub 上で作ります。`main` への push、`v*` のタグ、手動実行のときに動き、参照アセンブリを非公開リポジトリ（`TomXV/dragnwash-libs`。公開はしません）から `LIBS_TOKEN` シークレットで取り、Windows の runner で `tools/pack.ps1` を回して、`release/DragNWash.ModFramework-<version>.zip` を成果物として残します。タグのときは、zip を添えた **下書き** のリリースも作ります。ノートを書いて公開するのは人の手です。PR では動かないので、フォークからトークンには触れません。ゲームが更新されたら、`tools/copy-libs.ps1` でゲームから取り直して、非公開リポジトリを更新してください。

## このリポジトリのルール

- ゲームのファイル、BepInEx のバイナリ、`libs/` の中身は絶対にコミットしません。プッシュとプルリクエストのたびに自動でチェックします
- ゲームのクラスに触るコードは `internal` にとどめ、Mod にはフレームワーク自身の型だけを見せます

## 開発者の方へ

本プロジェクトは非公式のファン制作物で、Gator Dragon Games とは無関係です。ゲームのアセットやコードは含まず、ゲームのファイルを書き換えることもありません（BepInEx が実行時に読み込みます）。開発チームの方で懸念がある場合は、このリポジトリの Issue かメンテナーへの連絡でお知らせください。ご希望に応じて修正または公開停止します。

## クレジット

- 冒頭の**ロゴ**と、Mods 画面のフレームワークの**アイコン**は、**NotaGames** さん（[@NotaGames](https://github.com/NotaGames)）がゲームのロゴをもとに描いたものです。ゲームの開発元からも問題ないとの返事をいただいています（[#15](https://github.com/TomXV/dragnwash-modframework/issues/15)）。許可を得て使っており、MIT ライセンスの対象外です。
- Options 画面の **Mods ボタン**（`ModsButton0.png`、`ModsButton1.png`）は、**Mister ERIO** さん（[@mistererio](https://github.com/mistererio)）がこのフレームワークのために描き、許可を得て使っています。ゲームの絵ではなく Mister ERIO さんの作品で、MIT ライセンスの対象外です。

## ライセンス

[MIT](LICENSE)。ただし、上の「クレジット」に挙げた絵を除きます。
