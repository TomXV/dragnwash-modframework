# Mod 作者向けガイド

[English](GUIDE.md)

フレームワークの上に Drag'n Wash の Mod を作り、ほかのどの Mod とも一緒に安全に動かすための手引きです。フレームワークの第一の原則は **すべての Mod が一緒に安全に動くこと** で、このガイドはそれをコードでどう守るかを書いたものです。

## 準備

使うフレームワークの DLL を参照し（このリポジトリからビルドするか、リリースから取ります）、それぞれを依存関係として宣言します。BepInEx が先に読み込み、足りないときはプレイヤーに知らせます。

```csharp
[BepInPlugin("com.example.mymod", "My Mod", "1.0.0")]
[BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency(GameText.Guid, BepInDependency.DependencyFlags.HardDependency)]
public class MyMod : BaseUnityPlugin
{
    private void Awake()
    {
        ModFramework.Register(new ModInfo
        {
            Guid = "com.example.mymod",
            DisplayName = "My Mod",
            Description = "One or two sentences on what it does.",
            Authors = new[] { "Me" },
            Website = "https://github.com/me/mymod",
            UpdateRepository = "me/mymod",
            IconPath = Path.Combine(Path.GetDirectoryName(Info.Location), "icon.png"),
        });
    }
}
```

`UpdateRepository`（中核 1.1.0 以降）を書くと、GitHub リポジトリにプレイヤーが使っているものより新しいリリースがあるとき、Mods 画面がそれを知らせ、リリースページを開けるようにします。リリースのタグはプラグインのバージョン（`v1.2.0` や `1.2.0`）にし、テスト版はプレリリースにしてください。プレリリースは知らせません。GitHub でリリースを公開していなければ書かないでください。

入っているものより新しいライブラリが必要なら `[BepInDependency(GameText.Guid, "0.1.1")]` のように書けます。Mods 画面では、ライブラリ名の横にそのバージョンが出ます。

## 何を使うか

| やりたいこと | 使うもの | 代わりにやめること |
|---|---|---|
| Mod の説明をプレイヤーに見せる | `ModFramework.Register(ModInfo)` | なし（普通のプラグインも一覧に出ますが、情報が少なくなります） |
| プレイヤーに設定を用意する | BepInEx の `Config.Bind`（Mods 画面が設定ページを作ります）、ゲームの Options 画面には `GameOptions.AddChoice` / `AddToggle` | 独自の設定メニュー |
| 設定の並び順、上級者向けの項目を隠す、表示名、再起動が必要な印 | `ConfigDescription` のタグに `SettingMeta` / `SectionMeta`（実験的） | なし（省略するとセクション名とキー名の順に並びます） |
| 表示前のテキストを変える | `GameText.AddRewriter`（ライブラリ **Text**） | `TMP_Text.text` や `SetText` へのパッチ |
| 表示中の台詞と話者を知る | `GameDialogue.LineShowing`、`OptionShowing`、`TryGetLine`（ライブラリ **Dialogue**） | Yarn Spinner の表示部品へのパッチ |
| ゲームの更新で本文が変わった台詞のデータを見つけ直す | `LineKey`、`LineResolver`（ライブラリ **Dialogue**、実験的。[STABLE_LINE_KEYS.ja.md](STABLE_LINE_KEYS.ja.md)） | 英文だけをキーにする |
| 開発・デバッグ用ツールを足す | `ToolWindow.AddTab`（ライブラリ **Tool window**） | 独自の `OnGUI` ウィンドウ、カーソルの解放、入力の遮断 |
| 打ち込めるコマンドを用意する、ゲーム内でログを見る | `ToolWindow.AddCommand`、**Console** タブ（ライブラリ **Tool window**、実験的。[CONSOLE.ja.md](CONSOLE.ja.md)） | 独自のコンソール |
| ゲームのフォントにない文字を表示する | `GameFonts.Prepare` と `GameFonts.SetLanguage`（ライブラリ **Assets**） | `TMP_Settings.fallbackFontAssets` への直接の追加 |
| テクスチャやアセットバンドルを読み込む | `GameAssets.LoadTexture`、`GameAssets.LoadBundle`（ライブラリ **Assets**）を `Awake` から | ゲーム中の読み込み |
| ゲームのテクスチャを自分のものに差し替える、何が読み込まれているか見る | Mod フォルダの `assets/textures/<名前>.png`、`AssetCatalog`（ライブラリ **Assets**、実験的。[ASSET_TOOL.ja.md](ASSET_TOOL.ja.md)） | マテリアルのテクスチャを自分で入れ替える |
| セーブスロットやフラグを読み書きする | `GameSaves`、`GameFlags`（ライブラリ **Flags and saves**） | `savegame.dgn` を自分で書き換えること |
| ほかの Mod に API を提供する | `Services.Register<T>` と `Services.Get<T>` | リフレクションで探させる public static フィールド |
| パッチを当てるゲームのメソッドがまだあるか確かめる | `GameHooks.Require` | とりあえずパッチを当てること |
| オブジェクト、コンポーネント、マテリアルが何を持っているかを見て、値を試す | **Inspector** タブ（F1。独立したライブラリ）と `Inspector.Inspect(target)`（実験的） | 逆コンパイルと、当て推量ごとのビルドし直し |
| シーンの読み込み時、ゲームの起動時、終了時に何かする | `GameEvents.OnSceneLoaded`、`OnGameStarted`、`OnQuitting`（実験的） | `SceneManager.sceneLoaded` や `Application.quitting` への直接の登録 |

