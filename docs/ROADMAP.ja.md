# ロードマップ

[English](ROADMAP.md)

Drag'n Wash ModFramework のこれからの予定です。予定は変わることがあり、日付は近いものだけ書いています。更新日：2026-09-20。

判断の基準は変わりません。すべての Mod が一緒に安全に動くこと。そして、ほかの人が引き継げるように、それぞれの部分を小さく保つことです。

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

## できていて、次のリリースに入るもの

ここにあるものはすべて `main` にあり、実験的な扱いです。全部の一覧は [CHANGELOG.md](../CHANGELOG.md) にあります。

- **Overrides 0.1.0：コードのない Mod。** `mod.json` と `overrides/*.json` だけのフォルダーが、コンポーネントのフィールドやマテリアルのプロパティを書き換えます。ほかの Mod と同じく Mods 画面に出てオフにでき、きれいに元に戻せます。2 つの Mod が同じところを書き換えたときは、ログに両方の名前を出します。Inspector の History から、そこでの編集をこの形の Mod として書き出せます（[Overrides（wiki）](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides-ja)）。
- **操作の登録簿と、そこから出てきたもの**（[計画](API_PLAN.ja.md)の第 1〜3 段階）。各ライブラリが、できることを名前つきの操作（`library.noun.verb`）として登録し、引数は検査して素の値で返します。その 1 つの登録簿から、Console の `op`、**Bridge 0.1.0**（AI クライアント向けの読み取り操作を MCP で。この PC の中だけ、既定はオフ、トークンと、Web ページを断る入口つき）、**コードのグラフ**（ゲームのコードをブロックと分岐で。ページと、ゲームなしで動く Windows のアプリ）ができました。第 4 段階の「ノードグラフで組み立てる」はこれからです。
- **Inspector のオブジェクトエクスプローラー。** 読み込まれているものすべてを種類別に（テクスチャ、マテリアル、メッシュ、シェーダー、音、アニメーション、フォント、ScriptableObject に入ったゲームのデータ）Unity の Project ウィンドウのように並べ、**Used by** でどこで使われているかを調べられます。一覧は矢印キーで、Steam Deck やゲームパッドでは十字キーでたどれます（[設計](OBJECT_EXPLORER.ja.md)）。
- **言語ごとのテクスチャの差し替え**（Assets 1.2.0）。特定の言語を使っている間だけ効き、`fallback.txt` に書いた言語へたどり、元に戻せて、Direct3D 12 では再起動を待ちます。これを土台に Drag'n Wash Localization の絵の翻訳ができています（[設計](https://github.com/TomXV/dragnwash-localization/blob/main/docs/TRANSLATED_TEXTURES.ja.md)）。
- Inspector の Animator と Rigidbody、シーンとレベル、クラッシュレポートの後始末、Tool window の Steam Deck 対応（トラックパッドの押下が本物のマウスボタンに、十字キーが本物の矢印キーに）。

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

- **macOS：** BepInEx の Doorstop が、まだ Unity 6.3 にフックできません（[UnityDoorstop#108](https://github.com/NeighTools/UnityDoorstop/issues/108)）。
