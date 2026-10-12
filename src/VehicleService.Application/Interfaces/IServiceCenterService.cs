using VehicleService.Domain.Entities;

namespace VehicleService.Application.Interfaces;

public interface IServiceCenterService
{
    Task<IReadOnlyList<ServiceCenter>> GetAllCentersAsync();
    Task<IReadOnlyList<ServiceCenter>> GetActiveCentersAsync();
    Task<ServiceCenter?> GetCenterByIdAsync(int id);
    Task<ServiceCenter> CreateCenterAsync(ServiceCenter center);
    Task UpdateCenterAsync(ServiceCenter center);
    Task ToggleCenterStatusAsync(int id);

    Task<IReadOnlyList<ServiceBay>> GetAllBaysAsync();
    Task<IReadOnlyList<ServiceBay>> GetBaysByCenterIdAsync(int centerId, bool activeOnly = false);
    Task<ServiceBay?> GetBayByIdAsync(int id);
    Task<ServiceBay> CreateBayAsync(ServiceBay bay);
    Task UpdateBayAsync(ServiceBay bay);
    Task ToggleBayStatusAsync(int id);
    Task DeleteBayAsync(int id);
}
