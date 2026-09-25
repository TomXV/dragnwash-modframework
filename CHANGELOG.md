# Changelog

Versions of the core and of each library are separate, and follow semantic versioning: from 1.0.0 on, a change that breaks the public API comes only with a new major version.

## Unreleased

### Core

- The update check keeps more of GitHub's answer: the release notes, the release page, when it was published, and each file's name, size, address and SHA-256. It's the same single request as before, once a day, so nothing new goes online. The Mods screen works as it did.
- What the check found is written to `BepInEx/cache/DragNWash.ModFramework/updates.json`, for a launcher that runs before the game and shows updates without going online itself. It lists each installed mod that names its GitHub repository: the installed version, its folder under `plugins`, whether the installer's `mod-install.json` is there, and the latest release. It's written a few seconds after start, when a check brings a result, and when **Check for updates** is switched on or off; with checking off it lists no mods. [docs/LAUNCHER.md](docs/LAUNCHER.md) describes the format.
- After updating from 1.5.0, which kept only each release's tag, each repository is checked once more at the first start so the new details get filled in. After that it's once a day again.
- On the Mods screen, a mod our installer put in gets **Update now** next to **Open release page** when it has a newer release. It asks first, like **Uninstall**: the button turns into **Quit and update**, a yellow note says the game will quit and unsaved progress may be lost, and **Open release page** turns into **Cancel**. Pressing it again leaves `BepInEx/cache/DragNWash.ModFramework/update-request.json` and quits; the launcher updates the mod after the game has closed and starts it again. When the game wasn't started through the launcher, it starts the launcher first. If the launcher isn't installed, the game doesn't quit and says so. The game itself still downloads nothing, and the launcher only goes online after you press Update. [docs/LAUNCHER.md](docs/LAUNCHER.md#updating-from-the-mods-screen) has the details.
- A **Launcher** section in the framework's settings: **Logo lettering** (Handwriting or Typewriter) and **Progress bar** (Bottom edge or Under text), for how the launcher's logo screen looks. The launcher reads them from the config file.
- At the top of that section, **Check for mod updates when the game starts (Steam launch option)**: the installer's checkbox, from the game. The switch shows what Steam has now for the account you're playing on, so it has no reset button. Switching it asks first, like **Update now**: a yellow note says the game will quit and Steam and the game will start again, and the switch turns into **Cancel** and **Restart and apply**. Going ahead starts the launcher and quits; the launcher closes Steam, changes the option and starts Steam and the game again. Only on Windows, with the launcher in place (not on the Steam Deck). [docs/LAUNCHER.md](docs/LAUNCHER.md#switching-the-launch-option-from-the-mods-screen) has the details.

### Launcher

- New: `Launcher.exe`, a small program that runs before the game from Steam's launch options (`"<game>\BepInEx\DragNWash.Installer\Launcher.exe" %command%`). When the game's update check found new versions last time, it shows them with their release notes before the game starts, and **Update and play** downloads, checks, backs up and installs them, then starts the game. **Skip this version** keeps a version from bringing the window up again. With nothing to show, the logo plays for about four seconds ("No updates", then "Starting the game" while the bar fills up). See [docs/LAUNCHER_APP.md](docs/LAUNCHER_APP.md).
- The game starts only after the launcher's window has faded out and closed, on every path: after the logo, **Play without updating**, **Update and play**, and the countdown after an update asked for in the game (or its **Start now**). The launcher then waits for the game with no window.
- The window's ✕ closes the launcher without starting the game, on every screen. While files are being written it does nothing, and before that it cancels the download first. **Play without updating** is the way to play without the update.
- It goes online only when you press Update, and only to github.com and release-assets.githubusercontent.com. It installs only mods the installer put in, from the release's zip at an address it builds from the repository and tag, after checking the size and SHA-256 GitHub gave. Installing is Install.exe's own code, compiled into both, so it backs up first and puts everything back if copying fails.
- It stays the game's parent while you play, so Steam keeps counting play time, and when the game quits to be updated (`update-request.json`) it installs the update and starts the game again after a 10-second countdown. **Start now** skips the wait, and **Don't start it now** closes the window without starting the game. `Launcher.exe --update-after-exit --wait-pid <pid>` does the same for a game started without it, and has Steam start the game again.
- `Launcher.exe --launch-option on|off --wait-pid <pid>`, for that switch on the Mods screen. Once the game has closed, it closes Steam (waiting up to 90 seconds), puts itself into the launch options or takes itself out with Install.exe's own code (the backup first, nothing else in the file touched), and after the 10-second countdown starts Steam and the game. If Steam doesn't close, nothing changes, and you can try again or just start the game. If the file can't be written, nothing changes, the backup stays, and Steam and the game start as they were. ✕ never starts the game, but starts Steam again if the launcher had closed it. It doesn't go online.
- Without the WebView2 runtime there's no window: the game starts as usual. Whatever fails in the launcher, the game still starts: a page that doesn't come up or a window that doesn't close in time is hidden, and the game starts anyway.
- The framework's zip has it in `BepInEx/DragNWash.Installer/`, with the three WebView2 files it needs. Install.exe puts it in the game folder and sets the launch option (see Installer below).

### Installer

- Install.exe puts the launcher (`Launcher.exe` and its three WebView2 files) into `BepInEx\DragNWash.Installer` whenever it installs or updates the framework, from the framework's zip, whether or not the launch option is set: the game's Update button uses it too. The list of what Install will do says so. It goes through the same backup and putting back as every other file, an older launcher never replaces a newer one, and when the launcher installs an update itself, its files in use are renamed to `.old` and deleted by the next install.
- New checkbox in the Install group: **Check for mod updates when the game starts (sets the Steam launch option)**, ticked to begin with, or unticked when the launcher is already in the game folder but not in the launch options (you left it out last time). It puts `"<game>\BepInEx\DragNWash.Installer\Launcher.exe" %command%` in front of the game's launch options in Steam, keeping whatever you had: `-force-d3d11` becomes `"...\Launcher.exe" %command% -force-d3d11`, and anything before `%command%` stays before it. Unticked while the launcher is in the launch options, Install takes it out again, and `-force-d3d11` comes back as it was. The list of what Install will do has a line for either.
- It writes each Steam account's `localconfig.vdf` (the accounts that have played the game and the one that signed in last): only that one value changes, the rest of the file stays byte for byte, the file as it was is kept as `localconfig.vdf.dnw-backup`, and the new one replaces it in one step. A file that can't be read as expected is left alone, and the log says why.
- Steam writes over that file while it runs, so when Steam is running, a window comes up after you press Install, Update or Uninstall, before the download question and before anything changes: **Close Steam for me** (asks Steam to exit and waits; after 90 seconds it says Steam hasn't closed), **I'll close it** (waits), or **Skip this option** (goes on without changing the launch options this time). It carries on by itself once Steam has closed, and the close box or Esc cancel the whole install. Steam is started again afterwards when the installer closed it.
- Uninstall: when ModFramework itself goes, the launcher comes out of the launch options first (the same window if Steam is running), then `BepInEx\DragNWash.Installer` goes with it. While other mods still use the framework, the launcher and the launch option stay, and only the installer's backup is removed (before, any mod's uninstall removed the whole folder). A launch option that couldn't be taken out keeps the launcher in place, so the game still starts from Steam.
- Command line: `--launch-option on|off|keep` (`on` is the default for `--install`; `--uninstall` takes it out when ModFramework goes, unless `keep`). It never closes Steam: while Steam runs, the launch options are left alone and the log says so.
- `installer/tests`: checks of the launch option rules and the `localconfig.vdf` edits on made-up files (no apps block, the game without launch options, options you had, the launcher already there or from a moved game folder, escaped quotes, Windows line ends, several accounts). CI and `docker/checks.sh` run them.

## 2026-09-23: a whole new look

The core, the preloader patcher and every library go to 1.5.0, so from here on one number says which release you have. For mods, everything is additive, as before: a mod built for 1.4.x keeps working.

### Core 1.5.0

#### Mods screen: a settings-app look

- The list and the details sit on solid panels in the Tool window's colours, with rounded corners. Before, they were drawn on the game's see-through panel, so how easy the text was to read depended on the picture behind the menu. The rounded corners are small shapes the framework draws in code when the game starts; there are no image files.
- The frame that shows where the gamepad is has rounded corners and the accent colour.
- The list puts your own mods first, under "Your mods". The framework and its libraries are folded into "Libraries" at the bottom, so they no longer fill the top of the list; press the row to open it. When a library needs attention or has an update, the folded row says so.
- Each row shows the mod's icon (or its initials on a coloured square when it has none), its name at one size, its version and author, a picture of its switch, and every tag that applies: Not loaded, Conflict, Online, Update and Needs restart. The row of the mod the details show is marked with a line on its left. Rows are 80 high instead of 96, so more fit on the screen. Before, a library showed only its Library tag, which hid its Conflict and Update tags, so a library's conflict couldn't be seen from the list.
- A search field above the list finds mods by name, id or author, and the filters All, On, Off and Needs attention show how many mods each holds. "Needs attention" means a conflict, a connection the mod didn't declare, a mod that didn't load, or a feature this game build doesn't have.
- The details have a header that stays put: the mod's icon, its whole name, version, author and website, and its switch. Next to the switch is the word for what the mod is set to now (On or Off), so it's no longer unclear whether the green "On" button meant the state or what pressing it would do. The framework shows "Required" there instead.
- Under the header, every note about the mod gets a band of its own with a coloured bar: the "press again" confirmations, why a mod didn't load, going online without saying so (with a button to its Internet tab), conflicts, features this game build doesn't have, a new version (with "Open release page"), the hosts it declared, reloads and the running check. None of them is dropped any more; when there are very many, the bands scroll. Before, the panel had room for two or three lines, and a long label like "Went online without saying so:" could take two of them, so the update or conflict notes after it disappeared without a word.
- The mod's pages are tabs now: About, Settings (with how many settings there are), Internet (with a warning dot when the mod went online without saying so) and every page the mod added, however many there are. They go on to a second row when they don't fit. Before, the details had room for two page buttons at most, so a mod with an update and an Internet page had no way to reach its Graphs page. About has the description, what the mod uses and what needs it, as chips, and the Uninstall button. Pages other mods add get the tab's area, as they got the details before.
- The Internet tab uses the Tool window's colours.
- Settings is a tab next to About, and the list of mods stays on the left while it's open. Every setting is a row with its name and description on the left and its control on the right, grouped under its section, so you can read what a setting does while you change it: a switch for on/off, a slider for a number with a range (with - and +), - and + with a box you can type in for other numbers, buttons side by side for up to four choices, the key and **Change** for a shortcut, and a colour's swatch beside its value. Before, the list turned into the settings and only one setting's description showed at a time.
- A row whose value isn't the default has a dot and a button back to the default, and says what the default is. "Saved" shows on the row for two seconds after each change, as before, and changes are still saved at once.
- A warning about a setting sits right under its row: another setting on the same key, a typed value that wasn't accepted, and "Change this in the mod's config file". Before, those shared one small note and only one could show at a time. A row that needs a restart says so under its description.
- The slider stops at the same round values as - and +; left and right on the gamepad move it one step, and up and down go to the next row. Esc while a key is being taken cancels it instead of becoming the key.
- The - and + buttons move a number by a round step: about a twentieth of the range, rounded down to 1, 2 or 5 times a power of ten, and the value goes to the next multiple of it. The old `<` and `>` buttons moved Graphs' FrameBudgetMs (0.1 to 8) by 0.395, so one press from 1 gave 1.395; now it moves by 0.2 and gives 1.2. A number without a range moves by 1, or 0.1 when it has decimals.
- Changing a value builds only that row again, not the whole screen, so the screen stays quick with many mods and many settings.
- Gamepad: LB and RB go to the tab on the left or right, and Y goes to the search field. With a gamepad connected, a line at the bottom of the details names these buttons. Back steps out one level at a time: from something on a tab to the tab, from the tabs to the mod's row, from the list to Options. A click on the game's Back button still leaves at once. Whatever the pad selects is scrolled into view, in the list and in a tab.
- A mod with six settings or more has a search field above them, like the one above the list. It finds a setting by its name, key, description or section and hides the rest, sections and all, without building the tab again.
- In the Libraries group, a library goes by the part after the framework's name (Inspector, Graphs), since the group already says whose it is. The whole name was cut to "Drag'n Wash ModFramework: Insp...". The details still show all of it.
- Back (Esc, or B on the gamepad) while typing a setting's value puts the old value back and stops typing. Before, leaving the field saved whatever was half typed.
- The gamepad passing over a text field no longer starts typing in it (on the Steam Deck that could bring up the keyboard); A does, the Deck included.
- A shortcut's box is as wide as its key, so on the Steam Deck's smaller screen the description beside it keeps its room. A very long tab title is cut short instead of running past the panel.
- One notch of the mouse wheel moves the list, the notes and a tab about one row. The list used the game's setting, where a notch moved it a few pixels.
- The list's scrollbar is thin and dark like the panels: no track, a grey thumb that turns the accent colour under the pointer. It was a bright light-grey bar. The gamepad passes over it, since what it selects scrolls into view anyway.
- The first heading of the list sits right under the filters, with no empty gap above it.
- The details' title takes two lines at most and ends in ... when longer. A library's title is its short name (Inspector (experimental)), with the whole name on a small grey line under it.
- A mod's initials pass over what is in brackets and words like "experimental", so Inspector (experimental) is I, not IE.

#### Mods screen: frosted glass

- The list and the details sit on see-through dark panels now, with a faint light line around each, instead of solid ones, so the game shows behind them a little. Rows, notes, settings, the chosen filter and chips are darker see-through cards on top, and the search and value fields have a thin edge.
- Small grey text, and the accent and red colours where they're text (a website, "New version available:", "Saved", the Not loaded tag, a band's red heading), are a little lighter, so they stay easy to read over the brightest picture behind the menu, the white title logo. The switches, bars and lines keep their colours as they were.
- How see-through things are was matched to the design mock in the game's own colour blending, which lets much more through than a browser does at the same numbers.
- Behind each panel is a blurred, dimmed copy of the game's picture, lined up with the scene behind it, so the panels look like frosted glass: buildings and trees turn into soft patches of colour. The blur is about 40 px at 1080 lines, like the design mock, and the same share of the screen at other sizes. It's made with URP's own copying, after the game's colour grading and before the menu is drawn, so the menu itself is never in it: the picture is halved down to 1/32 of the screen (1/64 from about 1300 lines), spread a pixel at that size, and grown back to 1/8. Nothing is added to the game's renderer settings, and no shader comes with the framework.
- `[Mods screen] Glass` ("Frosted glass" in Options → Mods → Drag'n Wash ModFramework → Settings): **Snapshot** takes the picture again every 0.2 seconds (the default), **Every frame** takes it every frame so the glass moves with the game, and **Off** leaves the tint alone with no picture taken. It changes at once, while the screen is open.
- Nothing runs while the Mods screen is closed. Its small textures (about 2.8 MB at 1920x1080, 1.4 MB on the Steam Deck) are made when it opens, made again when the window changes size or the graphics driver drops them, and let go when it closes. Taking a picture reads the screen once and is most of the cost, roughly 0.1 ms of graphics card time at 1920x1080 on a desktop card and 0.2 to 0.3 ms on the Steam Deck (estimates): with Snapshot that's five times a second, with Every frame every frame.
- If the picture can't be made (HDR output, a game update that changed URP, anything else), the log gets one warning and the screen uses the tint alone until the game restarts. If no picture comes for about a second (a scene with no camera the glass can use), it uses the tint alone until the screen is closed. The copy only comes from the game's main camera (or a camera stacked on it), not from another camera that draws something of its own.

#### Mods screen: fixes

- The keyboard works on the Mods screen: the arrow keys move between the list and the details, the right arrow goes from Back into the list, and Enter presses the selected button. Before, the arrow keys only moved between the game's own buttons on the left, so the list could not be reached without the mouse or the pad.
- Notes built from pieces can be translated now: **Same key as** with the other settings' names and then "Both will answer it.", **Not accepted** with the reason, and **Times reloaded this session** with the number and then "What runs now is not the file BepInEx loaded." Each fixed sentence is a text of its own, so a language pack matches it whole. Before, the names and numbers sat inside the sentences, so they stayed in English.
- Pressing a tab in the details no longer flashes white. Every button the screen builds (the tabs, a tab's buttons, the rows' faces) came in white and faded to its colour over about five frames, since setting a button's colours starts a fade from what is drawn, and a new button is drawn white. The colours now land at once, and the short fade stays for hover and press.

#### Mods screen

- The screen opens straight away and lists the loaded mods at once. Reading every plugin DLL and looking for patch conflicts happen off the frame. Until they're in, a "Checking mods..." row ends the list and a band in the details says it's still checking, and On/Off and Uninstall wait for it (Settings doesn't).
- When a page another mod adds throws while it's being built, it shows a short message and **Try again** instead of half a page.
- A shortcut setting tells you when another setting uses the same key: "F1 is also used by Open / close key (Drag'n Wash ModFramework: Tool window). Both will answer it." It sits right under the shortcut's row, whether the clash came from a key you just set or was there already. It only reports; the key stays as you set it.
- Changing a setting shows **Saved** on its row for two seconds, and changes are still saved right away. If a mod turned off BepInEx's saving on every change, the page saves that mod's config file too.
- A native (non-.NET) DLL in the plugins folder is skipped quietly, at Debug level, instead of an Info line saying it couldn't be read. Other read failures are logged as before.

#### Installer: ModFramework from its own release

- `mod-install.json` schema 2 adds a `framework` block. It holds the ModFramework release the mod pins (`version`), the SHA-256 and size of its zip, and `needs`, the lowest version of each plugin folder the mod uses (`DragNWash.ModFramework`, `DragNWash.ModFramework.Text`, ...). Every field is checked: the version has to be digits and dots, the hash 64 lowercase hex digits, the size above 0 and at most 20 MB, and the folder names have to start with `DragNWash.ModFramework`. Schema 1 (the framework inside the mod's zip) installs as it did before, and a zip that brings the framework is used even with schema 2. A schema newer than 2 is refused with "Use the installer from the mod's release". `installer/mod-install.example.json` is schema 2.
- Install.exe: when the mod's zip has no framework, it installs the core, the preloader and only the libraries in `needs` from the pinned release (never Inspector, Overrides, Bridge or Graphs). It fetches a part that's missing, below the mod's minimum, or (for the core and the preloader) older than the pinned release. After that, each part is only updated when the release's copy is newer, so a newer one that another mod brought stays. Inside a framework folder, only the files in the zip get written. When everything is already there, nothing is downloaded and no connection is made.
- The zip comes from `https://github.com/TomXV/dragnwash-modframework/releases/download/v<version>/DragNWash.ModFramework-<version>.zip`. The installer builds that address from the version alone (never from the manifest, and `api.github.com` is never asked) and downloads into a temp folder of the run's own. The download stops as soon as the size differs from the manifest's (or goes past 20 MB), and the file is only used when its SHA-256 matches. Only the folders it needs are unpacked into the staging folder, and they're copied into place with the same backup and putting back as the rest of the install. Both downloads send the User-Agent `DragNWash.Installer/1.1.0`, wait 30 seconds at most, and try once more after a server error or time-out.
- Before the first connection a window comes up, only when a download is needed, and every time one is. It says where it connects (github.com and release-assets.githubusercontent.com, and BepInEx/BepInEx too when BepInEx is downloaded), why, what gets sent (an ordinary HTTPS request and the User-Agent; GitHub sees the IP address) and how the file is checked. It links to the release page and offers **Choose zip...**, **Download and install** or **Cancel**. A zip you choose there is checked the same way, with no internet.
- The list of what Install will do names both hosts. While Install runs, the list turns into a checklist (done, running, still to do) with the download's KB. Afterwards it says what was updated, what was kept and why, which libraries weren't installed, that other mods' folders weren't touched, and where the backup is. The status line shows the installed ModFramework version, and the SmartScreen line mentions that the installer downloads ModFramework.
- When a download fails, it says why: no internet, GitHub not answering, GitHub limiting downloads (with the minutes it gives), the release not published yet (tell the mod's author), a size or SHA-256 that doesn't match (the file is deleted), or a secure connection that couldn't be confirmed (there's no way around that one). The failure window links to the release page, has **Choose zip...**, and, when the installed framework meets every minimum, offers "Install only the mod and keep ModFramework X". Whatever happens, the game folder is left unchanged.
- Command line: `--framework-zip <file>` (a local zip, checked the same way), `--no-download` (never goes online; if BepInEx or ModFramework would have to be downloaded, it stops with nothing changed), `--keep-framework` (the failure window's "install only the mod"), and `--help`. `--install` counts as consent, just as it does for BepInEx.
- Steam Deck script (`install-steamdeck.sh`): the same flow, in the terminal or in kdialog. It asks first (where, why, what is sent), fetches the pinned release from the same address with the same User-Agent, checks the size and SHA-256, installs only what `needs` names, never puts an older part over a newer one, and puts the game folder back if a copy fails part way. `--framework-zip` and `--no-download` work the same as in Install.exe. A choice prompt in the terminal now reads what you type instead of always taking the default.
- Build workflow: every release carries `SHA256SUMS` (`<sha256>  DragNWash.ModFramework-<version>.zip`) next to the zip, both in the draft release and in the workflow artifact.

#### Installer: one loader per game folder, and an install that can be undone

- Install.exe: when another mod loader is in the game folder (KrazenLabs' dnw-modloader, or any Doorstop setup that doesn't start BepInEx), the install stops before anything is downloaded or changed, with "Another mod loader (dnw-modloader) is in this game folder. Only one loader can be installed per game folder, so nothing was changed." It used to get overwritten by BepInEx's `winhttp.dll` and `doorstop_config.ini`. The list of what Install will do says so before it runs. It counts as another loader when there's a `DnWModLoader` folder, a `doorstop_config.ini` whose `target_assembly` (Doorstop 4) or `targetAssembly` (Doorstop 3) isn't `BepInEx\core\BepInEx.Preloader.dll`, or a `winhttp.dll` with neither BepInEx nor a `doorstop_config.ini`. BepInEx's own leftovers (its `winhttp.dll` and config without `BepInEx\core`) are mended as before.
- Uninstall leaves `winhttp.dll`, `doorstop_config.ini` and `.doorstop_version` alone when they start another loader.
- Install.exe: every file an install replaces or deletes is copied to `BepInEx\DragNWash.Installer\backup\<date_time>` first, and BepInEx is unpacked into `BepInEx\DragNWash.Installer\staging` before it's copied into place. If a step fails halfway (a file in use, a folder that can't be written to), everything goes back the way it was: replaced files are restored, new files and new empty folders are removed, and the message says "Something went wrong while copying, so everything was put back as it was (N files)." After a successful install the staging folder goes and only that install's backup is kept (none if nothing was replaced), and the log ends with `Backup: N files -> BepInEx\DragNWash.Installer\backup\<date_time>`. Files the player added are still never deleted, and a newer framework DLL is still kept.
- Uninstalling any mod removes `BepInEx\DragNWash.Installer`, and its list says so.

#### Installer

- Install.exe: you choose **Install** or **Uninstall** first, and only that action's controls are shown, along with a list of what it will do. The list is built from `mod-install.json` and the game folder as it is: whether BepInEx gets downloaded, the framework version, each choice and the config file it's written to, and what's removed and what's kept. It replaces the "Uninstall …?" question. Enter runs the action, Esc closes, the buttons have Alt keys, and a line explains why SmartScreen or Defender may warn about Install.exe. The window is 640 high (at least 560).
- Install.exe: the BepInEx download shows a progress bar with its percentage, and Close turns into **Cancel** while it runs. The zip goes to the temp folder, so cancelling leaves the game folder as it was.
- Install.exe: a failure says what went wrong in plain words (download failed, game folder not writable, not a valid zip, anything else), with **Show details**, **Copy details** for a bug report, and **Retry**. The log and the details stay in English.
- install-steamdeck.sh: in a terminal, the BepInEx download shows curl's progress bar. A `mod-install.json` that's there but broken now says to download the zip again, instead of saying the mod's files are missing.

#### Crash report window

- The memory dump warning and the "nothing was sent" line stay in view above the buttons, in darker text, however small the window gets. The headline stays playful ("Oops! The kobold slipped!"), and the line under it says plainly that Drag'n Wash closed unexpectedly.
- "Copy report (text only)": Enter copies, Esc closes, Alt+O / Alt+C work, and the tab order follows what's on screen. If copying or opening the folder fails, the window now says so instead of doing nothing.
- When the window can't open, or the report can't be written, a message box says where the report or the session record is. Before, nothing happened.
- The window speaks the language the game is shown in, even without the localization mod. The core keeps `BepInEx/CrashReports/locale.txt` from the language a mod set, or from the game's own choice when it offers more than one. Windows' language is the fallback.

#### Startup timing in the log

- The core writes two `[startup]` blocks to the log once per launch, timed to the millisecond. The first comes when BepInEx has loaded the plugins: process start to the core, the core to the end of loading, each plugin's load time, and the longest gaps between log lines with the line that ended each one. The second comes 10 seconds after the first scene: how long that scene took to come, frames slower than 100 ms with the lines written in them, and the longest gaps. Then it stops listening, so it costs nothing afterwards. Because it costs a few tens of milliseconds, it only runs when developer tools are on at launch (Options → Mods → Drag'n Wash ModFramework → Developer tools). With them off, it lets go of the log in the core's Awake and none of it runs.
- The block after the first scene also breaks down the first frame after it: each plugin's Start (for a coroutine, up to its first yield), Update, LateUpdate and OnGUI, and each `sceneLoaded` handler a mod added, in milliseconds, then the timing's own share and the rest (the game's own and Unity's). These are timed by Harmony patches that go on once that scene has loaded and come off after the frame, and what putting them on and taking them off cost is in the block too.
- Connection watching only looks for `HttpClient` in System.Net.Http now, so when the game hasn't loaded that assembly, the core's start no longer spends about 100 ms and three HarmonyX "Could not find type" warnings on it. Once it's loaded, `HttpClient` is watched as before.

#### Public CSV reading, and a safe write for any library

- `CsvReader` (`DragNWash.ModFramework.Saves`) is public now, no longer internal to the flags catalog. `ReadRows` reads a CSV file the same shared way the catalog does, and `Escape` writes one field back, and now also quotes a value that starts with `#` so it isn't read back as a comment line. It comes from the localization mod.
- `SafeFile` (`DragNWash.ModFramework`), new: `Write(path, encoding, Action<StreamWriter>)` fills a temporary file next to the target and then moves it into place, so a crash or a sharing violation partway through never leaves the target truncated. It comes from the localization mod, where it protects a translator's saved work. The Flags and saves library writes a save it restores or edits through it, and the Inspector's Export as overrides writes `mod.json` and the override files with it.

### Tool window 1.5.0

#### Tool window: notices, questions in place, a remembered window, one row of tabs

- `ToolWindow.ShowNotice(string, NoticeKind, float)` (ToolWindow library), new: a notice in its own strip above the footer's hint line, with a colour bar for its kind (`NoticeKind.Info`, `Warning`, `Error`), on one line cut off with an ellipsis. The whole text shows while the pointer is on it, and a click dismisses it. A timed notice clears itself and the next one waits its turn; an Error goes first and stays until the tab changes. `ShowNotice(string)` is unchanged. The hint line no longer gives way to a notice.
- `ToolWindow.Busy(string, string)`, new: call it from a tab's draw while the tab works over several frames. It dims the body, shows what's running and how far along it is with a spinner, and keeps input away from the tab.
- `ToolWindow.AskConfirm`, `IsConfirming` and `Confirm`, new: a question on one row, right where the action was pressed, with Yes, Cancel and five seconds to answer (Esc cancels).
- `ToolWindow.Hint(string)` and `Hint(Rect, string)`, new: a hint on the footer line while the pointer is on something, and `GUIContent` tooltips show there too. `ToolWindow.Elide`, new: text cut with an ellipsis so it fits. `ToolWindowStyles.Hint`, `Tag` and `Danger`, new.
- The tabs are one row joined to the body, with More for the ones that don't fit, and the resize corner is drawn. The window opens where you left it, at the same size and on the same tab; Reset window in the footer puts it back. F1 with the developer tools off says so on screen for a few seconds.
- Inspector: History's Clear asks first. "?" lists the keyboard shortcuts (? or Esc closes them, even while a field has the keyboard). The debug view has a legend, and its tags start with the kind's letter. The view, the panes and the Objects folders are kept between sessions.
- Bridge: New token and Disconnect all ask first while a client is connected, and name it.
- Assets: Reload files reads one file a frame under the busy overlay. A file that fails no longer stops the rest, and it's shown in red.
- Flags and saves: `GameSaves.Restore`'s message says the save it replaced is kept as a snapshot.

#### Tool window: its font is made when you first open it

- The F1 window's font used to be picked and filled at startup. Now it's made the first time the window opens. That makes every start about 0.1 s faster, and up to about 1.5 s on a PC that was just switched on, because finding the font means reading the list of every installed font from disk. The "Window font: ..." line is written to the log when the font is made. With developer tools off, the window never opens, so that time is simply gone.
- In exchange, the first F1 shows the window one frame later. A character the window hasn't drawn before shows as "?" for one frame, the way the Console already does it.
- `ToolWindow.PrepareCharacters` now only notes the characters instead of drawing all of them into the font (the Localization mod passes it about 5800). In Unity 6 the window draws its text from an atlas of its own, and on Direct3D 12 the core already uploads that atlas once per frame. With `[Direct3D12] BatchFontAtlasUploads` off, everything happens at startup as before.
- `ToolWindow.Font` and `ToolWindow.CanDraw`, called from Awake or Update before the window was opened, make the font right then, so they answer as they did before.

#### Console: level toggles that show on and off

- The Error, Warning, Message, Info and Debug toggles are drawn on the tab strip's dark panel instead of Unity's grey button. A toggle that's on has a 3 px bar along the top in its level's colour (the colour its log lines have), a filled square in that colour and bright words. One that's off has an empty square and dimmer words, which brighten under the pointer. Before, on and off only differed in the colour of the word. The bar and the square are painted rather than drawn from the font.

#### Text-field underline and filter field for tool-window tabs

- `ToolWindow.Underline(Rect)` (ToolWindow library), new: the accent underline the built-in tabs draw under a text field.
- `ToolWindow.FilterField(Rect, string, string, ToolWindowStyles)`, new: a text field with that underline and a muted placeholder while it's empty. It returns the new text. The Inspector, Assets and Console tabs now use both instead of their own copies, so a mod's tab can look the same with one call.

### Inspector 1.5.0

#### Inspector: keys you can change

- The Inspector's shortcut keys can be changed now: the gizmo's move, rotate, scale and off (W, E, R, Q), pick (P), highlight (H), tree (T), free camera (C), bones (B), wireframe (N) and edit mesh (M). They stay as they were until you change one.
- In the F1 window, open the Inspector tab's "?" panel and click a key. It says "Press a key…", and the next key you press becomes the new one, with Ctrl, Shift or Alt if you hold them. Esc stops without changing anything, and Backspace leaves the action with no key ("none"). "Reset all keys" at the bottom of the panel puts all eleven back.
- The same keys are on the Mods screen too (Options → Mods, open Libraries, then Inspector → Settings → Keys), each with a **Change** button. Both places change the same setting.
- When another setting has the same key, in another mod or in the Inspector itself, the key gets a yellow bar in the panel and the line the Mods screen shows under it, like "C is also used by Screenshot key (Photo Mode). Both will answer it." One key for two things is allowed, and both answer it.
- The arrows, Home, End, Page Up and Page Down, Ctrl+Z, Ctrl+Up, Esc and ? can't be changed, and neither can the free camera's W A S D, Q E and Shift while you fly.
- The Edit and View menus, the Tree and Pick tooltips and the free camera's notice name the key you set, and leave it out when there's none.
- `ModFramework.SharedKeyNote(ConfigEntryBase)` (core), new: the line the Mods screen shows under a shortcut setting whose key another setting also has, for a mod that lets people change keys in a window of its own. It gives one English line; the Mods screen shows the same thing in parts a language pack can translate.

#### Inspector: easier to read and reach

- What happens after you press something now shows in the notice strip at the bottom of the window, where you can't miss it: Undo last and Ctrl+Z, Undo and Redo on History's rows, Show private's warning, a Go in Used by whose object is gone, a texture's Assets button without the Assets library, and Export as overrides. A failure is red and a warning yellow. Before, most of these went to the status line, which is hidden behind the breadcrumb while an object is selected in Scene, so a Revert that failed looked like nothing happened. An export that fails is red in the form too, and a folder that can't be written now says so instead of stopping the tab.
- Used by and the first Graph button of a session show "Looking where ... is used" or "Reading the game's code..." in the middle of the tab before they start, instead of freezing the window without a word.
- The members pane has its own "Filter members" field over the rows, with "14 of 62" beside it. It keeps what you typed while you select other objects of the same type, and on a Transform it looks through all the members, not only position, rotation and scale.
- A member name that doesn't fit ends in "..." instead of being cut off. With the pointer on a name, the hint line shows all of it with its type, like "maxAngularVelocity : float  (property).", and for a number it adds that you can drag up or down on the value to change it.
- Show private, Hold values, Enabled and Code wrap onto a second line in a narrow window instead of running off the edge. Freeze is now called Hold values, and its hint says what it does: it stops reading the values, and the game keeps changing them.
- Every member row ends in a small "..." button that opens the same menu as a right click (copy the value or the name, reset, go back one edit, show it in History). A gamepad or the Steam Deck only clicks with the left button, so this is the way to that menu there. The right click still works.
- An enum's button shows its value with a small arrow and opens a list of all its values, the current one marked, so you pick the one you want, and a long list opens at the value that's set. It used to step to the next value on each press, and going back meant going all the way round.
- Scene's search says what it found in a line over the results: "3 objects match "wheel"", "Nothing matches "whel".", or "The first 500 are shown; type more to narrow it." Before, no match just left the tree empty, and the cut at 500 was silent.
- Scene's search takes `t:Rigidbody` (or any component type) like Objects does, for the objects that have that component, with a name after it to narrow it down. It runs once you stop typing, since each one looks through every object of the type. A name no component type has says so.
- In a narrow window, typing in the search goes to the results page, so it no longer looks like nothing happened.
- The selected row in Scene's tree and in Objects' list has a 2 px accent line on its left, so it stands out by more than its text colour.
- A name too long for Scene's tree, in a narrow window say, ends in "..." instead of being cut off at the edge, and the whole name shows on the hint line while the pointer is on it.
- The same goes for the names, types and folder titles in Objects' list, which used to run into each other, and for method signatures in Code. Code no longer leaves an empty gap before the signatures where no method has a Graph button, and an enum's list of values is never narrower than its button. The grey hint in the search and filter fields stays on one line and ends in "..." in a narrow window, instead of wrapping and being cut in half.
- The View menu is split under headings: OVER THE GAME, DEBUG VIEW (PICK ONE), IT SHOWS, HOW IT DRAWS and IN THE PANE. The five settings that were indented under Rigidbodies, and looked like they belonged to it, are under HOW IT DRAWS now, since they're for the whole debug view. The debug view's three scopes, where only one can be on, have round marks, and the rest a tick when they're on (x and * where the window font has no such marks). The keys are the same. Where the window is too short for the whole menu, it scrolls with the wheel or a gamepad's stick.
- History, Rigidbodies, Scenes and levels, Layers, Clips, Used by and Code, the views that take the members' place, all have the same band on top: "< Members" on the left, then the view's name and a few words about it. "< Members" used to sit at the end of each view's buttons, somewhere else in each one, and on a second line when they wrapped.
- Going back has two words now. Undo and Redo go one edit back or forward, and Reset goes back to the value before the first edit. History's Revert button is called Undo, an undone edit says "(undone)" instead of "(put back)", and the row menu's "Back to previous" reads "Undo: back to ...". The results read "Undid drag: back to 0.05." and "Redid drag: 2 again.", and the log's `[inspector] Reverted ...` and `Reapplied ...` lines say `Undid ...` and `Redid ...` now.
- History's buttons wrap in a narrow window, so Export as overrides no longer gets cut off, and an edit's long lines end in "..." with the whole text on the hint line.
- With nothing to export, the Export button is greyed out like any other button that can't be used, with the reason next to it. Before, it looked selected, as if you could press it.
- Typing in Export's Name, Author and Description fields, or in the Clips and Rigidbodies filters, no longer sets off the Inspector's keys. Before, a letter that is a shortcut, ?, Home, End or an arrow did its shortcut instead of going into the field.

### Assets 1.5.0

#### Assets tab: a toolbar that fits, and replacements that say why they don't show

- `ToolWindow.FlowButton` (ToolWindow library), new: a button in a row of buttons that wraps onto the next row when the window is too narrow, sized to its label (or to a width you give, so a button whose label changes doesn't make the row jump). A selected one has the accent line under it, the way a view switch shows what's showing. It takes a string or a `GUIContent`, whose tooltip goes on the hint line.
- Assets tab: the top row switches between **Textures (1,234)** and **Replacements (4)** like tabs, with the one showing marked, and the name filter takes the rest of that row. Before, one button said the name of the list that wasn't showing, so you couldn't tell which one you were looking at. **List again**, **Apply replacements** and **Reload files** sit on the row under it. Both rows wrap in a narrow window, so the filter and Reload files no longer end up off the edge. The status gets a line of its own instead of sharing one with the replacement count, where it was cut off. The buttons explain themselves on the hint line.
- Assets tab, Replacements: the right column says how each file is doing. **In 12 places** is fine. **Not used yet** (yellow) means it hasn't gone in anywhere, and the hint line says whether a texture of that name is loaded at all, so a typo in the file name shows. **Not used: CleanSponges wins** (yellow) means another mod's file of the same name is used; the mod that lost now has a row of its own too, where before it was only named at the end of the winner's row. **Off** (dim) is a language picture its mod switched off, which used to look as if it applied. **Not reloaded: ...** (red) is a file that couldn't be read. The switch above the list says how many rows are yellow or red ("2 to check"). When a language's pictures wait for a restart on Direct3D 12, a band above the list says so; before, only the `assets replacements` command did. The filter works on this list too, by texture name or mod. A narrow window leaves the mod column out, and a row's hint shows what was cut off.
- Assets tab: the replacements list is only worked out again when the replacements change, and only the rows in sight are drawn.
- Assets tab, Textures: the tab lists the textures by itself the first time it opens (after a frame of "Listing textures..."), and again after Apply replacements or Reload files, instead of leaving "Nothing listed yet." behind. When a scene loads after the list was made, a yellow band says "The scene changed since this list was made." with a **List again** button. With a filter, the status line says how many of how many show.
- Assets tab, Textures: the mark column has room now (it was about 34 px wide at the default size, and less than nothing in a narrow window). A mod's replacement says **from SignPack**, and the game's texture it stands in for says **original, replaced** and is dimmed, so the two rows of the same name can be told apart. A narrow list leaves the material and sprite counts out, and the size too when the name would get too little room. Only the rows in sight are drawn, and the filter is only worked out again when it changes.
- Assets tab: Reload files and Apply replacements say what happened in plain words. "Nothing changed on disk." instead of "Reloaded 0 file(s).", and "Everything was already in place." or "Put replacements into 5 more places." instead of "applied in 0 place(s)". A PNG added after the game started isn't read by Reload files, and nothing used to say so; now a notice names the new files and says they're read when the game starts, so restart to use them. When a watched file reloads by itself (`[Reload] WatchFiles`, Direct3D 11), a notice says so ("shop_sign.png changed on disk and was reloaded.") and the list is made again; before, only the log said it.
- Assets tab: the two lines explaining the buttons are gone (the buttons explain themselves on the hint line), which gives the list more room. What stays in sight is a warning: on Direct3D 12 that Reload files can crash the game, and when Reload files is off, why and how to have it back (for example "Turn AllowReload back on in Options > Mods > Drag'n Wash ModFramework: Assets to try again."). On Direct3D 11 with reloading on, there's no line at all.
- Assets tab: both lists scroll with the gamepad stick and d-pad, as the other tabs' lists do, so they no longer need the Steam Deck's trackpad or the touch screen. The preview's Close button is 64 px wide and the rows' Inspect buttons 80 px, easier to hit.

#### Fonts: rasterized characters kept between starts

- Assets: what the fallback fonts rasterized (atlas pixels, glyph and character tables, free space on the last atlas) is kept in `BepInEx/cache/FontAtlases`, one file per face. A face created again from the same font file takes it all back at once and only rasterizes characters that are new. With the localization mod's 17 languages on Direct3D 12, its load goes from about 1230 ms to about 365 ms after the first start (on Direct3D 11, from 315 to 205 ms). A restored glyph is the same, pixel for pixel, as a freshly rasterized one.
- The cache is written about two seconds after the last change, one face a frame. Each file is written on a worker thread and then moved into place, so a start never reads half a file. A file for another font file or version, other atlas settings, another Unity or TextMeshPro or another library version, or a damaged one, is ignored and written again. Nothing gets uploaded while the game runs that wasn't uploaded before.
- `[Fonts] CacheAtlases` turns it off (it's on by default and takes effect at the next start).

### Flags and saves 1.5.0

#### Saves: restoring the oldest snapshot

- Restoring a snapshot when the history was full could fail with "Restore failed" and lose that snapshot. Before a restore, the save being replaced is kept as a snapshot, and with the history full that pushed out the oldest one, which could be the very snapshot being restored. Now the snapshot is read first, so the restore goes through. The current save was never at risk.

#### Saves: how many snapshots are kept, and a restored old snapshot counts as the save

- `GameSaves.Keep` (Flags and saves library), new: how many snapshots a slot keeps, the library's `[History] Keep` setting. A tab can show "30 of 30 kept" and say when the next snapshot will push the oldest one out. It's right even while `[History] Enabled` is off.
- `GameSaves.SnapshotMatchesSave` now says true for a snapshot from before the game update of 2026-09-14 once it has been restored. The restore adds the `{"version":1}` entry the game needs, so the save and the snapshot were never byte for byte the same, and a tab couldn't mark that snapshot as the current save.

#### Saves: no snapshot is lost to another one taken in the same second

- Snapshots are named by the second they're taken, and a second one in the same second was copied over the first. A mod's edit followed by the game saving within that second lost the save from before the edit. Now the second one is named with "-2" (then "-3", and so on) and both are kept.
- An edit that changes nothing (a level the save already has, flags already set that way) no longer takes a snapshot first. With the history full, that snapshot pushed the oldest one out for nothing.

### Bridge 1.5.0

#### Bridge tab: what's wrong, and the fix, in one place

- The top of the tab is a panel with a 3 px bar: accent while it listens ("Listening on 127.0.0.1:47821"), dim when it's off, red when it can't listen. The red one says why in words a player can follow ("Windows won't let the game use port 47821", "port 47821 is in use"), with the port in the title so a screenshot carries it. Turn on and Turn off sit in that panel. Before, a port Windows had taken showed "On, but the developer tools are off", which was never the reason (the F1 window doesn't open without them), and the real reason under the buttons was cut off by a fixed 44 px height. Turning the Bridge off now clears an old failure instead of leaving it under "Off", and the Inspector's Graph buttons and the console get the real reason too.
- When it can't listen, the red panel has **Use a free port** and **Try again**. Use a free port looks at the ports after the current one, skips every range in Windows' excluded port list (`netsh interface ipv4 show excludedportrange`, read only, no administrator needed) and every port it can't listen on for a moment on 127.0.0.1, then saves the first good one as `[Bridge] Port` and listens there. It looks on a worker thread, so the game doesn't stop (about 70 ms on a Windows 11 PC, most of it netsh). Only asking is involved: nothing about Windows' settings is changed, the Bridge still listens on 127.0.0.1 only and still needs the token. The notice says "Listening on port 47822 now. Clients set up for 47821 need the new setup (Copy setup).", and a yellow "Port changed from 47821" line stays under the address until something is copied or a client connects. Try again tries the same port, for when the PC has restarted and Windows let it go. Before, the only way was the Mods screen's port stepper, which moves by 3,226 at a time.
- CODE GRAPH: **Open page** and **Graphs** are now **Code graph** and **Graphs editor**, with "Opens in: App / Browser" beside them, the same setting as the Mods screen's "Open the code graph in". When App is picked but CodeGraph.exe isn't there, a dim line says it opens in the browser (it used to switch without a word), and off Windows it just says it opens in the browser. While the Bridge isn't listening both buttons are greyed out with "Opens once the Bridge is listening." instead of failing after the press.
- CLIENTS: Disconnect all moved up beside the heading, and each client has a small **Disconnect** of its own, with no question, since the same token lets it straight back in. A long client name is cut with "..." and shows whole, with its MCP version and when it connected, on the hint line; names a client gives go through the window's safe text path. A failed call in LAST CALLS has a red "failed" tag instead of a dim "(failed)".
- CONNECTION: the address, the token and the setup are a row each, each with its own Copy. The token is still never drawn ("kept in your user profile, never shown here"), and copying it alone gives away no more than Copy setup always did. Setup switches between Claude Code (the `claude mcp add` command, with a note to run `claude mcp remove dragnwash` first when it's already set up, since adding the same name twice fails), VS Code (a `.vscode/mcp.json` with a `servers` entry of type `http`) and Cursor (an `mcp.json` with an `mcpServers` entry), each in the form its own documentation gives, with the token filled in. Copying while the Bridge isn't listening says it won't answer until it is. New token moved to the token's row. The long setup paragraph, which a fixed 60 px height cut off, is gone, and the tab measures how tall it came out instead of guessing, so nothing at the bottom is out of reach in a narrow window.

#### Bridge: a clearer message when Windows holds the port

- When the Bridge can't listen because Windows refused the port ("access denied"), the F1 Bridge tab and the log now say the port has probably been set aside by Windows. Hyper-V, WSL and Docker reserve ranges of ports, and the ranges can change when the PC restarts. The message says to choose another `[Bridge] Port` and register the new address with the client. Before, it said another program might be using the port, which isn't what happens in that case. The system's own error text also no longer leaves a line break in the middle of the message.

#### Graphs editor and Bridge page

- Graphs editor: ✕ removes a block straight away and a toast says what went with it ("and the 2 blocks inside it"). Undo or Ctrl+Z puts it back, and that works for a variable or an on error part too.
- Graphs editor: Rename, Delete and "changes not saved" use the page's own dialogs instead of the browser's prompt and confirm. New graph now asks before throwing away unsaved changes.
- Graphs editor: Move to… moves a block into another list without dragging, and a block's head takes Alt+↑/↓, M, Delete and Enter.
- Bridge page: ? lists the keyboard shortcuts, the graphs editor's divider moves with the arrow keys, and icon-only buttons and every block field have screen-reader labels.
- Graphs editor: a refused drop says why, a failed check shows its reason along with Check again and the last result, and a LIVE in game pill shows while the colour picker writes to the game.

#### Code Graph app

- The waiting page says why it's waiting (the game or the Bridge isn't there, there's no token yet, or the token was refused), has a Retry now button, and shows how many tries have been made and when the next one is. A refused token waits for Retry instead of retrying every 2 seconds.
- When a start hands over to the open window and gets no answer, it opens a window of its own and says so once, instead of quitting without a word.
- Its own text comes in English, Japanese and Chinese and follows Windows' language, window title included.

### Graphs 1.5.0

#### Bridge page and Graphs page: the Tool window's look

- Bridge page: the view switches (Code and Graphs, Blocks and Nodes) look like the F1 window's tabs. The one showing sits on the page's ground with a 2 px accent line on top, and the others are plain dim words. The open graph in the list is marked the same way, with the line on its left. The status line is a band under the header with a 3 px bar on its left, in the accent colour, or in the error colour when something went wrong, and a long line wraps instead of making the page scroll sideways.
- Graphs page on the Mods screen: it sits on the screen's frosted panel like the other tabs, with no dark block of its own. Each graph is a card like a setting's row on the Settings tab (the same see-through fill, corners and padding), the headings, grey and red lines use the screen's colours, which stay readable over the brightest picture behind, and **Stop for this session** is one of the screen's buttons (raised, rounded, bold, lighter under the pointer or the gamepad). The note under the cards lines up with the text in them. With a core older than 1.5.0 the page looks as it did in Graphs 0.1.2.
- `ModsScreenLook` (core), new: the Mods screen's text colours (`Text`, `Muted`, `Accent`, `Error`, `Warning`), a `Card` like a setting's row and a `Button` like the screen's own, for a `ModsScreenPage` that should look like the rest of the screen. The colours follow the look in use (frosted glass or the tint alone), and an open page is built again when it changes.

### Text 1.5.0

- No change; follows the release.

### Dialogue 1.5.0

- No change; follows the release.

### Overrides 1.5.0

- No change; follows the release.

### Not in the release

#### Code graph without the game (standalone app)

- Experimental, and not in a release. `codegraph-standalone/` (CodeGraphStandalone.exe, Windows) shows the code graph of any .NET assembly without the game (docs/CODE_GRAPH_STANDALONE.md). It opens DLLs, a folder (leaving out .NET's and Unity's own unless you pass `--all`) or a Mono Unity game's folder, through Open…, the command line or a drop, and refuses IL2CPP games with the reason. It shows the Bridge's page in WebView2 and answers its calls inside the process (`WebResourceRequested`), with no port and no network.

## 2026-09-20: a way into the editor

The core and the preloader patcher go to 1.4.3, the Bridge to 0.1.2.

### Inspector 1.1.2

- `inspector.level.get` (read), and `inspector.scene.load` and `inspector.level.start` (writes, for the console and the page alone): what the game is playing, and opening the scene or level a graph is about without going back to the game to click through a menu. Neither write is offered to graphs or to an AI client.

### Bridge 0.1.2

- **Open page** and **Graphs** on the Bridge tab of the F1 window. The page could only be opened from a method in the Inspector's Code view, which is no help to somebody who wants to write a graph: **Graphs** opens the same page with the editor already in front (`bridge.page.open` takes `focus=v:graphs`), and **Open page** opens it at the code graph as before. Both views still switch inside the page.
- The row of buttons wraps instead of walking off the edge of a narrow window.
- Fixed in the editor: the number in a new block's id climbed for ever. A graph with one handler is `h1` again, whatever was made and thrown away before it - an id only has to be unique inside its file, so a number a removed block freed is used again.

### Graphs 0.1.2

- The editor is what somebody writing a graph needs, and not a corner of it: a graph's **variables** (a `set` statement could not be used at all without them), its **description**, a handler's **only if** and **again while running**, which **handler Run starts**, **Rename**, **Delete** (the file is kept as `.json.bak`), **Reload** for files that changed outside the editor, and **Clear** for the log panel. `graphs.rename`, `graphs.delete` and `graphs.reload` are page-only writes like the rest.
- The page lays itself out for the window it is in: below 1000 px the problems and the log go under the blocks, below 640 px everything is one column. A window of 700 px used to leave the blocks 30 px wide.
- Fixed in the editor: **New graph** kept the last graph's name, so Save wrote over it; a `key.pressed` handler showed `F6` and checked as having no key (the default was drawn and never written, and F6 is Drag'n Wash Localization's dump key - it is F8 now); a block's own buttons dropped onto a line of their own; after a save the bar still said *not saved yet* and Rename stayed off; a graph whose file was removed outside the editor stayed in the list and failed when opened.
- Fixed: **Run** starts a graph that was stopped. Stopping is for the session, but pressing Run is somebody asking by hand, so the graph comes back (its failure count with it) instead of refusing with *switched off for this session*; the page says when a run did that. `graphs.run` returns `started_again`.

### Core 1.4.3

- No change; follows the release.

## 2026-09-20: which mod has which key

The core and the preloader patcher go to 1.4.2, Graphs to 0.1.1. Additive, as before.

### Core 1.4.2

- `ModFramework.WhoElseUses(key, exceptGuid)`: which other mods have a setting on a keyboard key, as *Drag'n Wash Localization: [Debug] DumpDialogueKey*. Every BepInEx plugin keeps its shortcuts in its own settings and nobody asks anybody else, so two mods can sit on one key without either of them knowing; the framework can see all of them, so it can at least say so.

### Graphs 0.1.1

- A graph that answers `key.pressed` says who else answers that key - in the log, in the console's `graphs`, on the mod's **Graphs** page (*Key shared*) and in `graphs.list` for the page. It is said once per key and worked out again at every reload, and nothing is refused: a player may well want one key to do two things, and only they can say. (The framework's own F1 is still refused.)

## 2026-09-20: graphs, and who changed what

The core and the preloader patcher go to 1.4.1; Overrides to 0.1.1, the Bridge to 0.1.1, the Inspector to 1.1.1; and the **Graphs** library 0.1.0 arrives, experimental like the rest of the new ones. Everything is additive: mods built on 1.4.0 need no change.

Three of the things 1.4.0's notes said about write operations were not true when it shipped - a change another mod made was not listed in the Inspector's History, two mods changing one value were not named, and a take-back could put back a value somebody else had written since. They are true now, and were tried in the game with two graphs set on one value.

### Graphs 0.1.0

- New library, experimental: **mods with no code that do things** — *when this happens, do these things*. A folder in `BepInEx/plugins` with a `mod.json` and `graphs/*.json` (one mod on the Mods screen with its overrides, through the core's `DataMods`) answers the events the libraries raise and calls the operations they registered: nothing else, no methods by name, no reflection, no files, no network of its own. See docs/GRAPHS.md.
- Every file is checked completely before anything runs, against the operations and events registered at `ModFramework.Ready`: the shape and the limits (at most 2,000 statements, 32 deep, 256 KB), every call's operation and arguments, every variable and every value an event hands over. A file with one problem does not run at all, and its problems are listed in the console and on its mod's **Graphs** page. A call into a library that is not installed says *needs Inspector* instead of failing later.
- Running is data, not threads: all the graphs together get a millisecond a frame (`[Graphs] FrameBudgetMs`), shared between runs, and what is left over waits for the next frame. A run stops at 10,000 steps, a loop at its `max`, a wait at 600 seconds, and a graph has at most 8 runs at once. Three failures in a row switch a graph off for the session and mark its mod on the Mods screen; what it changed is put back.
- An event never starts a run where it is raised: events are queued and the runs begin in the library's own frame, so a graph's work never lands inside a dialogue line or a scene load. The library owns two events of its own, `timer.every` and `key.pressed` (the framework's F1 is refused).
- A **Graphs** page on each mod's details: what each graph answers, what it reads, what it changes, what it needs, how it is going, and **Stop for this session**. Console: `graphs`, `graphs reload` (the files are read again, changes put back, nothing carried over) and `graphs stop <file or name>`.
- Every call is made as `graph:<mod GUID>/<file>`, so a change can be traced back to the graph that made it; a call slower than 5 ms is logged with its graph, as a slow `GameEvents` handler is.
- For the editor, the Graphs library registers `graphs.catalog`, `graphs.list`, `graphs.read`, `graphs.check`, `graphs.log`, `graphs.save`, `graphs.run` and `graphs.stop`, all for the page and the console alone - an AI client over MCP never sees them, and neither does a graph.

### Overrides 0.1.1

- **The first writes a graph may call** (the Overrides library): `objects.member.set` (a component's field or property), `objects.material.set` (a material's property) and `objects.active.set` (an object shown or hidden). Each says how to put itself back, so a graph's changes are undone when it is switched off, reloaded or fails three times, and the registry raises `Operations.Written` with the value before and after. None of them outlives the session.
- **Who changed what, and nobody undoing anybody else.** The Overrides library keeps one ledger of the values it writes - an overrides row and a graph meet there whatever path each of them spelled - so: two mods changing the same member are named once in the log and on the mod's Graphs page (*Also changed: cars/car_3 (2) active: Clash Test graphs/second.json*); a take-back puts back only what it wrote itself, and leaves alone a value somebody else has written since, saying so in the log; and what a stop reports is what it really put back, not how many take-backs it tried. `objects.writes` lists it all for the Mods screen.

### Bridge 0.1.1

- **An editor on the Bridge's page**, beside the code graph: a **Graphs** tab that lists the graphs, shows one as blocks - a hat block per handler, the operations of the registry with their parameters as slots, writes in their own colour - checks it as you type (the same check the game makes, by statement id), saves it into a data mod's `graphs/` folder (making the mod when it does not exist, keeping the file it replaces as `.bak`), starts a handler without waiting for its event, stops a graph, and shows the library's log as it happens. A block is picked up by its head and dropped above or below another, or onto the *+ add* row at the end of a list, so a statement moves between lists as well as within one; a handler only lands among handlers, and nothing can be dropped inside itself. The **Nodes** view draws the same file as boxes - flow down the edges (next, then, else, do, on error), a result named with `as` as a dashed wire to what reads it, writes in their own colour - and a node opens its block, where the fields are.
- The page's door accepts a **write** that is offered to the page alone (`graphs.save`, `graphs.run`, `graphs.stop`), and nothing else: a write anyone else may call is refused there, and MCP never sees a write at all.
- Fixed: a view that is off screen is really off screen. The two views were told apart by the `hidden` attribute alone, which `display: grid` wins against, so the code graph stayed under the editor.

### Inspector 1.1.1

- The **Inspector's History** lists what other mods change through a write operation, with who asked (`graph:<mod>/<file>`, `console`, `page`). One such change can be put back from its row without stopping the graph, and **Undo last** and Ctrl+Z pass them over: they are for this session's own edits. They are not exported as overrides either.

### Core 1.4.1

- For a write operation: `OperationArgs.Caller` says who asked, and `OperationArgs.TakeBack` takes a `Func<bool>` that says whether it put the value back, so a caller counting changes counts changes and not tries. The `Action` form still works.

## 2026-09-20: mods with no code, the operations registry and the Bridge

Released together with Drag'n Wash Localization v1.4.0. The core and the preloader patcher go to 1.4.0; the Tool window and Dialogue to 1.2.0; Text, Flags and saves and the Inspector to 1.1.0; Assets to 1.2.0. Two libraries arrive, both experimental: **Overrides** 0.1.0 (mods with no code) and **Bridge** 0.1.0 (read operations for AI clients on this computer). Everything new is additive: mods built on 1.3 need no change.

### Core 1.4.0

- Experimental. `Operation.Audience`: who may call an operation — the console, the Bridge's page on this computer, an AI client over MCP, a graph — and each door asks with its own flag (`Operations.CallNow(..., OperationAudience.Mcp)`), so the registry, not each door, decides. What shows the game's own code is the page and the console only. `Operation.Lasting` marks a write that outlives the session (a file, a save), said in stronger words on the Mods screen and never offered to graphs. `ModFramework.NameOf(guid)`: the name the Mods screen shows for a mod, from its GUID or Harmony ID.
- Experimental. A write operation can say how to put itself back (`OperationArgs.TakeBack(label, undo, before, after)`): the caller gets it in `OperationResult.TakenBackBy` and the registry raises `Operations.Written`, so what makes a change can undo it and the Inspector's History can list it beside the changes made by hand. A caller that keeps the result in memory can skip the JSON size check (`measureResult: false`), which costs more than most calls.
- Experimental. An **event registry** beside the operations: `Operations.RegisterEvent` and `Operations.Raise`, with `Operations.AllEvents`, `FindEvent` and `Happened` for what answers events by name. The library that hooks an event owns it — the core registers `game.started`, `scene.loaded` and `scene.unloaded`, Dialogue `dialogue.node.started`, `dialogue.line.showing` and `dialogue.option.showing`, Flags and saves `saves.written` — and a mod can add its own. `op events` in the console lists them (docs/GRAPHS.md).
- Experimental. **One loader for mods with no code**, in the core: `DataMods` finds every folder in `BepInEx/plugins` with a `mod.json` and no DLL, reads the manifest, lists it on the Mods screen (the ones switched off included, so one can be switched on again) and hands it to whichever library reads its kind of content — `DataMods.With("overrides")`, `DataMods.With("graphs")`. The Overrides library now reads its files through it instead of walking the folder itself, unchanged for players. `Json` (the small reader the files need) moves into the core and is public.
- Experimental. **Operations** (docs/API_PLAN.md, stage 1): a registry of what each library can do, by name (`library.noun.verb`), with a description, plain parameters (text, number, true/false, with the accepted choices) and a kind (read or write). `Operations.Register`, `Find`, `All`; `CallNow` on the main thread and `Call` from any thread (queued to the next frame); arguments are checked and converted against the parameters; results are plain values (lists, dictionaries, text, numbers) with `Operations.ToJson`, capped at 200,000 characters; every call is logged with who made it (write calls at Info); an owner's operations go when it is reloaded. The core registers `mods.list`, `mods.network`, `game.info` and `scene.list`.
- Experimental. `ModFramework.RegisterDataMod(info, version, manifestPath)`: a mod with no DLL (a folder another library reads) is listed on the Mods screen like a plugin, and switching it off renames its `mod.json` to `mod.json.disabled` at the next launch (the preloader patcher renames it, as it does DLLs).

### Tool window 1.2.0

- Experimental. Console `op`: lists the operations, `op help <name>` describes one, `op <name> key=value ...` runs it and prints the result as JSON, with completion of names and parameters. The Tool window registers `log.read` (the last console lines, by source and level).
- The footer grows to fit a notice that wraps in a narrow window (up to three lines) instead of cutting off its second line.
- Steam Deck and gamepads: a trackpad click (or A, R2) over the window is now a real left mouse button, held while the button is, the sticks send real wheel steps and the d-pad real arrow keys (XTest on Linux, SendInput on Windows). Every control works with them, not buttons only: text fields, tree rows, value drags, sliders, scroll bars, moving and resizing the window. The d-pad walks a list a row at a time the way the arrow keys do - up and down a row, left and right closing and opening what has children, repeating while it is held - so the Inspector's lists and the console's history need no keyboard; the sticks are left to scroll. Where the system takes no such input, presses still click buttons and the sticks and the d-pad scroll, as before.

### Assets 1.2.0

- Experimental. **Inspect** on a texture in the Assets tab opens the texture in the Inspector's Objects view, whose Used by lists its materials and sprites (before, it opened the first material using it, and showed only when one did).
- Experimental. Read operations: `assets.textures.list`, `assets.materials.list`, `assets.meshes.list` (with a name filter), `assets.replacements.list` (which game texture, from which mod, for which language) and `assets.fonts.language`.
- Experimental. Texture replacements per language: `AssetReplacements.AddLanguageFolder(guid, root, subfolder)` takes `<root>/<language>/<subfolder>/*.png`, which apply only while `GameFonts.Language` is that language and win over a plain replacement of the same texture (both are named in the log). Only the language in use is loaded. A language change takes the previous pictures back and loads the new ones; on Direct3D 12 the new ones wait for a restart (`AssetReplacements.PendingLanguage`). `SetLanguageFoldersEnabled(guid, on)` switches a mod's pictures off and on; `AssetReplacements.Changed` is raised afterwards. A texture with no picture in the language falls back to the languages its `fallback.txt` names (one per line, in order), then to a plain replacement, then to the game's own; a picture that cannot be loaded falls back the same way. `TextureReplacement.Language` names the language a picture came from, and the Assets tab shows it. For Drag'n Wash Localization's translated pictures.
- Replacements can be taken back: the library remembers what each material property and each sprite user held before.

### Dialogue 1.2.0

- `DialogueLine.SpeakerGuess` and `SpeakerFrom`: the game's script mostly names no speaker in a line, so `Speaker` was empty for nearly every line; the guess takes the script's name when there is one, else the node's (the part before the first `_`: `Ryan_1_intro` is Ryan), and Kobold (the player) for options. `dialogue.recent` and `dialogue.current` report it with where it came from.
- Experimental. Read operations: `dialogue.current` (whether a conversation runs, its node, whether options are on screen, the last line, and whether this game build lets the library see lines and options) and `dialogue.recent` (the last 100 lines and options shown this session).

### Text 1.1.0

- Experimental. Read operations: `text.rewriters` (the mods that rewrite text, in order) and `text.shown` (text on screen, as the game set it and as shown).

### Flags and saves 1.1.0

- Experimental. Read operations: `saves.list` (slots with their level and history copies), `saves.flags.list` and `saves.flags.get` (with what the flag catalog says).

### Inspector 1.1.0

- Experimental. The code graph's model (`CodeGraphModel.cs`: the index, `code.graph`, `code.type`, `code.callers`, `code.search`) is apart from Unity, Harmony and BepInEx, so the standalone app compiles the same file; the Inspector adds the patches and UnityEvent listeners through hooks. Its answers are unchanged (compared over every method and type of the game's assemblies).

- Experimental. **Code graph** (docs/CODE_GRAPH.md): the game's code read with Mono.Cecil from its own assemblies and those of its authors (not Unity's, .NET's, the mods', nor bundled libraries): `code.graph` (a method as basic blocks and branches, each block in words — calls, fields, text, what it decides — with its IL; the calls and fields; its callers, those through a base method marked; the Harmony patches on it with the mod's name; the UnityEvent listeners in the loaded scenes and Unity messages that lead to it; a coroutine or `async` method drawn through its state machine, with the state dispatch and each yield or await marked), `code.type`, `code.callers`, `code.search` and `code.stats`. Page only: AI clients never get them. The index is built on first use (about 80 ms for 5,900 methods). In the Code view, **Graph** on each method and **Type graph** open it in the browser through the Bridge.
- Experimental. Read operations: `inspector.objects.find`, `inspector.objects.children`, `inspector.components.list`, `inspector.member.get` (a component's members as the rows show them, private ones on request) and `inspector.selection.get`.
- Experimental. **Export as overrides** in the History view (with the Overrides library installed): the edits become a mod with no code in BepInEx/plugins/<name> (mod.json and overrides/main.json; a second export into the same mod adds a file), one row per place with the value it holds now. Each History entry now keeps where the edit was made (scene, path, component and its index, member, private or not; for a material, the renderer showing it and the property). Edits an override cannot hold are left out with the reason: list elements, mesh vertices, Animator parameters and clip swaps, GameObject rows.
- Experimental. **Object explorer** (docs/OBJECT_EXPLORER.md): a **Scene | Objects** switch at the start of the toolbar. Objects lists every loaded object by kind (Textures, Sprites, Materials, Shaders, Meshes, Audio, Animation, Fonts, Data with a sub-folder per ScriptableObject type and the game's own first, Objects outside the scenes, Other), with counts, a short fact per row, a search by name and `t:Type`, **Show hidden** and **Close all**; only the rows on screen are drawn, and the list keeps instance IDs, not references, so it keeps no asset loaded. It is made in one pass when looked at, on Refresh and after a scene load, and says how long that took. The selected object's members are rows as in Scene, less the arrays Unity copies on every read, under a header per kind (a texture's size, format and readability with a preview; a sprite's part of its texture; a shader's properties, keywords and materials; a mesh's counts and bounds; a clip's length and events; a sound's length and format). Editing, Reset and History work as in Scene; the first edit of a shared object says so once; GameObjects outside the scenes and their components are read-only.
- Experimental. The **keys move through the left pane's list**, in the Objects view and in the tree and the search results of Scene: up and down one row, Page up and Page down a pane, Home and End the ends, left and right closing and opening a folder or a node with children (left on a closed one goes up to the one it is in). Moving onto an object selects it, and the list scrolls only as far as it must to keep the row in view.
- Experimental. **Used by** on the object explorer's header: where the object is used (renderers, mesh filters and colliders, sprites, audio sources, animators and controllers, materials, and every field of scripts and ScriptableObjects that can hold it), each with **Go**; it stops at 2,000 places and says how many objects it looked through and how long it took.
- Experimental. **Go** on every object row, not only objects, components, materials and textures: a scene's object in Scene, anything else in Objects; a texture row also has **Assets**. `Inspector.Inspect` accepts any object and opens the right view.
- Experimental. Console: `objects`, `objects <kind> [filter]`, `objects usedby <kind> <name>` and `inspect object <kind> <name>`. Read operations `inspector.loaded.kinds`, `inspector.loaded.list` and `inspector.loaded.usedby`; `inspector.selection.get` reports a selection in Objects.
- The wireframe is drawn whole for detailed meshes (parts of a dragon were missing): each renderer gets a mesh of its edges, built once (each edge once) and drawn with the camera's matrices. Before, every line went through GL immediate mode each frame, which dropped vertices past about 65,000 and, sent whole, uploaded megabytes a frame and crashed Direct3D 12 (UUM-140564); now only a skinned mesh's positions go up each frame.
- A deep tree (a rig's bones) no longer pushes names out of the tree pane: its levels get narrower, and when even that is not enough, the levels above every row in view are left out.
- Scenes and levels: **Start** and **Load** of another scene begin trial play, in which the game's saves are not written until the title screen; before, finishing a level started from the list saved its number + 1, which moved a further save's progress back. The pane's lines wrap instead of running out of a narrow window, and search results are searched again when a scene change destroyed some of them (they showed as upper-case headings).
- A search result whose path is too wide for the tree pane is cut from the front (.../sunny/main_Directional Light), so the object's own name stays in view.
- The debug view's names keep clear of the selection's name and of each other (moved above, or below the outline when there is no room), instead of being drawn over them.
- Experimental. Rigidbodies and Rigidbody2Ds, read by reflection (no physics module is referenced): a debug view with each body's centre of mass and a velocity arrow, tagged with its speed, mass, kind and sleep (View → Rigidbodies: centre of mass and velocity); a **Rigidbodies list**, scene-wide or under the selection, fastest first, with a filter and Awake only; buttons on a selected body: **Stop**, **Kinematic** (kept in History), **Sleep** / **Wake**; and **Pause physics** with **Step** (simulation mode set to Script, put back on Resume, when the window closes or when developer tools go off). Console: `bodies`, `bodies pause|resume|step [count]`. See [Inspector (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector), Rigidbodies.
- Experimental. Animators, read by reflection (no animation module is referenced): a selected Animator's parameters (Float, Int, Bool, Trigger) and layer weights are rows among its members, edited and kept in History like any member; the base layer's line, and **Layers** in place of the members, show each layer's weight, the clips it plays, how far through and the blend to the next state; **Pause animation** (speed 0, put back on Resume, when the window closes or when developer tools go off) with **Step** (1/30 s) and, in Layers, a time slider per layer. See [Inspector (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector), Animators.
- Experimental. Animator clips: **Clips** lists the controller's clips (length, frame rate, loop, events, what each is swapped for); **Preview** plays one in a graph of its own with a time slider, pause and stop; **Replace** swaps a clip for any clip loaded (an AnimatorOverrideController on the game's controller), kept in History.
- Experimental. **Scenes and levels** (View menu), read from the game's own types by reflection: the level running (dragon, weather, state, clean), the game's own Skip level and Clean cheats, every level with **Start** (plays it in place of the current one; saved only when finished), the scenes loaded, and every scene with **Load** through the game's loading screen.

### Overrides 0.1.0

- New library, experimental: mods with no code. A folder in BepInEx/plugins with `mod.json` (name, authors, description, version) and `overrides/*.json` changes values in the game: a component's field or property (`"private": true` for the private fields where the game's scripts keep their settings) or a material's property, found by scene, path, component and member. Applied a frame after each scene load and a second later, and to root objects as they appear (each level's dragon comes long after the scene loads); the game's values are kept, and `GameOverrides.Reload()` puts them back and reads the files again. Values are written so they read back exactly and read leniently (Euler angles, `#RRGGBB`). When two mods change the same thing, the one that loads later (folder name order) wins and the log names both. Each mod is listed on the Mods screen and can be switched off there. With the Tool window installed, the console's `overrides` lists the mods and how many of their values are written, and `overrides reload` reads the files again. See [Overrides (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides).

### Bridge 0.1.0

- New library, experimental: the registry's **read** operations for AI clients on this computer over MCP (Streamable HTTP at `http://127.0.0.1:47821/mcp`). Off by default (`[Bridge] Enabled`) and only while the developer tools are on. Checks the Host (DNS rebinding) and Origin (web pages) of every request and a token (`Authorization: Bearer`, kept in the user's profile, renewable); at most 8 connections, 4 sessions (30 minutes idle), 20 calls a second, 1 MB requests, 10 seconds a call. `initialize`, `ping`, `tools/list` (each read operation as a tool, `inspector.member.get` as `inspector_member_get`, with a JSON Schema and `readOnlyHint`) and `tools/call` (text and `structuredContent`). Declares itself in `ModInfo.Network`; a **Bridge** tab in the F1 window (status, clients, last calls, Copy setup, New token, Disconnect all) and a console command `bridge`. See docs/BRIDGE.md.
- The **page** on this computer (docs/CODE_GRAPH.md), with a door of its own apart from MCP: `bridge.page.open` (a write operation, so never offered over MCP; the Inspector's **Graph** buttons call it) makes a one-time code (one use, 60 seconds) and opens `http://127.0.0.1:<port>/page#code=…` in the browser; the page trades the code for a cookie (`HttpOnly`, `SameSite=Strict`, `Path=/page`), and every call after that needs the cookie and an `Origin` equal to the Bridge's own address. The page (one HTML file inside the DLL, nothing from the internet; strict Content-Security-Policy, no framing) calls read operations, the page-only ones included, through `/page/api/op`. New token, Disconnect all and turning the Bridge off sign the page out too.
- The **Code Graph app** on Windows (CodeGraph.exe, in the Bridge's folder with the WebView2 parts it needs): the page in a window of its own, opened by the Graph buttons (`[Bridge] OpenPageIn`, App or Browser). It signs in with the Bridge's token through `POST /page/api/code` (only with the token and without an `Origin`, so no web page can), waits while the game is closed and signs in again at the same method when it comes back, keeps one window (a second start hands its method over), Keep on top, and remembers its size and place. The game's start of it has the shell start it again, so Steam does not count the window as the game still running. Built deterministically.
- The page's door accepts a **write** that is offered to the page alone (the graphs editor's `graphs.save`, `graphs.run` and `graphs.stop`), and nothing else: a write anyone else may call is refused there, and MCP never sees a write at all.
- The Bridge's listening socket is not inherited by programs the game starts (on Windows it kept the port after the game exited).
- The page takes its header, status line and hint from `window.dnwHost` when a host sets it, and then shows an **Open…** button (for the standalone app below); without it the page is as before.
- While it listens, the game keeps running when its window is not in front (`Application.runInBackground`; the game's own setting comes back when the Bridge stops): the game stops otherwise, and a client, the browser above all, takes the front.

## 2026-09-19: crash reports, sliders, Direct3D 12

Released together with Drag'n Wash Localization v1.3.0. The core and the preloader patcher go to 1.3.0, the Tool window and Assets libraries to 1.1.1; the others stay as they are. Everything new is additive: mods built on 1.2 need no change.

### Core 1.3.0

- Experimental. **Crash reports** ([Crash reports (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Crash-reports)): the core keeps a short record of each session in `BepInEx/CrashReports/session.log` (the start with versions, graphics and mods, scene changes, Unity errors and device messages, mod reloads, a heartbeat), flushed line by line; when the next start finds the session did not end cleanly, it writes a report folder with the last notes and the native stack from Unity's crash folder. `CrashReports.Note` lets mods add their own notes. `[Diagnostics] CrashReports`, on. Nothing is sent anywhere.
- Experimental. Memory dumps: a crash report keeps a copy of Unity's `crash.dmp`, and a watchdog writes a minidump when the game freezes (no frame for 15 s while in front; Unity writes nothing then), `[Diagnostics] HangDumps`, on. Dumps are for private sharing, not public issues.
- Experimental. `[Diagnostics] TraceGpuUploads` (off, advanced) adds a note for every GPU upload and released GPU resource, with the mod on the calling stack, to find what triggers the Direct3D 12 crash (Unity UUM-140564).
- Experimental. **Crash report window**: on Windows the core starts `CrashReporter.exe`, which waits for the game to close and, when it crashed or froze, writes the report and shows it at once in a window of its own (what happened, what to do, details, open the folder, copy the report), in English, Japanese or Chinese. `[Diagnostics] CrashReporterWindow`, on. Not under Wine/Proton.
- The freeze watchdog asks Windows which window is in front rather than Unity, so a game that freezes the moment it comes back (exclusive fullscreen on Direct3D 12, a problem of the game and Unity that the framework leaves alone) is caught too.
- Experimental. On Direct3D 12 the core uploads each changed font atlas once per frame (`[Direct3D12] BatchFontAtlasUploads`, on), which the crash reports showed to be the Tool window's crash; `GameInfo.FontAtlasUploadsBatched` says when it does.
- Experimental. `GameOptions.AddSlider` ([#39](https://github.com/TomXV/dragnwash-modframework/issues/39)): a slider row in the game's own Options screen, built by the game with its own slider (the range at its ends, the value shown while dragging). `OptionsSlider` has Id, Label, Min, Max, Step (values snap to Min + n x Step; 0 is continuous), Section, DefaultValue, GetSaved, Save and Preview, and follows the game's flow like `OptionsChoice`: moving it previews the value and shows Save, Save keeps it, Back returns to the saved value, Set Default. `AddSlider(id, label, step, min, max, getSaved, save, ...)` for the short form.

### Tool window 1.1.1

- On Direct3D 12 the Console (and `ToolWindow.Drawable`) draws translated text instead of `?` while the core batches font atlas uploads; with an older core, or with batching off, nothing changes.

### Assets 1.1.1

- `GameFonts.SetLanguage` notes the language in the crash reports' session record, so the crash report window speaks the language chosen in the game.

## 2026-09-19: saves after the game update

Released together with Drag'n Wash Localization v1.2.1. The flags and saves library goes to 1.0.1; the core and the preloader patcher go to 1.2.1 only because the release carries their version; the other libraries stay as they are.

### Core 1.2.1

- No change in the core; its version is the release's.

### Flags and saves 1.0.1

- Saves made after the game update of 2026-09-14 are found again. The game now writes `<steamid>/slot<N>/savegame.dgn` (inside the folder Steam Cloud syncs) and reads the older `<steamid>_slot<N>/savegame.dgn` only for a slot with no new save, so once a slot was saved in game (or deleted and started again) it disappeared from the Saves tab, which showed "No save files found" ([#40](https://github.com/TomXV/dragnwash-modframework/issues/40)). `GameSaves.Slots()` lists both layouts under the same slot names as before, so snapshot history carries on, and `SavePath` returns the file the game reads. A snapshot taken before the update gets the `{"version":1}` entry the game now expects when it is restored into the newer layout.
- `GameSaves.Slots()` lists slots in slot order (1, 2, 3) instead of most recently written first, so the Saves tab shows them left to right in order.

## 2026-09-17: developer tools, going online, Inspector

Released together with Drag'n Wash Localization v1.2.0, first as a pre-release. The core and the preloader patcher go to 1.2.0; the Tool window, Assets and Dialogue libraries to 1.1.0; Text and Flags and saves stay at 1.0.0; the Inspector library arrives at 1.0.0.

### Core 1.2.0

- Experimental. `DeveloperTools`: one switch for everything meant for mod makers and translators, `[Developer] Tools`, **off by default** (Options → Mods → Drag'n Wash ModFramework → Developer tools). Off, the Tool window does not open (F1 or `ToolWindow.Open`) and texture reloading is refused. Mods keep their own developer features behind `DeveloperTools.Enabled`, `Changed` and `WhenEnabled`; see GUIDE rule 8.
- Experimental. The Mods screen's settings pages edit text values: strings, keyboard shortcuts, colours and anything else BepInEx writes to the config file as text get a text field (Enter or leaving the field applies it; a value the config parser refuses is put back with the reason). A keyboard shortcut also has **Capture key**, which takes the next key pressed with the modifiers held. Before, these values could only be changed in the config file.
- Experimental. `GameEvents`: the game's events received once and handed to each mod on its own. `OnSceneLoaded`, `OnSceneUnloaded`, `OnGameStarted` (the title screen's first appearance; late handlers run at once), `OnQuitting`, each registered with the mod's GUID, and `Remove(guid)`. A handler that throws is logged and shown on the Mods screen under its mod, the other mods' handlers still run, and a handler that fails three times in a row is switched off for the session; a handler slower than 100 ms is noted in the debug log. The Assets library's texture replacements use it. GUIDE rule 9.
- Experimental. `SettingMeta` and `SectionMeta`, put in a `ConfigDescription`'s tags: a display name, an order, **Advanced** (hidden until "Show advanced settings" at the top of the page is turned on) and **RequiresRestart** (the page says so under the description); a section's display name, description and order. Tags with the same member names, including ConfigurationManager's `IsAdvanced`, `DispName` and `Order`, are read the same way, so a mod need not reference the framework.
- Experimental. `ModReload`: a mod's DLL reloaded while the game runs, for people who build mods. Only a mod that says so (`ModInfo.Reloadable = true` or `[ReloadableMod]`) and only while developer tools are on; libraries never. The new build is loaded next to the old one and checked first; then everything the old build registered with the framework and the libraries is taken out by its GUID and its assembly (`ModReload.Unloading`, `Prune`, `PruneEvent` for libraries), its Harmony patches are removed (its Harmony ID must be its GUID), its plugin is destroyed and the new one added where BepInEx started it. A build delivered as `<Mod>.dll.new` next to the DLL (the running DLL is locked on Windows), or a changed DLL where overwriting works, is reloaded by itself half a second later (`[Developer] WatchMods`), or by hand with `mods reload <guid>` in the Console; the preloader patcher makes the `.new` the real DLL at the next launch; `mods watch on|off`. A marker file catches a crash during a reload at the next start and switches watching off. Adding an Options row with an existing id now takes over its callbacks instead of being ignored, so a reloaded mod's row keeps working. See [Mod reload (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Mod-reload) and GUIDE rule 10.
- Experimental. Mods that go online say so, and players can see it (GUIDE rule 11, [Going online (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Going-online)). `ModInfo.Network` lists each host a mod connects to as a `NetworkUse` (host, what for, what is sent, how to turn it off); the framework declares its own update check, and one for every mod that sets `UpdateRepository`. The Mods screen shows an **Online** tag, a *Uses the internet* line and an **Internet** page. `NetworkWatch` (`[Network] Watch connections`, on) notes which mod actually connected where through `UnityWebRequest`, `WebRequest`, `HttpClient`, sockets and `TcpClient`, credited to the first plugin on the calling stack, and marks a connection nobody declared on the Mods screen and in the log. It only watches: nothing is blocked, and it is not a security boundary.
- `GameHooks.Require(ownerGuid, feature, "Type:Method")` takes Harmony's one-string form as well as a type and a method name, so the name the Inspector's Code view copies goes straight into the check.
- A new **logo** and Mods screen **icon** (a sponge and a gear) by NotaGames (@NotaGames), after the game's own logo, with the developers' OK; they replace the hand-made "Dg" monogram.
- The Options screen's **Mods button** is drawn now: a green plaque with a gear, in the game's menu style, normal and selected, by Mister ERIO (@mistererio), in place of the text label on a blank Back button. The text label is still used if the artwork cannot be loaded or the game's button changes.
- `GameHooks.Unavailable(ownerGuid, feature, reason)` marks a feature unavailable for a reason other than a missing game member (switched off after a crash, refused on this renderer), shown on the Mods screen like a failed check.

### Tool window 1.1.0

- Experimental. `ToolWindow.AddOverlay` (drawing over the game while the window is open), `BlockGameInput` and `Drawable` for tabs that work in the game view, as the Inspector does.
- `mods network` in the Console: what each mod declares online and what it was seen connecting to.
- Experimental. A **Console** tab: every BepInEx log line with its level and source, in colour, the last 2,000 kept; which levels and sources are shown is the player's choice, saved in the config and changeable from the tab, from commands (`log show`, `log level`, `log filter`) and from the Mods screen. Mods register commands with `ToolWindow.AddCommand`; built in: `help`, `log`, `mods`, `scene`, `clear` / `cls`. `ToolWindow.ErrorColor` and `WarningColor`. See [Console (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Console).

- `clear` / `cls` console commands.

### Inspector 1.0.0

- Experimental. Its own library on top of the Tool window, so a mod's release can ship the Tool window without it. An **Inspector** tab: every loaded scene's objects as a tree with a name search, the selected object's components and its renderers' materials, and each one's fields and properties (private ones behind **Show private**), read as the game runs (**Freeze** stops that) and editable for booleans, numbers, strings, enums, vectors, colours, rects and lists of those; a material shows its shader's properties and keywords. Object references have a **Go** button. A vector, colour, rect or bounds has one field per component; a colour also has a swatch that opens a picker (RGBA and HSV bars, hex); **Reset** puts a row back to what it held before its first edit. A **Code** view per component: its type and assembly, its methods with a copyable `Type:Method` for `GameHooks.Require` and Harmony, **Patch** for the whole patch at once (the `GameHooks.Require` check, the `[HarmonyPatch]` attribute - carrying the parameter types, and how each is passed, where the name alone is ambiguous - and a Prefix and a Postfix with `__instance`, the game's own parameter names, `ref` where the game passes by reference, and `__result`), the Harmony patches mods put on them and by whom, its UnityEvents' listeners (serialized and added at run time), and a method's IL read with Mono.Cecil ("Copy for dnSpy" for the rest). A **debug view** (View menu) outlines everything of a kind at once: every renderer the camera sees, the selection's children, or what the search text matches by name or component type, plus colliders and triggers and lights, of the whole scene or of the selection alone, drawn in their own shape (a box as a box, a sphere as a sphere, a capsule as a capsule, a mesh collider as its wireframe, or, when only the physics engine knows its shape, as a shape scanned with rays from six sides, labelled as an estimate that may differ from the real one; a spot light as its cone) and as screen rectangles or 3D boxes, coloured by kind, with names near the pointer. **Bones** (B) draws the skinned meshes' armature in the game view, a click on a joint selects the bone for the gizmo; **Wire** (N) draws the selection's meshes as wireframes (a bounds box for a mesh the game keeps unreadable); **Edit mesh** (M, experimental even within the Inspector and labelled so) moves a readable mesh's vertices by dragging, into a copy of the mesh, one History entry per drag, **Reset mesh** puts the original back. A **free camera** (C): a copy of the game's camera flown with the right mouse button held (mouse look, W A S D, Q E, Shift, the wheel for speed) while the game's own camera is disabled, put back when it is turned off. **Pick** selects the object clicked in the game (uGUI first, then the renderer whose screen bounds are smallest around the pointer), **Highlight** outlines the selected object in the game with its name, and while picking the mouse wheel walks through overlapping objects. **Move** / **Rotate** / **Scale** draw a gizmo on the selected object in the game view (its local axes with a handle each, a readout of position, rotation and scale) that is dragged directly; **Reset transform** puts the object back. Every edit (rows and gizmo) is kept in a **History** view with the value before and after, Revert / Redo per entry and Undo last; a right click on a row offers its original and previous values, Copy value and Copy name. The colour picker has a saturation/value square with a hue bar. The hierarchy scrolls to the selection, and **Parent** selects the parent. Nothing is saved; an edit lasts until the scene reloads or the game quits. `Inspector.Inspect(target)` opens it on an object from a mod's own tab; the `inspect` command selects and sets from the console. See [Inspector (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Inspector).

### Assets 1.1.0

- Experimental. Texture replacements: a PNG at `BepInEx/plugins/<Mod>/assets/textures/<texture name>.png` takes the place of the game texture of that name in every material and sprite, read at startup and applied at each scene load; two mods replacing the same texture are both named, never overridden silently. `AssetCatalog` lists loaded textures, materials, meshes and shaders, and the Tool window gets an **Assets** tab. See [Assets (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Assets).
- Experimental. **Reload files** in the Assets tab re-reads changed replacement PNGs while the game runs, and `[Reload] WatchFiles` does it by itself when a file changes (never on Direct3D 12). A marker file catches a crash during a reload at the next start: the Mods screen says so and reloading is switched off until turned back on. Unreadable files keep their previous texture and are listed with the reason.
- Experimental. Clicking a texture's name in the Assets tab shows a **preview**: the texture as it is on the GPU, scaled to fit, over a checked ground, with its size, format and users. Beside the list, or above it in a narrow window.
- Experimental. The `assets` console command: `assets textures [filter]`, `assets replacements`, `assets apply`, `assets reload`.
### Dialogue 1.1.0

- Experimental. `LineKey` and `LineResolver`: keys for a line of dialogue that carry no text and survive a game update editing the line (line ID, exact hash, normalized hash, fingerprint), tried strongest first; matches by anything but the exact text are flagged for review. Same definitions in `tools/linekeys.py`, checked against `ci/linekey-vectors.json` in CI. See [Dialogue (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Dialogue).

## 2026-09-15: hand-made icon

Released together with Drag'n Wash Localization v1.1.2. The libraries stay at 1.0.0.

### Core 1.1.2

- The Mods screen icon is the "Dg" monogram from the new hand-made logo. The READMEs open with the hand-made logo too.
- The preloader patcher is unchanged; its version follows the core.

## 2026-09-15: icon

Released together with Drag'n Wash Localization v1.1.1. The libraries stay at 1.0.0.

### Core 1.1.1

- The framework has its own icon on the Mods screen (`icon.png` next to the DLL), and the READMEs open with the logo.
- The preloader patcher is unchanged; its version follows the core.

## 2026-09-15: update notices

Released together with Drag'n Wash Localization v1.1.0. The libraries stay at 1.0.0.

### Core 1.1.0

- Shared installer for every mod: `Install.exe` (Windows, no PowerShell) and `install-steamdeck.sh` (Steam Deck / Linux) read the mod's `mod-install.json`. They install BepInEx (pinned SHA-256), never replace a newer framework with an older one, keep the player's files on update, write the mod's choices to its config, and on uninstall keep the framework and BepInEx while other mods need them. Shipped in the release zip under `installer/`; see [Installer (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Installer).
- Uninstall from the Mods screen: press **Uninstall** twice, and the preloader patcher removes the mod's folder at the next launch, keeping the player's data listed in `mod-install.json`. The framework, its libraries and BepInEx are not removable from there.

- Update notices. A mod that names its GitHub repository (`ModInfo.UpdateRepository = "owner/name"`) is checked against the repository's latest release once a day. The Mods screen tags the mod with **Update**, shows the new version and opens its release page, and the title screen says how many updates are available. Nothing is downloaded or changed. Drafts and pre-releases are never offered. Players can switch it off in the framework's settings on the Mods screen (`[Updates] Check for updates`). The framework checks itself the same way.

## 2026-09-15: first release

First release, together with [Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) v1.0.0, the first mod built on the framework.

### Core 1.0.0

- Mods screen, reached from the game's Options screen: every BepInEx plugin with its name, version, description, authors, website and icon (`ModFramework.Register(ModInfo)`, or read from the DLL and a Thunderstore manifest), plugins that did not load and why, and preloader patchers.
- On/off switches, applied by the preloader patcher at the next launch; switching off a mod other mods need asks first.
- Settings pages generated from BepInEx config, and rows in the game's own Options screen (`GameOptions`).
- Extension points: service registry (`Services`), health checks for patched game methods (`GameHooks`), extra Mods screen pages (`ModFramework.AddModsPage`), libraries (`ModInfo.IsLibrary`) with the mods that need them and the mods each one uses.
- Detection of game methods patched by more than one mod, shown as a Conflict.
- The title screen shows "Drag'n Wash ModFramework <version>" and how many mods loaded, just above the game's build id, like Minecraft Forge.
- Works with mouse, gamepad and on the Steam Deck: a thin white frame shows the selected item, and A, R2 and the trackpad click press it. The layout follows window resizes and full screen.

### Text 1.0.0

- `GameText.AddRewriter`, `RefreshAll`, `TryGetSource`: see and replace every TextMeshPro text before the game shows it, in an explicit order, and apply the rewriters again after something they depend on changed.

### Dialogue 1.0.0

- `GameDialogue.LineShowing`, `OptionShowing`, `NodeStarted`, `CurrentNode`, `TryGetLine`: the line of dialogue or option about to be shown, with line ID, speaker and node.

### Tool window 1.0.0

- One shared window (F1 by default, `[General] ToggleKey`) where mods add tabs with `ToolWindow.AddTab`. Frees the cursor, blocks game input under the window, turns gamepad and Steam Deck trackpad presses into clicks, and draws with a font that has Japanese and Chinese glyphs. A tab that throws is turned off with its error shown; the other tabs keep working.

### Assets 1.0.0

- `GameFonts`: one fallback font chain for the whole game, prepared per language at startup so Direct3D 12 does not crash. `GameAssets.LoadTexture` and `LoadBundle`, cached per file.

### Flags and saves 1.0.0

- `GameSaves`: save slots, level and flags, edits that snapshot first, restore, and a history of every version in `BepInEx/SaveHistory`. `GameFlags`: the flag catalog from CSV files.
