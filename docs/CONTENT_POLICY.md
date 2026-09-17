# Material from the game

[日本語](CONTENT_POLICY.ja.md)

How this project, and the mods that follow its GUIDE, handle material that comes from Drag'n Wash: its art, models, sounds, text and code. Adopted 2026-09-18. It applies to every mod that uses Drag'n Wash ModFramework, so Drag'n Wash Localization follows it too.

The project follows the idea of **fair use**: material from the game may be used when the use is fair.

## Not allowed: the game's own data, unchanged

- Assets, script text and DLLs taken from the game as they are, without changes.
- They go into neither a repository nor a release. CI refuses the game's files (`ci/game-fingerprints.json`), and it will refuse untouched exports from the asset tool too ([ASSET_TOOL.md](ASSET_TOOL.md#export-and-import)).

## Allowed: made by hand, or changed into something new

- **Your own work.** Art you drew, text you wrote or translated, models you built.
- **Material from the game, changed into a new expression.** A sign painted over in another language, a redrawn texture, a modified model, a logo drawn after the game's own (the framework's logo is one: [#15](https://github.com/TomXV/dragnwash-modframework/issues/15)).

## How to judge a use

The four points of fair use:

1. **Purpose.** For a mod, not for profit. It adds a new expression or feature instead of standing in for the original.
2. **The original.** Respect the game as a work, and do not use it in a way that harms it.
3. **Amount.** Use only the parts you need. Never bundle the whole game or a large part of it.
4. **Effect on the game's market.** It must not replace buying the game. A mod is for people who own the game.

## When the developers ask

If Gator Dragon Games asks for something to be removed, it is removed at once.

## Note

This is the project's policy, not a legal guarantee. Whether a use is fair is decided case by case in the end. Whoever contributes material is responsible for it.
