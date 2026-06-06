using Microsoft.UI.Xaml.Controls;

namespace XTimelineViewer.Views.Settings
{
    public sealed partial class ExperimentalPage : Page
    {
        public ExperimentalPage()
        {
            this.InitializeComponent();
            PageTitle.Text = R.Get("Nav_Experimental");
            PlaceholderText.Text = R.Get("Settings_Placeholder");
        }
    }
}
