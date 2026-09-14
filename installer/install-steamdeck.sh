#!/usr/bin/env bash
# Drag'n Wash mod installer / uninstaller for Steam Deck and Linux.
# The same script ships with every mod; what is specific to the mod comes from
# mod-install.json next to it.
#
# Run it from the extracted release folder, in Desktop Mode:
#   bash install-steamdeck.sh              asks whether to install or uninstall
#   bash install-steamdeck.sh --install    install or update straight away
#   bash install-steamdeck.sh --uninstall  remove the mod straight away
#
# What it does:
#   1. finds Drag'n Wash in your Steam libraries
#   2. downloads the Linux build of BepInEx 5.4.23.5 if it is missing and
#      checks its SHA-256
#   3. sets executable_name="DragNWash" in run_bepinex.sh
#   4. copies Drag'n Wash ModFramework and its libraries (unless a newer copy
#      is already installed), then the mod, and applies the mod's choices
#   5. sets the Steam launch option ./run_bepinex.sh %command%
#      (Steam has to be closed for that; you are asked first)
#
# Options: --install  --uninstall  --choice <id>=<value>  --game-dir <path>
#          --yes (no questions, use defaults; installs unless --uninstall)
#          --remove-bepinex --remove-data (with --uninstall)
#          --close-steam (close Steam without asking when the launch option
#          has to change)  --no-launch-option (leave launch options alone)
#          --bepinex-zip <file> (use a local BepInEx zip, still checked)
#          --ui en|ja|zh
set -euo pipefail

APP_ID=4739660
GAME_BIN="DragNWash"
MARKER=".bepinex-installed-by-dragnwash-installer"
OLD_MARKER=".bepinex-installed-by-dragnwash-localization"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_linux_x64_5.4.23.5.zip"
BEPINEX_SHA256="e538560be65739f562519ab518a75f9c65b3f57f87457403ae7cde683c12dab7"
LAUNCH_OPTION="./run_bepinex.sh %command%"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MANIFEST="$HERE/mod-install.json"
FRAMEWORK_PREFIX="DragNWash.ModFramework"
FRAMEWORK_PATCHER="DragNWash.ModFramework.Preloader.dll"
FRAMEWORK_LISTS="com.tomxv.dragnwash.modframework.disabled.txt com.tomxv.dragnwash.modframework.state.txt com.tomxv.dragnwash.modframework.uninstall.txt"

MODE=""
GAME_DIR=""
ASSUME_YES=0
REMOVE_BEPINEX=0
REMOVE_DATA=0
CLOSE_STEAM=0
LAUNCH_OPTIONS=1
LOCAL_ZIP=""
UI=""
WARNINGS=""
declare -A CHOICE=()

while [ $# -gt 0 ]; do
    case "$1" in
        --install) MODE=install ;;
        --uninstall) MODE=uninstall ;;
        --choice) pair="${2:-}"; CHOICE["${pair%%=*}"]="${pair#*=}"; shift ;;
        --lang) CHOICE[language]="${2:-}"; shift ;;  # the option earlier Localization scripts had
        --game-dir) GAME_DIR="${2:-}"; shift ;;
        --yes|-y) ASSUME_YES=1 ;;
        --remove-bepinex) REMOVE_BEPINEX=1 ;;
        --remove-data) REMOVE_DATA=1 ;;
        --close-steam) CLOSE_STEAM=1 ;;
        --no-launch-option) LAUNCH_OPTIONS=0 ;;
        --bepinex-zip) LOCAL_ZIP="${2:-}"; shift ;;
        --ui) UI="${2:-}"; shift ;;
        -h|--help) sed -n '2,28p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

# ---------------------------------------------------------------- manifest --
# mod-install.json is read with Python 3, which SteamOS ships. The checks match
# the Windows installer: every path the manifest names stays inside the mod's
# own folders and files.
mf() {
    python3 - "$MANIFEST" "$@" <<'PY'
import json, os, re, sys
path, cmd, args = sys.argv[1], sys.argv[2], sys.argv[3:]

def fail(msg):
    print(msg, file=sys.stderr)
    sys.exit(3)

SIMPLE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._ \-]{0,99}$")
ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._\-]{0,63}$")

def config_name(f):
    if not isinstance(f, str) or not SIMPLE.match(f) or ".." in f or not f.lower().endswith((".cfg", ".txt", ".json")) \
            or f.lower().startswith("bepinex") or f.startswith("com.tomxv.dragnwash.modframework"):
        fail(f"mod-install.json: \"{f}\" is not a config file name this installer accepts")
    return f

def load():
    try:
        m = json.load(open(path, encoding="utf-8-sig"))
    except Exception as e:
        fail(f"mod-install.json could not be read: {e}")
    if m.get("schema") != 1:
        fail(f"mod-install.json: schema {m.get('schema')} is not supported (this installer reads schema 1)")
    if not str(m.get("name") or "").strip():
        fail("mod-install.json: \"name\" is required")
    plugins = [p for p in (m.get("plugins") or []) if str(p).strip()]
    if not plugins:
        fail("mod-install.json: \"plugins\" must name at least one folder")
    for p in plugins:
        if not SIMPLE.match(p) or p.endswith(".") or p.lower().startswith("dragnwash.modframework"):
            fail(f"mod-install.json: \"{p}\" is not a plugin folder name this installer accepts")
    m["plugins"] = plugins
    keep = []
    for k in m.get("keep") or []:
        parts = str(k).replace("\\", "/").strip("/").split("/")
        if len(parts) < 2 or any(s in ("", ".", "..") for s in parts) or parts[0] not in plugins:
            fail(f"mod-install.json: keep path \"{k}\" must be inside one of the mod's plugin folders")
        keep.append("/".join(parts))
    m["keep"] = keep
    m["configFiles"] = [config_name(f) for f in (m.get("configFiles") or []) if str(f).strip()]
    for c in m.get("choices") or []:
        if not ID.match(str(c.get("id") or "")):
            fail("mod-install.json: every choice needs an \"id\"")
        cfg = c.get("config") or {}
        config_name(cfg.get("file"))
        if not str(cfg.get("section") or "").strip() or not str(cfg.get("key") or "").strip() \
                or re.search(r"[\[\]\r\n]", cfg["section"]) or re.search(r"[=\r\n]", cfg["key"]):
            fail(f"mod-install.json: choice \"{c['id']}\" has an invalid config target")
        c["options"] = [o for o in (c.get("options") or []) if o and o.get("value") and "\n" not in o["value"]]
        if not c["options"]:
            fail(f"mod-install.json: choice \"{c['id']}\" has no options")
    m["choices"] = m.get("choices") or []
    return m

