using Microsoft.UI.Xaml.Controls;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class ProfilesPage : Page
    {
        public ProfilesPage()
        {
            this.InitializeComponent();
            PageTitle.Text = R.Get("Nav_Profiles");
            PlaceholderText.Text = R.Get("Settings_Placeholder");
        }
    }
}
