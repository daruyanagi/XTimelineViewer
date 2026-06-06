using Microsoft.UI.Xaml.Controls;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            this.InitializeComponent();
            PageTitle.Text = R.Get("Nav_About");
            PlaceholderText.Text = R.Get("Settings_Placeholder");
        }
    }
}
