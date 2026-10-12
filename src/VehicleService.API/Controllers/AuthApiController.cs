using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthApiController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthApiController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtTokenService jwtTokenService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtTokenService = jwtTokenService;
    }

    [HttpPost("login")]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null || !user.IsActive)
            return Unauthorized(new AuthResponse { Success = false, Message = "Invalid email or password." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status423Locked, new AuthResponse
            {
                Success = false,
                Message = "Account is temporarily locked due to multiple failed login attempts. Please try again later."
            });
        }

        if (!result.Succeeded)
            return Unauthorized(new AuthResponse { Success = false, Message = "Invalid email or password." });

        var roles = await _userManager.GetRolesAsync(user);
        var (token, refreshToken, expiresAt) = await _jwtTokenService.GenerateTokenWithRefreshTokenAsync(user, roles);

        return Ok(new AuthResponse
        {
            Success = true,
            Token = token,
            RefreshToken = refreshToken,
            Expiration = expiresAt,
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = roles.FirstOrDefault() ?? user.RoleType.ToString(),
            Message = "Authentication successful."
        });
    }

    [HttpPost("register")]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Security: Prevent privilege escalation during public registration
        var assignedRole = UserRoleType.Customer;
        if (User.Identity?.IsAuthenticated == true && User.IsInRole(UserRoles.Administrator))
        {
            assignedRole = request.RoleType;
        }
        else if (request.RoleType != UserRoleType.Customer)
        {
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Public registration is only permitted for the Customer role. Elevated roles must be provisioned by an Administrator."
            });
        }

        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing != null)
            return BadRequest(new AuthResponse { Success = false, Message = "Email is already registered." });

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            RoleType = assignedRole,
            Address = request.Address,
            City = request.City,
            EmailConfirmed = true,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = string.Join(", ", result.Errors.Select(e => e.Description))
            });
        }

        await _userManager.AddToRoleAsync(user, assignedRole.ToString());
        var roles = new List<string> { assignedRole.ToString() };
        var (token, refreshToken, expiresAt) = await _jwtTokenService.GenerateTokenWithRefreshTokenAsync(user, roles);

        return Ok(new AuthResponse
        {
            Success = true,
            Token = token,
            RefreshToken = refreshToken,
            Expiration = expiresAt,
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = assignedRole.ToString(),
            Message = "User registered successfully."
        });
    }

    [HttpPost("refresh-token")]
    public async Task<ActionResult<AuthResponse>> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var response = await _jwtTokenService.RefreshTokenAsync(request.Token, request.RefreshToken);
        if (!response.Success)
            return BadRequest(response);

        return Ok(response);
    }

    [HttpPost("revoke-token")]
    public async Task<IActionResult> RevokeToken([FromBody] RevokeTokenRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var success = await _jwtTokenService.RevokeRefreshTokenAsync(request.RefreshToken);
        if (!success)
            return NotFound(new { message = "Token not found or already revoked." });

        return Ok(new { message = "Token successfully revoked." });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> GetCurrentUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return NotFound();

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new UserProfileDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber ?? string.Empty,
            Role = roles.FirstOrDefault() ?? user.RoleType.ToString(),
            Address = user.Address,
            City = user.City,
            ServiceCenterId = user.ServiceCenterId,
            CompanyFleetId = user.CompanyFleetId
        });
    }
}