def cfg_get(file, section, key):
    if not os.path.exists(file):
        return None
    cur = None
    for raw in open(file, encoding="utf-8-sig"):
        line = raw.strip()
        if line.startswith("[") and line.endswith("]"):
            cur = line[1:-1].strip()
        elif cur == section and "=" in line and line.split("=", 1)[0].strip() == key:
            return line.split("=", 1)[1].strip()
    return None

def cfg_set(file, section, key, value):
    os.makedirs(os.path.dirname(file), exist_ok=True)
    lines = open(file, encoding="utf-8-sig").read().splitlines() if os.path.exists(file) else []
    cur, at = None, -1
    for i, raw in enumerate(lines):
        line = raw.strip()
        if line.startswith("[") and line.endswith("]"):
            cur = line[1:-1].strip()
            if cur == section:
                at = i
        elif cur == section and "=" in line and line.split("=", 1)[0].strip() == key:
            lines[i] = f"{key} = {value}"
            break
    else:
        if at >= 0:
            lines.insert(at + 1, f"{key} = {value}")
        else:
            if lines and lines[-1].strip():
                lines.append("")
            lines += [f"[{section}]", "", f"{key} = {value}"]
    open(file, "w", encoding="utf-8").write("\n".join(lines) + "\n")

def ui_match(options, locale):
    values = [o["value"] for o in options]
    for c in (locale, locale.split("-")[0]):
        for v in values:
            if v.lower() == c.lower():
                return v
    return None

m = load()
choices = {c["id"]: c for c in m["choices"]}
if cmd == "check":
    pass
elif cmd in ("name", "version"):
    print(m.get(cmd) or "")
elif cmd in ("plugins", "keep", "configFiles"):
    for v in m[cmd]:
        print(v)
elif cmd == "choices":
    for c in m["choices"]:
        print(c["id"])
elif cmd == "label":   # label <id> <ui>
    lab = choices[args[0]].get("label") or {}
    print(lab.get(args[1]) or lab.get("en") or args[0])
elif cmd == "options":   # options <id>
    for o in choices[args[0]]["options"]:
        print(f"{o['value']}\t{o.get('name') or o['value']}")
elif cmd == "has":   # has <id> <value>
    sys.exit(0 if any(o["value"] == args[1] for o in choices[args[0]]["options"]) else 1)
elif cmd == "default":   # default <id> <game> <ui-locale>
    c = choices[args[0]]
    t = c["config"]
    existing = cfg_get(os.path.join(args[1], "BepInEx", "config", t["file"]), t["section"], t["key"])
    values = [o["value"] for o in c["options"]]
    if existing in values:
        print(existing)
    elif c.get("default") == "ui-language" and ui_match(c["options"], args[2]):
        print(ui_match(c["options"], args[2]))
    elif c.get("default") in values:
        print(c["default"])
    else:
        print(values[0])
elif cmd == "apply":   # apply <id> <game> <value>
    c = choices[args[0]]
    t = c["config"]
    cfg_set(os.path.join(args[1], "BepInEx", "config", t["file"]), t["section"], t["key"], args[2])
elif cmd == "copy":   # copy <dst>: the manifest as installed, for the Mods screen
    json.dump(m, open(args[0], "w", encoding="utf-8"), ensure_ascii=False, indent=2)
elif cmd == "prune":   # prune <game> <plugin>: delete the plugin folder except keep
    import shutil
    game, plugin = args
    root = os.path.join(game, "BepInEx", "plugins", plugin)
    keep = [k[len(plugin) + 1:] for k in m["keep"] if k.startswith(plugin + "/")]
    keep = [k for k in keep if os.path.exists(os.path.join(root, k))]
    if not os.path.isdir(root):
        sys.exit(0)
    if not keep:
        shutil.rmtree(root)
        sys.exit(0)
    def prune(d, rel):
        for name in os.listdir(d):
            child = f"{rel}/{name}" if rel else name
            full = os.path.join(d, name)
            if child in keep:
                continue
            if any(k.startswith(child + "/") for k in keep) and os.path.isdir(full) and not os.path.islink(full):
                prune(full, child)
            elif os.path.isdir(full) and not os.path.islink(full):
                shutil.rmtree(full)
            else:
                os.remove(full)
    prune(root, "")
    print("kept: " + ", ".join(keep))
else:
    fail(f"unknown command {cmd}")
PY
}

# ----------------------------------------------------------------- strings --
# Which language to talk in, and which option a "ui-language" choice preselects.
#   1. The desktop's language, when it is not English. Prompts exist in
#      English, Japanese and Chinese; for other languages only the
#      preselected option follows.
#   2. Otherwise Steam's own language. Desktop Mode on a Deck is English
#      unless someone changed it in System Settings, while Steam itself is
#      often set to the player's language, and that is what they see in
#      Gaming Mode.
#   3. English.
detect_ui() {
    local v
    for v in "${LC_ALL:-}" "${LC_MESSAGES:-}" "${LANGUAGE:-}" "${LANG:-}"; do
        case "$v" in
            ja*) echo "ja ja"; return ;;
            zh_TW*|zh_HK*|zh_MO*|zh-Hant*) echo "zh zh-Hant"; return ;;
            zh*) echo "zh zh-Hans"; return ;;
            ko*) echo "en ko"; return ;;
            de*) echo "en de"; return ;;
            fr*) echo "en fr"; return ;;
            es*) echo "en es"; return ;;
            pt_BR*|pt-BR*) echo "en pt-BR"; return ;;
            ru*) echo "en ru"; return ;;
            pl*) echo "en pl"; return ;;
            he*|iw*) echo "en he"; return ;;
            eo*) echo "en eo"; return ;;
        esac
    done
    local steam_lang
    steam_lang="$(sed -n 's/^[[:space:]]*"language"[[:space:]]*"\([^"]*\)".*/\1/p' "$HOME/.steam/registry.vdf" 2>/dev/null | head -1 || true)"
    case "$steam_lang" in
        japanese) echo "ja ja" ;;
        schinese) echo "zh zh-Hans" ;;
        tchinese) echo "zh zh-Hant" ;;
        koreana) echo "en ko" ;;
        german) echo "en de" ;;
        french) echo "en fr" ;;
        spanish|latam) echo "en es" ;;
        brazilian) echo "en pt-BR" ;;
        russian) echo "en ru" ;;
        polish) echo "en pl" ;;
        *) echo "en en" ;;
    esac
}
read -r DETECTED_UI UI_LOCALE < <(detect_ui)
[ -n "$UI" ] || UI="$DETECTED_UI"

