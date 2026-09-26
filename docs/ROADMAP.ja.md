# ロードマップ

[English](ROADMAP.md)

Drag'n Wash ModFramework のこれからの予定です。予定は変わることがあり、日付は近いものだけ書いています。更新日：2026-09-26。

判断の基準は変わりません。すべての Mod が一緒に安全に動くこと。そして、ほかの人が引き継げるように、それぞれの部分を小さく保つことです。

## リリース済み：1.6.0（2026-09-26）

中核、preloader パッチャー、全部のライブラリが 1.6.0 になります。全部の一覧は [CHANGELOG.md](../CHANGELOG.md) にあります。

- **ゲームの前に動くランチャー。** `Launcher.exe` が Steam の起動オプションから動きます。ゲームの更新確認で新しい版が見つかっていれば、ゲームの前に一覧を出して、選んだものを入れます。サイズと SHA-256 で確かめ、先にバックアップを取ります。ネットにつなぐのは、更新を押したときだけです。
- **Mods 画面から更新。** インストーラーで入れた Mod には **更新する** が出ます。ゲームを終了して、ランチャーが更新し、ゲームを起動し直します。
- **インストーラーを作り直し。** Install.exe はランチャーと同じ見た目になり、Steam の起動オプションも設定し、アンインストールの進み具合も出します。WebView2 がないときは、今までの窓が開きます。
- **macOS。** macOS でも F1 の窓の文字が出るようになりました（223n さん、ありがとうございます）。macOS でゲームを Mod 付きで動かすのは、今のところ Drag'n Wash Localization の試験版のインストールスクリプトだけです（[#85](https://github.com/TomXV/dragnwash-modframework/issues/85)）。

## リリース済み：1.5.0（2026-09-23）

中核、preloader パッチャー、全部のライブラリが 1.5.0 になります。これからは同じ番号でそろえます。全部の一覧は [CHANGELOG.md](../CHANGELOG.md) にあります。

- **見た目をまるごと新しく。** Mods 画面は、すりガラス（パネルの後ろにぼかしたゲームの画面）の上の、設定アプリのような見た目になりました。検索、絞り込み、Mod のページごとのタブ、設定は 1 項目 1 行です。F1 のウィンドウはタブを 1 つずつ見直しました。お知らせの帯、押した場所でそのまま聞く確認、前に閉じた場所で開くウィンドウ、1 列のタブです。
- **速く、安全に。** 予備フォントが描いた文字は起動をまたいで取っておき、F1 のウィンドウのフォントは初めて開いたときに作ります。復元や同じ秒に撮ったスナップショットで、セーブのスナップショットが消えることもなくなりました。
- **インストーラーが ModFramework を取ってくる。** 専用のリリースから取ってきて、サイズと SHA-256 で確かめます。ゲームのフォルダーに別の Mod ローダーがあるときは、何も変えずに止まります。

## リリース済み：1.4.3（2026-09-20）

- **エディターへの入り口。** Bridge タブに **Open page**（コードのグラフ）と **Graphs**（エディター）のボタンを置き、窓が狭いときはボタンが折り返すようにしました。これまでページは、Inspector の Code 表示のメソッドからしか開けませんでした。

## リリース済み：1.4.2（2026-09-20）

- **どの Mod がどのキーを使っているか。** `key.pressed` に答えるグラフが、同じキーに設定を持つほかの Mod の名前を出します（ログ、Console、Mods 画面、ページ）。キーは誰のものでもないので拒否はせず、人が見るところに 1 度だけ書きます（`ModFramework.WhoElseUses`）。

## リリース済み：1.4.1（2026-09-20）

- **Graphs 0.1.0：何かをする、コードのない Mod**（[計画](API_PLAN.ja.md)の第 4 段階、[設計](GRAPHS.ja.md)）。`mod.json` と `graphs/*.json` の入ったフォルダーが、各ライブラリの出すイベントに答え、登録された操作を呼びます。ファイルは動く前に丸ごと検査され、すべてのグラフで 1 フレームあたり 1 ミリ秒を分け合い、3 回続けて失敗したグラフは変更を戻してオフになります。最初の書き込みは Overrides ライブラリの `objects.member.set`・`objects.material.set`・`objects.active.set`、エディター（ドラッグできるブロックと、同じグラフのノード表示）は Bridge のページの Graphs タブです。
- **誰が何を変えたか。** 書き込み操作による変更は、頼んだ人の名前つきで Inspector の History に並び、そこから戻せます。2 つの Mod が同じ値を変えたときは、ログとその Mod の Graphs ページで両方の名前が出ます。取り消しは、自分が書いた値だけを戻します。このうち 3 つは 1.4.0 のリリースノートに書いてありながら、実際には入っていませんでした。

