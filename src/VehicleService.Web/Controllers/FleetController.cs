using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;

namespace VehicleService.Web.Controllers;

[Authorize(Roles = UserRoles.FleetOrAdmin)]
public class FleetController : Controller
{
    private readonly IFleetService _fleetService;
    private readonly IVehicleService _vehicleService;
    private readonly IPreventiveMaintenanceService _pmService;
    private readonly UserManager<ApplicationUser> _userManager;

    public FleetController(
        IFleetService fleetService,
        IVehicleService vehicleService,
        IPreventiveMaintenanceService pmService,
        UserManager<ApplicationUser> userManager)
    {
        _fleetService = fleetService;
        _vehicleService = vehicleService;
        _pmService = pmService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var fleets = await _fleetService.GetAllFleetsAsync();
        var defaultFleet = fleets.FirstOrDefault();

        if (defaultFleet == null) return View(new FleetDashboardDto());

        var dashboard = await _fleetService.GetFleetDashboardAsync(defaultFleet.Id);
        ViewBag.Fleets = fleets;
        ViewBag.SelectedFleet = defaultFleet;

        return View(dashboard);
    }

    public async Task<IActionResult> Vehicles(int? fleetId)
    {
        var fleets = await _fleetService.GetAllFleetsAsync();
        var targetFleetId = fleetId ?? (fleets.FirstOrDefault()?.Id ?? 0);
        var vehicles = await _vehicleService.GetFleetVehiclesAsync(targetFleetId);
        var drivers = await _userManager.GetUsersInRoleAsync(UserRoleType.Customer.ToString());

        ViewBag.Fleets = fleets;
        ViewBag.TargetFleetId = targetFleetId;
        ViewBag.Drivers = drivers;

        return View(vehicles);
    }