t() {
    local key="$1"
    case "$UI:$key" in
        ja:nopayload) echo "Mod のファイルが見つかりません。zip を丸ごと展開して、その中でこのスクリプトを実行してください。" ;;
        zh:nopayload) echo "找不到 Mod 文件。请完整解压 zip，并在解压后的文件夹中运行此脚本。" ;;
        *:nopayload) echo "The mod files are missing. Extract the whole zip and run this script from inside it." ;;
        ja:nopython) echo "python3 が見つかりません。SteamOS には標準で入っています。" ;;
        zh:nopython) echo "找不到 python3。SteamOS 默认自带。" ;;
        *:nopython) echo "python3 was not found. SteamOS includes it." ;;
        ja:nogame) echo "Drag'n Wash が見つかりません。--game-dir でゲームのフォルダを指定してください。" ;;
        zh:nogame) echo "找不到 Drag'n Wash。请用 --game-dir 指定游戏文件夹。" ;;
        *:nogame) echo "Drag'n Wash was not found. Pass the game folder with --game-dir." ;;
        ja:running) echo "ゲームが起動中です。先に終了してください。" ;;
        zh:running) echo "游戏正在运行。请先关闭游戏。" ;;
        *:running) echo "The game is running. Close it first." ;;
        ja:bep_have) echo "BepInEx: 導入済み" ;;
        zh:bep_have) echo "BepInEx：已安装" ;;
        *:bep_have) echo "BepInEx: already installed" ;;
        ja:bep_get) echo "BepInEx: ダウンロード中..." ;;
        zh:bep_get) echo "BepInEx：正在下载..." ;;
        *:bep_get) echo "BepInEx: downloading..." ;;
        ja:bep_bad) echo "BepInEx のダウンロードが改ざんされているか壊れています（SHA-256 不一致）。中止します。" ;;
        zh:bep_bad) echo "下载的 BepInEx 已损坏或被篡改（SHA-256 不一致）。已中止。" ;;
        *:bep_bad) echo "The BepInEx download is corrupt or tampered with (SHA-256 mismatch). Stopping." ;;
        ja:bep_ok) echo "BepInEx: 検証 OK、展開しました" ;;
        zh:bep_ok) echo "BepInEx：校验通过，已解压" ;;
        *:bep_ok) echo "BepInEx: verified and unpacked" ;;
        ja:mod_ok) echo "Mod: ファイルをコピーしました" ;;
        zh:mod_ok) echo "Mod：文件已复制" ;;
        *:mod_ok) echo "Mod: files copied" ;;
        ja:lo_same) echo "起動オプション: 設定済み" ;;
        zh:lo_same) echo "启动选项：已设置" ;;
        *:lo_same) echo "Launch option: already set" ;;
        ja:lo_ask) echo "Steam の起動オプションを変更するため、Steam を一度終了します。Steam は起動中に起動オプションを上書きするので、終了しないと変更が反映されません。変更後に Steam を自動で起動し直します。

Steam を終了して続けますか？（「いいえ」の場合は、起動オプションを手動で変更してください）" ;;
        zh:lo_ask) echo "要修改 Steam 启动选项，需要先关闭 Steam。Steam 运行时会覆盖启动选项，不关闭则修改不会生效。修改后会自动重新启动 Steam。

关闭 Steam 并继续吗？（选择“否”则需要手动修改启动选项）" ;;
        *:lo_ask) echo "Steam will be closed briefly to change the launch option. Steam overwrites launch options while it runs, so the change only sticks with Steam closed. Steam is started again afterwards.

