using VehicleService.Domain.Common;
using VehicleService.Domain.Enums;

namespace VehicleService.Domain.Entities;

public class ServiceAppointment : BaseEntity
{
    public string AppointmentNumber { get; set; } = string.Empty; // e.g. "APT-2026-00101"
    
    public int VehicleId { get; set; }
    public virtual Vehicle? Vehicle { get; set; }

    public string CustomerId { get; set; } = string.Empty;
    public virtual ApplicationUser? Customer { get; set; }

    public int ServiceCenterId { get; set; }
    public virtual ServiceCenter? ServiceCenter { get; set; }

    public int? ServiceBayId { get; set; }
    public virtual ServiceBay? ServiceBay { get; set; }

    public int? ServiceTypeId { get; set; }
    public virtual ServiceType? ServiceType { get; set; }

    public int? ServicePackageId { get; set; }
    public virtual ServicePackage? ServicePackage { get; set; }

    public DateTime AppointmentDate { get; set; }
    public string TimeSlot { get; set; } = "09:00 AM - 11:00 AM"; // e.g. "09:00 AM - 11:00 AM"
    
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;
    public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;
    
    public string? CustomerNotes { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }

    public bool RequiresPickupDrop { get; set; } = false;
    public string? PickupAddress { get; set; }

    // Concurrency Token for slot booking
    public byte[]? RowVersion { get; set; }

    public virtual ServiceJobCard? JobCard { get; set; }
    public virtual ICollection<PartReservation> PartReservations { get; set; } = new List<PartReservation>();
}

public class ServiceJobCard : BaseEntity
{
    public string JobCardNumber { get; set; } = string.Empty; // e.g. "JC-2026-00125"

    public int AppointmentId { get; set; }
    public virtual ServiceAppointment? Appointment { get; set; }

    public int VehicleId { get; set; }
    public virtual Vehicle? Vehicle { get; set; }

    public string? ServiceAdvisorId { get; set; }
    public virtual ApplicationUser? ServiceAdvisor { get; set; }

    public string? MechanicId { get; set; }
    public virtual ApplicationUser? Mechanic { get; set; }

    public int? ServiceBayId { get; set; }
    public virtual ServiceBay? ServiceBay { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.VehicleReceived;
    public int OdometerIn { get; set; }
    public int? OdometerOut { get; set; }

    public DateTime? WorkStartedAt { get; set; }
    public DateTime? WorkCompletedAt { get; set; }
    public decimal TotalLaborHours { get; set; } = 0;

    public string? AdvisorObservations { get; set; }
    public string? MechanicNotes { get; set; }
    public string? QualityCheckNotes { get; set; }
    public string? Recommendations { get; set; }

    public virtual VehicleInspection? Inspection { get; set; }
    public virtual ICollection<RepairEstimate> RepairEstimates { get; set; } = new List<RepairEstimate>();
    public virtual ICollection<WorkLog> WorkLogs { get; set; } = new List<WorkLog>();
    public virtual ICollection<PartConsumption> PartsConsumed { get; set; } = new List<PartConsumption>();
    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public virtual ICollection<CustomerReview> Reviews { get; set; } = new List<CustomerReview>();
}
