using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Enums;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/assistance")]
[Authorize]
public class AssistanceApiController : ControllerBase
{
    private readonly IRoadsideService _roadsideService;

    public AssistanceApiController(IRoadsideService roadsideService)
    {
        _roadsideService = roadsideService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private bool IsStaff => User.IsInRole(UserRoles.Administrator) || User.IsInRole(UserRoles.ServiceAdvisor);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoadsideRequestDto>>> GetRequests([FromQuery] RoadsideStatus? status, [FromQuery] string? customerId)
    {
        // Customers can only access their own roadside assistance requests
        if (!IsStaff)
        {
            return Ok(await _roadsideService.GetCustomerRequestsAsync(CurrentUserId));
        }

        if (!string.IsNullOrEmpty(customerId))
            return Ok(await _roadsideService.GetCustomerRequestsAsync(customerId));

        return Ok(await _roadsideService.GetAllRequestsAsync(status));
    }

    [HttpPost]
    public async Task<ActionResult<RoadsideRequestDto>> CreateRequest([FromBody] CreateRoadsideRequestDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Prevent IDOR: Force CustomerId to authenticated user if not staff
        if (!IsStaff || string.IsNullOrEmpty(dto.CustomerId))
        {
            dto.CustomerId = CurrentUserId;
        }

        var result = await _roadsideService.CreateRequestAsync(dto);
        return Ok(result);
    }

    [HttpPut("{id:int}/status")]
    [Authorize(Roles = UserRoles.AdvisorOrAdmin)]
    public async Task<ActionResult<RoadsideRequestDto>> UpdateStatus(
        int id,
        [FromQuery] RoadsideStatus status,
        [FromQuery] string? notes,
        [FromQuery] string? techName,
        [FromQuery] string? techPhone)
    {
        var result = await _roadsideService.UpdateRequestStatusAsync(id, status, notes, techName, techPhone);
        return Ok(result);
    }
}
