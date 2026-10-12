using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;

namespace VehicleService.Infrastructure.Auth;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<ApplicationUser> _userManager;

    public JwtTokenService(
        IConfiguration configuration,
        IUnitOfWork unitOfWork,
        UserManager<ApplicationUser> userManager)
    {
        _configuration = configuration;
        _unitOfWork = unitOfWork;
        _userManager = userManager;
    }

    public string GenerateToken(ApplicationUser user, IList<string> roles)
    {
        var (accessToken, _, _) = GenerateTokenInternal(user, roles);
        return accessToken;
    }

    public async Task<(string AccessToken, string RefreshToken, DateTime ExpiresAt)> GenerateTokenWithRefreshTokenAsync(ApplicationUser user, IList<string> roles)
    {
        var (accessToken, jti, expiresAt) = GenerateTokenInternal(user, roles);

        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var refreshTokenString = Convert.ToBase64String(randomBytes);

        var refreshDays = int.TryParse(_configuration["Jwt:RefreshTokenExpiryDays"], out var days) ? days : 7;
        var refreshToken = new RefreshToken
        {
            Token = refreshTokenString,
            JwtId = jti,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(refreshDays),
            IsRevoked = false
        };

        await _unitOfWork.Repository<RefreshToken>().AddAsync(refreshToken);
        await _unitOfWork.SaveChangesAsync();

        return (accessToken, refreshTokenString, expiresAt);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string token, string refreshToken)
    {
        var principal = GetPrincipalFromExpiredToken(token);
        if (principal == null)
        {
            return new AuthResponse { Success = false, Message = "Invalid access token." };
        }

        var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(jti))
        {
            return new AuthResponse { Success = false, Message = "Token claims are missing or malformed." };
        }

        var tokenRepo = _unitOfWork.Repository<RefreshToken>();
        var storedTokens = await tokenRepo.FindAsync(r => r.Token == refreshToken);
        var storedToken = storedTokens.FirstOrDefault();

        if (storedToken == null)
        {
            return new AuthResponse { Success = false, Message = "Refresh token does not exist." };
        }

        if (storedToken.UserId != userId || storedToken.JwtId != jti)
        {
            return new AuthResponse { Success = false, Message = "Refresh token mismatch." };
        }

        if (storedToken.ExpiresAt <= DateTime.UtcNow)
        {
            return new AuthResponse { Success = false, Message = "Refresh token has expired." };
        }

        if (storedToken.IsRevoked || storedToken.IsDeleted)
        {
            return new AuthResponse { Success = false, Message = "Refresh token has been revoked." };
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            return new AuthResponse { Success = false, Message = "User is invalid or inactive." };
        }

        var roles = await _userManager.GetRolesAsync(user);

        // Issue new token pair
        var (newAccessToken, newRefreshToken, expiresAt) = await GenerateTokenWithRefreshTokenAsync(user, roles);

        // Invalidate previous refresh token and mark replaced
        storedToken.IsRevoked = true;
        storedToken.ReplacedByToken = newRefreshToken;
        storedToken.UpdatedAt = DateTime.UtcNow;
        await tokenRepo.UpdateAsync(storedToken);
        await _unitOfWork.SaveChangesAsync();

        return new AuthResponse
        {
            Success = true,
            Token = newAccessToken,
            RefreshToken = newRefreshToken,
            Expiration = expiresAt,
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = roles.FirstOrDefault() ?? user.RoleType.ToString(),
            Message = "Token refreshed successfully."
        };
    }

    public async Task<bool> RevokeRefreshTokenAsync(string refreshToken)
    {
        var tokenRepo = _unitOfWork.Repository<RefreshToken>();
        var storedTokens = await tokenRepo.FindAsync(r => r.Token == refreshToken);
        var storedToken = storedTokens.FirstOrDefault();

        if (storedToken == null || storedToken.IsRevoked)
        {
            return false;
        }

        storedToken.IsRevoked = true;
        storedToken.UpdatedAt = DateTime.UtcNow;
        await tokenRepo.UpdateAsync(storedToken);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var secret = _configuration["Jwt:Secret"] ?? "VehicleServiceFleetManagementSecureKey2026!#$LongEnoughForHmacSha256Signature";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidIssuer = _configuration["Jwt:Issuer"] ?? "VehicleServiceAPI",
            ValidAudience = _configuration["Jwt:Audience"] ?? "VehicleServiceClient",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = false // Token might be expired when calling refresh
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);
            if (securityToken is not JwtSecurityToken jwtSecurityToken ||
                !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }

    private (string Token, string Jti, DateTime ExpiresAt) GenerateTokenInternal(ApplicationUser user, IList<string> roles)
    {
        var secret = _configuration["Jwt:Secret"] ?? "VehicleServiceFleetManagementSecureKey2026!#$LongEnoughForHmacSha256Signature";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiryMinutes = int.TryParse(_configuration["Jwt:ExpiryMinutes"], out var min) ? min : 60;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);
        var jti = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.FullName),
            new("RoleType", user.RoleType.ToString()),
            new(JwtRegisteredClaimNames.Jti, jti)
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "VehicleServiceAPI",
            audience: _configuration["Jwt:Audience"] ?? "VehicleServiceClient",
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), jti, expiresAt);
    }
}