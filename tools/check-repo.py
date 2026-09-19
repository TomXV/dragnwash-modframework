"""Consistency checks that need no game files, run by CI on every push and pull request.

- Each project's <Version> in its .csproj matches the `public const string Version`
  in its code (the preloader patcher follows the core).
- Plugin GUIDs are unique.
- CHANGELOG.md has an entry for every current version.
- Relative links in the Markdown documentation point at files that exist.

    python tools/check-repo.py
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
CORE = "DragNWash.ModFramework"
PRELOADER = "DragNWash.ModFramework.Preloader"

# Project folder -> name used in CHANGELOG.md headings ("### Text 0.1.1").
CHANGELOG_NAMES = {
    CORE: "Core",
    "DragNWash.ModFramework.Text": "Text",
    "DragNWash.ModFramework.Dialogue": "Dialogue",
    "DragNWash.ModFramework.ToolWindow": "Tool window",
    "DragNWash.ModFramework.Assets": "Assets",
    "DragNWash.ModFramework.Saves": "Flags and saves",
    "DragNWash.ModFramework.Inspector": "Inspector",
    "DragNWash.ModFramework.Overrides": "Overrides",
}

errors = []


def fail(message):
    errors.append(message)


def rel(path):
    return path.relative_to(ROOT).as_posix()


def code_files(project_dir):
    return [p for p in project_dir.rglob("*.cs") if "obj" not in p.parts and "bin" not in p.parts]


# ---- versions ---------------------------------------------------------------
versions = {}
for csproj in sorted(SRC.glob("*/*.csproj")):
    project = csproj.parent.name
    match = re.search(r"<Version>([^<]+)</Version>", csproj.read_text(encoding="utf-8-sig"))
    file_match = re.search(r"<FileVersion>([^<]+)</FileVersion>", csproj.read_text(encoding="utf-8-sig"))
    if not match:
        fail(f"{rel(csproj)}: no <Version>")
        continue
    version = match.group(1).strip()
    if file_match and file_match.group(1).strip() != version:
        fail(f"{rel(csproj)}: <FileVersion> {file_match.group(1).strip()} differs from <Version> {version}")
    versions[project] = version

    constants = []
    for cs in code_files(csproj.parent):
        for m in re.finditer(r'public\s+const\s+string\s+Version\s*=\s*"([^"]+)"', cs.read_text(encoding="utf-8-sig")):
            constants.append((cs, m.group(1)))
    if project == PRELOADER:
        continue
    if len(constants) != 1:
        fail(f"{project}: expected one `public const string Version`, found {len(constants)}")
        continue
    cs, constant = constants[0]
    if constant != version:
        fail(f"{rel(cs)}: Version \"{constant}\" differs from {rel(csproj)} <Version> {version}")

if PRELOADER in versions and CORE in versions and versions[PRELOADER] != versions[CORE]:
    fail(f"{PRELOADER} version {versions[PRELOADER]} differs from the core's {versions[CORE]}; they ship together")

# ---- GUIDs ------------------------------------------------------------------
guids = {}
for cs in sorted(p for d in SRC.iterdir() if d.is_dir() for p in code_files(d)):
    for m in re.finditer(r'public\s+const\s+string\s+Guid\s*=\s*"([^"]+)"', cs.read_text(encoding="utf-8-sig")):
        guid = m.group(1)
        if guid in guids:
            fail(f"GUID {guid} is declared in both {rel(guids[guid])} and {rel(cs)}")
        guids[guid] = cs
        if not re.fullmatch(r"com\.tomxv\.dragnwash\.modframework(\.[a-z]+)?", guid):
            fail(f"{rel(cs)}: GUID {guid} does not follow com.tomxv.dragnwash.modframework[.<library>]")

# ---- changelog --------------------------------------------------------------
changelog = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8-sig")
for project, name in CHANGELOG_NAMES.items():
    if project not in versions:
        fail(f"{project}: project not found, but CHANGELOG_NAMES lists it")
        continue
    heading = f"### {name} {versions[project]}"
    if not re.search(rf"^{re.escape(heading)}\s*$", changelog, re.M):
        fail(f"CHANGELOG.md: no \"{heading}\" entry for the current version")
for project in versions:
    if project != PRELOADER and project not in CHANGELOG_NAMES:
        fail(f"{project}: add it to CHANGELOG_NAMES in tools/check-repo.py")

# ---- documentation links ----------------------------------------------------
link = re.compile(r"(?<!!)\[[^\]]*\]\(([^)\s]+)\)")
# Only files in the repository: a packed release folder has READMEs without docs/.
import subprocess
tracked = subprocess.run(["git", "ls-files", "*.md"], cwd=ROOT, capture_output=True, text=True, encoding="utf-8").stdout.split()
for md in sorted(ROOT / t for t in tracked):
    text = md.read_text(encoding="utf-8-sig")
    # Links inside code blocks are examples, not links.
    text = re.sub(r"```.*?```", "", text, flags=re.S)
    for m in link.finditer(text):
        target = m.group(1)
        if re.match(r"^[a-z]+:", target) or target.startswith("#"):
            continue
        path = target.split("#", 1)[0]
        if path and not (md.parent / path).exists():
            fail(f"{rel(md)}: link to {target} points at a file that does not exist")

if errors:
    for e in errors:
        print(f"::error::{e}")
    print(f"{len(errors)} problem(s) found.")
    sys.exit(1)
print(f"OK: {len(versions)} projects, {len(guids)} GUIDs, changelog and links checked.")
