using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;

namespace VehicleService.Web.Controllers;

[Authorize(Roles = UserRoles.Administrator)]
public class AdminController : Controller
{
    private readonly IReportService _reportService;
    private readonly IAuditService _auditService;
    private readonly IReviewAndComplaintService _feedbackService;
    private readonly IServiceCenterService _serviceCenterService;
    private readonly IServiceCatalogService _serviceCatalogService;
    private readonly ICouponService _couponService;
    private readonly IFleetService _fleetService;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(
        IReportService reportService,
        IAuditService auditService,
        IReviewAndComplaintService feedbackService,
        IServiceCenterService serviceCenterService,
        IServiceCatalogService serviceCatalogService,
        ICouponService couponService,
        IFleetService fleetService,
        UserManager<ApplicationUser> userManager)
    {
        _reportService = reportService;
        _auditService = auditService;
        _feedbackService = feedbackService;
        _serviceCenterService = serviceCenterService;
        _serviceCatalogService = serviceCatalogService;
        _couponService = couponService;
        _fleetService = fleetService;
        _userManager = userManager;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private string CurrentUserName => User.Identity?.Name ?? "Administrator";

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

        ViewBag.ServiceCenters = await _serviceCenterService.GetAllCentersAsync();
        ViewBag.CompanyFleets = await _fleetService.GetAllFleetsAsync();

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
            ViewBag.ServiceCenters = await _serviceCenterService.GetAllCentersAsync();
            ViewBag.CompanyFleets = await _fleetService.GetAllFleetsAsync();
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
                ViewBag.ServiceCenters = await _serviceCenterService.GetAllCentersAsync();
                ViewBag.CompanyFleets = await _fleetService.GetAllFleetsAsync();
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
            ViewBag.ServiceCenters = await _serviceCenterService.GetAllCentersAsync();
            ViewBag.CompanyFleets = await _fleetService.GetAllFleetsAsync();
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

    // -------------------------------------------------------------
    // Service Centers & Service Bays Management
    // -------------------------------------------------------------

    public async Task<IActionResult> ServiceCenters()
    {
        var centers = await _serviceCenterService.GetAllCentersAsync();
        return View(centers);
    }

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

        await _serviceCenterService.CreateCenterAsync(center);

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
                await _serviceCenterService.CreateBayAsync(bay);
            }
        }

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "AddServiceCenter", nameof(ServiceCenter), center.Id.ToString(), null, $"Name={center.Name}, Code={center.Code}, Capacity={center.MaxDailyCapacity}");

        TempData["SuccessMessage"] = $"Service Center '{center.Name}' ({center.Code}) created successfully!";
        return RedirectToAction(nameof(ServiceCenters));
    }

    [HttpGet]
    public async Task<IActionResult> EditServiceCenter(int id)
    {
        var center = await _serviceCenterService.GetCenterByIdAsync(id);
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

        var center = await _serviceCenterService.GetCenterByIdAsync(id);
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
        center.Code = dto.Code.Trim().ToUpper();
        center.Address = dto.Address.Trim();
        center.City = dto.City.Trim();
        center.State = dto.State.Trim();
        center.Phone = dto.Phone?.Trim() ?? string.Empty;
        center.Email = dto.Email?.Trim() ?? string.Empty;
        center.OperatingHours = dto.OperatingHours.Trim();
        center.MaxDailyCapacity = dto.MaxDailyCapacity;
        center.IsActive = dto.IsActive;

        await _serviceCenterService.UpdateCenterAsync(center);

        var newValues = $"Name={center.Name}, Code={center.Code}, Capacity={center.MaxDailyCapacity}, Active={center.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateServiceCenter", nameof(ServiceCenter), center.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Service Center '{center.Name}' updated successfully!";
        return RedirectToAction(nameof(ServiceCenters));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleServiceCenterStatus(int id)
    {
        var center = await _serviceCenterService.GetCenterByIdAsync(id);
        if (center == null)
        {
            TempData["ErrorMessage"] = "Service Center not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        await _serviceCenterService.ToggleCenterStatusAsync(id);
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleCenterStatus", nameof(ServiceCenter), center.Id.ToString(), null, $"IsActive={!center.IsActive}");

        TempData["SuccessMessage"] = $"Service Center '{center.Name}' status toggled.";
        return RedirectToAction(nameof(ServiceCenters));
    }

    [HttpGet]
    public async Task<IActionResult> AddServiceBay(int centerId)
    {
        var center = await _serviceCenterService.GetCenterByIdAsync(centerId);
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
        var center = await _serviceCenterService.GetCenterByIdAsync(centerId);
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

        await _serviceCenterService.CreateBayAsync(bay);

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "AddServiceBay", nameof(ServiceBay), bay.Id.ToString(), null, $"BayNumber={bay.BayNumber}, BayName={bay.BayName}, ServiceCenterId={centerId}");

        TempData["SuccessMessage"] = $"Service Bay '{bay.BayName}' added successfully to '{center.Name}'!";
        return RedirectToAction(nameof(ServiceCenters));
    }

    [HttpGet]
    public async Task<IActionResult> EditServiceBay(int id)
    {
        var bay = await _serviceCenterService.GetBayByIdAsync(id);
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

        var bay = await _serviceCenterService.GetBayByIdAsync(id);
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

        await _serviceCenterService.UpdateBayAsync(bay);

        var newValues = $"BayNumber={bay.BayNumber}, BayName={bay.BayName}, BayType={bay.BayType}, Status={bay.Status}, Active={bay.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateServiceBay", nameof(ServiceBay), bay.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Service Bay '{bay.BayName}' ({bay.BayNumber}) updated successfully!";
        return RedirectToAction(nameof(ServiceCenters));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBayStatus(int id, BayStatus status)
    {
        var bay = await _serviceCenterService.GetBayByIdAsync(id);
        if (bay == null)
        {
            TempData["ErrorMessage"] = "Service Bay not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        bay.Status = status;
        await _serviceCenterService.UpdateBayAsync(bay);

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateBayStatus", nameof(ServiceBay), bay.Id.ToString(), null, $"Status={bay.Status}");

        TempData["SuccessMessage"] = $"Bay '{bay.BayName}' status changed to {status}.";
        return RedirectToAction(nameof(ServiceCenters));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBayStatus(int id)
    {
        var bay = await _serviceCenterService.GetBayByIdAsync(id);
        if (bay == null)
        {
            TempData["ErrorMessage"] = "Service Bay not found.";
            return RedirectToAction(nameof(ServiceCenters));
        }

        await _serviceCenterService.ToggleBayStatusAsync(id);
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleBayStatus", nameof(ServiceBay), bay.Id.ToString(), null, $"IsActive={!bay.IsActive}");

        TempData["SuccessMessage"] = $"Bay '{bay.BayName}' status toggled.";
        return RedirectToAction(nameof(ServiceCenters));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteServiceBay(int id)
    {
        try
        {
            await _serviceCenterService.DeleteBayAsync(id);
            TempData["SuccessMessage"] = "Service Bay deleted successfully.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(ServiceCenters));
    }

    // -------------------------------------------------------------
    // Service Catalog (Types & Packages) Management
    // -------------------------------------------------------------

    public async Task<IActionResult> ServiceCatalog()
    {
        var types = await _serviceCatalogService.GetAllServiceTypesAsync();
        var packages = await _serviceCatalogService.GetAllPackagesAsync();

        ViewBag.Packages = packages;
        return View(types);
    }

    [HttpGet]
    public async Task<IActionResult> EditServiceType(int id)
    {
        var service = await _serviceCatalogService.GetServiceTypeByIdAsync(id);
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

        var service = await _serviceCatalogService.GetServiceTypeByIdAsync(id);
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

        await _serviceCatalogService.UpdateServiceTypeAsync(service);

        var newValues = $"Name={service.Name}, BasePrice={service.BasePrice}, LaborCharges={service.LaborCharges}, Duration={service.EstimatedDurationHours}, Taxes={service.ApplicableTaxPercent}%, Warranty={service.WarrantyPeriodDays}d, Parts={service.RecommendedPartsSummary}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateServiceType", nameof(ServiceType), service.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Service '{service.Name}' ({service.Code}) updated successfully!";
        return RedirectToAction(nameof(ServiceCatalog));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleServiceTypeStatus(int id)
    {
        var service = await _serviceCatalogService.GetServiceTypeByIdAsync(id);
        if (service == null)
        {
            TempData["ErrorMessage"] = "Service not found in catalog.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        service.IsActive = !service.IsActive;
        await _serviceCatalogService.UpdateServiceTypeAsync(service);

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleServiceTypeStatus", nameof(ServiceType), service.Id.ToString(), null, $"IsActive={service.IsActive}");

        TempData["SuccessMessage"] = $"Service '{service.Name}' status toggled to {(service.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(ServiceCatalog));
    }

    [HttpGet]
    public async Task<IActionResult> AddServicePackage()
    {
        ViewBag.ServiceTypes = (await _serviceCatalogService.GetActiveServiceTypesAsync())
            .OrderBy(s => s.Category).ThenBy(s => s.Name)
            .ToList();

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
        var allPackages = await _serviceCatalogService.GetAllPackagesAsync();
        var existing = allPackages.FirstOrDefault(p => p.Name.Trim().Equals(dto.Name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            ModelState.AddModelError(nameof(dto.Name), $"A service package with the name '{dto.Name}' already exists.");
        }

        if (dto.SelectedServiceTypeIds == null || !dto.SelectedServiceTypeIds.Any())
        {
            ModelState.AddModelError(nameof(dto.SelectedServiceTypeIds), "Please select at least one service to bundle into this package.");
        }

        var availableServices = (await _serviceCatalogService.GetActiveServiceTypesAsync())
            .OrderBy(s => s.Category).ThenBy(s => s.Name)
            .ToList();

        ViewBag.ServiceTypes = availableServices;

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

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
            IsActive = dto.IsActive,
            PackageItems = dto.SelectedServiceTypeIds!.Select(sId => new ServicePackageItem
            {
                ServiceTypeId = sId
            }).ToList()
        };

        await _serviceCatalogService.CreatePackageAsync(package);

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "AddServicePackage", nameof(ServicePackage), package.Id.ToString(), null,
            $"Name={package.Name}, Price={package.PackagePrice}, OriginalPrice={package.OriginalTotalPrice}, Savings={package.SavingsPercent}%, ItemsCount={dto.SelectedServiceTypeIds?.Count ?? 0}");

        TempData["SuccessMessage"] = $"Bundled Service Package '{package.Name}' created successfully!";
        return RedirectToAction(nameof(ServiceCatalog));
    }

    [HttpGet]
    public async Task<IActionResult> EditServicePackage(int id)
    {
        var package = await _serviceCatalogService.GetPackageByIdAsync(id);
        if (package == null)
        {
            TempData["ErrorMessage"] = "Service package not found.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        ViewBag.ServiceTypes = (await _serviceCatalogService.GetAllServiceTypesAsync())
            .OrderBy(s => s.Category).ThenBy(s => s.Name)
            .ToList();

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

        var package = await _serviceCatalogService.GetPackageByIdAsync(id);
        if (package == null)
        {
            TempData["ErrorMessage"] = "Service package not found.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        var allPackages = await _serviceCatalogService.GetAllPackagesAsync();
        var existing = allPackages.FirstOrDefault(p => p.Name.Trim().Equals(dto.Name.Trim(), StringComparison.OrdinalIgnoreCase) && p.Id != id);

        if (existing != null)
        {
            ModelState.AddModelError(nameof(dto.Name), $"Another service package with the name '{dto.Name}' already exists.");
        }

        if (dto.SelectedServiceTypeIds == null || !dto.SelectedServiceTypeIds.Any())
        {
            ModelState.AddModelError(nameof(dto.SelectedServiceTypeIds), "Please select at least one service to bundle into this package.");
        }

        var allServices = (await _serviceCatalogService.GetAllServiceTypesAsync())
            .OrderBy(s => s.Category).ThenBy(s => s.Name)
            .ToList();

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

        // Update items
        var newItemIds = dto.SelectedServiceTypeIds ?? new List<int>();
        package.PackageItems = newItemIds.Select(sId => new ServicePackageItem
        {
            ServicePackageId = package.Id,
            ServiceTypeId = sId
        }).ToList();

        await _serviceCatalogService.UpdatePackageAsync(package);

        var newValues = $"Name={package.Name}, Price={package.PackagePrice}, OriginalPrice={package.OriginalTotalPrice}, Savings={package.SavingsPercent}%, ItemsCount={newItemIds.Count}, Active={package.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateServicePackage", nameof(ServicePackage), package.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Bundled Service Package '{package.Name}' updated successfully!";
        return RedirectToAction(nameof(ServiceCatalog));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleServicePackageStatus(int id)
    {
        var package = await _serviceCatalogService.GetPackageByIdAsync(id);
        if (package == null)
        {
            TempData["ErrorMessage"] = "Service package not found.";
            return RedirectToAction(nameof(ServiceCatalog));
        }

        await _serviceCatalogService.TogglePackageStatusAsync(id);
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleServicePackageStatus", nameof(ServicePackage), package.Id.ToString(), null, $"IsActive={!package.IsActive}");

        TempData["SuccessMessage"] = $"Package '{package.Name}' status toggled.";
        return RedirectToAction(nameof(ServiceCatalog));
    }

    // -------------------------------------------------------------
    // Promotional Coupons Management
    // -------------------------------------------------------------

    public async Task<IActionResult> Coupons()
    {
        var coupons = await _couponService.GetAllCouponsAsync();
        return View(coupons);
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
        var existing = await _couponService.GetCouponByCodeAsync(code);

        if (existing != null)
        {
            ModelState.AddModelError(nameof(dto.Code), $"A coupon with code '{code}' already exists.");
        }

        if (dto.Type == DiscountType.Percentage && dto.Value > 100)
        {
            ModelState.AddModelError(nameof(dto.Value), "Percentage discount cannot exceed 100%.");
        }

        if (dto.ExpiryDate <= DateTime.UtcNow)
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

        await _couponService.CreateCouponAsync(coupon);

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "AddCoupon", nameof(Coupon), coupon.Id.ToString(), null,
            $"Code={coupon.Code}, Title={coupon.Title}, Type={coupon.Type}, Value={coupon.Value}, MinBill={coupon.MinimumBillAmount}, MaxCap={coupon.MaxDiscountLimit}, UsageLimit={coupon.UsageLimit}");

        TempData["SuccessMessage"] = $"Promotional coupon '{coupon.Code}' created successfully!";
        return RedirectToAction(nameof(Coupons));
    }

    [HttpGet]
    public async Task<IActionResult> EditCoupon(int id)
    {
        var coupon = await _couponService.GetCouponByIdAsync(id);
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

        var coupon = await _couponService.GetCouponByIdAsync(id);
        if (coupon == null)
        {
            TempData["ErrorMessage"] = "Coupon not found.";
            return RedirectToAction(nameof(Coupons));
        }

        var code = dto.Code.Trim().ToUpper();
        var existing = await _couponService.GetCouponByCodeAsync(code);

        if (existing != null && existing.Id != id)
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

        await _couponService.UpdateCouponAsync(coupon);

        var newValues = $"Code={coupon.Code}, Title={coupon.Title}, Type={coupon.Type}, Value={coupon.Value}, MinBill={coupon.MinimumBillAmount}, MaxCap={coupon.MaxDiscountLimit}, UsageLimit={coupon.UsageLimit}, Expiry={coupon.ExpiryDate:yyyy-MM-dd}, Active={coupon.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateCoupon", nameof(Coupon), coupon.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Coupon '{coupon.Code}' details updated successfully!";
        return RedirectToAction(nameof(Coupons));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCouponStatus(int id)
    {
        var coupon = await _couponService.GetCouponByIdAsync(id);
        if (coupon == null)
        {
            TempData["ErrorMessage"] = "Coupon not found.";
            return RedirectToAction(nameof(Coupons));
        }

        await _couponService.ToggleCouponStatusAsync(id);
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleCouponStatus", nameof(Coupon), coupon.Id.ToString(), null, $"IsActive={!coupon.IsActive}");

        TempData["SuccessMessage"] = $"Coupon '{coupon.Code}' status toggled.";
        return RedirectToAction(nameof(Coupons));
    }

    // -------------------------------------------------------------
    // Audit, Feedback & Complaints
    // -------------------------------------------------------------

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

    public async Task<IActionResult> Complaints()
    {
        var complaints = await _feedbackService.GetComplaintsAsync();
        return View(complaints);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveComplaint(int complaintId, string resolutionNotes)
    {
        await _feedbackService.ResolveComplaintAsync(complaintId, resolutionNotes, CurrentUserId);
        TempData["SuccessMessage"] = "Complaint marked as resolved!";
        return RedirectToAction(nameof(Complaints));
    }

    // -------------------------------------------------------------
    // Corporate Fleets (Admin View)
    // -------------------------------------------------------------

    public async Task<IActionResult> Fleets()
    {
        var fleets = await _fleetService.GetAllFleetsAsync();
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
        var allFleets = await _fleetService.GetAllFleetsAsync();
        var existing = allFleets.FirstOrDefault(f => f.CompanyName.Equals(name, StringComparison.OrdinalIgnoreCase));

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

        await _fleetService.RegisterFleetAsync(fleet);

        var newValues = $"Name={fleet.CompanyName}, RegNo={fleet.RegistrationNumber}, TaxId={fleet.TaxId}, ContactPerson={fleet.ContactPerson}, Email={fleet.ContactEmail}, Discount={fleet.CorporateDiscountRate}%, Budget={fleet.MonthlyBudgetLimit}, Active={fleet.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "AddCompanyFleet", nameof(CompanyFleet), fleet.Id.ToString(), null, newValues);

        TempData["SuccessMessage"] = $"Corporate Fleet '{fleet.CompanyName}' created successfully!";
        return RedirectToAction(nameof(Fleets));
    }

    [HttpGet]
    public async Task<IActionResult> EditFleet(int id)
    {
        var fleet = await _fleetService.GetFleetByIdAsync(id);
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

        var fleet = await _fleetService.GetFleetByIdAsync(id);
        if (fleet == null)
        {
            TempData["ErrorMessage"] = "Corporate fleet not found.";
            return RedirectToAction(nameof(Fleets));
        }

        var name = dto.CompanyName.Trim();
        var allFleets = await _fleetService.GetAllFleetsAsync();
        var existing = allFleets.FirstOrDefault(f => f.CompanyName.Equals(name, StringComparison.OrdinalIgnoreCase) && f.Id != id);

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

        await _fleetService.UpdateFleetAsync(fleet);

        var newValues = $"Name={fleet.CompanyName}, RegNo={fleet.RegistrationNumber}, TaxId={fleet.TaxId}, ContactPerson={fleet.ContactPerson}, Email={fleet.ContactEmail}, Discount={fleet.CorporateDiscountRate}%, Budget={fleet.MonthlyBudgetLimit}, Active={fleet.IsActive}";
        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "UpdateCompanyFleet", nameof(CompanyFleet), fleet.Id.ToString(), oldValues, newValues);

        TempData["SuccessMessage"] = $"Corporate Fleet '{fleet.CompanyName}' updated successfully!";
        return RedirectToAction(nameof(Fleets));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleFleetStatus(int id)
    {
        var fleet = await _fleetService.GetFleetByIdAsync(id);
        if (fleet == null)
        {
            TempData["ErrorMessage"] = "Corporate fleet not found.";
            return RedirectToAction(nameof(Fleets));
        }

        fleet.IsActive = !fleet.IsActive;
        await _fleetService.UpdateFleetAsync(fleet);

        await _auditService.LogAsync(CurrentUserId, CurrentUserName, "ToggleFleetStatus", nameof(CompanyFleet), fleet.Id.ToString(), null, $"IsActive={fleet.IsActive}");

        TempData["SuccessMessage"] = $"Fleet '{fleet.CompanyName}' status toggled to {(fleet.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(Fleets));
    }
}
