using System.Collections.Generic;
using System.IO;
using System.Linq;
using DragNWash.ModFramework.Diagnostics;

namespace DragNWash.CrashReporter
{
    // The window's words in English, Japanese and Chinese (as the installer).
    internal static class Strings
    {
        internal enum Key
        {
            WindowTitle, Headline, Subheadline, WhatHappened, WhatToDo, Details, DumpNote, OpenFolder, CopyReport, Copied, Close, Privacy,
            CopyFailed, OpenFailed, NoWindow, NotWritten,
        }

        private static readonly Dictionary<string, Dictionary<Key, string>> All = new Dictionary<string, Dictionary<Key, string>>
        {
            ["en"] = new Dictionary<Key, string>
            {
                [Key.WindowTitle] = "Drag'n Wash - crash report",
                [Key.Headline] = "Oops! The kobold slipped!",
                [Key.Subheadline] = "Drag'n Wash closed unexpectedly. Here is what happened and what you can do about it.",
                [Key.WhatHappened] = "What happened",
                [Key.WhatToDo] = "What you can do",
                [Key.Details] = "Details",
                [Key.DumpNote] = "The report includes a memory dump. It holds part of the game's memory: share it only privately with whoever looks into the problem.",
                [Key.OpenFolder] = "&Open report folder",
                [Key.CopyReport] = "&Copy report (text only)",
                [Key.Copied] = "Copied",
                [Key.Close] = "Close",
                [Key.Privacy] = "Made by Drag'n Wash ModFramework on this PC. Nothing was sent anywhere.",
                [Key.CopyFailed] = "Could not copy: the clipboard is busy. Open the report folder and copy report.txt instead.",
                [Key.OpenFailed] = "Could not open the folder. It is {0} inside the game's folder.",
                [Key.NoWindow] = "Drag'n Wash closed unexpectedly, but the crash report window could not open.\n\nThe report is in the game's folder under BepInEx\\CrashReports. What went wrong with the window is in %TEMP%\\DragNWash.CrashReporter.log.",
                [Key.NotWritten] = "Drag'n Wash closed unexpectedly, but the crash report could not be written.\n\nThe session record is in the game's folder under BepInEx\\CrashReports\\session.log, and the game will try again the next time it starts. What went wrong is in %TEMP%\\DragNWash.CrashReporter.log.",
            },
            ["ja"] = new Dictionary<Key, string>
            {
                [Key.WindowTitle] = "Drag'n Wash - クラッシュレポート",
                [Key.Headline] = "おっと、コボルトが滑ってしまったようです！",
                [Key.Subheadline] = "Drag'n Wash が予期せず終了しました。何が起きたかと、できることをまとめました。",
                [Key.WhatHappened] = "何が起きたか",
                [Key.WhatToDo] = "できること",
                [Key.Details] = "詳しい情報",
                [Key.DumpNote] = "レポートにはメモリダンプが入っています。ゲームのメモリの一部が含まれるので、原因を調べる人にだけ個別に渡してください。",
                [Key.OpenFolder] = "レポートのフォルダーを開く(&O)",
                [Key.CopyReport] = "レポートをコピー（テキストのみ）(&C)",
                [Key.Copied] = "コピーしました",
                [Key.Close] = "閉じる",
                [Key.Privacy] = "Drag'n Wash ModFramework がこの PC の中で作りました。どこにも送信していません。",
                [Key.CopyFailed] = "コピーできませんでした。クリップボードが使用中です。代わりにレポートのフォルダーを開き、report.txt をコピーしてください。",
                [Key.OpenFailed] = "フォルダーを開けませんでした。場所はゲームのフォルダーの中の {0} です。",
                [Key.NoWindow] = "Drag'n Wash が予期せず終了しましたが、クラッシュレポートのウィンドウを開けませんでした。\n\nレポートはゲームのフォルダーの BepInEx\\CrashReports にあります。ウィンドウを開けなかった理由は %TEMP%\\DragNWash.CrashReporter.log に記録しています。",
                [Key.NotWritten] = "Drag'n Wash が予期せず終了しましたが、クラッシュレポートを書けませんでした。\n\nセッションの記録はゲームのフォルダーの BepInEx\\CrashReports\\session.log にあり、次にゲームを起動したときにもう一度レポートを書きます。書けなかった理由は %TEMP%\\DragNWash.CrashReporter.log に記録しています。",
            },
            ["zh"] = new Dictionary<Key, string>
            {
                [Key.WindowTitle] = "Drag'n Wash - 崩溃报告",
                [Key.Headline] = "哎呀，狗头人滑倒了！",
                [Key.Subheadline] = "Drag'n Wash 意外关闭了。以下是发生的情况，以及你可以采取的措施。",
                [Key.WhatHappened] = "发生了什么",
                [Key.WhatToDo] = "你可以做什么",
                [Key.Details] = "详细信息",
                [Key.DumpNote] = "报告中包含内存转储，其中含有游戏的部分内存。请只私下发给负责调查问题的人。",
                [Key.OpenFolder] = "打开报告文件夹(&O)",
                [Key.CopyReport] = "复制报告（仅文本）(&C)",
                [Key.Copied] = "已复制",
                [Key.Close] = "关闭",
                [Key.Privacy] = "由 Drag'n Wash ModFramework 在这台电脑上生成，没有发送到任何地方。",
                [Key.CopyFailed] = "无法复制：剪贴板正被其他程序占用。请改为打开报告文件夹，复制其中的 report.txt。",
                [Key.OpenFailed] = "无法打开文件夹。报告位于游戏文件夹内的 {0}。",
                [Key.NoWindow] = "Drag'n Wash 意外关闭了，但崩溃报告窗口无法打开。\n\n报告位于游戏文件夹内的 BepInEx\\CrashReports。窗口无法打开的原因记录在 %TEMP%\\DragNWash.CrashReporter.log 中。",
                [Key.NotWritten] = "Drag'n Wash 意外关闭了，但无法写入崩溃报告。\n\n会话记录位于游戏文件夹内的 BepInEx\\CrashReports\\session.log，下次启动游戏时会再次尝试写入报告。无法写入的原因记录在 %TEMP%\\DragNWash.CrashReporter.log 中。",
            },
        };

