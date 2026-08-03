using Synapse.UI.Features.Common.Interfaces;

namespace Synapse.UI.Features.Customize.Interfaces;

/// <summary>
/// Marker interface identifying a feature ViewModel that belongs to the Customize page.
/// Enables <see cref="IEnumerable{ICustomizationFeatureViewModel}"/> injection via DI.
/// </summary>
public interface ICustomizationFeatureViewModel : ISettingsFeatureViewModel
{
}
