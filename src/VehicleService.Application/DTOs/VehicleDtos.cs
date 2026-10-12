using System.ComponentModel.DataAnnotations;
using VehicleService.Domain.Enums;

namespace VehicleService.Application.DTOs;

public class VehicleDto
{
    public int Id { get; set; }
    public string RegistrationNumber { get; set; } = string.Empty;
    public string VIN { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
    public int ManufacturingYear { get; set; }
    public string Color { get; set; } = string.Empty;
    public VehicleFuelType FuelType { get; set; }
    public TransmissionType Transmission { get; set; }
    public int CurrentMileage { get; set; }
    public bool IsActive { get; set; } = true;
    
    public string? InsuranceProvider { get; set; }
    public string? InsurancePolicyNumber { get; set; }
    public DateTime? InsuranceExpiryDate { get; set; }

    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int? CompanyFleetId { get; set; }
    public string? CompanyFleetName { get; set; }

    public int LastServiceMileage { get; set; }
    public DateTime? LastServiceDate { get; set; }
    public int NextServiceDueMileage { get; set; }
    public DateTime? NextServiceDueDate { get; set; }
    public bool IsMaintenanceDue { get; set; }

    public string? CurrentDriverName { get; set; }
    public int ActiveJobCount { get; set; }
}

public class CreateVehicleDto
{
    [Required(ErrorMessage = "Registration number is required.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Registration number must be between 3 and 20 characters.")]
    public string RegistrationNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "VIN is required.")]
    [StringLength(17, MinimumLength = 11, ErrorMessage = "VIN must be between 11 and 17 characters.")]
    public string VIN { get; set; } = string.Empty;

    [Required(ErrorMessage = "Make is required.")]
    [StringLength(50, ErrorMessage = "Make cannot exceed 50 characters.")]
    public string Make { get; set; } = string.Empty;

    [Required(ErrorMessage = "Model is required.")]
    [StringLength(50, ErrorMessage = "Model cannot exceed 50 characters.")]
    public string Model { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Variant cannot exceed 50 characters.")]
    public string Variant { get; set; } = string.Empty;

    [Range(1900, 2030, ErrorMessage = "Manufacturing year must be between 1900 and 2030.")]
    public int ManufacturingYear { get; set; } = DateTime.UtcNow.Year;

    [StringLength(30, ErrorMessage = "Color cannot exceed 30 characters.")]
    public string Color { get; set; } = "White";

    public VehicleFuelType FuelType { get; set; } = VehicleFuelType.Petrol;
    public TransmissionType Transmission { get; set; } = TransmissionType.Automatic;

    [Range(0, 1000000, ErrorMessage = "Current mileage must be between 0 and 1,000,000.")]
    public int CurrentMileage { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(100)]
    public string? InsuranceProvider { get; set; }

    [StringLength(50)]
    public string? InsurancePolicyNumber { get; set; }

    public DateTime? InsuranceExpiryDate { get; set; }

    public string? CustomerId { get; set; }
    public int? CompanyFleetId { get; set; }
}

public class DriverAssignmentDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Valid Vehicle ID is required.")]
    public int VehicleId { get; set; }

    [Required(ErrorMessage = "Driver ID is required.")]
    public string DriverId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Driver name is required.")]
    [StringLength(100, ErrorMessage = "Driver name cannot exceed 100 characters.")]
    public string DriverName { get; set; } = string.Empty;

    [StringLength(50)]
    public string? LicenseNumber { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
