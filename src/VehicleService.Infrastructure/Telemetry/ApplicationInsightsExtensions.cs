using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VehicleService.Infrastructure.Telemetry;

public static class ApplicationInsightsExtensions
{
    public static IServiceCollection AddAzureApplicationInsights(
        this IServiceCollection services,
        IConfiguration configuration,
        string roleName)
    {
        var connectionString = configuration["ApplicationInsights:ConnectionString"]
            ?? configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("ApplicationInsights");

        // Set OpenTelemetry & Azure Application Insights Cloud Role Name
        Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", roleName);
        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ROLE_NAME", roleName);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddApplicationInsightsTelemetry(options =>
            {
                options.ConnectionString = connectionString;
                options.EnableQuickPulseMetricStream = true;
                options.EnableDependencyTrackingTelemetryModule = true;
                options.EnableRequestTrackingTelemetryModule = true;
                options.EnablePerformanceCounterCollectionModule = true;
            });
        }

        return services;
    }
}
