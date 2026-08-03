using Microsoft.UI.Xaml.Controls;
using Synapse.Core.Features.Common.Interfaces;

namespace Synapse.UI.Features.SoftwareApps.Views;

public sealed partial class ExternalAppsHelpContent : UserControl
{
    public ExternalAppsHelpContent(ILocalizationService localizationService)
    {
        this.InitializeComponent();
        HelpContentText.Text = localizationService.GetString("Help_ExternalApps_Content");

        LearnMoreLink.Content = localizationService.GetString("Help_LearnMore_ExternalApps");
        LearnMoreLink.NavigateUri = new System.Uri("https://github.com/MaTows02/Synapse#optimisations-windows");
    }
}
