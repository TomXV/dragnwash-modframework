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
| 表示前のテキストを変える | `GameText.AddRewriter`（ライブラリ **Text**） | `TMP_Text.text` や `SetText` へのパッチ |
| 表示中の台詞と話者を知る | `GameDialogue.LineShowing`、`OptionShowing`、`TryGetLine`（ライブラリ **Dialogue**） | Yarn Spinner の表示部品へのパッチ |
| 開発・デバッグ用ツールを足す | `ToolWindow.AddTab`（ライブラリ **Tool window**） | 独自の `OnGUI` ウィンドウ、カーソルの解放、入力の遮断 |
| ゲームのフォントにない文字を表示する | `GameFonts.Prepare` と `GameFonts.SetLanguage`（ライブラリ **Assets**） | `TMP_Settings.fallbackFontAssets` への直接の追加 |
| テクスチャやアセットバンドルを読み込む | `GameAssets.LoadTexture`、`GameAssets.LoadBundle`（ライブラリ **Assets**）を `Awake` から | ゲーム中の読み込み |
| セーブスロットやフラグを読み書きする | `GameSaves`、`GameFlags`（ライブラリ **Flags and saves**） | `savegame.dgn` を自分で書き換えること |
| ほかの Mod に API を提供する | `Services.Register<T>` と `Services.Get<T>` | リフレクションで探させる public static フィールド |
| パッチを当てるゲームのメソッドがまだあるか確かめる | `GameHooks.Require` | とりあえずパッチを当てること |

## ルール

1. **フレームワークがフックしている場所にパッチを当てない。** テキスト、会話の表示部品、Options 画面の設定一覧、`GUI.Button`、ゲームのカーソルロックには、共有のフックが 1 つずつあります。同じメソッドに 2 つ目のパッチを当てると、ほかのすべての Mod から見える結果が変わります。Mods 画面は、同じゲームのメソッドにパッチを当てている Mod に **競合** の印を付けます
2. **パッチの前に確認する。** 自分でゲームのメソッドにパッチが必要なときは、先にメソッドを探し、`GameHooks.Require(自分のGUID, "機能名", method != null, "Type.Method")` を呼びます。ゲームのアップデートで消えていたらその機能は使わずに済ませ、Mods 画面に「使えない機能」として表示されます。ゲームはクラッシュしません
3. **ゲームに例外を投げない。** Harmony のパッチは `try`/`catch` で囲んでログに出します。フレームワークのコールバック（テキストの書き換え、会話イベント、ツールウィンドウのタブ、設定のコールバック）はすでに隔離されていて、例外は Mod の GUID 付きでログに出てほかの Mod は動き続けますが、その機能は止まります
4. **テクスチャ・フォント・バンドルは起動時に読み込む。** このゲームの Unity のバージョンと Direct3D 12 の組み合わせでは、ゲーム中にテクスチャを作ったりアップロードしたりするとクラッシュすることがあります（Unity UUM-140564）。`Awake` で行ってください。ツールウィンドウのタブで ASCII 以外の文字を描くときは、その文字を `Awake` か `Update` から `ToolWindow.PrepareCharacters` に渡します
5. **ほかの Mod のサービスを置き換えない。** `Services.Register` は同じインターフェースの 2 つ目の提供者を拒否します。サービスは `Services.TryGet<T>(out var service, 最低バージョン)` で求め、無いときも動くようにします
6. **公開 API にゲームの型を出さない。** ほかの Mod のためのライブラリなら、独自の型を公開します。ゲームがアップデートされても、変わるのはライブラリの中身だけで済みます
7. **ゲームのファイルを配布しない。** アセット、台本の文章、ゲームの DLL をリポジトリやリリースに入れないでください

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
