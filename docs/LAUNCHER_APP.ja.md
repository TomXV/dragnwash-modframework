# ランチャー：ゲームの前に動くプログラム

[English](LAUNCHER_APP.md)

> **作りました**：`Launcher.exe` 本体と、その画面です。インストーラーがゲームのフォルダーに置くことと起動オプションを入れること、ゲームの中の「更新」ボタンから使うことは、まだです（このあと作ります）。ゲームがランチャーのために書くファイル（`updates.json`）は `docs/LAUNCHER.ja.md` にあります。

前回遊んだときに、ゲームの更新チェックが Mod の新しいバージョンを見つけていたら、ランチャーはゲームの前にそれを見せます。何が変わったかと、「更新して起動」のボタンです。GitHub を開いて zip を落とし、Install.exe をもう一度動かす必要はもうありません。この時点ではまだゲームが動いていないので、使用中のファイルもありません。

## 置き場所と起動のしかた

`<ゲーム>\BepInEx\DragNWash.Installer\Launcher.exe` と、その横に WebView2 のファイル 3 つ（`Microsoft.Web.WebView2.Core.dll`、`Microsoft.Web.WebView2.WinForms.dll`、`WebView2Loader.dll`）です。フレームワークの zip の中でも、同じ場所に入っています。

Steam の起動オプション：

```
"<ゲーム>\BepInEx\DragNWash.Installer\Launcher.exe" %command%
```

Steam が `%command%` をゲームの exe と引数に置き換えます。ランチャーはそれをそのまま起動し、ゲームが終わるまで親として待ちます（なので Steam のプレイ時間も途切れません）。ゲームには `DNW_LAUNCHER=1` を渡すので、ゲームはランチャーが待っていることがわかります。

もう 1 つの入り方は、ランチャーを通さずに起動したゲームのためのものです：

```
Launcher.exe --update-after-exit --wait-pid <pid> [--mods <guid>,<guid>]
```

そのプロセスが終わるのを待ち、更新を入れ、Steam にゲームをもう一度起動してもらいます（`steam://rungameid/4739660`）。`--mods` がなければ、`update-request.json` に書かれた Mod を入れます。

## 何が起きるか

**ゲームの前**

1. `BepInEx/cache/DragNWash.ModFramework/updates.json` を読みます。ファイルを読むだけで、更新を探しにネットにはつなぎません。探すのはゲームで、1 日 1 回です。
2. 見せるものがないとき（新しい版がない、ファイルがまだない、ゲームの更新チェックを切っている、見つかった更新が全部「このバージョンは知らせない」にしたもの）は、すぐゲームを起動し、読み込みのあいだロゴを流します。ゲームの窓が出たら閉じます。
3. あるときは、ロゴが上に動いて更新の一覧が出ます。Mod ごとに今の版と新しい版、大きさ、変更点です。入れたいものにチェックを入れて、「更新して起動」で入れてから起動、「このまま起動」で何もせずに起動します。「このバージョンは知らせない」にすると、その Mod のその版では、もうこの画面を出しません。次の版が出たら、また知らせます。
4. チェックボックスが付くのは、インストーラーで入れた Mod（フォルダーに `mod-install.json` があるもの）だけです。ほかの Mod には「リリースページを開く」が付きます。

ゲームが調べたあとに、もう入れてあった版（その Mod の `mod-install.json` がそう言っている）は出しません。

**ゲームの中から頼まれた更新のあと**

ゲームは `BepInEx/cache/DragNWash.ModFramework/update-request.json` を書いて終了します。ゲームが終わると、ランチャーがそのファイルを見つけ（今回の起動のあとに書かれたものだけを使い、読んだら消します）、更新の画面を出して入れ、3 秒数えてから同じコマンドでゲームをもう一度起動します。「今は起動しない」を押すと、起動せずに窓を閉じます。

**WebView2 がないとき**

窓は出しません。ゲームはいつも通り起動し（`--update-after-exit` のときは、更新せずに Steam に起動してもらい）、理由をログに書きます。Mods 画面からは、今まで通りリリースページを開けます。

**何か失敗したら**

それでもゲームは起動します。更新の途中で失敗したときは、何が起きたかと、ゲームのフォルダーがどうなったかを出します。ゲームの前なら、もう一度試すか、更新せずに起動するかを選べます。ゲームの中から頼まれた更新なら、カウントダウンのあと、元のままのゲームを起動し直します。

## 入れ方

ランチャーがネットにつなぐのは、「更新して起動」（またはゲームの中の「更新」）を押したあとだけで、つなぐ先は github.com と release-assets.githubusercontent.com だけです。

