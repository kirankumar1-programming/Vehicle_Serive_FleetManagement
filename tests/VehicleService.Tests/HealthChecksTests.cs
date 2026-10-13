using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using VehicleService.API.HealthChecks;
using VehicleService.Persistence.Context;
using Xunit;

namespace VehicleService.Tests;

public class HealthChecksTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ILogger<DatabaseHealthCheck>> _mockDbLogger;
    private readonly Mock<IWebHostEnvironment> _mockEnv;

    public HealthChecksTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"HealthDb_{Guid.NewGuid()}")
            .Options;
        _context = new ApplicationDbContext(options);
        _mockDbLogger = new Mock<ILogger<DatabaseHealthCheck>>();

        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockEnv.Setup(e => e.ContentRootPath).Returns(AppDomain.CurrentDomain.BaseDirectory);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task DatabaseHealthCheck_WhenCanConnect_ReturnsHealthy()
    {
        // Arrange
        var healthCheck = new DatabaseHealthCheck(_context, _mockDbLogger.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("active and responsive");
        result.Data.Should().ContainKey("status");
    }

    [Fact]
    public async Task SystemMemoryHealthCheck_WhenNormalUsage_ReturnsHealthy()
    {
        // Arrange
        var healthCheck = new SystemMemoryHealthCheck();
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("allocatedMemoryMB");
        result.Data.Should().ContainKey("workingSetMB");
    }

    [Fact]
    public async Task DiskStorageHealthCheck_WhenAccessible_ReturnsHealthy()
    {
        // Arrange
        var healthCheck = new DiskStorageHealthCheck(_mockEnv.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("freeSpaceGB");
        result.Data.Should().ContainKey("driveName");
    }

    [Fact]
    public async Task HealthCheckResponseWriter_WritesDetailedJsonResponse()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var bodyStream = new MemoryStream();
        httpContext.Response.Body = bodyStream;

        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["Database"] = new HealthReportEntry(HealthStatus.Healthy, "Database connection active", TimeSpan.FromMilliseconds(5), null, null),
            ["Memory"] = new HealthReportEntry(HealthStatus.Healthy, "Memory healthy", TimeSpan.FromMilliseconds(1), null, null)
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(6));

        // Act
        await HealthCheckResponseWriter.WriteDetailedJsonResponse(httpContext, report);

        // Assert
        httpContext.Response.ContentType.Should().Contain("application/json");

        bodyStream.Seek(0, SeekOrigin.Begin);
        using var jsonDoc = await JsonDocument.ParseAsync(bodyStream);
        var root = jsonDoc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.GetProperty("totalDuration").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("entries").GetArrayLength().Should().Be(2);
    }
}
