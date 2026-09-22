using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace DragNWash.Installer
{
    // The installer's own words, in English, Japanese and Chinese. The mod's words
    // (its name, choice labels) come from mod-install.json. Log lines stay English
    // so they read the same in a bug report.
    internal static class Strings
    {
        internal enum Key
        {
            Title,
            GameFolder,
            Browse,
            NotFound,
            Running,
            NoPayload,
            BadManifest,
            BepInExHash,
            Install,
            Update,
            Uninstall,
            KeepData,
            AlsoBepInEx,
            Installed,
            Uninstalled,
            StatusBepInEx,
            StatusMod,
            Yes,
            No,
            Language,
            Website,
            Working,
            SteamLaunchHint,
            Action,
            NothingToUninstall,
            InstallButton,
            UpdateButton,
            UninstallButton,
            Close,
            InstallWill,
            UninstallWill,
            SmartScreenHint,
            ListAnd,
            ListComma,
            PlanDownloadBepInEx,
            PlanHaveBepInEx,
            PlanFrameworkAndMod,
            PlanMod,
            PlanKeepNewerFramework,
            PlanSet,
            PlanNothingElse,
            PlanRemovePlugin,
            PlanRemovePluginKeep,
            PlanRemoveConfig,
            PlanRemoveFramework,
            PlanKeepFramework,
            PlanRemoveSaveHistory,
            PlanRemoveBepInEx,
            PlanRemoveBepInExKeep,
            PlanKeepBepInEx,
            PlanKeepBepInExUsed,
            Cancel,
            Downloading,
            Cancelled,
            Stopped,
            DownloadFailed,
            DownloadFailedHelp,
            CannotWrite,
            CannotWriteHelp,
            BadZip,
            BadZipHelp,
            SomethingWrong,
            SomethingWrongHelp,
            ShowDetails,
            HideDetails,
            CopyDetails,
            Retry,
        }

        private static readonly Dictionary<string, Dictionary<Key, string>> All = new Dictionary<string, Dictionary<Key, string>>
        {
            ["en"] = new Dictionary<Key, string>
            {
                [Key.Title] = "{0} installer",
                [Key.GameFolder] = "Game folder",
                [Key.Browse] = "Browse...",
                [Key.NotFound] = "Drag'n Wash was not found. Choose the game folder (the one with DragNWash.exe).",
                [Key.Running] = "Close the game first.",
                [Key.NoPayload] = "The mod's files are missing next to the installer. Extract the whole zip first, then run Install.exe from the extracted folder.",
                [Key.BadManifest] = "mod-install.json next to the installer could not be read.",
                [Key.BepInExHash] = "The BepInEx download is corrupt or has been tampered with (SHA-256 mismatch). Nothing was installed.",
                [Key.Install] = "Install",
                [Key.Update] = "Update",
                [Key.Uninstall] = "Uninstall",
                [Key.KeepData] = "Keep save history and the mod's working files",
                [Key.AlsoBepInEx] = "Also remove BepInEx when no other mod is left",
                [Key.Installed] = "Done. Start the game from Steam.",
                [Key.Uninstalled] = "Uninstalled.",
                [Key.StatusBepInEx] = "BepInEx",
                [Key.StatusMod] = "Installed version",
                [Key.Yes] = "installed",
                [Key.No] = "not installed",
                [Key.Language] = "Installer language",
                [Key.Website] = "Website",
                [Key.Working] = "Working...",
                [Key.SteamLaunchHint] = "Mods can also be uninstalled in the game: Options → Mods.",
                [Key.Action] = "Action",
                [Key.NothingToUninstall] = "(nothing to uninstall)",
                [Key.InstallButton] = "&Install",
                [Key.UpdateButton] = "&Update",
                [Key.UninstallButton] = "&Uninstall",
                [Key.Close] = "&Close",
                [Key.InstallWill] = "Install will:",
                [Key.UninstallWill] = "Uninstall will:",
                [Key.SmartScreenHint] = "Windows SmartScreen or Defender may warn about Install.exe: it is not code-signed. The list above is all it does.",
                [Key.ListAnd] = " and ",
                [Key.ListComma] = ", ",
                [Key.PlanDownloadBepInEx] = "Download BepInEx {0} from {1} and check its SHA-256",
                [Key.PlanHaveBepInEx] = "Use the BepInEx that is already in the game folder",
                [Key.PlanFrameworkAndMod] = "Put Drag'n Wash ModFramework {0} and {1} into BepInEx\\plugins (files you added there are kept)",
                [Key.PlanMod] = "Put {0} into BepInEx\\plugins (files you added there are kept)",
                [Key.PlanKeepNewerFramework] = "Keep Drag'n Wash ModFramework {0}, which is newer than the one in this zip",
                [Key.PlanSet] = "Set {0} = {1} in BepInEx\\config\\{2}",
                [Key.PlanNothingElse] = "Nothing else is downloaded or changed.",
                [Key.PlanRemovePlugin] = "Remove BepInEx\\plugins\\{0}",
                [Key.PlanRemovePluginKeep] = "Remove BepInEx\\plugins\\{0}, keeping {1}",
                [Key.PlanRemoveConfig] = "Remove BepInEx\\config\\{0}",
                [Key.PlanRemoveFramework] = "Remove Drag'n Wash ModFramework too (no other mod uses it)",
                [Key.PlanKeepFramework] = "Keep Drag'n Wash ModFramework (other mods use it: {0})",
                [Key.PlanRemoveSaveHistory] = "Remove the save history in BepInEx\\SaveHistory",
                [Key.PlanRemoveBepInEx] = "Remove BepInEx",
                [Key.PlanRemoveBepInExKeep] = "Remove BepInEx; your kept data stays in the BepInEx folder",
                [Key.PlanKeepBepInEx] = "Keep BepInEx",
                [Key.PlanKeepBepInExUsed] = "Keep BepInEx (other mods or patchers use it)",
                [Key.Cancel] = "&Cancel",
                [Key.Downloading] = "Downloading BepInEx {0} from {1}",
                [Key.Cancelled] = "Cancelled. Nothing was changed in the game folder.",
                [Key.Stopped] = "Stopped.",
                [Key.DownloadFailed] = "BepInEx could not be downloaded.",
                [Key.DownloadFailedHelp] = "Check the internet connection and try again. Nothing was changed in the game folder.",
                [Key.CannotWrite] = "Could not write to the game folder.",
                [Key.CannotWriteHelp] = "A file there may be in use or read-only. Close the game and anything else using its folder, then try again.",
                [Key.BadZip] = "The download is not a valid zip.",
                [Key.BadZipHelp] = "Try again. If it keeps happening, copy the details into a bug report.",
                [Key.SomethingWrong] = "Something went wrong.",
                [Key.SomethingWrongHelp] = "Copy the details into a bug report on the mod's website.",
                [Key.ShowDetails] = "Show details",
                [Key.HideDetails] = "Hide details",
                [Key.CopyDetails] = "Copy &details",
                [Key.Retry] = "&Retry",
            },
            ["ja"] = new Dictionary<Key, string>
            {
                [Key.Title] = "{0} インストーラー",
                [Key.GameFolder] = "ゲームのフォルダー",
                [Key.Browse] = "参照...",
                [Key.NotFound] = "Drag'n Wash が見つかりません。ゲームのフォルダー（DragNWash.exe がある場所）を選んでください。",
                [Key.Running] = "先にゲームを終了してください。",
                [Key.NoPayload] = "インストーラーの隣に Mod のファイルがありません。zip を丸ごと展開してから、展開したフォルダーの Install.exe を実行してください。",
                [Key.BadManifest] = "インストーラーの隣にある mod-install.json を読めませんでした。",
                [Key.BepInExHash] = "ダウンロードした BepInEx が壊れているか、改ざんされています（SHA-256 が一致しません）。何もインストールしていません。",
                [Key.Install] = "インストール",
                [Key.Update] = "更新",
                [Key.Uninstall] = "アンインストール",
                [Key.KeepData] = "セーブ履歴と Mod の作業ファイルは残す",
                [Key.AlsoBepInEx] = "ほかの Mod が残っていなければ BepInEx も削除する",
                [Key.Installed] = "完了しました。Steam からゲームを起動してください。",
                [Key.Uninstalled] = "アンインストールしました。",
                [Key.StatusBepInEx] = "BepInEx",
                [Key.StatusMod] = "入っているバージョン",
                [Key.Yes] = "導入済み",
                [Key.No] = "未導入",
                [Key.Language] = "インストーラーの言語",
                [Key.Website] = "Web サイト",
                [Key.Working] = "処理中...",
                [Key.SteamLaunchHint] = "Mod はゲームの中（Options → Mods）からもアンインストールできます。",
                [Key.Action] = "操作",
                [Key.NothingToUninstall] = "（アンインストールするものはありません）",
                [Key.InstallButton] = "インストール(&I)",
                [Key.UpdateButton] = "更新(&U)",
                [Key.UninstallButton] = "アンインストール(&U)",
                [Key.Close] = "閉じる(&C)",
                [Key.InstallWill] = "インストールで行うこと：",
                [Key.UninstallWill] = "アンインストールで行うこと：",
                [Key.SmartScreenHint] = "Install.exe はコード署名がないため、Windows SmartScreen や Defender が警告することがあります。行うのは上の一覧にあることだけです。",
                [Key.ListAnd] = " と ",
                [Key.ListComma] = "、",
                [Key.PlanDownloadBepInEx] = "BepInEx {0} を {1} からダウンロードし、SHA-256 を確認する",
                [Key.PlanHaveBepInEx] = "ゲームのフォルダーにすでにある BepInEx を使う",
                [Key.PlanFrameworkAndMod] = "Drag'n Wash ModFramework {0} と {1} を BepInEx\\plugins に配置する（自分で追加したファイルはそのまま残す）",
                [Key.PlanMod] = "{0} を BepInEx\\plugins に配置する（自分で追加したファイルはそのまま残す）",
                [Key.PlanKeepNewerFramework] = "Drag'n Wash ModFramework {0} はこの zip のものより新しいので、そのまま残す",
                [Key.PlanSet] = "「{0}」を「{1}」に設定する（BepInEx\\config\\{2}）",
                [Key.PlanNothingElse] = "これ以外のダウンロードや変更は行いません。",
                [Key.PlanRemovePlugin] = "BepInEx\\plugins\\{0} を削除する",
                [Key.PlanRemovePluginKeep] = "BepInEx\\plugins\\{0} を削除する（{1} は残す）",
                [Key.PlanRemoveConfig] = "BepInEx\\config\\{0} を削除する",
                [Key.PlanRemoveFramework] = "Drag'n Wash ModFramework も削除する（ほかに使っている Mod がない）",
                [Key.PlanKeepFramework] = "Drag'n Wash ModFramework は残す（ほかの Mod が使っている：{0}）",
                [Key.PlanRemoveSaveHistory] = "BepInEx\\SaveHistory のセーブ履歴を削除する",
                [Key.PlanRemoveBepInEx] = "BepInEx を削除する",
                [Key.PlanRemoveBepInExKeep] = "BepInEx を削除する（残すデータは BepInEx フォルダーに置いたまま）",
                [Key.PlanKeepBepInEx] = "BepInEx は残す",
                [Key.PlanKeepBepInExUsed] = "BepInEx は残す（ほかの Mod かパッチャーが使っている）",
                [Key.Cancel] = "キャンセル(&C)",
                [Key.Downloading] = "BepInEx {0} を {1} からダウンロード中",
                [Key.Cancelled] = "中止しました。ゲームのフォルダーは何も変わっていません。",
                [Key.Stopped] = "中断しました。",
                [Key.DownloadFailed] = "BepInEx をダウンロードできませんでした。",
                [Key.DownloadFailedHelp] = "インターネット接続を確認して、もう一度試してください。ゲームのフォルダーは何も変わっていません。",
                [Key.CannotWrite] = "ゲームのフォルダーに書き込めませんでした。",
                [Key.CannotWriteHelp] = "ファイルが使用中か、読み取り専用になっている可能性があります。ゲームと、そのフォルダーを使っているほかのプログラムを閉じてから、もう一度試してください。",
                [Key.BadZip] = "ダウンロードしたファイルが正しい zip ではありません。",
                [Key.BadZipHelp] = "もう一度試してください。何度も失敗する場合は、詳細をコピーしてバグ報告に貼ってください。",
                [Key.SomethingWrong] = "問題が発生しました。",
                [Key.SomethingWrongHelp] = "詳細をコピーして、Mod の Web サイトでバグ報告に貼ってください。",
                [Key.ShowDetails] = "詳細を表示",
                [Key.HideDetails] = "詳細を隠す",
                [Key.CopyDetails] = "詳細をコピー(&D)",
                [Key.Retry] = "再試行(&R)",
            },
            ["zh"] = new Dictionary<Key, string>
            {
                [Key.Title] = "{0} 安装器",
                [Key.GameFolder] = "游戏文件夹",
                [Key.Browse] = "浏览...",
                [Key.NotFound] = "未找到 Drag'n Wash。请选择游戏文件夹（包含 DragNWash.exe 的文件夹）。",
                [Key.Running] = "请先关闭游戏。",
                [Key.NoPayload] = "安装器旁边缺少模组文件。请先完整解压 zip，再运行解压后文件夹中的 Install.exe。",
                [Key.BadManifest] = "无法读取安装器旁边的 mod-install.json。",
                [Key.BepInExHash] = "下载的 BepInEx 已损坏或被篡改（SHA-256 不一致）。未安装任何内容。",
                [Key.Install] = "安装",
                [Key.Update] = "更新",
                [Key.Uninstall] = "卸载",
                [Key.KeepData] = "保留存档历史和模组的工作文件",
                [Key.AlsoBepInEx] = "没有其他模组时同时删除 BepInEx",
                [Key.Installed] = "完成。请从 Steam 启动游戏。",
                [Key.Uninstalled] = "已卸载。",
                [Key.StatusBepInEx] = "BepInEx",
                [Key.StatusMod] = "已安装的版本",
                [Key.Yes] = "已安装",
                [Key.No] = "未安装",
                [Key.Language] = "安装器语言",
                [Key.Website] = "网站",
                [Key.Working] = "处理中...",
                [Key.SteamLaunchHint] = "也可以在游戏中（Options → Mods）卸载模组。",
                [Key.Action] = "操作",
                [Key.NothingToUninstall] = "（没有可卸载的内容）",
                [Key.InstallButton] = "安装(&I)",
                [Key.UpdateButton] = "更新(&U)",
                [Key.UninstallButton] = "卸载(&U)",
                [Key.Close] = "关闭(&C)",
                [Key.InstallWill] = "安装将会：",
                [Key.UninstallWill] = "卸载将会：",
                [Key.SmartScreenHint] = "由于 Install.exe 没有代码签名，Windows SmartScreen 或 Defender 可能会发出警告。它只会执行上面列出的操作。",
                [Key.ListAnd] = " 和 ",
                [Key.ListComma] = "、",
                [Key.PlanDownloadBepInEx] = "从 {1} 下载 BepInEx {0} 并校验 SHA-256",
                [Key.PlanHaveBepInEx] = "使用游戏文件夹中已有的 BepInEx",
                [Key.PlanFrameworkAndMod] = "将 Drag'n Wash ModFramework {0} 和 {1} 放入 BepInEx\\plugins（你自行添加的文件会保留）",
                [Key.PlanMod] = "将 {0} 放入 BepInEx\\plugins（你自行添加的文件会保留）",
                [Key.PlanKeepNewerFramework] = "保留 Drag'n Wash ModFramework {0}（其版本比此 zip 中的新）",
                [Key.PlanSet] = "将“{0}”设为“{1}”（BepInEx\\config\\{2}）",
                [Key.PlanNothingElse] = "不会下载或更改其他任何内容。",
                [Key.PlanRemovePlugin] = "删除 BepInEx\\plugins\\{0}",
                [Key.PlanRemovePluginKeep] = "删除 BepInEx\\plugins\\{0}，保留 {1}",
                [Key.PlanRemoveConfig] = "删除 BepInEx\\config\\{0}",
                [Key.PlanRemoveFramework] = "同时删除 Drag'n Wash ModFramework（没有其他模组使用它）",
                [Key.PlanKeepFramework] = "保留 Drag'n Wash ModFramework（其他模组在使用：{0}）",
                [Key.PlanRemoveSaveHistory] = "删除 BepInEx\\SaveHistory 中的存档历史",
                [Key.PlanRemoveBepInEx] = "删除 BepInEx",
                [Key.PlanRemoveBepInExKeep] = "删除 BepInEx（保留的数据仍留在 BepInEx 文件夹中）",
                [Key.PlanKeepBepInEx] = "保留 BepInEx",
                [Key.PlanKeepBepInExUsed] = "保留 BepInEx（其他模组或补丁程序在使用）",
                [Key.Cancel] = "取消(&C)",
                [Key.Downloading] = "正在从 {1} 下载 BepInEx {0}",
                [Key.Cancelled] = "已取消。游戏文件夹没有任何更改。",
                [Key.Stopped] = "已停止。",
                [Key.DownloadFailed] = "无法下载 BepInEx。",
                [Key.DownloadFailedHelp] = "请检查网络连接后重试。游戏文件夹没有任何更改。",
                [Key.CannotWrite] = "无法写入游戏文件夹。",
                [Key.CannotWriteHelp] = "文件夹中的文件可能正在被使用或为只读。请关闭游戏及其他正在使用该文件夹的程序，然后重试。",
                [Key.BadZip] = "下载的文件不是有效的 zip。",
                [Key.BadZipHelp] = "请重试。如果反复出现，请复制详情并提交错误报告。",
                [Key.SomethingWrong] = "出现了问题。",
                [Key.SomethingWrongHelp] = "请复制详情，在模组网站上提交错误报告。",
                [Key.ShowDetails] = "显示详情",
                [Key.HideDetails] = "隐藏详情",
                [Key.CopyDetails] = "复制详情(&D)",
                [Key.Retry] = "重试(&R)",
            },
        };

        internal static readonly string[] Languages = { "en", "ja", "zh" };

        internal static string Current = Detect();

        internal static string Detect()
        {
            string name = CultureInfo.CurrentUICulture.Name;
            if (name.StartsWith("ja")) return "ja";
            if (name.StartsWith("zh")) return "zh";
            return "en";
        }

        internal static string Get(Key key, params object[] args)
        {
            string text = All.TryGetValue(Current, out var table) && table.TryGetValue(key, out string s) ? s : All["en"][key];
            return args.Length == 0 ? text : string.Format(text, args);
        }

        // For bug reports and the log, which stay English whatever the window shows.
        internal static string English(Key key)
        {
            return All["en"][key];
        }

        // The windows' font for the installer language. Windows' own font for another
        // script draws the missing characters from a fallback font it did not measure
        // them with, so a line that should wrap ran past the right edge instead (Chinese
        // on Japanese Windows). Windows in the language itself keeps its own font.
        internal static Font UiFont()
        {
            Font system = SystemFonts.MessageBoxFont;
            string ui = CultureInfo.CurrentUICulture.Name;
            string family =
                Current == "ja" && !ui.StartsWith("ja") ? "Yu Gothic UI" :
                Current == "zh" && !(ui == "zh-CN" || ui == "zh-SG" || ui.StartsWith("zh-Hans")) ? "Microsoft YaHei UI" :
                null;
            if (family == null)
            {
                return system;
            }
            var font = new Font(family, system.SizeInPoints);
            if (font.Name == family)
            {
                return font;
            }
            // Not installed (Windows 7 has no Yu Gothic UI): Windows' own font as before.
            font.Dispose();
            return system;
        }

        internal static string LanguageName(string code)
        {
            switch (code)
            {
                case "ja": return "日本語";
                case "zh": return "中文";
                default: return "English";
            }
        }
    }
}
