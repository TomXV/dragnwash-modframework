using System.Collections.Generic;
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
            Failed,
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
                [Key.Failed] = "Failed: {0}",
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
                [Key.Failed] = "失敗しました：{0}",
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
                [Key.SmartScreenHint] = "Install.exe はコード署名がないため、Windows SmartScreen や Defender が警告することがあります。行うのは上の一覧だけです。",
                [Key.ListAnd] = " と ",
                [Key.ListComma] = "、",
                [Key.PlanDownloadBepInEx] = "BepInEx {0} を {1} からダウンロードし、SHA-256 を確認する",
                [Key.PlanHaveBepInEx] = "ゲームのフォルダーにすでにある BepInEx を使う",
                [Key.PlanFrameworkAndMod] = "Drag'n Wash ModFramework {0} と {1} を BepInEx\\plugins に置く（そこに自分で追加したファイルは残す）",
                [Key.PlanMod] = "{0} を BepInEx\\plugins に置く（そこに自分で追加したファイルは残す）",
                [Key.PlanKeepNewerFramework] = "Drag'n Wash ModFramework {0} はこの zip のものより新しいので、そのまま残す",
                [Key.PlanSet] = "BepInEx\\config\\{2} の {0} を {1} にする",
                [Key.PlanNothingElse] = "ほかには何もダウンロード・変更しません。",
                [Key.PlanRemovePlugin] = "BepInEx\\plugins\\{0} を削除する",
                [Key.PlanRemovePluginKeep] = "BepInEx\\plugins\\{0} を削除する（{1} は残す）",
                [Key.PlanRemoveConfig] = "BepInEx\\config\\{0} を削除する",
                [Key.PlanRemoveFramework] = "Drag'n Wash ModFramework も削除する（ほかに使う Mod がない）",
                [Key.PlanKeepFramework] = "Drag'n Wash ModFramework は残す（ほかの Mod が使っている：{0}）",
                [Key.PlanRemoveSaveHistory] = "BepInEx\\SaveHistory のセーブ履歴を削除する",
                [Key.PlanRemoveBepInEx] = "BepInEx を削除する",
                [Key.PlanRemoveBepInExKeep] = "BepInEx を削除する（残すデータは BepInEx フォルダーに残る）",
                [Key.PlanKeepBepInEx] = "BepInEx は残す",
                [Key.PlanKeepBepInExUsed] = "BepInEx は残す（ほかの Mod かパッチャーが使っている）",
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
                [Key.Failed] = "失败：{0}",
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
                [Key.SmartScreenHint] = "Install.exe 没有代码签名，Windows SmartScreen 或 Defender 可能会发出警告。它所做的只有上面列出的这些。",
                [Key.ListAnd] = " 和 ",
                [Key.ListComma] = "、",
                [Key.PlanDownloadBepInEx] = "从 {1} 下载 BepInEx {0} 并校验 SHA-256",
                [Key.PlanHaveBepInEx] = "使用游戏文件夹中已有的 BepInEx",
                [Key.PlanFrameworkAndMod] = "将 Drag'n Wash ModFramework {0} 和 {1} 放入 BepInEx\\plugins（保留你在那里添加的文件）",
                [Key.PlanMod] = "将 {0} 放入 BepInEx\\plugins（保留你在那里添加的文件）",
                [Key.PlanKeepNewerFramework] = "保留 Drag'n Wash ModFramework {0}，它比此 zip 中的更新",
                [Key.PlanSet] = "在 BepInEx\\config\\{2} 中将 {0} 设为 {1}",
                [Key.PlanNothingElse] = "不会下载或更改其他任何内容。",
                [Key.PlanRemovePlugin] = "删除 BepInEx\\plugins\\{0}",
                [Key.PlanRemovePluginKeep] = "删除 BepInEx\\plugins\\{0}，保留 {1}",
                [Key.PlanRemoveConfig] = "删除 BepInEx\\config\\{0}",
                [Key.PlanRemoveFramework] = "同时删除 Drag'n Wash ModFramework（没有其他模组使用它）",
                [Key.PlanKeepFramework] = "保留 Drag'n Wash ModFramework（其他模组在使用：{0}）",
                [Key.PlanRemoveSaveHistory] = "删除 BepInEx\\SaveHistory 中的存档历史",
                [Key.PlanRemoveBepInEx] = "删除 BepInEx",
                [Key.PlanRemoveBepInExKeep] = "删除 BepInEx；保留的数据留在 BepInEx 文件夹中",
                [Key.PlanKeepBepInEx] = "保留 BepInEx",
                [Key.PlanKeepBepInExUsed] = "保留 BepInEx（其他模组或补丁程序在使用）",
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
