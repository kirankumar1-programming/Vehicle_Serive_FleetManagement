using VehicleService.Domain.Common;
using VehicleService.Domain.Enums;

namespace VehicleService.Domain.Entities;

public class Vehicle : BaseEntity
{
    public string RegistrationNumber { get; set; } = string.Empty; // License Plate (Unique)
    public string VIN { get; set; } = string.Empty; // 17-char VIN
    public string Make { get; set; } = string.Empty; // e.g. Hyundai, Toyota, Tata
    public string Model { get; set; } = string.Empty; // e.g. Creta, Fortuner, Nexon
    public string Variant { get; set; } = string.Empty; // e.g. SX (O), ZX, Fearless
    public int ManufacturingYear { get; set; }
    public string Color { get; set; } = string.Empty;
    public VehicleFuelType FuelType { get; set; } = VehicleFuelType.Petrol;
    public TransmissionType Transmission { get; set; } = TransmissionType.Automatic;
    public int CurrentMileage { get; set; } // Current Odometer in KM
    
    // Insurance
    public string? InsuranceProvider { get; set; }
    public string? InsurancePolicyNumber { get; set; }
    public DateTime? InsuranceExpiryDate { get; set; }

    // Ownership: Individual Customer or Corporate Fleet
    public string? CustomerId { get; set; }
    public virtual ApplicationUser? Customer { get; set; }

    public int? CompanyFleetId { get; set; }
    public virtual CompanyFleet? CompanyFleet { get; set; }

    // Preventive Maintenance Schedule Tracker
    public int LastServiceMileage { get; set; }
    public DateTime? LastServiceDate { get; set; }
    public int NextServiceDueMileage { get; set; }
    public DateTime? NextServiceDueDate { get; set; }

    // Navigation collections
    public virtual ICollection<DriverAssignment> DriverAssignments { get; set; } = new List<DriverAssignment>();
    public virtual ICollection<ServiceAppointment> ServiceAppointments { get; set; } = new List<ServiceAppointment>();
    public virtual ICollection<ServiceJobCard> JobCards { get; set; } = new List<ServiceJobCard>();
    public virtual ICollection<VehicleInspection> Inspections { get; set; } = new List<VehicleInspection>();
    public virtual ICollection<Warranty> Warranties { get; set; } = new List<Warranty>();
    public virtual ICollection<RoadsideAssistanceRequest> RoadsideRequests { get; set; } = new List<RoadsideAssistanceRequest>();
}