        private static readonly Dictionary<string, Dictionary<CrashDiagnosis.Kind, string[]>> Kinds = new Dictionary<string, Dictionary<CrashDiagnosis.Kind, string[]>>
        {
            // { what happened, what to do (or null) }
            ["en"] = new Dictionary<CrashDiagnosis.Kind, string[]>
            {
                [CrashDiagnosis.Kind.Direct3D12Uploads] = new[]
                {
                    "The game crashed inside Unity while sending text or pictures to the graphics card. This is a known problem of this Unity version with Direct3D 12 (Unity issue UUM-140564).",
                    "Drag'n Wash ModFramework avoids its most common cause. If it keeps happening, add -force-d3d11 to the game's launch options in Steam (Properties → General → Launch options).",
                },
                [CrashDiagnosis.Kind.GraphicsDevice] = new[]
                {
                    "The graphics card stopped responding to the game (Direct3D 12). This often happens when switching away from the game in exclusive fullscreen.",
                    "Choose Fullscreen instead of Exclusive fullscreen in the game's options, or add -force-d3d11 to the game's launch options in Steam.",
                },
                [CrashDiagnosis.Kind.Freeze] = new[]
                {
                    "The game froze and was then closed.",
                    "A memory dump of the freeze was saved with the report. It helps the mods' authors find the cause.",
                },
                [CrashDiagnosis.Kind.Stopped] = new[]
                {
                    "The game stopped without reporting a crash. It may have been closed from the Task Manager, or the PC shut down or lost power.",
                    null,
                },
                [CrashDiagnosis.Kind.Crash] = new[]
                {
                    "The game crashed inside Unity.",
                    "If it keeps happening, open the report folder and send report.txt to the author of the mod you suspect, or to Drag'n Wash ModFramework.",
                },
            },
            ["ja"] = new Dictionary<CrashDiagnosis.Kind, string[]>
            {
                [CrashDiagnosis.Kind.Direct3D12Uploads] = new[]
                {
                    "文字や画像をグラフィックスカードに送る途中で、Unity の中でゲームが落ちました。この Unity のバージョンの、Direct3D 12 での既知の不具合です（Unity の不具合 UUM-140564）。",
                    "Drag'n Wash ModFramework は、そのいちばん多い原因を避けるようにしています。それでも続くときは、Steam でゲームの起動オプション（プロパティ → 一般 → 起動オプション）に -force-d3d11 を加えてください。",
                },
                [CrashDiagnosis.Kind.GraphicsDevice] = new[]
                {
                    "グラフィックスカードがゲームに応答しなくなりました（Direct3D 12）。排他的フルスクリーンのまま、ほかのウィンドウに切り替えたときによく起きます。",
                    "ゲームのオプションで、排他的フルスクリーンではなくフルスクリーンを選ぶか、Steam でゲームの起動オプションに -force-d3d11 を加えてください。",
                },
                [CrashDiagnosis.Kind.Freeze] = new[]
                {
                    "ゲームが固まり、そのあと閉じられました。",
                    "固まったときのメモリダンプをレポートと一緒に保存しました。Mod の作者が原因を探す手がかりになります。",
                },
                [CrashDiagnosis.Kind.Stopped] = new[]
                {
                    "ゲームはクラッシュを報告せずに止まりました。タスクマネージャーから閉じられたか、PC の電源が切れた可能性があります。",
                    null,
                },
                [CrashDiagnosis.Kind.Crash] = new[]
                {
                    "Unity の中でゲームが落ちました。",
                    "何度も起きるときは、レポートのフォルダーを開き、report.txt を怪しい Mod の作者か、Drag'n Wash ModFramework に送ってください。",
                },
            },
            ["zh"] = new Dictionary<CrashDiagnosis.Kind, string[]>
            {
                [CrashDiagnosis.Kind.Direct3D12Uploads] = new[]
                {
                    "游戏在向显卡发送文字或图片时，在 Unity 内部崩溃了。这是此 Unity 版本在 Direct3D 12 下的已知问题（Unity 问题 UUM-140564）。",
                    "Drag'n Wash ModFramework 会避开它最常见的原因。如果仍然发生，请在 Steam 中为游戏的启动选项（属性 → 通用 → 启动选项）加上 -force-d3d11。",
                },
                [CrashDiagnosis.Kind.GraphicsDevice] = new[]
                {
                    "显卡停止响应游戏（Direct3D 12）。这常发生在独占全屏下切换到其他窗口时。",
                    "请在游戏选项中选择全屏而不是独占全屏，或在 Steam 中为游戏的启动选项加上 -force-d3d11。",
                },
                [CrashDiagnosis.Kind.Freeze] = new[]
                {
                    "游戏卡住了，随后被关闭。",
                    "卡住时的内存转储已与报告一起保存，可以帮助 Mod 作者查找原因。",
                },
                [CrashDiagnosis.Kind.Stopped] = new[]
                {
                    "游戏在没有报告崩溃的情况下停止了。可能是被任务管理器关闭，或者电脑关机、断电。",
                    null,
                },
                [CrashDiagnosis.Kind.Crash] = new[]
                {
                    "游戏在 Unity 内部崩溃了。",
                    "如果反复发生，请打开报告文件夹，把 report.txt 发给你怀疑的 Mod 的作者，或发给 Drag'n Wash ModFramework。",
                },
            },
        };

