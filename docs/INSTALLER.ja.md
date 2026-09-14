# 共通インストーラーで Mod を配布する

[English](INSTALLER.md)

Drag'n Wash のどの Mod も、同じインストーラーを同梱できます。Windows 用の `Install.exe` と、Steam Deck / Linux 用の `install-steamdeck.sh` です。BepInEx、Drag'n Wash ModFramework、Mod を入れ、また取り除きます。どの Mod でも同じ決まりで動くので、Mod 同士がお互いの導入を壊しません。Mod ごとに違うことは、`mod-install.json` という 1 つのファイルに書きます。

プレイヤーはゲームの中の **Options → Mods** からもアンインストールできます。[ゲーム内でのアンインストール](#ゲーム内でのアンインストール) を参照してください。

## リリースの zip

```
Install.exe                  フレームワークのリリース zip の installer/ から
install-steamdeck.sh         フレームワークのリリース zip の installer/ から
mod-install.json             自分で書く
BepInEx/plugins/<自分の Mod>/...
BepInEx/plugins/DragNWash.ModFramework*/...        フレームワークと使うライブラリ、
BepInEx/patchers/DragNWash.ModFramework.Preloader.dll   フレームワークのリリースのまま
README.md
```

プレイヤーは zip をどこかに展開して `Install.exe` をダブルクリックするか、デスクトップモードで `bash install-steamdeck.sh` を実行します。インストーラーのファイルは変更せずに同梱してください。`Install.exe` は決定的にビルドしているので、どの Mod に入っていてもハッシュが同じで、ウイルス対策ソフトからの評価も共有されます。

## mod-install.json

```json
{
  "schema": 1,
  "name": "My Mod",
  "version": "1.2.0",
  "website": "https://github.com/me/mymod",
  "plugins": ["MyMod"],
  "keep": ["MyMod/UserData"],
  "configFiles": ["com.example.mymod.cfg"],
  "choices": [
    {
      "id": "difficulty",
      "label": { "en": "Difficulty", "ja": "難しさ", "zh": "难度" },
      "config": { "file": "com.example.mymod.cfg", "section": "General", "key": "Difficulty" },
      "options": [
        { "value": "Normal", "name": "Normal" },
        { "value": "Hard", "name": "Hard" }
      ],
      "default": "Normal"
    }
  ]
}
```

| 項目 | |
|---|---|
| `schema` | いつも `1`。知らない schema はインストーラーが拒否します。 |
| `name`、`version`、`website` | インストーラーの画面に出ます。`website` は `https://` にしてください。 |
| `plugins` | `BepInEx/plugins` の下の自分のフォルダー。フォルダー名だけを書き、`DragNWash.ModFramework*` は指定できません。 |
| `keep` | 自分のフォルダーの中で、プレイヤーのデータが入っているパス。プレイヤーがすべて削除を選ばない限り、アンインストールしても残します。 |
| `configFiles` | `BepInEx/config` にある自分のファイル。アンインストールで削除します。 |
| `choices` | 任意。1 つが、インストーラーでの 1 つの質問になり、答えを BepInEx の設定に書き込みます。`default` は選択肢の値か、システムの言語に合う値（`ja`、`zh-Hans`、`pt-BR` など）を選ぶ `"ui-language"`。設定ファイルにすでに値があればそちらが優先され、更新してもプレイヤーの選択は変わりません。 |

パスはすべて確認します。manifest から、自分のフォルダーとファイルの外を指すことはできません。

## インストーラーがすること

**インストールと更新**

1. Steam からゲームを探す（見つからなければプレイヤーがフォルダーを選ぶ）。ゲームが起動中なら止める
2. BepInEx 5.4.23.5 が無ければ入れる。ダウンロードは固定の SHA-256 で確認し、一致しなければ何も入れない
3. フレームワークとライブラリをコピーする。同じか新しいバージョンが入っていればそのまま。ほかの Mod が入れた新しいフレームワークを、古い zip が上書きすることはない
4. 自分のフォルダーを、今あるものに**上書き**でコピーする。更新でファイルを消すことはないので、プレイヤーが足したファイルは残る
5. 選択肢の答えを設定ファイルに書く
6. Mods 画面でオフにされていればオンに戻し、ゲーム内で予約されたアンインストールを取り消す
7. Mods 画面や次のインストーラーのために、`mod-install.json` の写しを最初のプラグインフォルダーに置く
8. Steam Deck / Linux では、`run_bepinex.sh` の `executable_name` と、起動オプション `./run_bepinex.sh %command%` を設定する（確認してから Steam を一度閉じる）

**アンインストール**

1. 自分のフォルダーを `keep` 以外（プレイヤーはこれも削除を選べる）と、`configFiles` を削除する
2. フレームワークはほかの Mod が残っていないときだけ、`BepInEx/SaveHistory` はプレイヤーがデータを残さないときだけ削除する
3. BepInEx は、プレイヤーが選び、ほかの Mod もパッチャーも残っていないときだけ削除する。Steam Deck では、Mod が残っていなければ起動オプションも元に戻す

ログは、Windows では画面に、Linux では `~/.local/state/dragnwash-installer/installer.log` に出ます。

## ゲーム内でのアンインストール

Mods 画面には、フレームワークとそのライブラリ以外のすべての Mod に **Uninstall** ボタンがあります。1 回目に押すと何が起きるかと、この Mod を必要としている Mod を表示し、2 回目で確定します。ゲームの起動中は Mod を取り除けないので、次にゲームを起動したとき、どの Mod も読み込まれる前にフレームワークのプリローダーが削除します。それまでは **Cancel uninstall** で取り消せます。

削除されるもの：`BepInEx/plugins` の下のその Mod のフォルダー（直接置かれていればその DLL）。フォルダーに `mod-install.json` があれば、`keep` は残して `configFiles` を削除し、なければプラグインの GUID の名前の設定ファイルを削除します。BepInEx とフレームワークは残るので、それらはインストーラーで削除してください。

## コマンドライン

テスト用に、どちらも質問なしで動かせます。

```
Install.exe --install [--game-dir <フォルダー>] [--choice <id>=<値>]
Install.exe --uninstall [--game-dir <フォルダー>] [--remove-data] [--remove-bepinex]
bash install-steamdeck.sh --yes --install [--game-dir <フォルダー>] [--choice <id>=<値>] [--no-launch-option]
bash install-steamdeck.sh --yes --uninstall [--remove-data] [--remove-bepinex]
```

`--bepinex-zip <ファイル>` で手元の BepInEx の zip を使えます。その場合も固定の SHA-256 で確認します。
