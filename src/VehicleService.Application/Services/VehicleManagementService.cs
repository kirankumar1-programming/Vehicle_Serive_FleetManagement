using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Common;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Application.Services;

public class VehicleManagementService : IVehicleService
{
    private readonly IUnitOfWork _unitOfWork;

    public VehicleManagementService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<VehicleDto>> GetCustomerVehiclesAsync(string customerId)
    {
        var vehicles = await _unitOfWork.Repository<Vehicle>().Query()
            .Include(v => v.Customer)
            .Include(v => v.CompanyFleet)
            .Include(v => v.DriverAssignments.Where(d => d.IsCurrentDriver)).ThenInclude(d => d.Driver)
            .Include(v => v.JobCards)
            .Where(v => v.CustomerId == customerId)
            .ToListAsync();

        return vehicles.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<VehicleDto>> GetFleetVehiclesAsync(int fleetId)
    {
        var vehicles = await _unitOfWork.Repository<Vehicle>().Query()
            .Include(v => v.Customer)
            .Include(v => v.CompanyFleet)
            .Include(v => v.DriverAssignments.Where(d => d.IsCurrentDriver)).ThenInclude(d => d.Driver)
            .Include(v => v.JobCards)
            .Where(v => v.CompanyFleetId == fleetId)
            .ToListAsync();

        return vehicles.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<VehicleDto>> GetAllVehiclesAsync()
    {
        var vehicles = await _unitOfWork.Repository<Vehicle>().Query()
            .Include(v => v.Customer)
            .Include(v => v.CompanyFleet)
            .Include(v => v.DriverAssignments.Where(d => d.IsCurrentDriver)).ThenInclude(d => d.Driver)
            .Include(v => v.JobCards)
            .ToListAsync();

        return vehicles.Select(MapToDto).ToList();
    }

    public async Task<VehicleDto?> GetVehicleByIdAsync(int id)
    {
        var v = await _unitOfWork.Repository<Vehicle>().Query()
            .Include(v => v.Customer)
            .Include(v => v.CompanyFleet)
            .Include(v => v.DriverAssignments.Where(d => d.IsCurrentDriver)).ThenInclude(d => d.Driver)
            .Include(v => v.JobCards)
            .FirstOrDefaultAsync(v => v.Id == id);

        return v == null ? null : MapToDto(v);
    }

    public async Task<VehicleDto?> GetVehicleByRegistrationAsync(string regNumber)
    {
        var v = await _unitOfWork.Repository<Vehicle>().Query()
            .Include(v => v.Customer)
            .Include(v => v.CompanyFleet)
            .FirstOrDefaultAsync(v => v.RegistrationNumber.ToUpper() == regNumber.ToUpper());

        return v == null ? null : MapToDto(v);
    }

    public async Task<VehicleDto> RegisterVehicleAsync(CreateVehicleDto dto)
    {
        var existing = await _unitOfWork.Repository<Vehicle>()
            .FirstOrDefaultAsync(v => v.RegistrationNumber.ToUpper() == dto.RegistrationNumber.ToUpper() || v.VIN.ToUpper() == dto.VIN.ToUpper());

        if (existing != null)
        {
            throw new DomainException($"Vehicle with registration {dto.RegistrationNumber} or VIN {dto.VIN} is already registered.");
        }

        var vehicle = new Vehicle
        {
            RegistrationNumber = dto.RegistrationNumber.ToUpper().Trim(),
            VIN = dto.VIN.ToUpper().Trim(),
            Make = dto.Make.Trim(),
            Model = dto.Model.Trim(),
            Variant = dto.Variant.Trim(),
            ManufacturingYear = dto.ManufacturingYear,
            Color = dto.Color.Trim(),
            FuelType = dto.FuelType,
            Transmission = dto.Transmission,
            CurrentMileage = dto.CurrentMileage,
            IsActive = dto.IsActive,
            InsuranceProvider = dto.InsuranceProvider,
            InsurancePolicyNumber = dto.InsurancePolicyNumber,
            InsuranceExpiryDate = dto.InsuranceExpiryDate,
            CustomerId = dto.CustomerId,
            CompanyFleetId = dto.CompanyFleetId,
            LastServiceMileage = dto.CurrentMileage,
            LastServiceDate = DateTime.UtcNow,
            NextServiceDueMileage = dto.CurrentMileage + 10000,
            NextServiceDueDate = DateTime.UtcNow.AddMonths(6)
        };

        await _unitOfWork.Repository<Vehicle>().AddAsync(vehicle);
        await _unitOfWork.SaveChangesAsync();

        return MapToDto(vehicle);
    }

    public async Task UpdateVehicleAsync(int id, CreateVehicleDto dto)
    {
        var vehicle = await _unitOfWork.Repository<Vehicle>().GetByIdAsync(id);
        if (vehicle == null) throw new DomainException("Vehicle not found.");

        if (!string.IsNullOrWhiteSpace(dto.Make)) vehicle.Make = dto.Make.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Model)) vehicle.Model = dto.Model.Trim();
        vehicle.Variant = dto.Variant?.Trim() ?? string.Empty;
        if (dto.ManufacturingYear > 1900) vehicle.ManufacturingYear = dto.ManufacturingYear;
        vehicle.Color = dto.Color?.Trim() ?? string.Empty;
        vehicle.FuelType = dto.FuelType;
        vehicle.Transmission = dto.Transmission;
        if (dto.CurrentMileage >= 0) vehicle.CurrentMileage = dto.CurrentMileage;
        vehicle.InsuranceProvider = dto.InsuranceProvider?.Trim();
        vehicle.InsurancePolicyNumber = dto.InsurancePolicyNumber?.Trim();
        vehicle.InsuranceExpiryDate = dto.InsuranceExpiryDate;
        vehicle.IsActive = dto.IsActive;

        await _unitOfWork.Repository<Vehicle>().UpdateAsync(vehicle);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ToggleVehicleStatusAsync(int vehicleId)
    {
        var vehicle = await _unitOfWork.Repository<Vehicle>().GetByIdAsync(vehicleId);
        if (vehicle == null) throw new DomainException("Vehicle not found.");

        vehicle.IsActive = !vehicle.IsActive;
        await _unitOfWork.Repository<Vehicle>().UpdateAsync(vehicle);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task UpdateMileageAsync(int vehicleId, int newMileage)
    {
        var vehicle = await _unitOfWork.Repository<Vehicle>().GetByIdAsync(vehicleId);
        if (vehicle == null) throw new DomainException("Vehicle not found.");

        if (newMileage > vehicle.CurrentMileage)
        {
            vehicle.CurrentMileage = newMileage;
            await _unitOfWork.Repository<Vehicle>().UpdateAsync(vehicle);
            await _unitOfWork.SaveChangesAsync();
        }
    }

    public async Task AssignDriverAsync(DriverAssignmentDto dto)
    {
        var existingAssignments = await _unitOfWork.Repository<DriverAssignment>()
            .FindAsync(d => d.VehicleId == dto.VehicleId && d.IsCurrentDriver);

        foreach (var assignment in existingAssignments)
        {
            assignment.IsCurrentDriver = false;
            assignment.AssignedTo = DateTime.UtcNow;
            await _unitOfWork.Repository<DriverAssignment>().UpdateAsync(assignment);
        }

        var newAssignment = new DriverAssignment
        {
            VehicleId = dto.VehicleId,
            DriverId = dto.DriverId,
            LicenseNumber = dto.LicenseNumber,
            Notes = dto.Notes,
            IsCurrentDriver = true,
            AssignedFrom = DateTime.UtcNow
        };

        await _unitOfWork.Repository<DriverAssignment>().AddAsync(newAssignment);
        await _unitOfWork.SaveChangesAsync();
    }

    private static VehicleDto MapToDto(Vehicle v)
    {
        bool isDue = v.CurrentMileage >= v.NextServiceDueMileage || (v.NextServiceDueDate.HasValue && v.NextServiceDueDate.Value <= DateTime.UtcNow);
        var currentDriver = v.DriverAssignments?.FirstOrDefault(d => d.IsCurrentDriver)?.Driver?.FullName;

        return new VehicleDto
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
            IsActive = v.IsActive,
            InsuranceProvider = v.InsuranceProvider,
            InsurancePolicyNumber = v.InsurancePolicyNumber,
            InsuranceExpiryDate = v.InsuranceExpiryDate,
            CustomerId = v.CustomerId,
            CustomerName = v.Customer?.FullName,
            CompanyFleetId = v.CompanyFleetId,
            CompanyFleetName = v.CompanyFleet?.CompanyName,
            LastServiceMileage = v.LastServiceMileage,
            LastServiceDate = v.LastServiceDate,
            NextServiceDueMileage = v.NextServiceDueMileage,
            NextServiceDueDate = v.NextServiceDueDate,
            IsMaintenanceDue = isDue,
            CurrentDriverName = currentDriver,
            ActiveJobCount = v.JobCards?.Count(j => j.Status != AppointmentStatus.Completed && j.Status != AppointmentStatus.Cancelled) ?? 0
        };
    }
}