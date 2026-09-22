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

    public async Task<JobCardDto> CreateJobCardFromAppointmentAsync(int appointmentId, string advisorId, int? bayId = null, List<int>? partIdsToReserve = null)
    {
        var appointment = await _unitOfWork.Repository<ServiceAppointment>().Query()
            .Include(a => a.Vehicle)
            .Include(a => a.ServiceType)
            .Include(a => a.ServicePackage).ThenInclude(sp => sp!.PackageItems).ThenInclude(pi => pi.ServiceType)
            .Include(a => a.PartReservations)
            .FirstOrDefaultAsync(a => a.Id == appointmentId);

        if (appointment == null) throw new DomainException("Appointment not found.");

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            throw new DomainException("Cannot create a job card for a cancelled appointment.");
        }

        if (appointment.Status == AppointmentStatus.Completed)
        {
            throw new DomainException("Cannot create a job card for a completed appointment.");
        }

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

        // ---------------------------------------------------------
        // Reserve Spare Parts upon Job Card Creation
        // ---------------------------------------------------------
        var targetPartIds = new List<int>();
        if (partIdsToReserve != null && partIdsToReserve.Count > 0)
        {
            targetPartIds.AddRange(partIdsToReserve.Distinct());
        }
        else
        {
            // Auto-detect recommended parts if appointment does not already have active reservations
            var existingReservations = appointment.PartReservations?
                .Where(r => !r.IsReleased && !r.IsConsumed).ToList() ?? new List<PartReservation>();

            if (existingReservations.Count == 0)
            {
                var allParts = await _unitOfWork.Repository<InventoryPart>().Query()
                    .Where(p => p.IsActive && p.AvailableQuantity > 0)
                    .ToListAsync();

                var serviceName = (appointment.ServiceType?.Name ?? string.Empty).ToLowerInvariant();
                var serviceCat = (appointment.ServiceType?.Category ?? string.Empty).ToLowerInvariant();
                var packageDesc = (appointment.ServicePackage?.Name ?? string.Empty).ToLowerInvariant();

                // Periodic / General Service / Oil Service -> reserve Engine Oil & Oil Filter
                if (serviceName.Contains("general") || serviceName.Contains("periodic") || packageDesc.Contains("periodic") || serviceName.Contains("oil"))
                {
                    var oil = allParts.FirstOrDefault(p => p.Category.Equals("Fluids", StringComparison.OrdinalIgnoreCase) || p.PartNumber.Contains("OIL"));
                    var filter = allParts.FirstOrDefault(p => p.Category.Equals("Filters", StringComparison.OrdinalIgnoreCase) || p.PartNumber.Contains("FIL"));
                    if (oil != null) targetPartIds.Add(oil.Id);
                    if (filter != null) targetPartIds.Add(filter.Id);
                }

                // Brake service -> reserve Brake Pads
                if (serviceName.Contains("brake") || serviceCat.Contains("brake"))
                {
                    var brake = allParts.FirstOrDefault(p => p.Category.Equals("Brakes", StringComparison.OrdinalIgnoreCase) || p.PartNumber.Contains("BP"));
                    if (brake != null) targetPartIds.Add(brake.Id);
                }

                // Battery / Electrical -> reserve Battery
                if (serviceName.Contains("battery") || serviceName.Contains("electrical"))
                {
                    var bat = allParts.FirstOrDefault(p => p.Category.Equals("Electrical", StringComparison.OrdinalIgnoreCase) || p.PartNumber.Contains("BAT"));
                    if (bat != null) targetPartIds.Add(bat.Id);
                }
            }
        }

        // Apply reservations and update inventory stock counters
        foreach (var partId in targetPartIds.Distinct())
        {
            var alreadyReserved = await _unitOfWork.Repository<PartReservation>().Query()
                .AnyAsync(r => r.AppointmentId == appointmentId && r.InventoryPartId == partId && !r.IsReleased && !r.IsConsumed);

            if (!alreadyReserved)
            {
                var part = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(partId);
                if (part != null && part.AvailableQuantity > 0)
                {
                    int beforeAvail = part.AvailableQuantity;
                    part.AvailableQuantity -= 1;
                    part.ReservedQuantity += 1;
                    await _unitOfWork.Repository<InventoryPart>().UpdateAsync(part);

                    await _unitOfWork.Repository<PartReservation>().AddAsync(new PartReservation
                    {
                        InventoryPartId = partId,
                        AppointmentId = appointmentId,
                        Quantity = 1,
                        IsReleased = false
                    });

                    await _unitOfWork.Repository<InventoryTransaction>().AddAsync(new InventoryTransaction
                    {
                        InventoryPartId = part.Id,
                        TransactionType = InventoryTransactionType.Reservation,
                        Quantity = 1,
                        QuantityBefore = beforeAvail,
                        QuantityAfter = part.AvailableQuantity,
                        UnitCost = part.CostPrice,
                        TotalAmount = part.CostPrice,
                        ReferenceNumber = jobCardNumber,
                        Notes = $"Stock reserved upon Job Card {jobCardNumber} creation"
                    });
                }
            }
        }

        await _unitOfWork.SaveChangesAsync();

        return (await GetJobCardByIdAsync(jobCard.Id))!;
    }

    // SCENARIO 6: Mechanic Availability Validation
    public async Task AssignMechanicAsync(int jobCardId, string mechanicId)
    {
        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment)
            .FirstOrDefaultAsync(j => j.Id == jobCardId);
        if (jobCard == null) throw new DomainException("Job Card not found.");

        if (jobCard.Status == AppointmentStatus.Cancelled || jobCard.Appointment?.Status == AppointmentStatus.Cancelled)
        {
            throw new DomainException("Cannot assign mechanic to a cancelled job card.");
        }
        if (jobCard.Status == AppointmentStatus.Completed)
        {
            throw new DomainException("Cannot assign mechanic to a completed job card.");
        }

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

        if (jobCard.Status == AppointmentStatus.Cancelled || jobCard.Appointment?.Status == AppointmentStatus.Cancelled)
        {
            throw new DomainException("Cannot update status of a cancelled job card.");
        }

        if (jobCard.Status == AppointmentStatus.Completed)
        {
            throw new DomainException("Cannot update status of a completed job card.");
        }

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
        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment)
            .FirstOrDefaultAsync(j => j.Id == jobCardId);
        if (jobCard == null) throw new DomainException("Job Card not found.");

        if (jobCard.Status == AppointmentStatus.Cancelled || jobCard.Appointment?.Status == AppointmentStatus.Cancelled)
        {
            throw new DomainException("Cannot log labor for a cancelled job card.");
        }
        if (jobCard.Status == AppointmentStatus.Completed)
        {
            throw new DomainException("Cannot log labor for a completed job card.");
        }

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
        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment)
            .FirstOrDefaultAsync(j => j.Id == jobCardId);
        if (jobCard == null) throw new DomainException("Job card not found.");

        if (jobCard.Status == AppointmentStatus.Cancelled || jobCard.Appointment?.Status == AppointmentStatus.Cancelled)
        {
            throw new DomainException("Cannot record replaced parts for a cancelled job card.");
        }
        if (jobCard.Status == AppointmentStatus.Completed)
        {
            throw new DomainException("Cannot record replaced parts for a completed job card.");
        }

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

    public async Task UpdateJobCardDetailsAsync(UpdateJobCardDto dto)
    {
        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment)
            .Include(j => j.Vehicle)
            .FirstOrDefaultAsync(j => j.Id == dto.JobCardId);

        if (jobCard == null) throw new DomainException("Job card not found.");

        if (jobCard.Status == AppointmentStatus.Completed || jobCard.Status == AppointmentStatus.Cancelled || jobCard.Appointment?.Status == AppointmentStatus.Cancelled)
        {
            throw new DomainException($"Cannot edit job card in '{jobCard.Status}' status.");
        }

        if (dto.OdometerIn > 0)
        {
            jobCard.OdometerIn = dto.OdometerIn;
        }

        if (dto.OdometerOut.HasValue && dto.OdometerOut.Value > 0)
        {
            jobCard.OdometerOut = dto.OdometerOut.Value;
        }

        if (dto.AdvisorObservations != null)
        {
            jobCard.AdvisorObservations = dto.AdvisorObservations.Trim();
        }

        if (dto.MechanicNotes != null)
        {
            jobCard.MechanicNotes = dto.MechanicNotes.Trim();
        }

        if (dto.QualityCheckNotes != null)
        {
            jobCard.QualityCheckNotes = dto.QualityCheckNotes.Trim();
        }

        if (dto.Recommendations != null)
        {
            jobCard.Recommendations = dto.Recommendations.Trim();
        }

        if (dto.ServiceBayId.HasValue)
        {
            jobCard.ServiceBayId = dto.ServiceBayId.Value > 0 ? dto.ServiceBayId.Value : null;
        }

        if (!string.IsNullOrWhiteSpace(dto.MechanicId) && dto.MechanicId != jobCard.MechanicId)
        {
            await AssignMechanicAsync(jobCard.Id, dto.MechanicId);
        }

        if (dto.Status.HasValue && dto.Status.Value != jobCard.Status)
        {
            await UpdateJobCardStatusAsync(jobCard.Id, dto.Status.Value);
        }
        else
        {
            await _unitOfWork.Repository<ServiceJobCard>().UpdateAsync(jobCard);
            await _unitOfWork.SaveChangesAsync();
        }
    }

    public async Task<bool> CancelJobCardAsync(int jobCardId, string reason, string cancelledByUserId)
    {
        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment).ThenInclude(a => a!.PartReservations)
            .Include(j => j.Appointment).ThenInclude(a => a!.Customer)
            .Include(j => j.Vehicle)
            .FirstOrDefaultAsync(j => j.Id == jobCardId);

        if (jobCard == null) return false;

        if (jobCard.Status == AppointmentStatus.Completed || jobCard.Status == AppointmentStatus.Cancelled)
        {
            throw new DomainException($"Cannot cancel job card in '{jobCard.Status}' status.");
        }

        // 1. Update Job Card Status & Notes
        jobCard.Status = AppointmentStatus.Cancelled;
        var cancellationNote = $"[Cancelled on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC]: {reason}";
        jobCard.MechanicNotes = string.IsNullOrWhiteSpace(jobCard.MechanicNotes)
            ? cancellationNote
            : $"{jobCard.MechanicNotes}\n{cancellationNote}";

        // Release bay
        jobCard.ServiceBayId = null;

        await _unitOfWork.Repository<ServiceJobCard>().UpdateAsync(jobCard);

        // 2. Synchronize Appointment Status & Release Reserved Parts
        if (jobCard.Appointment != null)
        {
            jobCard.Appointment.Status = AppointmentStatus.Cancelled;
            jobCard.Appointment.CancellationReason = reason;
            jobCard.Appointment.CancelledAt = DateTime.UtcNow;
            jobCard.Appointment.ServiceBayId = null;

            await _unitOfWork.Repository<ServiceAppointment>().UpdateAsync(jobCard.Appointment);

            // Release all active reserved parts back to inventory
            var reservations = await _unitOfWork.Repository<PartReservation>().Query()
                .Where(r => r.AppointmentId == jobCard.AppointmentId && !r.IsReleased && !r.IsConsumed)
                .ToListAsync();

            foreach (var reservation in reservations)
            {
                reservation.IsReleased = true;
                reservation.ReleasedAt = DateTime.UtcNow;
                await _unitOfWork.Repository<PartReservation>().UpdateAsync(reservation);

                var part = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(reservation.InventoryPartId);
                if (part != null)
                {
                    int beforeAvail = part.AvailableQuantity;
                    part.AvailableQuantity += reservation.Quantity;
                    part.ReservedQuantity = Math.Max(0, part.ReservedQuantity - reservation.Quantity);
                    await _unitOfWork.Repository<InventoryPart>().UpdateAsync(part);

                    await _unitOfWork.Repository<InventoryTransaction>().AddAsync(new InventoryTransaction
                    {
                        InventoryPartId = part.Id,
                        TransactionType = InventoryTransactionType.ReservationRelease,
                        Quantity = reservation.Quantity,
                        QuantityBefore = beforeAvail,
                        QuantityAfter = part.AvailableQuantity,
                        UnitCost = part.CostPrice,
                        TotalAmount = part.CostPrice * reservation.Quantity,
                        ReferenceNumber = jobCard.JobCardNumber,
                        Notes = $"Released from cancelled Job Card {jobCard.JobCardNumber}"
                    });
                }
            }

            // 3. Notify Customer
            if (!string.IsNullOrEmpty(jobCard.Appointment.CustomerId))
            {
                await _notificationService.SendNotificationAsync(
                    jobCard.Appointment.CustomerId,
                    "Service Job Cancelled",
                    $"Job card {jobCard.JobCardNumber} for vehicle {jobCard.Vehicle?.Make} {jobCard.Vehicle?.Model} ({jobCard.Vehicle?.RegistrationNumber}) has been cancelled. Reason: {reason}",
                    NotificationType.Appointment,
                    $"/Customer/Appointments");
            }
        }

        await _unitOfWork.SaveChangesAsync();
        return true;
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