## リリース済み：1.4.0（2026-09-20）

Drag'n Wash Localization v1.4.0 と同時にリリースしました。中核と preloader パッチャーが 1.4.0、Tool window と Dialogue が 1.2.0、Text・Flags and saves・Inspector が 1.1.0、Assets が 1.2.0 になります。以下はすべて実験的な扱いです。全部の一覧は [CHANGELOG.md](../CHANGELOG.md) にあります。

- **Overrides 0.1.0：コードのない Mod。** `mod.json` と `overrides/*.json` だけのフォルダーが、コンポーネントのフィールドやマテリアルのプロパティを書き換えます。ほかの Mod と同じく Mods 画面に出てオフにでき、きれいに元に戻せます。2 つの Mod が同じところを書き換えたときは、ログに両方の名前を出します。Inspector の History から、そこでの編集をこの形の Mod として書き出せます（[Overrides（wiki）](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides-ja)）。
- **操作の登録簿と、そこから出てきたもの**（[計画](API_PLAN.ja.md)の第 1〜3 段階）。各ライブラリが、できることを名前つきの操作（`library.noun.verb`）として登録し、引数は検査して素の値で返します。その 1 つの登録簿から、Console の `op`、**Bridge 0.1.0**（AI クライアント向けの読み取り操作を MCP で。この PC の中だけ、既定はオフ、トークンと、Web ページを断る入口つき）、**コードのグラフ**（ゲームのコードをブロックと分岐で。ページと、ゲームなしで動く Windows のアプリ）ができました。第 4 段階の「ブロックとノードで組み立てる」は、1.4.1 の **Graphs** ライブラリです（[GRAPHS.ja.md](GRAPHS.ja.md)）。
- **Inspector のオブジェクトエクスプローラー。** 読み込まれているものすべてを種類別に（テクスチャ、マテリアル、メッシュ、シェーダー、音、アニメーション、フォント、ScriptableObject に入ったゲームのデータ）Unity の Project ウィンドウのように並べ、**Used by** でどこで使われているかを調べられます。一覧は矢印キーで、Steam Deck やゲームパッドでは十字キーでたどれます（[設計](OBJECT_EXPLORER.ja.md)）。
- **言語ごとのテクスチャの差し替え**（Assets 1.2.0）。特定の言語を使っている間だけ効き、`fallback.txt` に書いた言語へたどり、元に戻せて、Direct3D 12 では再起動を待ちます。これを土台に Drag'n Wash Localization の絵の翻訳ができています（[設計](https://github.com/TomXV/dragnwash-localization/blob/main/docs/TRANSLATED_TEXTURES.ja.md)）。
- Inspector の Animator と Rigidbody、シーンとレベル、クラッシュレポートの後始末、Tool window の Steam Deck 対応（トラックパッドの押下が本物のマウスボタンに、十字キーが本物の矢印キーに）。

## リリース済み：1.3.0（2026-09-19）

Drag'n Wash Localization v1.3.0 と一緒にリリースしました。

