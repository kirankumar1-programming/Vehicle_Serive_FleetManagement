using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Application.Services;

public class AppointmentService : IAppointmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private static readonly object _bookingLock = new();

    public AppointmentService(IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    public async Task<IReadOnlyList<AppointmentDto>> GetCustomerAppointmentsAsync(string customerId)
    {
        var appointments = await _unitOfWork.Repository<ServiceAppointment>().Query()
            .Include(a => a.Vehicle)
            .Include(a => a.Customer)
            .Include(a => a.ServiceCenter)
            .Include(a => a.ServiceBay)
            .Include(a => a.ServiceType)
            .Include(a => a.ServicePackage)
            .Include(a => a.JobCard)
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.AppointmentDate)
            .ToListAsync();

        return appointments.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<AppointmentDto>> GetCenterAppointmentsAsync(int centerId, DateTime? date = null)
    {
        var query = _unitOfWork.Repository<ServiceAppointment>().Query()
            .Include(a => a.Vehicle)
            .Include(a => a.Customer)
            .Include(a => a.ServiceCenter)
            .Include(a => a.ServiceBay)
            .Include(a => a.ServiceType)
            .Include(a => a.ServicePackage)
            .Include(a => a.JobCard)
            .Where(a => a.ServiceCenterId == centerId);

        if (date.HasValue)
        {
            var targetDate = date.Value.Date;
            query = query.Where(a => a.AppointmentDate.Date == targetDate);
        }

        var appointments = await query.OrderBy(a => a.AppointmentDate).ToListAsync();
        return appointments.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<AppointmentDto>> GetAllAppointmentsAsync()
    {
        var appointments = await _unitOfWork.Repository<ServiceAppointment>().Query()
            .Include(a => a.Vehicle)
            .Include(a => a.Customer)
            .Include(a => a.ServiceCenter)
            .Include(a => a.ServiceBay)
            .Include(a => a.ServiceType)
            .Include(a => a.ServicePackage)
            .Include(a => a.JobCard)
            .OrderByDescending(a => a.AppointmentDate)
            .ToListAsync();

        return appointments.Select(MapToDto).ToList();
    }

    public async Task<AppointmentDto?> GetAppointmentByIdAsync(int id)
    {
        var a = await _unitOfWork.Repository<ServiceAppointment>().Query()
            .Include(a => a.Vehicle)
            .Include(a => a.Customer)
            .Include(a => a.ServiceCenter)
            .Include(a => a.ServiceBay)
            .Include(a => a.ServiceType)
            .Include(a => a.ServicePackage)
            .Include(a => a.JobCard)
            .FirstOrDefaultAsync(a => a.Id == id);

        return a == null ? null : MapToDto(a);
    }

    public async Task<IReadOnlyList<BayAvailabilityDto>> GetAvailableBaysAsync(int centerId, DateTime date, string timeSlot)
    {
        var bays = await _unitOfWork.Repository<ServiceBay>().Query()
            .Where(b => b.ServiceCenterId == centerId && b.IsActive && b.Status != BayStatus.Maintenance)
            .ToListAsync();

        var bookedBayIds = await _unitOfWork.Repository<ServiceAppointment>().Query()
            .Where(a => a.ServiceCenterId == centerId &&
                        a.AppointmentDate.Date == date.Date &&
                        a.TimeSlot == timeSlot &&
                        a.ServiceBayId.HasValue &&
                        a.Status != AppointmentStatus.Cancelled)
            .Select(a => a.ServiceBayId!.Value)
            .ToListAsync();

        return bays.Select(b => new BayAvailabilityDto
        {
            BayId = b.Id,
            BayNumber = b.BayNumber,
            BayName = b.BayName,
            BayType = b.BayType,
            Status = b.Status,
            IsAvailableForSlot = !bookedBayIds.Contains(b.Id)
        }).ToList();
    }

    // SCENARIO 1: Double Booking Prevention
    public async Task<AppointmentDto> BookAppointmentAsync(BookAppointmentDto dto)
    {
        var targetDate = dto.AppointmentDate.Date;

        lock (_bookingLock)
        {
            // 1. Check if specific bay is chosen and already booked for this slot
            if (dto.ServiceBayId.HasValue)
            {
                bool isBayBooked = _unitOfWork.Repository<ServiceAppointment>().Query()
                    .Any(a => a.ServiceCenterId == dto.ServiceCenterId &&
                              a.ServiceBayId == dto.ServiceBayId.Value &&
                              a.AppointmentDate.Date == targetDate &&
                              a.TimeSlot == dto.TimeSlot &&
                              a.Status != AppointmentStatus.Cancelled);

                if (isBayBooked)
                {
                    throw new DoubleBookingException($"Service Bay is already booked for {targetDate:yyyy-MM-dd} during time slot {dto.TimeSlot}. Please select another bay or time slot.");
                }
            }

            // 2. Check total center capacity for this date and time slot
            var totalActiveBays = _unitOfWork.Repository<ServiceBay>().Query()
                .Count(b => b.ServiceCenterId == dto.ServiceCenterId && b.IsActive && b.Status != BayStatus.Maintenance);

            var existingSlotBookings = _unitOfWork.Repository<ServiceAppointment>().Query()
                .Count(a => a.ServiceCenterId == dto.ServiceCenterId &&
                            a.AppointmentDate.Date == targetDate &&
                            a.TimeSlot == dto.TimeSlot &&
                            a.Status != AppointmentStatus.Cancelled);

            if (existingSlotBookings >= totalActiveBays)
            {
                throw new DoubleBookingException($"All service slots are fully booked at this center for {targetDate:yyyy-MM-dd} ({dto.TimeSlot}). Please choose another time slot.");
            }

            // 3. Prevent duplicate active booking for the exact same vehicle on the same date
            bool vehicleAlreadyBooked = _unitOfWork.Repository<ServiceAppointment>().Query()
                .Any(a => a.VehicleId == dto.VehicleId &&
                          a.AppointmentDate.Date == targetDate &&
                          a.Status != AppointmentStatus.Cancelled &&
                          a.Status != AppointmentStatus.Completed);

            if (vehicleAlreadyBooked)
            {
                throw new DoubleBookingException("This vehicle already has an active appointment scheduled for the selected date.");
            }
        }

        // Auto-assign available bay if not explicitly selected
        int? assignedBayId = dto.ServiceBayId;
        if (!assignedBayId.HasValue)
        {
            var bookedBayIds = await _unitOfWork.Repository<ServiceAppointment>().Query()
                .Where(a => a.ServiceCenterId == dto.ServiceCenterId &&
                            a.AppointmentDate.Date == targetDate &&
                            a.TimeSlot == dto.TimeSlot &&
                            a.ServiceBayId.HasValue &&
                            a.Status != AppointmentStatus.Cancelled)
                .Select(a => a.ServiceBayId!.Value)
                .ToListAsync();

            var availableBay = await _unitOfWork.Repository<ServiceBay>().Query()
                .FirstOrDefaultAsync(b => b.ServiceCenterId == dto.ServiceCenterId &&
                                          b.IsActive &&
                                          b.Status != BayStatus.Maintenance &&
                                          !bookedBayIds.Contains(b.Id));

            if (availableBay != null)
            {
                assignedBayId = availableBay.Id;
            }
        }

        var appointmentNumber = $"APT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..5].ToUpper()}";

        var appointment = new ServiceAppointment
        {
            AppointmentNumber = appointmentNumber,
            VehicleId = dto.VehicleId,
            CustomerId = dto.CustomerId ?? string.Empty,
            ServiceCenterId = dto.ServiceCenterId,
            ServiceBayId = assignedBayId,
            ServiceTypeId = dto.ServiceTypeId,
            ServicePackageId = dto.ServicePackageId,
            AppointmentDate = targetDate,
            TimeSlot = dto.TimeSlot,
            Status = AppointmentStatus.Confirmed,
            Priority = dto.Priority,
            CustomerNotes = dto.CustomerNotes,
            RequiresPickupDrop = dto.RequiresPickupDrop,
            PickupAddress = dto.PickupAddress
        };

        await _unitOfWork.Repository<ServiceAppointment>().AddAsync(appointment);
        await _unitOfWork.SaveChangesAsync();

        // Reserve parts if required for chosen service
        if (dto.RequiredPartIds != null && dto.RequiredPartIds.Count > 0)
        {
            foreach (var partId in dto.RequiredPartIds)
            {
                var part = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(partId);
                if (part != null && part.AvailableQuantity > 0)
                {
                    part.AvailableQuantity -= 1;
                    part.ReservedQuantity += 1;
                    await _unitOfWork.Repository<InventoryPart>().UpdateAsync(part);

                    await _unitOfWork.Repository<PartReservation>().AddAsync(new PartReservation
                    {
                        InventoryPartId = partId,
                        AppointmentId = appointment.Id,
                        Quantity = 1,
                        IsReleased = false
                    });
                }
            }
            await _unitOfWork.SaveChangesAsync();
        }

        // Notify Customer
        if (!string.IsNullOrEmpty(dto.CustomerId))
        {
            await _notificationService.SendNotificationAsync(
                dto.CustomerId,
                "Appointment Confirmed",
                $"Your service appointment {appointmentNumber} is confirmed for {targetDate:MMM dd, yyyy} ({dto.TimeSlot}).",
                NotificationType.Appointment,
                $"/Customer/Appointments");
        }

        return (await GetAppointmentByIdAsync(appointment.Id))!;
    }

    // SCENARIO 5: Service Cancellation (Releases Slot & Reserved Parts)
    public async Task<bool> CancelAppointmentAsync(int appointmentId, string reason, string cancelledByUserId)
    {
        var appointment = await _unitOfWork.Repository<ServiceAppointment>().Query()
            .Include(a => a.PartReservations)
            .Include(a => a.Vehicle)
            .FirstOrDefaultAsync(a => a.Id == appointmentId);

        if (appointment == null) return false;

        if (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Cancelled)
        {
            throw new DomainException($"Cannot cancel appointment in '{appointment.Status}' status.");
        }

        // 1. Cancel Appointment
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancellationReason = reason;
        appointment.CancelledAt = DateTime.UtcNow;
        appointment.ServiceBayId = null; // Release Service Bay

        await _unitOfWork.Repository<ServiceAppointment>().UpdateAsync(appointment);

        // 2. Release all Reserved Spare Parts back to available inventory
        foreach (var reservation in appointment.PartReservations.Where(r => !r.IsReleased && !r.IsConsumed))
        {
            reservation.IsReleased = true;
            reservation.ReleasedAt = DateTime.UtcNow;
            await _unitOfWork.Repository<PartReservation>().UpdateAsync(reservation);

            var part = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(reservation.InventoryPartId);
            if (part != null)
            {
                part.AvailableQuantity += reservation.Quantity;
                part.ReservedQuantity = Math.Max(0, part.ReservedQuantity - reservation.Quantity);
                await _unitOfWork.Repository<InventoryPart>().UpdateAsync(part);
            }
        }

        await _unitOfWork.SaveChangesAsync();

        // 3. Notify Customer
        if (!string.IsNullOrEmpty(appointment.CustomerId))
        {
            await _notificationService.SendNotificationAsync(
                appointment.CustomerId,
                "Appointment Cancelled",
                $"Your service appointment {appointment.AppointmentNumber} for {appointment.Vehicle?.Make} {appointment.Vehicle?.Model} has been cancelled. Reserved bays and parts have been released.",
                NotificationType.Appointment,
                "/Customer/Appointments");
        }

        return true;
    }

    private static AppointmentDto MapToDto(ServiceAppointment a)
    {
        return new AppointmentDto
        {
            Id = a.Id,
            AppointmentNumber = a.AppointmentNumber,
            VehicleId = a.VehicleId,
            VehicleInfo = a.Vehicle != null ? $"{a.Vehicle.Make} {a.Vehicle.Model} ({a.Vehicle.RegistrationNumber})" : "Unknown Vehicle",
            RegistrationNumber = a.Vehicle?.RegistrationNumber ?? string.Empty,
            CustomerId = a.CustomerId,
            CustomerName = a.Customer?.FullName ?? "Customer",
            CustomerPhone = a.Customer?.PhoneNumber ?? string.Empty,
            ServiceCenterId = a.ServiceCenterId,
            ServiceCenterName = a.ServiceCenter?.Name ?? string.Empty,
            ServiceBayId = a.ServiceBayId,
            ServiceBayName = a.ServiceBay?.BayName,
            ServiceTypeId = a.ServiceTypeId,
            ServiceTypeName = a.ServiceType?.Name,
            ServicePackageId = a.ServicePackageId,
            ServicePackageName = a.ServicePackage?.Name,
            AppointmentDate = a.AppointmentDate,
            TimeSlot = a.TimeSlot,
            Status = a.Status,
            Priority = a.Priority,
            CustomerNotes = a.CustomerNotes,
            RequiresPickupDrop = a.RequiresPickupDrop,
            PickupAddress = a.PickupAddress,
            JobCardId = a.JobCard?.Id,
            JobCardNumber = a.JobCard?.JobCardNumber
        };
    }
}