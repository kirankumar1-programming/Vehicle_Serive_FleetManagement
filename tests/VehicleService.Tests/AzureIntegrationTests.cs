using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;
using VehicleService.Application.Interfaces;
using VehicleService.Infrastructure.KeyVault;
using VehicleService.Infrastructure.Storage;
using VehicleService.Infrastructure.Telemetry;
using VehicleService.Persistence;
using VehicleService.Persistence.Context;
using Xunit;

namespace VehicleService.Tests;

public class AzureIntegrationTests
{
    // =========================================================================
    // 1. Azure Key Vault Tests
    // =========================================================================

    [Fact]
    public void AzureKeyVaultOptions_EffectiveVaultUri_ResolvesCorrectly()
    {
        // Direct URI
        var optionsWithUri = new AzureKeyVaultOptions { VaultUri = "https://my-vault.vault.azure.net/" };
        optionsWithUri.GetEffectiveVaultUri().Should().Be("https://my-vault.vault.azure.net");

        // Named Vault
        var optionsWithName = new AzureKeyVaultOptions { VaultName = "my-fleet-kv" };
        optionsWithName.GetEffectiveVaultUri().Should().Be("https://my-fleet-kv.vault.azure.net");

        // Empty
        var emptyOptions = new AzureKeyVaultOptions();
        emptyOptions.GetEffectiveVaultUri().Should().BeNull();
    }

    [Fact]
    public async Task AzureKeyVaultSecretService_FallbackMode_StoresAndRetrievesSecrets()
    {
        // Arrange
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["ExistingConfigKey"] = "ConfigValue123"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var options = Microsoft.Extensions.Options.Options.Create(new AzureKeyVaultOptions { Enabled = false });
        var logger = new Mock<ILogger<AzureKeyVaultSecretService>>();

        var service = new AzureKeyVaultSecretService(options, config, logger.Object);

        // Act & Assert - existing config fallback
        var existingSecret = await service.GetSecretAsync("ExistingConfigKey");
        existingSecret.Should().Be("ConfigValue123");

        // Act & Assert - store new secret dynamically
        await service.SetSecretAsync("NewSecretKey", "SecretVal456");
        var retrievedSecret = await service.GetSecretAsync("NewSecretKey");
        retrievedSecret.Should().Be("SecretVal456");

        // Act & Assert - check existence
        var exists = await service.SecretExistsAsync("NewSecretKey");
        exists.Should().BeTrue();

        var nonExistent = await service.SecretExistsAsync("UnknownSecret");
        nonExistent.Should().BeFalse();
    }

    [Fact]
    public void AddAzureKeyVaultIfConfigured_WhenDisabledOrEmpty_DoesNotThrow()
    {
        // Arrange
        var configBuilder = new ConfigurationBuilder();
        var emptyConfig = new ConfigurationBuilder().Build();

        // Act & Assert
        var action = () => configBuilder.AddAzureKeyVaultIfConfigured(emptyConfig);
        action.Should().NotThrow();
    }

    // =========================================================================
    // 2. Azure Blob Storage Tests
    // =========================================================================

    [Fact]
    public void AzureBlobStorageOptions_UriResolutionAndConfigDetection()
    {
        var optionsWithAccount = new AzureBlobStorageOptions { AccountName = "fleetstorage" };
        optionsWithAccount.GetEffectiveServiceUri().Should().Be("https://fleetstorage.blob.core.windows.net");
        optionsWithAccount.IsConfigured().Should().BeTrue();

        var optionsDisabled = new AzureBlobStorageOptions { Enabled = false, AccountName = "fleetstorage" };
        optionsDisabled.IsConfigured().Should().BeFalse();

        var unconfigured = new AzureBlobStorageOptions();
        unconfigured.IsConfigured().Should().BeFalse();
    }

    [Fact]
    public async Task AzureBlobStorageService_LocalStorageFallback_UploadsAndDeletesSuccessfully()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var options = Microsoft.Extensions.Options.Options.Create(new AzureBlobStorageOptions { Enabled = false });
        var logger = new Mock<ILogger<AzureBlobStorageService>>();

        var service = new AzureBlobStorageService(options, config, logger.Object);
        service.IsAzureActive.Should().BeFalse();

        // Act - Upload
        var content = "Sample vehicle inspection report content";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var fileUrl = await service.UploadFileAsync(stream, "inspection_report.txt", "text/plain");

        // Assert
        fileUrl.Should().NotBeNullOrWhiteSpace();
        fileUrl.Should().Contain("inspection_report.txt");

        // Act - Delete
        var deleteAction = async () => await service.DeleteFileAsync(fileUrl);
        await deleteAction.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AzureBlobStorageHealthCheck_ReturnsHealthyWithTelemetry()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var options = Microsoft.Extensions.Options.Options.Create(new AzureBlobStorageOptions { Enabled = false });
        var logger = new Mock<ILogger<AzureBlobStorageService>>();
        var blobService = new AzureBlobStorageService(options, config, logger.Object);

        var healthCheck = new AzureBlobStorageHealthCheck(options, blobService);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("azureConfigured");
        result.Data.Should().ContainKey("azureActive");
        result.Data.Should().ContainKey("containerName");
    }

    // =========================================================================
    // 3. Azure SQL Persistence Tests
    // =========================================================================

    [Theory]
    [InlineData("Server=tcp:vehicleserver.database.windows.net,1433;Initial Catalog=vehicledb;")]
    [InlineData("Data Source=tcp:sqlserver.database.windows.net;Initial Catalog=vehicledb;")]
    [InlineData("Server=localhost;Database=VehicleService;Trusted_Connection=True;")]
    public void AddPersistenceServices_AutoDetectsSqlServerConnectionString(string connStr)
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connStr,
                ["DatabaseProvider"] = "Auto"
            })
            .Build();

        // Act
        services.AddPersistenceServices(config);
        var provider = services.BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.SqlServer");
    }

    [Fact]
    public void AddPersistenceServices_ExplicitAzureSqlProvider_ConfiguresSqlServer()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "AzureSql",
                ["ConnectionStrings:DefaultConnection"] = "Server=tcp:azuresql.database.windows.net;Database=VehicleDb;"
            })
            .Build();

        // Act
        services.AddPersistenceServices(config);
        var provider = services.BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.SqlServer");
    }

    [Fact]
    public void AddPersistenceServices_SqliteProvider_ConfiguresSqlite()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "Sqlite",
                ["ConnectionStrings:DefaultConnection"] = "Data Source=TestVehicle.db"
            })
            .Build();

        // Act
        services.AddPersistenceServices(config);
        var provider = services.BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.Sqlite");
    }

    // =========================================================================
    // 4. Azure Application Insights Observability Tests
    // =========================================================================

    [Fact]
    public void AddAzureApplicationInsights_SetsEnvironmentVariablesAndRegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://eastus-8.in.applicationinsights.azure.com/"
            })
            .Build();

        // Act
        services.AddAzureApplicationInsights(config, "VehicleService.TestApp");

        // Assert
        Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME").Should().Be("VehicleService.TestApp");
        Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_ROLE_NAME").Should().Be("VehicleService.TestApp");

        var provider = services.BuildServiceProvider();
        provider.Should().NotBeNull();
    }
}
