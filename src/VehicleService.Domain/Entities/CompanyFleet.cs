using VehicleService.Domain.Common;

namespace VehicleService.Domain.Entities;

public class CompanyFleet : BaseEntity
{
    public string CompanyName { get; set; } = string.Empty;
    public string RegistrationNumber { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public decimal CorporateDiscountRate { get; set; } = 10.0m;
    public decimal MonthlyBudgetLimit { get; set; } = 100000m;
    public bool IsActive { get; set; } = true;

    public virtual ICollection<ApplicationUser> ManagersAndDrivers { get; set; } = new List<ApplicationUser>();
    public virtual ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
}

public class DriverAssignment : BaseEntity
{
    public int VehicleId { get; set; }
    public virtual Vehicle? Vehicle { get; set; }

    public string DriverId { get; set; } = string.Empty;
    public virtual ApplicationUser? Driver { get; set; }

    public DateTime AssignedFrom { get; set; } = DateTime.UtcNow;
    public DateTime? AssignedTo { get; set; }
    public string? LicenseNumber { get; set; }
    public bool IsCurrentDriver { get; set; } = true;
    public string? Notes { get; set; }
}
