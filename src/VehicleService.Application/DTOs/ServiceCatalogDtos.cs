using System.ComponentModel.DataAnnotations;

namespace VehicleService.Application.DTOs;

public class UpdateServiceTypeDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Service Name is required")]
    [StringLength(100)]
    [Display(Name = "Service Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Service Code")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required")]
    [StringLength(1000)]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Estimated duration is required")]
    [Range(0.1, 100.0, ErrorMessage = "Estimated duration must be greater than 0")]
    [Display(Name = "Estimated Duration (Hours)")]
    public decimal EstimatedDurationHours { get; set; } = 2.0m;

    [Required(ErrorMessage = "Base price is required")]
    [Range(0, 1000000, ErrorMessage = "Base price must be a non-negative value")]
    [Display(Name = "Base Price")]
    public decimal BasePrice { get; set; }

    [Display(Name = "Required Parts")]
    [StringLength(500)]
    public string? RecommendedPartsSummary { get; set; }

    [Required(ErrorMessage = "Labor charges is required")]
    [Range(0, 1000000, ErrorMessage = "Labor charges must be a non-negative value")]
    [Display(Name = "Labor Charges")]
    public decimal LaborCharges { get; set; }

    [Required(ErrorMessage = "Applicable taxes percent is required")]
    [Range(0, 100, ErrorMessage = "Applicable tax percent must be between 0 and 100")]
    [Display(Name = "Applicable Taxes (%)")]
    public decimal ApplicableTaxPercent { get; set; } = 18.0m;

    [Required(ErrorMessage = "Warranty period in days is required")]
    [Range(0, 3650, ErrorMessage = "Warranty period must be between 0 and 3650 days")]
    [Display(Name = "Warranty Period (Days)")]
    public int WarrantyPeriodDays { get; set; } = 90;

    [Display(Name = "Category")]
    [StringLength(100)]
    public string? Category { get; set; }

    [Display(Name = "Is Active")]
    public bool IsActive { get; set; } = true;
}

public class CreateCouponDto
{
    [Required(ErrorMessage = "Coupon Code is required")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Coupon Code must be between 3 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "Coupon Code can only contain letters, numbers, hyphens, and underscores")]
    [Display(Name = "Coupon Code")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Coupon Title is required")]
    [StringLength(150, ErrorMessage = "Coupon Title cannot exceed 150 characters")]
    [Display(Name = "Campaign Title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Discount Type is required")]
    [Display(Name = "Discount Type")]
    public VehicleService.Domain.Enums.DiscountType Type { get; set; } = VehicleService.Domain.Enums.DiscountType.Percentage;

    [Required(ErrorMessage = "Discount Value is required")]
    [Range(0.01, 1000000.0, ErrorMessage = "Discount Value must be greater than 0")]
    [Display(Name = "Discount Value")]
    public decimal Value { get; set; }

    [Required(ErrorMessage = "Minimum Bill Amount is required")]
    [Range(0, 10000000.0, ErrorMessage = "Minimum Bill Amount must be non-negative")]
    [Display(Name = "Minimum Bill Amount")]
    public decimal MinimumBillAmount { get; set; } = 1000m;

    [Required(ErrorMessage = "Max Discount Limit is required")]
    [Range(0, 10000000.0, ErrorMessage = "Max Discount Limit must be non-negative")]
    [Display(Name = "Maximum Discount Cap")]
    public decimal MaxDiscountLimit { get; set; } = 2000m;

    [Required(ErrorMessage = "Usage Limit is required")]
    [Range(1, 1000000, ErrorMessage = "Usage Limit must be at least 1")]
    [Display(Name = "Usage Limit")]
    public int UsageLimit { get; set; } = 1000;

    [Required(ErrorMessage = "Expiry Date is required")]
    [Display(Name = "Expiry Date")]
    public DateTime ExpiryDate { get; set; } = DateTime.UtcNow.AddMonths(6);

    [Display(Name = "Is Active")]
    public bool IsActive { get; set; } = true;
}

