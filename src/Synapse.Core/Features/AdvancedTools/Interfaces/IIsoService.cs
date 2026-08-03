using System;
using System.Threading;
using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.AdvancedTools.Interfaces;

public interface IIsoService
{
    Task<bool> ValidateIsoFileAsync(string isoPath);

    Task<bool> ExtractIsoAsync(
        string isoPath,
        string workingDirectory,
        IProgress<TaskProgressDetail>? progress = null,
        CancellationToken cancellationToken = default);

    Task<bool> CreateIsoAsync(
        string workingDirectory,
        string outputPath,
        IProgress<TaskProgressDetail>? progress = null,
        CancellationToken cancellationToken = default);

    Task<bool> CleanupWorkingDirectoryAsync(string workingDirectory);
}
