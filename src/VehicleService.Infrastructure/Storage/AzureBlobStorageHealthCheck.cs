using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace VehicleService.Infrastructure.Storage;

public class AzureBlobStorageHealthCheck : IHealthCheck
{
    private readonly AzureBlobStorageOptions _options;
    private readonly AzureBlobStorageService _blobStorageService;

    public AzureBlobStorageHealthCheck(
        IOptions<AzureBlobStorageOptions> options,
        AzureBlobStorageService blobStorageService)
    {
        _options = options.Value;
        _blobStorageService = blobStorageService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["azureConfigured"] = _options.IsConfigured(),
            ["azureActive"] = _blobStorageService.IsAzureActive,
            ["containerName"] = _options.ContainerName
        };

        if (_blobStorageService.IsAzureActive)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Azure Blob Storage is active and operational for container '{_options.ContainerName}'.",
                data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "Azure Blob Storage is not configured; running with LocalBlobStorage fallback.",
            data));
    }
}
