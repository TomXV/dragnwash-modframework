using System.Collections.Generic;
using System.Globalization;

namespace DragNWash.CodeGraph
{
    // The window's own words in English, Japanese and Chinese (as the installer and the
    // crash report window), in Windows' language. The Bridge's page it shows has its own.
    internal static class Strings
    {
        internal enum Key
        {
            Title, NoWebView2, TookOver,
            Waiting, NotSignedIn, NotRunning, NoToken, TokenRefused, OtherReason, NoCode,
            HowStart, HowToken, RetryNow, TriedOne, TriedMany, NextIn, TryingNow, WaitsForRetry, NothingLeaves,
        }

        private static readonly Dictionary<string, Dictionary<Key, string>> All = new Dictionary<string, Dictionary<Key, string>>
        {
            ["en"] = new Dictionary<Key, string>
            {
                [Key.Title] = "Drag'n Wash Code Graph",
                [Key.NoWebView2] = "This window needs the Microsoft Edge WebView2 Runtime, which comes with Windows 10 and 11 but is missing here.\n\nSet [Bridge] OpenPageIn to Browser in the game's Mods screen to use the browser instead, or install the WebView2 Runtime from Microsoft.",
                [Key.TookOver] = "The Code Graph window that was open did not answer; this one took over.",
                [Key.Waiting] = "Waiting for the game.",
                [Key.NotSignedIn] = "The game is there, but this window may not sign in.",
                [Key.NotRunning] = "Nothing answers at 127.0.0.1:{0}: the game is not running, or the Bridge is off.",
                [Key.NoToken] = "This window has no token yet: the Bridge writes bridge-token.txt the first time it runs in the game.",
                [Key.TokenRefused] = "The Bridge refused this window's token ({0}). The token was renewed in the game, and this window still has the old one.",
                [Key.OtherReason] = "The game did not give this window a sign-in code: {0}.",
                [Key.NoCode] = "the answer had no code",
                [Key.HowStart] = "Start Drag'n Wash with the developer tools and the Bridge on (F1\u00A0→\u00A0Bridge). This window signs in by itself when the game is there.",
                [Key.HowToken] = "In the game: F1\u00A0→\u00A0Bridge shows the token in use; this window reads it from bridge-token.txt. Press Graph again in the Inspector, or Retry now once the Bridge is on.",
                [Key.RetryNow] = "Retry now",
                [Key.TriedOne] = "Tried {0} time · last {1}",
                [Key.TriedMany] = "Tried {0} times · last {1}",
                [Key.NextIn] = "next in {0} s",
                [Key.TryingNow] = "trying now",
                [Key.WaitsForRetry] = "waits for Retry",
                [Key.NothingLeaves] = "Nothing leaves this computer: this window talks only to the game at 127.0.0.1, and shows only what is on this computer.",
            },
            ["ja"] = new Dictionary<Key, string>
            {
                [Key.Title] = "Drag'n Wash コードグラフ",
                [Key.NoWebView2] = "このウィンドウには Microsoft Edge WebView2 ランタイムが必要です。Windows 10 と 11 には標準で入っていますが、このコンピューターには見つかりません。\n\nゲームの Mods 画面で [Bridge] OpenPageIn を Browser にすると、代わりにブラウザーで開きます。または、Microsoft から WebView2 ランタイムをインストールしてください。",
                [Key.TookOver] = "開いていたコードグラフのウィンドウが応答しなかったため、このウィンドウが代わりに開きました。",
                [Key.Waiting] = "ゲームを待っています。",
                [Key.NotSignedIn] = "ゲームは見つかりましたが、サインインできません。",
                [Key.NotRunning] = "127.0.0.1:{0} から応答がありません。ゲームが起動していないか、Bridge がオフです。",
                [Key.NoToken] = "このウィンドウにはまだトークンがありません。bridge-token.txt は、ゲーム内で Bridge が初めて動いたときに作られます。",
                [Key.TokenRefused] = "Bridge がこのウィンドウのトークンを拒否しました（{0}）。ゲーム側でトークンが新しくなりましたが、このウィンドウは古いトークンのままです。",
                [Key.OtherReason] = "ゲームからサインイン用のコードを受け取れませんでした（{0}）。",
                [Key.NoCode] = "応答にコードが含まれていません",
                [Key.HowStart] = "開発者ツールと Bridge をオンにして Drag'n Wash を起動してください（F1\u00A0→\u00A0Bridge）。ゲームが見つかると、このウィンドウは自動でサインインします。",
                [Key.HowToken] = "使用中のトークンはゲームの F1\u00A0→\u00A0Bridge で確認できます。このウィンドウは bridge-token.txt からトークンを読み込みます。Inspector で Graph をもう一度押すか、Bridge がオンになってから「今すぐ再試行」を押してください。",
                [Key.RetryNow] = "今すぐ再試行",
                [Key.TriedOne] = "{0} 回試行 · 前回 {1}",
                [Key.TriedMany] = "{0} 回試行 · 前回 {1}",
                [Key.NextIn] = "次は {0} 秒後",
                [Key.TryingNow] = "試行中",
                [Key.WaitsForRetry] = "再試行待ち",
                [Key.NothingLeaves] = "このコンピューターの外には何も送信しません。このウィンドウが通信するのは 127.0.0.1 のゲームだけで、表示するのもこのコンピューター上の情報だけです。",
            },
            ["zh"] = new Dictionary<Key, string>
            {
                [Key.Title] = "Drag'n Wash 代码图",
                [Key.NoWebView2] = "此窗口需要 Microsoft Edge WebView2 运行时。Windows 10 和 11 自带该组件，但在这台电脑上没有找到。\n\n在游戏的 Mods 界面中将 [Bridge] OpenPageIn 设为 Browser，即可改用浏览器打开；也可以从 Microsoft 安装 WebView2 运行时。",
                [Key.TookOver] = "之前打开的代码图窗口没有响应，已由此窗口接替。",
                [Key.Waiting] = "正在等待游戏。",
                [Key.NotSignedIn] = "已找到游戏，但此窗口无法登录。",
                [Key.NotRunning] = "127.0.0.1:{0} 没有响应：游戏未运行，或 Bridge 已关闭。",
                [Key.NoToken] = "此窗口还没有令牌：Bridge 首次在游戏中运行时会创建 bridge-token.txt。",
                [Key.TokenRefused] = "Bridge 拒绝了此窗口的令牌（{0}）。游戏中的令牌已更新，而此窗口仍在使用旧令牌。",
                [Key.OtherReason] = "未能从游戏获得登录代码（{0}）。",
                [Key.NoCode] = "响应中没有代码",
                [Key.HowStart] = "请在开启开发者工具和 Bridge 的状态下启动 Drag'n Wash（F1\u00A0→\u00A0Bridge）。找到游戏后，此窗口会自动登录。",
                [Key.HowToken] = "游戏中的 F1\u00A0→\u00A0Bridge 会显示正在使用的令牌；此窗口从 bridge-token.txt 读取令牌。请在 Inspector 中再按一次 Graph，或在 Bridge 开启后按“立即重试”。",
                [Key.RetryNow] = "立即重试",
                [Key.TriedOne] = "已尝试 {0} 次 · 上次 {1}",
                [Key.TriedMany] = "已尝试 {0} 次 · 上次 {1}",
                [Key.NextIn] = "{0} 秒后重试",
                [Key.TryingNow] = "正在重试",
                [Key.WaitsForRetry] = "等待手动重试",
                [Key.NothingLeaves] = "不会向这台电脑以外发送任何内容：此窗口只与 127.0.0.1 上的游戏通信，也只显示这台电脑上的内容。",
            },
        };

        internal static readonly string Current = Detect(CultureInfo.CurrentUICulture.Name);

        internal static string Detect(string culture)
        {
            culture = culture ?? "";
            if (culture.StartsWith("ja")) return "ja";
            if (culture.StartsWith("zh")) return "zh";
            return "en";
        }

        internal static string Get(Key key) => All.TryGetValue(Current, out var t) && t.TryGetValue(key, out string s) ? s : All["en"][key];

        internal static string Get(Key key, params object[] args) => string.Format(CultureInfo.InvariantCulture, Get(key), args);

        // The page's fonts, with one that has the language's characters first.
        internal static string Fonts => Current == "ja" ? "'Yu Gothic UI','Segoe UI',system-ui,sans-serif"
            : Current == "zh" ? "'Microsoft YaHei UI','Segoe UI',system-ui,sans-serif"
            : "system-ui,'Segoe UI',sans-serif";
    }
}
