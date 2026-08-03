// File: src/Synapse.Infrastructure/Features/Common/Services/SpecialDiscoveryRegistry.cs
using System.Collections.Generic;
using Synapse.Core.Features.Common.Interfaces;

namespace Synapse.Infrastructure.Features.Common.Services;

public sealed class SpecialDiscoveryRegistry(IReadOnlyList<ISpecialSettingHandler> handlers)
    : ISpecialDiscoveryRegistry
{
    public IEnumerable<ISpecialSettingHandler> All => handlers;
}