        private static readonly Dictionary<string, Dictionary<CrashDiagnosis.Detail, string>> DetailNames = new Dictionary<string, Dictionary<CrashDiagnosis.Detail, string>>
        {
            ["en"] = new Dictionary<CrashDiagnosis.Detail, string>
            {
                [CrashDiagnosis.Detail.Ended] = "When", [CrashDiagnosis.Detail.Scene] = "Scene", [CrashDiagnosis.Detail.Played] = "Played for",
                [CrashDiagnosis.Detail.LastError] = "Last error", [CrashDiagnosis.Detail.Graphics] = "Graphics", [CrashDiagnosis.Detail.Report] = "Report",
            },
            ["ja"] = new Dictionary<CrashDiagnosis.Detail, string>
            {
                [CrashDiagnosis.Detail.Ended] = "日時", [CrashDiagnosis.Detail.Scene] = "シーン", [CrashDiagnosis.Detail.Played] = "プレイ時間",
                [CrashDiagnosis.Detail.LastError] = "最後のエラー", [CrashDiagnosis.Detail.Graphics] = "グラフィックス", [CrashDiagnosis.Detail.Report] = "レポート",
            },
            ["zh"] = new Dictionary<CrashDiagnosis.Detail, string>
            {
                [CrashDiagnosis.Detail.Ended] = "时间", [CrashDiagnosis.Detail.Scene] = "场景", [CrashDiagnosis.Detail.Played] = "游玩时长",
                [CrashDiagnosis.Detail.LastError] = "最后的错误", [CrashDiagnosis.Detail.Graphics] = "图形", [CrashDiagnosis.Detail.Report] = "报告",
            },
        };

