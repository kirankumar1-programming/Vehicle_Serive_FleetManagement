using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize(Roles = UserRoles.Staff)]
public class InventoryApiController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryApiController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InventoryPartDto>>> GetParts(
        [FromQuery] int? centerId,
        [FromQuery] string? search,
        [FromQuery] string? category)
    {
        return Ok(await _inventoryService.GetAllPartsAsync(centerId, search, category));
    }

    [HttpGet("low-stock")]
    [Authorize(Roles = $"{UserRoles.InventoryManager},{UserRoles.ServiceAdvisor},{UserRoles.Administrator}")]
    public async Task<ActionResult<IReadOnlyList<InventoryPartDto>>> GetLowStockParts([FromQuery] int? centerId)
    {
        return Ok(await _inventoryService.GetLowStockPartsAsync(centerId));
    }

    [HttpPost("adjust")]
    [Authorize(Roles = UserRoles.InventoryOrAdmin)]
    public async Task<IActionResult> AdjustStock([FromBody] AdjustStockDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        dto.PerformedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "InventoryManager";

        var success = await _inventoryService.AdjustStockAsync(dto);
        return Ok(new { success });
    }
}
