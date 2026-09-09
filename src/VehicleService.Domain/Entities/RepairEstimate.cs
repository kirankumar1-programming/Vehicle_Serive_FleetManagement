using VehicleService.Domain.Common;
using VehicleService.Domain.Enums;

namespace VehicleService.Domain.Entities;

public class RepairEstimate : BaseEntity
{
    public string EstimateNumber { get; set; } = string.Empty; // e.g. "EST-2026-0034"
    
    public int JobCardId { get; set; }
    public virtual ServiceJobCard? JobCard { get; set; }

    public string PreparedByUserId { get; set; } = string.Empty;
    public virtual ApplicationUser? PreparedByUser { get; set; }

    public decimal TotalPartsCost { get; set; }
    public decimal TotalLaborCost { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public EstimateApprovalStatus ApprovalStatus { get; set; } = EstimateApprovalStatus.Pending;
    public string? CustomerNotes { get; set; }
    public DateTime? CustomerRespondedAt { get; set; }
    public string? ClarificationQuestion { get; set; }
    public string? AdvisorReply { get; set; }

    public virtual ICollection<EstimateItem> Items { get; set; } = new List<EstimateItem>();
}

public class EstimateItem : BaseEntity
{
    public int RepairEstimateId { get; set; }
    public virtual RepairEstimate? RepairEstimate { get; set; }

    public string Description { get; set; } = string.Empty;
    public EstimateItemType ItemType { get; set; } = EstimateItemType.Part;

    public int? InventoryPartId { get; set; }
    public virtual InventoryPart? InventoryPart { get; set; }

    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LaborCharges { get; set; }
    public decimal TaxPercent { get; set; } = 18.0m;
    public decimal LineTotal { get; set; }

    public bool IsApprovedByCustomer { get; set; } = true;
    public bool IsOptional { get; set; } = false;
    public string? Reason { get; set; }
}

public class WorkLog : BaseEntity
{
    public int JobCardId { get; set; }
    public virtual ServiceJobCard? JobCard { get; set; }

    public string MechanicId { get; set; } = string.Empty;
    public virtual ApplicationUser? Mechanic { get; set; }

    public string TaskDescription { get; set; } = string.Empty;
    public decimal HoursSpent { get; set; }
    public DateTime LoggedAt { get; set; } = DateTime.UtcNow;
    public string? Observations { get; set; }
}

public class PartConsumption : BaseEntity
{
    public int JobCardId { get; set; }
    public virtual ServiceJobCard? JobCard { get; set; }

    public int InventoryPartId { get; set; }
    public virtual InventoryPart? InventoryPart { get; set; }

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? LoggedByUserId { get; set; }
    public DateTime ConsumedAt { get; set; } = DateTime.UtcNow;
}
