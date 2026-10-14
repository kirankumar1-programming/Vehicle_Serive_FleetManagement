namespace VehicleService.Application.Interfaces;

public interface IKeyVaultSecretService
{
    /// <summary>
    /// Retrieves a secret value from Azure Key Vault or configured secret provider.
    /// </summary>
    Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or updates a secret in Azure Key Vault.
    /// </summary>
    Task SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a secret exists in Azure Key Vault.
    /// </summary>
    Task<bool> SecretExistsAsync(string secretName, CancellationToken cancellationToken = default);
}
