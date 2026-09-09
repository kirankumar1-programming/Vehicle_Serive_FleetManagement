using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Application.Services;

public class FleetManagementService : IFleetService
{
    private readonly IUnitOfWork _unitOfWork;

    public FleetManagementService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<FleetDashboardDto> GetFleetDashboardAsync(int fleetId)
    {
        var vehicles = await _unitOfWork.Repository<Vehicle>().Query()
            .Include(v => v.CompanyFleet)
            .Include(v => v.DriverAssignments.Where(d => d.IsCurrentDriver)).ThenInclude(d => d.Driver)
            .Include(v => v.JobCards).ThenInclude(j => j.Invoices)
            .Where(v => v.CompanyFleetId == fleetId)
            .ToListAsync();

        int totalVehicles = vehicles.Count;
        int inService = vehicles.Count(v => v.JobCards.Any(j => j.Status != AppointmentStatus.Completed && j.Status != AppointmentStatus.Cancelled));
        int dueMaintenance = vehicles.Count(v => v.CurrentMileage >= v.NextServiceDueMileage || (v.NextServiceDueDate.HasValue && v.NextServiceDueDate.Value <= DateTime.UtcNow));

        decimal totalMaintenanceCost = vehicles
            .SelectMany(v => v.JobCards)
            .SelectMany(j => j.Invoices)
            .Where(i => i.Status == PaymentStatus.Successful)
            .Sum(i => i.GrandTotal);

        decimal avgCost = totalVehicles > 0 ? totalMaintenanceCost / totalVehicles : 0;
        double estimatedDowntime = inService * 1.5; // Avg 1.5 days per vehicle in workshop

        var vehicleDtos = vehicles.Select(v => new VehicleDto
        {
            Id = v.Id,
            RegistrationNumber = v.RegistrationNumber,
            VIN = v.VIN,
            Make = v.Make,
            Model = v.Model,
            Variant = v.Variant,
            ManufacturingYear = v.ManufacturingYear,
            Color = v.Color,
            FuelType = v.FuelType,
            Transmission = v.Transmission,
            CurrentMileage = v.CurrentMileage,
            InsuranceProvider = v.InsuranceProvider,
            InsurancePolicyNumber = v.InsurancePolicyNumber,
            InsuranceExpiryDate = v.InsuranceExpiryDate,
            CompanyFleetId = v.CompanyFleetId,
            CompanyFleetName = v.CompanyFleet?.CompanyName,
            LastServiceMileage = v.LastServiceMileage,
            LastServiceDate = v.LastServiceDate,
            NextServiceDueMileage = v.NextServiceDueMileage,
            NextServiceDueDate = v.NextServiceDueDate,
            IsMaintenanceDue = v.CurrentMileage >= v.NextServiceDueMileage || (v.NextServiceDueDate.HasValue && v.NextServiceDueDate.Value <= DateTime.UtcNow),
            CurrentDriverName = v.DriverAssignments.FirstOrDefault(d => d.IsCurrentDriver)?.Driver?.FullName,
            ActiveJobCount = v.JobCards.Count(j => j.Status != AppointmentStatus.Completed && j.Status != AppointmentStatus.Cancelled)
        }).ToList();

        return new FleetDashboardDto
        {
            TotalFleetVehicles = totalVehicles,
            ActiveInService = inService,
            DueForMaintenanceCount = dueMaintenance,
            TotalMaintenanceCost = totalMaintenanceCost,
            AverageCostPerVehicle = avgCost,
            EstimatedDowntimeDays = estimatedDowntime,
            FleetVehicles = vehicleDtos
        };
    }

    public async Task<IReadOnlyList<CompanyFleet>> GetAllFleetsAsync()
    {
        return await _unitOfWork.Repository<CompanyFleet>().Query()
            .Include(f => f.Vehicles)
            .Include(f => f.ManagersAndDrivers)
            .ToListAsync();
    }

    public async Task<CompanyFleet?> GetFleetByIdAsync(int fleetId)
    {
        return await _unitOfWork.Repository<CompanyFleet>().Query()
            .Include(f => f.Vehicles)
            .Include(f => f.ManagersAndDrivers)
            .FirstOrDefaultAsync(f => f.Id == fleetId);
    }

