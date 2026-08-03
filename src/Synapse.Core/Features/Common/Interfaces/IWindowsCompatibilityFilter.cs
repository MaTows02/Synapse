using System.Collections.Generic;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IWindowsCompatibilityFilter
{
    IEnumerable<SettingDefinition> FilterSettingsByWindowsVersion(
        IEnumerable<SettingDefinition> settings
    );

    IEnumerable<SettingDefinition> FilterSettingsByWindowsVersion(
        IEnumerable<SettingDefinition> settings,
        bool applyFilter
    );
}
