using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Application.Services;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Persistence.Context;
using VehicleService.Persistence.Repositories;
using Xunit;

namespace VehicleService.Tests;

public class AutomaticAuditLoggingTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ICurrentUserService> _mockCurrentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuditService _auditService;

    public AutomaticAuditLoggingTests()
    {
        _mockCurrentUserService = new Mock<ICurrentUserService>();
        _mockCurrentUserService.Setup(u => u.UserId).Returns("audit-user-001");
        _mockCurrentUserService.Setup(u => u.UserName).Returns("admin@fleetmanagement.com");
        _mockCurrentUserService.Setup(u => u.IpAddress).Returns("192.168.1.100");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AuditTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options, _mockCurrentUserService.Object);
        _unitOfWork = new UnitOfWork(_context);
        _auditService = new AuditService(_unitOfWork);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityAdded_CreatesAuditLogWithCreatedAction()
    {
        // Arrange
        var center = new ServiceCenter
        {
            Name = "Automated Audit Center",
            Code = "SC-AUDIT-01",
            Address = "100 Observability Way",
            City = "Bangalore",
            State = "Karnataka",
            MaxDailyCapacity = 20,
            IsActive = true
        };

        // Act
        _context.ServiceCenters.Add(center);
        await _context.SaveChangesAsync();

        // Assert
        var logs = await _context.AuditLogs.ToListAsync();
        logs.Should().ContainSingle();

        var log = logs.First();
        log.Action.Should().Be("Created");
        log.EntityName.Should().Be(nameof(ServiceCenter));
        log.EntityId.Should().Be(center.Id.ToString());
        log.UserId.Should().Be("audit-user-001");
        log.UserName.Should().Be("admin@fleetmanagement.com");
        log.IpAddress.Should().Be("192.168.1.100");
        log.NewValues.Should().Contain("SC-AUDIT-01");
        log.OldValues.Should().BeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityModified_CreatesAuditLogWithUpdatedActionAndDiff()
    {
        // Arrange
        var part = new InventoryPart
        {
            PartNumber = "PRT-AUDIT-01",
            Name = "Spark Plug",
            CostPrice = 200.00m,
            SellingPrice = 250.00m,
            AvailableQuantity = 50,
            ReorderLevel = 10,
            Category = "Ignition"
        };
        _context.InventoryParts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        part.SellingPrice = 299.00m;
        part.AvailableQuantity = 45;
        await _context.SaveChangesAsync();

        // Assert
        var logs = await _context.AuditLogs.OrderBy(l => l.Timestamp).ToListAsync();
        logs.Should().HaveCount(2);

        var updateLog = logs.Last();
        updateLog.Action.Should().Be("Updated");
        updateLog.EntityName.Should().Be(nameof(InventoryPart));
        updateLog.EntityId.Should().Be(part.Id.ToString());
        updateLog.OldValues.Should().Contain("250");
        updateLog.NewValues.Should().Contain("299");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntitySoftDeleted_CreatesAuditLogWithSoftDeletedAction()
    {
        // Arrange
        var center = new ServiceCenter
        {
            Name = "Retiring Center",
            Code = "SC-RETIRE",
            Address = "Legacy Street",
            City = "Mumbai",
            State = "Maharashtra",
            MaxDailyCapacity = 5,
            IsActive = true,
            IsDeleted = false
        };
        _context.ServiceCenters.Add(center);
        await _context.SaveChangesAsync();

        // Act - Soft delete
        center.IsDeleted = true;
        await _context.SaveChangesAsync();

        // Assert
        var logs = await _context.AuditLogs.OrderBy(l => l.Timestamp).ToListAsync();
        logs.Should().HaveCount(2);

        var softDeleteLog = logs.Last();
        softDeleteLog.Action.Should().Be("SoftDeleted");
        softDeleteLog.EntityName.Should().Be(nameof(ServiceCenter));
        softDeleteLog.EntityId.Should().Be(center.Id.ToString());
    }

    [Fact]
    public async Task SaveChangesAsync_DoesNotAuditAuditLogTableItself()
    {
        // Arrange
        var manualLog = new AuditLog
        {
            UserId = "sec-officer",
            UserName = "officer@test.com",
            Action = "SecurityReview",
            EntityName = "AuthAudit",
            Timestamp = DateTime.UtcNow
        };

        // Act
        _context.AuditLogs.Add(manualLog);
        await _context.SaveChangesAsync();

        // Assert
        var logs = await _context.AuditLogs.ToListAsync();
        // Should only be the 1 record added manually, NO recursive audit record created
        logs.Should().HaveCount(1);
        logs.First().Action.Should().Be("SecurityReview");
    }

    [Fact]
    public async Task AuditService_GetFilteredLogsAsync_FiltersCorrectly()
    {
        // Arrange
        var part = new InventoryPart
        {
            PartNumber = "PRT-FILTER-01",
            Name = "Air Filter",
            CostPrice = 250.00m,
            SellingPrice = 350.00m,
            AvailableQuantity = 20,
            ReorderLevel = 5,
            Category = "Filtration"
        };
        _context.InventoryParts.Add(part);
        await _context.SaveChangesAsync();

        part.AvailableQuantity = 18;
        await _context.SaveChangesAsync();

        // Act - Query only Updated actions
        var filter = new AuditLogFilterDto
        {
            EntityName = nameof(InventoryPart),
            Action = "Updated",
            Page = 1,
            PageSize = 10
        };
        var result = await _auditService.GetFilteredLogsAsync(filter);

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle();
        result.Items.First().Action.Should().Be("Updated");
        result.Items.First().EntityName.Should().Be(nameof(InventoryPart));
    }
}
