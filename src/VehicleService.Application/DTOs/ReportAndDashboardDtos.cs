namespace VehicleService.Application.DTOs;

public class RevenueReportDto
{
    public decimal TotalRevenue { get; set; }
    public decimal PartsRevenue { get; set; }
    public decimal LaborRevenue { get; set; }
    public decimal TaxCollected { get; set; }
    public decimal TotalDiscounts { get; set; }
    public int PaidInvoicesCount { get; set; }
    public int PendingInvoicesCount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public List<MonthlyRevenueItemDto> MonthlyBreakdown { get; set; } = new();
}

public class MonthlyRevenueItemDto
{
    public string MonthName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int ServicesCompleted { get; set; }
}

public class ServiceMetricsDto
{
    public int TotalServicesBooked { get; set; }
    public int TotalCompleted { get; set; }
    public int ActiveJobsCount { get; set; }
    public decimal AverageServiceDurationHours { get; set; }
    public int DelayedJobsCount { get; set; }
    public List<PopularServiceDto> PopularServices { get; set; } = new();
    public List<MechanicProductivityDto> MechanicProductivity { get; set; } = new();
}

public class PopularServiceDto
{
    public string ServiceName { get; set; } = string.Empty;
    public int RequestCount { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class MechanicProductivityDto
{
    public string MechanicName { get; set; } = string.Empty;
    public int CompletedJobs { get; set; }
    public decimal TotalHoursLogged { get; set; }
}

public class DashboardMetricsDto
{
    public int TotalCustomers { get; set; }
    public int TotalVehicles { get; set; }
    public int TodayAppointments { get; set; }
    public int ActiveJobs { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal PendingPayments { get; set; }
    public int LowStockPartsCount { get; set; }
    public int OpenComplaintsCount { get; set; }
    public int AvailableBaysCount { get; set; }
    public int ActiveRoadsideRequests { get; set; }
}

public class FleetDashboardDto
{
    public int TotalFleetVehicles { get; set; }
    public int ActiveInService { get; set; }
    public int DueForMaintenanceCount { get; set; }
    public decimal TotalMaintenanceCost { get; set; }
    public decimal AverageCostPerVehicle { get; set; }
    public double EstimatedDowntimeDays { get; set; }
    public List<VehicleDto> FleetVehicles { get; set; } = new();
}
