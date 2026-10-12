using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/servicecenters")]
[AllowAnonymous]
public class ServiceCentersApiController : ControllerBase
{
    private readonly IServiceCenterService _serviceCenterService;

    public ServiceCentersApiController(IServiceCenterService serviceCenterService)
    {
        _serviceCenterService = serviceCenterService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServiceCenter>>> GetCenters([FromQuery] bool activeOnly = true)
    {
        var centers = activeOnly
            ? await _serviceCenterService.GetActiveCentersAsync()
            : await _serviceCenterService.GetAllCentersAsync();
        return Ok(centers);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceCenter>> GetCenterById(int id)
    {
        var center = await _serviceCenterService.GetCenterByIdAsync(id);
        if (center == null) return NotFound();
        return Ok(center);
    }

    [HttpGet("{id:int}/bays")]
    public async Task<ActionResult<IReadOnlyList<ServiceBay>>> GetBaysByCenter(int id, [FromQuery] bool activeOnly = false)
    {
        var bays = await _serviceCenterService.GetBaysByCenterIdAsync(id, activeOnly);
        return Ok(bays);
    }
}
