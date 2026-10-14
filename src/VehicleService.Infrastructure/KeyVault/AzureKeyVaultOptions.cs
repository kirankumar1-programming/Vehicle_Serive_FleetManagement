namespace VehicleService.Infrastructure.KeyVault;

public class AzureKeyVaultOptions
{
    public const string SectionName = "KeyVault";

    public bool Enabled { get; set; } = true;
    public string? VaultUri { get; set; }
    public string? VaultName { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public int ReloadIntervalMinutes { get; set; } = 15;

    public string? GetEffectiveVaultUri()
    {
        if (!string.IsNullOrWhiteSpace(VaultUri))
            return VaultUri.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(VaultName))
            return $"https://{VaultName.Trim()}.vault.azure.net";

        return null;
    }
}