Close Steam and continue? (If not, change the launch option by hand.)" ;;
        ja:lo_manual) echo "起動オプションは手動で設定してください: Steam でゲームのプロパティ → 起動オプション に次を入力" ;;
        zh:lo_manual) echo "请手动设置启动选项：在 Steam 中打开游戏属性 → 启动选项，输入以下内容" ;;
        *:lo_manual) echo "Set the launch option by hand: in Steam, game Properties → Launch Options, enter" ;;
        ja:lo_done) echo "起動オプションを設定しました:" ;;
        zh:lo_done) echo "启动选项已设置：" ;;
        *:lo_done) echo "Launch option set:" ;;
        ja:attention) echo "【要確認】うまくいかなかった手順があります:" ;;
        zh:attention) echo "【请注意】有步骤未能完成：" ;;
        *:attention) echo "Something needs your attention:" ;;
        ja:steam_slow_remove) echo "Steam が終了しなかったため、起動オプションを変更できませんでした。" ;;
        zh:steam_slow_remove) echo "Steam 没有退出，无法修改启动选项。" ;;
        *:steam_slow_remove) echo "Steam did not exit, so the launch option could not be changed." ;;
        ja:lo_manual_remove) echo "起動オプションは手動で元に戻してください: Steam でゲームのプロパティ → 起動オプション から ./run_bepinex.sh を消す" ;;
        zh:lo_manual_remove) echo "请手动恢复启动选项：在 Steam 中打开游戏属性 → 启动选项，删除 ./run_bepinex.sh" ;;
        *:lo_manual_remove) echo "Restore the launch option by hand: in Steam, game Properties → Launch Options, remove ./run_bepinex.sh" ;;
        ja:lo_removed) echo "起動オプションを元に戻しました" ;;
        zh:lo_removed) echo "启动选项已恢复" ;;
        *:lo_removed) echo "Launch option restored" ;;
        ja:steam_wait) echo "Steam の終了を待っています..." ;;
        zh:steam_wait) echo "正在等待 Steam 退出..." ;;
        *:steam_wait) echo "Waiting for Steam to exit..." ;;
        ja:steam_slow) echo "Steam が終了しなかったため、起動オプションを設定できませんでした。" ;;
        zh:steam_slow) echo "Steam 没有退出，无法设置启动选项。" ;;
        *:steam_slow) echo "Steam did not exit, so the launch option could not be set." ;;
        ja:done) echo "完了しました。ゲームモードに戻って、Steam からゲームを起動してください。Mod はゲームの Options → Mods からもアンインストールできます。" ;;
        zh:done) echo "完成。请回到游戏模式，从 Steam 启动游戏。也可以在游戏的 Options → Mods 中卸载模组。" ;;
        *:done) echo "Done. Go back to Gaming Mode and start the game from Steam. Mods can also be uninstalled in the game: Options → Mods." ;;
        ja:keep) echo "セーブ履歴と Mod の作業ファイルは残しますか？" ;;
        zh:keep) echo "保留存档历史和模组的工作文件吗？" ;;
        *:keep) echo "Keep save history and the mod's working files?" ;;
        ja:rmbep) echo "BepInEx も削除しますか？（BepInEx を使う Mod はほかにありません）" ;;
        zh:rmbep) echo "同时删除 BepInEx 吗？（没有其他使用 BepInEx 的 Mod）" ;;
        *:rmbep) echo "Also remove BepInEx? (no other mod uses it)" ;;
        ja:bep_kept) echo "BepInEx: 他の Mod があるため残しました" ;;
        zh:bep_kept) echo "BepInEx：存在其他 Mod，已保留" ;;
        *:bep_kept) echo "BepInEx: kept, other mods use it" ;;
        ja:bep_removed) echo "BepInEx: 削除しました" ;;
        zh:bep_removed) echo "BepInEx：已删除" ;;
        *:bep_removed) echo "BepInEx: removed" ;;
        ja:fw_kept) echo "ModFramework: 他の Mod があるため残しました" ;;
        zh:fw_kept) echo "ModFramework：存在其他 Mod，已保留" ;;
        *:fw_kept) echo "ModFramework: kept, other mods are installed" ;;
        ja:fw_removed) echo "ModFramework: 削除しました" ;;
        zh:fw_removed) echo "ModFramework：已删除" ;;
        *:fw_removed) echo "ModFramework: removed" ;;
        ja:mod_removed) echo "Mod: 削除しました" ;;
        zh:mod_removed) echo "Mod：已删除" ;;
        *:mod_removed) echo "Mod: removed" ;;
        ja:undone) echo "アンインストールが完了しました。" ;;
        zh:undone) echo "卸载完成。" ;;
        *:undone) echo "Uninstall finished." ;;
        ja:confirm_install) echo "次のフォルダに Mod を導入します。よろしいですか？" ;;
        zh:confirm_install) echo "将把 Mod 安装到以下文件夹。继续吗？" ;;
        *:confirm_install) echo "Install the mod into this folder?" ;;
        ja:confirm_uninstall) echo "次のフォルダから Mod を削除します。よろしいですか？" ;;
        zh:confirm_uninstall) echo "将从以下文件夹删除 Mod。继续吗？" ;;
        *:confirm_uninstall) echo "Remove the mod from this folder?" ;;
        ja:action) echo "何をしますか？" ;;
        zh:action) echo "要执行什么操作？" ;;
        *:action) echo "What would you like to do?" ;;
        ja:act_install) echo "インストール / 更新" ;;
        zh:act_install) echo "安装 / 更新" ;;
        *:act_install) echo "Install / Update" ;;
        ja:act_uninstall) echo "アンインストール" ;;
        zh:act_uninstall) echo "卸载" ;;
        *:act_uninstall) echo "Uninstall" ;;
        ja:st_bep) echo "BepInEx" ;;
        zh:st_bep) echo "BepInEx" ;;
        *:st_bep) echo "BepInEx" ;;
        ja:st_mod) echo "Mod" ;;
        zh:st_mod) echo "Mod" ;;
        *:st_mod) echo "Mod" ;;
        ja:st_yes) echo "導入済み" ;;
        zh:st_yes) echo "已安装" ;;
        *:st_yes) echo "installed" ;;
        ja:st_no) echo "未導入" ;;
        zh:st_no) echo "未安装" ;;
        *:st_no) echo "not installed" ;;
        ja:nothing) echo "Mod は導入されていません。" ;;
        zh:nothing) echo "尚未安装 Mod。" ;;
        *:nothing) echo "The mod is not installed." ;;
        *) echo "$key" ;;
    esac
}

# -------------------------------------------------------------- dialogs ----
GUI=0
if [ "$ASSUME_YES" -eq 0 ] && command -v kdialog >/dev/null 2>&1 && { [ -n "${DISPLAY:-}" ] || [ -n "${WAYLAND_DISPLAY:-}" ]; }; then
    GUI=1
fi

LOG="${XDG_STATE_HOME:-$HOME/.local/state}/dragnwash-installer/installer.log"
mkdir -p "$(dirname "$LOG")" 2>/dev/null || true
log() { printf '%s %s\n' "$(date '+%F %T')" "$*" >> "$LOG" 2>/dev/null || true; }
say() { echo "$*"; log "$*"; }
warn() { say "$*"; WARNINGS="${WARNINGS}${WARNINGS:+

}$*"; }
TITLE="Drag'n Wash mod installer"
fail() {
    echo "ERROR: $*" >&2
    log "ERROR: $*"
    if [ "$GUI" -eq 1 ]; then kdialog --title "$TITLE" --error "$*" >/dev/null 2>&1 || true; fi
    exit 1
}
ask_yes() {  # ask_yes "question" default(1=yes,0=no)
    local q="$1" def="${2:-1}"
    if [ "$ASSUME_YES" -eq 1 ]; then [ "$def" -eq 1 ]; return; fi
    if [ "$GUI" -eq 1 ]; then kdialog --title "$TITLE" --yesno "$q" >/dev/null 2>&1; return; fi
    local hint="[Y/n]"; [ "$def" -eq 0 ] && hint="[y/N]"
    local reply; read -r -p "$q $hint " reply || reply=""
    case "$reply" in
        [Yy]*) return 0 ;;
        [Nn]*) return 1 ;;
        *) [ "$def" -eq 1 ] ;;
    esac
}
finish_message() {
    local text="$1"
    if [ -n "$WARNINGS" ]; then
        text="$(t attention)

$WARNINGS

$1"
    fi
    say "$text"
    if [ "$GUI" -eq 1 ]; then
        if [ -n "$WARNINGS" ]; then
            kdialog --title "$TITLE" --sorry "$text" >/dev/null 2>&1 || true
        else
            kdialog --title "$TITLE" --msgbox "$text" >/dev/null 2>&1 || true
        fi
    fi
}

