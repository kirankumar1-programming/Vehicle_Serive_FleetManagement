using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthApiController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthApiController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtTokenService jwtTokenService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtTokenService = jwtTokenService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null) return Unauthorized(new AuthResponse { Success = false, Message = "Invalid email or password." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
        if (!result.Succeeded) return Unauthorized(new AuthResponse { Success = false, Message = "Invalid email or password." });

        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwtTokenService.GenerateToken(user, roles);

        return Ok(new AuthResponse
        {
            Success = true,
            Token = token,
            Expiration = DateTime.UtcNow.AddDays(7),
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = roles.FirstOrDefault() ?? user.RoleType.ToString(),
            Message = "Authentication successful."
        });
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing != null) return BadRequest(new AuthResponse { Success = false, Message = "Email is already registered." });

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            RoleType = request.RoleType,
            Address = request.Address,
            City = request.City,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new AuthResponse { Success = false, Message = string.Join(", ", result.Errors.Select(e => e.Description)) });
        }

        await _userManager.AddToRoleAsync(user, request.RoleType.ToString());
        var roles = new List<string> { request.RoleType.ToString() };
        var token = _jwtTokenService.GenerateToken(user, roles);

        return Ok(new AuthResponse
        {
            Success = true,
            Token = token,
            Expiration = DateTime.UtcNow.AddDays(7),
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = request.RoleType.ToString(),
            Message = "User registered successfully."
        });
    }
}

[ApiController]
[Route("api/vehicles")]
public class VehiclesApiController : ControllerBase
{
    private readonly IVehicleService _vehicleService;

