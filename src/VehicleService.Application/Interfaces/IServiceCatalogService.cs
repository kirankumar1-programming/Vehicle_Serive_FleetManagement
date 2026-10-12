using VehicleService.Domain.Entities;

namespace VehicleService.Application.Interfaces;

public interface IServiceCatalogService
{
    Task<IReadOnlyList<ServiceType>> GetAllServiceTypesAsync();
    Task<IReadOnlyList<ServiceType>> GetActiveServiceTypesAsync();
    Task<ServiceType?> GetServiceTypeByIdAsync(int id);
    Task UpdateServiceTypeAsync(ServiceType serviceType);

    Task<IReadOnlyList<ServicePackage>> GetAllPackagesAsync();
    Task<IReadOnlyList<ServicePackage>> GetActivePackagesAsync();
    Task<ServicePackage?> GetPackageByIdAsync(int id);
    Task<ServicePackage> CreatePackageAsync(ServicePackage package, List<string>? itemDescriptions = null);
    Task UpdatePackageAsync(ServicePackage package, List<string>? itemDescriptions = null);
    Task TogglePackageStatusAsync(int id);
}
