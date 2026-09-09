using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;

namespace VehicleService.Web.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginRequest model, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            ModelState.AddModelError("", "Invalid login credentials.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, false);
        if (result.Succeeded)
        {
            return RedirectToRoleDashboard(user.RoleType, returnUrl);
        }

        ModelState.AddModelError("", "Invalid login credentials.");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterRequest model)
    {
        if (!ModelState.IsValid) return View(model);

        var existing = await _userManager.FindByEmailAsync(model.Email);
        if (existing != null)
        {
            ModelState.AddModelError("", "Email is already registered.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            PhoneNumber = model.PhoneNumber,
            RoleType = model.RoleType,
            Address = model.Address,
            City = model.City,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, model.RoleType.ToString());
            await _signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToRoleDashboard(user.RoleType);
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError("", error.Description);
        }

        return View(model);
    }

    // Quick Demo Role Switcher
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchRole(string role)
    {
        string email = role.ToLower() switch
        {
            "administrator" or "admin" => "admin@vehicleservice.com",
            "serviceadvisor" or "advisor" => "advisor@vehicleservice.com",
            "mechanic" => "mechanic@vehicleservice.com",
            "inventorymanager" or "inventory" => "inventory@vehicleservice.com",
            "financemanager" or "finance" => "finance@vehicleservice.com",
            "fleetmanager" or "fleet" => "fleet@vehicleservice.com",
            _ => "customer@vehicleservice.com"
        };

        var user = await _userManager.FindByEmailAsync(email);
        if (user != null)
        {
            await _signInManager.SignOutAsync();
            await _signInManager.SignInAsync(user, isPersistent: true);
            return RedirectToRoleDashboard(user.RoleType);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    private IActionResult RedirectToRoleDashboard(UserRoleType role, string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);

        return role switch
        {
            UserRoleType.Administrator => RedirectToAction("Index", "Admin"),
            UserRoleType.ServiceAdvisor => RedirectToAction("Index", "ServiceAdvisor"),
            UserRoleType.Mechanic => RedirectToAction("Index", "Mechanic"),
            UserRoleType.InventoryManager => RedirectToAction("Index", "Inventory"),
            UserRoleType.FinanceManager => RedirectToAction("Index", "Finance"),
            UserRoleType.FleetManager => RedirectToAction("Index", "Fleet"),
            _ => RedirectToAction("Index", "Customer")
        };
    }
}