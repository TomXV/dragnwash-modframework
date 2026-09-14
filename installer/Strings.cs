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
            ConfirmUninstall,
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
                [Key.ConfirmUninstall] = "Uninstall {0}?",
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
                [Key.ConfirmUninstall] = "{0} をアンインストールしますか？",
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
                [Key.ConfirmUninstall] = "要卸载 {0} 吗？",
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
