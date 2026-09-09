using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Application.Services;

public class InvoiceService : IInvoiceService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;

    public InvoiceService(IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    public async Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(PaymentStatus? status = null, string? customerId = null)
    {
        var query = _unitOfWork.Repository<Invoice>().Query()
            .Include(i => i.JobCard)
            .Include(i => i.Customer)
            .Include(i => i.Vehicle)
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        if (!string.IsNullOrEmpty(customerId))
        {
            query = query.Where(i => i.CustomerId == customerId);
        }

        var list = await query.OrderByDescending(i => i.IssueDate).ToListAsync();
        return list.Select(MapToDto).ToList();
    }

    public async Task<InvoiceDto?> GetInvoiceByIdAsync(int id)
    {
        var i = await _unitOfWork.Repository<Invoice>().Query()
            .Include(i => i.JobCard)
            .Include(i => i.Customer)
            .Include(i => i.Vehicle)
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        return i == null ? null : MapToDto(i);
    }

    public async Task<InvoiceDto?> GetInvoiceByJobCardIdAsync(int jobCardId)
    {
        var i = await _unitOfWork.Repository<Invoice>().Query()
            .Include(i => i.JobCard)
            .Include(i => i.Customer)
            .Include(i => i.Vehicle)
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(i => i.JobCardId == jobCardId);

        return i == null ? null : MapToDto(i);
    }

    public async Task<InvoiceDto> GenerateInvoiceFromJobCardAsync(int jobCardId)
    {
        var existing = await _unitOfWork.Repository<Invoice>().Query()
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .Include(i => i.Customer)
            .Include(i => i.Vehicle)
            .Include(i => i.JobCard)
            .FirstOrDefaultAsync(i => i.JobCardId == jobCardId);

        if (existing != null) return MapToDto(existing);

        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment).ThenInclude(a => a!.ServiceType)
            .Include(j => j.Appointment).ThenInclude(a => a!.ServicePackage)
            .Include(j => j.Appointment).ThenInclude(a => a!.Customer)
            .Include(j => j.Vehicle).ThenInclude(v => v!.CompanyFleet)
            .Include(j => j.RepairEstimates).ThenInclude(e => e.Items)
            .Include(j => j.PartsConsumed).ThenInclude(p => p.InventoryPart)
            .FirstOrDefaultAsync(j => j.Id == jobCardId);

        if (jobCard == null) throw new DomainException("Job card not found.");

        var invoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..5].ToUpper()}";
        var invoiceItems = new List<InvoiceItem>();

        decimal servicesTotal = 0;
        decimal partsTotal = 0;
        decimal laborTotal = 0;

        // 1. Base Service Type or Package
        if (jobCard.Appointment?.ServicePackage != null)
        {
            var pkg = jobCard.Appointment.ServicePackage;
            servicesTotal += pkg.PackagePrice;
            invoiceItems.Add(new InvoiceItem
            {
                Description = $"Service Package: {pkg.Name}",
                Category = "Package",
                Quantity = 1,
                UnitPrice = pkg.PackagePrice,
                LineTotal = pkg.PackagePrice
            });
        }
        else if (jobCard.Appointment?.ServiceType != null)
        {
            var srv = jobCard.Appointment.ServiceType;
            servicesTotal += srv.BasePrice;
            laborTotal += srv.LaborCharges;
            invoiceItems.Add(new InvoiceItem
            {
                Description = $"Service: {srv.Name}",
                Category = "Service",
                Quantity = 1,
                UnitPrice = srv.BasePrice,
                LineTotal = srv.BasePrice
            });
            invoiceItems.Add(new InvoiceItem
            {
                Description = "Standard Base Service Labor",
                Category = "Labor",
                Quantity = 1,
                UnitPrice = srv.LaborCharges,
                LineTotal = srv.LaborCharges
            });
        }

        // 2. Approved Estimate Items (Only approved items!)
        var activeEstimate = jobCard.RepairEstimates.OrderByDescending(e => e.CreatedAt).FirstOrDefault();
        if (activeEstimate != null && activeEstimate.ApprovalStatus != EstimateApprovalStatus.Rejected)
        {
            foreach (var item in activeEstimate.Items.Where(i => i.IsApprovedByCustomer))
            {
                if (item.ItemType == EstimateItemType.Part)
                {
                    partsTotal += item.UnitPrice * item.Quantity;
                }
                if (item.LaborCharges > 0)
                {
                    laborTotal += item.LaborCharges;
                }

                invoiceItems.Add(new InvoiceItem
                {
                    Description = item.Description,
                    Category = item.ItemType.ToString(),
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.UnitPrice * item.Quantity + item.LaborCharges
                });
            }
        }

        decimal subTotal = servicesTotal + partsTotal + laborTotal;

        // 3. Corporate Fleet or Customer Discount
        decimal discount = 0;
        if (jobCard.Vehicle?.CompanyFleet != null)
        {
            decimal rate = jobCard.Vehicle.CompanyFleet.CorporateDiscountRate;
            discount = subTotal * (rate / 100m);
        }

        decimal taxable = Math.Max(0, subTotal - discount);
        decimal tax = taxable * 0.18m; // Standard 18% GST/VAT
        decimal grandTotal = taxable + tax;

        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            JobCardId = jobCardId,
            CustomerId = jobCard.Appointment?.CustomerId ?? string.Empty,
            VehicleId = jobCard.VehicleId,
            IssueDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(7),
            ServicesTotal = servicesTotal,
            PartsTotal = partsTotal,
            LaborTotal = laborTotal,
            SubTotal = subTotal,
            DiscountAmount = discount,
            TaxAmount = tax,
            GrandTotal = grandTotal,
            PaidAmount = 0,
            Status = PaymentStatus.Pending,
            Items = invoiceItems
        };

        await _unitOfWork.Repository<Invoice>().AddAsync(invoice);
        await _unitOfWork.SaveChangesAsync();

        if (jobCard.Appointment?.CustomerId != null)
        {
            await _notificationService.SendNotificationAsync(
                jobCard.Appointment.CustomerId,
                "Invoice Generated",
                $"Invoice {invoiceNumber} for ₹{grandTotal:N2} is ready. You can download or pay online.",
                NotificationType.Invoice,
                $"/Customer/ViewInvoice/{invoice.Id}");
        }

        return (await GetInvoiceByIdAsync(invoice.Id))!;
    }

    private static InvoiceDto MapToDto(Invoice i)
    {
        return new InvoiceDto
        {
            Id = i.Id,
            InvoiceNumber = i.InvoiceNumber,
            JobCardId = i.JobCardId,
            JobCardNumber = i.JobCard?.JobCardNumber ?? string.Empty,
            CustomerId = i.CustomerId,
            CustomerName = i.Customer?.FullName ?? "Customer",
            CustomerEmail = i.Customer?.Email ?? string.Empty,
            CustomerPhone = i.Customer?.PhoneNumber ?? string.Empty,
            VehicleId = i.VehicleId,
            VehicleInfo = i.Vehicle != null ? $"{i.Vehicle.Make} {i.Vehicle.Model}" : "Vehicle",
            RegistrationNumber = i.Vehicle?.RegistrationNumber ?? string.Empty,
            IssueDate = i.IssueDate,
            DueDate = i.DueDate,
            ServicesTotal = i.ServicesTotal,
            PartsTotal = i.PartsTotal,
            LaborTotal = i.LaborTotal,
            SubTotal = i.SubTotal,
            DiscountAmount = i.DiscountAmount,
            TaxAmount = i.TaxAmount,
            GrandTotal = i.GrandTotal,
            PaidAmount = i.PaidAmount,
            Status = i.Status,
            Notes = i.Notes,
            Items = i.Items?.Select(item => new InvoiceItemDto
            {
                Id = item.Id,
                Description = item.Description,
                Category = item.Category,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TaxRate = item.TaxRate,
                LineTotal = item.LineTotal
            }).ToList() ?? new(),
            Payments = i.Payments?.Select(p => new PaymentTransactionDto
            {
                Id = p.Id,
                TransactionReference = p.TransactionReference,
                GatewayTransactionId = p.GatewayTransactionId,
                InvoiceId = p.InvoiceId,
                Amount = p.Amount,
                Method = p.Method,
                Status = p.Status,
                ProcessedAt = p.ProcessedAt
            }).ToList() ?? new()
        };
    }
}

