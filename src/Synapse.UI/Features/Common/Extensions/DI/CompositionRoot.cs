using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Synapse.Infrastructure.Extensions.DI;

namespace Synapse.UI.Features.Common.Extensions.DI;

/// <summary>
/// The composition root for the  WinUI 3 application.
/// Orchestrates the registration of all services following Clean Architecture principles.
/// </summary>
public static class CompositionRoot
{
    /// <summary>
    /// Configures all services for the  application.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The configured service collection for method chaining</returns>
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        // Register services in dependency order
        services
            .AddInfrastructureServices() // Infrastructure implementations
            .AddSettingServices()        // Setting services (Customization, Optimization, SoftwareApps)
            .AddUIServices();            // UI-specific services (ThemeService, etc.)

        return services;
    }

    /// <summary>
    /// Creates and configures a host builder with the  service configuration.
    /// </summary>
    /// <returns>Configured host builder</returns>
    public static IHostBuilder CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.ConfigureServices();
            });
    }
}
