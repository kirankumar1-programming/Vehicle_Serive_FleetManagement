using System.ComponentModel.DataAnnotations;
using VehicleService.Domain.Enums;

namespace VehicleService.Application.DTOs;

public class RoadsideRequestDto
{
    public int Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public int VehicleId { get; set; }
    public string VehicleInfo { get; set; } = string.Empty;
    public string RegistrationNumber { get; set; } = string.Empty;
    public RoadsideRequestType RequestType { get; set; }
    public RoadsideStatus Status { get; set; }
    public string LocationAddress { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string ContactPhone { get; set; } = string.Empty;
    public string? IssueDescription { get; set; }
    public string? AssignedTechnicianName { get; set; }
    public string? AssignedTechnicianPhone { get; set; }
    public string? TowTruckPlate { get; set; }
    public DateTime? EstimatedArrivalTime { get; set; }
    public DateTime? ActualArrivalTime { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateRoadsideRequestDto
{
    public string? CustomerId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Valid Vehicle ID is required.")]
    public int VehicleId { get; set; }

    public RoadsideRequestType RequestType { get; set; } = RoadsideRequestType.Breakdown;

    [Required(ErrorMessage = "Location address is required.")]
    [StringLength(300, ErrorMessage = "Location address cannot exceed 300 characters.")]
    public string LocationAddress { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    [Required(ErrorMessage = "Contact phone number is required.")]
    [Phone(ErrorMessage = "Invalid phone number.")]
    [StringLength(20, ErrorMessage = "Contact phone cannot exceed 20 characters.")]
    public string ContactPhone { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Issue description cannot exceed 1000 characters.")]
    public string? IssueDescription { get; set; }
}

public class UpdateRoadsideRequestDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Valid Request ID is required.")]
    public int RequestId { get; set; }

    public RoadsideRequestType? RequestType { get; set; }
    public RoadsideStatus Status { get; set; }

    [StringLength(300, ErrorMessage = "Location address cannot exceed 300 characters.")]
    public string LocationAddress { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Invalid phone number.")]
    [StringLength(20, ErrorMessage = "Contact phone cannot exceed 20 characters.")]
    public string ContactPhone { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Issue description cannot exceed 1000 characters.")]
    public string? IssueDescription { get; set; }

    [StringLength(100)]
    public string? AssignedTechnicianName { get; set; }

    [StringLength(20)]
    public string? AssignedTechnicianPhone { get; set; }

    [StringLength(30)]
    public string? TowTruckPlate { get; set; }

    public DateTime? EstimatedArrivalTime { get; set; }

    [StringLength(1000)]
    public string? ResolutionNotes { get; set; }
}

public class CustomerReviewDto
{
    public int Id { get; set; }
    public int JobCardId { get; set; }
    public string JobCardNumber { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public int OverallRating { get; set; }
    public int ServiceQualityScore { get; set; }
    public int StaffRating { get; set; }
    public int CleanlinessRating { get; set; }
    public string Comments { get; set; } = string.Empty;
    public ReviewStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CustomerComplaintDto
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public int? VehicleId { get; set; }
    public string? VehicleInfo { get; set; }
    public int? JobCardId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PriorityLevel Priority { get; set; }
    public ComplaintStatus Status { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NotificationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditLogDto
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime Timestamp { get; set; }
}
