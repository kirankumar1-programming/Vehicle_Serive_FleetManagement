using VehicleService.Domain.Common;

namespace VehicleService.Domain.Entities;

public class VehicleInspection : BaseEntity
{
    public int JobCardId { get; set; }
    public virtual ServiceJobCard? JobCard { get; set; }

    public int VehicleId { get; set; }
    public virtual Vehicle? Vehicle { get; set; }

    public string InspectedByUserId { get; set; } = string.Empty;
    public virtual ApplicationUser? InspectedByUser { get; set; }

    public int Mileage { get; set; }
    public string FuelLevel { get; set; } = "50%"; // e.g. "25%", "50%", "75%", "Full"

    // Condition Ratings (Good, Fair, NeedsAttention, Critical)
    public string EngineCondition { get; set; } = "Good";
    public string BrakeCondition { get; set; } = "Good";
    public string TyreCondition { get; set; } = "Good";
    public string BatteryCondition { get; set; } = "Good";
    public string FluidLevels { get; set; } = "Normal";
    public string ACPerformance { get; set; } = "Good";
    public string SuspensionCondition { get; set; } = "Good";
    public string ElectricalsCondition { get; set; } = "Good";

    public string? ExistingDamages { get; set; } // Scratches, dents
    public string? InspectionSummary { get; set; }

    public virtual ICollection<InspectionPhoto> InspectionPhotos { get; set; } = new List<InspectionPhoto>();
}

public class InspectionPhoto : BaseEntity
{
    public int InspectionId { get; set; }
    public virtual VehicleInspection? Inspection { get; set; }

    public string PhotoUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string PhotoCategory { get; set; } = "Inspection"; // "Before", "Damage", "Inspection", "After", "ReplacedPart"
}
