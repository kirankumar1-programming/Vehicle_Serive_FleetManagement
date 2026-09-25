using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;

namespace VehicleService.Application.Services;

public class DatabaseRagService : IDatabaseRagService
{
    private readonly IUnitOfWork _unitOfWork;

    public DatabaseRagService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<DatabaseRagResult> QueryDatabaseContextAsync(string query, string userId, string userRole)
    {
        var result = new DatabaseRagResult();
        string lower = query.ToLower();

        // 1. Check for specific identifiers (Registration Plate, Job Card #, Invoice #, Part SKU)
        await CheckSpecificIdentifiersAsync(lower, userId, userRole, result);

        // 2. Role-based contextual querying
        if (userRole == "Customer")
        {
            await QueryCustomerDataAsync(lower, userId, result);
        }
        else if (userRole == "FleetManager")
        {
            await QueryFleetDataAsync(lower, userId, result);
        }
        else if (userRole == "Mechanic")
        {
            await QueryMechanicDataAsync(lower, userId, result);
        }
        else if (userRole == "ServiceAdvisor" || userRole == "Admin" || userRole == "Administrator" || userRole == "InventoryManager" || userRole == "FinanceManager")
        {
            await QueryStaffAndInventoryDataAsync(lower, result);
        }
        else
        {
            // Guest or unrecognized role: if userId present try customer, else catalog
            if (!string.IsNullOrEmpty(userId))
            {
                await QueryCustomerDataAsync(lower, userId, result);
            }
        }

        // 2.5 Fallback for mechanic / assigned jobs queries across any role or session
        if ((lower.Contains("assigned") || lower.Contains("my job") || lower.Contains("workstation")) && result.ContextSummaries.Count == 0)
        {
            await QueryMechanicDataAsync(lower, userId, result);
        }

        // 3. General Public / Catalog Queries (Packages, Service Centers, Coupons)
        await QueryCatalogDataAsync(lower, result);

        return result;
    }

