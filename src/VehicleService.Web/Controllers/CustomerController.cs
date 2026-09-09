using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Web.Controllers;

[Authorize]
public class CustomerController : Controller
{
    private readonly IVehicleService _vehicleService;
    private readonly IAppointmentService _appointmentService;
    private readonly IJobCardService _jobCardService;
    private readonly IEstimateService _estimateService;
    private readonly IInvoiceService _invoiceService;
    private readonly IPaymentService _paymentService;
    private readonly IWarrantyService _warrantyService;
    private readonly IRoadsideService _roadsideService;
    private readonly IReviewAndComplaintService _feedbackService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<ApplicationUser> _userManager;

    public CustomerController(
        IVehicleService vehicleService,
        IAppointmentService appointmentService,
        IJobCardService jobCardService,
        IEstimateService estimateService,
        IInvoiceService invoiceService,
        IPaymentService paymentService,
        IWarrantyService warrantyService,
        IRoadsideService roadsideService,
        IReviewAndComplaintService feedbackService,
        IUnitOfWork unitOfWork,
        UserManager<ApplicationUser> userManager)
    {
        _vehicleService = vehicleService;
        _appointmentService = appointmentService;
        _jobCardService = jobCardService;
        _estimateService = estimateService;
        _invoiceService = invoiceService;
        _paymentService = paymentService;
        _warrantyService = warrantyService;
        _roadsideService = roadsideService;
        _feedbackService = feedbackService;
        _unitOfWork = unitOfWork;
        _userManager = userManager;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // 1. My Vehicles / Garage Dashboard
    public async Task<IActionResult> Index()
    {
        var vehicles = await _vehicleService.GetCustomerVehiclesAsync(CurrentUserId);
        var appointments = await _appointmentService.GetCustomerAppointmentsAsync(CurrentUserId);
        var activeJobs = appointments.Where(a => a.JobCardId.HasValue && a.Status != AppointmentStatus.Completed && a.Status != AppointmentStatus.Cancelled).ToList();

        ViewBag.ActiveJobs = activeJobs;
        ViewBag.RecentAppointments = appointments.Take(5).ToList();
        return View(vehicles);
    }

    // Add Vehicle
    [HttpGet]
    public IActionResult AddVehicle()
    {
        return View(new CreateVehicleDto { CustomerId = CurrentUserId, IsActive = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddVehicle(CreateVehicleDto dto)
    {
        dto.CustomerId = CurrentUserId;
        try
        {
            await _vehicleService.RegisterVehicleAsync(dto);
            TempData["SuccessMessage"] = $"Vehicle {dto.Make} {dto.Model} ({dto.RegistrationNumber}) registered successfully!";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(dto);
        }
    }

    // Edit Vehicle
    [HttpGet]
    public async Task<IActionResult> EditVehicle(int id)
    {
        var vehicle = await _vehicleService.GetVehicleByIdAsync(id);
        if (vehicle == null || vehicle.CustomerId != CurrentUserId)
        {
            TempData["ErrorMessage"] = "Vehicle not found or access denied.";
            return RedirectToAction(nameof(Index));
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
            CustomerId = vehicle.CustomerId,
            IsActive = vehicle.IsActive
        };

        ViewBag.VehicleId = id;
        ViewBag.RegistrationNumber = vehicle.RegistrationNumber;
        ViewBag.IsActive = vehicle.IsActive;
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditVehicle(int id, CreateVehicleDto dto)
    {
        var vehicle = await _vehicleService.GetVehicleByIdAsync(id);
        if (vehicle == null || vehicle.CustomerId != CurrentUserId)
        {
            TempData["ErrorMessage"] = "Vehicle not found or access denied.";
            return RedirectToAction(nameof(Index));
        }

        dto.CustomerId = CurrentUserId;
        dto.RegistrationNumber = vehicle.RegistrationNumber; // Keep registration unchanged
        dto.VIN = vehicle.VIN;

        try
        {
            await _vehicleService.UpdateVehicleAsync(id, dto);
            TempData["SuccessMessage"] = $"Vehicle {dto.Make} {dto.Model} ({vehicle.RegistrationNumber}) updated successfully!";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.VehicleId = id;
            ViewBag.RegistrationNumber = vehicle.RegistrationNumber;
            ViewBag.IsActive = dto.IsActive;
            return View(dto);
        }
    }

    // Toggle Active/Inactive Status
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleVehicleStatus(int id)
    {
        var vehicle = await _vehicleService.GetVehicleByIdAsync(id);
        if (vehicle == null || vehicle.CustomerId != CurrentUserId)
        {
            TempData["ErrorMessage"] = "Vehicle not found or access denied.";
            return RedirectToAction(nameof(Index));
        }

        await _vehicleService.ToggleVehicleStatusAsync(id);
        var newStatus = !vehicle.IsActive ? "Active" : "Inactive";
        TempData["SuccessMessage"] = $"Vehicle {vehicle.Make} {vehicle.Model} ({vehicle.RegistrationNumber}) is now {newStatus}.";
        return RedirectToAction(nameof(Index));
    }

    // 2. Book Service Appointment Wizard
    [HttpGet]
    public async Task<IActionResult> BookService(int? vehicleId = null)
    {
        var vehicles = await _vehicleService.GetCustomerVehiclesAsync(CurrentUserId);
        var activeVehicles = vehicles.Where(v => v.IsActive).ToList();
        var serviceTypes = await _unitOfWork.Repository<ServiceType>().FindAsync(s => s.IsActive);
        var servicePackages = await _unitOfWork.Repository<ServicePackage>().FindAsync(p => p.IsActive);
        var serviceCenters = await _unitOfWork.Repository<ServiceCenter>().FindAsync(c => c.IsActive);

        ViewBag.Vehicles = activeVehicles;
        ViewBag.ServiceTypes = serviceTypes;
        ViewBag.ServicePackages = servicePackages;
        ViewBag.ServiceCenters = serviceCenters;

        var model = new BookAppointmentDto
        {
            CustomerId = CurrentUserId,
            VehicleId = vehicleId ?? (vehicles.FirstOrDefault()?.Id ?? 0),
            AppointmentDate = DateTime.UtcNow.Date.AddDays(1),
            TimeSlot = "09:00 AM - 11:00 AM"
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BookService(BookAppointmentDto dto)
    {
        dto.CustomerId = CurrentUserId;
        try
        {
            var appointment = await _appointmentService.BookAppointmentAsync(dto);
            TempData["SuccessMessage"] = $"Service appointment {appointment.AppointmentNumber} booked successfully!";
            return RedirectToAction(nameof(Appointments));
        }
        catch (DoubleBookingException dbEx)
        {
            TempData["ErrorMessage"] = dbEx.Message;
            return RedirectToAction(nameof(BookService), new { vehicleId = dto.VehicleId });
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(BookService), new { vehicleId = dto.VehicleId });
        }
    }

    // Appointments List
    public async Task<IActionResult> Appointments()
    {
        var appointments = await _appointmentService.GetCustomerAppointmentsAsync(CurrentUserId);
        return View(appointments);
    }

    // Cancel Appointment (Scenario 5)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelAppointment(int appointmentId, string reason)
    {
        try
        {
            await _appointmentService.CancelAppointmentAsync(appointmentId, reason, CurrentUserId);
            TempData["SuccessMessage"] = "Appointment cancelled successfully. Reserved bays and parts have been released.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Appointments));
    }

    // 3. Real-Time Vehicle Servicing Tracker
    public async Task<IActionResult> TrackJob(int id)
    {
        var jobCard = await _jobCardService.GetJobCardByIdAsync(id);
        if (jobCard == null) return NotFound();

        var inspection = await _unitOfWork.Repository<VehicleInspection>().Query()
            .Include(i => i.InspectionPhotos)
            .FirstOrDefaultAsync(i => i.JobCardId == id);

        var estimate = await _estimateService.GetEstimateByJobCardIdAsync(id);
        var invoice = await _invoiceService.GetInvoiceByJobCardIdAsync(id);

        ViewBag.Inspection = inspection;
        ViewBag.Estimate = estimate;
        ViewBag.Invoice = invoice;

        return View(jobCard);
    }

    // 4. View & Approve Repair Estimate (Scenario 4)
    public async Task<IActionResult> ViewEstimate(int id)
    {
        var estimate = await _estimateService.GetEstimateByIdAsync(id);
        if (estimate == null) return NotFound();
        return View(estimate);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RespondToEstimate(CustomerEstimateResponseDto response)
    {
        try
        {
            var updated = await _estimateService.ProcessCustomerEstimateResponseAsync(response);
            TempData["SuccessMessage"] = $"Estimate status updated: {updated.ApprovalStatus}";
            return RedirectToAction(nameof(TrackJob), new { id = updated.JobCardId });
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(ViewEstimate), new { id = response.EstimateId });
        }
    }

    // 5. Invoices & Online Checkout
    public async Task<IActionResult> Invoices()
    {
        var invoices = await _invoiceService.GetInvoicesAsync(customerId: CurrentUserId);
        return View(invoices);
    }

    public async Task<IActionResult> ViewInvoice(int id)
    {
        var invoice = await _invoiceService.GetInvoiceByIdAsync(id);
        if (invoice == null) return NotFound();
        return View(invoice);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PayInvoice(PaymentRequestDto dto)
    {
        try
        {
            var result = await _paymentService.ProcessPaymentAsync(dto);
            if (result.Success)
            {
                TempData["SuccessMessage"] = $"Payment of ₹{result.Amount:N2} completed successfully! Transaction ID: {result.TransactionReference}";
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(ViewInvoice), new { id = dto.InvoiceId });
    }

    // 6. Service History
    public async Task<IActionResult> ServiceHistory(int? vehicleId = null)
    {
        var vehicles = await _vehicleService.GetCustomerVehiclesAsync(CurrentUserId);
        ViewBag.Vehicles = vehicles;
        ViewBag.SelectedVehicleId = vehicleId;

        var allJobCards = await _jobCardService.GetJobCardsAsync();
        var customerJobs = allJobCards.Where(j => j.CustomerId == CurrentUserId && j.Status == AppointmentStatus.Completed);

        if (vehicleId.HasValue)
        {
            customerJobs = customerJobs.Where(j => j.VehicleId == vehicleId.Value);
        }

        return View(customerJobs.ToList());
    }

    // 7. Roadside Assistance
    public async Task<IActionResult> Roadside()
    {
        var requests = await _roadsideService.GetCustomerRequestsAsync(CurrentUserId);
        var vehicles = await _vehicleService.GetCustomerVehiclesAsync(CurrentUserId);
        ViewBag.Vehicles = vehicles;
        return View(requests);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestRoadside(CreateRoadsideRequestDto dto)
    {
        dto.CustomerId = CurrentUserId;
        try
        {
            var result = await _roadsideService.CreateRequestAsync(dto);
            TempData["SuccessMessage"] = $"Emergency Roadside Assistance request {result.RequestNumber} submitted! Technician is being assigned.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Roadside));
    }

    // 8. Warranties & Claims
    public async Task<IActionResult> Warranties()
    {
        var vehicles = await _vehicleService.GetCustomerVehiclesAsync(CurrentUserId);
        var allWarranties = new List<WarrantyDto>();

        foreach (var v in vehicles)
        {
            var w = await _warrantyService.GetVehicleWarrantiesAsync(v.Id);
            allWarranties.AddRange(w);
        }

        ViewBag.Vehicles = vehicles;
        return View(allWarranties);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitWarrantyClaim(int warrantyId, string issueDesc, string component, decimal amount, int? jobCardId = null)
    {
        try
        {
            var claim = await _warrantyService.SubmitClaimAsync(warrantyId, jobCardId, CurrentUserId, issueDesc, component, amount);
            TempData["SuccessMessage"] = $"Warranty claim {claim.ClaimNumber} submitted for review.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Warranties));
    }

    // 9. Feedback & Reviews
    [HttpGet]
    public async Task<IActionResult> ReviewService(int jobCardId)
    {
        var jobCard = await _jobCardService.GetJobCardByIdAsync(jobCardId);
        if (jobCard == null) return NotFound();
        return View(jobCard);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReview(int jobCardId, int overallRating, int qualityScore, int staffRating, int cleanRating, string comments)
    {
        await _feedbackService.SubmitReviewAsync(jobCardId, CurrentUserId, overallRating, qualityScore, staffRating, cleanRating, comments);
        TempData["SuccessMessage"] = "Thank you for your valuable feedback!";
        return RedirectToAction(nameof(ServiceHistory));
    }

    // 10. Complaints
    public async Task<IActionResult> Complaints()
    {
        var complaints = await _feedbackService.GetComplaintsAsync(customerId: CurrentUserId);
        var vehicles = await _vehicleService.GetCustomerVehiclesAsync(CurrentUserId);
        ViewBag.Vehicles = vehicles;
        return View(complaints);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RaiseComplaint(string subject, string description, int? vehicleId, int? jobCardId)
    {
        var complaint = await _feedbackService.RaiseComplaintAsync(CurrentUserId, subject, description, vehicleId, jobCardId);
        TempData["SuccessMessage"] = $"Support ticket {complaint.TicketNumber} raised. Our team will review shortly.";
        return RedirectToAction(nameof(Complaints));
    }
}