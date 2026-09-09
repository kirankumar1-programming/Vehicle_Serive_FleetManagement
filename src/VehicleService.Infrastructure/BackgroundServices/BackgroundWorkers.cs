using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;

namespace VehicleService.Infrastructure.BackgroundServices;

public class MaintenanceReminderBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MaintenanceReminderBackgroundService> _logger;

    public MaintenanceReminderBackgroundService(IServiceProvider serviceProvider, ILogger<MaintenanceReminderBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Maintenance Reminder Background Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var pmService = scope.ServiceProvider.GetRequiredService<IPreventiveMaintenanceService>();
                await pmService.CheckAndScheduleMaintenanceRemindersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Maintenance Reminder Worker");
            }

            // Runs every 4 hours in production; 30 minutes in local demo
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}

public class WarrantyExpiryBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WarrantyExpiryBackgroundService> _logger;

    public WarrantyExpiryBackgroundService(IServiceProvider serviceProvider, ILogger<WarrantyExpiryBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Warranty Expiry Scanner Background Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

                var next30Days = DateTime.UtcNow.AddDays(30);
                var expiringWarranties = await unitOfWork.Repository<Warranty>().Query()
                    .Include(w => w.Vehicle).ThenInclude(v => v!.Customer)
                    .Where(w => w.Status == WarrantyStatus.Active && w.EndDate <= next30Days && w.EndDate >= DateTime.UtcNow)
                    .ToListAsync(stoppingToken);

                foreach (var war in expiringWarranties)
                {
                    if (war.Vehicle?.CustomerId != null)
                    {
                        await notificationService.SendNotificationAsync(
                            war.Vehicle.CustomerId,
                            "Warranty Expiring Soon",
                            $"Warranty {war.WarrantyNumber} for {war.Vehicle.Make} {war.Vehicle.Model} is expiring on {war.EndDate:yyyy-MM-dd}. Contact your service advisor to extend coverage.",
                            NotificationType.Warranty,
                            "/Customer/Warranties");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Warranty Expiry Background Worker");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}

public class LowStockAlertBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LowStockAlertBackgroundService> _logger;

    public LowStockAlertBackgroundService(IServiceProvider serviceProvider, ILogger<LowStockAlertBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var invService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
                var lowStock = await invService.GetLowStockPartsAsync();

                if (lowStock.Count > 0)
                {
                    _logger.LogWarning("Found {Count} low-stock inventory parts requiring reorder: {Parts}",
                        lowStock.Count, string.Join(", ", lowStock.Select(p => $"{p.PartNumber} (Qty: {p.AvailableQuantity})")));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Low Stock Alert Worker");
            }

            await Task.Delay(TimeSpan.FromHours(2), stoppingToken);
        }
    }
}