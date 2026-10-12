using System.ComponentModel.DataAnnotations;
using VehicleService.Domain.Enums;

namespace VehicleService.Application.DTOs;

public class AppointmentDto
{
    public int Id { get; set; }
    public string AppointmentNumber { get; set; } = string.Empty;
    public int VehicleId { get; set; }
    public string VehicleInfo { get; set; } = string.Empty;
    public string RegistrationNumber { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int ServiceCenterId { get; set; }
    public string ServiceCenterName { get; set; } = string.Empty;
    public int? ServiceBayId { get; set; }
    public string? ServiceBayName { get; set; }
    public int? ServiceTypeId { get; set; }
    public string? ServiceTypeName { get; set; }
    public int? ServicePackageId { get; set; }
    public string? ServicePackageName { get; set; }
    public DateTime AppointmentDate { get; set; }
    public string TimeSlot { get; set; } = string.Empty;
    public AppointmentStatus Status { get; set; }
    public PriorityLevel Priority { get; set; }
    public string? CustomerNotes { get; set; }
    public bool RequiresPickupDrop { get; set; }
    public string? PickupAddress { get; set; }
    public int? JobCardId { get; set; }
    public string? JobCardNumber { get; set; }
}

public class BookAppointmentDto
{
    [Required(ErrorMessage = "Vehicle is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "A valid Vehicle ID is required.")]
    public int VehicleId { get; set; }

    public string? CustomerId { get; set; }

    [Required(ErrorMessage = "Service Center is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "A valid Service Center ID is required.")]
    public int ServiceCenterId { get; set; }

    public int? ServiceBayId { get; set; }
    public int? ServiceTypeId { get; set; }
    public int? ServicePackageId { get; set; }

    [Required(ErrorMessage = "Appointment date is required.")]
    public DateTime AppointmentDate { get; set; }

    [Required(ErrorMessage = "Time slot is required.")]
    [StringLength(50, ErrorMessage = "Time slot cannot exceed 50 characters.")]
    public string TimeSlot { get; set; } = "09:00 AM - 11:00 AM";

    public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;

    [StringLength(1000, ErrorMessage = "Customer notes cannot exceed 1000 characters.")]
    public string? CustomerNotes { get; set; }

    public bool RequiresPickupDrop { get; set; }

    [StringLength(300, ErrorMessage = "Pickup address cannot exceed 300 characters.")]
    public string? PickupAddress { get; set; }

    public List<int> RequiredPartIds { get; set; } = new();
}

public class BayAvailabilityDto
{
    public int BayId { get; set; }
    public string BayNumber { get; set; } = string.Empty;
    public string BayName { get; set; } = string.Empty;
    public string? BayType { get; set; }
    public BayStatus Status { get; set; }
    public bool IsAvailableForSlot { get; set; }
}

public class JobCardDto
{
    public int Id { get; set; }
    public string JobCardNumber { get; set; } = string.Empty;
    public int AppointmentId { get; set; }
    public string AppointmentNumber { get; set; } = string.Empty;
    public int VehicleId { get; set; }
    public string VehicleInfo { get; set; } = string.Empty;
    public string RegistrationNumber { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? ServiceAdvisorId { get; set; }
    public string? ServiceAdvisorName { get; set; }
    public string? MechanicId { get; set; }
    public string? MechanicName { get; set; }
    public int? ServiceBayId { get; set; }
    public string? ServiceBayName { get; set; }
    public AppointmentStatus Status { get; set; }
    public int OdometerIn { get; set; }
    public int? OdometerOut { get; set; }
    public DateTime? WorkStartedAt { get; set; }
    public DateTime? WorkCompletedAt { get; set; }
    public decimal TotalLaborHours { get; set; }
    public string? AdvisorObservations { get; set; }
    public string? MechanicNotes { get; set; }
    public string? QualityCheckNotes { get; set; }
    public string? Recommendations { get; set; }
    public int? InspectionId { get; set; }
    public int? ActiveEstimateId { get; set; }
    public EstimateApprovalStatus? EstimateStatus { get; set; }
    public int? InvoiceId { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public List<WorkLogDto> WorkLogs { get; set; } = new();
    public List<PartConsumptionDto> ConsumedParts { get; set; } = new();
}

public class WorkLogDto
{
    public int Id { get; set; }
    public int JobCardId { get; set; }
    public string MechanicId { get; set; } = string.Empty;
    public string MechanicName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Task description is required.")]
    [StringLength(500, ErrorMessage = "Task description cannot exceed 500 characters.")]
    public string TaskDescription { get; set; } = string.Empty;

    [Range(0.1, 24.0, ErrorMessage = "Hours spent must be between 0.1 and 24.0.")]
    public decimal HoursSpent { get; set; }

    public DateTime LoggedAt { get; set; }

    [StringLength(1000, ErrorMessage = "Observations cannot exceed 1000 characters.")]
    public string? Observations { get; set; }
}

public class PartConsumptionDto
{
    public int Id { get; set; }
    public int JobCardId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Valid inventory part ID is required.")]
    public int InventoryPartId { get; set; }

    public string PartNumber { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;

    [Range(1, 1000, ErrorMessage = "Quantity must be between 1 and 1000.")]
    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public DateTime ConsumedAt { get; set; }
}

public class UpdateJobCardDto
{
    public int JobCardId { get; set; }
    public int? ServiceBayId { get; set; }
    public string? MechanicId { get; set; }
    public int OdometerIn { get; set; }
    public int? OdometerOut { get; set; }
    public string? AdvisorObservations { get; set; }
    public string? MechanicNotes { get; set; }
    public string? QualityCheckNotes { get; set; }
    public string? Recommendations { get; set; }
    public AppointmentStatus? Status { get; set; }
}

public class CancelJobCardDto
{
    [Range(1, int.MaxValue)]
    public int JobCardId { get; set; }

    [Required(ErrorMessage = "Cancellation reason is required.")]
    [StringLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
    public string Reason { get; set; } = string.Empty;

    public string? CancelledByUserId { get; set; }
}
