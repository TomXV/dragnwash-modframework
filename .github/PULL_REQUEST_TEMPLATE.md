## What / 内容

<!-- What this changes, and why. If it fixes an issue, write "Fixes #123".
     何を変えるのか、なぜなのか。Issue を直すなら "Fixes #123" と書いてください。 -->

## How it was checked / 確認したこと

<!-- The framework compiles against the game's own assemblies, so CI cannot build
     the core or the libraries. Say what you ran, and where: the game build, the
     platform (Windows / Steam Deck / Linux), and what you looked at.
     フレームワークはゲームのアセンブリを参照するため、中核とライブラリは CI では
     ビルドできません。どのゲームのビルドの、どの環境（Windows / Steam Deck /
     Linux）で、何を見て確かめたかを書いてください。 -->

## Checklist / チェックリスト

- [ ] `python tools/check-repo.py` and `python tools/linekeys.py --check` pass / 通る
- [ ] No game files, BepInEx binaries or anything from `libs/` is committed / ゲームのファイル・BepInEx のバイナリ・`libs/` の中身を含めていない
- [ ] Material from the game follows [docs/CONTENT_POLICY.md](https://github.com/TomXV/dragnwash-modframework/blob/main/docs/CONTENT_POLICY.md) / ゲーム由来の素材が方針に沿っている
- [ ] Code that touches game classes stays `internal`; only the framework's own types are `public` / ゲームのクラスに触れるコードは `internal` のまま
- [ ] A changed `<Version>` matches the `Version` constant in the code and has an entry in [CHANGELOG.md](https://github.com/TomXV/dragnwash-modframework/blob/main/CHANGELOG.md) / バージョンを変えたなら、コード中の定数と CHANGELOG も合わせた
- [ ] A public API change is documented, and a breaking one is called out here / 公開 API の変更は文書化した。互換性を壊すならここに明記した
- [ ] Documentation changed in both languages, or the missing side is noted here / ドキュメントは英語と日本語の両方を直した（片方だけならここに書いた）

<!-- The wiki is a separate repository: documentation for players and for mod
     authors lives there, not in docs/. Say here what should change on it.
     Wiki は別リポジトリです。プレイヤー向け・Mod 作者向けの説明はそちらにあります。
     直すべきページがあればここに書いてください。 -->

## Notes for the reviewer / レビューへの補足

<!-- Anything you are unsure about, or decided on purpose. 迷ったところ、あえてそうしたところ。 -->
