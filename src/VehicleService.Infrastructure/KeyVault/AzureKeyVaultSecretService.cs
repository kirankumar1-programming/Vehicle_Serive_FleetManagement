using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using VehicleService.Application.Interfaces;

namespace VehicleService.Infrastructure.KeyVault;

public class AzureKeyVaultSecretService : IKeyVaultSecretService
{
    private readonly SecretClient? _secretClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureKeyVaultSecretService> _logger;
    private readonly ConcurrentDictionary<string, string> _fallbackStore = new();

    public AzureKeyVaultSecretService(
        IOptions<AzureKeyVaultOptions> options,
        IConfiguration configuration,
        ILogger<AzureKeyVaultSecretService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var opt = options.Value;
        var vaultUri = opt.GetEffectiveVaultUri()
            ?? configuration["AZURE_KEYVAULT_VAULTURI"]
            ?? configuration["KeyVault:VaultUri"];

        if (opt.Enabled && !string.IsNullOrWhiteSpace(vaultUri))
        {
            try
            {
                var credential = AzureKeyVaultConfigurationExtensions.CreateTokenCredential(opt);
                var clientOptions = new SecretClientOptions
                {
                    Retry =
                    {
                        Mode = RetryMode.Exponential,
                        MaxRetries = 5,
                        Delay = TimeSpan.FromSeconds(1),
                        MaxDelay = TimeSpan.FromSeconds(16)
                    }
                };
                _secretClient = new SecretClient(new Uri(vaultUri), credential, clientOptions);
                _logger.LogInformation("Azure Key Vault SecretClient initialized for vault: {VaultUri}", vaultUri);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize SecretClient for vault '{VaultUri}'. Operating in fallback mode.", vaultUri);
            }
        }
        else
        {
            _logger.LogInformation("Azure Key Vault is not configured. Operating in local fallback mode.");
        }
    }

    public async Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            throw new ArgumentException("Secret name cannot be null or empty.", nameof(secretName));

        // Format secret name for Azure Key Vault (Azure Key Vault allows only alphanumeric and hyphens)
        var vaultSecretKey = secretName.Replace(":", "--");

        if (_secretClient != null)
        {
            try
            {
                KeyVaultSecret secret = await _secretClient.GetSecretAsync(vaultSecretKey, cancellationToken: cancellationToken);
                return secret.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogDebug("Secret '{SecretName}' not found in Azure Key Vault.", vaultSecretKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading secret '{SecretName}' from Azure Key Vault.", vaultSecretKey);
            }
        }

        // Fallback: in-memory store, then configuration
        if (_fallbackStore.TryGetValue(secretName, out var storedVal))
            return storedVal;

        return _configuration[secretName] ?? _configuration[vaultSecretKey];
    }

    public async Task SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            throw new ArgumentException("Secret name cannot be null or empty.", nameof(secretName));

        var vaultSecretKey = secretName.Replace(":", "--");

        if (_secretClient != null)
        {
            try
            {
                await _secretClient.SetSecretAsync(vaultSecretKey, secretValue, cancellationToken);
                _logger.LogInformation("Successfully stored secret '{SecretName}' in Azure Key Vault.", vaultSecretKey);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set secret '{SecretName}' in Azure Key Vault. Falling back to local storage.", vaultSecretKey);
            }
        }

        _fallbackStore[secretName] = secretValue;
    }

    public async Task<bool> SecretExistsAsync(string secretName, CancellationToken cancellationToken = default)
    {
        var secret = await GetSecretAsync(secretName, cancellationToken);
        return secret != null;
    }
}
