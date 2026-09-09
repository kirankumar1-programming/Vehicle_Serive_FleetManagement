using VehicleService.Domain.Common;
using VehicleService.Domain.Enums;

namespace VehicleService.Domain.Entities;

public class RoadsideAssistanceRequest : BaseEntity
{
    public string RequestNumber { get; set; } = string.Empty; // e.g. "RSA-2026-0044"

    public string CustomerId { get; set; } = string.Empty;
    public virtual ApplicationUser? Customer { get; set; }

    public int VehicleId { get; set; }
    public virtual Vehicle? Vehicle { get; set; }

    public RoadsideRequestType RequestType { get; set; } = RoadsideRequestType.Breakdown;
    public RoadsideStatus Status { get; set; } = RoadsideStatus.Requested;

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

    public virtual ICollection<RoadsideLog> Logs { get; set; } = new List<RoadsideLog>();
}

public class RoadsideLog : BaseEntity
{
    public int RoadsideAssistanceRequestId { get; set; }
    public virtual RoadsideAssistanceRequest? RoadsideAssistanceRequest { get; set; }

    public RoadsideStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
