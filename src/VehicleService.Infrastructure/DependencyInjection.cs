using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VehicleService.Application;
using VehicleService.Application.Interfaces;
using VehicleService.Infrastructure.Auth;
using VehicleService.Infrastructure.BackgroundServices;
using VehicleService.Infrastructure.KeyVault;
using VehicleService.Infrastructure.Payment;
using VehicleService.Infrastructure.Storage;

namespace VehicleService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Infrastructure External Services
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IPaymentService, MockPaymentService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        // Azure Key Vault Integration
        services.Configure<AzureKeyVaultOptions>(configuration.GetSection(AzureKeyVaultOptions.SectionName));
        services.AddSingleton<IKeyVaultSecretService, AzureKeyVaultSecretService>();

        // Azure Blob Storage Integration with Local Fallback
        services.Configure<AzureBlobStorageOptions>(configuration.GetSection(AzureBlobStorageOptions.SectionName));
        services.AddSingleton<AzureBlobStorageService>();
        services.AddSingleton<IBlobStorageService>(sp => sp.GetRequiredService<AzureBlobStorageService>());
        services.AddSingleton<AzureBlobStorageHealthCheck>();

        // AI & External LLM Services
        services.Configure<Ai.LlmSettings>(configuration.GetSection(Ai.LlmSettings.SectionName));
        services.AddHttpClient<Ai.ILlmClient, Ai.LlmClient>();

        // Background Hosted Services
        services.AddHostedService<MaintenanceReminderBackgroundService>();
        services.AddHostedService<WarrantyExpiryBackgroundService>();
        services.AddHostedService<LowStockAlertBackgroundService>();

        return services;
    }

    /// <summary>
    /// Legacy adapter method for registering both Application and Infrastructure services.
    /// </summary>
    [Obsolete("Use services.AddApplicationServices() and services.AddInfrastructureServices() for explicit layer separation.")]
    public static IServiceCollection AddApplicationAndInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApplicationServices();
        services.AddInfrastructureServices(configuration);
        return services;
    }
}