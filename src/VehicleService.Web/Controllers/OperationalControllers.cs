using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Web.Controllers;

// 1. SERVICE ADVISOR PORTAL
[Authorize]
public class ServiceAdvisorController : Controller
{
    private readonly IAppointmentService _appointmentService;
    private readonly IJobCardService _jobCardService;
    private readonly IInspectionService _inspectionService;
    private readonly IEstimateService _estimateService;
    private readonly IInventoryService _inventoryService;
    private readonly IInvoiceService _invoiceService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<ApplicationUser> _userManager;

    public ServiceAdvisorController(
        IAppointmentService appointmentService,
        IJobCardService jobCardService,
        IInspectionService inspectionService,
        IEstimateService estimateService,
        IInventoryService inventoryService,
        IInvoiceService invoiceService,
        IUnitOfWork unitOfWork,
        UserManager<ApplicationUser> userManager)
    {
        _appointmentService = appointmentService;
        _jobCardService = jobCardService;
        _inspectionService = inspectionService;
        _estimateService = estimateService;
        _inventoryService = inventoryService;
        _invoiceService = invoiceService;
        _unitOfWork = unitOfWork;
        _userManager = userManager;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    public async Task<IActionResult> Index()
    {
        var appointments = await _appointmentService.GetAllAppointmentsAsync();
        var todayAppts = appointments.Where(a => a.AppointmentDate.Date == DateTime.UtcNow.Date).ToList();
        var jobCards = await _jobCardService.GetJobCardsAsync();

        ViewBag.TodayAppointments = todayAppts;
        ViewBag.ActiveJobs = jobCards.Where(j => j.Status != AppointmentStatus.Completed && j.Status != AppointmentStatus.Cancelled).ToList();
        ViewBag.PendingEstimates = jobCards.Where(j => j.Status == AppointmentStatus.EstimateCreated || j.Status == AppointmentStatus.UnderInspection).ToList();
        ViewBag.ReadyForDelivery = jobCards.Where(j => j.Status == AppointmentStatus.ReadyForDelivery).ToList();

        return View();
    }

    public async Task<IActionResult> Appointments()
    {
        var appointments = await _appointmentService.GetAllAppointmentsAsync();
        return View(appointments);
    }

    [HttpGet]
    public async Task<IActionResult> CheckInVehicle(int appointmentId)
    {
        var appointment = await _appointmentService.GetAppointmentByIdAsync(appointmentId);
        if (appointment == null) return NotFound();

        var mechanics = await _userManager.GetUsersInRoleAsync(UserRoleType.Mechanic.ToString());
        var bays = await _unitOfWork.Repository<ServiceBay>().FindAsync(b => b.ServiceCenterId == appointment.ServiceCenterId && b.IsActive);

        var parts = await _inventoryService.GetAllPartsAsync();

        ViewBag.Appointment = appointment;
        ViewBag.Mechanics = mechanics;
        ViewBag.Bays = bays;
        ViewBag.Parts = parts;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckInVehicle(
        int appointmentId, 
        int? bayId, 
        string? mechanicId, 
        int mileage, 
        string fuelLevel, 
        string engineCond, 
        string brakeCond, 
        string tyreCond, 
        string batteryCond, 
        string fluidLevels, 
        string summary, 
        string? damages,
        List<int>? reservePartIds = null)
    {
        try
        {
            var jobCard = await _jobCardService.CreateJobCardFromAppointmentAsync(appointmentId, CurrentUserId, bayId, reservePartIds);

            var inspection = new VehicleInspection
            {
                JobCardId = jobCard.Id,
                VehicleId = jobCard.VehicleId,
                InspectedByUserId = CurrentUserId,
                Mileage = mileage,
                FuelLevel = fuelLevel,
                EngineCondition = engineCond,
                BrakeCondition = brakeCond,
                TyreCondition = tyreCond,
                BatteryCondition = batteryCond,
                FluidLevels = fluidLevels,
                InspectionSummary = summary,
                ExistingDamages = damages
            };
            await _inspectionService.SaveInspectionAsync(inspection);

            if (!string.IsNullOrEmpty(mechanicId))
            {
                await _jobCardService.AssignMechanicAsync(jobCard.Id, mechanicId);
            }

            TempData["SuccessMessage"] = $"Job Card {jobCard.JobCardNumber} generated & vehicle checked in successfully!";
            return RedirectToAction(nameof(JobCardDetails), new { id = jobCard.Id });
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(CheckInVehicle), new { appointmentId });
        }
    }

    public async Task<IActionResult> JobCardDetails(int id)
    {
        var jobCard = await _jobCardService.GetJobCardByIdAsync(id);
        if (jobCard == null) return NotFound();

        var inspection = await _inspectionService.GetInspectionByJobCardIdAsync(id);
        var estimate = await _estimateService.GetEstimateByJobCardIdAsync(id);
        var invoice = await _invoiceService.GetInvoiceByJobCardIdAsync(id);
        var mechanics = await _userManager.GetUsersInRoleAsync(UserRoleType.Mechanic.ToString());
        var parts = await _inventoryService.GetAllPartsAsync();
        var bays = await _unitOfWork.Repository<ServiceBay>().GetAllAsync();

        ViewBag.Inspection = inspection;
        ViewBag.Estimate = estimate;
        ViewBag.Invoice = invoice;
        ViewBag.Mechanics = mechanics;
        ViewBag.Parts = parts;
        ViewBag.Bays = bays.Where(b => b.IsActive).ToList();

        return View(jobCard);
    }

    [AcceptVerbs("GET", "POST")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> GenerateInvoice(int jobCardId)
    {
        try
        {
            var inv = await _invoiceService.GenerateInvoiceFromJobCardAsync(jobCardId);
            TempData["SuccessMessage"] = $"Invoice {inv.InvoiceNumber} generated successfully!";
            return RedirectToAction("InvoiceDetails", "Finance", new { id = inv.Id });
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(JobCardDetails), new { id = jobCardId });
        }
    }

    [HttpGet]
    public async Task<IActionResult> CreateEstimate(int jobCardId)
    {
        var jobCard = await _jobCardService.GetJobCardByIdAsync(jobCardId);
        if (jobCard == null) return NotFound();

        var parts = await _inventoryService.GetAllPartsAsync();
        ViewBag.JobCard = jobCard;
        ViewBag.Parts = parts;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEstimate(int jobCardId, List<EstimateItemDto> items)
    {
        try
        {
            var validItems = items.Where(i => !string.IsNullOrWhiteSpace(i.Description) && (i.UnitPrice > 0 || i.LaborCharges > 0)).ToList();
            if (validItems.Count == 0)
            {
                TempData["ErrorMessage"] = "Please add at least one valid estimate line item.";
                return RedirectToAction(nameof(CreateEstimate), new { jobCardId });
            }

            var est = await _estimateService.CreateEstimateAsync(jobCardId, CurrentUserId, validItems);
            TempData["SuccessMessage"] = $"Estimate {est.EstimateNumber} created and sent to customer!";
            return RedirectToAction(nameof(JobCardDetails), new { id = jobCardId });
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(CreateEstimate), new { jobCardId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignMechanic(int jobCardId, string mechanicId)
    {
        try
        {
            await _jobCardService.AssignMechanicAsync(jobCardId, mechanicId);
            TempData["SuccessMessage"] = "Mechanic assigned successfully!";
        }
        catch (MechanicUnavailableException mEx)
        {
            TempData["ErrorMessage"] = mEx.Message;
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(JobCardDetails), new { id = jobCardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDetails(UpdateJobCardDto dto)
    {
        try
        {
            await _jobCardService.UpdateJobCardDetailsAsync(dto);
            TempData["SuccessMessage"] = "Job card details updated successfully!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(JobCardDetails), new { id = dto.JobCardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelJobCard(int jobCardId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["ErrorMessage"] = "Please provide a valid reason for cancellation.";
            return RedirectToAction(nameof(JobCardDetails), new { id = jobCardId });
        }

        try
        {
            var success = await _jobCardService.CancelJobCardAsync(jobCardId, reason, CurrentUserId);
            if (success)
            {
                TempData["SuccessMessage"] = "Job card cancelled successfully. Reserved parts and bay released.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to cancel job card.";
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}

// 2. MECHANIC PORTAL
[Authorize]
public class MechanicController : Controller
{
    private readonly IJobCardService _jobCardService;
    private readonly IInventoryService _inventoryService;

    public MechanicController(IJobCardService jobCardService, IInventoryService inventoryService)
    {
        _jobCardService = jobCardService;
        _inventoryService = inventoryService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    public async Task<IActionResult> Index()
    {
        var myJobs = await _jobCardService.GetMechanicJobCardsAsync(CurrentUserId);
        return View(myJobs);
    }

    public async Task<IActionResult> JobDetails(int id)
    {
        var jobCard = await _jobCardService.GetJobCardByIdAsync(id);
        if (jobCard == null) return NotFound();

        var parts = await _inventoryService.GetAllPartsAsync();
        ViewBag.Parts = parts;

        return View(jobCard);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int jobCardId, AppointmentStatus newStatus, string? notes)
    {
        try
        {
            await _jobCardService.UpdateJobCardStatusAsync(jobCardId, newStatus, notes);
            TempData["SuccessMessage"] = $"Job card status updated to {newStatus}!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(JobDetails), new { id = jobCardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogLabor(int jobCardId, string taskDesc, decimal hours, string? observations)
    {
        try
        {
            await _jobCardService.AddWorkLogAsync(jobCardId, CurrentUserId, taskDesc, hours, observations);
            TempData["SuccessMessage"] = $"Logged {hours} hours of labor.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(JobDetails), new { id = jobCardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConsumePart(int jobCardId, int partId, int quantity)
    {
        try
        {
            await _inventoryService.ConsumePartAsync(partId, quantity, jobCardId, CurrentUserId);
            TempData["SuccessMessage"] = "Spare part recorded and consumed from stock.";
        }
        catch (InsufficientStockException sEx)
        {
            TempData["ErrorMessage"] = sEx.Message;
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(JobDetails), new { id = jobCardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDetails(UpdateJobCardDto dto, string? returnUrl = null)
    {
        try
        {
            await _jobCardService.UpdateJobCardDetailsAsync(dto);
            TempData["SuccessMessage"] = "Job card details updated successfully!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(JobDetails), new { id = dto.JobCardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelJobCard(int jobCardId, string reason, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["ErrorMessage"] = "Please provide a valid reason for cancellation.";
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(JobDetails), new { id = jobCardId });
        }

        try
        {
            var success = await _jobCardService.CancelJobCardAsync(jobCardId, reason, CurrentUserId);
            if (success)
            {
                TempData["SuccessMessage"] = "Job card cancelled successfully. Reserved parts and service bay have been released.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to cancel job card.";
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }
}

// 3. INVENTORY MANAGER PORTAL
[Authorize]
public class InventoryController : Controller
{
    private readonly IInventoryService _inventoryService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;

    public InventoryController(IInventoryService inventoryService, IUnitOfWork unitOfWork, IAuditService auditService)
    {
        _inventoryService = inventoryService;
        _unitOfWork = unitOfWork;
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

        ViewBag.ServiceCenters = await _unitOfWork.Repository<ServiceCenter>().GetAllAsync();
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
            ViewBag.ServiceCenters = await _unitOfWork.Repository<ServiceCenter>().GetAllAsync();
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
            ViewBag.ServiceCenters = await _unitOfWork.Repository<ServiceCenter>().GetAllAsync();
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

// 4. FINANCE MANAGER PORTAL
[Authorize]
public class FinanceController : Controller
{
    private readonly IInvoiceService _invoiceService;
    private readonly IPaymentService _paymentService;
    private readonly IReportService _reportService;
    private readonly IJobCardService _jobCardService;

    public FinanceController(
        IInvoiceService invoiceService,
        IPaymentService paymentService,
        IReportService reportService,
        IJobCardService jobCardService)
    {
        _invoiceService = invoiceService;
        _paymentService = paymentService;
        _reportService = reportService;
        _jobCardService = jobCardService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    public async Task<IActionResult> Index(PaymentStatus? status)
    {
        var invoices = await _invoiceService.GetInvoicesAsync(status: status);
        var report = await _reportService.GetRevenueReportAsync();
        var allJobCards = await _jobCardService.GetJobCardsAsync();
        var invoicedJobCardIds = invoices.Select(i => i.JobCardId).ToHashSet();
        var pendingJobCards = allJobCards
            .Where(j => j.Status != AppointmentStatus.Cancelled && !invoicedJobCardIds.Contains(j.Id))
            .ToList();

        ViewBag.RevenueReport = report;
        ViewBag.SelectedStatus = status;
        ViewBag.PendingJobCards = pendingJobCards;

        return View(invoices);
    }

    public async Task<IActionResult> InvoiceDetails(int id)
    {
        var inv = await _invoiceService.GetInvoiceByIdAsync(id);
        if (inv == null) return NotFound();
        return View(inv);
    }

    [AcceptVerbs("GET", "POST")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> GenerateInvoice(int jobCardId)
    {
        try
        {
            var inv = await _invoiceService.GenerateInvoiceFromJobCardAsync(jobCardId);
            TempData["SuccessMessage"] = $"Invoice {inv.InvoiceNumber} generated successfully!";
            return RedirectToAction(nameof(InvoiceDetails), new { id = inv.Id });
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction("JobCardDetails", "ServiceAdvisor", new { id = jobCardId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessRefund(int invoiceId, decimal amount, string reason)
    {
        try
        {
            var result = await _paymentService.ProcessRefundAsync(invoiceId, amount, reason, CurrentUserId);
            TempData["SuccessMessage"] = result.Message;
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(InvoiceDetails), new { id = invoiceId });
    }

    public async Task<IActionResult> RevenueReports()
    {
        var report = await _reportService.GetRevenueReportAsync();
        return View(report);
    }

    public async Task<IActionResult> ExportRevenueCsv()
    {
        var bytes = await _reportService.ExportRevenueReportCsvAsync();
        return File(bytes, "text/csv", $"RevenueReport_{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}

// 5. CORPORATE FLEET MANAGER PORTAL
[Authorize]
public class FleetController : Controller
{
    private readonly IFleetService _fleetService;
    private readonly IVehicleService _vehicleService;
    private readonly IPreventiveMaintenanceService _pmService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<ApplicationUser> _userManager;

    public FleetController(
        IFleetService fleetService,
        IVehicleService vehicleService,
        IPreventiveMaintenanceService pmService,
        IUnitOfWork unitOfWork,
        UserManager<ApplicationUser> userManager)
    {
        _fleetService = fleetService;
        _vehicleService = vehicleService;
        _pmService = pmService;
        _unitOfWork = unitOfWork;
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
        var existing = await _unitOfWork.Repository<CompanyFleet>().Query()
            .FirstOrDefaultAsync(f => f.CompanyName.ToLower() == name.ToLower() && f.Id != dto.Id);

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

// 6. ADMINISTRATOR PORTAL
[Authorize]
public class AdminController : Controller
{
    private readonly IReportService _reportService;
    private readonly IAuditService _auditService;
    private readonly IReviewAndComplaintService _feedbackService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(
        IReportService reportService,
        IAuditService auditService,
        IReviewAndComplaintService feedbackService,
        IUnitOfWork unitOfWork,
        UserManager<ApplicationUser> userManager)
    {
        _reportService = reportService;
        _auditService = auditService;
        _feedbackService = feedbackService;
        _unitOfWork = unitOfWork;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var metrics = await _reportService.GetAdminDashboardMetricsAsync();
        var serviceMetrics = await _reportService.GetServiceMetricsAsync();
        var recentAuditLogs = await _auditService.GetLogsAsync(10);

        ViewBag.ServiceMetrics = serviceMetrics;
        ViewBag.RecentLogs = recentAuditLogs;

        return View(metrics);
    }

    public async Task<IActionResult> Users()
    {
        var users = await _userManager.Users
            .Include(u => u.ServiceCenter)
            .Include(u => u.CompanyFleet)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();
        return View(users);
    }

    // Edit User Details
    [HttpGet]
    public async Task<IActionResult> EditUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Users));
        }

        ViewBag.ServiceCenters = await _unitOfWork.Repository<ServiceCenter>().GetAllAsync();
        ViewBag.CompanyFleets = await _unitOfWork.Repository<CompanyFleet>().GetAllAsync();

        var dto = new UpdateUserDetailsDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber,
            RoleType = user.RoleType,
            ServiceCenterId = user.ServiceCenterId,
            CompanyFleetId = user.CompanyFleetId,
            Address = user.Address,
            City = user.City,
            State = user.State,
            PostalCode = user.PostalCode,
            IsActive = user.IsActive
        };

        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(string id, UpdateUserDetailsDto dto)
    {
        if (id != dto.Id)
        {
            TempData["ErrorMessage"] = "Mismatched user identifier.";
            return RedirectToAction(nameof(Users));
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Users));
        }

        if (!ModelState.IsValid)
        {
            ViewBag.ServiceCenters = await _unitOfWork.Repository<ServiceCenter>().GetAllAsync();
            ViewBag.CompanyFleets = await _unitOfWork.Repository<CompanyFleet>().GetAllAsync();
            return View(dto);
        }

        var oldValues = $"FullName={user.FullName}, Email={user.Email}, Phone={user.PhoneNumber}, Role={user.RoleType}, CenterId={user.ServiceCenterId}, Active={user.IsActive}";

        user.FullName = dto.FullName.Trim();
        user.PhoneNumber = dto.PhoneNumber?.Trim();
        user.Address = dto.Address?.Trim();
        user.City = dto.City?.Trim();
        user.State = dto.State?.Trim();
        user.PostalCode = dto.PostalCode?.Trim();
        user.ServiceCenterId = dto.ServiceCenterId;
        user.CompanyFleetId = dto.CompanyFleetId;
        user.IsActive = dto.IsActive;

        // Update Email & UserName if changed
        var newEmail = dto.Email.Trim();
        if (!string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            var existingByEmail = await _userManager.FindByEmailAsync(newEmail);
            if (existingByEmail != null && existingByEmail.Id != user.Id)
            {
                ModelState.AddModelError(nameof(dto.Email), "Email address is already in use by another user.");
                ViewBag.ServiceCenters = await _unitOfWork.Repository<ServiceCenter>().GetAllAsync();
                ViewBag.CompanyFleets = await _unitOfWork.Repository<CompanyFleet>().GetAllAsync();
                return View(dto);
            }

            user.Email = newEmail;
            user.UserName = newEmail;
            user.NormalizedEmail = newEmail.ToUpper();
            user.NormalizedUserName = newEmail.ToUpper();
        }

        // Update role if changed
        if (user.RoleType != dto.RoleType)
        {
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Any())
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
            }
            await _userManager.AddToRoleAsync(user, dto.RoleType.ToString());
            user.RoleType = dto.RoleType;
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
            {
                ModelState.AddModelError("", err.Description);
            }
            ViewBag.ServiceCenters = await _unitOfWork.Repository<ServiceCenter>().GetAllAsync();
            ViewBag.CompanyFleets = await _unitOfWork.Repository<CompanyFleet>().GetAllAsync();
            return View(dto);
        }

        var newValues = $"FullName={user.FullName}, Email={user.Email}, Phone={user.PhoneNumber}, Role={user.RoleType}, CenterId={user.ServiceCenterId}, Active={user.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateUser", nameof(ApplicationUser), user.Id, oldValues, newValues);

        TempData["SuccessMessage"] = $"User '{user.FullName}' ({user.Email}) details updated successfully!";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUserStatus(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Users));
        }

        if (user.Id == CurrentUserId)
        {
            TempData["ErrorMessage"] = "You cannot deactivate your own administrative account.";
            return RedirectToAction(nameof(Users));
        }

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleUserStatus", nameof(ApplicationUser), user.Id, null, $"IsActive={user.IsActive}");

        TempData["SuccessMessage"] = $"User '{user.FullName}' status toggled to {(user.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(Users));
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private string CurrentUserName => User.Identity?.Name ?? "Administrator";

    public async Task<IActionResult> ServiceCenters()
    {
        var centers = await _unitOfWork.Repository<ServiceCenter>().Query()
            .Include(c => c.ServiceBays)
            .ToListAsync();
        return View(centers);
    }

    // Add Service Center
    [HttpGet]
    public IActionResult AddServiceCenter()
    {
        return View(new CreateServiceCenterDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddServiceCenter(CreateServiceCenterDto dto)
    {
        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var center = new ServiceCenter
        {
            Name = dto.Name.Trim(),
            Code = dto.Code.Trim().ToUpper(),
            Address = dto.Address.Trim(),
            City = dto.City.Trim(),
            State = dto.State.Trim(),
            Phone = dto.Phone?.Trim() ?? string.Empty,
            Email = dto.Email?.Trim() ?? string.Empty,
            OperatingHours = dto.OperatingHours.Trim(),
            MaxDailyCapacity = dto.MaxDailyCapacity,
            IsActive = dto.IsActive
        };

        await _unitOfWork.Repository<ServiceCenter>().AddAsync(center);
        await _unitOfWork.SaveChangesAsync();

        if (dto.InitialBaysCount > 0)
        {
            var bayTypes = new[] { "General Maintenance", "Express Service", "Wheel Alignment", "Heavy Repair", "Diagnostics" };
            for (int i = 1; i <= dto.InitialBaysCount; i++)
            {
                var bayType = bayTypes[(i - 1) % bayTypes.Length];
                var bay = new ServiceBay
                {
                    ServiceCenterId = center.Id,
                    BayNumber = $"BAY-{center.Code.Replace("SC-", "")}-{i:D2}",
                    BayName = $"Service Bay #{i} ({bayType})",
                    BayType = bayType,
                    Status = BayStatus.Available,
                    IsActive = true
                };
                await _unitOfWork.Repository<ServiceBay>().AddAsync(bay);
            }
            await _unitOfWork.SaveChangesAsync();
        }

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "AddServiceCenter", nameof(ServiceCenter), center.Id.ToString(), null, $"Name={center.Name}, Code={center.Code}, Capacity={center.MaxDailyCapacity}");

        TempData["SuccessMessage"] = $"Service Center '{center.Name}' ({center.Code}) created successfully!";
        return RedirectToAction(nameof(ServiceCenters));
    }

    // Edit Service Center
    [HttpGet]
    public async Task<IActionResult> EditServiceCenter(int id)
    {
        var center = await _unitOfWork.Repository<ServiceCenter>().GetByIdAsync(id);
        if (center == null)
        {
            TempData["ErrorMessage"] = "Service Center not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        var dto = new UpdateServiceCenterDto
        {
            Id = center.Id,
            Name = center.Name,
            Code = center.Code,
            Address = center.Address,
            City = center.City,
            State = center.State,
            Phone = center.Phone,
            Email = center.Email,
            OperatingHours = center.OperatingHours,
            MaxDailyCapacity = center.MaxDailyCapacity,
            IsActive = center.IsActive
        };

        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditServiceCenter(int id, UpdateServiceCenterDto dto)
    {
        if (id != dto.Id)
        {
            TempData["ErrorMessage"] = "Mismatched service center identifier.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        var center = await _unitOfWork.Repository<ServiceCenter>().GetByIdAsync(id);
        if (center == null)
        {
            TempData["ErrorMessage"] = "Service Center not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var oldValues = $"Name={center.Name}, Code={center.Code}, Capacity={center.MaxDailyCapacity}, Active={center.IsActive}";

        center.Name = dto.Name.Trim();
        center.Code = dto.Code.Trim();
        center.Address = dto.Address.Trim();
        center.City = dto.City.Trim();
        center.State = dto.State.Trim();
        center.Phone = dto.Phone?.Trim() ?? string.Empty;
        center.Email = dto.Email?.Trim() ?? string.Empty;
        center.OperatingHours = dto.OperatingHours.Trim();
        center.MaxDailyCapacity = dto.MaxDailyCapacity;
        center.IsActive = dto.IsActive;
        center.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<ServiceCenter>().UpdateAsync(center);
        await _unitOfWork.SaveChangesAsync();

        var newValues = $"Name={center.Name}, Code={center.Code}, Capacity={center.MaxDailyCapacity}, Active={center.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateServiceCenter", nameof(ServiceCenter), center.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Service Center '{center.Name}' ({center.Code}) updated successfully!";
        return RedirectToAction(nameof(ServiceCenters));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleServiceCenterStatus(int id)
    {
        var center = await _unitOfWork.Repository<ServiceCenter>().GetByIdAsync(id);
        if (center == null)
        {
            TempData["ErrorMessage"] = "Service Center not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        center.IsActive = !center.IsActive;
        center.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<ServiceCenter>().UpdateAsync(center);
        await _unitOfWork.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleServiceCenterStatus", nameof(ServiceCenter), center.Id.ToString(), null, $"IsActive={center.IsActive}");

        TempData["SuccessMessage"] = $"Service Center '{center.Name}' status toggled to {(center.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(ServiceCenters));
    }

    // Edit Service Bay
    [HttpGet]
    public async Task<IActionResult> EditServiceBay(int id)
    {
        var bay = await _unitOfWork.Repository<ServiceBay>().Query()
            .Include(b => b.ServiceCenter)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (bay == null)
        {
            TempData["ErrorMessage"] = "Service Bay not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        var dto = new UpdateServiceBayDto
        {
            Id = bay.Id,
            ServiceCenterId = bay.ServiceCenterId,
            ServiceCenterName = bay.ServiceCenter?.Name,
            BayNumber = bay.BayNumber,
            BayName = bay.BayName,
            BayType = bay.BayType,
            Status = bay.Status,
            IsActive = bay.IsActive
        };

        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditServiceBay(int id, UpdateServiceBayDto dto)
    {
        if (id != dto.Id)
        {
            TempData["ErrorMessage"] = "Mismatched service bay identifier.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        var bay = await _unitOfWork.Repository<ServiceBay>().Query()
            .Include(b => b.ServiceCenter)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (bay == null)
        {
            TempData["ErrorMessage"] = "Service Bay not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        if (!ModelState.IsValid)
        {
            dto.ServiceCenterName = bay.ServiceCenter?.Name;
            return View(dto);
        }

        var oldValues = $"BayNumber={bay.BayNumber}, BayName={bay.BayName}, BayType={bay.BayType}, Status={bay.Status}, Active={bay.IsActive}";

        bay.BayNumber = dto.BayNumber.Trim();
        bay.BayName = dto.BayName.Trim();
        bay.BayType = dto.BayType?.Trim();
        bay.Status = dto.Status;
        bay.IsActive = dto.IsActive;
        bay.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<ServiceBay>().UpdateAsync(bay);
        await _unitOfWork.SaveChangesAsync();

        var newValues = $"BayNumber={bay.BayNumber}, BayName={bay.BayName}, BayType={bay.BayType}, Status={bay.Status}, Active={bay.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateServiceBay", nameof(ServiceBay), bay.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Service Bay '{bay.BayName}' ({bay.BayNumber}) updated successfully!";
        return RedirectToAction(nameof(ServiceCenters));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBayStatus(int id, BayStatus status)
    {
        var bay = await _unitOfWork.Repository<ServiceBay>().GetByIdAsync(id);
        if (bay == null)
        {
            TempData["ErrorMessage"] = "Service Bay not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        var oldStatus = bay.Status;
        bay.Status = status;
        bay.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<ServiceBay>().UpdateAsync(bay);
        await _unitOfWork.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateBayStatus", nameof(ServiceBay), bay.Id.ToString(), $"Status={oldStatus}", $"Status={status}");

        TempData["SuccessMessage"] = $"Bay '{bay.BayName}' status changed to {status}.";
        return RedirectToAction(nameof(ServiceCenters));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBayStatus(int id)
    {
        var bay = await _unitOfWork.Repository<ServiceBay>().GetByIdAsync(id);
        if (bay == null)
        {
            TempData["ErrorMessage"] = "Service Bay not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        bay.IsActive = !bay.IsActive;
        bay.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<ServiceBay>().UpdateAsync(bay);
        await _unitOfWork.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleBayStatus", nameof(ServiceBay), bay.Id.ToString(), null, $"IsActive={bay.IsActive}");

        TempData["SuccessMessage"] = $"Bay '{bay.BayName}' toggled to {(bay.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(ServiceCenters));
    }

    // Add Service Bay
    [HttpGet]
    public async Task<IActionResult> AddServiceBay(int centerId)
    {
        var center = await _unitOfWork.Repository<ServiceCenter>().GetByIdAsync(centerId);
        if (center == null)
        {
            TempData["ErrorMessage"] = "Service Center not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        var dto = new UpdateServiceBayDto
        {
            ServiceCenterId = center.Id,
            ServiceCenterName = center.Name,
            Status = BayStatus.Available,
            IsActive = true
        };

        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddServiceBay(int centerId, UpdateServiceBayDto dto)
    {
        var center = await _unitOfWork.Repository<ServiceCenter>().GetByIdAsync(centerId);
        if (center == null)
        {
            TempData["ErrorMessage"] = "Service Center not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        if (!ModelState.IsValid)
        {
            dto.ServiceCenterName = center.Name;
            return View(dto);
        }

        var bay = new ServiceBay
        {
            ServiceCenterId = centerId,
            BayNumber = dto.BayNumber.Trim(),
            BayName = dto.BayName.Trim(),
            BayType = dto.BayType?.Trim(),
            Status = dto.Status,
            IsActive = dto.IsActive
        };

        await _unitOfWork.Repository<ServiceBay>().AddAsync(bay);
        await _unitOfWork.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "AddServiceBay", nameof(ServiceBay), bay.Id.ToString(), null, $"BayNumber={bay.BayNumber}, BayName={bay.BayName}, ServiceCenterId={centerId}");

        TempData["SuccessMessage"] = $"Service Bay '{bay.BayName}' added successfully to '{center.Name}'!";
        return RedirectToAction(nameof(ServiceCenters));
    }

    public async Task<IActionResult> ServiceCatalog()
    {
        var types = await _unitOfWork.Repository<ServiceType>().GetAllAsync();
        var packages = await _unitOfWork.Repository<ServicePackage>().Query()
            .Include(p => p.PackageItems).ThenInclude(i => i.ServiceType)
            .ToListAsync();

        ViewBag.Packages = packages;
        return View(types);
    }

    // Edit Service Type
    [HttpGet]
    public async Task<IActionResult> EditServiceType(int id)
    {
        var service = await _unitOfWork.Repository<ServiceType>().GetByIdAsync(id);
        if (service == null)
        {
            TempData["ErrorMessage"] = "Service not found in catalog.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        var dto = new UpdateServiceTypeDto
        {
            Id = service.Id,
            Name = service.Name,
            Code = service.Code,
            Description = service.Description,
            EstimatedDurationHours = service.EstimatedDurationHours,
            BasePrice = service.BasePrice,
            RecommendedPartsSummary = service.RecommendedPartsSummary,
            LaborCharges = service.LaborCharges,
            ApplicableTaxPercent = service.ApplicableTaxPercent,
            WarrantyPeriodDays = service.WarrantyPeriodDays,
            Category = service.Category,
            IsActive = service.IsActive
        };

        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditServiceType(int id, UpdateServiceTypeDto dto)
    {
        if (id != dto.Id)
        {
            TempData["ErrorMessage"] = "Mismatched service identifier.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        var service = await _unitOfWork.Repository<ServiceType>().GetByIdAsync(id);
        if (service == null)
        {
            TempData["ErrorMessage"] = "Service not found in catalog.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var oldValues = $"Name={service.Name}, BasePrice={service.BasePrice}, LaborCharges={service.LaborCharges}, Duration={service.EstimatedDurationHours}, Taxes={service.ApplicableTaxPercent}%, Warranty={service.WarrantyPeriodDays}d, Parts={service.RecommendedPartsSummary}";

        service.Name = dto.Name.Trim();
        service.Description = dto.Description.Trim();
        service.EstimatedDurationHours = dto.EstimatedDurationHours;
        service.BasePrice = dto.BasePrice;
        service.RecommendedPartsSummary = dto.RecommendedPartsSummary?.Trim();
        service.LaborCharges = dto.LaborCharges;
        service.ApplicableTaxPercent = dto.ApplicableTaxPercent;
        service.WarrantyPeriodDays = dto.WarrantyPeriodDays;
        service.Category = dto.Category?.Trim();
        service.IsActive = dto.IsActive;
        service.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<ServiceType>().UpdateAsync(service);
        await _unitOfWork.SaveChangesAsync();

        var newValues = $"Name={service.Name}, BasePrice={service.BasePrice}, LaborCharges={service.LaborCharges}, Duration={service.EstimatedDurationHours}, Taxes={service.ApplicableTaxPercent}%, Warranty={service.WarrantyPeriodDays}d, Parts={service.RecommendedPartsSummary}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateServiceType", nameof(ServiceType), service.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Service '{service.Name}' ({service.Code}) updated successfully!";
        return RedirectToAction(nameof(ServiceCatalog));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleServiceTypeStatus(int id)
    {
        var service = await _unitOfWork.Repository<ServiceType>().GetByIdAsync(id);
        if (service == null)
        {
            TempData["ErrorMessage"] = "Service not found in catalog.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        service.IsActive = !service.IsActive;
        service.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<ServiceType>().UpdateAsync(service);
        await _unitOfWork.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleServiceTypeStatus", nameof(ServiceType), service.Id.ToString(), null, $"IsActive={service.IsActive}");

        TempData["SuccessMessage"] = $"Service '{service.Name}' status toggled to {(service.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(ServiceCatalog));
    }

    // Add Bundled Service Package
    [HttpGet]
    public async Task<IActionResult> AddServicePackage()
    {
        ViewBag.ServiceTypes = await _unitOfWork.Repository<ServiceType>().Query()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Category).ThenBy(s => s.Name)
            .ToListAsync();

        var dto = new CreateServicePackageDto
        {
            ValidityDays = 365,
            IsActive = true
        };
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddServicePackage(CreateServicePackageDto dto)
    {
        var existing = await _unitOfWork.Repository<ServicePackage>().Query()
            .FirstOrDefaultAsync(p => p.Name.ToLower() == dto.Name.Trim().ToLower());

        if (existing != null)
        {
            ModelState.AddModelError(nameof(dto.Name), $"A service package with the name '{dto.Name}' already exists.");
        }

        if (dto.SelectedServiceTypeIds == null || !dto.SelectedServiceTypeIds.Any())
        {
            ModelState.AddModelError(nameof(dto.SelectedServiceTypeIds), "Please select at least one service to bundle into this package.");
        }

        var availableServices = await _unitOfWork.Repository<ServiceType>().Query()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Category).ThenBy(s => s.Name)
            .ToListAsync();

        ViewBag.ServiceTypes = availableServices;

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        // Calculate original total price from selected services
        var selectedServices = availableServices.Where(s => dto.SelectedServiceTypeIds!.Contains(s.Id)).ToList();
        var originalTotal = selectedServices.Sum(s => s.BasePrice + s.LaborCharges);

        if (dto.OriginalTotalPrice <= 0)
        {
            dto.OriginalTotalPrice = originalTotal;
        }

        if (dto.SavingsPercent <= 0 && dto.OriginalTotalPrice > 0 && dto.OriginalTotalPrice > dto.PackagePrice)
        {
            dto.SavingsPercent = Math.Round(((dto.OriginalTotalPrice - dto.PackagePrice) / dto.OriginalTotalPrice) * 100, 1);
        }

        var package = new ServicePackage
        {
            Name = dto.Name.Trim(),
            Description = dto.Description.Trim(),
            PackagePrice = dto.PackagePrice,
            OriginalTotalPrice = dto.OriginalTotalPrice,
            SavingsPercent = dto.SavingsPercent,
            ValidityDays = dto.ValidityDays,
            IsActive = dto.IsActive
        };

        await _unitOfWork.Repository<ServicePackage>().AddAsync(package);
        await _unitOfWork.SaveChangesAsync();

        // Add bundled package items
        if (dto.SelectedServiceTypeIds != null && dto.SelectedServiceTypeIds.Any())
        {
            foreach (var sId in dto.SelectedServiceTypeIds)
            {
                await _unitOfWork.Repository<ServicePackageItem>().AddAsync(new ServicePackageItem
                {
                    ServicePackageId = package.Id,
                    ServiceTypeId = sId
                });
            }
            await _unitOfWork.SaveChangesAsync();
        }

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "AddServicePackage", nameof(ServicePackage), package.Id.ToString(), null,
            $"Name={package.Name}, Price={package.PackagePrice}, OriginalPrice={package.OriginalTotalPrice}, Savings={package.SavingsPercent}%, ItemsCount={dto.SelectedServiceTypeIds?.Count ?? 0}");

        TempData["SuccessMessage"] = $"Bundled Service Package '{package.Name}' created successfully!";
        return RedirectToAction(nameof(ServiceCatalog));
    }

    // Edit Bundled Service Package
    [HttpGet]
    public async Task<IActionResult> EditServicePackage(int id)
    {
        var package = await _unitOfWork.Repository<ServicePackage>().Query()
            .Include(p => p.PackageItems)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (package == null)
        {
            TempData["ErrorMessage"] = "Service package not found.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        ViewBag.ServiceTypes = await _unitOfWork.Repository<ServiceType>().Query()
            .OrderBy(s => s.Category).ThenBy(s => s.Name)
            .ToListAsync();

        var dto = new UpdateServicePackageDto
        {
            Id = package.Id,
            Name = package.Name,
            Description = package.Description,
            PackagePrice = package.PackagePrice,
            OriginalTotalPrice = package.OriginalTotalPrice,
            SavingsPercent = package.SavingsPercent,
            ValidityDays = package.ValidityDays,
            IsActive = package.IsActive,
            SelectedServiceTypeIds = package.PackageItems.Select(i => i.ServiceTypeId).ToList()
        };

        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditServicePackage(int id, UpdateServicePackageDto dto)
    {
        if (id != dto.Id)
        {
            TempData["ErrorMessage"] = "Mismatched package identifier.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        var package = await _unitOfWork.Repository<ServicePackage>().Query()
            .Include(p => p.PackageItems)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (package == null)
        {
            TempData["ErrorMessage"] = "Service package not found.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        var existing = await _unitOfWork.Repository<ServicePackage>().Query()
            .FirstOrDefaultAsync(p => p.Name.ToLower() == dto.Name.Trim().ToLower() && p.Id != id);

        if (existing != null)
        {
            ModelState.AddModelError(nameof(dto.Name), $"Another service package with the name '{dto.Name}' already exists.");
        }

        if (dto.SelectedServiceTypeIds == null || !dto.SelectedServiceTypeIds.Any())
        {
            ModelState.AddModelError(nameof(dto.SelectedServiceTypeIds), "Please select at least one service to bundle into this package.");
        }

        var allServices = await _unitOfWork.Repository<ServiceType>().Query()
            .OrderBy(s => s.Category).ThenBy(s => s.Name)
            .ToListAsync();

        ViewBag.ServiceTypes = allServices;

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var oldValues = $"Name={package.Name}, Price={package.PackagePrice}, OriginalPrice={package.OriginalTotalPrice}, Savings={package.SavingsPercent}%, ItemsCount={package.PackageItems.Count}, Active={package.IsActive}";

        var selectedServices = allServices.Where(s => dto.SelectedServiceTypeIds!.Contains(s.Id)).ToList();
        var calculatedOriginal = selectedServices.Sum(s => s.BasePrice + s.LaborCharges);

        if (dto.OriginalTotalPrice <= 0)
        {
            dto.OriginalTotalPrice = calculatedOriginal;
        }

        if (dto.SavingsPercent <= 0 && dto.OriginalTotalPrice > 0 && dto.OriginalTotalPrice > dto.PackagePrice)
        {
            dto.SavingsPercent = Math.Round(((dto.OriginalTotalPrice - dto.PackagePrice) / dto.OriginalTotalPrice) * 100, 1);
        }

        package.Name = dto.Name.Trim();
        package.Description = dto.Description.Trim();
        package.PackagePrice = dto.PackagePrice;
        package.OriginalTotalPrice = dto.OriginalTotalPrice;
        package.SavingsPercent = dto.SavingsPercent;
        package.ValidityDays = dto.ValidityDays;
        package.IsActive = dto.IsActive;
        package.UpdatedAt = DateTime.UtcNow;

        // Update Package Items
        var existingItemIds = package.PackageItems.Select(i => i.ServiceTypeId).ToList();
        var newItemIds = dto.SelectedServiceTypeIds ?? new List<int>();

        // Remove unselected
        var itemsToRemove = package.PackageItems.Where(i => !newItemIds.Contains(i.ServiceTypeId)).ToList();
        foreach (var item in itemsToRemove)
        {
            await _unitOfWork.Repository<ServicePackageItem>().DeleteAsync(item);
        }

        // Add newly selected
        var itemsToAdd = newItemIds.Where(id => !existingItemIds.Contains(id)).ToList();
        foreach (var serviceTypeId in itemsToAdd)
        {
            await _unitOfWork.Repository<ServicePackageItem>().AddAsync(new ServicePackageItem
            {
                ServicePackageId = package.Id,
                ServiceTypeId = serviceTypeId
            });
        }

        await _unitOfWork.Repository<ServicePackage>().UpdateAsync(package);
        await _unitOfWork.SaveChangesAsync();

        var newValues = $"Name={package.Name}, Price={package.PackagePrice}, OriginalPrice={package.OriginalTotalPrice}, Savings={package.SavingsPercent}%, ItemsCount={newItemIds.Count}, Active={package.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateServicePackage", nameof(ServicePackage), package.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Bundled Service Package '{package.Name}' updated successfully!";
        return RedirectToAction(nameof(ServiceCatalog));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleServicePackageStatus(int id)
    {
        var package = await _unitOfWork.Repository<ServicePackage>().GetByIdAsync(id);
        if (package == null)
        {
            TempData["ErrorMessage"] = "Service package not found.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        package.IsActive = !package.IsActive;
        package.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<ServicePackage>().UpdateAsync(package);
        await _unitOfWork.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleServicePackageStatus", nameof(ServicePackage), package.Id.ToString(), null, $"IsActive={package.IsActive}");

        TempData["SuccessMessage"] = $"Package '{package.Name}' status toggled to {(package.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(ServiceCatalog));
    }

    public async Task<IActionResult> Coupons()
    {
        var coupons = await _unitOfWork.Repository<Coupon>().GetAllAsync();
        return View(coupons.OrderByDescending(c => c.CreatedAt).ToList());
    }

    [HttpGet]
    public IActionResult AddCoupon()
    {
        var dto = new CreateCouponDto
        {
            Type = DiscountType.Percentage,
            Value = 10m,
            MinimumBillAmount = 1000m,
            MaxDiscountLimit = 2000m,
            UsageLimit = 500,
            ExpiryDate = DateTime.UtcNow.AddMonths(6),
            IsActive = true
        };
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCoupon(CreateCouponDto dto)
    {
        var code = dto.Code.Trim().ToUpper();
        var existing = await _unitOfWork.Repository<Coupon>().Query()
            .FirstOrDefaultAsync(c => c.Code.ToUpper() == code);

        if (existing != null)
        {
            ModelState.AddModelError(nameof(dto.Code), $"A coupon with code '{code}' already exists.");
        }

        if (dto.Type == DiscountType.Percentage && dto.Value > 100)
        {
            ModelState.AddModelError(nameof(dto.Value), "Percentage discount cannot exceed 100%.");
        }

        if (dto.ExpiryDate <= DateTime.UtcNow.Date)
        {
            ModelState.AddModelError(nameof(dto.ExpiryDate), "Expiry date must be in the future.");
        }

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var coupon = new Coupon
        {
            Code = code,
            Title = dto.Title.Trim(),
            Type = dto.Type,
            Value = dto.Value,
            MinimumBillAmount = dto.MinimumBillAmount,
            MaxDiscountLimit = dto.MaxDiscountLimit,
            UsageLimit = dto.UsageLimit,
            TimesUsed = 0,
            ExpiryDate = dto.ExpiryDate,
            IsActive = dto.IsActive
        };

        await _unitOfWork.Repository<Coupon>().AddAsync(coupon);
        await _unitOfWork.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "AddCoupon", nameof(Coupon), coupon.Id.ToString(), null,
            $"Code={coupon.Code}, Title={coupon.Title}, Type={coupon.Type}, Value={coupon.Value}, MinBill={coupon.MinimumBillAmount}, MaxCap={coupon.MaxDiscountLimit}, UsageLimit={coupon.UsageLimit}");

        TempData["SuccessMessage"] = $"Promotional coupon '{coupon.Code}' created successfully!";
        return RedirectToAction(nameof(Coupons));
    }

    [HttpGet]
    public async Task<IActionResult> EditCoupon(int id)
    {
        var coupon = await _unitOfWork.Repository<Coupon>().GetByIdAsync(id);
        if (coupon == null)
        {
            TempData["ErrorMessage"] = "Coupon not found.";
            return RedirectToAction(nameof(Coupons));
        }

        var dto = new UpdateCouponDto
        {
            Id = coupon.Id,
            Code = coupon.Code,
            Title = coupon.Title,
            Type = coupon.Type,
            Value = coupon.Value,
            MinimumBillAmount = coupon.MinimumBillAmount,
            MaxDiscountLimit = coupon.MaxDiscountLimit,
            UsageLimit = coupon.UsageLimit,
            TimesUsed = coupon.TimesUsed,
            ExpiryDate = coupon.ExpiryDate,
            IsActive = coupon.IsActive
        };

        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCoupon(int id, UpdateCouponDto dto)
    {
        if (id != dto.Id)
        {
            TempData["ErrorMessage"] = "Mismatched coupon identifier.";
            return RedirectToAction(nameof(Coupons));
        }

        var coupon = await _unitOfWork.Repository<Coupon>().GetByIdAsync(id);
        if (coupon == null)
        {
            TempData["ErrorMessage"] = "Coupon not found.";
            return RedirectToAction(nameof(Coupons));
        }

        var code = dto.Code.Trim().ToUpper();
        var existing = await _unitOfWork.Repository<Coupon>().Query()
            .FirstOrDefaultAsync(c => c.Code.ToUpper() == code && c.Id != id);

        if (existing != null)
        {
            ModelState.AddModelError(nameof(dto.Code), $"Another coupon with code '{code}' already exists.");
        }

        if (dto.Type == DiscountType.Percentage && dto.Value > 100)
        {
            ModelState.AddModelError(nameof(dto.Value), "Percentage discount cannot exceed 100%.");
        }

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var oldValues = $"Code={coupon.Code}, Title={coupon.Title}, Type={coupon.Type}, Value={coupon.Value}, MinBill={coupon.MinimumBillAmount}, MaxCap={coupon.MaxDiscountLimit}, UsageLimit={coupon.UsageLimit}, Expiry={coupon.ExpiryDate:yyyy-MM-dd}, Active={coupon.IsActive}";

        coupon.Code = code;
        coupon.Title = dto.Title.Trim();
        coupon.Type = dto.Type;
        coupon.Value = dto.Value;
        coupon.MinimumBillAmount = dto.MinimumBillAmount;
        coupon.MaxDiscountLimit = dto.MaxDiscountLimit;
        coupon.UsageLimit = dto.UsageLimit;
        coupon.ExpiryDate = dto.ExpiryDate;
        coupon.IsActive = dto.IsActive;
        coupon.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<Coupon>().UpdateAsync(coupon);
        await _unitOfWork.SaveChangesAsync();

        var newValues = $"Code={coupon.Code}, Title={coupon.Title}, Type={coupon.Type}, Value={coupon.Value}, MinBill={coupon.MinimumBillAmount}, MaxCap={coupon.MaxDiscountLimit}, UsageLimit={coupon.UsageLimit}, Expiry={coupon.ExpiryDate:yyyy-MM-dd}, Active={coupon.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateCoupon", nameof(Coupon), coupon.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Coupon '{coupon.Code}' details updated successfully!";
        return RedirectToAction(nameof(Coupons));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCouponStatus(int id)
    {
        var coupon = await _unitOfWork.Repository<Coupon>().GetByIdAsync(id);
        if (coupon == null)
        {
            TempData["ErrorMessage"] = "Coupon not found.";
            return RedirectToAction(nameof(Coupons));
        }

        coupon.IsActive = !coupon.IsActive;
        coupon.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<Coupon>().UpdateAsync(coupon);
        await _unitOfWork.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleCouponStatus", nameof(Coupon), coupon.Id.ToString(), null, $"IsActive={coupon.IsActive}");

        TempData["SuccessMessage"] = $"Coupon '{coupon.Code}' status toggled to {(coupon.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(Coupons));
    }

    public async Task<IActionResult> AuditLogs()
    {
        var logs = await _auditService.GetLogsAsync(100);
        return View(logs);
    }

    public async Task<IActionResult> Reviews()
    {
        var reviews = await _feedbackService.GetAllReviewsForModerationAsync();
        return View(reviews);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ModerateReview(int reviewId, ReviewStatus status, string? notes)
    {
        await _feedbackService.ModerateReviewAsync(reviewId, status, notes);
        TempData["SuccessMessage"] = $"Review moderation updated to: {status}";
        return RedirectToAction(nameof(Reviews));
    }

    // Corporate Fleet Management for Admin
    public async Task<IActionResult> Fleets()
    {
        var fleets = await _unitOfWork.Repository<CompanyFleet>().Query()
            .Include(f => f.Vehicles)
            .Include(f => f.ManagersAndDrivers)
            .OrderBy(f => f.CompanyName)
            .ToListAsync();
        return View(fleets);
    }

    [HttpGet]
    public IActionResult AddFleet()
    {
        return View(new CreateCompanyFleetDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddFleet(CreateCompanyFleetDto dto)
    {
        var name = dto.CompanyName.Trim();
        var existing = await _unitOfWork.Repository<CompanyFleet>().Query()
            .FirstOrDefaultAsync(f => f.CompanyName.ToLower() == name.ToLower());

        if (existing != null)
        {
            ModelState.AddModelError(nameof(dto.CompanyName), $"A corporate fleet with the name '{name}' already exists.");
        }

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var fleet = new CompanyFleet
        {
            CompanyName = name,
            RegistrationNumber = dto.RegistrationNumber.Trim(),
            TaxId = dto.TaxId.Trim(),
            ContactPerson = dto.ContactPerson.Trim(),
            ContactEmail = dto.ContactEmail.Trim(),
            ContactPhone = dto.ContactPhone.Trim(),
            Address = dto.Address.Trim(),
            CorporateDiscountRate = dto.CorporateDiscountRate,
            MonthlyBudgetLimit = dto.MonthlyBudgetLimit,
            IsActive = dto.IsActive
        };

        await _unitOfWork.Repository<CompanyFleet>().AddAsync(fleet);
        await _unitOfWork.SaveChangesAsync();

        var newValues = $"Name={fleet.CompanyName}, RegNo={fleet.RegistrationNumber}, TaxId={fleet.TaxId}, ContactPerson={fleet.ContactPerson}, Email={fleet.ContactEmail}, Discount={fleet.CorporateDiscountRate}%, Budget={fleet.MonthlyBudgetLimit}, Active={fleet.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "AddCompanyFleet", nameof(CompanyFleet), fleet.Id.ToString(), null, newValues);

        TempData["SuccessMessage"] = $"Corporate Fleet '{fleet.CompanyName}' created successfully!";
        return RedirectToAction(nameof(Fleets));
    }

    [HttpGet]
    public async Task<IActionResult> EditFleet(int id)
    {
        var fleet = await _unitOfWork.Repository<CompanyFleet>().GetByIdAsync(id);
        if (fleet == null)
        {
            TempData["ErrorMessage"] = "Corporate fleet not found.";
            return RedirectToAction(nameof(Fleets));
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
    public async Task<IActionResult> EditFleet(int id, UpdateCompanyFleetDto dto)
    {
        if (id != dto.Id)
        {
            TempData["ErrorMessage"] = "Mismatched fleet identifier.";
            return RedirectToAction(nameof(Fleets));
        }

        var fleet = await _unitOfWork.Repository<CompanyFleet>().GetByIdAsync(id);
        if (fleet == null)
        {
            TempData["ErrorMessage"] = "Corporate fleet not found.";
            return RedirectToAction(nameof(Fleets));
        }

        var name = dto.CompanyName.Trim();
        var existing = await _unitOfWork.Repository<CompanyFleet>().Query()
            .FirstOrDefaultAsync(f => f.CompanyName.ToLower() == name.ToLower() && f.Id != id);

        if (existing != null)
        {
            ModelState.AddModelError(nameof(dto.CompanyName), $"Another corporate fleet with the name '{name}' already exists.");
        }

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var oldValues = $"Name={fleet.CompanyName}, RegNo={fleet.RegistrationNumber}, TaxId={fleet.TaxId}, ContactPerson={fleet.ContactPerson}, Email={fleet.ContactEmail}, Discount={fleet.CorporateDiscountRate}%, Budget={fleet.MonthlyBudgetLimit}, Active={fleet.IsActive}";

        fleet.CompanyName = name;
        fleet.RegistrationNumber = dto.RegistrationNumber.Trim();
        fleet.TaxId = dto.TaxId.Trim();
        fleet.ContactPerson = dto.ContactPerson.Trim();
        fleet.ContactEmail = dto.ContactEmail.Trim();
        fleet.ContactPhone = dto.ContactPhone.Trim();
        fleet.Address = dto.Address.Trim();
        fleet.CorporateDiscountRate = dto.CorporateDiscountRate;
        fleet.MonthlyBudgetLimit = dto.MonthlyBudgetLimit;
        fleet.IsActive = dto.IsActive;
        fleet.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<CompanyFleet>().UpdateAsync(fleet);
        await _unitOfWork.SaveChangesAsync();

        var newValues = $"Name={fleet.CompanyName}, RegNo={fleet.RegistrationNumber}, TaxId={fleet.TaxId}, ContactPerson={fleet.ContactPerson}, Email={fleet.ContactEmail}, Discount={fleet.CorporateDiscountRate}%, Budget={fleet.MonthlyBudgetLimit}, Active={fleet.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateCompanyFleet", nameof(CompanyFleet), fleet.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Corporate Fleet '{fleet.CompanyName}' updated successfully!";
        return RedirectToAction(nameof(Fleets));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleFleetStatus(int id)
    {
        var fleet = await _unitOfWork.Repository<CompanyFleet>().GetByIdAsync(id);
        if (fleet == null)
        {
            TempData["ErrorMessage"] = "Corporate fleet not found.";
            return RedirectToAction(nameof(Fleets));
        }

        fleet.IsActive = !fleet.IsActive;
        fleet.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<CompanyFleet>().UpdateAsync(fleet);
        await _unitOfWork.SaveChangesAsync();

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleFleetStatus", nameof(CompanyFleet), fleet.Id.ToString(), null, $"IsActive={fleet.IsActive}");

        TempData["SuccessMessage"] = $"Fleet '{fleet.CompanyName}' status toggled to {(fleet.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(Fleets));
    }
}

// 7. HOME & NOTIFICATIONS
public class HomeController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReviewAndComplaintService _feedbackService;

    public HomeController(IUnitOfWork unitOfWork, IReviewAndComplaintService feedbackService)
    {
        _unitOfWork = unitOfWork;
        _feedbackService = feedbackService;
    }

    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var role = User.FindFirstValue(ClaimTypes.Role);
            return role switch
            {
                "Administrator" => RedirectToAction("Index", "Admin"),
                "ServiceAdvisor" => RedirectToAction("Index", "ServiceAdvisor"),
                "Mechanic" => RedirectToAction("Index", "Mechanic"),
                "InventoryManager" => RedirectToAction("Index", "Inventory"),
                "FinanceManager" => RedirectToAction("Index", "Finance"),
                "FleetManager" => RedirectToAction("Index", "Fleet"),
                _ => RedirectToAction("Index", "Customer")
            };
        }

        var packages = await _unitOfWork.Repository<ServicePackage>().Query().Take(3).ToListAsync();
        var reviews = await _feedbackService.GetApprovedReviewsAsync();

        ViewBag.Packages = packages;
        ViewBag.Reviews = reviews.Take(4).ToList();

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }
}

[Authorize]
public class NotificationsController : Controller
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    [HttpGet]
    public async Task<IActionResult> GetNotifications()
    {
        var list = await _notificationService.GetUserNotificationsAsync(CurrentUserId);
        return Json(list);
    }

    [HttpPost]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        await _notificationService.MarkAsReadAsync(id);
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> MarkAllAsRead()
    {
        await _notificationService.MarkAllAsReadAsync(CurrentUserId);
        return Ok();
    }
}