        internal static string Current = Detect(System.Globalization.CultureInfo.CurrentUICulture.Name);

        // The window's language: the one the player sees the game in when this
        // window has words for it (locale.txt, which the core keeps beside the
        // session record, or else the session's last "language" note), else
        // Windows' (as passed with --lang, or the user's UI culture), else English.
        internal static void Choose(string crashReportsFolder, string sessionLanguage, string windowsLanguage)
        {
            string chosen = new[] { GameLanguage(crashReportsFolder), sessionLanguage, windowsLanguage, System.Globalization.CultureInfo.CurrentUICulture.Name }.FirstOrDefault(Has);
            Current = chosen != null ? Detect(chosen) : "en";
        }

        // The first line of locale.txt; null when missing, unreadable or "-" (not known).
        private static string GameLanguage(string crashReportsFolder)
        {
            try
            {
                if (string.IsNullOrEmpty(crashReportsFolder)) return null;
                string path = Path.Combine(crashReportsFolder, CrashReportWriter.LocaleFile);
                if (!File.Exists(path)) return null;
                string code = File.ReadLines(path).FirstOrDefault()?.Trim();
                return string.IsNullOrEmpty(code) || code == "-" ? null : code;
            }
            catch
            {
                return null;
            }
        }

        private static bool Has(string culture)
        {
            if (string.IsNullOrEmpty(culture)) return false;
            string code = Detect(culture);
            return code != "en" || culture.StartsWith("en");
        }

        internal static string Detect(string culture)
        {
            culture = culture ?? "";
            if (culture.StartsWith("ja")) return "ja";
            if (culture.StartsWith("zh")) return "zh";
            return "en";
        }

        internal static string Get(Key key) => All.TryGetValue(Current, out var t) && t.TryGetValue(key, out string s) ? s : All["en"][key];

        internal static string[] For(CrashDiagnosis.Kind kind) => Kinds.TryGetValue(Current, out var t) && t.TryGetValue(kind, out string[] s) ? s : Kinds["en"][kind];

        internal static string For(CrashDiagnosis.Detail detail) => DetailNames.TryGetValue(Current, out var t) && t.TryGetValue(detail, out string s) ? s : DetailNames["en"][detail];
    }
}
