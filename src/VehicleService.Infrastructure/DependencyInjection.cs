using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VehicleService.Application.Interfaces;
using VehicleService.Application.Services;
using VehicleService.Infrastructure.Auth;
using VehicleService.Infrastructure.BackgroundServices;
using VehicleService.Infrastructure.Payment;
using VehicleService.Infrastructure.Storage;
using VehicleService.Persistence.Context;
using VehicleService.Persistence.Repositories;

namespace VehicleService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationAndInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Persistence
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        // Application Services
        services.AddScoped<IVehicleService, VehicleManagementService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IJobCardService, JobCardService>();
        services.AddScoped<IInspectionService, VehicleInspectionService>();
        services.AddScoped<IEstimateService, EstimateService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IWarrantyService, WarrantyService>();
        services.AddScoped<IFleetService, FleetManagementService>();
        services.AddScoped<IRoadsideService, RoadsideAssistanceService>();
        services.AddScoped<IPreventiveMaintenanceService, PreventiveMaintenanceService>();
        services.AddScoped<IReviewAndComplaintService, ReviewAndComplaintService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<INotificationService, NotificationService>();

        // Infrastructure Services
        services.AddScoped<IPaymentService, MockPaymentService>();
        services.AddSingleton<IBlobStorageService, LocalBlobStorageService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        // Background Services
        services.AddHostedService<MaintenanceReminderBackgroundService>();
        services.AddHostedService<WarrantyExpiryBackgroundService>();
        services.AddHostedService<LowStockAlertBackgroundService>();

        return services;
    }
}