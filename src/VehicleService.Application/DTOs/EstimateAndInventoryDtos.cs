using System.ComponentModel.DataAnnotations;
using VehicleService.Domain.Enums;

namespace VehicleService.Application.DTOs;

public class RepairEstimateDto
{
    public int Id { get; set; }
    public string EstimateNumber { get; set; } = string.Empty;
    public int JobCardId { get; set; }
    public string JobCardNumber { get; set; } = string.Empty;
    public string VehicleInfo { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string PreparedByUserId { get; set; } = string.Empty;
    public string PreparedByName { get; set; } = string.Empty;
    public decimal TotalPartsCost { get; set; }
    public decimal TotalLaborCost { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public EstimateApprovalStatus ApprovalStatus { get; set; }
    public string? CustomerNotes { get; set; }
    public DateTime? CustomerRespondedAt { get; set; }
    public string? ClarificationQuestion { get; set; }
    public string? AdvisorReply { get; set; }
    public AppointmentStatus? JobCardStatus { get; set; }
    public AppointmentStatus? AppointmentStatus { get; set; }
    public bool IsCancelled => JobCardStatus == VehicleService.Domain.Enums.AppointmentStatus.Cancelled || AppointmentStatus == VehicleService.Domain.Enums.AppointmentStatus.Cancelled;
    public List<EstimateItemDto> Items { get; set; } = new();
}

public class EstimateItemDto
{
    public int Id { get; set; }
    public int RepairEstimateId { get; set; }
    public string Description { get; set; } = string.Empty;
    public EstimateItemType ItemType { get; set; }
    public int? InventoryPartId { get; set; }
    public string? PartNumber { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LaborCharges { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal LineTotal { get; set; }
    public bool IsApprovedByCustomer { get; set; } = true;
    public bool IsOptional { get; set; }
    public string? Reason { get; set; }
}

public class CustomerEstimateResponseDto
{
    public int EstimateId { get; set; }
    public EstimateApprovalStatus Action { get; set; } // Approved, Rejected, ClarificationRequested
    public string? CustomerNotes { get; set; }
    public string? ClarificationQuestion { get; set; }
    public List<int>? ApprovedItemIds { get; set; } // For line-by-line item approvals
}

public class InventoryPartDto
{
    public int Id { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string CompatibleVehicles { get; set; } = string.Empty;
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public int AvailableQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int TotalStock => AvailableQuantity + ReservedQuantity;
    public int ReorderLevel { get; set; }
    public string WarehouseLocation { get; set; } = string.Empty;
    public int? ServiceCenterId { get; set; }
    public string? ServiceCenterName { get; set; }
    public int WarrantyPeriodDays { get; set; }
    public bool IsLowStock => AvailableQuantity <= ReorderLevel;
    public bool IsActive { get; set; }
}

public class AdjustStockDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Valid Part ID is required.")]
    public int PartId { get; set; }

    public InventoryTransactionType TransactionType { get; set; }

    [Range(1, 100000, ErrorMessage = "Quantity must be between 1 and 100,000.")]
    public int Quantity { get; set; }

    [Range(0, 1000000, ErrorMessage = "Unit cost must be non-negative.")]
    public decimal UnitCost { get; set; }

    [StringLength(100, ErrorMessage = "Reference number cannot exceed 100 characters.")]
    public string? ReferenceNumber { get; set; }

    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
    public string? Notes { get; set; }

    public string? PerformedByUserId { get; set; }
}
