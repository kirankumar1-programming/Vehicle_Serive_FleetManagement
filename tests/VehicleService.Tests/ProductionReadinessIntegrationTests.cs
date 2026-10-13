using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using VehicleService.API.Controllers;
using VehicleService.API.HealthChecks;
using VehicleService.API.Middleware;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Application.Services;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Entities;
using VehicleService.Persistence.Context;
using VehicleService.Persistence.Repositories;
using Xunit;

namespace VehicleService.Tests;

public class ProductionReadinessIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly Mock<ILogger<AuditLogsApiController>> _mockControllerLogger;

    public ProductionReadinessIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ProdReadinessDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _unitOfWork = new UnitOfWork(_context);
        _auditService = new AuditService(_unitOfWork);
        _mockControllerLogger = new Mock<ILogger<AuditLogsApiController>>();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static ControllerContext CreateControllerContext(string userId, string userName, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    [Fact]
    public async Task AuditLogsApiController_GetAuditLogs_ReturnsPagedResultsForAdmin()
    {
        // Arrange
        // Seed an audit log
        var log = new AuditLog
        {
            UserId = "user-123",
            UserName = "admin@example.com",
            Action = "Created",
            EntityName = "ServiceAppointment",
            EntityId = "45",
            NewValues = "{\"AppointmentNumber\":\"APT-100\"}",
            Timestamp = DateTime.UtcNow
        };
        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync();

        var controller = new AuditLogsApiController(_auditService, _mockControllerLogger.Object)
        {
            ControllerContext = CreateControllerContext("admin-001", "Administrator", UserRoles.Administrator)
        };

        // Act
        var result = await controller.GetAuditLogs(new AuditLogFilterDto { Page = 1, PageSize = 10 });

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var pagedResult = (PagedResult<AuditLogDto>)okResult.Value!;

        pagedResult.TotalCount.Should().Be(1);
        pagedResult.Items.Should().ContainSingle();
        pagedResult.Items.First().EntityName.Should().Be("ServiceAppointment");
        pagedResult.Items.First().Action.Should().Be("Created");
    }

    [Fact]
    public async Task AuditLogsApiController_GetAuditLogById_WhenFound_ReturnsLog()
    {
        // Arrange
        var log = new AuditLog
        {
            UserId = "admin-123",
            UserName = "admin@test.com",
            Action = "Updated",
            EntityName = "Vehicle",
            EntityId = "12",
            OldValues = "{\"Mileage\":50000}",
            NewValues = "{\"Mileage\":55000}",
            Timestamp = DateTime.UtcNow
        };
        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync();

        var controller = new AuditLogsApiController(_auditService, _mockControllerLogger.Object)
        {
            ControllerContext = CreateControllerContext("admin-001", "Administrator", UserRoles.Administrator)
        };

        // Act
        var result = await controller.GetAuditLogById(log.Id);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var returnedLog = (AuditLogDto)okResult.Value!;

        returnedLog.EntityName.Should().Be("Vehicle");
        returnedLog.Action.Should().Be("Updated");
        returnedLog.OldValues.Should().Contain("50000");
    }

    [Fact]
    public async Task AuditLogsApiController_GetAuditLogById_WhenNotFound_Returns404()
    {
        // Arrange
        var controller = new AuditLogsApiController(_auditService, _mockControllerLogger.Object)
        {
            ControllerContext = CreateControllerContext("admin-001", "Administrator", UserRoles.Administrator)
        };

        // Act
        var result = await controller.GetAuditLogById(9999);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task MiddlewarePipeline_CorrelationAndLogging_ExecutesSeamlessly()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Correlation-ID"] = "integration-correlation-abc-777";
        httpContext.Request.Path = "/api/test";

        var loggerMock = new Mock<ILogger<CorrelationIdMiddleware>>();
        var reqLoggerMock = new Mock<ILogger<RequestLoggingMiddleware>>();

        var executed = false;
        RequestDelegate terminalDelegate = ctx =>
        {
            executed = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        var requestLoggingMiddleware = new RequestLoggingMiddleware(terminalDelegate, reqLoggerMock.Object);
        var correlationIdMiddleware = new CorrelationIdMiddleware(requestLoggingMiddleware.InvokeAsync, loggerMock.Object);

        // Act
        await correlationIdMiddleware.InvokeAsync(httpContext);

        // Assert
        executed.Should().BeTrue();
        httpContext.TraceIdentifier.Should().Be("integration-correlation-abc-777");
    }
}
