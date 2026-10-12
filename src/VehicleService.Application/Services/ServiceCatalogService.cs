using Microsoft.EntityFrameworkCore;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Application.Services;

public class ServiceCatalogService : IServiceCatalogService
{
    private readonly IUnitOfWork _unitOfWork;

    public ServiceCatalogService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ServiceType>> GetAllServiceTypesAsync()
    {
        return await _unitOfWork.Repository<ServiceType>().Query()
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ServiceType>> GetActiveServiceTypesAsync()
    {
        return await _unitOfWork.Repository<ServiceType>().Query()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<ServiceType?> GetServiceTypeByIdAsync(int id)
    {
        return await _unitOfWork.Repository<ServiceType>().GetByIdAsync(id);
    }

    public async Task UpdateServiceTypeAsync(ServiceType serviceType)
    {
        serviceType.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Repository<ServiceType>().UpdateAsync(serviceType);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ServicePackage>> GetAllPackagesAsync()
    {
        return await _unitOfWork.Repository<ServicePackage>().Query()
            .Include(p => p.PackageItems)
                .ThenInclude(pi => pi.ServiceType)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ServicePackage>> GetActivePackagesAsync()
    {
        return await _unitOfWork.Repository<ServicePackage>().Query()
            .Include(p => p.PackageItems)
                .ThenInclude(pi => pi.ServiceType)
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<ServicePackage?> GetPackageByIdAsync(int id)
    {
        return await _unitOfWork.Repository<ServicePackage>().Query()
            .Include(p => p.PackageItems)
                .ThenInclude(pi => pi.ServiceType)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<ServicePackage> CreatePackageAsync(ServicePackage package, List<string>? itemDescriptions = null)
    {
        await _unitOfWork.Repository<ServicePackage>().AddAsync(package);
        await _unitOfWork.SaveChangesAsync();
        return package;
    }

    public async Task UpdatePackageAsync(ServicePackage package, List<string>? itemDescriptions = null)
    {
        package.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Repository<ServicePackage>().UpdateAsync(package);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task TogglePackageStatusAsync(int id)
    {
        var package = await _unitOfWork.Repository<ServicePackage>().GetByIdAsync(id);
        if (package == null)
            throw new EntityNotFoundException($"Service Package with ID {id} was not found.");

        package.IsActive = !package.IsActive;
        package.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Repository<ServicePackage>().UpdateAsync(package);
        await _unitOfWork.SaveChangesAsync();
    }
}
