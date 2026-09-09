using VehicleService.Domain.Common;
using VehicleService.Domain.Enums;

namespace VehicleService.Domain.Entities;

public class InventoryPart : BaseEntity
{
    public string PartNumber { get; set; } = string.Empty; // Unique SKU e.g. "BP-102"
    public string Name { get; set; } = string.Empty; // e.g. "Ceramic Brake Pad Front"
    public string Category { get; set; } = string.Empty; // Brakes, Engine, Suspension, Electrical, Fluids, Body
    public string Manufacturer { get; set; } = string.Empty; // Bosch, Mobil, Brembo, Denso
    public string Supplier { get; set; } = string.Empty;
    public string CompatibleVehicles { get; set; } = "All Models"; // e.g. "Hyundai Creta, Kia Seltos"

    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }

    public int AvailableQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int TotalStock => AvailableQuantity + ReservedQuantity;
    public int ReorderLevel { get; set; } = 5;

    public string WarehouseLocation { get; set; } = "Main Warehouse - Bay A3";
    public int? ServiceCenterId { get; set; }
    public virtual ServiceCenter? ServiceCenter { get; set; }

    public int WarrantyPeriodDays { get; set; } = 180;
    public bool IsActive { get; set; } = true;

    // Concurrency Token
    public byte[]? RowVersion { get; set; }

    public virtual ICollection<PartReservation> Reservations { get; set; } = new List<PartReservation>();
    public virtual ICollection<PartConsumption> Consumptions { get; set; } = new List<PartConsumption>();
    public virtual ICollection<InventoryTransaction> Transactions { get; set; } = new List<InventoryTransaction>();
}

public class PartReservation : BaseEntity
{
    public int InventoryPartId { get; set; }
    public virtual InventoryPart? InventoryPart { get; set; }

    public int AppointmentId { get; set; }
    public virtual ServiceAppointment? Appointment { get; set; }

    public int Quantity { get; set; }
    public bool IsReleased { get; set; } = false;
    public bool IsConsumed { get; set; } = false;
    public DateTime? ReleasedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public class InventoryTransaction : BaseEntity
{
    public int InventoryPartId { get; set; }
    public virtual InventoryPart? InventoryPart { get; set; }

    public InventoryTransactionType TransactionType { get; set; }
    public int Quantity { get; set; }
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalAmount { get; set; }

    public string? ReferenceNumber { get; set; } // PO Number, Job Card Number, or Invoice
    public string? PerformedByUserId { get; set; }
    public string? Notes { get; set; }
}

public class PurchaseOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty; // e.g. "PO-2026-0045"
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierContact { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Ordered"; // Draft, Ordered, Received, Cancelled
    public DateTime? ExpectedDeliveryDate { get; set; }
    public DateTime? ReceivedAt { get; set; }

    public virtual ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
}

public class PurchaseOrderItem : BaseEntity
{
    public int PurchaseOrderId { get; set; }
    public virtual PurchaseOrder? PurchaseOrder { get; set; }

    public int InventoryPartId { get; set; }
    public virtual InventoryPart? InventoryPart { get; set; }

    public int QuantityOrdered { get; set; }
    public int QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
}