    private async Task CheckSpecificIdentifiersAsync(string lower, string userId, string userRole, DatabaseRagResult result)
    {
        // Check for Job Card mentions e.g. "JC-2026-00125" or "job 1"
        var jobMatch = Regex.Match(lower, @"(jc-\d{4}-\d{5}|jobcard\s*#?\s*(\d+)|job\s*#?\s*(\d+))");
        if (jobMatch.Success)
        {
            var jobCards = await _unitOfWork.Repository<ServiceJobCard>().Query()
                .Include(j => j.Vehicle)
                .Include(j => j.Mechanic)
                .Where(j => j.JobCardNumber.ToLower().Contains(jobMatch.Value) || 
                            j.Id.ToString() == jobMatch.Groups[2].Value || 
                            j.Id.ToString() == jobMatch.Groups[3].Value)
                .Take(2)
                .ToListAsync();

            foreach (var j in jobCards)
            {
                result.ContextSummaries.Add($"Job Card {j.JobCardNumber} for vehicle {j.Vehicle?.Make} {j.Vehicle?.Model} ({j.Vehicle?.RegistrationNumber}): Status is '{j.Status}', Assigned Mechanic: {j.Mechanic?.FullName ?? "Unassigned"}, Bay: #{j.ServiceBayId}.");
                result.EntityCards.Add(new ChatEntityCardDto
                {
                    Type = "JobCard",
                    Title = $"Job Card {j.JobCardNumber}",
                    Subtitle = $"{j.Vehicle?.Make} {j.Vehicle?.Model} ({j.Vehicle?.RegistrationNumber})",
                    BadgeText = j.Status.ToString(),
                    BadgeColor = GetStatusColor(j.Status.ToString()),
                    KeyValues = new()
                    {
                        { "Mechanic", j.Mechanic?.FullName ?? "Pending Assignment" },
                        { "Odometer In", $"{j.OdometerIn:N0} km" },
                        { "Advisor Notes", string.IsNullOrEmpty(j.AdvisorObservations) ? "Standard Service" : j.AdvisorObservations }
                    },
                    ActionUrl = $"/Customer/TrackJob/{j.Id}",
                    ActionText = "Track Live Servicing"
                });
                result.DbCitations.Add(new ChatSourceCitationDto
                {
                    Title = $"Job Card Record: {j.JobCardNumber}",
                    Section = "Live Workshop Operations",
                    Snippet = $"Vehicle: {j.Vehicle?.RegistrationNumber}, Status: {j.Status}, Mechanic: {j.Mechanic?.FullName}",
                    SourceType = "Database",
                    Score = 1.0,
                    LinkUrl = $"/Customer/TrackJob/{j.Id}"
                });
            }
        }

        // Check for Invoice mentions e.g. "INV-2026-0098" or "invoice"
        var invMatch = Regex.Match(lower, @"(inv-\d{4}-\d{4}|invoice\s*#?\s*(\d+))");
        if (invMatch.Success)
        {
            var invoices = await _unitOfWork.Repository<Invoice>().Query()
                .Include(i => i.Vehicle)
                .Where(i => i.InvoiceNumber.ToLower().Contains(invMatch.Value) || i.Id.ToString() == invMatch.Groups[2].Value)
                .Take(2)
                .ToListAsync();

            foreach (var inv in invoices)
            {
                result.ContextSummaries.Add($"Invoice {inv.InvoiceNumber}: Grand Total = ${inv.GrandTotal:F2}, Status = '{inv.Status}', Issued On = {inv.IssueDate:d}.");
                result.EntityCards.Add(new ChatEntityCardDto
                {
                    Type = "Invoice",
                    Title = $"Invoice {inv.InvoiceNumber}",
                    Subtitle = $"Vehicle: {inv.Vehicle?.RegistrationNumber ?? "N/A"}",
                    BadgeText = inv.Status.ToString(),
                    BadgeColor = inv.Status == PaymentStatus.Successful ? "success" : "danger",
                    KeyValues = new()
                    {
                        { "Grand Total", $"${inv.GrandTotal:F2}" },
                        { "Tax / GST", $"${inv.TaxAmount:F2}" },
                        { "Date", inv.IssueDate.ToString("dd MMM yyyy") }
                    },
                    ActionUrl = $"/Customer/ViewInvoice/{inv.Id}",
                    ActionText = "View Tax Invoice"
                });
            }
        }

        // Check for Warranty Claim mentions e.g. "CLM-20260924-8DA77" or "claim 1"
        var claimMatch = Regex.Match(lower, @"(clm-[a-z0-9\-]+|claim\s*#?\s*(\d+))");
        if (claimMatch.Success)
        {
            var claims = await _unitOfWork.Repository<WarrantyClaim>().Query()
                .Include(c => c.Warranty).ThenInclude(w => w!.Vehicle)
                .Where(c => c.ClaimNumber.ToLower().Contains(claimMatch.Value) || 
                            c.Id.ToString() == claimMatch.Groups[2].Value)
                .Take(2)
                .ToListAsync();

            foreach (var c in claims)
            {
                result.ContextSummaries.Add($"Warranty Claim {c.ClaimNumber} for {c.ComponentName} ({c.Warranty?.Vehicle?.Make} {c.Warranty?.Vehicle?.Model} {c.Warranty?.Vehicle?.RegistrationNumber}): Amount Claimed = ₹{c.AmountClaimed:N0}, Status = '{c.Status}', Submitted On = {c.CreatedAt:dd MMM yyyy}.");
                result.EntityCards.Add(new ChatEntityCardDto
                {
                    Type = "WarrantyClaim",
                    Title = $"Claim {c.ClaimNumber}",
                    Subtitle = $"{c.ComponentName} - {c.Warranty?.Vehicle?.RegistrationNumber ?? "Vehicle"}",
                    BadgeText = c.Status.ToString(),
                    BadgeColor = c.Status == WarrantyClaimStatus.Approved ? "success" : (c.Status == WarrantyClaimStatus.Submitted ? "warning" : (c.Status == WarrantyClaimStatus.Rejected ? "danger" : "info")),
                    KeyValues = new()
                    {
                        { "Component", c.ComponentName },
                        { "Amount Claimed", $"₹{c.AmountClaimed:N0}" },
                        { "Amount Approved", c.AmountApproved > 0 ? $"₹{c.AmountApproved:N0}" : "Pending Review" },
                        { "Warranty", c.Warranty?.WarrantyNumber ?? "OEM Warranty" },
                        { "Date", c.CreatedAt.ToString("dd MMM yyyy") }
                    },
                    ActionUrl = "/Customer/Warranties",
                    ActionText = "View Claim Status"
                });
                result.DbCitations.Add(new ChatSourceCitationDto
                {
                    Title = $"Warranty Claim: {c.ClaimNumber}",
                    Section = "Warranty Protection Ledger",
                    Snippet = $"Claim for {c.ComponentName} - Status: {c.Status}, Amount: ₹{c.AmountClaimed:N0}",
                    SourceType = "Database",
                    Score = 1.0,
                    LinkUrl = "/Customer/Warranties"
                });
            }
        }

        // Check for Warranty Number mentions e.g. "WAR-HY-2023-9901"
        var warMatch = Regex.Match(lower, @"(war-[a-z0-9\-]+|warranty\s*#?\s*(\d+))");
        if (warMatch.Success)
        {
            var warranties = await _unitOfWork.Repository<Warranty>().Query()
                .Include(w => w.Vehicle)
                .Include(w => w.Claims)
                .Where(w => w.WarrantyNumber.ToLower().Contains(warMatch.Value) || 
                            w.Id.ToString() == warMatch.Groups[2].Value)
                .Take(2)
                .ToListAsync();

            foreach (var w in warranties)
            {
                result.ContextSummaries.Add($"Warranty {w.WarrantyNumber} ({w.Provider} - {w.Type}) for {w.Vehicle?.RegistrationNumber}: Valid until {w.EndDate:dd MMM yyyy} or {w.MaxMileageLimit:N0} km. Status: '{w.Status}', Claims: {w.Claims.Count}.");
                result.EntityCards.Add(new ChatEntityCardDto
                {
                    Type = "Warranty",
                    Title = $"{w.Provider} ({w.WarrantyNumber})",
                    Subtitle = $"{w.Vehicle?.Make} {w.Vehicle?.Model} ({w.Vehicle?.RegistrationNumber})",
                    BadgeText = w.Status.ToString(),
                    BadgeColor = w.Status == WarrantyStatus.Active ? "success" : "danger",
                    KeyValues = new()
                    {
                        { "Plan", w.Type.ToString() },
                        { "Coverage", string.IsNullOrEmpty(w.CoverageTerms) ? w.CoveredItemsSummary : w.CoverageTerms },
                        { "Expires On", w.EndDate.ToString("dd MMM yyyy") },
                        { "Claims Filed", w.Claims.Count.ToString() }
                    },
                    ActionUrl = "/Customer/Warranties",
                    ActionText = "View Warranty Details"
                });
                result.DbCitations.Add(new ChatSourceCitationDto
                {
                    Title = $"Warranty Policy: {w.WarrantyNumber}",
                    Section = "Active Protection Plans",
                    Snippet = $"{w.Provider} {w.Type} Warranty - Status: {w.Status}",
                    SourceType = "Database",
                    Score = 1.0,
                    LinkUrl = "/Customer/Warranties"
                });
            }
        }
    }