- **どのファイルか。** リリースの zip です。`.zip` が 1 つだけならそれ、いくつかあるときは名前にバージョンが入ったもの（`DragNWash.ModFramework-1.5.1.zip` のような）です。どちらでもないリリースは、リリースページだけを出します。
- **どのアドレスか。** `updates.json` のリポジトリとタグから組み立てます：`https://github.com/<owner>/<repo>/releases/download/<tag>/<ファイル>`。ファイルに書かれたアドレスがこれとぴったり同じでなければ、その zip は使いません。「リリースページを開く」で開くページも、github.com の同じリポジトリの下にあるものだけです。
- **確かめること。** ダウンロードしたファイルが、GitHub の言う大きさと SHA-256 に合うこと。GitHub が SHA-256 を出す前に上げられたファイルは大きさだけで確かめ、ログにそう書きます。それから zip の中に、`BepInEx` フォルダーと並んで `mod-install.json` があり、それが `plugins` の下のその Mod 自身のフォルダーのものであること。どれかに合わない zip は使わず、ゲームのフォルダーは何も変わっていません。
- **入れる。** Install.exe と同じコードで（`installer/` のファイルを両方でビルドしています）、`Install.exe --install` と同じように入れます。Mod の選択肢は設定ファイルの値を残し、決まった版の ModFramework が要ればそれも取り、フレームワークの部品を古いもので上書きはしません。上書きするファイルは先に `BepInEx\DragNWash.Installer\backup\<日時>` に控え、コピーの途中で失敗したら全部元に戻します。
- **いくつかの Mod** は、先に全部ダウンロードして確かめ、それから 1 つずつ入れます。1 つずつがそれだけで完結しているので、2 つ目で失敗しても 1 つ目は更新されたままです（失敗の画面にそう出ます）。Install.exe と同じく、控えは最後に入れたぶんだけが残ります。
- **キャンセル**は、確認が終わるまで押せます。それまではゲームのフォルダーに何も書きません。

## 設定

ランチャーは `BepInEx\config\com.tomxv.dragnwash.modframework.cfg` の `[Launcher]` を読みます（ゲームが登録するので、Mods 画面の設定にも出ます）。なければ標準の値です。

| 設定 | 値 |
|---|---|
| `Logo lettering` | `Handwriting`（標準）、`Typewriter`：ロゴの MOD FRAMEWORK の出し方 |
| `Progress bar` | `Bottom edge`（標準）、`Under text`：ロゴの画面のプログレスバーの位置 |

Drag'n Wash Localization が日本語になっているとき（その設定の `TargetLocale`）は日本語、それ以外は英語で出します。Localization の設定がなければ Windows の言語に合わせます。リリースノートは、日本語なら `## 日本語` の節を、英語ならそれ以外を出します。

## ファイル

| ファイル | |
|---|---|
| `BepInEx/cache/DragNWash.ModFramework/updates.json` | 読むだけ。書くのはゲーム。 |
| `BepInEx/cache/DragNWash.ModFramework/update-request.json` | 読んで消す。書くのはゲーム。 |
| `BepInEx/cache/DragNWash.ModFramework/launcher-state.json` | ランチャー自身の記録：`{ "schema": 1, "skipped": { "<guid>": "<tag>" } }` |
| `BepInEx/DragNWash.Installer/launcher.log` | 今回のログ（英語）。前回のぶんは `launcher.prev.log`。 |
| `%LOCALAPPDATA%\DragNWash ModFramework\Launcher\WebView2` | 窓の WebView2 が使うデータ。 |

## 窓

枠なしの 720 × 440 の窓で、WebView2（Windows 10 と 11 に入っています）を使います。ページ（`launcher/web/`）は exe の中にあり、exe から `https://launcher.invalid/` として出します。ページはほかのものを読み込めず、ネットにもつなげず、ほかのページも開けません。「リリースページを開く」と「ログのフォルダーを開く」はランチャーを通り、ランチャーがアドレスを確かめてからブラウザーで開きます。

ページとランチャーは小さな JSON でやりとりします。ページは `chrome.webview.postMessage` で `{cmd, ...}` を送り、ランチャーは `window.dnw(event)` を呼びます。

- **ページからランチャーへ**：`ready`、`introDone`、`update`（チェックした Mod）、`skip`、`open`（Mod のリリースページ）、`proceed`、`cancel`、`retry`、`play`、`close`、`minimize`、`drag`、`openLog`、`copy`
- **ランチャーからページへ**：`init`（どの画面か、言語、設定、Mod）、`step`（`wait`、`dl`、`chk`、`bak`、`ins`）、`download`（バイト数）、`verified`（zip 1 つが確認を通った）、`checked`（全部通った。ページが確認の絵を見せ終えて `proceed` を返すまで、ランチャーは何も書きません）、`log`、`done`、`failed`、`cancelled`

ランチャーの作業は絵よりずっと先に進むことがあるので、ページは知らせを順に並べ、手順ごとにデザインで決めた時間は必ず見せます。

## ビルドと試し方

`tools/pack.ps1` がフレームワークの zip に入れます。手で作るとき：

```
dotnet build launcher/DragNWash.Launcher.csproj -c Release -warnaserror
```

Debug ビルドは、GitHub や Steam やゲームなしで試せるように、次の環境変数も読みます。Release ビルドは読みません。

| 変数 | |
|---|---|
| `DNW_LAUNCHER_LOCAL_RELEASES=<フォルダー>` | ダウンロードの代わりに、このフォルダーの zip をコピーする。 |
| `DNW_LAUNCHER_FAIL=offline\|busy\|limited\|notfound` | 上と合わせて、ダウンロードをその形で失敗させる。 |
| `DNW_LAUNCHER_NO_WEBVIEW2=1` | WebView2 がないものとして動く。 |
| `DNW_LAUNCHER_NO_STEAM=1` | Steam にゲームの起動を頼まない。 |
| `DNW_LAUNCHER_WEB=<launcher/web フォルダー>` | ページをディスクから出す。ビルドし直さずにページを直せる。 |

ゲームの代わりには、`BepInEx\core\BepInEx.dll` があるフォルダーに置いた `DragNWash.exe` という名前の exe なら何でも使えます。
