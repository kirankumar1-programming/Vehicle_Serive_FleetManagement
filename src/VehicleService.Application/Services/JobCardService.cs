using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Application.Services;

public class JobCardService : IJobCardService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;

    public JobCardService(IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    public async Task<IReadOnlyList<JobCardDto>> GetJobCardsAsync(AppointmentStatus? status = null, int? centerId = null)
    {
        var query = _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment).ThenInclude(a => a!.ServiceCenter)
            .Include(j => j.Appointment).ThenInclude(a => a!.Customer)
            .Include(j => j.Vehicle)
            .Include(j => j.ServiceAdvisor)
            .Include(j => j.Mechanic)
            .Include(j => j.ServiceBay)
            .Include(j => j.Inspection)
            .Include(j => j.RepairEstimates)
            .Include(j => j.Invoices)
            .Include(j => j.WorkLogs)
            .Include(j => j.PartsConsumed).ThenInclude(p => p.InventoryPart)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(j => j.Status == status.Value);
        }

        if (centerId.HasValue)
        {
            query = query.Where(j => j.Appointment!.ServiceCenterId == centerId.Value);
        }

        var list = await query.OrderByDescending(j => j.CreatedAt).ToListAsync();
        return list.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<JobCardDto>> GetMechanicJobCardsAsync(string mechanicId)
    {
        var list = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment).ThenInclude(a => a!.ServiceCenter)
            .Include(j => j.Appointment).ThenInclude(a => a!.Customer)
            .Include(j => j.Vehicle)
            .Include(j => j.ServiceAdvisor)
            .Include(j => j.Mechanic)
            .Include(j => j.ServiceBay)
            .Include(j => j.Inspection)
            .Include(j => j.RepairEstimates)
            .Include(j => j.Invoices)
            .Include(j => j.WorkLogs)
            .Include(j => j.PartsConsumed).ThenInclude(p => p.InventoryPart)
            .Where(j => j.MechanicId == mechanicId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();

        return list.Select(MapToDto).ToList();
    }

    public async Task<JobCardDto?> GetJobCardByIdAsync(int id)
    {
        var j = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment).ThenInclude(a => a!.ServiceCenter)
            .Include(j => j.Appointment).ThenInclude(a => a!.Customer)
            .Include(j => j.Vehicle)
            .Include(j => j.ServiceAdvisor)
            .Include(j => j.Mechanic)
            .Include(j => j.ServiceBay)
            .Include(j => j.Inspection)
            .Include(j => j.RepairEstimates).ThenInclude(e => e.Items)
            .Include(j => j.Invoices).ThenInclude(i => i.Payments)
            .Include(j => j.WorkLogs).ThenInclude(w => w.Mechanic)
            .Include(j => j.PartsConsumed).ThenInclude(p => p.InventoryPart)
            .FirstOrDefaultAsync(j => j.Id == id);

        return j == null ? null : MapToDto(j);
    }

    public async Task<JobCardDto> CreateJobCardFromAppointmentAsync(int appointmentId, string advisorId, int? bayId = null)
    {
        var appointment = await _unitOfWork.Repository<ServiceAppointment>().Query()
            .Include(a => a.Vehicle)
            .FirstOrDefaultAsync(a => a.Id == appointmentId);

        if (appointment == null) throw new DomainException("Appointment not found.");

        var existingJobCard = await _unitOfWork.Repository<ServiceJobCard>()
            .FirstOrDefaultAsync(j => j.AppointmentId == appointmentId);

        if (existingJobCard != null) return (await GetJobCardByIdAsync(existingJobCard.Id))!;

        var jobCardNumber = $"JC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..5].ToUpper()}";

        var jobCard = new ServiceJobCard
        {
            JobCardNumber = jobCardNumber,
            AppointmentId = appointmentId,
            VehicleId = appointment.VehicleId,
            ServiceAdvisorId = advisorId,
            ServiceBayId = bayId ?? appointment.ServiceBayId,
            Status = AppointmentStatus.VehicleReceived,
            OdometerIn = appointment.Vehicle?.CurrentMileage ?? 0,
            AdvisorObservations = appointment.CustomerNotes
        };

        appointment.Status = AppointmentStatus.VehicleReceived;
        await _unitOfWork.Repository<ServiceAppointment>().UpdateAsync(appointment);

        await _unitOfWork.Repository<ServiceJobCard>().AddAsync(jobCard);
        await _unitOfWork.SaveChangesAsync();

        return (await GetJobCardByIdAsync(jobCard.Id))!;
    }

    // SCENARIO 6: Mechanic Availability Validation
    public async Task AssignMechanicAsync(int jobCardId, string mechanicId)
    {
        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().GetByIdAsync(jobCardId);
        if (jobCard == null) throw new DomainException("Job Card not found.");

        // Check if mechanic is already engaged in an active, in-progress job
        var activeJobs = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Where(j => j.MechanicId == mechanicId &&
                        j.Id != jobCardId &&
                        (j.Status == AppointmentStatus.InProgress || j.Status == AppointmentStatus.UnderInspection))
            .ToListAsync();

        // Enforce maximum concurrent active job policy (e.g. max 2 concurrent jobs in workshop)
        if (activeJobs.Count >= 2)
        {
            throw new MechanicUnavailableException($"Mechanic is currently occupied with {activeJobs.Count} active repair jobs ({string.Join(", ", activeJobs.Select(j => j.JobCardNumber))}). Please complete an existing job or assign an available mechanic.");
        }

        jobCard.MechanicId = mechanicId;
        await _unitOfWork.Repository<ServiceJobCard>().UpdateAsync(jobCard);
        await _unitOfWork.SaveChangesAsync();

        await _notificationService.SendNotificationAsync(
            mechanicId,
            "New Job Assigned",
            $"You have been assigned to Job Card {jobCard.JobCardNumber}.",
            NotificationType.JobProgress,
            $"/Mechanic/JobDetails/{jobCardId}");
    }

    public async Task UpdateJobCardStatusAsync(int jobCardId, AppointmentStatus newStatus, string? notes = null)
    {
        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment).ThenInclude(a => a!.Customer)
            .Include(j => j.Vehicle)
            .FirstOrDefaultAsync(j => j.Id == jobCardId);

        if (jobCard == null) throw new DomainException("Job card not found.");

        jobCard.Status = newStatus;
        if (jobCard.Appointment != null)
        {
            jobCard.Appointment.Status = newStatus;
            await _unitOfWork.Repository<ServiceAppointment>().UpdateAsync(jobCard.Appointment);
        }

        if (newStatus == AppointmentStatus.InProgress && !jobCard.WorkStartedAt.HasValue)
        {
            jobCard.WorkStartedAt = DateTime.UtcNow;
        }
        else if (newStatus == AppointmentStatus.Completed)
        {
            jobCard.WorkCompletedAt = DateTime.UtcNow;
            if (notes != null) jobCard.QualityCheckNotes = notes;

            // Update Vehicle last service info
            if (jobCard.Vehicle != null)
            {
                jobCard.Vehicle.LastServiceDate = DateTime.UtcNow;
                jobCard.Vehicle.LastServiceMileage = jobCard.OdometerIn;
                jobCard.Vehicle.NextServiceDueMileage = jobCard.OdometerIn + 10000;
                jobCard.Vehicle.NextServiceDueDate = DateTime.UtcNow.AddMonths(6);
                await _unitOfWork.Repository<Vehicle>().UpdateAsync(jobCard.Vehicle);
            }
        }

        if (!string.IsNullOrEmpty(notes))
        {
            jobCard.MechanicNotes = (jobCard.MechanicNotes + "\n" + notes).Trim();
        }

        await _unitOfWork.Repository<ServiceJobCard>().UpdateAsync(jobCard);
        await _unitOfWork.SaveChangesAsync();

        // Notify customer on key milestones
        if (jobCard.Appointment?.CustomerId != null)
        {
            string message = newStatus switch
            {
                AppointmentStatus.UnderInspection => $"Your vehicle {jobCard.Vehicle?.Make} {jobCard.Vehicle?.Model} is currently under inspection.",
                AppointmentStatus.InProgress => $"Repairs have commenced for your vehicle {jobCard.Vehicle?.Make} {jobCard.Vehicle?.Model}.",
                AppointmentStatus.QualityCheck => $"Repairs completed. Vehicle is currently undergoing final Quality Check & Road Test.",
                AppointmentStatus.ReadyForDelivery => $"Your vehicle {jobCard.Vehicle?.Make} {jobCard.Vehicle?.Model} is ready for delivery / pick-up!",
                AppointmentStatus.Completed => $"Service job {jobCard.JobCardNumber} is completed. Thank you for choosing AutoPro!",
                _ => $"Job card {jobCard.JobCardNumber} status updated to {newStatus}."
            };

            await _notificationService.SendNotificationAsync(
                jobCard.Appointment.CustomerId,
                "Service Status Update",
                message,
                NotificationType.JobProgress,
                $"/Customer/TrackJob/{jobCard.Id}");
        }
    }

    public async Task AddWorkLogAsync(int jobCardId, string mechanicId, string taskDesc, decimal hours, string? observations)
    {
        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().GetByIdAsync(jobCardId);
        if (jobCard == null) throw new DomainException("Job Card not found.");

        var log = new WorkLog
        {
            JobCardId = jobCardId,
            MechanicId = mechanicId,
            TaskDescription = taskDesc,
            HoursSpent = hours,
            Observations = observations,
            LoggedAt = DateTime.UtcNow
        };

        jobCard.TotalLaborHours += hours;
        await _unitOfWork.Repository<ServiceJobCard>().UpdateAsync(jobCard);

        await _unitOfWork.Repository<WorkLog>().AddAsync(log);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task RecordReplacedPartAsync(int jobCardId, int partId, int quantity, string loggedByUserId)
    {
        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().GetByIdAsync(jobCardId);
        if (jobCard == null) throw new DomainException("Job card not found.");

        var part = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(partId);
        if (part == null) throw new DomainException("Part not found.");

        // Record consumption
        var consumption = new PartConsumption
        {
            JobCardId = jobCardId,
            InventoryPartId = partId,
            Quantity = quantity,
            UnitPrice = part.SellingPrice,
            LineTotal = part.SellingPrice * quantity,
            LoggedByUserId = loggedByUserId,
            ConsumedAt = DateTime.UtcNow
        };

        await _unitOfWork.Repository<PartConsumption>().AddAsync(consumption);
        await _unitOfWork.SaveChangesAsync();
    }

    private static JobCardDto MapToDto(ServiceJobCard j)
    {
        var activeEstimate = j.RepairEstimates?.OrderByDescending(e => e.CreatedAt).FirstOrDefault();
        var activeInvoice = j.Invoices?.OrderByDescending(i => i.CreatedAt).FirstOrDefault();

        return new JobCardDto
        {
            Id = j.Id,
            JobCardNumber = j.JobCardNumber,
            AppointmentId = j.AppointmentId,
            AppointmentNumber = j.Appointment?.AppointmentNumber ?? string.Empty,
            VehicleId = j.VehicleId,
            VehicleInfo = j.Vehicle != null ? $"{j.Vehicle.Make} {j.Vehicle.Model} ({j.Vehicle.RegistrationNumber})" : "Unknown",
            RegistrationNumber = j.Vehicle?.RegistrationNumber ?? string.Empty,
            CustomerId = j.Appointment?.CustomerId ?? string.Empty,
            CustomerName = j.Appointment?.Customer?.FullName ?? "Customer",
            CustomerPhone = j.Appointment?.Customer?.PhoneNumber ?? string.Empty,
            ServiceAdvisorId = j.ServiceAdvisorId,
            ServiceAdvisorName = j.ServiceAdvisor?.FullName,
            MechanicId = j.MechanicId,
            MechanicName = j.Mechanic?.FullName,
            ServiceBayId = j.ServiceBayId,
            ServiceBayName = j.ServiceBay?.BayName,
            Status = j.Status,
            OdometerIn = j.OdometerIn,
            OdometerOut = j.OdometerOut,
            WorkStartedAt = j.WorkStartedAt,
            WorkCompletedAt = j.WorkCompletedAt,
            TotalLaborHours = j.TotalLaborHours,
            AdvisorObservations = j.AdvisorObservations,
            MechanicNotes = j.MechanicNotes,
            QualityCheckNotes = j.QualityCheckNotes,
            Recommendations = j.Recommendations,
            InspectionId = j.Inspection?.Id,
            ActiveEstimateId = activeEstimate?.Id,
            EstimateStatus = activeEstimate?.ApprovalStatus,
            InvoiceId = activeInvoice?.Id,
            PaymentStatus = activeInvoice?.Status,
            WorkLogs = j.WorkLogs?.Select(w => new WorkLogDto
            {
                Id = w.Id,
                JobCardId = w.JobCardId,
                MechanicId = w.MechanicId,
                MechanicName = w.Mechanic?.FullName ?? "Mechanic",
                TaskDescription = w.TaskDescription,
                HoursSpent = w.HoursSpent,
                LoggedAt = w.LoggedAt,
                Observations = w.Observations
            }).ToList() ?? new(),
            ConsumedParts = j.PartsConsumed?.Select(p => new PartConsumptionDto
            {
                Id = p.Id,
                JobCardId = p.JobCardId,
                InventoryPartId = p.InventoryPartId,
                PartNumber = p.InventoryPart?.PartNumber ?? string.Empty,
                PartName = p.InventoryPart?.Name ?? string.Empty,
                Quantity = p.Quantity,
                UnitPrice = p.UnitPrice,
                LineTotal = p.LineTotal,
                ConsumedAt = p.ConsumedAt
            }).ToList() ?? new()
        };
    }
}