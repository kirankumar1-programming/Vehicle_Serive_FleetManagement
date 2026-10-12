using Microsoft.EntityFrameworkCore;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Application.Services;

public class ServiceCenterService : IServiceCenterService
{
    private readonly IUnitOfWork _unitOfWork;

    public ServiceCenterService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ServiceCenter>> GetAllCentersAsync()
    {
        return await _unitOfWork.Repository<ServiceCenter>().Query()
            .Include(c => c.ServiceBays)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ServiceCenter>> GetActiveCentersAsync()
    {
        return await _unitOfWork.Repository<ServiceCenter>().Query()
            .Include(c => c.ServiceBays.Where(b => b.IsActive))
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<ServiceCenter?> GetCenterByIdAsync(int id)
    {
        return await _unitOfWork.Repository<ServiceCenter>().Query()
            .Include(c => c.ServiceBays)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<ServiceCenter> CreateCenterAsync(ServiceCenter center)
    {
        await _unitOfWork.Repository<ServiceCenter>().AddAsync(center);
        await _unitOfWork.SaveChangesAsync();
        return center;
    }

    public async Task UpdateCenterAsync(ServiceCenter center)
    {
        center.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Repository<ServiceCenter>().UpdateAsync(center);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ToggleCenterStatusAsync(int id)
    {
        var center = await _unitOfWork.Repository<ServiceCenter>().GetByIdAsync(id);
        if (center == null)
            throw new EntityNotFoundException($"Service Center with ID {id} was not found.");

        center.IsActive = !center.IsActive;
        center.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Repository<ServiceCenter>().UpdateAsync(center);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ServiceBay>> GetAllBaysAsync()
    {
        return await _unitOfWork.Repository<ServiceBay>().Query()
            .Include(b => b.ServiceCenter)
            .OrderBy(b => b.BayNumber)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ServiceBay>> GetBaysByCenterIdAsync(int centerId, bool activeOnly = false)
    {
        var query = _unitOfWork.Repository<ServiceBay>().Query()
            .Include(b => b.ServiceCenter)
            .Where(b => b.ServiceCenterId == centerId);

        if (activeOnly)
        {
            query = query.Where(b => b.IsActive);
        }

        return await query.OrderBy(b => b.BayNumber).ToListAsync();
    }

    public async Task<ServiceBay?> GetBayByIdAsync(int id)
    {
        return await _unitOfWork.Repository<ServiceBay>().Query()
            .Include(b => b.ServiceCenter)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<ServiceBay> CreateBayAsync(ServiceBay bay)
    {
        await _unitOfWork.Repository<ServiceBay>().AddAsync(bay);
        await _unitOfWork.SaveChangesAsync();
        return bay;
    }

    public async Task UpdateBayAsync(ServiceBay bay)
    {
        bay.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Repository<ServiceBay>().UpdateAsync(bay);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ToggleBayStatusAsync(int id)
    {
        var bay = await _unitOfWork.Repository<ServiceBay>().GetByIdAsync(id);
        if (bay == null)
            throw new EntityNotFoundException($"Service Bay with ID {id} was not found.");

        bay.IsActive = !bay.IsActive;
        bay.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Repository<ServiceBay>().UpdateAsync(bay);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task DeleteBayAsync(int id)
    {
        var bay = await _unitOfWork.Repository<ServiceBay>().GetByIdAsync(id);
        if (bay == null)
            throw new EntityNotFoundException($"Service Bay with ID {id} was not found.");

        await _unitOfWork.Repository<ServiceBay>().DeleteAsync(bay);
        await _unitOfWork.SaveChangesAsync();
    }
}