command -v python3 >/dev/null 2>&1 || fail "$(t nopython)"
[ -f "$MANIFEST" ] || fail "$(t nopayload)"
mf check >/dev/null || fail "$(t nopayload)"
MOD_NAME="$(mf name)"
MOD_VERSION="$(mf version)"
mapfile -t PLUGINS < <(mf plugins)
TITLE="$MOD_NAME $MOD_VERSION (Steam Deck / Linux)"

# ------------------------------------------------------------ discovery ----
steam_roots() {
    local r
    for r in "$HOME/.local/share/Steam" "$HOME/.steam/steam" "$HOME/.steam/root"; do
        [ -d "$r/steamapps" ] && readlink -f "$r"
    done | awk '!seen[$0]++'
}

library_paths() {
    local root vdf
    for root in $(steam_roots); do
        echo "$root"
        vdf="$root/steamapps/libraryfolders.vdf"
        [ -f "$vdf" ] && sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$vdf"
    done | awk '!seen[$0]++'
}

find_game() {
    local lib dir name
    while IFS= read -r lib; do
        [ -n "$lib" ] || continue
        name="Drag'n Wash"
        if [ -f "$lib/steamapps/appmanifest_$APP_ID.acf" ]; then
            name="$(sed -n 's/^[[:space:]]*"installdir"[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$lib/steamapps/appmanifest_$APP_ID.acf" | head -1)"
        fi
        dir="$lib/steamapps/common/$name"
        if [ -f "$dir/$GAME_BIN" ]; then echo "$dir"; return 0; fi
    done < <(library_paths)
    return 1
}

