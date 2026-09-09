using Microsoft.AspNetCore.Identity;
using VehicleService.Domain.Enums;

namespace VehicleService.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public UserRoleType RoleType { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Optional linkage to Company Fleet or Service Center
    public int? CompanyFleetId { get; set; }
    public virtual CompanyFleet? CompanyFleet { get; set; }

    public int? ServiceCenterId { get; set; }
    public virtual ServiceCenter? ServiceCenter { get; set; }

    // Navigation collections
    public virtual ICollection<Vehicle> OwnedVehicles { get; set; } = new List<Vehicle>();
    public virtual ICollection<ServiceAppointment> AppointmentsAsCustomer { get; set; } = new List<ServiceAppointment>();
    public virtual ICollection<ServiceJobCard> JobCardsAsAdvisor { get; set; } = new List<ServiceJobCard>();
    public virtual ICollection<ServiceJobCard> JobCardsAsMechanic { get; set; } = new List<ServiceJobCard>();
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public virtual ICollection<CustomerReview> CustomerReviews { get; set; } = new List<CustomerReview>();
    public virtual ICollection<CustomerComplaint> CustomerComplaints { get; set; } = new List<CustomerComplaint>();
}

public class ApplicationRole : IdentityRole
{
    public string? Description { get; set; }
}
