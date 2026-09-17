# ロードマップ

[English](ROADMAP.md)

Drag'n Wash ModFramework のこれからの予定です。予定は変わることがあり、日付は近いものだけ書いています。更新日：2026-09-17。

判断の基準は変わりません。すべての Mod が一緒に安全に動くこと。そして、ほかの人が引き継げるように、それぞれの部分を小さく保つことです。

## リリース済み：1.2.0（2026-09-17）

Drag'n Wash Localization v1.2.0 と一緒にリリースしました。

- 中核 1.2.0：開発者ツールのスイッチ、Mods 画面での文字列とキーの設定、`GameEvents`、`SettingMeta`、ゲームを動かしたままの Mod のリロード、外と通信する Mod の申告（[NETWORK.ja.md](NETWORK.ja.md)）。
- Tool window 1.1.0（Console）、Assets 1.1.0（テクスチャの差し替え、プレビュー）、Dialogue 1.1.0（安定した行キー）。
- Inspector 1.0.0。新しいライブラリで、今後も実験的なままです。
- NotaGames さんの新しいロゴと、Mister ERIO さんの手描きの Mods ボタン。

## 予定していること

- **Mods 画面に、Mod ごとのお知らせ欄。** 今の画面が出せるのは「使えない機能」と「同じコードを書き換えている」の 2 種類だけです。どちらにも当てはまらないこともあります。たとえば、2 つの Mod が同じ行に違う訳を同梱した場合です（[Localization #28](https://github.com/TomXV/dragnwash-localization/issues/28)）。Mod やライブラリが、Mod の下に一言残せる小さな共通の仕組みを用意します。
- **他の Mod が同梱する訳。** まず Drag'n Wash Localization が単独で `<Mod のフォルダー>/Translations/` を読むようにします。β版の実験的機能で、既定はオフです（[設計](https://github.com/TomXV/dragnwash-localization/blob/main/docs/MOD_TRANSLATIONS.ja.md)）。2 つ目の翻訳 Mod が同じ約束事を使いたくなったら、フォルダーを見つける処理を Text ライブラリに移します。
- **Direct3D 12。** F1 の窓を開いたときやテクスチャのリロード中に、まれにゲームが落ちることがあります（Unity UUM-140564）。きっかけを突き止めるか、アップロードをすべて起動時に済ませる形にします。
- **Inspector のオブジェクトエクスプローラー。** 読み込まれているものすべてを種類別に（テクスチャ、マテリアル、メッシュ、シェーダー、音、アニメーション、フォント、ScriptableObject に入ったゲームのデータ）。Unity の Project ウィンドウのように並べ、**Used by** でどこで使われているかも調べられます。Inspector と同じく実験的です（[設計](OBJECT_EXPLORER.ja.md)）。
- **Steam Deck：** F1 の窓で、画面キーボードで文字を入力できるかの確認。

## 必要が出てきたら

- テクスチャだけでなく、メッシュとシェーダーの差し替え（Assets）。
- Mod が必要とするなら、`GameEvents` にゲームの出来事を追加。ただし、毎フレームの出来事は今後も用意しません。
- Mod がほかの方法で通信するようになったら、接続の見張りの対象を追加。

## このままのもの

- **Inspector** は、リリース後も実験的なままです。Mod を作る人のための道具で、Mod のリリースからは外してかまいません。
- **接続の見張りは、見張るだけです。** 接続を止めることはせず、安全を保証する仕組みでもありません。Mod が何をしているかを見えるようにするためのものです。

## 予定していないこと

- Console のスクリプト言語。
- 機械翻訳。
- ゲームのファイルや、描き直したゲームの絵の配布。
- 2 つ目の Mod ローダーを作ること、BepInEx の役割を肩代わりすること。

## ほかの動きを待っているもの

- **macOS：** BepInEx の Doorstop が、まだ Unity 6.3 にフックできません（[UnityDoorstop#108](https://github.com/NeighTools/UnityDoorstop/issues/108)）。