    public VehiclesApiController(IVehicleService vehicleService)
    {
        _vehicleService = vehicleService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VehicleDto>>> GetVehicles([FromQuery] string? customerId, [FromQuery] int? fleetId)
    {
        if (!string.IsNullOrEmpty(customerId)) return Ok(await _vehicleService.GetCustomerVehiclesAsync(customerId));
        if (fleetId.HasValue) return Ok(await _vehicleService.GetFleetVehiclesAsync(fleetId.Value));
        return Ok(await _vehicleService.GetAllVehiclesAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VehicleDto>> GetVehicleById(int id)
    {
        var v = await _vehicleService.GetVehicleByIdAsync(id);
        if (v == null) return NotFound();
        return Ok(v);
    }

    [HttpPost]
    public async Task<ActionResult<VehicleDto>> RegisterVehicle([FromBody] CreateVehicleDto dto)
    {
        var v = await _vehicleService.RegisterVehicleAsync(dto);
        return CreatedAtAction(nameof(GetVehicleById), new { id = v.Id }, v);
    }

    [HttpPut("{id:int}/mileage")]
    public async Task<IActionResult> UpdateMileage(int id, [FromBody] int newMileage)
    {
        await _vehicleService.UpdateMileageAsync(id, newMileage);
        return NoContent();
    }
}

[ApiController]
[Route("api/appointments")]
public class AppointmentsApiController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;

    public AppointmentsApiController(IAppointmentService appointmentService)
    {
        _appointmentService = appointmentService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AppointmentDto>>> GetAppointments([FromQuery] string? customerId, [FromQuery] int? centerId, [FromQuery] DateTime? date)
    {
        if (!string.IsNullOrEmpty(customerId)) return Ok(await _appointmentService.GetCustomerAppointmentsAsync(customerId));
        if (centerId.HasValue) return Ok(await _appointmentService.GetCenterAppointmentsAsync(centerId.Value, date));
        return Ok(await _appointmentService.GetAllAppointmentsAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AppointmentDto>> GetAppointmentById(int id)
    {
        var a = await _appointmentService.GetAppointmentByIdAsync(id);
        if (a == null) return NotFound();
        return Ok(a);
    }

    [HttpPost]
    public async Task<ActionResult<AppointmentDto>> BookAppointment([FromBody] BookAppointmentDto dto)
    {
        var result = await _appointmentService.BookAppointmentAsync(dto);
        return CreatedAtAction(nameof(GetAppointmentById), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelAppointment(int id, [FromBody] string reason)
    {
        var success = await _appointmentService.CancelAppointmentAsync(id, reason, User.Identity?.Name ?? "API User");
        if (!success) return BadRequest("Could not cancel appointment.");
        return NoContent();
    }
}

[ApiController]
[Route("api/jobcards")]
public class JobCardsApiController : ControllerBase
{
    private readonly IJobCardService _jobCardService;

    public JobCardsApiController(IJobCardService jobCardService)
    {
        _jobCardService = jobCardService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JobCardDto>>> GetJobCards([FromQuery] AppointmentStatus? status, [FromQuery] int? centerId, [FromQuery] string? mechanicId)
    {
        if (!string.IsNullOrEmpty(mechanicId)) return Ok(await _jobCardService.GetMechanicJobCardsAsync(mechanicId));
        return Ok(await _jobCardService.GetJobCardsAsync(status, centerId));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<JobCardDto>> GetJobCardById(int id)
    {
        var j = await _jobCardService.GetJobCardByIdAsync(id);
        if (j == null) return NotFound();
        return Ok(j);
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] AppointmentStatus newStatus, [FromQuery] string? notes)
    {
        await _jobCardService.UpdateJobCardStatusAsync(id, newStatus, notes);
        return NoContent();
    }

    [HttpPost("{id:int}/assign-mechanic")]
    public async Task<IActionResult> AssignMechanic(int id, [FromBody] string mechanicId)
    {
        await _jobCardService.AssignMechanicAsync(id, mechanicId);
        return NoContent();
    }

    [HttpPost("{id:int}/worklogs")]
    public async Task<IActionResult> AddWorkLog(int id, [FromBody] WorkLogDto dto)
    {
        await _jobCardService.AddWorkLogAsync(id, dto.MechanicId, dto.TaskDescription, dto.HoursSpent, dto.Observations);
        return Ok();
    }

    [HttpPost("{id:int}/parts")]
    public async Task<IActionResult> RecordPart(int id, [FromBody] PartConsumptionDto dto)
    {
        await _jobCardService.RecordReplacedPartAsync(id, dto.InventoryPartId, dto.Quantity, User.Identity?.Name ?? "Mechanic");
        return Ok();
    }
}

[ApiController]
[Route("api/inventory")]
public class InventoryApiController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryApiController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InventoryPartDto>>> GetParts([FromQuery] int? centerId, [FromQuery] string? search, [FromQuery] string? category)
    {
        return Ok(await _inventoryService.GetAllPartsAsync(centerId, search, category));
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<IReadOnlyList<InventoryPartDto>>> GetLowStockParts([FromQuery] int? centerId)
    {
        return Ok(await _inventoryService.GetLowStockPartsAsync(centerId));
    }

    [HttpPost("adjust")]
    public async Task<IActionResult> AdjustStock([FromBody] AdjustStockDto dto)
    {
        var success = await _inventoryService.AdjustStockAsync(dto);
        return Ok(new { success });
    }
}

[ApiController]
[Route("api/payments")]
public class PaymentsApiController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsApiController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost]
    public async Task<ActionResult<PaymentResultDto>> ProcessPayment([FromBody] PaymentRequestDto dto)
    {
        var result = await _paymentService.ProcessPaymentAsync(dto);
        return Ok(result);
    }

    [HttpPost("callback")]
    public async Task<ActionResult<PaymentResultDto>> ProcessCallback([FromQuery] string transactionReference, [FromQuery] string gatewayTxnId, [FromQuery] PaymentStatus status, [FromQuery] decimal amount)
    {
        var result = await _paymentService.ProcessCallbackAsync(transactionReference, gatewayTxnId, status, amount);
        return Ok(result);
    }

    [HttpPost("refund")]
    public async Task<ActionResult<PaymentResultDto>> ProcessRefund([FromQuery] int invoiceId, [FromQuery] decimal amount, [FromQuery] string reason)
    {
        var result = await _paymentService.ProcessRefundAsync(invoiceId, amount, reason, User.Identity?.Name ?? "Finance");
        return Ok(result);
    }
}

[ApiController]
[Route("api/assistance")]
public class AssistanceApiController : ControllerBase
{
    private readonly IRoadsideService _roadsideService;

    public AssistanceApiController(IRoadsideService roadsideService)
    {
        _roadsideService = roadsideService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoadsideRequestDto>>> GetRequests([FromQuery] RoadsideStatus? status, [FromQuery] string? customerId)
    {
        if (!string.IsNullOrEmpty(customerId)) return Ok(await _roadsideService.GetCustomerRequestsAsync(customerId));
        return Ok(await _roadsideService.GetAllRequestsAsync(status));
    }

    [HttpPost]
    public async Task<ActionResult<RoadsideRequestDto>> CreateRequest([FromBody] CreateRoadsideRequestDto dto)
    {
        var result = await _roadsideService.CreateRequestAsync(dto);
        return Ok(result);
    }

    [HttpPut("{id:int}/status")]
    public async Task<ActionResult<RoadsideRequestDto>> UpdateStatus(int id, [FromQuery] RoadsideStatus status, [FromQuery] string? notes, [FromQuery] string? techName, [FromQuery] string? techPhone)
    {
        var result = await _roadsideService.UpdateRequestStatusAsync(id, status, notes, techName, techPhone);
        return Ok(result);
    }
}