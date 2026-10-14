namespace VehicleService.Infrastructure.Storage;

public class AzureBlobStorageOptions
{
    public const string SectionName = "AzureBlobStorage";

    public bool Enabled { get; set; } = true;
    public string? ConnectionString { get; set; }
    public string? ServiceUri { get; set; }
    public string? AccountName { get; set; }
    public string ContainerName { get; set; } = "vehicle-attachments";
    public bool CreateContainerIfNotExists { get; set; } = true;
    public bool PublicBlobAccess { get; set; } = true;

    public string? GetEffectiveServiceUri()
    {
        if (!string.IsNullOrWhiteSpace(ServiceUri))
            return ServiceUri.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(AccountName))
            return $"https://{AccountName.Trim()}.blob.core.windows.net";

        return null;
    }

    public bool IsConfigured()
    {
        return Enabled && (!string.IsNullOrWhiteSpace(ConnectionString) || !string.IsNullOrWhiteSpace(GetEffectiveServiceUri()));
    }
}
