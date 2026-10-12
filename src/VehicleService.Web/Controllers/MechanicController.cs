using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Web.Controllers;

[Authorize(Roles = $"{UserRoles.Mechanic},{UserRoles.Administrator}")]
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
