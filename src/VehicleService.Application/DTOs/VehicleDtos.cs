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
    public string RegistrationNumber { get; set; } = string.Empty;
    public string VIN { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
    public int ManufacturingYear { get; set; } = DateTime.UtcNow.Year;
    public string Color { get; set; } = "White";
    public VehicleFuelType FuelType { get; set; } = VehicleFuelType.Petrol;
    public TransmissionType Transmission { get; set; } = TransmissionType.Automatic;
    public int CurrentMileage { get; set; }
    public bool IsActive { get; set; } = true;
    public string? InsuranceProvider { get; set; }
    public string? InsurancePolicyNumber { get; set; }
    public DateTime? InsuranceExpiryDate { get; set; }
    public string? CustomerId { get; set; }
    public int? CompanyFleetId { get; set; }
}

public class DriverAssignmentDto
{
    public int VehicleId { get; set; }
    public string DriverId { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string? LicenseNumber { get; set; }
    public string? Notes { get; set; }
}
