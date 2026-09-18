using System.ComponentModel.DataAnnotations;
using VehicleService.Domain.Enums;

namespace VehicleService.Application.DTOs;

public class CreateServiceCenterDto
{
    [Required(ErrorMessage = "Service Center Name is required")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Center Code is required")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Address is required")]
    [StringLength(200)]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "City is required")]
    [StringLength(100)]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "State is required")]
    [StringLength(100)]
    public string State { get; set; } = string.Empty;

    [Phone]
    [StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Operating Hours is required")]
    [StringLength(100)]
    public string OperatingHours { get; set; } = "08:00 AM - 06:00 PM";

    [Range(1, 500, ErrorMessage = "Daily capacity must be between 1 and 500")]
    public int MaxDailyCapacity { get; set; } = 20;

    public bool IsActive { get; set; } = true;

    [Range(0, 20, ErrorMessage = "Initial bays count must be between 0 and 20")]
    public int InitialBaysCount { get; set; } = 2;
}

public class UpdateServiceCenterDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Service Center Name is required")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Center Code is required")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Address is required")]
    [StringLength(200)]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "City is required")]
    [StringLength(100)]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "State is required")]
    [StringLength(100)]
    public string State { get; set; } = string.Empty;

    [Phone]
    [StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Operating Hours is required")]
    [StringLength(100)]
    public string OperatingHours { get; set; } = "8:00 AM - 6:00 PM";

    [Range(1, 500, ErrorMessage = "Daily capacity must be between 1 and 500")]
    public int MaxDailyCapacity { get; set; } = 20;

    public bool IsActive { get; set; } = true;
}

public class UpdateServiceBayDto
{
    public int Id { get; set; }

    public int ServiceCenterId { get; set; }

    public string? ServiceCenterName { get; set; }

    [Required(ErrorMessage = "Bay Number is required")]
    [StringLength(50)]
    public string BayNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bay Name is required")]
    [StringLength(100)]
    public string BayName { get; set; } = string.Empty;

    [StringLength(100)]
    public string? BayType { get; set; }

    public BayStatus Status { get; set; } = BayStatus.Available;

    public bool IsActive { get; set; } = true;
}

public class CreateCompanyFleetDto
{
    [Required(ErrorMessage = "Company Name is required")]
    [StringLength(150, ErrorMessage = "Company Name cannot exceed 150 characters")]
    public string CompanyName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Registration Number / CIN is required")]
    [StringLength(100, ErrorMessage = "Registration Number cannot exceed 100 characters")]
    public string RegistrationNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tax ID / GSTIN is required")]
    [StringLength(50, ErrorMessage = "Tax ID cannot exceed 50 characters")]
    public string TaxId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Contact Person is required")]
    [StringLength(100, ErrorMessage = "Contact Person cannot exceed 100 characters")]
    public string ContactPerson { get; set; } = string.Empty;

    [Required(ErrorMessage = "Contact Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [StringLength(100, ErrorMessage = "Contact Email cannot exceed 100 characters")]
    public string ContactEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Contact Phone is required")]
    [Phone(ErrorMessage = "Please enter a valid phone number")]
    [StringLength(30, ErrorMessage = "Contact Phone cannot exceed 30 characters")]
    public string ContactPhone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Address is required")]
    [StringLength(200, ErrorMessage = "Address cannot exceed 200 characters")]
    public string Address { get; set; } = string.Empty;

    [Range(0, 100, ErrorMessage = "Corporate discount rate must be between 0% and 100%")]
    public decimal CorporateDiscountRate { get; set; } = 10.0m;

    [Range(0, 100000000, ErrorMessage = "Monthly budget limit must be non-negative")]
    public decimal MonthlyBudgetLimit { get; set; } = 100000m;

    public bool IsActive { get; set; } = true;
}

public class UpdateCompanyFleetDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Company Name is required")]
    [StringLength(150, ErrorMessage = "Company Name cannot exceed 150 characters")]
    public string CompanyName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Registration Number / CIN is required")]
    [StringLength(100, ErrorMessage = "Registration Number cannot exceed 100 characters")]
    public string RegistrationNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tax ID / GSTIN is required")]
    [StringLength(50, ErrorMessage = "Tax ID cannot exceed 50 characters")]
    public string TaxId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Contact Person is required")]
    [StringLength(100, ErrorMessage = "Contact Person cannot exceed 100 characters")]
    public string ContactPerson { get; set; } = string.Empty;

    [Required(ErrorMessage = "Contact Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [StringLength(100, ErrorMessage = "Contact Email cannot exceed 100 characters")]
    public string ContactEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Contact Phone is required")]
    [Phone(ErrorMessage = "Please enter a valid phone number")]
    [StringLength(30, ErrorMessage = "Contact Phone cannot exceed 30 characters")]
    public string ContactPhone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Address is required")]
    [StringLength(200, ErrorMessage = "Address cannot exceed 200 characters")]
    public string Address { get; set; } = string.Empty;

    [Range(0, 100, ErrorMessage = "Corporate discount rate must be between 0% and 100%")]
    public decimal CorporateDiscountRate { get; set; } = 10.0m;

    [Range(0, 100000000, ErrorMessage = "Monthly budget limit must be non-negative")]
    public decimal MonthlyBudgetLimit { get; set; } = 100000m;

    public bool IsActive { get; set; } = true;
}

