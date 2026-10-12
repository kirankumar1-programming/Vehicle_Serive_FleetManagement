using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Web.Controllers;

[Authorize(Roles = UserRoles.AdvisorOrAdmin)]
public class ServiceAdvisorController : Controller
{
    private readonly IAppointmentService _appointmentService;
    private readonly IJobCardService _jobCardService;
    private readonly IInspectionService _inspectionService;
    private readonly IEstimateService _estimateService;
    private readonly IInventoryService _inventoryService;
    private readonly IInvoiceService _invoiceService;
    private readonly IServiceCenterService _serviceCenterService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ServiceAdvisorController(
        IAppointmentService appointmentService,
        IJobCardService jobCardService,
        IInspectionService inspectionService,
        IEstimateService estimateService,
        IInventoryService inventoryService,
        IInvoiceService invoiceService,
        IServiceCenterService serviceCenterService,
        UserManager<ApplicationUser> userManager)
    {
        _appointmentService = appointmentService;
        _jobCardService = jobCardService;
        _inspectionService = inspectionService;
        _estimateService = estimateService;
        _inventoryService = inventoryService;
        _invoiceService = invoiceService;
        _serviceCenterService = serviceCenterService;
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
        var bays = await _serviceCenterService.GetBaysByCenterIdAsync(appointment.ServiceCenterId, activeOnly: true);
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
        var bays = await _serviceCenterService.GetAllBaysAsync();

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