## ルール

1. **フレームワークがフックしている場所にパッチを当てない。** テキスト、会話の表示部品、Options 画面の設定一覧、`GUI.Button`、ゲームのカーソルロックには、共有のフックが 1 つずつあります。同じメソッドに 2 つ目のパッチを当てると、ほかのすべての Mod から見える結果が変わります。Mods 画面は、同じゲームのメソッドにパッチを当てている Mod に **競合** の印を付けます
2. **パッチの前に確認する。** 自分でゲームのメソッドにパッチが必要なときは、先にメソッドを探し、`GameHooks.Require(自分のGUID, "機能名", method != null, "Type.Method")` を呼びます。ゲームのアップデートで消えていたらその機能は使わずに済ませ、Mods 画面に「使えない機能」として表示されます。ゲームはクラッシュしません
3. **ゲームに例外を投げない。** Harmony のパッチは `try`/`catch` で囲んでログに出します。フレームワークのコールバック（テキストの書き換え、会話イベント、ツールウィンドウのタブ、設定のコールバック）はすでに隔離されていて、例外は Mod の GUID 付きでログに出てほかの Mod は動き続けますが、その機能は止まります
4. **テクスチャ・フォント・バンドルは起動時に読み込む。** このゲームの Unity のバージョンと Direct3D 12 の組み合わせでは、ゲーム中にテクスチャを作ったりアップロードしたりするとクラッシュすることがあります（Unity UUM-140564）。`Awake` で行ってください。ツールウィンドウのタブで ASCII 以外の文字を描くときは、その文字を `Awake` か `Update` から `ToolWindow.PrepareCharacters` に渡します
5. **ほかの Mod のサービスを置き換えない。** `Services.Register` は同じインターフェースの 2 つ目の提供者を拒否します。サービスは `Services.TryGet<T>(out var service, 最低バージョン)` で求め、無いときも動くようにします
6. **公開 API にゲームの型を出さない。** ほかの Mod のためのライブラリなら、独自の型を公開します。ゲームがアップデートされても、変わるのはライブラリの中身だけで済みます
7. **ゲームのファイルを配布しない。** アセット、台本の文章、ゲームの DLL をリポジトリやリリースに入れないでください
8. **開発者向けの機能は、開発者ツールのスイッチの内側に置く。** 書き出し、ホットリロード、デバッグ用のキーや窓は、`DeveloperTools.Enabled` が真のときだけ動かす（あとから始めるなら `DeveloperTools.WhenEnabled`）。Mod を入れただけの人には見えないようにするためです。Tool window のタブは、すでにそうなっています。
9. **ゲームの出来事は `GameEvents` から受け取る。** `SceneManager.sceneLoaded` に直接つないだ処理が例外を投げると、あとから登録したすべての Mod の処理が止まり、どの Mod のせいかも分かりません。`GameEvents.OnSceneLoaded(自分のGUID, ...)` は Mod ごとに切り離して呼び、失敗した Mod を Mods 画面に名前つきで出し、3 回続けて失敗した処理をそのセッションでは止めます。