public class UpdateCouponDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Coupon Code is required")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Coupon Code must be between 3 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "Coupon Code can only contain letters, numbers, hyphens, and underscores")]
    [Display(Name = "Coupon Code")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Coupon Title is required")]
    [StringLength(150, ErrorMessage = "Coupon Title cannot exceed 150 characters")]
    [Display(Name = "Campaign Title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Discount Type is required")]
    [Display(Name = "Discount Type")]
    public VehicleService.Domain.Enums.DiscountType Type { get; set; } = VehicleService.Domain.Enums.DiscountType.Percentage;

    [Required(ErrorMessage = "Discount Value is required")]
    [Range(0.01, 1000000.0, ErrorMessage = "Discount Value must be greater than 0")]
    [Display(Name = "Discount Value")]
    public decimal Value { get; set; }

    [Required(ErrorMessage = "Minimum Bill Amount is required")]
    [Range(0, 10000000.0, ErrorMessage = "Minimum Bill Amount must be non-negative")]
    [Display(Name = "Minimum Bill Amount")]
    public decimal MinimumBillAmount { get; set; } = 1000m;

    [Required(ErrorMessage = "Max Discount Limit is required")]
    [Range(0, 10000000.0, ErrorMessage = "Max Discount Limit must be non-negative")]
    [Display(Name = "Maximum Discount Cap")]
    public decimal MaxDiscountLimit { get; set; } = 2000m;

    [Required(ErrorMessage = "Usage Limit is required")]
    [Range(1, 1000000, ErrorMessage = "Usage Limit must be at least 1")]
    [Display(Name = "Usage Limit")]
    public int UsageLimit { get; set; } = 1000;

    [Display(Name = "Times Used")]
    public int TimesUsed { get; set; } = 0;

    [Required(ErrorMessage = "Expiry Date is required")]
    [Display(Name = "Expiry Date")]
    public DateTime ExpiryDate { get; set; } = DateTime.UtcNow.AddMonths(6);

    [Display(Name = "Is Active")]
    public bool IsActive { get; set; } = true;
}

public class CreateServicePackageDto
{
    [Required(ErrorMessage = "Package Name is required")]
    [StringLength(150, ErrorMessage = "Package Name cannot exceed 150 characters")]
    [Display(Name = "Package Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required")]
    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Package Price is required")]
    [Range(0.01, 1000000.0, ErrorMessage = "Package Price must be greater than 0")]
    [Display(Name = "Bundled Package Price (₹)")]
    public decimal PackagePrice { get; set; }

    [Display(Name = "Original Total Price (₹)")]
    [Range(0, 1000000.0, ErrorMessage = "Original Total Price must be non-negative")]
    public decimal OriginalTotalPrice { get; set; }

    [Display(Name = "Savings Percentage (%)")]
    [Range(0, 100.0, ErrorMessage = "Savings percentage must be between 0 and 100")]
    public decimal SavingsPercent { get; set; }

    [Required(ErrorMessage = "Validity in days is required")]
    [Range(1, 3650, ErrorMessage = "Validity must be between 1 and 3650 days")]
    [Display(Name = "Validity (Days)")]
    public int ValidityDays { get; set; } = 365;

    [Display(Name = "Is Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Included Services")]
    public List<int> SelectedServiceTypeIds { get; set; } = new();
}

public class UpdateServicePackageDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Package Name is required")]
    [StringLength(150, ErrorMessage = "Package Name cannot exceed 150 characters")]
    [Display(Name = "Package Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required")]
    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Package Price is required")]
    [Range(0.01, 1000000.0, ErrorMessage = "Package Price must be greater than 0")]
    [Display(Name = "Bundled Package Price (₹)")]
    public decimal PackagePrice { get; set; }

    [Display(Name = "Original Total Price (₹)")]
    [Range(0, 1000000.0, ErrorMessage = "Original Total Price must be non-negative")]
    public decimal OriginalTotalPrice { get; set; }

    [Display(Name = "Savings Percentage (%)")]
    [Range(0, 100.0, ErrorMessage = "Savings percentage must be between 0 and 100")]
    public decimal SavingsPercent { get; set; }

    [Required(ErrorMessage = "Validity in days is required")]
    [Range(1, 3650, ErrorMessage = "Validity must be between 1 and 3650 days")]
    [Display(Name = "Validity (Days)")]
    public int ValidityDays { get; set; } = 365;

    [Display(Name = "Is Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Included Services")]
    public List<int> SelectedServiceTypeIds { get; set; } = new();
}
