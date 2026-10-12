using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.Interfaces;
using VehicleService.Web.Models;

namespace VehicleService.Web.Controllers;

public class HomeController : Controller
{
    private readonly IServiceCatalogService _serviceCatalogService;
    private readonly IReviewAndComplaintService _feedbackService;

    public HomeController(IServiceCatalogService serviceCatalogService, IReviewAndComplaintService feedbackService)
    {
        _serviceCatalogService = serviceCatalogService;
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

        var packages = (await _serviceCatalogService.GetActivePackagesAsync()).Take(3).ToList();
        var reviews = await _feedbackService.GetApprovedReviewsAsync();

        ViewBag.Packages = packages;
        ViewBag.Reviews = reviews.Take(4).ToList();

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
