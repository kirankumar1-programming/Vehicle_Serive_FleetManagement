using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using VehicleService.API.Controllers;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Infrastructure.Auth;
using VehicleService.Persistence.Context;
using VehicleService.Persistence.Repositories;
using Xunit;

namespace VehicleService.Tests;

public class SecurityAndAuthTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<SignInManager<ApplicationUser>> _mockSignInManager;
    private readonly JwtTokenService _jwtTokenService;

    public SecurityAndAuthTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"SecurityAuthDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _unitOfWork = new UnitOfWork(_context);

        var configValues = new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "TestSecretKeyForVehicleServiceSecurityUnitTests2026!#$LongEnough",
            ["Jwt:Issuer"] = "VehicleServiceAPI",
            ["Jwt:Audience"] = "VehicleServiceClient",
            ["Jwt:ExpiryMinutes"] = "60",
            ["Jwt:RefreshTokenExpiryDays"] = "7"
        };
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        var userClaimsPrincipalFactoryMock = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
            _mockUserManager.Object,
            contextAccessorMock.Object,
            userClaimsPrincipalFactoryMock.Object,
            null!, null!, null!, null!);

        _jwtTokenService = new JwtTokenService(_configuration, _unitOfWork, _mockUserManager.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // ==========================================
    // 1. JWT Authentication & Claims Tests
    // ==========================================

    [Fact]
    public void GenerateToken_ShouldIncludeExpectedClaimsAndSignature()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "user_123",
            Email = "john@example.com",
            FullName = "John Doe",
            RoleType = UserRoleType.Customer
        };
        var roles = new List<string> { "Customer" };

        // Act
        var token = _jwtTokenService.GenerateToken(user, roles);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Issuer.Should().Be("VehicleServiceAPI");
        jwt.Audiences.Should().Contain("VehicleServiceClient");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == "user_123");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Email && c.Value == "john@example.com");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "John Doe");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "Customer");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
    }

    [Fact]
    public async Task GenerateTokenWithRefreshTokenAsync_ShouldPersistRefreshTokenInDatabase()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "user_456",
            Email = "admin@example.com",
            FullName = "Alexander Wright",
            RoleType = UserRoleType.Administrator
        };
        var roles = new List<string> { "Administrator" };

        // Act
        var (accessToken, refreshToken, expiresAt) = await _jwtTokenService.GenerateTokenWithRefreshTokenAsync(user, roles);

        // Assert
        accessToken.Should().NotBeNullOrWhiteSpace();
        refreshToken.Should().NotBeNullOrWhiteSpace();
        expiresAt.Should().BeAfter(DateTime.UtcNow);

        var storedToken = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshToken);
        storedToken.Should().NotBeNull();
        storedToken!.UserId.Should().Be("user_456");
        storedToken.IsRevoked.Should().BeFalse();
        storedToken.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        storedToken.IsActive.Should().BeTrue();
    }

    // ==========================================
    // 2. Refresh Token Rotation & Replay Protection
    // ==========================================

    [Fact]
    public async Task RefreshTokenAsync_ValidToken_ShouldRotateRefreshTokenAndRevokeOldOne()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "user_789",
            Email = "advisor@example.com",
            FullName = "Vikram Malhotra",
            RoleType = UserRoleType.ServiceAdvisor,
            IsActive = true
        };
        var roles = new List<string> { "ServiceAdvisor" };

        _mockUserManager.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);
        _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(roles);

        var (initialAccessToken, initialRefreshToken, _) = await _jwtTokenService.GenerateTokenWithRefreshTokenAsync(user, roles);

        // Act
        var response = await _jwtTokenService.RefreshTokenAsync(initialAccessToken, initialRefreshToken);

        // Assert
        response.Success.Should().BeTrue();
        response.Token.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBe(initialRefreshToken); // Rotated to a new refresh token

        // Verify old refresh token is marked revoked
        var oldToken = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == initialRefreshToken);
        oldToken.Should().NotBeNull();
        oldToken!.IsRevoked.Should().BeTrue();
        oldToken.ReplacedByToken.Should().Be(response.RefreshToken);
    }

    [Fact]
    public async Task RefreshTokenAsync_RevokedToken_ShouldFail()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "user_revoked",
            Email = "revoked@example.com",
            FullName = "Revoked User",
            RoleType = UserRoleType.Customer,
            IsActive = true
        };
        var roles = new List<string> { "Customer" };

        var (token, refreshToken, _) = await _jwtTokenService.GenerateTokenWithRefreshTokenAsync(user, roles);
        await _jwtTokenService.RevokeRefreshTokenAsync(refreshToken);

        // Act
        var response = await _jwtTokenService.RefreshTokenAsync(token, refreshToken);

        // Assert
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("revoked");
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_ExistingToken_ShouldMarkTokenAsRevoked()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "user_to_revoke",
            Email = "test@example.com",
            FullName = "Test User",
            RoleType = UserRoleType.Customer
        };
        var roles = new List<string> { "Customer" };

        var (_, refreshToken, _) = await _jwtTokenService.GenerateTokenWithRefreshTokenAsync(user, roles);

        // Act
        var result = await _jwtTokenService.RevokeRefreshTokenAsync(refreshToken);

        // Assert
        result.Should().BeTrue();
        var stored = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshToken);
        stored!.IsRevoked.Should().BeTrue();
        stored.IsActive.Should().BeFalse();
    }

    // ==========================================
    // 3. RBAC Constants & Groupings Tests
    // ==========================================

    [Fact]
    public void RoleConstants_ShouldContainCorrectRolesAndGroupings()
    {
        UserRoles.Administrator.Should().Be("Administrator");
        UserRoles.ServiceAdvisor.Should().Be("ServiceAdvisor");
        UserRoles.Mechanic.Should().Be("Mechanic");
        UserRoles.InventoryManager.Should().Be("InventoryManager");
        UserRoles.FinanceManager.Should().Be("FinanceManager");
        UserRoles.FleetManager.Should().Be("FleetManager");
        UserRoles.Customer.Should().Be("Customer");

        UserRoles.Staff.Should().Contain("Administrator");
        UserRoles.Staff.Should().Contain("ServiceAdvisor");
        UserRoles.Staff.Should().Contain("Mechanic");
        UserRoles.Staff.Should().Contain("InventoryManager");
        UserRoles.Staff.Should().Contain("FinanceManager");
        UserRoles.Staff.Should().Contain("FleetManager");

        UserRoles.AdvisorOrAdmin.Should().Be("Administrator,ServiceAdvisor");
        UserRoles.FinanceOrAdmin.Should().Be("Administrator,FinanceManager");
        UserRoles.InventoryOrAdmin.Should().Be("Administrator,InventoryManager");
    }

    // ==========================================
    // 4. Input Validation Model Tests
    // ==========================================

    [Theory]
    [InlineData("", "password123", false)] // Empty email
    [InlineData("not-an-email", "password123", false)] // Invalid email format
    [InlineData("user@example.com", "", false)] // Empty password
    [InlineData("user@example.com", "123", false)] // Password too short (< 6)
    [InlineData("user@example.com", "validPassword123", true)] // Valid
    public void LoginRequest_Validation_ShouldEnforceRules(string email, string password, bool shouldBeValid)
    {
        var model = new LoginRequest { Email = email, Password = password };
        var validationResults = ValidateModel(model);
        (validationResults.Count == 0).Should().Be(shouldBeValid);
    }

    [Theory]
    [InlineData("John Doe", "john@test.com", "9876543210", "weak", "weak", false)] // Password too short
    [InlineData("John Doe", "john@test.com", "9876543210", "Password123", "Password123", false)] // Missing special character
    [InlineData("John Doe", "john@test.com", "9876543210", "password@123", "password@123", false)] // Missing uppercase
    [InlineData("John Doe", "john@test.com", "9876543210", "Password@123", "Mismatch@123", false)] // Passwords don't match
    [InlineData("John Doe", "invalid-email", "9876543210", "Password@123", "Password@123", false)] // Invalid email
    [InlineData("John Doe", "john@test.com", "9876543210", "Password@123", "Password@123", true)] // Valid strong password
    public void RegisterRequest_Validation_ShouldEnforceStrongPasswordAndMatch(
        string name, string email, string phone, string pass, string confirm, bool shouldBeValid)
    {
        var model = new RegisterRequest
        {
            FullName = name,
            Email = email,
            PhoneNumber = phone,
            Password = pass,
            ConfirmPassword = confirm
        };
        var validationResults = ValidateModel(model);
        (validationResults.Count == 0).Should().Be(shouldBeValid);
    }

    [Theory]
    [InlineData(0, 1, false)] // Invalid vehicle id
    [InlineData(1, 0, false)] // Invalid service center id
    [InlineData(1, 1, true)]  // Valid
    public void BookAppointmentDto_Validation_ShouldEnforceValidIds(int vehicleId, int centerId, bool shouldBeValid)
    {
        var model = new BookAppointmentDto
        {
            VehicleId = vehicleId,
            ServiceCenterId = centerId,
            AppointmentDate = DateTime.UtcNow.AddDays(2),
            TimeSlot = "10:00 AM - 12:00 PM"
        };
        var validationResults = ValidateModel(model);
        (validationResults.Count == 0).Should().Be(shouldBeValid);
    }

    [Theory]
    [InlineData("", "VIN12345678901", "Hyundai", "Creta", false)] // Missing registration number
    [InlineData("KA01AB1234", "SHORT", "Hyundai", "Creta", false)] // VIN too short (< 11)
    [InlineData("KA01AB1234", "VIN12345678901", "", "Creta", false)] // Missing make
    [InlineData("KA01AB1234", "VIN12345678901", "Hyundai", "", false)] // Missing model
    [InlineData("KA01AB1234", "VIN12345678901", "Hyundai", "Creta", true)] // Valid
    public void CreateVehicleDto_Validation_ShouldEnforceVehicleFields(string reg, string vin, string make, string model, bool shouldBeValid)
    {
        var dto = new CreateVehicleDto
        {
            RegistrationNumber = reg,
            VIN = vin,
            Make = make,
            Model = model,
            ManufacturingYear = 2024,
            CurrentMileage = 10000
        };
        var validationResults = ValidateModel(dto);
        (validationResults.Count == 0).Should().Be(shouldBeValid);
    }

    [Theory]
    [InlineData(0, 100, false)] // Invalid invoice id
    [InlineData(1, 0, false)]   // Zero amount
    [InlineData(1, -50, false)] // Negative amount
    [InlineData(1, 500, true)]  // Valid
    public void PaymentRequestDto_Validation_ShouldEnforcePositiveAmount(int invoiceId, decimal amount, bool shouldBeValid)
    {
        var model = new PaymentRequestDto
        {
            InvoiceId = invoiceId,
            Amount = amount,
            Method = PaymentMethod.CreditCard
        };
        var validationResults = ValidateModel(model);
        (validationResults.Count == 0).Should().Be(shouldBeValid);
    }

    [Theory]
    [InlineData(0, 10, false)] // Invalid part id
    [InlineData(1, 0, false)]  // Zero quantity
    [InlineData(1, -5, false)] // Negative quantity
    [InlineData(1, 50, true)]  // Valid
    public void AdjustStockDto_Validation_ShouldEnforceQuantityAndPartId(int partId, int quantity, bool shouldBeValid)
    {
        var model = new AdjustStockDto
        {
            PartId = partId,
            Quantity = quantity,
            TransactionType = InventoryTransactionType.Adjustment,
            UnitCost = 150
        };
        var validationResults = ValidateModel(model);
        (validationResults.Count == 0).Should().Be(shouldBeValid);
    }

    // ==========================================
    // 5. Privilege Escalation Prevention Tests
    // ==========================================

    [Fact]
    public async Task Register_PublicUnauthenticated_WithElevatedRole_ShouldBeRejected()
    {
        // Arrange
        var controller = new AuthApiController(_mockUserManager.Object, _mockSignInManager.Object, _jwtTokenService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext() // Unauthenticated user
        };

        var request = new RegisterRequest
        {
            FullName = "Hacker User",
            Email = "hacker@test.com",
            PhoneNumber = "+91 99000 00000",
            Password = "StrongPassword@123",
            ConfirmPassword = "StrongPassword@123",
            RoleType = UserRoleType.Administrator // Attempting privilege escalation
        };

        // Act
        var result = await controller.Register(request);

        // Assert
        var badRequest = result.Result as BadRequestObjectResult;
        badRequest.Should().NotBeNull();
        var response = badRequest!.Value as AuthResponse;
        response.Should().NotBeNull();
        response!.Success.Should().BeFalse();
        response.Message.Should().Contain("Public registration is only permitted for the Customer role");
    }

    [Fact]
    public async Task Register_PublicUnauthenticated_WithCustomerRole_ShouldSucceed()
    {
        // Arrange
        var controller = new AuthApiController(_mockUserManager.Object, _mockSignInManager.Object, _jwtTokenService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var request = new RegisterRequest
        {
            FullName = "Legit Customer",
            Email = "legit@test.com",
            PhoneNumber = "+91 99000 11111",
            Password = "StrongPassword@123",
            ConfirmPassword = "StrongPassword@123",
            RoleType = UserRoleType.Customer
        };

        _mockUserManager.Setup(m => m.FindByEmailAsync(request.Email)).ReturnsAsync((ApplicationUser?)null);
        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), request.Password))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Customer"))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await controller.Register(request);

        // Assert
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var response = okResult!.Value as AuthResponse;
        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        response.Role.Should().Be("Customer");
        response.Token.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    // Helper method to run DataAnnotations validation
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, validationContext, validationResults, validateAllProperties: true);
        return validationResults;
    }
}
