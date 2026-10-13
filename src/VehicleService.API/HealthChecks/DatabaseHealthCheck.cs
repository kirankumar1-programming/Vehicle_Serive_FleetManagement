using Microsoft.Extensions.Diagnostics.HealthChecks;
using VehicleService.Persistence.Context;

namespace VehicleService.API.HealthChecks;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(ApplicationDbContext dbContext, ILogger<DatabaseHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            if (canConnect)
            {
                return HealthCheckResult.Healthy("Database connection is active and responsive.", new Dictionary<string, object>
                {
                    ["provider"] = _dbContext.Database.ProviderName ?? "SQLite",
                    ["status"] = "Connected"
                });
            }

            return HealthCheckResult.Unhealthy("Unable to establish connection to the primary database.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return HealthCheckResult.Unhealthy("Database health check encountered an exception.", ex);
        }
    }
}
