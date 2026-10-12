using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Web.Controllers;

[Authorize(Roles = UserRoles.InventoryOrAdmin)]
public class InventoryController : Controller
{
    private readonly IInventoryService _inventoryService;
    private readonly IServiceCenterService _serviceCenterService;
    private readonly IAuditService _auditService;

    public InventoryController(
        IInventoryService inventoryService,
        IServiceCenterService serviceCenterService,
        IAuditService auditService)
    {
        _inventoryService = inventoryService;
        _serviceCenterService = serviceCenterService;
        _auditService = auditService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private string CurrentUserName => User.Identity?.Name ?? "User";

    public async Task<IActionResult> Index(string? search, string? category)
    {
        var parts = await _inventoryService.GetAllPartsAsync(search: search, category: category);
        var lowStock = await _inventoryService.GetLowStockPartsAsync();

        ViewBag.LowStockCount = lowStock.Count;
        ViewBag.TotalPartsCount = parts.Count;
        ViewBag.Search = search;
        ViewBag.Category = category;

        return View(parts);
    }

    public async Task<IActionResult> LowStock()
    {
        var lowStock = await _inventoryService.GetLowStockPartsAsync();
        return View(lowStock);
    }

    public async Task<IActionResult> Transactions(int? partId)
    {
        var txns = await _inventoryService.GetTransactionsAsync(partId);
        return View(txns);
    }

    [HttpGet]
    public IActionResult AddPart()
    {
        return View(new InventoryPartDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPart(InventoryPartDto dto)
    {
        try
        {
            await _inventoryService.CreatePartAsync(dto);
            TempData["SuccessMessage"] = $"Part {dto.Name} ({dto.PartNumber}) added to catalog!";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(dto);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustStock(AdjustStockDto dto)
    {
        dto.PerformedByUserId = CurrentUserId;
        try
        {
            await _inventoryService.AdjustStockAsync(dto);
            TempData["SuccessMessage"] = "Inventory stock adjustment completed successfully!";
        }
        catch (InsufficientStockException sEx)
        {
            TempData["ErrorMessage"] = sEx.Message;
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReconcileReservedStock()
    {
        try
        {
            var count = await _inventoryService.ReconcileReservedStockAsync();
            if (count > 0)
            {
                TempData["SuccessMessage"] = $"Successfully reconciled inventory! Updated {count} part(s) with orphaned reservations.";
            }
            else
            {
                TempData["SuccessMessage"] = "All reserved inventory quantities are fully synchronized with active reservations.";
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Reconciliation failed: {ex.Message}";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> EditPart(int id)
    {
        var part = await _inventoryService.GetPartByIdAsync(id);
        if (part == null)
        {
            TempData["ErrorMessage"] = "Spare part not found.";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.ServiceCenters = await _serviceCenterService.GetAllCentersAsync();
        return View(part);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPart(int id, InventoryPartDto dto)
    {
        if (id != dto.Id)
        {
            TempData["ErrorMessage"] = "Mismatched part identifier.";
            return RedirectToAction(nameof(Index));
        }

        var part = await _inventoryService.GetPartByIdAsync(id);
        if (part == null)
        {
            TempData["ErrorMessage"] = "Spare part not found.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            ViewBag.ServiceCenters = await _serviceCenterService.GetAllCentersAsync();
            return View(dto);
        }

        try
        {
            var oldValues = $"PartNumber={part.PartNumber}, Name={part.Name}, Category={part.Category}, Manufacturer={part.Manufacturer}, CostPrice={part.CostPrice}, SellingPrice={part.SellingPrice}, ReorderLevel={part.ReorderLevel}, Location={part.WarehouseLocation}, WarrantyDays={part.WarrantyPeriodDays}, ServiceCenterId={part.ServiceCenterId}, IsActive={part.IsActive}";
            await _inventoryService.UpdatePartAsync(id, dto);
            var newValues = $"PartNumber={dto.PartNumber}, Name={dto.Name}, Category={dto.Category}, Manufacturer={dto.Manufacturer}, CostPrice={dto.CostPrice}, SellingPrice={dto.SellingPrice}, ReorderLevel={dto.ReorderLevel}, Location={dto.WarehouseLocation}, WarrantyDays={dto.WarrantyPeriodDays}, ServiceCenterId={dto.ServiceCenterId}, IsActive={dto.IsActive}";

            await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateInventoryPart", nameof(InventoryPart), id.ToString(), oldValues, newValues);

            TempData["SuccessMessage"] = $"Spare part '{dto.Name}' ({dto.PartNumber}) updated successfully!";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.ServiceCenters = await _serviceCenterService.GetAllCentersAsync();
            return View(dto);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePartStatus(int id)
    {
        var part = await _inventoryService.GetPartByIdAsync(id);
        if (part == null)
        {
            TempData["ErrorMessage"] = "Spare part not found.";
            return RedirectToAction(nameof(Index));
        }

        part.IsActive = !part.IsActive;
        await _inventoryService.UpdatePartAsync(id, part);

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "TogglePartStatus", nameof(InventoryPart), id.ToString(), null, $"IsActive={part.IsActive}");

        TempData["SuccessMessage"] = $"Part '{part.Name}' ({part.PartNumber}) status changed to {(part.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(Index));
    }
}
