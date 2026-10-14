using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VehicleService.Application.Interfaces;

namespace VehicleService.Infrastructure.Storage;

public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient? _containerClient;
    private readonly LocalBlobStorageService _fallbackLocalStorage;
    private readonly ILogger<AzureBlobStorageService> _logger;
    private readonly bool _isAzureActive;
    private bool _containerInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public AzureBlobStorageService(
        IOptions<AzureBlobStorageOptions> options,
        IConfiguration configuration,
        ILogger<AzureBlobStorageService> logger)
    {
        _logger = logger;
        _fallbackLocalStorage = new LocalBlobStorageService();

        var opt = options.Value;
        var connectionString = opt.ConnectionString
            ?? configuration["AZURE_STORAGE_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("AzureBlobStorage");

        var serviceUri = opt.GetEffectiveServiceUri()
            ?? configuration["AZURE_STORAGE_SERVICE_URI"]
            ?? configuration["AzureBlobStorage:ServiceUri"];

        var containerName = string.IsNullOrWhiteSpace(opt.ContainerName) ? "vehicle-attachments" : opt.ContainerName.ToLowerInvariant();

        if (opt.Enabled && (!string.IsNullOrWhiteSpace(connectionString) || !string.IsNullOrWhiteSpace(serviceUri)))
        {
            try
            {
                var clientOptions = new BlobClientOptions
                {
                    Retry =
                    {
                        Mode = RetryMode.Exponential,
                        MaxRetries = 5,
                        Delay = TimeSpan.FromSeconds(1),
                        MaxDelay = TimeSpan.FromSeconds(16)
                    }
                };

                BlobServiceClient blobServiceClient;
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    blobServiceClient = new BlobServiceClient(connectionString, clientOptions);
                    _logger.LogInformation("Azure Blob Storage initialized with connection string.");
                }
                else
                {
                    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                    {
                        ExcludeInteractiveBrowserCredential = true
                    });
                    blobServiceClient = new BlobServiceClient(new Uri(serviceUri!), credential, clientOptions);
                    _logger.LogInformation("Azure Blob Storage initialized with ServiceUri '{ServiceUri}' and DefaultAzureCredential.", serviceUri);
                }

                _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                _isAzureActive = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize Azure Blob Storage client. Falling back to local storage.");
                _isAzureActive = false;
            }
        }
        else
        {
            _logger.LogInformation("Azure Blob Storage is not configured. Falling back to LocalBlobStorageService.");
            _isAzureActive = false;
        }
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
    {
        if (!_isAzureActive || _containerClient == null)
        {
            return await _fallbackLocalStorage.UploadFileAsync(fileStream, fileName, contentType);
        }

        try
        {
            await EnsureContainerCreatedAsync();

            string cleanFileName = Path.GetFileName(fileName);
            string uniqueBlobName = $"{Guid.NewGuid():N}_{cleanFileName}";
            var blobClient = _containerClient.GetBlobClient(uniqueBlobName);

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
                }
            };

            await blobClient.UploadAsync(fileStream, uploadOptions);
            _logger.LogInformation("File uploaded to Azure Blob Storage: {BlobUri}", blobClient.Uri);

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file '{FileName}' to Azure Blob Storage. Attempting local fallback.", fileName);

            // If stream is seekable, rewind and fallback
            if (fileStream.CanSeek)
            {
                fileStream.Position = 0;
                return await _fallbackLocalStorage.UploadFileAsync(fileStream, fileName, contentType);
            }

            throw;
        }
    }

    public async Task DeleteFileAsync(string fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            return;

        if (!_isAzureActive || _containerClient == null)
        {
            await _fallbackLocalStorage.DeleteFileAsync(fileUrl);
            return;
        }

        try
        {
            string blobName = ExtractBlobName(fileUrl);
            var blobClient = _containerClient.GetBlobClient(blobName);
            var response = await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots);

            if (response.Value)
            {
                _logger.LogInformation("Deleted blob '{BlobName}' from Azure Blob Storage.", blobName);
            }
            else
            {
                _logger.LogDebug("Blob '{BlobName}' did not exist in Azure Blob Storage.", blobName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file '{FileUrl}' from Azure Blob Storage.", fileUrl);
            // Attempt local fallback just in case
            await _fallbackLocalStorage.DeleteFileAsync(fileUrl);
        }
    }

    private string ExtractBlobName(string fileUrl)
    {
        if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
        {
            // URL format: https://<account>.blob.core.windows.net/<container>/<blobName>
            var segments = uri.Segments;
            if (segments.Length > 2)
            {
                return string.Join("", segments.Skip(2)).Trim('/');
            }
            return Path.GetFileName(uri.LocalPath);
        }

        return Path.GetFileName(fileUrl);
    }

    private async Task EnsureContainerCreatedAsync()
    {
        if (_containerInitialized || _containerClient == null)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (!_containerInitialized)
            {
                await _containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);
                _containerInitialized = true;
            }
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == "ContainerAlreadyExists")
        {
            _containerInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure Azure Blob Storage container exists.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public bool IsAzureActive => _isAzureActive;
}
