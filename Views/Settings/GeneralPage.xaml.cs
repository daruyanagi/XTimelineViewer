using Microsoft.UI.Xaml.Controls;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class GeneralPage : Page
    {
        public GeneralPage()
        {
            this.InitializeComponent();
            PageTitle.Text = R.Get("Nav_General");
            PlaceholderText.Text = R.Get("Settings_Placeholder");
        }
    }
}
