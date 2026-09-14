# Shipping your mod with the shared installer

[日本語](INSTALLER.ja.md)

Every Drag'n Wash mod can ship the same installer: `Install.exe` for Windows and `install-steamdeck.sh` for the Steam Deck and Linux. They install BepInEx, Drag'n Wash ModFramework and your mod, and uninstall them again, following the same rules for every mod so that mods never break each other's installs. What is specific to your mod comes from one file, `mod-install.json`.

Players can also uninstall a mod in the game, from **Options → Mods**. See [Uninstalling in the game](#uninstalling-in-the-game).

## Your release zip

```
Install.exe                  from installer/ in the framework's release zip
install-steamdeck.sh         from installer/ in the framework's release zip
mod-install.json             yours
BepInEx/plugins/<YourMod>/...
BepInEx/plugins/DragNWash.ModFramework*/...        the framework and the libraries you use,
BepInEx/patchers/DragNWash.ModFramework.Preloader.dll   unchanged from the framework's release
README.md
```

Players extract the zip anywhere and double-click `Install.exe`, or run `bash install-steamdeck.sh` in Desktop Mode. Ship the installer files unchanged: `Install.exe` is built deterministically, so its hash, and the reputation antivirus software gives it, stays the same across every mod that ships it.

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

| Field | |
|---|---|
| `schema` | Always `1`. An installer refuses a schema it does not know. |
| `name`, `version`, `website` | Shown in the installer window. `website` must be `https://`. |
| `plugins` | Your folders under `BepInEx/plugins`. Plain folder names; never a `DragNWash.ModFramework*` folder. |
| `keep` | Paths inside your folders that hold the player's own data. Kept when uninstalling unless the player asks to remove everything. |
| `configFiles` | Your files in `BepInEx/config`, removed when uninstalling. |
| `choices` | Optional. Each one is a question in the installer whose answer is written to a BepInEx config entry. `default` is an option value, or `"ui-language"` for the option whose value matches the system's language (`ja`, `zh-Hans`, `pt-BR`, ...). A value already in the config file always wins, so updating keeps what the player chose. |

Every path is checked: nothing in the manifest can point outside your own folders and files.

## What the installer does

**Install and update**

1. Finds the game through Steam, or lets the player choose the folder. Stops if the game is running.
2. Installs BepInEx 5.4.23.5 when it is missing. The download is checked against a pinned SHA-256; a mismatch installs nothing.
3. Copies the framework and its libraries, unless the same or a newer version is already installed. An older zip never replaces a newer framework another mod brought.
4. Copies your folders **over** what is there. An update never deletes files, so files the player added stay.
5. Writes the choices to your config file.
6. Switches your mod back on if it was switched off on the Mods screen, and cancels an uninstall waiting in the game.
7. Leaves a copy of `mod-install.json` in your first plugin folder, for the Mods screen and later installers.
8. On the Steam Deck and Linux: sets `executable_name` in `run_bepinex.sh` and the launch option `./run_bepinex.sh %command%`, closing Steam briefly after asking.

**Uninstall**

1. Removes your folders, except `keep` (the player can choose to remove those too), and your `configFiles`.
2. Removes the framework only when no other mod is left, and `BepInEx/SaveHistory` only when the player does not keep their data.
3. Removes BepInEx only when the player asks and no other mod or patcher is left. On the Steam Deck the launch option is taken out when no mod is left.

The installers write a log: in the window on Windows, and in `~/.local/state/dragnwash-installer/installer.log` on Linux.

## Uninstalling in the game

The Mods screen has an **Uninstall** button for every mod except the framework and its libraries. The first press explains what happens and names the mods that need this one; the second press confirms. A mod cannot be removed while the game runs, so the framework's preloader patcher removes it the next time the game starts, before any mod loads. Until then, **Cancel uninstall** takes it back.

What is removed: the mod's folder under `BepInEx/plugins` (or its DLL, when it sits there directly). With a `mod-install.json` in the folder, `keep` paths stay and `configFiles` go; without one, the config file named after the plugin's GUID goes. BepInEx and the framework stay; use the installer to remove those.

## Command line

For testing, both installers run without questions:

```
Install.exe --install [--game-dir <folder>] [--choice <id>=<value>]
Install.exe --uninstall [--game-dir <folder>] [--remove-data] [--remove-bepinex]
bash install-steamdeck.sh --yes --install [--game-dir <folder>] [--choice <id>=<value>] [--no-launch-option]
bash install-steamdeck.sh --yes --uninstall [--remove-data] [--remove-bepinex]
```

`--bepinex-zip <file>` uses a local BepInEx zip; it is still checked against the pinned SHA-256.