steam_running() {
    local pid
    pid="$(cat "$HOME/.steam/steam.pid" 2>/dev/null || true)"
    [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null
}

# ------------------------------------------------------ launch options ----
# localconfig.vdf: UserLocalConfigStore > Software > Valve > Steam > apps > "<id>".
# Steam rewrites this file when it exits, so it must not be running.
vdf_tool() {  # vdf_tool <file> set|remove|check
    python3 - "$1" "$APP_ID" "$LAUNCH_OPTION" "$2" <<'PY'
import re, shutil, sys
path, app, option, action = sys.argv[1:5]
text = open(path, encoding="utf-8").read()

def find_block(text, key, start=0, end=None):
    """Return (open_brace_index, close_brace_index) of "key" { ... } within [start, end)."""
    end = len(text) if end is None else end
    for m in re.finditer(r'"%s"\s*\{' % re.escape(key), text[start:end]):
        o = start + m.end() - 1
        depth, i = 0, o
        while i < end:
            c = text[i]
            if c == '"':
                i += 1
                while i < end and text[i] != '"':
                    i += 2 if text[i] == '\\' else 1
            elif c == '{':
                depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    return o, i
            i += 1
    return None

blk = find_block(text, "Software")
blk = blk and find_block(text, "Valve", blk[0], blk[1])
blk = blk and find_block(text, "Steam", blk[0], blk[1])
blk = blk and find_block(text, "apps", blk[0], blk[1])
if not blk:
    sys.exit(1)
app_blk = find_block(text, app, blk[0], blk[1])

def indent_at(i):
    line_start = text.rfind("\n", 0, i) + 1
    return re.match(r"[\t ]*", text[line_start:]).group(0)

wrapped = option.replace(" %command%", "")   # ./run_bepinex.sh
if action == "check":
    if app_blk is None:
        sys.exit(4)
    body = text[app_blk[0]:app_blk[1]]
    m = re.search(r'"LaunchOptions"\s*"((?:[^"\\]|\\.)*)"', body)
    sys.exit(0 if (m and wrapped in m.group(1)) else 3)
if app_blk is None:
    if action == "remove":
        sys.exit(3)
    ind = indent_at(blk[1]) + "\t"
    insert = f'{ind}"{app}"\n{ind}{{\n{ind}\t"LaunchOptions"\t\t"{option}"\n{ind}}}\n'
    line_start = text.rfind("\n", 0, blk[1]) + 1
    new = text[:line_start] + insert + text[line_start:]
else:
    body = text[app_blk[0]:app_blk[1]]
    m = re.search(r'"LaunchOptions"\s*"((?:[^"\\]|\\.)*)"', body)
    current = m.group(1) if m else ""
    if action == "set":
        if wrapped in current:
            sys.exit(3)
        if "%command%" in current:
            value = current.replace("%command%", option, 1)
        elif current.strip():
            value = option + " " + current.strip()
        else:
            value = option
    else:
        if wrapped not in current:
            sys.exit(3)
        value = current.replace(option, "%command%", 1).replace(wrapped + " ", "", 1).strip()
        if value == "%command%":
            value = ""
    if m:
        s, e = app_blk[0] + m.start(1), app_blk[0] + m.end(1)
        new = text[:s] + value + text[e:]
    else:
        ind = indent_at(app_blk[1]) + "\t"
        line_start = text.rfind("\n", 0, app_blk[1]) + 1
        new = text[:line_start] + f'{ind}"LaunchOptions"\t\t"{value}"\n' + text[line_start:]

shutil.copy2(path, path + ".dragnwash-backup")
open(path, "w", encoding="utf-8").write(new)
sys.exit(0)
PY
}

edit_launch_options() {  # edit_launch_options set|remove ; 0 if any profile changed
    local action="$1" changed=0 vdf rc
    for vdf in "$HOME/.local/share/Steam/userdata"/*/config/localconfig.vdf; do
        [ -f "$vdf" ] || continue
        rc=0; vdf_tool "$vdf" "$action" || rc=$?
        case "$rc" in
            0) changed=1 ;;
            3|4) ;;        # nothing to change in this profile
            *) say "  (could not update $vdf)" ;;
        esac
    done
    [ "$changed" -eq 1 ]
}

launch_options_any() {  # yes when at least one profile has the wrapper
    local vdf rc
    for vdf in "$HOME/.local/share/Steam/userdata"/*/config/localconfig.vdf; do
        [ -f "$vdf" ] || continue
        rc=0; vdf_tool "$vdf" check || rc=$?
        [ "$rc" -eq 0 ] && { echo yes; return; }
    done
    echo no
}

launch_options_state() {  # yes when every profile that knows the game has the wrapper
    local vdf rc known=0 missing=0
    for vdf in "$HOME/.local/share/Steam/userdata"/*/config/localconfig.vdf; do
        [ -f "$vdf" ] || continue
        rc=0; vdf_tool "$vdf" check || rc=$?
        case "$rc" in
            0) known=1 ;;
            3) known=1; missing=1 ;;
        esac
    done
    if [ "$known" -eq 1 ] && [ "$missing" -eq 0 ]; then echo yes; else echo no; fi
}

start_steam() {
    # Start Steam in its own app-steam-*.scope. A plain child would stay in the
    # cgroup of whatever ran this script (Dolphin names it after the script),
    # and the desktop portal would then take Steam for install-steamdeck.sh and
    # ask "Share screen with" again instead of using Steam's saved permission.
    local unit
    unit="app-steam-$(od -An -N8 -tx8 /dev/urandom | tr -d ' \n').scope"
    if command -v systemd-run >/dev/null 2>&1 &&
        systemd-run --user --scope --quiet true >/dev/null 2>&1; then
        log "starting Steam in $unit"
        (nohup systemd-run --user --scope --quiet --slice=app.slice --unit="$unit" steam >/dev/null 2>&1 &) || true
    else
        log "systemd-run not usable; starting Steam directly"
        (nohup steam >/dev/null 2>&1 &) || true
    fi
}

with_steam_closed() {  # with_steam_closed set|remove ; returns 0 if applied
    local action="$1" was_running=0
    if steam_running; then
        log "Steam is running (pid $(cat "$HOME/.steam/steam.pid" 2>/dev/null)); launch option change needs it closed"
        if [ "$CLOSE_STEAM" -eq 1 ]; then
            log "closing Steam without asking (--close-steam)"
        elif ! ask_yes "$(t lo_ask)" 1; then
            log "user chose not to close Steam"
            return 2
        fi
        was_running=1
        log "running: steam -shutdown"
        steam -shutdown >/dev/null 2>&1 || true
        say "$(t steam_wait)"
        local i
        for i in $(seq 1 90); do
            steam_running || break
            sleep 1
        done
        if steam_running; then
            log "Steam still running after 90 s"
            return 3
        fi
        log "Steam exited"
        # Steam writes its config on the way out; give that a moment.
        sleep 3
    fi
    local rc=0
    edit_launch_options "$action" || rc=$?
    log "launch option $action: edit returned $rc"
    if [ "$was_running" -eq 1 ]; then
        log "starting Steam again"
        start_steam
    fi
    return "$rc"
}

# ------------------------------------------------------------------ main ----
log "---- start: $0 $* (mod=$MOD_NAME $MOD_VERSION, mode=${MODE:-ask}, ui=$UI, gui=$GUI)"
say "== $TITLE"

for p in "${PLUGINS[@]}"; do
    [ -d "$HERE/BepInEx/plugins/$p" ] || fail "$(t nopayload)"
done

if [ -z "$GAME_DIR" ]; then GAME_DIR="$(find_game || true)"; fi
[ -n "$GAME_DIR" ] && [ -f "$GAME_DIR/$GAME_BIN" ] || fail "$(t nogame)"
say "Game: $GAME_DIR"
# Only a game started from this folder counts.
for pid in $(pgrep -x "$GAME_BIN" 2>/dev/null || true); do
    exe="$(readlink -f "/proc/$pid/exe" 2>/dev/null || true)"
    if [ -z "$exe" ] || [ "$exe" = "$(readlink -f "$GAME_DIR/$GAME_BIN")" ]; then fail "$(t running)"; fi
done

installed_version() {
    local folder="$GAME_DIR/BepInEx/plugins/${PLUGINS[0]}"
    if [ -f "$folder/mod-install.json" ]; then
        MANIFEST="$folder/mod-install.json" mf version 2>/dev/null && return
    fi
    ls "$folder"/*.dll >/dev/null 2>&1 && echo "?"
}

if [ -z "$MODE" ]; then
    have_bep="$(t st_no)"; [ -f "$GAME_DIR/BepInEx/core/BepInEx.dll" ] && have_bep="$(t st_yes)"
    have_mod="$(installed_version || true)"; [ -n "$have_mod" ] || have_mod="$(t st_no)"
    status="$(t st_bep): $have_bep    $(t st_mod): $have_mod"
    if [ "$ASSUME_YES" -eq 1 ]; then
        MODE=install
    elif [ "$GUI" -eq 1 ]; then
        MODE="$(kdialog --title "$TITLE" --menu "$GAME_DIR
$status

$(t action)" install "$(t act_install)" uninstall "$(t act_uninstall)")" || exit 1
    else
        say "$status"
        say "$(t action)"
        say "  1) $(t act_install)"
        say "  2) $(t act_uninstall)"
        read -r -p "> " pick || pick=""
        case "$pick" in
            2) MODE=uninstall ;;
            1|"") MODE=install ;;
            *) exit 1 ;;
        esac
    fi
fi

# FileVersion of a .NET DLL (from its version resource), or empty.
dll_version() {
    [ -f "$1" ] || return 0
    python3 - "$1" <<'PY' 2>/dev/null || true
import re, sys
data = open(sys.argv[1], "rb").read()
z = b"\x00"
key = "FileVersion".encode("utf-16-le")
m = re.search(re.escape(key) + z + b"+((?:[0-9.]" + z + b")+)", data)
if m:
    print(m.group(1).decode("utf-16-le"))
PY
}

# 0 when version $1 is newer than $2.
version_newer() {
    [ -n "$1" ] && [ -n "$2" ] && [ "$1" != "$2" ] &&
        [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | tail -1)" = "$1" ]
}

# Forgets what the framework recorded about a plugin folder: switched off,
# renamed by the patcher, or waiting to be uninstalled.
forget_folder() {
    local folder="$1" list f
    for list in $FRAMEWORK_LISTS; do
        f="$GAME_DIR/BepInEx/config/$list"
        [ -f "$f" ] || continue
        awk -F '\t' -v p="$folder/" -v d="$folder" 'index($1, p) != 1 && $1 != d' "$f" > "$f.tmp" && mv -f "$f.tmp" "$f"
    done
}

# Installing means wanting the plugin on: drop copies the Mods screen switched off.
enable_folder() {
    local folder="$1" off
    while IFS= read -r -d '' off; do
        [ -f "${off%.disabled}" ] && rm -f "$off"
    done < <(find "$GAME_DIR/BepInEx/plugins/$folder" -name '*.dll.disabled' -print0 2>/dev/null)
    forget_folder "$folder"
}

other_mods() {
    local entry name p skip
    for entry in "$GAME_DIR/BepInEx/plugins"/*; do
        [ -e "$entry" ] || continue
        name="$(basename "$entry")"
        case "$name" in "$FRAMEWORK_PREFIX"*) continue ;; esac
        skip=0
        for p in "${PLUGINS[@]}"; do [ "$name" = "$p" ] && skip=1; done
        [ "$skip" -eq 1 ] || echo "$name"
    done
}

if [ "$MODE" = install ]; then
    # The mod's choices
    declare -A VALUE=()
    while IFS= read -r id; do
        [ -n "$id" ] || continue
        if [ -n "${CHOICE[$id]:-}" ]; then
            mf has "$id" "${CHOICE[$id]}" || fail "Unknown value for $id: ${CHOICE[$id]}"
            VALUE[$id]="${CHOICE[$id]}"
            continue
        fi
        default="$(mf default "$id" "$GAME_DIR" "$UI_LOCALE")"
        label="$(mf label "$id" "$UI")"
        if [ "$ASSUME_YES" -eq 1 ]; then
            VALUE[$id]="$default"
        elif [ "$GUI" -eq 1 ]; then
            args=()
            while IFS=$'\t' read -r value name; do
                state=off; [ "$value" = "$default" ] && state=on
                args+=("$value" "$name" "$state")
            done < <(mf options "$id")
            VALUE[$id]="$(kdialog --title "$TITLE" --radiolist "$label" "${args[@]}")" || exit 1
        else
            say "$label:"
            mapfile -t rows < <(mf options "$id")
            for i in "${!rows[@]}"; do
                value="${rows[$i]%%$'\t'*}"; name="${rows[$i]#*$'\t'}"
                mark=" "; [ "$value" = "$default" ] && mark="*"
                printf ' %s %2d) %s (%s)\n' "$mark" "$((i + 1))" "$name" "$value"
            done
            read -r -p "> " pick || pick=""
            if [ -z "$pick" ]; then
                VALUE[$id]="$default"
            elif [[ "$pick" =~ ^[0-9]+$ ]] && [ "$pick" -ge 1 ] && [ "$pick" -le "${#rows[@]}" ]; then
                VALUE[$id]="${rows[$((pick - 1))]%%$'\t'*}"
            else
                mf has "$id" "$pick" || fail "Unknown value for $id: $pick"
                VALUE[$id]="$pick"
            fi
        fi
    done < <(mf choices)

    ask_yes "$(t confirm_install)
$GAME_DIR" 1 || exit 1

    # BepInEx
    if [ -f "$GAME_DIR/BepInEx/core/BepInEx.dll" ]; then
        say "$(t bep_have)"
    else
        say "$(t bep_get)"
        tmp="$(mktemp -d)"
        trap 'rm -rf "$tmp"' EXIT
        if [ -n "$LOCAL_ZIP" ]; then cp -f "$LOCAL_ZIP" "$tmp/bepinex.zip"; else curl -fsSL -o "$tmp/bepinex.zip" "$BEPINEX_URL"; fi
        actual="$(sha256sum "$tmp/bepinex.zip" | cut -d' ' -f1)"
        [ "$actual" = "$BEPINEX_SHA256" ] || fail "$(t bep_bad)"
        unzip -oq "$tmp/bepinex.zip" -d "$GAME_DIR"
        echo "BepInEx was added by the Drag'n Wash mod installer." > "$GAME_DIR/BepInEx/$MARKER"
        say "$(t bep_ok)"
    fi

    # run_bepinex.sh
    if [ -f "$GAME_DIR/run_bepinex.sh" ]; then
        sed -i 's/^executable_name=.*/executable_name="'"$GAME_BIN"'"/' "$GAME_DIR/run_bepinex.sh"
        chmod +x "$GAME_DIR/run_bepinex.sh"
        say "run_bepinex.sh: executable_name=\"$GAME_BIN\""
    fi

    # Drag'n Wash ModFramework and its libraries, each in its own folder;
    # never replaced by an older copy.
    mkdir -p "$GAME_DIR/BepInEx/plugins"
    for src in "$HERE/BepInEx/plugins/$FRAMEWORK_PREFIX"*/; do
        [ -d "$src" ] || continue
        name="$(basename "$src")"
        dst="$GAME_DIR/BepInEx/plugins/$name"
        have="$(dll_version "$dst/$name.dll")"
        offered="$(dll_version "$src/$name.dll")"
        if version_newer "$have" "$offered"; then
            say "$name: kept $have (newer than $offered)"
            continue
        fi
        mkdir -p "$dst"
        cp -rf "$src." "$dst/"
        enable_folder "$name"
        say "$name: ${offered:-ok}"
    done
    if [ -f "$HERE/BepInEx/patchers/$FRAMEWORK_PATCHER" ]; then
        have="$(dll_version "$GAME_DIR/BepInEx/patchers/$FRAMEWORK_PATCHER")"
        offered="$(dll_version "$HERE/BepInEx/patchers/$FRAMEWORK_PATCHER")"
        if ! version_newer "$have" "$offered"; then
            mkdir -p "$GAME_DIR/BepInEx/patchers"
            cp -f "$HERE/BepInEx/patchers/$FRAMEWORK_PATCHER" "$GAME_DIR/BepInEx/patchers/"
        fi
    fi

    # The mod, copied over what is there: files the player added are kept.
    for p in "${PLUGINS[@]}"; do
        mkdir -p "$GAME_DIR/BepInEx/plugins/$p"
        cp -rf "$HERE/BepInEx/plugins/$p/." "$GAME_DIR/BepInEx/plugins/$p/"
        enable_folder "$p"
        say "$p: $(t mod_ok)"
    done
    mf copy "$GAME_DIR/BepInEx/plugins/${PLUGINS[0]}/mod-install.json"

    for id in "${!VALUE[@]}"; do
        mf apply "$id" "$GAME_DIR" "${VALUE[$id]}"
        say "$id: ${VALUE[$id]}"
    done

    # Steam launch option
    if [ "$LAUNCH_OPTIONS" -eq 0 ]; then
        log "launch options left alone (--no-launch-option)"
    elif [ "$(launch_options_state)" = yes ]; then
        say "$(t lo_same)"
    else
        rc=0; with_steam_closed set || rc=$?
        case "$rc" in
            0) say "$(t lo_done) $LAUNCH_OPTION" ;;
            3) warn "$(t steam_slow)
