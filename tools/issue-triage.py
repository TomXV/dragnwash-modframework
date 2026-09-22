"""Issue triage: labels a new issue by kind, area and severity, and asks for what a bug report is missing.

Run by .github/workflows/issue-triage.yml when an issue is opened, in this
repository and (through workflow_call) in the localization mod's. The judging
is done by TypeSafe's Jev model, which answers typed questions about a text:
a choice, a 0-1 truth value or a score, each with a confidence. It writes no
text; every comment this posts is one of the fixed templates below.

    python tools/issue-triage.py                  # the issue in $GITHUB_EVENT_PATH
    python tools/issue-triage.py --issue 40       # one issue of $GITHUB_REPOSITORY
    python tools/issue-triage.py --issue 40 --dry-run   # judge only, change nothing

Needs TYPESAFE_API_KEY (a repository secret) and, unless --dry-run, GH_TOKEN with
issues: write. Without the key it says so and does nothing: triage is a help, so
it never fails the run.

What leaves GitHub: the issue's title and body with fenced code, tables, CSV-like
rows and images taken out, so no game text or translation file is sent. Unless
it is a feature request, the log-like lines (errors, exceptions, stack frames) with
user names in paths replaced. Nothing else.

Decisions stay in this file, not in the model: Jev is asked concrete things it
can see ("does it give a version?"), and the rules here turn the answers into
labels. A label is only added when the answer's confidence is at least
CONFIDENT; anything less gets "triage: check" so a person looks. Labels are only
ever added, never removed, and the issue's own template labels win.
"""
import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request

JEV_URL = "https://api.typesafe.ai/v1/systemone"
GITHUB_API = "https://api.github.com"
CONFIDENT = 0.8

# ---- what is asked -----------------------------------------------------------
KINDS = {
    "bug": "Something is broken, crashes, or behaves wrongly",
    "feature": "A request for a new capability or improvement",
    "question": "Asks how to do something or whether something is expected",
    "translation": "Asks for a new language, or reports or offers better wording in a translation",
    "other": "Thanks, artwork or logo ideas, discussion, or anything else",
}
AREAS = {
    "framework-core": "Game start-up, BepInEx loading, window or alt-tab, crashes not tied to one feature",
    "mods-screen": "The in-game Mods screen that lists installed mods",
    "options": "Game options rows added by mods (toggles, sliders, the GameOptions API)",
    "tool-window": "The F1 developer tool window: inspector, console, objects, animation",
    "saves": "Save files, save slots, the Saves tab, flags",
    "assets": "Textures, pictures and fonts, and replacing them",
    "graphs": "The Graphs visual editor and its nodes",
    "bridge": "The Bridge browser page, its access code, MCP clients",
    "install": "Installing, the installer, antivirus warnings, Steam Deck or Linux setup",
    "localization": "The translation mod: languages, translated lines, text of other mods",
    "unknown": "Not enough information, or not about the software",
}
LANGUAGES = {
    "ja": "Written in Japanese",
    "zh": "Written in Chinese",
    "en": "Written in English",
    "other": "Written in another language",
}
CAUSES = {
    "d3d12": "D3D12 Present fails with 887a0001 or DXGI device removed, often after alt-tab or a focus change",
    "font-atlas": "Crash while uploading a font atlas or texture to the GPU",
    "missing-dependency": "BepInEx could not load a plugin because a dependency is missing or too old",
    "patch-conflict": "Two mods patch the same method, or a Harmony patch failed",
    "mod-exception": "An exception thrown from a mod's own code",
    "game": "An error in the game's own code with no mod in the stack trace",
    "unknown": "Not enough information to tell",
}

QUESTIONS = {
    "kind": {"type": "choice", "instructions": "What kind of GitHub issue is this?", "criteria": KINDS},
    "area": {"type": "choice", "criteria": AREAS, "instructions":
             "Which part of the Drag'n Wash mod framework or its translation mod does it concern?"},
    "language": {"type": "choice", "instructions": "Which language is the issue written in?", "criteria": LANGUAGES},
    "has_version": {"type": "noul", "instructions":
                    "The text states a version number of the mod, the framework or the game (like 1.4.3 or v1.2.0)."},
    "has_where": {"type": "noul", "instructions":
                  "The text says what the user did or where in the game the problem happens, "
                  "so a developer could try to reproduce it."},
    "severity": {"type": "score", "instructions": "How severe is the problem for players?", "criteria": [
        "No problem reported (a question, a request, thanks)",
        "Something is wrong but the game is playable",
        "Crash, freeze, lost saves, or the game cannot be played"]},
}
CAUSE_QUESTION = {"cause": {"type": "choice", "criteria": CAUSES, "instructions":
                            "What most likely caused this Drag'n Wash (Unity, BepInEx mods) crash or error?"}}

