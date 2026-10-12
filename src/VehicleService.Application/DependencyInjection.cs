using Microsoft.Extensions.DependencyInjection;
using VehicleService.Application.Interfaces;
using VehicleService.Application.Services;

namespace VehicleService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Core Business Operations
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

        // Catalog, Facility & Promotions Management
        services.AddScoped<IServiceCenterService, ServiceCenterService>();
        services.AddScoped<IServiceCatalogService, ServiceCatalogService>();
        services.AddScoped<ICouponService, CouponService>();

        // Intelligent Knowledge & RAG Services
        services.AddScoped<IDocumentKnowledgeService, DocumentKnowledgeService>();
        services.AddScoped<IDatabaseRagService, DatabaseRagService>();
        services.AddScoped<IRagOrchestratorService, RagOrchestratorService>();
        services.AddScoped<IChatbotService, RagOrchestratorService>();

        return services;
    }
}