10. **リロードに頼る前に、そう宣言する。** ゲームを動かしたまま Mod がリロードされるのは、`ModInfo.Reloadable = true` にした（または `[ReloadableMod]` を付けた）ときだけで、そのとき [ゲームを動かしたまま Mod をリロードする](#ゲームを動かしたまま-mod-をリロードする) の約束を守ることになります。
11. **外と通信する前に、そう申告する。** Mod が接続するホストをすべて `ModInfo.Network` に並べ、何のためか・何を送るか・どう止めるかを書きます。Mods 画面でプレイヤーに表示されます。プレイヤーについての情報（名前、セーブ、入力した文字、その人を追える ID）は、本人がオンにしてから送ります。申告せずに通信した Mod には、Mods 画面で印が付きます。[docs/NETWORK.ja.md](NETWORK.ja.md) を参照。

## ゲームを動かしたまま Mod をリロードする

実験的な機能で、中核 1.2.0 から、開発者ツールがオンの間だけ。ビルドすると、再起動なしで新しい DLL が動いている版と入れ替わります（`[Developer] WatchMods`、または Console の `mods reload <guid>`）。ライブラリはリロードされません。リロードできると宣言した Mod が守ること：

- Harmony の ID は自分の GUID（`new Harmony(MyMod.Guid)`）。パッチはこれで見つけて外します。
- 自前の static フィールドで抱えず、フレームワーク経由で登録する（`ModFramework.Register`、`AddTab`、`AddCommand`、`AddRewriter`、`GameEvents`、`Services`、`GameOptions`）。フレームワークが知らないものは外せません。ライブラリのイベントにつないだ処理はアセンブリ単位で外れます。
- ゲームに渡したもの（コルーチン、`DontDestroyOnLoad` のオブジェクト、ファイルの監視）は `OnDestroy` で片付ける。
- `Awake` に、Direct3D 12 でゲームの途中に走らせて危ないこと（テクスチャのアップロード）を置かない。置くなら `GameFonts.RuntimeUploadsAreSafe` で分ける。

ビルドを、入っている DLL の隣に `<Mod>.dll.new` として届ければ、あとはフレームワークがやります。Windows では動いている DLL を Mono が掴んでいて上書きできないので、ゲームは `.new` からすぐ読み直し、次の起動時にプリローダーのパッチャーがそれを本物の DLL にします。

```xml
<!-- .csproj に。ビルド後に DLL が .dll.new としてゲームへ行き、動いているゲームがそれを読み直します。 -->
<PropertyGroup>
  <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Drag'n Wash</GameDir>
</PropertyGroup>
<Target Name="CopyToGame" AfterTargets="Build" Condition="Exists('$(GameDir)')">
  <Copy SourceFiles="$(TargetPath)" DestinationFiles="$(GameDir)\BepInEx\plugins\$(AssemblyName)\$(AssemblyName).dll.new" />
</Target>
```

残るもの：古いアセンブリ（Mono は外せません。リロード 1 回あたり数百 KB）と、ゲームがまだ持っている古い版のオブジェクト。古い版を外す前に失敗したリロードは古い版を動かしたままにし、外したあとで失敗したものはログにそう出て、再起動が要ります。詳しくは [MOD_RELOAD.ja.md](MOD_RELOAD.ja.md)。

## ライブラリの作り方

ライブラリは、ほかの Mod が依存する普通の BepInEx プラグインです。

- 中核に依存し、`IsLibrary = true` で登録します。Mods 画面にライブラリとして表示され、それを必要とする Mod の一覧が出て、オフにする前に確認されます
- 独自の GUID とバージョンを持ち、セマンティックバージョニングに従います。公開 API を互換性のない形で変えるときはメジャーバージョンを上げます（0.x の間はマイナーバージョンを上げて、変更点を書き残します）
- ゲームへのフックは 1 回だけ、順番が大事なら `Priority.First` で当て、Mod には順番を明示して登録してもらいます
- パッチ対象はすべて `GameHooks.Require` で確認し、Mod が判断できるよう `IsAvailable` を公開します
- Mod のコールバックは 1 つずつ実行し、例外を捕まえて Mod の GUID 付きでログに出し、次に進みます
- ゲームの型に触るものは `internal` にします

## テスト

- 自分の Mod とフレームワークだけで試し、次にほかの Mod と一緒に試します。Mods 画面の **競合** の印と、`BepInEx/LogOutput.log` の「Game methods patched by more than one mod」の行で、ほかの Mod とゲームのメソッドを共有しているかが分かります
- Direct3D 12（Windows の既定）で試し、できれば Steam Deck でも試します
- Mods 画面で自分の Mod をオフにして再起動し、無くてもゲームが動くことを確かめます