# ---- labels -----------------------------------------------------------------
# name -> (colour, description). Created on first use.
KIND_LABELS = {"bug": "bug", "feature": "enhancement", "question": "question", "translation": "translation"}
LABELS = {
    "bug": ("d73a4a", "Something isn't working"),
    "enhancement": ("a2eeef", "New feature or request"),
    "question": ("d876e3", "Further information is requested"),
    "translation": ("0e8a16", "A language or the wording of a translation"),
    "needs info": ("fbca04", "Waiting for a version, steps or a log from the reporter"),
    "severity: crash": ("b60205", "A crash, freeze, lost save, or the game cannot be played"),
    "triage: check": ("ededed", "The automatic triage was unsure; a person should look"),
}
for _area in AREAS:
    if _area != "unknown":
        LABELS["area: " + _area] = ("c5def5", AREAS[_area])
for _cause in CAUSES:
    if _cause != "unknown":
        LABELS["cause: " + _cause] = ("f9d0c4", CAUSES[_cause])

# ---- the only comment it writes ----------------------------------------------
ASK_MARK = "<!-- issue-triage: needs-info -->"
ASK = {
    "ja": "報告ありがとうございます。原因を調べるために、次のうち書かれていないものを教えてください。\n\n"
          "- バージョン（ModFramework、使っている Mod、ゲーム）\n"
          "- 何をしたときに起きたか（手順）\n"
          "- ログ: ゲームのフォルダーの `BepInEx/LogOutput.log`（クラッシュしたときは CrashReporter のレポートも）",
    "zh": "感谢您的报告。为了查明原因，请补充以下尚未提供的信息：\n\n"
          "- 版本（ModFramework、所用的 Mod、游戏）\n"
          "- 在做什么操作时发生（重现步骤）\n"
          "- 日志：游戏文件夹中的 `BepInEx/LogOutput.log`（如果崩溃，也请附上 CrashReporter 的报告）",
    "en": "Thanks for the report. To find the cause, please add whichever of these is missing:\n\n"
          "- Versions (ModFramework, the mods you use, the game)\n"
          "- What you did when it happened (steps)\n"
          "- The log: `BepInEx/LogOutput.log` in the game folder (after a crash, the CrashReporter report too)",
}
ASK_FOOTER = ("\n\n<sub>This comment and the labels were added automatically; a person reads every issue. "
              "このコメントとラベルは自動で付けています。Issue はすべて人が読みます。</sub>")

# ---- what is sent ------------------------------------------------------------
FENCE = re.compile(r"```.*?(```|\Z)", re.S)
IMAGE = re.compile(r"<img[^>]*>|!\[[^\]]*\]\([^)]*\)")
TABLE_OR_CSV = re.compile(r"^.*[,\t|].*[,\t|].*$", re.M)
USER_PATH = re.compile(r"([A-Za-z]:\\+Users\\+|/home/|/Users/)[^\\/\s]+", re.I)
LOG_LINE = re.compile(r"(\[(Error|Fatal|Warning)\s*:|Exception|\bat [\w.<>`]+\s*\(|Crash|DXGI|D3D1[12]|"
                      r"887a000\d|missing dependencies|Harmony|patch)", re.I)


def prose(text):
    """The report's own words, without anything that could hold game text."""
    text = FENCE.sub("[code]", text or "")
    text = IMAGE.sub("[image]", text)
    text = TABLE_OR_CSV.sub("[row]", text)
    return USER_PATH.sub(r"\1<user>", text)[:4000]


def log_lines(text):
    """Error, exception and stack lines from anywhere in the report, paths anonymised."""
    lines = [USER_PATH.sub(r"\1<user>", line.strip()) for line in (text or "").splitlines()]
    return "\n".join(line for line in lines if LOG_LINE.search(line) and "," not in line)[:4000]


# ---- the two APIs -------------------------------------------------------------
def post_json(url, body, headers, method="POST"):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(url, data, {"Content-Type": "application/json", **headers}, method=method)
    with urllib.request.urlopen(req, timeout=60) as r:
        raw = r.read()
        return json.loads(raw) if raw else None


def jev(state, questions):
    return post_json(JEV_URL, {"state": state, "model": "jev-latest", "questions": questions},
                     {"Authorization": "Bearer " + os.environ["TYPESAFE_API_KEY"]})["answers"]


def github(path, body=None, method="GET"):
    return post_json(f"{GITHUB_API}/repos/{os.environ['GITHUB_REPOSITORY']}{path}", body,
                     {"Authorization": "Bearer " + os.environ["GH_TOKEN"],
                      "Accept": "application/vnd.github+json"}, method)


