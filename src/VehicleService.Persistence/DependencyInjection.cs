using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VehicleService.Application.Interfaces;
using VehicleService.Persistence.Context;
using VehicleService.Persistence.Repositories;

namespace VehicleService.Persistence;

public static class DependencyInjection
{
    public const string AzureSqlProvider = "AzureSql";
    public const string SqlServerProvider = "SqlServer";
    public const string SqliteProvider = "Sqlite";

    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration,
        string? defaultConnection = null)
    {
        var provider = configuration["DatabaseProvider"]?.Trim() ?? "Auto";
        var connectionString = defaultConnection
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? configuration["ConnectionStrings:DefaultConnection"]
            ?? "Data Source=VehicleService.db";

        // Determine if Azure SQL / SQL Server should be used
        bool isSqlServer = string.Equals(provider, AzureSqlProvider, StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, SqlServerProvider, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(provider, "Auto", StringComparison.OrdinalIgnoreCase) && IsSqlServerConnectionString(connectionString));

        int maxRetryCount = configuration.GetValue("Database:MaxRetryCount", 6);
        int maxRetryDelaySeconds = configuration.GetValue("Database:MaxRetryDelaySeconds", 30);
        int commandTimeoutSeconds = configuration.GetValue("Database:CommandTimeoutSeconds", 60);

        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            if (isSqlServer)
            {
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: maxRetryCount,
                        maxRetryDelay: TimeSpan.FromSeconds(maxRetryDelaySeconds),
                        errorNumbersToAdd: null);
                    sqlOptions.CommandTimeout(commandTimeoutSeconds);
                    sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                });
            }
            else
            {
                options.UseSqlite(connectionString, sqliteOptions =>
                {
                    sqliteOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                });
            }

            // Enable detailed errors and sensitive data logging only in Development
            var environment = configuration["ASPNETCORE_ENVIRONMENT"];
            if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
            {
                options.EnableDetailedErrors();
                options.EnableSensitiveDataLogging();
            }
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        return services;
    }

    private static bool IsSqlServerConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        return connectionString.Contains("database.windows.net", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Server=tcp:", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Data Source=tcp:", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Trusted_Connection=", StringComparison.OrdinalIgnoreCase);
    }
}
