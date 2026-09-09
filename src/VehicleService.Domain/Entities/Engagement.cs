using VehicleService.Domain.Common;
using VehicleService.Domain.Enums;

namespace VehicleService.Domain.Entities;

public class CustomerReview : BaseEntity
{
    public int JobCardId { get; set; }
    public virtual ServiceJobCard? JobCard { get; set; }

    public string CustomerId { get; set; } = string.Empty;
    public virtual ApplicationUser? Customer { get; set; }

    public int OverallRating { get; set; } = 5; // 1-5 Stars
    public int ServiceQualityScore { get; set; } = 5;
    public int StaffRating { get; set; } = 5;
    public int CleanlinessRating { get; set; } = 5;

    public string Comments { get; set; } = string.Empty;
    public ReviewStatus Status { get; set; } = ReviewStatus.Approved;
    public string? ModerationNotes { get; set; }
}

public class CustomerComplaint : BaseEntity
{
    public string TicketNumber { get; set; } = string.Empty; // e.g. "CMP-2026-0012"

    public string CustomerId { get; set; } = string.Empty;
    public virtual ApplicationUser? Customer { get; set; }

    public int? VehicleId { get; set; }
    public virtual Vehicle? Vehicle { get; set; }

    public int? JobCardId { get; set; }
    public virtual ServiceJobCard? JobCard { get; set; }

    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;
    public ComplaintStatus Status { get; set; } = ComplaintStatus.Open;

    public string? AssignedToUserId { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class Notification : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public virtual ApplicationUser? User { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; } = NotificationType.System;
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }
}

public class AuditLog : BaseEntity
{
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = string.Empty; // e.g. "Created", "Updated", "Deleted", "ApprovedEstimate"
    public string EntityName { get; set; } = string.Empty; // e.g. "InventoryPart", "ServiceJobCard"
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