public class WarrantyService : IWarrantyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;

    public WarrantyService(IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    public async Task<IReadOnlyList<WarrantyDto>> GetVehicleWarrantiesAsync(int vehicleId)
    {
        var list = await _unitOfWork.Repository<Warranty>().Query()
            .Include(w => w.Vehicle)
            .Include(w => w.Claims)
            .Where(w => w.VehicleId == vehicleId)
            .ToListAsync();

        return list.Select(MapToDto).ToList();
    }

    public async Task<WarrantyDto> RegisterWarrantyAsync(Warranty warranty)
    {
        warranty.WarrantyNumber = $"WAR-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..5].ToUpper()}";
        await _unitOfWork.Repository<Warranty>().AddAsync(warranty);
        await _unitOfWork.SaveChangesAsync();
        return MapToDto(warranty);
    }

    // SCENARIO 7: Warranty Coverage Eligibility Check
    public async Task<WarrantyEligibilityResultDto> CheckWarrantyEligibilityAsync(int vehicleId, string componentOrServiceName, int currentMileage)
    {
        var warranties = await _unitOfWork.Repository<Warranty>().Query()
            .Where(w => w.VehicleId == vehicleId && w.Status == WarrantyStatus.Active)
            .ToListAsync();

        if (!warranties.Any())
        {
            return new WarrantyEligibilityResultDto
            {
                IsEligible = false,
                Reason = "No active warranty policies registered for this vehicle."
            };
        }

        foreach (var war in warranties)
        {
            // 1. Date Validity Check
            if (DateTime.UtcNow > war.EndDate)
            {
                continue; // Expired by date
            }

            // 2. Mileage Limit Check
            if (currentMileage > war.MaxMileageLimit)
            {
                continue; // Exceeded mileage limit
            }

            // 3. Covered Components Check
            var coveredTerms = war.CoveredItemsSummary.ToLower().Split(',', StringSplitEmpty());
            bool isCovered = coveredTerms.Any(term => componentOrServiceName.ToLower().Contains(term.Trim().ToLower()));

            if (isCovered)
            {
                return new WarrantyEligibilityResultDto
                {
                    IsEligible = true,
                    WarrantyId = war.Id,
                    WarrantyNumber = war.WarrantyNumber,
                    MaxCoverageAmount = 50000m,
                    Reason = $"Covered under active warranty policy '{war.Provider}' ({war.WarrantyNumber}). Valid until {war.EndDate:yyyy-MM-dd} or {war.MaxMileageLimit:N0} KM."
                };
            }
        }

        return new WarrantyEligibilityResultDto
        {
            IsEligible = false,
            Reason = $"The component or service '{componentOrServiceName}' is not included in the covered warranty terms."
        };
    }

    public async Task<WarrantyClaimDto> SubmitClaimAsync(int warrantyId, int? jobCardId, string customerId, string issueDesc, string component, decimal amount)
    {
        var warranty = await _unitOfWork.Repository<Warranty>().Query()
            .Include(w => w.Vehicle)
            .FirstOrDefaultAsync(w => w.Id == warrantyId);

        if (warranty == null) throw new DomainException("Warranty not found.");

        var claimNumber = $"CLM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..5].ToUpper()}";

        var claim = new WarrantyClaim
        {
            ClaimNumber = claimNumber,
            WarrantyId = warrantyId,
            JobCardId = jobCardId,
            ClaimedByCustomerId = customerId,
            IssueDescription = issueDesc,
            ComponentName = component,
            AmountClaimed = amount,
            AmountApproved = 0,
            Status = WarrantyClaimStatus.Submitted
        };

        await _unitOfWork.Repository<WarrantyClaim>().AddAsync(claim);
        await _unitOfWork.SaveChangesAsync();

        return new WarrantyClaimDto
        {
            Id = claim.Id,
            ClaimNumber = claim.ClaimNumber,
            WarrantyId = claim.WarrantyId,
            WarrantyNumber = warranty.WarrantyNumber,
            JobCardId = claim.JobCardId,
            ClaimedByCustomerId = claim.ClaimedByCustomerId,
            IssueDescription = claim.IssueDescription,
            ComponentName = claim.ComponentName,
            AmountClaimed = claim.AmountClaimed,
            AmountApproved = claim.AmountApproved,
            Status = claim.Status
        };
    }

    public async Task<WarrantyClaimDto> ReviewClaimAsync(int claimId, WarrantyClaimStatus status, decimal approvedAmount, string reviewNotes, string reviewerUserId)
    {
        var claim = await _unitOfWork.Repository<WarrantyClaim>().Query()
            .Include(c => c.Warranty)
            .FirstOrDefaultAsync(c => c.Id == claimId);

        if (claim == null) throw new DomainException("Warranty claim not found.");

        claim.Status = status;
        claim.AmountApproved = status == WarrantyClaimStatus.Approved ? approvedAmount : 0;
        claim.ReviewNotes = reviewNotes;
        claim.ReviewedByUserId = reviewerUserId;
        claim.ReviewedAt = DateTime.UtcNow;

        await _unitOfWork.Repository<WarrantyClaim>().UpdateAsync(claim);
        await _unitOfWork.SaveChangesAsync();

        await _notificationService.SendNotificationAsync(
            claim.ClaimedByCustomerId,
            "Warranty Claim Decision",
            $"Your claim {claim.ClaimNumber} has been '{status}'. Approved amount: ₹{claim.AmountApproved:N2}.",
            NotificationType.Warranty,
            "/Customer/Warranties");

        return new WarrantyClaimDto
        {
            Id = claim.Id,
            ClaimNumber = claim.ClaimNumber,
            WarrantyId = claim.WarrantyId,
            WarrantyNumber = claim.Warranty?.WarrantyNumber ?? string.Empty,
            JobCardId = claim.JobCardId,
            ClaimedByCustomerId = claim.ClaimedByCustomerId,
            IssueDescription = claim.IssueDescription,
            ComponentName = claim.ComponentName,
            AmountClaimed = claim.AmountClaimed,
            AmountApproved = claim.AmountApproved,
            Status = claim.Status,
            ReviewNotes = claim.ReviewNotes
        };
    }

    private static StringSplitOptions StringSplitEmpty() => StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

    private static WarrantyDto MapToDto(Warranty w)
    {
        bool isValid = w.Status == WarrantyStatus.Active && DateTime.UtcNow <= w.EndDate;
        return new WarrantyDto
        {
            Id = w.Id,
            WarrantyNumber = w.WarrantyNumber,
            Type = w.Type,
            VehicleId = w.VehicleId,
            VehicleInfo = w.Vehicle != null ? $"{w.Vehicle.Make} {w.Vehicle.Model}" : string.Empty,
            RegistrationNumber = w.Vehicle?.RegistrationNumber ?? string.Empty,
            Provider = w.Provider,
            CoverageTerms = w.CoverageTerms,
            CoveredItemsSummary = w.CoveredItemsSummary,
            StartDate = w.StartDate,
            EndDate = w.EndDate,
            MaxMileageLimit = w.MaxMileageLimit,
            Status = w.Status,
            IsCurrentlyValid = isValid,
            Claims = w.Claims?.Select(c => new WarrantyClaimDto
            {
                Id = c.Id,
                ClaimNumber = c.ClaimNumber,
                WarrantyId = c.WarrantyId,
                WarrantyNumber = w.WarrantyNumber,
                JobCardId = c.JobCardId,
                ClaimedByCustomerId = c.ClaimedByCustomerId,
                IssueDescription = c.IssueDescription,
                ComponentName = c.ComponentName,
                AmountClaimed = c.AmountClaimed,
                AmountApproved = c.AmountApproved,
                Status = c.Status,
                ReviewNotes = c.ReviewNotes
            }).ToList() ?? new()
        };
    }
}