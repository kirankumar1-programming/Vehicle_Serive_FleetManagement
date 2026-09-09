using VehicleService.Domain.Common;
using VehicleService.Domain.Enums;

namespace VehicleService.Domain.Entities;

public class ServiceCenter : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string OperatingHours { get; set; } = "8:00 AM - 6:00 PM";
    public int MaxDailyCapacity { get; set; } = 20;
    public bool IsActive { get; set; } = true;

    public virtual ICollection<ServiceBay> ServiceBays { get; set; } = new List<ServiceBay>();
    public virtual ICollection<ApplicationUser> StaffMembers { get; set; } = new List<ApplicationUser>();
    public virtual ICollection<ServiceAppointment> Appointments { get; set; } = new List<ServiceAppointment>();
    public virtual ICollection<InventoryPart> InventoryParts { get; set; } = new List<InventoryPart>();
}

public class ServiceBay : BaseEntity
{
    public int ServiceCenterId { get; set; }
    public virtual ServiceCenter? ServiceCenter { get; set; }

    public string BayNumber { get; set; } = string.Empty;
    public string BayName { get; set; } = string.Empty;
    public string? BayType { get; set; } // General, Express, Heavy, Alignment, Painting
    public BayStatus Status { get; set; } = BayStatus.Available;
    public bool IsActive { get; set; } = true;

    public virtual ICollection<ServiceJobCard> ActiveJobCards { get; set; } = new List<ServiceJobCard>();
    public virtual ICollection<ServiceAppointment> Appointments { get; set; } = new List<ServiceAppointment>();
}
