using VehicleService.Domain.Common;
using VehicleService.Domain.Enums;

namespace VehicleService.Domain.Entities;

public class Warranty : BaseEntity
{
    public string WarrantyNumber { get; set; } = string.Empty; // e.g. "WAR-2026-9011"
    public WarrantyType Type { get; set; } = WarrantyType.Vehicle;

    public int VehicleId { get; set; }
    public virtual Vehicle? Vehicle { get; set; }

    public string Provider { get; set; } = "Manufacturer OEM";
    public string CoverageTerms { get; set; } = string.Empty;
    public string CoveredItemsSummary { get; set; } = "Engine, Transmission, Electricals, Brakes"; // Comma-separated or rule

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int MaxMileageLimit { get; set; } = 100000;

    public WarrantyStatus Status { get; set; } = WarrantyStatus.Active;

    public virtual ICollection<WarrantyClaim> Claims { get; set; } = new List<WarrantyClaim>();
}

public class WarrantyClaim : BaseEntity
{
    public string ClaimNumber { get; set; } = string.Empty; // e.g. "CLM-2026-0056"

    public int WarrantyId { get; set; }
    public virtual Warranty? Warranty { get; set; }

    public int? JobCardId { get; set; }
    public virtual ServiceJobCard? JobCard { get; set; }

    public string ClaimedByCustomerId { get; set; } = string.Empty;
    public string IssueDescription { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty; // e.g. "Brake Caliper"

    public decimal AmountClaimed { get; set; }
    public decimal AmountApproved { get; set; }

    public WarrantyClaimStatus Status { get; set; } = WarrantyClaimStatus.Submitted;
    public string? ReviewNotes { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