# ---- deciding ------------------------------------------------------------------
def decide(issue, answers, cause):
    """Turns Jev's answers into the labels to add and whether to ask for more."""
    existing = {label["name"] for label in issue.get("labels", [])}
    add, unsure = [], []
    kind, area = answers["kind"], answers["area"]

    # The kind is only acted on when it is certain: from the issue form's own
    # label, or a confident answer. An unsure kind decides nothing below, so an
    # unsure "bug" never asks the reporter for more.
    template_kind = existing & set(KIND_LABELS.values())
    kind_name = None
    if template_kind:  # the issue form already said what it is
        kind_name = next(k for k, v in KIND_LABELS.items() if v in template_kind)
    elif kind["confidence"] >= CONFIDENT:
        kind_name = kind["choice"]
        if kind_name in KIND_LABELS:
            add.append(KIND_LABELS[kind_name])
    else:
        unsure.append("kind")

    if area["choice"] != "unknown":
        if area["confidence"] >= CONFIDENT:
            add.append("area: " + area["choice"])
        else:
            unsure.append("area")

    is_bug = kind_name == "bug"
    if is_bug and answers["severity"]["score"] >= 1.5:
        add.append("severity: crash")
    if cause and cause["choice"] != "unknown":
        if cause["confidence"] >= CONFIDENT:
            add.append("cause: " + cause["choice"])
        else:
            unsure.append("cause")

    needs_info = is_bug and (answers["has_version"]["noul"] < 0.5 or answers["has_where"]["noul"] < 0.5)
    if needs_info:
        add.append("needs info")
    if unsure:
        add.append("triage: check")
    return [label for label in dict.fromkeys(add) if label not in existing], needs_info, unsure


def ensure_labels(names):
    have = {label["name"] for label in github("/labels?per_page=100")}
    for name in names:
        if name not in have and name in LABELS:
            colour, description = LABELS[name]
            github("/labels", {"name": name, "color": colour, "description": description[:100]}, "POST")


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--issue", type=int, help="issue number (default: the one in the event)")
    parser.add_argument("--dry-run", action="store_true", help="judge and print, change nothing")
    args = parser.parse_args()

    if not os.environ.get("TYPESAFE_API_KEY"):
        print("::notice::TYPESAFE_API_KEY is not set; issue triage skipped.")
        return
    if args.issue:
        issue = github(f"/issues/{args.issue}")
    else:
        issue = json.load(open(os.environ["GITHUB_EVENT_PATH"], encoding="utf-8"))["issue"]
    if issue.get("pull_request"):
        print("A pull request, not an issue; skipped.")
        return

    body = issue.get("body") or ""
    try:
        answers = jev(f"Title: {issue['title']}\n\n{prose(body)}", QUESTIONS)
        lines = log_lines(body)
        cause = jev(lines, CAUSE_QUESTION)["cause"] if lines and answers["kind"]["choice"] != "feature" else None
    except (urllib.error.URLError, KeyError, ValueError) as e:
        print(f"::warning::Issue triage could not reach the model ({e}); nothing changed.")
        return

    add, needs_info, unsure = decide(issue, answers, cause)
    language = answers["language"]["choice"]
    owner = os.environ["GITHUB_REPOSITORY"].split("/")[0].lower()
    ask = needs_info and issue["user"]["login"].lower() != owner

    report = {
        "issue": issue["number"],
        "kind": (answers["kind"]["choice"], round(answers["kind"]["confidence"], 2)),
        "area": (answers["area"]["choice"], round(answers["area"]["confidence"], 2)),
        "language": language,
        "has_version": round(answers["has_version"]["noul"], 2),
        "has_where": round(answers["has_where"]["noul"], 2),
        "severity": round(answers["severity"]["score"], 2),
        "cause": cause and (cause["choice"], round(cause["confidence"], 2)),
        "unsure": unsure,
        "add_labels": add,
        "ask_for_info": ask,
    }
    print(json.dumps(report, ensure_ascii=False, indent=1))
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as f:
            f.write(f"### Issue #{issue['number']}{' (dry run)' if args.dry_run else ''}\n\n"
                    f"```json\n{json.dumps(report, ensure_ascii=False, indent=1)}\n```\n")
    if args.dry_run:
        return

    if add:
        ensure_labels(add)
        github(f"/issues/{issue['number']}/labels", {"labels": add}, "POST")
    if ask:
        comments = github(f"/issues/{issue['number']}/comments?per_page=100")
        if not any(ASK_MARK in (c.get("body") or "") for c in comments):
            text = ASK.get(language, ASK["en"])
            github(f"/issues/{issue['number']}/comments", {"body": f"{ASK_MARK}\n{text}{ASK_FOOTER}"}, "POST")


if __name__ == "__main__":
    try:
        main()
    except urllib.error.HTTPError as e:
        # A GitHub or model error is reported, never raised: triage must not fail the run.
        print(f"::warning::Issue triage stopped: HTTP {e.code} {e.read().decode(errors='replace')[:300]}")
    sys.exit(0)
