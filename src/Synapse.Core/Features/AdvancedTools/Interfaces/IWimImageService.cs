using System;
using System.Threading;
using System.Threading.Tasks;
using Synapse.Core.Features.AdvancedTools.Models;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.AdvancedTools.Interfaces;

public interface IWimImageService
{
    Task<ImageFormatInfo?> DetectImageFormatAsync(string workingDirectory);

    Task<ImageDetectionResult> DetectAllImageFormatsAsync(string workingDirectory);

    Task<bool> ConvertImageAsync(
        string workingDirectory,
        ImageFormat targetFormat,
        IProgress<TaskProgressDetail>? progress = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteImageFileAsync(
        string workingDirectory,
        ImageFormat format,
        IProgress<TaskProgressDetail>? progress = null,
        CancellationToken cancellationToken = default);
}
