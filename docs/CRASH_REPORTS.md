# Crash reports

[日本語](CRASH_REPORTS.ja.md)

> **Experimental.** On the `experimental/d3d12-trace` branch, not in a release yet.

When the game crashes, what happened just before is usually lost: BepInEx's log stops mid-line, and Unity's crash folder has a native stack but no idea which mod was doing what. The core keeps a short record of each session and turns it into a report when the game did not exit cleanly. Everything stays on this computer.

## What is recorded

`BepInEx/CrashReports/session.log`, one line per note, written and flushed at once so the last line survives a crash:

```
04:22:07.528 f18344 main scene loaded 'Wash' (Single)
04:22:07.611 f18351 main unity-error NullReferenceException: ...
04:22:09.002 f18470 main beat 16.7 ms/frame
```

- the start: framework and Unity versions, graphics API and card, screen mode, launch options; then every loaded mod with its version;
- scene loads and unloads, mod reloads;
- Unity errors, exceptions and graphics device messages;
- a heartbeat every 10 seconds (every second while GPU uploads are traced), which shows how long after the last note the game stopped;
- notes mods add with `CrashReports.Note(ownerGuid, category, message)`.

A clean exit ends the file with `END clean exit`. A long session rolls over at 4 MB, keeping the previous part as `session.1.log`.

## The report

At the next start, a `session.log` without that marker means the game crashed, froze and was closed, or was killed. The core then writes `BepInEx/CrashReports/<date>_<time>/` with:

- `report.txt`: when it ended, the versions and mods, the last 40 notes, and the native stack from Unity's own crash folder (`%TEMP%/<company>/<product>/Crashes/Crash_*`) when one was made within two minutes of the end;
- `session.log` (and `session.1.log` if the session rolled over).
- `crash.dmp`: Unity's own memory dump of the crash, copied from its crash folder.

The log says where the report is. The last ten reports are kept. To report a problem, attach `report.txt`.

## The crash report window

On Windows the core also starts `CrashReporter.exe` (next to the core DLL), a small program outside the game that waits for the game to close. After a clean exit it quits without a trace. Otherwise it waits for Unity's crash handler, writes the report (the same code the core would run at the next start; whichever comes first marks the record as reported) and shows it right away in a window of its own, in English, Japanese or Chinese after Windows' language:

- **What happened**, in words: a known Direct3D 12 crash (UUM-140564), the graphics device that stopped responding, a freeze, a stop without a crash (killed, power loss), or another crash inside Unity;
- **What you can do** about it, when there is something;
- the details (when, the scene, how long the session ran, the last error, the graphics) and where the report is;
- **Open report folder**, **Copy report** (report.txt, for a bug report) and **Close**.

The window is drawn by Windows, not by the game, so it works however the game went down. It is not started under Wine or Proton (Steam Deck, Linux); there the report is written at the next start. `CrashReporter.exe --show <report folder>` opens a report again. `[Diagnostics] CrashReporterWindow` switches it off.

## Memory dumps

A dump is a snapshot of the game process: every thread's stack and the modules loaded, which a debugger (WinDbg, Visual Studio) opens with the game's PDB files (the game ships `UnityPlayer_Win64_player_mono_x64.pdb`). It answers what a log cannot: where each thread was when it went wrong.

- **Crashes**: Unity writes `crash.dmp` itself; the report keeps a copy.
- **Freezes**: Unity writes nothing when the game hangs, so a watchdog thread does. When the main thread has finished no frame for 15 seconds while the game window has focus, it writes a minidump of the process (a few MB, Windows) into `BepInEx/CrashReports/<date>_<time>_hang/` and notes it in the session record; when the game runs again, that is noted too. A game in the background is not counted: it may stop running frames on purpose.

A dump holds part of the game's memory. **Share it privately with whoever investigates, not in a public issue**; `report.txt` is the part meant for public reports.

## Settings

Options → Mods → Drag'n Wash ModFramework → **Diagnostics** (Crash reports and Trace GPU uploads apply at the next start):

- **Crash reports** (`[Diagnostics] CrashReports`, on): the record and the reports.
- **Crash report window** (`[Diagnostics] CrashReporterWindow`, on): the separate window on Windows.
- **Memory dump when the game freezes** (`[Diagnostics] HangDumps`, on, advanced): the watchdog's dump.
- **Trace GPU uploads** (`[Diagnostics] TraceGpuUploads`, off, advanced, experimental): adds a note for every texture upload and creation, dynamic font and TextMeshPro atlas growth, mesh upload, asset bundle load and released GPU resource, with its size and the mod on the calling stack. It patches Unity methods and walks the stack for each, so turn it on only to investigate.

## Why: the Direct3D 12 crash

On Direct3D 12 this Unity version crashes on the render thread in `D3D12ScratchAllocator::DestroyScratch`, called from `ReleaseExcessScratch` when a frame trims the scratch memory that GPU uploads go through (Unity issue UUM-140564). Every crash folder on the maintainer's machine so far (30, 2026-09-11 to 09-16) has that stack; the last lines before them are opening Options or the Tool window. Which upload makes the scratch memory grow is not known yet; *Trace GPU uploads* is there to find out, so the framework can avoid it (split or move the upload to startup) rather than only tell players to use `-force-d3d11`.

## For mod authors

```csharp
CrashReports.Note(MyPlugin.Guid, "load", $"importing {count} textures from {folder}");
```

Notes are cheap and safe from any thread. Leave one before something heavy or risky, so a crash right after it points at it. Keep them short, and never put personal data in them: reports are meant to be attached to public bug reports.
