using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics;

namespace XTimelineViewer
{
    public sealed partial class AddProfileWindow : Window
    {
        private readonly string _profileId = Guid.NewGuid().ToString("N");

        public event EventHandler<ProfileConfig>? ProfileCreated;

        public AddProfileWindow()
        {
            this.InitializeComponent();
            AppWindow.Resize(new SizeInt32(500, 700));
            Title = R.Get("AddProfile_Title");
            LoginHintText.Text = R.Get("AddProfile_LoginHint");
            CancelBtn.Content = R.Get("Button_Cancel");
            CreateBtn.Content = R.Get("AddProfile_CreateBtn");
            ProfileNameBox.PlaceholderText = R.Get("AddProfile_FallbackLabel");
            _ = InitAsync();
        }

        private static string GetProfilesDataDir()
        {
            if (PackageContext.IsPackaged)
                return Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalFolder.Path, "profiles");
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XTimelineViewer", "profiles");
        }

        private async Task InitAsync()
        {
            var folder = Path.Combine(GetProfilesDataDir(), _profileId);
            Directory.CreateDirectory(folder);
            var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = false };
            var env = await CoreWebView2Environment.CreateWithOptionsAsync("", folder, options);
            await LoginWebView.EnsureCoreWebView2Async(env);

            LoginWebView.Source = new Uri("https://x.com/i/flow/login");

            LoginWebView.CoreWebView2.NavigationCompleted += async (s, e) =>
            {
                if (!e.IsSuccess) return;
                if (!Uri.TryCreate(LoginWebView.CoreWebView2.Source, UriKind.Absolute, out var uri)) return;
                if (!uri.AbsolutePath.TrimEnd('/').Equals("/home", StringComparison.OrdinalIgnoreCase)) return;

                var screenName = await TryGetScreenNameAsync();
                ShowConfirmPhase(screenName);
            };
        }

        private async Task<string?> TryGetScreenNameAsync()
        {
            var result = await LoginWebView.CoreWebView2.ExecuteScriptAsync(
                "document.querySelector('[data-testid=\"AppTabBar_Profile_Link\"]')?.href?.split('/').pop() ?? null");
            return result?.Trim('"') is { Length: > 0 } name && name != "null" ? name : null;
        }

        private void ShowConfirmPhase(string? screenName)
        {
            LoginPhase.Visibility = Visibility.Collapsed;
            ConfirmPhase.Visibility = Visibility.Visible;
            CreateBtn.Visibility = Visibility.Visible;

            if (screenName is not null)
            {
                DetectedText.Text = string.Format(R.Get("AddProfile_Detected"), $"@{screenName}");
                ProfileNameBox.Text = screenName;
            }
            else
            {
                DetectedText.Text = "";
            }
        }

        private void CreateBtn_Click(object sender, RoutedEventArgs e)
        {
            var name = ProfileNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var profile = new ProfileConfig
            {
                Id = _profileId,
                Name = name,
            };
            ProfileCreated?.Invoke(this, profile);
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CleanupProfileData();
            Close();
        }

        private void CleanupProfileData()
        {
            try
            {
                LoginWebView.Close();
                var folder = Path.Combine(GetProfilesDataDir(), _profileId);
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch { }
        }
    }
}
