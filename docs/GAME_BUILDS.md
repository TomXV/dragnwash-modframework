# Game builds checked

Drag'n Wash builds the framework was run on, and what was checked. The build id is shown in the corner of the game's title screen; the Windows and Linux versions of the game carry different ids. / フレームワークを動かして確認したゲームのビルドです。ビルド番号はタイトル画面の隅に出ています。Windows 版と Linux 版で番号が違います。

| Game build | Unity | Checked on | Framework | What was checked |
|---|---|---|---|---|
| `9/12/2026_a93aa21a` | 6000.3.14f1 | Windows 11, Direct3D 12 (2026-09-15) | 1.0.0 (core and all libraries) | All patch targets found (no unavailable features). Mods screen at 16:9, 4:3, ultrawide and when switching to full screen, with mouse and gamepad; settings page; Options row with preview, Back and Save; pending Options changes kept across the Mods screen; tool window tabs from two mods, a failing tab isolated; fonts for 13 languages prepared at startup; save history moved and read; text and dialogue libraries through Drag'n Wash Localization's checklists; installers, fresh and over an earlier version; version line above the build id on the title screen. Core 1.1.0: update notices (tag, details, release page button, title screen line, switching off, cached results after a restart, no errors offline). |
| `9/13/2026_2a0da92f` (Linux) | 6000.3.14f1 | Steam Deck, Vulkan (2026-09-15) | 1.0.0 (core and all libraries) | Same game code as the Windows build above. Startup, Mods screen and settings with the controller and trackpad, selection frame, tool window, Japanese text; `install-steamdeck.sh`, fresh and over an earlier version. Core 1.1.0: update notices; the release page opens in Steam's browser in Game Mode. |

After a game update, add the new build's files to `ci/game-fingerprints.json` with `tools/game-fingerprints.py` (on Windows and on the Steam Deck), so CI keeps refusing the game's files. / ゲームがアップデートされたら、`tools/game-fingerprints.py` で新しいビルドのファイルを `ci/game-fingerprints.json` に追加してください（Windows と Steam Deck の両方）。CI がゲームのファイルを拒否し続けられます。