    public async Task<CompanyFleet> RegisterFleetAsync(CompanyFleet fleet)
    {
        await _unitOfWork.Repository<CompanyFleet>().AddAsync(fleet);
        await _unitOfWork.SaveChangesAsync();
        return fleet;
    }
}

public class RoadsideAssistanceService : IRoadsideService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;

    public RoadsideAssistanceService(IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    public async Task<IReadOnlyList<RoadsideRequestDto>> GetAllRequestsAsync(RoadsideStatus? status = null)
    {
        var query = _unitOfWork.Repository<RoadsideAssistanceRequest>().Query()
            .Include(r => r.Customer)
            .Include(r => r.Vehicle)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return list.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<RoadsideRequestDto>> GetCustomerRequestsAsync(string customerId)
    {
        var list = await _unitOfWork.Repository<RoadsideAssistanceRequest>().Query()
            .Include(r => r.Customer)
            .Include(r => r.Vehicle)
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return list.Select(MapToDto).ToList();
    }

    public async Task<RoadsideRequestDto?> GetRequestByIdAsync(int id)
    {
        var r = await _unitOfWork.Repository<RoadsideAssistanceRequest>().Query()
            .Include(r => r.Customer)
            .Include(r => r.Vehicle)
            .Include(r => r.Logs)
            .FirstOrDefaultAsync(r => r.Id == id);

        return r == null ? null : MapToDto(r);
    }

    public async Task<RoadsideRequestDto> CreateRequestAsync(CreateRoadsideRequestDto dto)
    {
        var reqNumber = $"RSA-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..5].ToUpper()}";

        var request = new RoadsideAssistanceRequest
        {
            RequestNumber = reqNumber,
            CustomerId = dto.CustomerId ?? string.Empty,
            VehicleId = dto.VehicleId,
            RequestType = dto.RequestType,
            Status = RoadsideStatus.Requested,
            LocationAddress = dto.LocationAddress,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            ContactPhone = dto.ContactPhone,
            IssueDescription = dto.IssueDescription,
            EstimatedArrivalTime = DateTime.UtcNow.AddMinutes(30)
        };

        await _unitOfWork.Repository<RoadsideAssistanceRequest>().AddAsync(request);
        await _unitOfWork.SaveChangesAsync();

        await _unitOfWork.Repository<RoadsideLog>().AddAsync(new RoadsideLog
        {
            RoadsideAssistanceRequestId = request.Id,
            Status = RoadsideStatus.Requested,
            Message = "Assistance request logged by customer.",
            Timestamp = DateTime.UtcNow
        });
        await _unitOfWork.SaveChangesAsync();

        return (await GetRequestByIdAsync(request.Id))!;
    }

    public async Task<RoadsideRequestDto> UpdateRequestStatusAsync(int requestId, RoadsideStatus status, string? notes = null, string? techName = null, string? techPhone = null)
    {
        var req = await _unitOfWork.Repository<RoadsideAssistanceRequest>().GetByIdAsync(requestId);
        if (req == null) throw new DomainException("Roadside request not found.");

        req.Status = status;
        if (!string.IsNullOrEmpty(techName)) req.AssignedTechnicianName = techName;
        if (!string.IsNullOrEmpty(techPhone)) req.AssignedTechnicianPhone = techPhone;

        if (status == RoadsideStatus.TechnicianArrived) req.ActualArrivalTime = DateTime.UtcNow;
        if (status == RoadsideStatus.IssueResolved)
        {
            req.ResolvedAt = DateTime.UtcNow;
            req.ResolutionNotes = notes;
        }

        await _unitOfWork.Repository<RoadsideAssistanceRequest>().UpdateAsync(req);

        await _unitOfWork.Repository<RoadsideLog>().AddAsync(new RoadsideLog
        {
            RoadsideAssistanceRequestId = req.Id,
            Status = status,
            Message = notes ?? $"Status updated to {status}",
            Timestamp = DateTime.UtcNow
        });

        await _unitOfWork.SaveChangesAsync();

        if (!string.IsNullOrEmpty(req.CustomerId))
        {
            await _notificationService.SendNotificationAsync(
                req.CustomerId,
                "Roadside Assistance Update",
                $"Your roadside request {req.RequestNumber} is now: {status}.",
                NotificationType.Roadside,
                $"/Customer/RoadsideDetails/{req.Id}");
        }

        return (await GetRequestByIdAsync(req.Id))!;
    }

    private static RoadsideRequestDto MapToDto(RoadsideAssistanceRequest r)
    {
        return new RoadsideRequestDto
        {
            Id = r.Id,
            RequestNumber = r.RequestNumber,
            CustomerId = r.CustomerId,
            CustomerName = r.Customer?.FullName ?? "Customer",
            VehicleId = r.VehicleId,
            VehicleInfo = r.Vehicle != null ? $"{r.Vehicle.Make} {r.Vehicle.Model}" : "Vehicle",
            RegistrationNumber = r.Vehicle?.RegistrationNumber ?? string.Empty,
            RequestType = r.RequestType,
            Status = r.Status,
            LocationAddress = r.LocationAddress,
            Latitude = r.Latitude,
            Longitude = r.Longitude,
            ContactPhone = r.ContactPhone,
            IssueDescription = r.IssueDescription,
            AssignedTechnicianName = r.AssignedTechnicianName,
            AssignedTechnicianPhone = r.AssignedTechnicianPhone,
            TowTruckPlate = r.TowTruckPlate,
            EstimatedArrivalTime = r.EstimatedArrivalTime,
            ActualArrivalTime = r.ActualArrivalTime,
            ResolvedAt = r.ResolvedAt,
            ResolutionNotes = r.ResolutionNotes,
            CreatedAt = r.CreatedAt
        };
    }
}

public class PreventiveMaintenanceService : IPreventiveMaintenanceService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;

    public PreventiveMaintenanceService(IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    public async Task<IReadOnlyList<VehicleDto>> GetVehiclesDueForMaintenanceAsync(int? fleetId = null)
    {
        var query = _unitOfWork.Repository<Vehicle>().Query()
            .Include(v => v.Customer)
            .Include(v => v.CompanyFleet)
            .AsQueryable();

        if (fleetId.HasValue)
        {
            query = query.Where(v => v.CompanyFleetId == fleetId.Value);
        }

        var list = await query.ToListAsync();
        var dueList = list.Where(v => v.CurrentMileage >= v.NextServiceDueMileage || (v.NextServiceDueDate.HasValue && v.NextServiceDueDate.Value <= DateTime.UtcNow)).ToList();

        return dueList.Select(v => new VehicleDto
        {
            Id = v.Id,
            RegistrationNumber = v.RegistrationNumber,
            VIN = v.VIN,
            Make = v.Make,
            Model = v.Model,
            Variant = v.Variant,
            ManufacturingYear = v.ManufacturingYear,
            Color = v.Color,
            FuelType = v.FuelType,
            Transmission = v.Transmission,
            CurrentMileage = v.CurrentMileage,
            CustomerId = v.CustomerId,
            CustomerName = v.Customer?.FullName,
            CompanyFleetId = v.CompanyFleetId,
            CompanyFleetName = v.CompanyFleet?.CompanyName,
            LastServiceMileage = v.LastServiceMileage,
            LastServiceDate = v.LastServiceDate,
            NextServiceDueMileage = v.NextServiceDueMileage,
            NextServiceDueDate = v.NextServiceDueDate,
            IsMaintenanceDue = true
        }).ToList();
    }

    public async Task CheckAndScheduleMaintenanceRemindersAsync()
    {
        var dueVehicles = await GetVehiclesDueForMaintenanceAsync();
        foreach (var v in dueVehicles)
        {
            if (!string.IsNullOrEmpty(v.CustomerId))
            {
                await _notificationService.SendNotificationAsync(
                    v.CustomerId,
                    "Preventive Maintenance Due",
                    $"Your vehicle {v.Make} {v.Model} ({v.RegistrationNumber}) has reached {v.CurrentMileage:N0} KM and is due for periodic maintenance.",
                    NotificationType.MaintenanceDue,
                    $"/Customer/BookService?vehicleId={v.Id}");
            }
        }
    }
}