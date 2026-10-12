using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VehicleService.Application.Interfaces;
using VehicleService.Application.Services;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;
using VehicleService.Persistence.Context;
using VehicleService.Persistence.Repositories;
using Xunit;

namespace VehicleService.Tests;

public class ServiceLayerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceCenterService _serviceCenterService;
    private readonly IServiceCatalogService _serviceCatalogService;
    private readonly ICouponService _couponService;

    public ServiceLayerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ServiceLayerTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _unitOfWork = new UnitOfWork(_context);

        _serviceCenterService = new ServiceCenterService(_unitOfWork);
        _serviceCatalogService = new ServiceCatalogService(_unitOfWork);
        _couponService = new CouponService(_unitOfWork);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task ServiceCenterService_CreateAndRetrieve_ShouldSucceed()
    {
        // Arrange
        var center = new ServiceCenter
        {
            Name = "Metro Auto Works",
            Code = "SC-METRO",
            Address = "123 Tech Blvd",
            City = "Bangalore",
            State = "Karnataka",
            Phone = "9876543210",
            MaxDailyCapacity = 15,
            IsActive = true
        };

        // Act
        var created = await _serviceCenterService.CreateCenterAsync(center);
        var fetched = await _serviceCenterService.GetCenterByIdAsync(created.Id);
        var allCenters = await _serviceCenterService.GetAllCentersAsync();

        // Assert
        created.Id.Should().BeGreaterThan(0);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Metro Auto Works");
        allCenters.Should().Contain(c => c.Id == created.Id);
    }

    [Fact]
    public async Task ServiceCenterService_ToggleStatus_ShouldInvertAndThrowIfNotFound()
    {
        // Arrange
        var center = new ServiceCenter
        {
            Name = "North Center",
            Code = "SC-NORTH",
            IsActive = true
        };
        await _serviceCenterService.CreateCenterAsync(center);

        // Act
        await _serviceCenterService.ToggleCenterStatusAsync(center.Id);
        var updated = await _serviceCenterService.GetCenterByIdAsync(center.Id);

        // Assert
        updated!.IsActive.Should().BeFalse();

        // Non-existent id throws EntityNotFoundException
        var act = () => _serviceCenterService.ToggleCenterStatusAsync(9999);
        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task ServiceCenterService_BayOperations_ShouldWorkCorrectly()
    {
        // Arrange
        var center = await _serviceCenterService.CreateCenterAsync(new ServiceCenter
        {
            Name = "East Bay Hub",
            Code = "SC-EAST",
            IsActive = true
        });

        var bay = new ServiceBay
        {
            ServiceCenterId = center.Id,
            BayNumber = "BAY-101",
            BayName = "Quick Inspection 1",
            Status = BayStatus.Available,
            IsActive = true
        };

        // Act
        var createdBay = await _serviceCenterService.CreateBayAsync(bay);
        var bays = await _serviceCenterService.GetBaysByCenterIdAsync(center.Id);

        // Assert
        bays.Should().HaveCount(1);
        bays[0].BayNumber.Should().Be("BAY-101");

        // Delete bay
        await _serviceCenterService.DeleteBayAsync(createdBay.Id);
        var remainingBays = await _serviceCenterService.GetBaysByCenterIdAsync(center.Id);
        remainingBays.Should().BeEmpty();
    }

    [Fact]
    public async Task ServiceCatalogService_PackageOperations_ShouldManagePackages()
    {
        // Arrange
        var package = new ServicePackage
        {
            Name = "Monsoon Special Care",
            Description = "Full monsoon checkup & wipers",
            PackagePrice = 2499m,
            OriginalTotalPrice = 3200m,
            SavingsPercent = 22m,
            ValidityDays = 180,
            IsActive = true
        };

        // Act
        var created = await _serviceCatalogService.CreatePackageAsync(package);
        var fetched = await _serviceCatalogService.GetPackageByIdAsync(created.Id);

        // Assert
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Monsoon Special Care");
        fetched.PackagePrice.Should().Be(2499m);

        // Update package
        fetched.PackagePrice = 2199m;
        await _serviceCatalogService.UpdatePackageAsync(fetched);

        var updated = await _serviceCatalogService.GetPackageByIdAsync(created.Id);
        updated!.PackagePrice.Should().Be(2199m);

        // Toggle status
        await _serviceCatalogService.TogglePackageStatusAsync(created.Id);
        var toggled = await _serviceCatalogService.GetPackageByIdAsync(created.Id);
        toggled!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task CouponService_ValidateCoupon_ShouldCalculatePercentageDiscountAndRespectCap()
    {
        // Arrange
        var coupon = new Coupon
        {
            Code = "SAVE20",
            Title = "20% off up to ₹500",
            Type = DiscountType.Percentage,
            Value = 20m,
            MaxDiscountLimit = 500m,
            MinimumBillAmount = 1000m,
            ExpiryDate = DateTime.UtcNow.AddMonths(1),
            UsageLimit = 100,
            TimesUsed = 5,
            IsActive = true
        };
        await _couponService.CreateCouponAsync(coupon);

        // Act & Assert 1: Valid within max limit
        var result1 = await _couponService.ValidateCouponAsync("save20", 2000m); // 20% of 2000 is 400
        result1.IsValid.Should().BeTrue();
        result1.DiscountAmount.Should().Be(400m);

        // Act & Assert 2: Capped at max discount limit
        var result2 = await _couponService.ValidateCouponAsync("SAVE20", 5000m); // 20% of 5000 is 1000, capped at 500
        result2.IsValid.Should().BeTrue();
        result2.DiscountAmount.Should().Be(500m);

        // Act & Assert 3: Bill below minimum requirement
        var result3 = await _couponService.ValidateCouponAsync("SAVE20", 800m);
        result3.IsValid.Should().BeFalse();
        result3.DiscountAmount.Should().Be(0);
        result3.Message.Should().Contain("Minimum bill amount");
    }

    [Fact]
    public async Task CouponService_ValidateCoupon_ShouldRejectExpiredOrExhaustedCoupon()
    {
        // Arrange
        var expiredCoupon = new Coupon
        {
            Code = "EXPIRED",
            Title = "Expired Coupon",
            Type = DiscountType.FixedAmount,
            Value = 200m,
            MinimumBillAmount = 500m,
            ExpiryDate = DateTime.UtcNow.AddDays(-1),
            UsageLimit = 50,
            TimesUsed = 2,
            IsActive = true
        };
        await _couponService.CreateCouponAsync(expiredCoupon);

        var exhaustedCoupon = new Coupon
        {
            Code = "MAXEDOUT",
            Title = "Exhausted Coupon",
            Type = DiscountType.FixedAmount,
            Value = 200m,
            MinimumBillAmount = 500m,
            ExpiryDate = DateTime.UtcNow.AddDays(10),
            UsageLimit = 10,
            TimesUsed = 10,
            IsActive = true
        };
        await _couponService.CreateCouponAsync(exhaustedCoupon);

        // Act
        var expiredResult = await _couponService.ValidateCouponAsync("EXPIRED", 1000m);
        var exhaustedResult = await _couponService.ValidateCouponAsync("MAXEDOUT", 1000m);

        // Assert
        expiredResult.IsValid.Should().BeFalse();
        expiredResult.Message.Should().Contain("expired");

        exhaustedResult.IsValid.Should().BeFalse();
        exhaustedResult.Message.Should().Contain("maximum usage limit");
    }
}
