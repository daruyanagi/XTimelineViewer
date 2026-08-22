using System;
using System.IO;
using Xunit;

namespace XTimelineViewer.Tests
{
    /// <summary>
    /// キーボードショートカットの二重管理（WebView2 内の注入 JS と XAML の KeyboardAccelerator）の
    /// 不整合（ドリフト）を検出する（#229）。
    ///
    /// 「全フォーカスで効くべきショートカット（アプリ横断系）」は、
    ///   - WebView2 にフォーカスがあるとき … 注入 JS（KeyboardShortcutScript）
    ///   - メインウィンドウ/検索ボックス等にフォーカスがあるとき … XAML アクセラレータ
    /// の両経路に定義されていなければ、特定のフォーカス状態でだけ効かなくなる（#225 / #227 / #228）。
    ///
    /// 新しいショートカットを足したときの片側追加漏れを CI で自動検出するため、
    /// 両ソース（MainWindow.xaml / Scripts/KeyboardShortcut.js）を文字列スキャンして照合する。
    /// WebView2 内コンテンツ操作（Ctrl+↑↓ / Ctrl+R・B・L / Home・End / F5 / Backspace）は
    /// JS 単独管理が正しいため対象外。
    /// </summary>
    public class KeyboardShortcutDriftTests
    {
        private static string FindRepoFile(string relative)
        {
            var rel = relative.Replace('/', Path.DirectorySeparatorChar);
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, rel);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException($"リポジトリ内で {relative} が見つかりません");
        }

        private static readonly string Xaml = File.ReadAllText(FindRepoFile("Views/MainWindow.xaml"));
        // 注入 JS は Scripts/*.js へ出した（#345）。以前は MainWindow.xaml.cs の
        // 生文字列を読んでいた。
        private static readonly string Js   = File.ReadAllText(FindRepoFile("Scripts/KeyboardShortcut.js"));

        // 表示名, JS 側トークン, XAML 側トークン（複数指定時は全て必要）
        public static TheoryData<string, string, string[]> GlobalShortcuts() => new()
        {
            { "Ctrl+Left",  "=== 'ArrowLeft'",    new[] { "Key=\"Left\"" } },
            { "Ctrl+Right", "=== 'ArrowRight'",   new[] { "Key=\"Right\"" } },
            { "Ctrl+N",     "=== 'n'",            new[] { "Key=\"N\"" } },
            { "Ctrl+F",     "=== 'f'",            new[] { "Key=\"F\"" } },
            { "F3",         "=== 'F3'",           new[] { "Key=\"F3\"" } },
            { "Ctrl+1-9",   ">= '1' && k <= '9'", new[] { "Key=\"Number1\"", "Key=\"Number9\"" } },
        };

        [Theory]
        [MemberData(nameof(GlobalShortcuts))]
        public void GlobalShortcut_IsHandledInInjectedJs(string name, string jsToken, string[] xamlTokens)
        {
            _ = xamlTokens;
            Assert.True(Js.Contains(jsToken),
                $"{name}: 注入 JS（KeyboardShortcutScript）にハンドラ '{jsToken}' が見つかりません。" +
                "WebView2 にフォーカスがあるときに効かなくなります。");
        }

        [Theory]
        [MemberData(nameof(GlobalShortcuts))]
        public void GlobalShortcut_HasXamlAccelerator(string name, string jsToken, string[] xamlTokens)
        {
            _ = jsToken;
            foreach (var token in xamlTokens)
                Assert.True(Xaml.Contains(token),
                    $"{name}: XAML アクセラレータ '{token}' が見つかりません。" +
                    "メインウィンドウ/検索ボックス等にフォーカスがあるときに効かなくなります。");
        }
    }
}
