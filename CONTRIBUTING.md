# Contributing

[日本語](CONTRIBUTING.ja.md)

Thank you for looking. This is a prerequisite mod: other people's mods stand on
it, so the bar for a change is "does this still hold up when five mods use it at
once", and a small, boring pull request is usually the best kind.

Everyone taking part is expected to follow the [code of conduct](CODE_OF_CONDUCT.md).
Security problems do not go in a pull request or a public issue — see
[SECURITY.md](SECURITY.md).

## Ways to help that are not code

- **Report what broke.** A bug report with `BepInEx/LogOutput.log`, the game
  build and the platform is worth a lot, especially after a game update.
- **Say what your mod cannot do.** The framework exists because mods kept
  rebuilding the same machinery. If you are patching the game directly because
  the framework gives you no way to do something, that is the most useful issue
  you can open.
- **Fix the documentation.** The [wiki](https://github.com/TomXV/dragnwash-modframework/wiki)
  is the documentation for players and mod authors. It is a repository of its
  own and GitHub wikis take no pull requests, so open an issue here saying
  which page is wrong and what it should say. The design and research records
  stay in [`docs/`](docs/) here.
- **Try it somewhere unusual.** Steam Deck, a Linux distribution that is not
  SteamOS, Windows on ARM. Results either way are useful; add them to
  [`docs/GAME_BUILDS.md`](docs/GAME_BUILDS.md) if you like.
- **Sponsor it**, if you want to and can. It is never required, and nothing is
  kept behind it: https://github.com/sponsors/TomXV

## Before you start on something big

Open an issue first. A new library, a change to a public API, or anything that
changes how mods are loaded is worth agreeing on before you write it — partly
so the work is not wasted, partly because [`docs/DESIGN.md`](docs/DESIGN.md) and
[`docs/ROADMAP.md`](docs/ROADMAP.md) may already have a plan for it that reads
differently from yours.

Small fixes need none of that. Send them.

## Setting up

1. Install the .NET SDK, and BepInEx 5.4.23.5 in the game.
2. Copy the game's reference assemblies out of your own install:

   ```bash
   pwsh tools/copy-libs.ps1
   ```

   Pass `-GamePath` if the game is not in the default Steam library. They land
   in `libs/` folders, which are ignored and **must never be committed**.
3. Build what you are working on:

   ```bash
   dotnet build src/DragNWash.ModFramework/DragNWash.ModFramework.csproj -c Release
   ```

4. Copy each plugin DLL to its own folder under `<Game>/BepInEx/plugins/<assembly name>/`,
   and `DragNWash.ModFramework.Preloader.dll` to `<Game>/BepInEx/patchers/`.

[README.md](README.md) has the same steps in more detail, and
[Playing well with others (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Playing-well-with-others)
has what each library is for.

## The rules

These are not style preferences; a pull request that breaks one cannot be
merged.

- **Never commit the game's files, BepInEx binaries or anything from `libs/`.**
  A check runs on every push and pull request and will fail the build. This is
  what keeps the repository legal to publish.
- **Material from the game follows [`docs/CONTENT_POLICY.md`](docs/CONTENT_POLICY.md):**
  made by hand or turned into something new is fine; the game's data as it is,
  is not.
- **Code that touches game classes stays `internal`.** Mods see the framework's
  own types and nothing else. That boundary is the whole point: when the game
  updates, only the framework has to follow.
- **A change that breaks the public API needs a new major version**, and a good
  reason, since every mod built on it will break too.

## Checks

CI runs what needs no game files, and you can run all of it yourself:

```bash
python tools/check-repo.py        # versions, GUIDs, changelog, documentation links
python tools/linekeys.py --check  # line keys still match the vectors
python tools/check-commits.py     # no tool's attribution in the commit messages
```

The last one is the **Commit checker**. This history names the people who
decided what a commit should say, not the editor, the assistant or the IDE that
typed it. It fails the build on two things:

- **The message** — a `Co-authored-by` line naming a tool, a "Generated with"
  footer, or a link to an assistant's session. A human co-author is welcome.
- **The author or the committer** — a commit signed by a tool's account, even
  when its message reads perfectly well. `dependabot[bot]`, `github-actions[bot]`
  and the `GitHub <noreply@github.com>` committer of a web merge all pass, and so
  does a person whatever they are called: Claude is somebody's name, so the rule
  asks for a model or a bot suffix after it before refusing anything.

If it catches you, fix the commit (`git commit --amend`, adding
`--reset-author` when the author is wrong, or `git rebase -i` for an older one)
and push again.

The core and the libraries cannot be built on a runner — they need the game's
assemblies — so **you** are the one who checked them. Say in the pull request
what you ran the change against: the game build, the platform, and what you
looked at.

## Versions and the changelog

Each project has its own version. If you change one:

- `<Version>` in the `.csproj`, the `Version` constant in the code, and the
  `### <Name> <version>` heading in [CHANGELOG.md](CHANGELOG.md) all have to
  agree. `tools/check-repo.py` checks exactly this.
- The preloader patcher follows the core's version.

Most pull requests should not bump a version at all — that usually happens when
a release is put together.

## Documentation

Everything in `docs/` comes in two languages: `NAME.md` and `NAME.ja.md`, each
linking to the other. Change both if you can. If you can only do one, say so in
the pull request and it will be picked up — a missing translation is not a
reason to hold a fix back.

The wiki is a separate repository and takes no pull requests. If your change
means a wiki page is now wrong, say which one in the pull request.

## The pull request itself

- Branch off `main`, one topic per pull request.
- Write the title and body so someone reading the history in a year knows what
  changed and why. The template asks the questions.
- Both languages in the body are welcome but not expected; either is fine.
- Draft pull requests are welcome, including for a design you want to argue
  about before finishing it.

By sending a pull request you agree that your contribution is licensed under
the [MIT license](LICENSE), like the rest of the code.

## One more thing

This is an unofficial fan project, not affiliated with Gator Dragon Games.
Please do not send the game's developers bugs that only happen with mods
installed, and please do not ask them to support anything here.
