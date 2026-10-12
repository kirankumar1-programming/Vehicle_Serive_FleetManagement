using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Enums;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/jobcards")]
[Authorize(Roles = UserRoles.MechanicOrAdvisorOrAdmin)]
public class JobCardsApiController : ControllerBase
{
    private readonly IJobCardService _jobCardService;

    public JobCardsApiController(IJobCardService jobCardService)
    {
        _jobCardService = jobCardService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private bool IsMechanicOnly => User.IsInRole(UserRoles.Mechanic) &&
                                  !User.IsInRole(UserRoles.Administrator) &&
                                  !User.IsInRole(UserRoles.ServiceAdvisor);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JobCardDto>>> GetJobCards(
        [FromQuery] AppointmentStatus? status,
        [FromQuery] int? centerId,
        [FromQuery] string? mechanicId)
    {
        // Mechanics can only see their assigned job cards
        if (IsMechanicOnly)
        {
            return Ok(await _jobCardService.GetMechanicJobCardsAsync(CurrentUserId));
        }

        if (!string.IsNullOrEmpty(mechanicId))
            return Ok(await _jobCardService.GetMechanicJobCardsAsync(mechanicId));

        return Ok(await _jobCardService.GetJobCardsAsync(status, centerId));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<JobCardDto>> GetJobCardById(int id)
    {
        var jobCard = await _jobCardService.GetJobCardByIdAsync(id);
        if (jobCard == null)
            return NotFound();

        if (IsMechanicOnly && jobCard.MechanicId != CurrentUserId)
            return Forbid();

        return Ok(jobCard);
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] AppointmentStatus newStatus, [FromQuery] string? notes)
    {
        var jobCard = await _jobCardService.GetJobCardByIdAsync(id);
        if (jobCard == null)
            return NotFound();

        if (IsMechanicOnly && jobCard.MechanicId != CurrentUserId)
            return Forbid();

        await _jobCardService.UpdateJobCardStatusAsync(id, newStatus, notes);
        return NoContent();
    }

    [HttpPost("{id:int}/assign-mechanic")]
    [Authorize(Roles = UserRoles.AdvisorOrAdmin)]
    public async Task<IActionResult> AssignMechanic(int id, [FromBody] string mechanicId)
    {
        if (string.IsNullOrWhiteSpace(mechanicId))
            return BadRequest("Mechanic ID is required.");

        await _jobCardService.AssignMechanicAsync(id, mechanicId);
        return NoContent();
    }

    [HttpPost("{id:int}/worklogs")]
    public async Task<IActionResult> AddWorkLog(int id, [FromBody] WorkLogDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (IsMechanicOnly || string.IsNullOrEmpty(dto.MechanicId))
        {
            dto.MechanicId = CurrentUserId;
        }

        await _jobCardService.AddWorkLogAsync(id, dto.MechanicId, dto.TaskDescription, dto.HoursSpent, dto.Observations);
        return Ok();
    }

    [HttpPost("{id:int}/parts")]
    public async Task<IActionResult> RecordPart(int id, [FromBody] PartConsumptionDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await _jobCardService.RecordReplacedPartAsync(id, dto.InventoryPartId, dto.Quantity, User.Identity?.Name ?? CurrentUserId);
        return Ok();
    }
}