    private async Task QueryCustomerDataAsync(string lower, string userId, DatabaseRagResult result)
    {
        bool askVehicles = lower.Contains("my vehicle") || lower.Contains("my car") || lower.Contains("garage") || lower.Contains("registered vehicle") || lower.Contains("my vehicles");
        bool askAppointments = lower.Contains("appointment") || lower.Contains("booking") || lower.Contains("schedule") || lower.Contains("booked");
        bool askEstimates = lower.Contains("estimate") || lower.Contains("approval") || lower.Contains("quote") || lower.Contains("repair cost");
        bool askInvoices = lower.Contains("invoice") || lower.Contains("bill") || lower.Contains("payment") || lower.Contains("pay") || lower.Contains("receipt");
        bool askWarranties = lower.Contains("warranty") || lower.Contains("warranties") || lower.Contains("claim") || lower.Contains("claims") || lower.Contains("coverage") || lower.Contains("protection plan");
        bool askRoadside = lower.Contains("roadside") || lower.Contains("breakdown") || lower.Contains("sos") || lower.Contains("tow");

        bool noSpecificCategory = !askAppointments && !askEstimates && !askInvoices && !askWarranties && !askRoadside;

        // Customer's registered vehicles (only when explicitly asked or general vehicle intent)
        if (askVehicles || (noSpecificCategory && (lower.Contains("vehicle") || lower.Contains("car") || result.ContextSummaries.Count == 0)))
        {
            var vehiclesQuery = _unitOfWork.Repository<Vehicle>().Query().Where(v => v.IsActive);
            if (!string.IsNullOrEmpty(userId))
            {
                vehiclesQuery = vehiclesQuery.Where(v => v.CustomerId == userId);
            }

            var vehicles = await vehiclesQuery.Take(4).ToListAsync();
            if (vehicles.Count > 0)
            {
                foreach (var v in vehicles)
                {
                    result.ContextSummaries.Add($"Customer Vehicle: {v.Make} {v.Model} ({v.ManufacturingYear}), Plate: {v.RegistrationNumber}, Fuel: {v.FuelType}, Current Mileage: {v.CurrentMileage:N0} km.");
                    result.EntityCards.Add(new ChatEntityCardDto
                    {
                        Type = "Vehicle",
                        Title = $"{v.Make} {v.Model} ({v.ManufacturingYear})",
                        Subtitle = $"Registration: {v.RegistrationNumber}",
                        BadgeText = $"{v.CurrentMileage:N0} KM",
                        BadgeColor = "info",
                        KeyValues = new()
                        {
                            { "Fuel", v.FuelType.ToString() },
                            { "VIN", string.IsNullOrEmpty(v.VIN) ? "N/A" : v.VIN }
                        },
                        ActionUrl = $"/Customer/BookService?vehicleId={v.Id}",
                        ActionText = "Book Service"
                    });
                }
                result.DbCitations.Add(new ChatSourceCitationDto
                {
                    Title = "Customer Digital Garage",
                    Section = "Live Vehicle Assets",
                    Snippet = $"{vehicles.Count} vehicles registered in customer profile.",
                    SourceType = "Database",
                    Score = 0.95,
                    LinkUrl = "/Customer/Index"
                });
            }
        }

        // Appointments & Service Bookings
        if (askAppointments)
        {
            bool askActiveOnly = lower.Contains("active") || lower.Contains("upcoming") || lower.Contains("current") || lower.Contains("next") || lower.Contains("pending") || lower.Contains("open");

            var allAppointments = await _unitOfWork.Repository<ServiceAppointment>().Query()
                .Include(a => a.Vehicle)
                .Include(a => a.ServiceCenter)
                .Include(a => a.JobCard)
                .Where(a => string.IsNullOrEmpty(userId) || a.CustomerId == userId)
                .OrderByDescending(a => a.AppointmentDate)
                .Take(5)
                .ToListAsync();

            if (askActiveOnly)
            {
                var activeAppointments = allAppointments
                    .Where(a => a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.Completed)
                    .ToList();

                if (activeAppointments.Count > 0)
                {
                    foreach (var a in activeAppointments)
                    {
                        result.ContextSummaries.Add($"Active Appointment #{a.Id} on {a.AppointmentDate:dd MMM yyyy} at {a.TimeSlot} at {a.ServiceCenter?.Name} for {a.Vehicle?.Make} {a.Vehicle?.Model} ({a.Vehicle?.RegistrationNumber}): Status = '{a.Status}'.");
                        result.EntityCards.Add(new ChatEntityCardDto
                        {
                            Type = "Appointment",
                            Title = $"Active Appointment #{a.Id}",
                            Subtitle = $"{a.AppointmentDate:dd MMM yyyy} at {a.TimeSlot}",
                            BadgeText = a.Status.ToString(),
                            BadgeColor = GetStatusColor(a.Status.ToString()),
                            KeyValues = new()
                            {
                                { "Center", a.ServiceCenter?.Name ?? "Main Workshop" },
                                { "Vehicle", $"{a.Vehicle?.Make} {a.Vehicle?.Model} ({a.Vehicle?.RegistrationNumber})" },
                                { "Status", a.Status.ToString() }
                            },
                            ActionUrl = a.JobCard != null ? $"/Customer/TrackJob/{a.JobCard.Id}" : "/Customer/Appointments",
                            ActionText = a.JobCard != null ? "Track Live Servicing" : "Manage Appointment"
                        });
                    }

                    result.DbCitations.Add(new ChatSourceCitationDto
                    {
                        Title = "Live Active Appointments",
                        Section = "Workshop Booking Ledger",
                        Snippet = $"{activeAppointments.Count} active service booking(s) found.",
                        SourceType = "Database",
                        Score = 1.0,
                        LinkUrl = "/Customer/Appointments"
                    });
                }
                else
                {
                    // No active appointments
                    var pastCancelled = allAppointments.FirstOrDefault(a => a.Status == AppointmentStatus.Cancelled);
                    if (pastCancelled != null)
                    {
                        result.ContextSummaries.Add($"You currently have no active service bookings. (Your previous appointment #{pastCancelled.Id} scheduled for {pastCancelled.AppointmentDate:dd MMM yyyy} was Cancelled).");
                    }
                    else
                    {
                        result.ContextSummaries.Add("You currently have no active service bookings scheduled.");
                    }

                    result.Actions.Add(new ChatQuickActionDto
                    {
                        Label = "Book New Service",
                        Url = "/Customer/BookService",
                        Icon = "bi-calendar-plus"
                    });

                    result.DbCitations.Add(new ChatSourceCitationDto
                    {
                        Title = "Service Booking Ledger",
                        Section = "Customer Appointments",
                        Snippet = "0 active service bookings found.",
                        SourceType = "Database",
                        Score = 0.9,
                        LinkUrl = "/Customer/Appointments"
                    });
                }
            }
            else
            {
                // General appointments list
                foreach (var a in allAppointments.Take(3))
                {
                    result.ContextSummaries.Add($"Appointment #{a.Id} on {a.AppointmentDate:d} at {a.TimeSlot} at {a.ServiceCenter?.Name} for {a.Vehicle?.RegistrationNumber}: Status = '{a.Status}'.");
                    result.EntityCards.Add(new ChatEntityCardDto
                    {
                        Type = "Appointment",
                        Title = $"Appointment #{a.Id}",
                        Subtitle = $"{a.AppointmentDate:dd MMM yyyy} at {a.TimeSlot}",
                        BadgeText = a.Status.ToString(),
                        BadgeColor = GetStatusColor(a.Status.ToString()),
                        KeyValues = new()
                        {
                            { "Center", a.ServiceCenter?.Name ?? "Main Workshop" },
                            { "Vehicle", a.Vehicle?.RegistrationNumber ?? "N/A" }
                        },
                        ActionUrl = a.Status == AppointmentStatus.Cancelled ? "/Customer/BookService" : "/Customer/Appointments",
                        ActionText = a.Status == AppointmentStatus.Cancelled ? "Book New Service" : "Manage Appointments"
                    });
                }
            }
        }

        // Pending Estimates
        if (askEstimates)
        {
            var estimates = await _unitOfWork.Repository<RepairEstimate>().Query()
                .Include(e => e.JobCard).ThenInclude(j => j!.Vehicle)
                .Include(e => e.Items)
                .Where(e => string.IsNullOrEmpty(userId) || (e.JobCard != null && e.JobCard.Vehicle != null && e.JobCard.Vehicle.CustomerId == userId))
                .OrderByDescending(e => e.CreatedAt)
                .Take(3)
                .ToListAsync();

            foreach (var est in estimates)
            {
                result.ContextSummaries.Add($"Repair Estimate {est.EstimateNumber}: Status = '{est.ApprovalStatus}', Grand Total = ${est.GrandTotal:F2}.");
                result.EntityCards.Add(new ChatEntityCardDto
                {
                    Type = "Estimate",
                    Title = $"Estimate {est.EstimateNumber}",
                    Subtitle = $"Total: ${est.GrandTotal:F2}",
                    BadgeText = est.ApprovalStatus.ToString(),
                    BadgeColor = est.ApprovalStatus == EstimateApprovalStatus.Approved ? "success" : (est.ApprovalStatus == EstimateApprovalStatus.Pending ? "warning" : "secondary"),
                    KeyValues = new()
                    {
                        { "Parts Total", $"${est.TotalPartsCost:F2}" },
                        { "Labor Total", $"${est.TotalLaborCost:F2}" }
                    },
                    ActionUrl = $"/Customer/ViewEstimate/{est.Id}",
                    ActionText = "Review & Approve Estimate"
                });
            }
        }

        // Active Warranties & Warranty Claims History
        if (askWarranties)
        {
            var warranties = await _unitOfWork.Repository<Warranty>().Query()
                .Include(w => w.Vehicle)
                .Include(w => w.Claims)
                .Where(w => string.IsNullOrEmpty(userId) || (w.Vehicle != null && w.Vehicle.CustomerId == userId))
                .OrderByDescending(w => w.CreatedAt)
                .Take(4)
                .ToListAsync();

            var claims = await _unitOfWork.Repository<WarrantyClaim>().Query()
                .Include(c => c.Warranty).ThenInclude(w => w!.Vehicle)
                .Where(c => string.IsNullOrEmpty(userId) || c.ClaimedByCustomerId == userId || (c.Warranty != null && c.Warranty.Vehicle != null && c.Warranty.Vehicle.CustomerId == userId))
                .OrderByDescending(c => c.CreatedAt)
                .Take(5)
                .ToListAsync();

            bool askClaimsOnly = (lower.Contains("claim") || lower.Contains("claims")) && !lower.Contains("warranty");

            if (warranties.Count > 0)
            {
                result.ContextSummaries.Add($"Active Protection Plans ({warranties.Count}):");
                foreach (var w in warranties)
                {
                    result.ContextSummaries.Add($"Warranty {w.WarrantyNumber} ({w.Provider} - {w.Type}) for {w.Vehicle?.Make} {w.Vehicle?.Model} ({w.Vehicle?.RegistrationNumber}): Status = '{w.Status}', Valid until {w.EndDate:dd MMM yyyy} (Max Limit: {w.MaxMileageLimit:N0} km). Covered: {w.CoveredItemsSummary}.");

                    if (!askClaimsOnly || claims.Count == 0)
                    {
                        result.EntityCards.Add(new ChatEntityCardDto
                        {
                            Type = "Warranty",
                            Title = $"{w.Provider} ({w.WarrantyNumber})",
                            Subtitle = $"{w.Vehicle?.Make} {w.Vehicle?.Model} ({w.Vehicle?.RegistrationNumber})",
                            BadgeText = w.Status.ToString(),
                            BadgeColor = w.Status == WarrantyStatus.Active ? "success" : "danger",
                            KeyValues = new()
                            {
                                { "Plan", w.Type.ToString() },
                                { "Covered", string.IsNullOrEmpty(w.CoverageTerms) ? w.CoveredItemsSummary : w.CoverageTerms },
                                { "Expires On", w.EndDate.ToString("dd MMM yyyy") },
                                { "Max Limit", $"{w.MaxMileageLimit:N0} km" }
                            },
                            ActionUrl = "/Customer/Warranties",
                            ActionText = "Submit Claim"
                        });
                    }
                }
            }

            if (claims.Count > 0)
            {
                result.ContextSummaries.Add($"Warranty Claim History ({claims.Count}):");
                foreach (var c in claims)
                {
                    result.ContextSummaries.Add($"Claim {c.ClaimNumber}: Component = '{c.ComponentName}', Amount = ₹{c.AmountClaimed:N0}, Status = '{c.Status}', Submitted On = {c.CreatedAt:dd MMM yyyy}.");
                    result.EntityCards.Add(new ChatEntityCardDto
                    {
                        Type = "WarrantyClaim",
                        Title = $"Claim {c.ClaimNumber}",
                        Subtitle = $"{c.ComponentName} - {c.Warranty?.Vehicle?.RegistrationNumber ?? "Vehicle"}",
                        BadgeText = c.Status.ToString(),
                        BadgeColor = c.Status == WarrantyClaimStatus.Approved ? "success" : (c.Status == WarrantyClaimStatus.Submitted ? "warning" : (c.Status == WarrantyClaimStatus.Rejected ? "danger" : "info")),
                        KeyValues = new()
                        {
                            { "Component", c.ComponentName },
                            { "Amount Claimed", $"₹{c.AmountClaimed:N0}" },
                            { "Amount Approved", c.AmountApproved > 0 ? $"₹{c.AmountApproved:N0}" : "Pending Review" },
                            { "Warranty", c.Warranty?.WarrantyNumber ?? "OEM Warranty" },
                            { "Date", c.CreatedAt.ToString("dd MMM yyyy") }
                        },
                        ActionUrl = "/Customer/Warranties",
                        ActionText = "View Claim Details"
                    });
                }

                result.DbCitations.Add(new ChatSourceCitationDto
                {
                    Title = "Warranty Claims History",
                    Section = "Customer Claims Ledger",
                    Snippet = $"{claims.Count} warranty claims recorded on file.",
                    SourceType = "Database",
                    Score = 0.99,
                    LinkUrl = "/Customer/Warranties"
                });
            }
            else if (warranties.Count > 0)
            {
                result.ContextSummaries.Add("No warranty claims have been submitted under your active warranty protection plans yet.");
                result.DbCitations.Add(new ChatSourceCitationDto
                {
                    Title = "Active Protection Plans",
                    Section = "Warranty Registry",
                    Snippet = $"{warranties.Count} active warranties found. 0 claims submitted.",
                    SourceType = "Database",
                    Score = 0.95,
                    LinkUrl = "/Customer/Warranties"
                });
            }
            else
            {
                result.ContextSummaries.Add("No active warranties or warranty claims found for your profile.");
            }

            result.Actions.Add(new ChatQuickActionDto
            {
                Label = "Submit Warranty Claim",
                Url = "/Customer/Warranties",
                Icon = "bi-shield-plus"
            });
            result.Actions.Add(new ChatQuickActionDto
            {
                Label = "View Warranties Dashboard",
                Url = "/Customer/Warranties",
                Icon = "bi-patch-check"
            });
        }

        // Roadside Assistance
        if (askRoadside)
        {
            var rsa = await _unitOfWork.Repository<RoadsideAssistanceRequest>().Query()
                .Include(r => r.Vehicle)
                .Where(r => string.IsNullOrEmpty(userId) || r.CustomerId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (rsa != null)
            {
                result.ContextSummaries.Add($"Active Roadside Request {rsa.RequestNumber}: Type = {rsa.RequestType}, Status = '{rsa.Status}', Technician: {rsa.AssignedTechnicianName} ({rsa.AssignedTechnicianPhone}), ETA: {rsa.EstimatedArrivalTime:t}.");
                result.EntityCards.Add(new ChatEntityCardDto
                {
                    Type = "Roadside",
                    Title = $"SOS Request {rsa.RequestNumber}",
                    Subtitle = $"{rsa.RequestType} Support",
                    BadgeText = rsa.Status.ToString(),
                    BadgeColor = "warning",
                    KeyValues = new()
                    {
                        { "Location", rsa.LocationAddress },
                        { "Technician", rsa.AssignedTechnicianName ?? "Dispatching" },
                        { "Tech Phone", rsa.AssignedTechnicianPhone ?? "1800-AUTOPRO" }
                    },
                    ActionUrl = "/Customer/Roadside",
                    ActionText = "Track Roadside Rescue"
                });
            }
        }
    }

    private async Task QueryFleetDataAsync(string lower, string userId, DatabaseRagResult result)
    {
        var fleets = await _unitOfWork.Repository<CompanyFleet>().Query()
            .Include(f => f.Vehicles)
            .ToListAsync();

        foreach (var f in fleets)
        {
            result.ContextSummaries.Add($"Corporate Fleet '{f.CompanyName}' (Reg: {f.RegistrationNumber}): Total vehicles: {f.Vehicles.Count}, Contact: {f.ContactPerson}.");
        }

        bool askMaintenance = lower.Contains("maintenance") || lower.Contains("pm") || lower.Contains("due") || lower.Contains("service");
        if (askMaintenance)
        {
            var fleetVehicles = await _unitOfWork.Repository<Vehicle>().Query()
                .Where(v => v.CompanyFleetId != null && v.IsActive)
                .ToListAsync();

            int overdueCount = 0;
            foreach (var fv in fleetVehicles)
            {
                bool isDue = fv.CurrentMileage % 10000 >= 8000;
                if (isDue)
                {
                    overdueCount++;
                    result.ContextSummaries.Add($"Fleet Vehicle {fv.Make} {fv.Model} ({fv.RegistrationNumber}) is due for Preventive Maintenance (Current Mileage: {fv.CurrentMileage:N0} km).");
                    result.EntityCards.Add(new ChatEntityCardDto
                    {
                        Type = "Vehicle",
                        Title = $"{fv.Make} {fv.Model}",
                        Subtitle = $"Plate: {fv.RegistrationNumber}",
                        BadgeText = "Maintenance Due",
                        BadgeColor = "warning",
                        KeyValues = new()
                        {
                            { "Mileage", $"{fv.CurrentMileage:N0} km" },
                            { "Fleet", "Corporate Fleet" }
                        },
                        ActionUrl = "/Fleet/PreventiveMaintenance",
                        ActionText = "Schedule Fleet PM"
                    });
                }
            }

            result.DbCitations.Add(new ChatSourceCitationDto
            {
                Title = "Corporate Fleet Registry",
                Section = "Preventive Maintenance Telematics",
                Snippet = $"{overdueCount} commercial fleet vehicles approaching or exceeding scheduled maintenance intervals.",
                SourceType = "Database",
                Score = 0.92,
                LinkUrl = "/Fleet/PreventiveMaintenance"
            });
        }
    }

    private async Task QueryMechanicDataAsync(string lower, string userId, DatabaseRagResult result)
    {
        var query = _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Vehicle)
            .Include(j => j.ServiceBay)
            .Include(j => j.ServiceAdvisor)
            .Include(j => j.Appointment);

        List<ServiceJobCard> jobs;
        if (!string.IsNullOrEmpty(userId))
        {
            jobs = await query.Where(j => j.MechanicId == userId)
                .OrderByDescending(j => j.CreatedAt)
                .Take(6)
                .ToListAsync();

            // If no jobs assigned to this specific mechanic user ID, check all recent jobs in workshop
            if (jobs.Count == 0)
            {
                jobs = await query
                    .OrderByDescending(j => j.CreatedAt)
                    .Take(4)
                    .ToListAsync();
            }
        }
        else
        {
            jobs = await query
                .OrderByDescending(j => j.CreatedAt)
                .Take(4)
                .ToListAsync();
        }

        var activeJobs = jobs.Where(j => j.Status != AppointmentStatus.Cancelled && j.Status != AppointmentStatus.Completed).ToList();
        var otherJobs = jobs.Where(j => j.Status == AppointmentStatus.Cancelled || j.Status == AppointmentStatus.Completed).ToList();

        bool askActiveOnly = lower.Contains("active") || lower.Contains("current") || lower.Contains("in progress");

        if (activeJobs.Count > 0)
        {
            result.ContextSummaries.Add($"You have {activeJobs.Count} active repair job card(s) assigned in your workstation:");
            foreach (var j in activeJobs)
            {
                result.ContextSummaries.Add($"Job Card {j.JobCardNumber}: {j.Vehicle?.Make} {j.Vehicle?.Model} ({j.Vehicle?.RegistrationNumber}) - Status: '{j.Status}', Bay: {j.ServiceBay?.BayName ?? (j.ServiceBayId.HasValue ? $"Bay #{j.ServiceBayId}" : "General Bay")}, Labor Logged: {j.TotalLaborHours} hrs.");
                result.EntityCards.Add(new ChatEntityCardDto
                {
                    Type = "JobCard",
                    Title = $"Job Card {j.JobCardNumber}",
                    Subtitle = $"{j.Vehicle?.Make} {j.Vehicle?.Model} ({j.Vehicle?.RegistrationNumber})",
                    BadgeText = j.Status.ToString(),
                    BadgeColor = GetStatusColor(j.Status.ToString()),
                    KeyValues = new()
                    {
                        { "Bay", j.ServiceBay?.BayName ?? (j.ServiceBayId.HasValue ? $"Bay #{j.ServiceBayId}" : "General Bay") },
                        { "Advisor", j.ServiceAdvisor?.FullName ?? "Service Advisor" },
                        { "Labor Logged", $"{j.TotalLaborHours} hrs" }
                    },
                    ActionUrl = $"/Mechanic/JobDetails/{j.Id}",
                    ActionText = "Open Digital Checklist"
                });
            }

            result.DbCitations.Add(new ChatSourceCitationDto
            {
                Title = "Mechanic Workstation",
                Section = "Active Assigned Repair Jobs",
                Snippet = $"{activeJobs.Count} active repair job card(s) assigned in your workstation.",
                SourceType = "Database",
                Score = 0.98,
                LinkUrl = "/Mechanic/Index"
            });

            result.Actions.Add(new ChatQuickActionDto
            {
                Label = "Open Workstation",
                Url = "/Mechanic/Index",
                Icon = "bi-wrench-adjustable"
            });
        }
        else if (otherJobs.Count > 0)
        {
            // All assigned jobs are either Cancelled or Completed
            string header = askActiveOnly
                ? $"You currently have no active repair jobs in progress in your workstation. (You have {otherJobs.Count} previous/cancelled job card(s)):"
                : $"Assigned repair job cards in your workstation ({otherJobs.Count} previous/cancelled):";

            result.ContextSummaries.Add(header);

            foreach (var j in otherJobs.Take(4))
            {
                result.ContextSummaries.Add($"Job Card {j.JobCardNumber} for {j.Vehicle?.Make} {j.Vehicle?.Model} ({j.Vehicle?.RegistrationNumber}): Status is '{j.Status}'.");
                result.EntityCards.Add(new ChatEntityCardDto
                {
                    Type = "JobCard",
                    Title = $"Job Card {j.JobCardNumber}",
                    Subtitle = $"{j.Vehicle?.Make} {j.Vehicle?.Model} ({j.Vehicle?.RegistrationNumber})",
                    BadgeText = j.Status.ToString(),
                    BadgeColor = GetStatusColor(j.Status.ToString()),
                    KeyValues = new()
                    {
                        { "Bay", j.ServiceBay?.BayName ?? (j.ServiceBayId.HasValue ? $"Bay #{j.ServiceBayId}" : "General Bay") },
                        { "Advisor", j.ServiceAdvisor?.FullName ?? "Service Advisor" },
                        { "Status", j.Status.ToString() }
                    },
                    ActionUrl = $"/Mechanic/JobDetails/{j.Id}",
                    ActionText = "View Job Details"
                });
            }

            result.DbCitations.Add(new ChatSourceCitationDto
            {
                Title = "Mechanic Workstation",
                Section = "Assigned Jobs History",
                Snippet = $"Found {otherJobs.Count} assigned job card(s) ({string.Join(", ", otherJobs.Select(j => j.Status.ToString()).Distinct())}).",
                SourceType = "Database",
                Score = 0.95,
                LinkUrl = "/Mechanic/Index"
            });

            result.Actions.Add(new ChatQuickActionDto
            {
                Label = "View Workstation",
                Url = "/Mechanic/Index",
                Icon = "bi-wrench-adjustable"
            });
        }
        else
        {
            result.ContextSummaries.Add("You currently have no repair job cards assigned to your workstation. When a Service Advisor assigns a job card to you, it will appear here.");
            result.DbCitations.Add(new ChatSourceCitationDto
            {
                Title = "Mechanic Workstation",
                Section = "Assigned Repair Jobs",
                Snippet = "0 assigned repair job cards currently found.",
                SourceType = "Database",
                Score = 0.90,
                LinkUrl = "/Mechanic/Index"
            });

            result.Actions.Add(new ChatQuickActionDto
            {
                Label = "Open Workstation",
                Url = "/Mechanic/Index",
                Icon = "bi-wrench-adjustable"
            });
        }
    }

    private async Task QueryStaffAndInventoryDataAsync(string lower, DatabaseRagResult result)
    {
        bool askInventory = lower.Contains("inventory") || lower.Contains("part") || lower.Contains("stock") || lower.Contains("reorder") || lower.Contains("low stock");
        bool askBays = lower.Contains("bay") || lower.Contains("capacity") || lower.Contains("occupancy") || lower.Contains("workshop");
        bool askJobs = lower.Contains("job card") || lower.Contains("active job") || lower.Contains("assigned") || lower.Contains("work order");

        if (askJobs)
        {
            var activeJobs = await _unitOfWork.Repository<ServiceJobCard>().Query()
                .Include(j => j.Vehicle)
                .Include(j => j.Mechanic)
                .Include(j => j.ServiceBay)
                .Where(j => j.Status != AppointmentStatus.Cancelled && j.Status != AppointmentStatus.Completed)
                .OrderByDescending(j => j.CreatedAt)
                .Take(4)
                .ToListAsync();

            if (activeJobs.Count > 0)
            {
                result.ContextSummaries.Add($"Active Workshop Job Cards ({activeJobs.Count}):");
                foreach (var j in activeJobs)
                {
                    result.ContextSummaries.Add($"Job Card {j.JobCardNumber} for {j.Vehicle?.Make} {j.Vehicle?.Model} ({j.Vehicle?.RegistrationNumber}): Status = '{j.Status}', Mechanic: {j.Mechanic?.FullName ?? "Unassigned"}, Bay: #{j.ServiceBayId}.");
                    result.EntityCards.Add(new ChatEntityCardDto
                    {
                        Type = "JobCard",
                        Title = $"Job Card {j.JobCardNumber}",
                        Subtitle = $"{j.Vehicle?.Make} {j.Vehicle?.Model} ({j.Vehicle?.RegistrationNumber})",
                        BadgeText = j.Status.ToString(),
                        BadgeColor = GetStatusColor(j.Status.ToString()),
                        KeyValues = new()
                        {
                            { "Mechanic", j.Mechanic?.FullName ?? "Pending Assignment" },
                            { "Bay", j.ServiceBay?.BayName ?? (j.ServiceBayId.HasValue ? $"Bay #{j.ServiceBayId}" : "General Bay") },
                            { "Labor", $"{j.TotalLaborHours} hrs" }
                        },
                        ActionUrl = $"/ServiceAdvisor/JobCardDetails/{j.Id}",
                        ActionText = "Manage Job Card"
                    });
                }

                result.DbCitations.Add(new ChatSourceCitationDto
                {
                    Title = "Workshop Active Job Cards",
                    Section = "Live Workshop Floor",
                    Snippet = $"{activeJobs.Count} job cards currently active in the workshop.",
                    SourceType = "Database",
                    Score = 0.98,
                    LinkUrl = "/ServiceAdvisor/Appointments"
                });
            }
        }

        if (askInventory)
        {
            var lowStockParts = await _unitOfWork.Repository<InventoryPart>().Query()
                .Where(p => p.AvailableQuantity <= p.ReorderLevel && p.IsActive)
                .Take(5)
                .ToListAsync();

            if (lowStockParts.Count > 0)
            {
                foreach (var p in lowStockParts)
                {
                    result.ContextSummaries.Add($"Low Stock Spare Part: {p.Name} (SKU: {p.PartNumber}) - Available: {p.AvailableQuantity}, Reorder Level: {p.ReorderLevel}, Selling Price: ${p.SellingPrice:F2}.");
                    result.EntityCards.Add(new ChatEntityCardDto
                    {
                        Type = "Part",
                        Title = p.Name,
                        Subtitle = $"SKU: {p.PartNumber}",
                        BadgeText = $"Stock: {p.AvailableQuantity}",
                        BadgeColor = "danger",
                        KeyValues = new()
                        {
                            { "Category", p.Category },
                            { "Reorder Level", p.ReorderLevel.ToString() },
                            { "Price", $"${p.SellingPrice:F2}" }
                        },
                        ActionUrl = "/Inventory/LowStock",
                        ActionText = "Reorder Stock"
                    });
                }
                result.DbCitations.Add(new ChatSourceCitationDto
                {
                    Title = "Inventory Stock Ledger",
                    Section = "Threshold & Concurrency Control",
                    Snippet = $"{lowStockParts.Count} critical spare parts are below safe reorder thresholds.",
                    SourceType = "Database",
                    Score = 0.98,
                    LinkUrl = "/Inventory/LowStock"
                });
            }
        }

        if (askBays)
        {
            var bays = await _unitOfWork.Repository<ServiceBay>().Query()
                .Include(b => b.ServiceCenter)
                .ToListAsync();

            int occupied = bays.Count(b => b.Status == BayStatus.Occupied);
            int available = bays.Count(b => b.Status == BayStatus.Available && b.IsActive);

            result.ContextSummaries.Add($"Workshop Service Bays: Total = {bays.Count}, Currently Occupied = {occupied}, Available = {available}.");
        }
    }

    private async Task QueryCatalogDataAsync(string lower, DatabaseRagResult result)
    {
        bool askPackages = lower.Contains("package") || lower.Contains("price") || lower.Contains("cost") || lower.Contains("pricing") || lower.Contains("service type") || lower.Contains("how much");
        bool askCenters = lower.Contains("center") || lower.Contains("location") || lower.Contains("where") || lower.Contains("address") || lower.Contains("hours");
        bool askCoupons = lower.Contains("coupon") || lower.Contains("discount") || lower.Contains("offer") || lower.Contains("promo");

        if (askPackages)
        {
            var packages = await _unitOfWork.Repository<ServicePackage>().Query()
                .Include(p => p.PackageItems)
                .Where(p => p.IsActive)
                .Take(4)
                .ToListAsync();

            foreach (var p in packages)
            {
                result.ContextSummaries.Add($"Service Package '{p.Name}': Package Price = ${p.PackagePrice:F2}, Validity = {p.ValidityDays} days. Description: {p.Description}");
            }
        }

        if (askCenters)
        {
            var centers = await _unitOfWork.Repository<ServiceCenter>().Query().Where(c => c.IsActive).Take(3).ToListAsync();
            foreach (var c in centers)
            {
                result.ContextSummaries.Add($"Service Center '{c.Name}' ({c.Code}): Address: {c.Address}, {c.City}. Phone: {c.Phone}, Email: {c.Email}. Operating Hours: {c.OperatingHours}.");
            }
        }

        if (askCoupons)
        {
            var coupons = await _unitOfWork.Repository<Coupon>().Query()
                .Where(c => c.IsActive && c.ExpiryDate >= DateTime.UtcNow)
                .Take(3)
                .ToListAsync();

            foreach (var cp in coupons)
            {
                string valStr = cp.Type == DiscountType.Percentage ? $"{cp.Value}%" : $"${cp.Value}";
                result.ContextSummaries.Add($"Promotional Coupon '{cp.Code}': {valStr} discount (Max discount: ${cp.MaxDiscountLimit:F2}, Min spend: ${cp.MinimumBillAmount:F2}). Valid until {cp.ExpiryDate:d}.");
            }
        }
    }

    private static string GetStatusColor(string status)
    {
        return status.ToLower() switch
        {
            "completed" or "successful" or "approved" or "active" => "success",
            "inprogress" or "underinspection" or "pending" => "warning",
            "cancelled" or "rejected" or "failed" => "danger",
            _ => "primary"
        };
    }
}
