"""Commit checker: no tool's attribution in the history, run by CI on every push and pull request.

This repository's history names the people who decided what a commit should say,
not the editor, the assistant or the IDE that typed it. The check refuses a
commit whose message credits a tool, and one whose author or committer IS a
tool. A human co-author, and a human whatever their name, are welcome.

    python tools/check-commits.py                 # what is not yet on origin/main
    python tools/check-commits.py <base>..<head>  # an explicit range (CI passes this)
    python tools/check-commits.py <base> <head>   # the same, as two arguments

A failure's reason is an annotation on the run. To fix one before it is pushed:

    git commit --amend                       # the message of the last commit
    git commit --amend --reset-author        # and its author, to you
    git rebase -i <base>                     # an older one, then reword or edit it
"""
import re
import subprocess
import sys

# ---- messages ---------------------------------------------------------------
# Attribution that must not appear in a commit message. Each entry is a name for
# the report and a pattern matched against the whole message, case-insensitively.
# A co-author who is a person is welcome; these are tools.
#
# The link rules are deliberately narrow. claude.ai is where a session lives, so
# any address there is one of those links and none of them belongs in a commit
# message. A documentation address such as docs.anthropic.com is left alone: the
# Bridge answers AI clients over MCP, and a commit citing the specification it
# follows is a normal commit. The @anthropic.com rule needs the at sign, so it
# catches the address in a trailer and not a link to a page.
FORBIDDEN_MESSAGE = [
    ("Claude's attribution", r"co-?authored-by:.*\b(claude|anthropic)\b"),
    ("an Anthropic address", r"@anthropic\.com"),
    ("Claude Code's footer", r"generated with \[?claude code"),
    ("an assistant's footer", r"\N{ROBOT FACE}\s*generated with"),
    ("a Claude session link", r"claude\.ai/"),
    ("a Claude Code link", r"claude\.com/claude-code"),
    ("Copilot's attribution", r"co-?authored-by:.*\bcopilot\b"),
    ("Cursor's attribution", r"co-?authored-by:.*\bcursor(\s|@|>)"),
    ("Codex's attribution", r"co-?authored-by:.*\bcodex\b"),
    ("Devin's attribution", r"co-?authored-by:.*\bdevin\b"),
    ("Aider's attribution", r"co-?authored-by:.*\baider\b"),
]

# ---- identities -------------------------------------------------------------
# The author and the committer of every commit are checked too: a message can be
# written in this repository's voice while the commit itself is signed by a tool.
#
# Addresses are matched whole, names only where no person would use them. Claude
# is a person's name - a translator called Claude, committing from their own
# address, passes - so the name rules ask for a model or a bot suffix after it.
#
# What is meant to pass, and does: dependabot[bot] and github-actions[bot],
# which open the dependency pull requests; GitHub <noreply@github.com>, the
# committer GitHub writes when a pull request is merged from the web; and every
# person's own address, including the GitHub noreply addresses people use.
FORBIDDEN_EMAIL = [
    ("an Anthropic address", r"@anthropic\.com$"),
    ("a Claude account", r"^(\d+\+)?claude([-.]?code)?(\[bot\])?@"),
    ("a Copilot account", r"^(\d+\+)?(github[-.]?)?copilot(\[bot\])?@"),
    ("a Cursor account", r"^(\d+\+)?cursor([-.]?agent)?(\[bot\])?@"),
    ("a Codex account", r"^(\d+\+)?codex(\[bot\])?@"),
    ("a Devin account", r"^(\d+\+)?devin([-.]?ai)?(\[bot\])?@"),
    ("an Aider account", r"^(\d+\+)?aider(\[bot\])?@"),
]

FORBIDDEN_NAME = [
    ("Claude", r"^claude\[bot\]$|^claude[ -](code|opus|sonnet|haiku|ai|\d)"),
    ("Copilot", r"^(github )?copilot(\[bot\])?$"),
    ("Cursor", r"^cursor([ -]agent)?(\[bot\])?$"),
    ("Codex", r"^codex([ -]cli)?(\[bot\])?$"),
    ("Devin", r"^devin([ -]ai)?(\[bot\])?$"),
    ("Aider", r"^aider(\[bot\])?$"),
]

SEPARATOR = "\x1e"  # a record separator no commit message contains
FIELDS = "%H%x1e%an%x1e%ae%x1e%cn%x1e%ce%x1e%B%x1e"


def git(*args):
    done = subprocess.run(["git", *args], capture_output=True, text=True, encoding="utf-8")
    if done.returncode != 0:
        return None
    return done.stdout


def exists(ref):
    return git("rev-parse", "--verify", "--quiet", ref + "^{commit}") is not None


def default_range():
    """Everything on this branch that origin/main does not have yet."""
    for base in ("origin/main", "main"):
        if exists(base) and git("merge-base", base, "HEAD") is not None:
            return base + "..HEAD"
    return "HEAD~1..HEAD" if exists("HEAD~1") else "HEAD"


def commits(rev_range):
    out = git("log", "--format=" + FIELDS, rev_range)
    if out is None:
        return None
    parts = out.split(SEPARATOR)
    found = []
    for i in range(0, len(parts) - 5, 6):
        found.append({
            "sha": parts[i].strip(),
            "author_name": parts[i + 1],
            "author_email": parts[i + 2],
            "committer_name": parts[i + 3],
            "committer_email": parts[i + 4],
            "message": parts[i + 5],
        })
    return found


def offender(commit):
    """The first rule this commit breaks, as (what, where), or None."""
    for who, pattern in FORBIDDEN_MESSAGE:
        if re.search(pattern, commit["message"], re.I):
            return who, "its commit message"
    for role in ("author", "committer"):
        name, email = commit[role + "_name"], commit[role + "_email"]
        for who, pattern in FORBIDDEN_EMAIL:
            if re.search(pattern, email, re.I):
                return who, f"its {role} ({name} <{email}>)"
        for who, pattern in FORBIDDEN_NAME:
            if re.search(pattern, name.strip(), re.I):
                return who, f"its {role} ({name} <{email}>)"
    return None


def main(argv):
    if len(argv) == 2:
        rev_range = argv[0] + ".." + argv[1]
    elif len(argv) == 1:
        rev_range = argv[0]
    elif not argv:
        rev_range = default_range()
    else:
        print("usage: check-commits.py [<base>..<head> | <base> <head>]")
        return 2

    found = commits(rev_range)
    if found is None:
        # A range CI cannot resolve (a new branch, or a force-push that left
        # github.event.before behind) is not a reason to fail the build.
        print(f"Nothing to check: {rev_range} is not a range this clone can resolve.")
        return 0

    errors = []
    for commit in found:
        broke = offender(commit)
        if broke:
            who, where = broke
            subject = commit["message"].strip().split("\n")[0][:72]
            errors.append(
                f"{commit['sha'][:7]} carries {who} in {where}: \"{subject}\". "
                f"This history names only the people who decided what the commit should say. "
                f"Fix it (git commit --amend, add --reset-author for the author, or git rebase -i) and push again."
            )

    if errors:
        for e in errors:
            print(f"::error::{e}")
        print(f"{len(errors)} commit(s) with a tool's attribution.")
        return 1

    print(f"OK: {len(found)} commit(s) checked, no tool's attribution ({rev_range}).")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
