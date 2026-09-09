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
    public int VehicleId { get; set; }
    public string? CustomerId { get; set; }
    public int ServiceCenterId { get; set; }
    public int? ServiceBayId { get; set; }
    public int? ServiceTypeId { get; set; }
    public int? ServicePackageId { get; set; }
    public DateTime AppointmentDate { get; set; }
    public string TimeSlot { get; set; } = "09:00 AM - 11:00 AM";
    public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;
    public string? CustomerNotes { get; set; }
    public bool RequiresPickupDrop { get; set; }
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
    public string TaskDescription { get; set; } = string.Empty;
    public decimal HoursSpent { get; set; }
    public DateTime LoggedAt { get; set; }
    public string? Observations { get; set; }
}

public class PartConsumptionDto
{
    public int Id { get; set; }
    public int JobCardId { get; set; }
    public int InventoryPartId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public DateTime ConsumedAt { get; set; }
}
