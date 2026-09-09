using VehicleService.Domain.Common;
using VehicleService.Domain.Enums;

namespace VehicleService.Domain.Entities;

public class ServiceType : BaseEntity
{
    public string Name { get; set; } = string.Empty; // e.g. "General Service", "Oil Change", "Brake Service"
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal EstimatedDurationHours { get; set; } = 2.0m;
    public decimal BasePrice { get; set; }
    public decimal LaborCharges { get; set; }
    public decimal ApplicableTaxPercent { get; set; } = 18.0m; // Standard GST/VAT
    public int WarrantyPeriodDays { get; set; } = 90;
    public string? Category { get; set; } // Periodic, Repair, Diagnostic, Bodywork
    public string? RecommendedPartsSummary { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual ICollection<ServicePackageItem> ServicePackageItems { get; set; } = new List<ServicePackageItem>();
    public virtual ICollection<ServiceAppointment> ServiceAppointments { get; set; } = new List<ServiceAppointment>();
}

public class ServicePackage : BaseEntity
{
    public string Name { get; set; } = string.Empty; // e.g. "Premium Periodic Package", "Winter Readiness Package"
    public string Description { get; set; } = string.Empty;
    public decimal PackagePrice { get; set; }
    public decimal OriginalTotalPrice { get; set; }
    public decimal SavingsPercent { get; set; }
    public int ValidityDays { get; set; } = 365;
    public bool IsActive { get; set; } = true;

    public virtual ICollection<ServicePackageItem> PackageItems { get; set; } = new List<ServicePackageItem>();
    public virtual ICollection<ServiceAppointment> ServiceAppointments { get; set; } = new List<ServiceAppointment>();
}

public class ServicePackageItem : BaseEntity
{
    public int ServicePackageId { get; set; }
    public virtual ServicePackage? ServicePackage { get; set; }

    public int ServiceTypeId { get; set; }
    public virtual ServiceType? ServiceType { get; set; }
}

public class DiscountRule : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DiscountType Type { get; set; } = DiscountType.Percentage;
    public decimal DiscountValue { get; set; } // Percentage (e.g. 15%) or Fixed Amount (e.g. 500)
    public decimal MinimumOrderAmount { get; set; } = 0;
    public decimal MaximumDiscountCap { get; set; } = 5000;
    public DateTime ValidFrom { get; set; } = DateTime.UtcNow;
    public DateTime ValidTo { get; set; } = DateTime.UtcNow.AddYears(1);
    public bool IsActive { get; set; } = true;
}

public class Coupon : BaseEntity
{
    public string Code { get; set; } = string.Empty; // e.g. "SAVE500", "FIRSTSERVICE20"
    public string Title { get; set; } = string.Empty;
    public DiscountType Type { get; set; } = DiscountType.Percentage;
    public decimal Value { get; set; }
    public decimal MinimumBillAmount { get; set; } = 1000m;
    public decimal MaxDiscountLimit { get; set; } = 2000m;
    public int UsageLimit { get; set; } = 1000;
    public int TimesUsed { get; set; } = 0;
    public DateTime ExpiryDate { get; set; } = DateTime.UtcNow.AddMonths(6);
    public bool IsActive { get; set; } = true;
}
