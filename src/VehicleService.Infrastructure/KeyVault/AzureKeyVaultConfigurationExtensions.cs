using Azure.Core;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace VehicleService.Infrastructure.KeyVault;

public static class AzureKeyVaultConfigurationExtensions
{
    /// <summary>
    /// Integrates Azure Key Vault as a configuration provider if configured in environment or appsettings.
    /// Supports DefaultAzureCredential (Managed Identity, Azure CLI, env) or explicit Service Principal credentials.
    /// </summary>
    public static IConfigurationBuilder AddAzureKeyVaultIfConfigured(
        this IConfigurationBuilder configurationBuilder,
        IConfiguration initialConfig)
    {
        var options = new AzureKeyVaultOptions();
        initialConfig.GetSection(AzureKeyVaultOptions.SectionName).Bind(options);

        // Also check direct environment variables
        options.VaultUri ??= initialConfig["AZURE_KEYVAULT_VAULTURI"] ?? initialConfig["AZURE_KEYVAULT_URI"];
        options.VaultName ??= initialConfig["AZURE_KEYVAULT_NAME"];
        options.TenantId ??= initialConfig["AZURE_TENANT_ID"];
        options.ClientId ??= initialConfig["AZURE_CLIENT_ID"];
        options.ClientSecret ??= initialConfig["AZURE_CLIENT_SECRET"];

        if (!options.Enabled)
            return configurationBuilder;

        var vaultUri = options.GetEffectiveVaultUri();
        if (string.IsNullOrWhiteSpace(vaultUri))
            return configurationBuilder;

        try
        {
            var credential = CreateTokenCredential(options);
            var vaultUriInstance = new Uri(vaultUri);

            var keyVaultConfigOptions = new AzureKeyVaultConfigurationOptions
            {
                ReloadInterval = options.ReloadIntervalMinutes > 0
                    ? TimeSpan.FromMinutes(options.ReloadIntervalMinutes)
                    : null
            };

            configurationBuilder.AddAzureKeyVault(vaultUriInstance, credential, keyVaultConfigOptions);
        }
        catch (Exception ex)
        {
            // Log or write error to Console so application startup isn't fatally blocked in offline dev scenarios
            Console.WriteLine($"[Azure Key Vault Warning] Failed to configure Key Vault provider for '{vaultUri}': {ex.Message}");
        }

        return configurationBuilder;
    }

    public static TokenCredential CreateTokenCredential(AzureKeyVaultOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TenantId) &&
            !string.IsNullOrWhiteSpace(options.ClientId) &&
            !string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            return new ClientSecretCredential(options.TenantId, options.ClientId, options.ClientSecret);
        }

        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        });
    }
}
