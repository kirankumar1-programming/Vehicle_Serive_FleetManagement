using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/catalog")]
[AllowAnonymous]
public class CatalogApiController : ControllerBase
{
    private readonly IServiceCatalogService _catalogService;
    private readonly ICouponService _couponService;

    public CatalogApiController(IServiceCatalogService catalogService, ICouponService couponService)
    {
        _catalogService = catalogService;
        _couponService = couponService;
    }

    [HttpGet("services")]
    public async Task<ActionResult<IReadOnlyList<ServiceType>>> GetServices([FromQuery] bool activeOnly = true)
    {
        var services = activeOnly
            ? await _catalogService.GetActiveServiceTypesAsync()
            : await _catalogService.GetAllServiceTypesAsync();
        return Ok(services);
    }

    [HttpGet("packages")]
    public async Task<ActionResult<IReadOnlyList<ServicePackage>>> GetPackages([FromQuery] bool activeOnly = true)
    {
        var packages = activeOnly
            ? await _catalogService.GetActivePackagesAsync()
            : await _catalogService.GetAllPackagesAsync();
        return Ok(packages);
    }

    [HttpGet("packages/{id:int}")]
    public async Task<ActionResult<ServicePackage>> GetPackageById(int id)
    {
        var package = await _catalogService.GetPackageByIdAsync(id);
        if (package == null) return NotFound();
        return Ok(package);
    }

    [HttpGet("coupons/validate")]
    public async Task<IActionResult> ValidateCoupon([FromQuery] string code, [FromQuery] decimal amount)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest("Coupon code is required.");

        if (amount < 0)
            return BadRequest("Amount cannot be negative.");

        var result = await _couponService.ValidateCouponAsync(code, amount);
        return Ok(new
        {
            isValid = result.IsValid,
            discount = result.DiscountAmount,
            message = result.Message
        });
    }
}