$(t lo_manual):
  $LAUNCH_OPTION" ;;
            *) warn "$(t lo_manual):
  $LAUNCH_OPTION" ;;
        esac
    fi

    finish_message "$(t done)"
else
    any=0
    for p in "${PLUGINS[@]}"; do [ -d "$GAME_DIR/BepInEx/plugins/$p" ] && any=1; done
    if [ "$any" -eq 0 ]; then
        finish_message "$(t nothing)"
        exit 0
    fi
    ask_yes "$(t confirm_uninstall)
$GAME_DIR" 1 || exit 1

    keep=1
    if [ "$REMOVE_DATA" -eq 1 ]; then
        keep=0
    elif [ "$ASSUME_YES" -eq 0 ]; then
        has_data=0
        while IFS= read -r k; do [ -e "$GAME_DIR/BepInEx/plugins/$k" ] && has_data=1; done < <(mf keep)
        [ -d "$GAME_DIR/BepInEx/SaveHistory" ] && has_data=1
        if [ "$has_data" -eq 1 ]; then ask_yes "$(t keep)" 1 || keep=0; fi
    fi

    for p in "${PLUGINS[@]}"; do
        forget_folder "$p"
        if [ "$keep" -eq 1 ]; then
            kept="$(mf prune "$GAME_DIR" "$p")"
        else
            rm -rf "$GAME_DIR/BepInEx/plugins/$p"; kept=""
        fi
        say "$p: $(t mod_removed)${kept:+ ($kept)}"
    done
    while IFS= read -r f; do
        [ -n "$f" ] && rm -f "$GAME_DIR/BepInEx/config/$f"
    done < <(mf configFiles)

    # The framework stays while any other mod is installed.
    if [ -n "$(other_mods)" ]; then
        say "$(t fw_kept)"
    else
        if [ -n "$(find "$GAME_DIR/BepInEx/plugins" -mindepth 1 -maxdepth 1 -name "$FRAMEWORK_PREFIX*" 2>/dev/null | head -1)" ]; then
            find "$GAME_DIR/BepInEx/plugins" -mindepth 1 -maxdepth 1 -name "$FRAMEWORK_PREFIX*" -exec rm -rf {} +
            rm -f "$GAME_DIR/BepInEx/patchers/$FRAMEWORK_PATCHER" "$GAME_DIR/BepInEx/config/com.tomxv.dragnwash.modframework"*
            say "$(t fw_removed)"
        fi
        # Save snapshots taken by the framework's saves library.
        if [ -d "$GAME_DIR/BepInEx/SaveHistory" ] && [ "$keep" -eq 0 ]; then
            rm -rf "$GAME_DIR/BepInEx/SaveHistory"
        fi
    fi

    # BepInEx and the launch option only matter to other mods now. If there
    # are none, offer to remove BepInEx (the default follows whether an
    # installer added it), and take ./run_bepinex.sh out of the launch options
    # either way: without BepInEx's files it would stop the game from starting.
    if [ -n "$(other_mods)" ] || [ -n "$(find "$GAME_DIR/BepInEx/patchers" -mindepth 1 2>/dev/null | head -1)" ]; then
        say "$(t bep_kept)"
    else
        if [ -f "$GAME_DIR/BepInEx/core/BepInEx.dll" ]; then
            default_remove=0
            { [ -f "$GAME_DIR/BepInEx/$MARKER" ] || [ -f "$GAME_DIR/BepInEx/$OLD_MARKER" ]; } && default_remove=1
            remove_bep=0
            if [ "$REMOVE_BEPINEX" -eq 1 ]; then
                remove_bep=1
            elif [ "$ASSUME_YES" -eq 0 ] && ask_yes "$(t rmbep)" "$default_remove"; then
                remove_bep=1
            fi
            if [ "$remove_bep" -eq 1 ]; then
                rm -f "$GAME_DIR/run_bepinex.sh" "$GAME_DIR/libdoorstop.so" "$GAME_DIR/.doorstop_version"
                if [ -f "$GAME_DIR/changelog.txt" ] && grep -qi 'bepinex\|doorstop' "$GAME_DIR/changelog.txt"; then rm -f "$GAME_DIR/changelog.txt"; fi
                if [ "$keep" -eq 1 ] && [ -n "$(find "$GAME_DIR/BepInEx/plugins" "$GAME_DIR/BepInEx/SaveHistory" -mindepth 1 -maxdepth 1 2>/dev/null | head -1)" ]; then
                    find "$GAME_DIR/BepInEx" -mindepth 1 -maxdepth 1 ! -name plugins ! -name SaveHistory -exec rm -rf {} +
                else
                    rm -rf "$GAME_DIR/BepInEx"
                fi
                say "$(t bep_removed)"
            fi
        fi
        if [ "$LAUNCH_OPTIONS" -eq 0 ]; then
            log "launch options left alone (--no-launch-option)"
        elif [ "$(launch_options_any)" = no ]; then
            log "no launch option to take out"
        else
            rc=0; with_steam_closed remove || rc=$?
            case "$rc" in
                0) say "$(t lo_removed)" ;;
                3) warn "$(t steam_slow_remove)
$(t lo_manual_remove)" ;;
                *) warn "$(t lo_manual_remove)" ;;
            esac
        fi
    fi
    finish_message "$(t undone)"
fi
