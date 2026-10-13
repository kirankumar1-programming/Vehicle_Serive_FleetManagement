using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VehicleService.API.HealthChecks;

public class SystemMemoryHealthCheck : IHealthCheck
{
    private const long DefaultThresholdBytes = 1024L * 1024L * 1024L * 2; // 2 GB

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var allocatedMemory = GC.GetTotalMemory(forceFullCollection: false);
        using var process = Process.GetCurrentProcess();
        var workingSet = process.WorkingSet64;

        var allocatedMb = allocatedMemory / (1024 * 1024);
        var workingSetMb = workingSet / (1024 * 1024);

        var data = new Dictionary<string, object>
        {
            ["allocatedMemoryMB"] = allocatedMb,
            ["workingSetMB"] = workingSetMb,
            ["gen0Collections"] = GC.CollectionCount(0),
            ["gen1Collections"] = GC.CollectionCount(1),
            ["gen2Collections"] = GC.CollectionCount(2)
        };

        if (workingSet > DefaultThresholdBytes)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Working set memory ({workingSetMb} MB) exceeds threshold (2048 MB).",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Memory usage is healthy: {allocatedMb} MB managed heap, {workingSetMb} MB working set.",
            data: data));
    }
}
