using VehicleService.Domain.Common;

namespace VehicleService.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public string Token { get; set; } = string.Empty;
    public string JwtId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public string? ReplacedByToken { get; set; }
    public string UserId { get; set; } = string.Empty;
    public virtual ApplicationUser? User { get; set; }

    public bool IsActive => !IsRevoked && !IsDeleted && DateTime.UtcNow < ExpiresAt;
}