    public async Task<IActionResult> PreventiveMaintenance(int? fleetId)
    {
        var dueVehicles = await _pmService.GetVehiclesDueForMaintenanceAsync(fleetId);
        return View(dueVehicles);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignDriver(DriverAssignmentDto dto)
    {
        try
        {
            await _vehicleService.AssignDriverAsync(dto);
            TempData["SuccessMessage"] = "Driver assigned successfully!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Vehicles), new { fleetId = dto.VehicleId });
    }

    [HttpGet]
    public async Task<IActionResult> EditDetails(int? id)
    {
        var user = await _userManager.GetUserAsync(User);
        var targetFleetId = id ?? user?.CompanyFleetId;
        if (!targetFleetId.HasValue || targetFleetId.Value == 0)
        {
            var fleets = await _fleetService.GetAllFleetsAsync();
            targetFleetId = fleets.FirstOrDefault()?.Id;
        }

        if (!targetFleetId.HasValue)
        {
            TempData["ErrorMessage"] = "No company fleet found.";
            return RedirectToAction(nameof(Index));
        }

        var fleet = await _fleetService.GetFleetByIdAsync(targetFleetId.Value);
        if (fleet == null)
        {
            TempData["ErrorMessage"] = "Corporate fleet not found.";
            return RedirectToAction(nameof(Index));
        }

        var dto = new UpdateCompanyFleetDto
        {
            Id = fleet.Id,
            CompanyName = fleet.CompanyName,
            RegistrationNumber = fleet.RegistrationNumber,
            TaxId = fleet.TaxId,
            ContactPerson = fleet.ContactPerson,
            ContactEmail = fleet.ContactEmail,
            ContactPhone = fleet.ContactPhone,
            Address = fleet.Address,
            CorporateDiscountRate = fleet.CorporateDiscountRate,
            MonthlyBudgetLimit = fleet.MonthlyBudgetLimit,
            IsActive = fleet.IsActive
        };

        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDetails(UpdateCompanyFleetDto dto)
    {
        var fleet = await _fleetService.GetFleetByIdAsync(dto.Id);
        if (fleet == null)
        {
            TempData["ErrorMessage"] = "Corporate fleet not found.";
            return RedirectToAction(nameof(Index));
        }

        var name = dto.CompanyName.Trim();
        var allFleets = await _fleetService.GetAllFleetsAsync();
        var existing = allFleets.FirstOrDefault(f => f.CompanyName.Equals(name, StringComparison.OrdinalIgnoreCase) && f.Id != dto.Id);

        if (existing != null)
        {
            ModelState.AddModelError(nameof(dto.CompanyName), $"Another fleet with the name '{name}' already exists.");
        }

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        fleet.CompanyName = name;
        fleet.RegistrationNumber = dto.RegistrationNumber.Trim();
        fleet.TaxId = dto.TaxId.Trim();
        fleet.ContactPerson = dto.ContactPerson.Trim();
        fleet.ContactEmail = dto.ContactEmail.Trim();
        fleet.ContactPhone = dto.ContactPhone.Trim();
        fleet.Address = dto.Address.Trim();
        fleet.CorporateDiscountRate = dto.CorporateDiscountRate;
        fleet.MonthlyBudgetLimit = dto.MonthlyBudgetLimit;
        fleet.UpdatedAt = DateTime.UtcNow;

        await _fleetService.UpdateFleetAsync(fleet);

        TempData["SuccessMessage"] = $"Fleet '{fleet.CompanyName}' details updated successfully!";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> EditVehicle(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        var vehicle = await _vehicleService.GetVehicleByIdAsync(id);
        if (vehicle == null)
        {
            TempData["ErrorMessage"] = "Vehicle not found.";
            return RedirectToAction(nameof(Vehicles));
        }

        if (user?.CompanyFleetId.HasValue == true && vehicle.CompanyFleetId.HasValue && vehicle.CompanyFleetId != user.CompanyFleetId)
        {
            TempData["ErrorMessage"] = "Access denied to vehicles of other fleets.";
            return RedirectToAction(nameof(Vehicles));
        }

        var dto = new CreateVehicleDto
        {
            RegistrationNumber = vehicle.RegistrationNumber,
            VIN = vehicle.VIN,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Variant = vehicle.Variant,
            ManufacturingYear = vehicle.ManufacturingYear,
            Color = vehicle.Color,
            FuelType = vehicle.FuelType,
            Transmission = vehicle.Transmission,
            CurrentMileage = vehicle.CurrentMileage,
            InsuranceProvider = vehicle.InsuranceProvider,
            InsurancePolicyNumber = vehicle.InsurancePolicyNumber,
            InsuranceExpiryDate = vehicle.InsuranceExpiryDate,
            CompanyFleetId = vehicle.CompanyFleetId,
            CustomerId = vehicle.CustomerId,
            IsActive = vehicle.IsActive
        };

        ViewBag.VehicleId = id;
        ViewBag.RegistrationNumber = vehicle.RegistrationNumber;
        ViewBag.IsActive = vehicle.IsActive;
        ViewBag.FleetId = vehicle.CompanyFleetId;
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditVehicle(int id, CreateVehicleDto dto)
    {
        var user = await _userManager.GetUserAsync(User);
        var vehicle = await _vehicleService.GetVehicleByIdAsync(id);
        if (vehicle == null)
        {
            TempData["ErrorMessage"] = "Vehicle not found.";
            return RedirectToAction(nameof(Vehicles));
        }

        if (user?.CompanyFleetId.HasValue == true && vehicle.CompanyFleetId.HasValue && vehicle.CompanyFleetId != user.CompanyFleetId)
        {
            TempData["ErrorMessage"] = "Access denied to vehicles of other fleets.";
            return RedirectToAction(nameof(Vehicles));
        }

        dto.RegistrationNumber = vehicle.RegistrationNumber;
        dto.VIN = vehicle.VIN;
        dto.CompanyFleetId = vehicle.CompanyFleetId;
        dto.CustomerId = vehicle.CustomerId;

        try
        {
            await _vehicleService.UpdateVehicleAsync(id, dto);
            TempData["SuccessMessage"] = $"Fleet Vehicle {dto.Make} {dto.Model} ({vehicle.RegistrationNumber}) updated successfully!";
            return RedirectToAction(nameof(Vehicles), new { fleetId = vehicle.CompanyFleetId });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.VehicleId = id;
            ViewBag.RegistrationNumber = vehicle.RegistrationNumber;
            ViewBag.IsActive = dto.IsActive;
            ViewBag.FleetId = vehicle.CompanyFleetId;
            return View(dto);
        }
    }

    [HttpGet]
    public async Task<IActionResult> AddVehicle(int? fleetId)
    {
        var user = await _userManager.GetUserAsync(User);
        var targetFleetId = fleetId ?? user?.CompanyFleetId;

        if (!targetFleetId.HasValue || targetFleetId.Value == 0)
        {
            var fleets = await _fleetService.GetAllFleetsAsync();
            targetFleetId = fleets.FirstOrDefault()?.Id;
        }

        if (!targetFleetId.HasValue)
        {
            TempData["ErrorMessage"] = "No company fleet found to register vehicle.";
            return RedirectToAction(nameof(Vehicles));
        }

        var fleet = await _fleetService.GetFleetByIdAsync(targetFleetId.Value);
        if (fleet == null)
        {
            TempData["ErrorMessage"] = "Corporate fleet not found.";
            return RedirectToAction(nameof(Vehicles));
        }

        if (user?.CompanyFleetId.HasValue == true && user.CompanyFleetId.Value != targetFleetId.Value)
        {
            TempData["ErrorMessage"] = "Access denied to add vehicles to other corporate fleets.";
            return RedirectToAction(nameof(Vehicles));
        }

        var dto = new CreateVehicleDto
        {
            CompanyFleetId = targetFleetId.Value,
            ManufacturingYear = DateTime.UtcNow.Year,
            Color = "White",
            CurrentMileage = 0,
            IsActive = true
        };

        ViewBag.Fleet = fleet;
        ViewBag.FleetId = targetFleetId.Value;
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddVehicle(CreateVehicleDto dto)
    {
        var user = await _userManager.GetUserAsync(User);
        var targetFleetId = dto.CompanyFleetId ?? user?.CompanyFleetId;

        if (!targetFleetId.HasValue || targetFleetId.Value == 0)
        {
            var fleets = await _fleetService.GetAllFleetsAsync();
            targetFleetId = fleets.FirstOrDefault()?.Id;
        }

        if (!targetFleetId.HasValue)
        {
            TempData["ErrorMessage"] = "No company fleet found to register vehicle.";
            return RedirectToAction(nameof(Vehicles));
        }

        var fleet = await _fleetService.GetFleetByIdAsync(targetFleetId.Value);
        if (fleet == null)
        {
            TempData["ErrorMessage"] = "Corporate fleet not found.";
            return RedirectToAction(nameof(Vehicles));
        }

        if (user?.CompanyFleetId.HasValue == true && user.CompanyFleetId.Value != targetFleetId.Value)
        {
            TempData["ErrorMessage"] = "Access denied to add vehicles to other corporate fleets.";
            return RedirectToAction(nameof(Vehicles));
        }

        dto.CompanyFleetId = targetFleetId.Value;
        dto.CustomerId = null; // Commercial fleet vehicle

        if (string.IsNullOrWhiteSpace(dto.RegistrationNumber))
        {
            ModelState.AddModelError(nameof(dto.RegistrationNumber), "Registration / License plate number is required.");
        }
        if (string.IsNullOrWhiteSpace(dto.VIN))
        {
            ModelState.AddModelError(nameof(dto.VIN), "Vehicle Identification Number (VIN) is required.");
        }
        if (string.IsNullOrWhiteSpace(dto.Make))
        {
            ModelState.AddModelError(nameof(dto.Make), "Vehicle Make / Brand is required.");
        }
        if (string.IsNullOrWhiteSpace(dto.Model))
        {
            ModelState.AddModelError(nameof(dto.Model), "Vehicle Model is required.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Fleet = fleet;
            ViewBag.FleetId = targetFleetId.Value;
            return View(dto);
        }

        try
        {
            await _vehicleService.RegisterVehicleAsync(dto);
            TempData["SuccessMessage"] = $"Fleet Vehicle {dto.Make} {dto.Model} ({dto.RegistrationNumber.ToUpper().Trim()}) registered to {fleet.CompanyName} successfully!";
            return RedirectToAction(nameof(Vehicles), new { fleetId = targetFleetId.Value });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.Fleet = fleet;
            ViewBag.FleetId = targetFleetId.Value;
            return View(dto);
        }
    }
}
