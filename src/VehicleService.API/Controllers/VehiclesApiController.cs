using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/vehicles")]
[Authorize]
public class VehiclesApiController : ControllerBase
{
    private readonly IVehicleService _vehicleService;

    public VehiclesApiController(IVehicleService vehicleService)
    {
        _vehicleService = vehicleService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private bool IsStaff => User.IsInRole(UserRoles.Administrator) ||
                            User.IsInRole(UserRoles.ServiceAdvisor) ||
                            User.IsInRole(UserRoles.Mechanic);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VehicleDto>>> GetVehicles([FromQuery] string? customerId, [FromQuery] int? fleetId)
    {
        // Enforce customer data isolation
        if (!IsStaff && !User.IsInRole(UserRoles.FleetManager))
        {
            return Ok(await _vehicleService.GetCustomerVehiclesAsync(CurrentUserId));
        }

        if (!string.IsNullOrEmpty(customerId))
            return Ok(await _vehicleService.GetCustomerVehiclesAsync(customerId));

        if (fleetId.HasValue)
            return Ok(await _vehicleService.GetFleetVehiclesAsync(fleetId.Value));

        return Ok(await _vehicleService.GetAllVehiclesAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VehicleDto>> GetVehicleById(int id)
    {
        var vehicle = await _vehicleService.GetVehicleByIdAsync(id);
        if (vehicle == null)
            return NotFound();

        // Enforce ownership checks for customers
        if (!IsStaff && !User.IsInRole(UserRoles.FleetManager) && vehicle.CustomerId != CurrentUserId)
            return Forbid();

        return Ok(vehicle);
    }

    [HttpPost]
    [Authorize(Roles = $"{UserRoles.Customer},{UserRoles.FleetManager},{UserRoles.ServiceAdvisor},{UserRoles.Administrator}")]
    public async Task<ActionResult<VehicleDto>> RegisterVehicle([FromBody] CreateVehicleDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Prevent IDOR: Force CustomerId to authenticated user for customer role
        if (!IsStaff && !User.IsInRole(UserRoles.FleetManager) || string.IsNullOrEmpty(dto.CustomerId))
        {
            dto.CustomerId = CurrentUserId;
        }

        var vehicle = await _vehicleService.RegisterVehicleAsync(dto);
        return CreatedAtAction(nameof(GetVehicleById), new { id = vehicle.Id }, vehicle);
    }

    [HttpPut("{id:int}/mileage")]
    public async Task<IActionResult> UpdateMileage(int id, [FromBody] int newMileage)
    {
        if (newMileage < 0)
            return BadRequest("Mileage must be non-negative.");

        var vehicle = await _vehicleService.GetVehicleByIdAsync(id);
        if (vehicle == null)
            return NotFound();

        if (!IsStaff && !User.IsInRole(UserRoles.FleetManager) && vehicle.CustomerId != CurrentUserId)
            return Forbid();

        await _vehicleService.UpdateMileageAsync(id, newMileage);
        return NoContent();
    }
}
