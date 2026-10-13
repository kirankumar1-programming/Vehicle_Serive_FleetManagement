using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/appointments")]
[Authorize]
public class AppointmentsApiController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;
    private readonly ILogger<AppointmentsApiController> _logger;

    public AppointmentsApiController(IAppointmentService appointmentService, ILogger<AppointmentsApiController> logger)
    {
        _appointmentService = appointmentService;
        _logger = logger;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private bool IsStaff => User.IsInRole(UserRoles.Administrator) || User.IsInRole(UserRoles.ServiceAdvisor);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AppointmentDto>>> GetAppointments(
        [FromQuery] string? customerId,
        [FromQuery] int? centerId,
        [FromQuery] DateTime? date)
    {
        // Enforce customer isolation: non-staff users can only query their own appointments
        if (!IsStaff)
        {
            return Ok(await _appointmentService.GetCustomerAppointmentsAsync(CurrentUserId));
        }

        if (!string.IsNullOrEmpty(customerId))
            return Ok(await _appointmentService.GetCustomerAppointmentsAsync(customerId));

        if (centerId.HasValue)
            return Ok(await _appointmentService.GetCenterAppointmentsAsync(centerId.Value, date));

        return Ok(await _appointmentService.GetAllAppointmentsAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AppointmentDto>> GetAppointmentById(int id)
    {
        var appointment = await _appointmentService.GetAppointmentByIdAsync(id);
        if (appointment == null)
            return NotFound();

        // Enforce resource authorization: Customers can only view their own appointments
        if (!IsStaff && appointment.CustomerId != CurrentUserId)
            return Forbid();

        return Ok(appointment);
    }

    [HttpPost]
    [Authorize(Roles = $"{UserRoles.Customer},{UserRoles.ServiceAdvisor},{UserRoles.Administrator}")]
    public async Task<ActionResult<AppointmentDto>> BookAppointment([FromBody] BookAppointmentDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Prevent IDOR: Force CustomerId to authenticated user for customer role
        if (!IsStaff || string.IsNullOrEmpty(dto.CustomerId))
        {
            dto.CustomerId = CurrentUserId;
        }

        var result = await _appointmentService.BookAppointmentAsync(dto);
        _logger.LogInformation("Appointment {AppointmentNumber} successfully booked for Customer {CustomerId} at Service Center {CenterId}",
            result.AppointmentNumber, result.CustomerId, result.ServiceCenterId);
        return CreatedAtAction(nameof(GetAppointmentById), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelAppointment(int id, [FromBody] string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest("A valid cancellation reason is required.");

        var appointment = await _appointmentService.GetAppointmentByIdAsync(id);
        if (appointment == null)
            return NotFound();

        // Check ownership or staff permission
        if (!IsStaff && appointment.CustomerId != CurrentUserId)
            return Forbid();

        var success = await _appointmentService.CancelAppointmentAsync(id, reason, User.Identity?.Name ?? CurrentUserId);
        if (!success)
            return BadRequest("Could not cancel appointment.");

        _logger.LogInformation("Appointment {Id} cancelled by {User}. Reason: {Reason}", id, User.Identity?.Name ?? CurrentUserId, reason);
        return NoContent();
    }
}
