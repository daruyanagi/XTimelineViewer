using Microsoft.UI.Xaml.Controls;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class ExtensionsPage : Page
    {
        public ExtensionsPage()
        {
            this.InitializeComponent();
            PageTitle.Text = R.Get("Nav_Extensions");
            PlaceholderText.Text = R.Get("Settings_Placeholder");
        }
    }
}
