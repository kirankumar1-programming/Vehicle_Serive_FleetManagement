using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = "RequireAdministrator")]
public class AuditLogsApiController : ControllerBase
{
    private readonly IAuditService _auditService;
    private readonly ILogger<AuditLogsApiController> _logger;

    public AuditLogsApiController(IAuditService auditService, ILogger<AuditLogsApiController> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> GetAuditLogs([FromQuery] AuditLogFilterDto filter)
    {
        _logger.LogInformation("Querying audit logs by Admin: {UserName}. Entity: {Entity}, Action: {Action}",
            User.Identity?.Name, filter.EntityName, filter.Action);

        var result = await _auditService.GetFilteredLogsAsync(filter);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AuditLogDto>> GetAuditLogById(int id)
    {
        var log = await _auditService.GetLogByIdAsync(id);
        if (log == null)
        {
            return NotFound(new { message = $"Audit log with ID {id} was not found." });
        }

        return Ok(log);
    }
}
