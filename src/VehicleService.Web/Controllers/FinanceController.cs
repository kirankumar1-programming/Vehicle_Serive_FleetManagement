using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Enums;

namespace VehicleService.Web.Controllers;

[Authorize(Roles = UserRoles.FinanceOrAdmin)]
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
