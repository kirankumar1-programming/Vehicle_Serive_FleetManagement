using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VehicleService.API.HealthChecks;

public class DiskStorageHealthCheck : IHealthCheck
{
    private readonly IWebHostEnvironment _environment;

    public DiskStorageHealthCheck(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var contentRoot = _environment.ContentRootPath;
            var testFilePath = Path.Combine(contentRoot, $".health_check_probe_{Guid.NewGuid():N}.tmp");

            // Verify write permission
            File.WriteAllText(testFilePath, "health-check-probe");
            // Verify read permission
            var readContent = File.ReadAllText(testFilePath);
            // Clean up
            File.Delete(testFilePath);

            var drive = new DriveInfo(Path.GetPathRoot(contentRoot) ?? "C:\\");
            var freeSpaceGb = drive.AvailableFreeSpace / (1024 * 1024 * 1024);

            var data = new Dictionary<string, object>
            {
                ["driveName"] = drive.Name,
                ["freeSpaceGB"] = freeSpaceGb,
                ["totalSizeGB"] = drive.TotalSize / (1024 * 1024 * 1024),
                ["format"] = drive.DriveFormat
            };

            if (freeSpaceGb < 1)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Low disk space: {freeSpaceGb} GB remaining on {drive.Name}.",
                    data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Disk storage is accessible and writable. {freeSpaceGb} GB available on {drive.Name}.",
                data: data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Storage write/read probe failed.", ex));
        }
    }
}
