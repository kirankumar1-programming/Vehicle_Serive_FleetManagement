using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsApiController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsApiController(IReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("revenue")]
    [Authorize(Roles = UserRoles.FinanceOrAdmin)]
    public async Task<ActionResult<RevenueReportDto>> GetRevenueReport([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var report = await _reportService.GetRevenueReportAsync(startDate, endDate);
        return Ok(report);
    }

    [HttpGet("metrics")]
    [Authorize(Roles = $"{UserRoles.Administrator},{UserRoles.ServiceAdvisor},{UserRoles.FinanceManager}")]
    public async Task<ActionResult<ServiceMetricsDto>> GetServiceMetrics([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var metrics = await _reportService.GetServiceMetricsAsync(startDate, endDate);
        return Ok(metrics);
    }

    [HttpGet("dashboard")]
    [Authorize(Roles = UserRoles.Administrator)]
    public async Task<ActionResult<DashboardMetricsDto>> GetDashboardMetrics()
    {
        var metrics = await _reportService.GetAdminDashboardMetricsAsync();
        return Ok(metrics);
    }
}