- クラッシュレポート：セッションごとの記録、クラッシュやフリーズのあとのレポート（Unity のクラッシュダンプや、フリーズ時のダンプつき）、何が起きたかを知らせるゲームの外のウィンドウ（`CrashReporter.exe`、Windows）（[Crash reports (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Crash-reports-ja)）。
- それで突き止めた、Tool window を開いたときの Direct3D 12 のクラッシュ：フォントのアトラスの転送を 1 フレームに 1 回にまとめ、Console の訳もまた表示されるように。
- `GameOptions.AddSlider`（[#39](https://github.com/TomXV/dragnwash-modframework/issues/39)）。

## リリース済み：1.2.1（2026-09-19）

- Flags and saves 1.0.1：2026-09-14 のゲームのアップデート以降に作ったセーブを、Saves タブがまた見つけられるように。スロットも番号順に（[#40](https://github.com/TomXV/dragnwash-modframework/issues/40)）。

## リリース済み：1.2.0（2026-09-17）

Drag'n Wash Localization v1.2.0 と一緒にリリースしました。

- 中核 1.2.0：開発者ツールのスイッチ、Mods 画面での文字列とキーの設定、`GameEvents`、`SettingMeta`、ゲームを動かしたままの Mod のリロード、外と通信する Mod の申告（[Going online (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Going-online-ja)）。
- Tool window 1.1.0（Console）、Assets 1.1.0（テクスチャの差し替え、プレビュー）、Dialogue 1.1.0（安定した行キー）。
- Inspector 1.0.0。新しいライブラリで、今後も実験的なままです。
- NotaGames さんの新しいロゴと、Mister ERIO さんの手描きの Mods ボタン。

## できているが、まだリリースしていないもの

- **ゲームなしのコードのグラフ**（`codegraph-standalone/`、[設計](CODE_GRAPH_STANDALONE.ja.md)）。同じページを、どの .NET アセンブリにも、ポートも通信もなしで。このリポジトリの作業用の道具で、リリースには入れません。

## 予定していること

- **Mods 画面に、Mod ごとのお知らせ欄。** 今の画面が出せるのは「使えない機能」と「同じコードを書き換えている」の 2 種類だけです。どちらにも当てはまらないこともあります。たとえば、2 つの Mod が同じ行に違う訳を同梱した場合です（[Localization #28](https://github.com/TomXV/dragnwash-localization/issues/28)）。Mod やライブラリが、Mod の下に一言残せる小さな共通の仕組みを用意します。
- **他の Mod が同梱する訳。** Drag'n Wash Localization が単独で `<Mod のフォルダー>/Translations/` を読むところまではできました。β版の実験的機能で、既定はオフです（[設計](https://github.com/TomXV/dragnwash-localization/blob/main/docs/MOD_TRANSLATIONS.ja.md)）。2 つ目の翻訳 Mod が同じ約束事を使いたくなったら、フォルダーを見つける処理を Text ライブラリに移します。
- **Direct3D 12。** 1.3.0 で、このクラッシュ（Unity UUM-140564）のいちばん多いきっかけだった、フォントのアトラスの転送の集中をなくしました。ゲーム中のテクスチャのリロードは、まだ一度に転送します。同じ対策が要るかは、クラッシュレポートの GPU 転送のトレースで確かめます。
- **あらゆる種類の書き出しと取り込み。** テクスチャ、メッシュ、マテリアル、音、ゲームのデータ（ScriptableObject）をファイルに書き出し、手を加え、Mod から取り込めるように（[設計](https://github.com/TomXV/dragnwash-modframework/wiki/Assets-ja)）。
- **Steam Deck：** F1 の窓で、画面キーボードで文字を入力できるかの確認。

## 必要が出てきたら

- テクスチャだけでなく、メッシュとシェーダーの差し替え（Assets）。
- Mod が必要とするなら、`GameEvents` にゲームの出来事を追加。ただし、毎フレームの出来事は今後も用意しません。
- Mod がほかの方法で通信するようになったら、接続の見張りの対象を追加。

## このままのもの

- **Inspector** は、リリース後も実験的なままです。Mod を作る人のための道具で、Mod のリリースからは外してかまいません。
- **接続の見張りは、見張るだけです。** 接続を止めることはせず、安全を保証する仕組みでもありません。Mod が何をしているかを見えるようにするためのものです。

## 予定していないこと

- 自由なスクリプト言語（任意のメソッド、リフレクション）。代わりに、登録された操作だけを呼ぶノードグラフを予定しています（[計画](API_PLAN.ja.md)）。
- 機械翻訳。
- ゲームのファイルを、手を加えずにそのまま配布すること（[CONTENT_POLICY.ja.md](CONTENT_POLICY.ja.md)）。
- 2 つ目の Mod ローダーを作ること、BepInEx の役割を肩代わりすること。

## ほかの動きを待っているもの

- **macOS：** 今の BepInEx のリリースに入っている Doorstop は、Unity 6.3 にフックできません（[UnityDoorstop#108](https://github.com/NeighTools/UnityDoorstop/issues/108)）。修正は本家に入りましたがまだリリースされていないので、今のところ macOS では、Drag'n Wash Localization の試験版のインストールスクリプトで、そのビルドを使って Rosetta で動かすだけです。待っているのは 2 つのリリースです。UnityDoorstop 4.6.0 の安定版と、ネイティブの arm64 の修正（[BepInEx#1402](https://github.com/BepInEx/BepInEx/pull/1402)）が入った BepInEx 5 で、こちらが出れば Rosetta なしで動かせます。進み具合：[#85](https://github.com/TomXV/dragnwash-modframework/issues/85)
