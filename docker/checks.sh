#!/usr/bin/env bash
# Every check CI runs, in one go, in the container image (docker/Dockerfile).
#
#   docker compose run --rm checks
#
# Each check is the command the workflow runs, so a pass here is a pass there.
# The script stops at the first failure; --keep-going runs the rest anyway and
# reports every reason at once.
set -u

KEEP_GOING=0
[ "${1:-}" = "--keep-going" ] && KEEP_GOING=1

. "$(dirname "$0")/tree.sh"

failed=0

run() {
    title=$1
    shift
    printf '\n\033[1m== %s ==\033[0m\n' "$title"
    if "$@"; then
        return 0
    fi
    printf '\033[31mFAILED: %s\033[0m\n' "$title"
    failed=$((failed + 1))
    [ "$KEEP_GOING" = "1" ] || exit 1
}

# ---- the checks that need nothing but Python --------------------------------

run "Versions, GUIDs, changelog and documentation links" \
    python tools/check-repo.py

run "Line keys match the vectors" \
    python tools/linekeys.py --check

run "Graphs: the operations, the example graph and the broken ones" \
    python tools/graphs.py --test

# The range the commit checker walks: what a push or a pull request brings.
# The script's own default (what is not yet on origin/main) is the right one
# for a working tree; BASE and HEAD override it the way the workflow does.
if [ -n "${BASE:-}" ] && [ -n "${HEAD:-}" ]; then
    run "No tool's attribution in the commit messages" \
        python tools/check-commits.py "$BASE" "$HEAD"
else
    run "No tool's attribution in the commit messages" \
        python tools/check-commits.py
fi

# ---- no game files ----------------------------------------------------------

no_game_files() {
    found=$(git ls-files | grep -Ei '(^|/)libs/|\.(dll|pdb|exe|so|dylib|assets|ress|resource|bundle|unity3d)$' || true)
    if [ -n "$found" ]; then
        echo "These files must not be committed:"
        echo "$found"
        return 1
    fi
    python3 tools/check-game-files.py
}

run "No game files or binaries in the repository" no_game_files

# ---- the two projects that build without the game ---------------------------

TREE=$(dnw_build_tree) || exit 1
[ "$TREE" = "/work" ] || echo "
The working tree has been built on the host, so the build below runs on a copy
at $TREE and leaves bin/ and obj/ here as they are."

# BepInEx, pinned by its SHA-256 exactly as the workflow pins it. Skipped when
# the folder already has it, which is the case on a machine that has run
# tools/copy-libs.ps1.
BEPINEX_URL=https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip
BEPINEX_SHA256=82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4

get_bepinex() {
    libs="$TREE/src/DragNWash.ModFramework/libs"
    if [ -f "$libs/BepInEx.dll" ] && [ -f "$libs/Mono.Cecil.dll" ]; then
        echo "BepInEx is already there; leaving it alone."
        return 0
    fi
    zip=$(mktemp -d)/bepinex.zip
    curl -fsSL -o "$zip" "$BEPINEX_URL" || return 1
    echo "$BEPINEX_SHA256  $zip" | sha256sum -c - || return 1
    mkdir -p "$libs"
    unzip -j -o "$zip" BepInEx/core/BepInEx.dll BepInEx/core/Mono.Cecil.dll -d "$libs"
}

run "BepInEx 5.4.23.5 (checked against a pinned SHA-256)" get_bepinex

build() {
    (cd "$TREE" && dotnet build "$1" -c Release -warnaserror)
}

run "Build the preloader patcher" \
    build src/DragNWash.ModFramework.Preloader/DragNWash.ModFramework.Preloader.csproj

run "Build Install.exe" \
    build installer/DragNWash.Installer.csproj

installer_tests() {
    (cd "$TREE" && dotnet run --project installer/tests -c Release)
}

run "Installer checks: the Steam launch option and localconfig.vdf" installer_tests

installer_extras() {
    bash -n installer/install-steamdeck.sh \
        && python3 -c "import json; json.load(open('installer/mod-install.example.json', encoding='utf-8'))"
}

run "The Steam Deck installer script and the example manifest" installer_extras

# ---- the result -------------------------------------------------------------

printf '\n'
if [ "$failed" -gt 0 ]; then
    printf '\033[31m%d check(s) failed.\033[0m\n' "$failed"
    exit 1
fi
printf '\033[32mEvery check passed.\033[0m\n'
