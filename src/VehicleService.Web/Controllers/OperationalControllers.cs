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
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<ApplicationUser> _userManager;

    public ServiceAdvisorController(
        IAppointmentService appointmentService,
        IJobCardService jobCardService,
        IInspectionService inspectionService,
        IEstimateService estimateService,
        IInventoryService inventoryService,
        IUnitOfWork unitOfWork,
        UserManager<ApplicationUser> userManager)
    {
        _appointmentService = appointmentService;
        _jobCardService = jobCardService;
        _inspectionService = inspectionService;
        _estimateService = estimateService;
        _inventoryService = inventoryService;
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

        ViewBag.Appointment = appointment;
        ViewBag.Mechanics = mechanics;
        ViewBag.Bays = bays;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckInVehicle(int appointmentId, int? bayId, string? mechanicId, int mileage, string fuelLevel, string engineCond, string brakeCond, string tyreCond, string batteryCond, string fluidLevels, string summary, string? damages)
    {
        try
        {
            var jobCard = await _jobCardService.CreateJobCardFromAppointmentAsync(appointmentId, CurrentUserId, bayId);

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
        var mechanics = await _userManager.GetUsersInRoleAsync(UserRoleType.Mechanic.ToString());
        var parts = await _inventoryService.GetAllPartsAsync();

        ViewBag.Inspection = inspection;
        ViewBag.Estimate = estimate;
        ViewBag.Mechanics = mechanics;
        ViewBag.Parts = parts;

        return View(jobCard);
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
}

// 3. INVENTORY MANAGER PORTAL
[Authorize]
public class InventoryController : Controller
{
    private readonly IInventoryService _inventoryService;
    private readonly IUnitOfWork _unitOfWork;

    public InventoryController(IInventoryService inventoryService, IUnitOfWork unitOfWork)
    {
        _inventoryService = inventoryService;
        _unitOfWork = unitOfWork;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

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

        ViewBag.RevenueReport = report;
        ViewBag.SelectedStatus = status;

        return View(invoices);
    }

    public async Task<IActionResult> InvoiceDetails(int id)
    {
        var inv = await _invoiceService.GetInvoiceByIdAsync(id);
        if (inv == null) return NotFound();
        return View(inv);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
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
            return RedirectToAction(nameof(Index));
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
        var users = await _userManager.Users.ToListAsync();
        return View(users);
    }

    public async Task<IActionResult> ServiceCenters()
    {
        var centers = await _unitOfWork.Repository<ServiceCenter>().Query()
            .Include(c => c.ServiceBays)
            .ToListAsync();
        return View(centers);
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

    public async Task<IActionResult> Coupons()
    {
        var coupons = await _unitOfWork.Repository<Coupon>().GetAllAsync();
        return View(coupons);
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