using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Application.Services;

public class EstimateService : IEstimateService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;

    public EstimateService(IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    public async Task<RepairEstimateDto?> GetEstimateByIdAsync(int id)
    {
        var e = await _unitOfWork.Repository<RepairEstimate>().Query()
            .Include(e => e.JobCard).ThenInclude(j => j!.Vehicle)
            .Include(e => e.JobCard).ThenInclude(j => j!.Appointment).ThenInclude(a => a!.Customer)
            .Include(e => e.Items).ThenInclude(i => i.InventoryPart)
            .FirstOrDefaultAsync(e => e.Id == id);

        return e == null ? null : MapToDto(e);
    }

    public async Task<RepairEstimateDto?> GetEstimateByJobCardIdAsync(int jobCardId)
    {
        var e = await _unitOfWork.Repository<RepairEstimate>().Query()
            .Include(e => e.JobCard).ThenInclude(j => j!.Vehicle)
            .Include(e => e.JobCard).ThenInclude(j => j!.Appointment).ThenInclude(a => a!.Customer)
            .Include(e => e.Items).ThenInclude(i => i.InventoryPart)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(e => e.JobCardId == jobCardId);

        return e == null ? null : MapToDto(e);
    }

    public async Task<RepairEstimateDto> CreateEstimateAsync(int jobCardId, string advisorId, List<EstimateItemDto> items)
    {
        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment).ThenInclude(a => a!.Customer)
            .Include(j => j.Vehicle)
            .FirstOrDefaultAsync(j => j.Id == jobCardId);

        if (jobCard == null) throw new DomainException("Job card not found.");

        if (jobCard.Status == AppointmentStatus.Cancelled || jobCard.Appointment?.Status == AppointmentStatus.Cancelled)
        {
            throw new DomainException("Cannot create an estimate for a cancelled appointment or job card.");
        }

        if (jobCard.Status == AppointmentStatus.Completed)
        {
            throw new DomainException("Cannot create an estimate for a completed job card.");
        }

        var estimateNumber = $"EST-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..5].ToUpper()}";

        decimal totalParts = 0;
        decimal totalLabor = 0;
        decimal totalTax = 0;

        var estimateItems = new List<EstimateItem>();
        foreach (var itemDto in items)
        {
            decimal partCost = itemDto.UnitPrice * itemDto.Quantity;
            decimal laborCost = itemDto.LaborCharges;
            decimal sub = partCost + laborCost;
            decimal tax = sub * (itemDto.TaxPercent / 100m);
            decimal lineTotal = sub + tax;

            totalParts += partCost;
            totalLabor += laborCost;
            totalTax += tax;

            estimateItems.Add(new EstimateItem
            {
                Description = itemDto.Description,
                ItemType = itemDto.ItemType,
                InventoryPartId = itemDto.InventoryPartId,
                Quantity = itemDto.Quantity,
                UnitPrice = itemDto.UnitPrice,
                LaborCharges = itemDto.LaborCharges,
                TaxPercent = itemDto.TaxPercent,
                LineTotal = lineTotal,
                IsApprovedByCustomer = true,
                IsOptional = itemDto.IsOptional,
                Reason = itemDto.Reason
            });
        }

        var estimate = new RepairEstimate
        {
            EstimateNumber = estimateNumber,
            JobCardId = jobCardId,
            PreparedByUserId = advisorId,
            TotalPartsCost = totalParts,
            TotalLaborCost = totalLabor,
            TaxAmount = totalTax,
            DiscountAmount = 0,
            GrandTotal = totalParts + totalLabor + totalTax,
            ApprovalStatus = EstimateApprovalStatus.Pending,
            Items = estimateItems
        };

        jobCard.Status = AppointmentStatus.EstimateCreated;
        await _unitOfWork.Repository<ServiceJobCard>().UpdateAsync(jobCard);

        await _unitOfWork.Repository<RepairEstimate>().AddAsync(estimate);

        // Reserve parts included in the estimate for this appointment if not already reserved
        if (jobCard.AppointmentId > 0)
        {
            foreach (var item in items.Where(i => i.ItemType == EstimateItemType.Part && i.InventoryPartId.HasValue))
            {
                var partId = item.InventoryPartId!.Value;
                var qty = Math.Max(1, item.Quantity);
                var alreadyReserved = await _unitOfWork.Repository<PartReservation>().Query()
                    .AnyAsync(r => r.AppointmentId == jobCard.AppointmentId && r.InventoryPartId == partId && !r.IsReleased && !r.IsConsumed);

                if (!alreadyReserved)
                {
                    var part = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(partId);
                    if (part != null && part.AvailableQuantity >= qty)
                    {
                        int beforeAvail = part.AvailableQuantity;
                        part.AvailableQuantity -= qty;
                        part.ReservedQuantity += qty;
                        await _unitOfWork.Repository<InventoryPart>().UpdateAsync(part);

                        await _unitOfWork.Repository<PartReservation>().AddAsync(new PartReservation
                        {
                            InventoryPartId = partId,
                            AppointmentId = jobCard.AppointmentId,
                            Quantity = qty,
                            IsReleased = false
                        });

                        await _unitOfWork.Repository<InventoryTransaction>().AddAsync(new InventoryTransaction
                        {
                            InventoryPartId = part.Id,
                            TransactionType = InventoryTransactionType.Reservation,
                            Quantity = qty,
                            QuantityBefore = beforeAvail,
                            QuantityAfter = part.AvailableQuantity,
                            UnitCost = part.CostPrice,
                            TotalAmount = part.CostPrice * qty,
                            ReferenceNumber = estimateNumber,
                            Notes = $"Stock reserved for Estimate {estimateNumber} on Job Card {jobCard.JobCardNumber}"
                        });
                    }
                }
            }
        }

        await _unitOfWork.SaveChangesAsync();

        if (jobCard.Appointment?.CustomerId != null)
        {
            await _notificationService.SendNotificationAsync(
                jobCard.Appointment.CustomerId,
                "Repair Estimate Ready for Review",
                $"Repair estimate {estimateNumber} (Total: ₹{estimate.GrandTotal:N2}) is ready. Please review and approve.",
                NotificationType.Estimate,
                $"/Customer/ViewEstimate/{estimate.Id}");
        }

        return (await GetEstimateByIdAsync(estimate.Id))!;
    }

    // SCENARIO 4: Estimate Rejection & Customer Line-Item Approval Handling
    public async Task<RepairEstimateDto> ProcessCustomerEstimateResponseAsync(CustomerEstimateResponseDto response)
    {
        var estimate = await _unitOfWork.Repository<RepairEstimate>().Query()
            .Include(e => e.Items)
            .Include(e => e.JobCard).ThenInclude(j => j!.Vehicle)
            .Include(e => e.JobCard).ThenInclude(j => j!.Appointment)
            .FirstOrDefaultAsync(e => e.Id == response.EstimateId);

        if (estimate == null) throw new DomainException("Estimate not found.");

        if (estimate.JobCard == null) throw new DomainException("Job card not found.");

        if (estimate.JobCard.Status == AppointmentStatus.Cancelled || estimate.JobCard.Appointment?.Status == AppointmentStatus.Cancelled)
        {
            throw new DomainException("Cannot approve or update an estimate for a cancelled appointment or job card.");
        }

        if (estimate.JobCard.Status == AppointmentStatus.Completed || estimate.JobCard.Appointment?.Status == AppointmentStatus.Completed)
        {
            throw new DomainException("Cannot process estimate for a completed job card.");
        }

        estimate.CustomerNotes = response.CustomerNotes;
        estimate.CustomerRespondedAt = DateTime.UtcNow;

        if (response.Action == EstimateApprovalStatus.Rejected)
        {
            // Customer rejected additional repairs
            estimate.ApprovalStatus = EstimateApprovalStatus.Rejected;
            foreach (var item in estimate.Items)
            {
                item.IsApprovedByCustomer = false;
                await _unitOfWork.Repository<EstimateItem>().UpdateAsync(item);
            }

            // Job proceeds ONLY with basic/original scheduled package services, excluding rejected additional repairs
            if (estimate.JobCard != null && estimate.JobCard.Status != AppointmentStatus.Cancelled && estimate.JobCard.Appointment?.Status != AppointmentStatus.Cancelled)
            {
                estimate.JobCard.Status = AppointmentStatus.InProgress;
                estimate.JobCard.MechanicNotes = (estimate.JobCard.MechanicNotes + $"\n[Customer Decision]: Additional repairs rejected ({response.CustomerNotes}). Proceeding with standard base service only.").Trim();
                if (estimate.JobCard.Appointment != null && estimate.JobCard.Appointment.Status != AppointmentStatus.Cancelled)
                {
                    estimate.JobCard.Appointment.Status = AppointmentStatus.InProgress;
                    await _unitOfWork.Repository<ServiceAppointment>().UpdateAsync(estimate.JobCard.Appointment);
                }
                await _unitOfWork.Repository<ServiceJobCard>().UpdateAsync(estimate.JobCard);
            }
        }
        else if (response.Action == EstimateApprovalStatus.Approved)
        {
            // Customer approved either all items or selective line items
            if (response.ApprovedItemIds != null && response.ApprovedItemIds.Count > 0)
            {
                foreach (var item in estimate.Items)
                {
                    item.IsApprovedByCustomer = response.ApprovedItemIds.Contains(item.Id);
                    await _unitOfWork.Repository<EstimateItem>().UpdateAsync(item);
                }
                estimate.ApprovalStatus = estimate.Items.All(i => i.IsApprovedByCustomer)
                    ? EstimateApprovalStatus.Approved
                    : EstimateApprovalStatus.PartiallyApproved;
            }
            else
            {
                foreach (var item in estimate.Items)
                {
                    item.IsApprovedByCustomer = true;
                    await _unitOfWork.Repository<EstimateItem>().UpdateAsync(item);
                }
                estimate.ApprovalStatus = EstimateApprovalStatus.Approved;
            }

            // Recalculate totals based strictly on approved items
            var approvedItems = estimate.Items.Where(i => i.IsApprovedByCustomer).ToList();
            estimate.TotalPartsCost = approvedItems.Where(i => i.ItemType == EstimateItemType.Part).Sum(i => i.UnitPrice * i.Quantity);
            estimate.TotalLaborCost = approvedItems.Sum(i => i.LaborCharges);
            estimate.TaxAmount = approvedItems.Sum(i => (i.UnitPrice * i.Quantity + i.LaborCharges) * (i.TaxPercent / 100m));
            estimate.GrandTotal = estimate.TotalPartsCost + estimate.TotalLaborCost + estimate.TaxAmount - estimate.DiscountAmount;

            // Job advances to InProgress only if not cancelled
            if (estimate.JobCard != null && estimate.JobCard.Status != AppointmentStatus.Cancelled && estimate.JobCard.Appointment?.Status != AppointmentStatus.Cancelled)
            {
                estimate.JobCard.Status = AppointmentStatus.InProgress;
                if (!estimate.JobCard.WorkStartedAt.HasValue)
                {
                    estimate.JobCard.WorkStartedAt = DateTime.UtcNow;
                }
                if (estimate.JobCard.Appointment != null && estimate.JobCard.Appointment.Status != AppointmentStatus.Cancelled)
                {
                    estimate.JobCard.Appointment.Status = AppointmentStatus.InProgress;
                    await _unitOfWork.Repository<ServiceAppointment>().UpdateAsync(estimate.JobCard.Appointment);
                }
                await _unitOfWork.Repository<ServiceJobCard>().UpdateAsync(estimate.JobCard);
            }
        }
        else if (response.Action == EstimateApprovalStatus.ClarificationRequested)
        {
            estimate.ApprovalStatus = EstimateApprovalStatus.ClarificationRequested;
            estimate.ClarificationQuestion = response.ClarificationQuestion;
        }

        await _unitOfWork.Repository<RepairEstimate>().UpdateAsync(estimate);
        await _unitOfWork.SaveChangesAsync();

        if (estimate.JobCard?.ServiceAdvisorId != null)
        {
            await _notificationService.SendNotificationAsync(
                estimate.JobCard.ServiceAdvisorId,
                "Customer Response to Estimate",
                $"Customer updated estimate {estimate.EstimateNumber} to '{estimate.ApprovalStatus}'.",
                NotificationType.Estimate,
                $"/ServiceAdvisor/JobCardDetails/{estimate.JobCardId}");
        }

        return (await GetEstimateByIdAsync(estimate.Id))!;
    }

    public async Task ClarifyEstimateAsync(int estimateId, string clarificationQuestion)
    {
        var estimate = await _unitOfWork.Repository<RepairEstimate>().Query()
            .Include(e => e.JobCard).ThenInclude(j => j!.Appointment)
            .FirstOrDefaultAsync(e => e.Id == estimateId);

        if (estimate == null) throw new DomainException("Estimate not found.");

        if (estimate.JobCard != null && (estimate.JobCard.Status == AppointmentStatus.Cancelled || estimate.JobCard.Appointment?.Status == AppointmentStatus.Cancelled))
        {
            throw new DomainException("Cannot request clarification for a cancelled job card or appointment.");
        }

        estimate.ClarificationQuestion = clarificationQuestion;
        estimate.ApprovalStatus = EstimateApprovalStatus.ClarificationRequested;
        await _unitOfWork.Repository<RepairEstimate>().UpdateAsync(estimate);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ReplyEstimateClarificationAsync(int estimateId, string advisorReply)
    {
        var estimate = await _unitOfWork.Repository<RepairEstimate>().Query()
            .Include(e => e.JobCard).ThenInclude(j => j!.Appointment)
            .FirstOrDefaultAsync(e => e.Id == estimateId);

        if (estimate == null) throw new DomainException("Estimate not found.");

        if (estimate.JobCard != null && (estimate.JobCard.Status == AppointmentStatus.Cancelled || estimate.JobCard.Appointment?.Status == AppointmentStatus.Cancelled))
        {
            throw new DomainException("Cannot reply to clarification for a cancelled job card or appointment.");
        }

        estimate.AdvisorReply = advisorReply;
        estimate.ApprovalStatus = EstimateApprovalStatus.Pending; // Back to pending customer action
        await _unitOfWork.Repository<RepairEstimate>().UpdateAsync(estimate);
        await _unitOfWork.SaveChangesAsync();

        if (estimate.JobCard?.Appointment?.CustomerId != null)
        {
            await _notificationService.SendNotificationAsync(
                estimate.JobCard.Appointment.CustomerId,
                "Service Advisor Clarification",
                $"Advisor replied to your question regarding estimate {estimate.EstimateNumber}: \"{advisorReply}\"",
                NotificationType.Estimate,
                $"/Customer/ViewEstimate/{estimate.Id}");
        }
    }

    private static RepairEstimateDto MapToDto(RepairEstimate e)
    {
        return new RepairEstimateDto
        {
            Id = e.Id,
            EstimateNumber = e.EstimateNumber,
            JobCardId = e.JobCardId,
            JobCardNumber = e.JobCard?.JobCardNumber ?? string.Empty,
            VehicleInfo = e.JobCard?.Vehicle != null ? $"{e.JobCard.Vehicle.Make} {e.JobCard.Vehicle.Model} ({e.JobCard.Vehicle.RegistrationNumber})" : string.Empty,
            CustomerId = e.JobCard?.Appointment?.CustomerId ?? string.Empty,
            CustomerName = e.JobCard?.Appointment?.Customer?.FullName ?? "Customer",
            CustomerEmail = e.JobCard?.Appointment?.Customer?.Email ?? string.Empty,
            PreparedByUserId = e.PreparedByUserId,
            PreparedByName = e.PreparedByUser?.FullName ?? "Service Advisor",
            TotalPartsCost = e.TotalPartsCost,
            TotalLaborCost = e.TotalLaborCost,
            TaxAmount = e.TaxAmount,
            DiscountAmount = e.DiscountAmount,
            GrandTotal = e.GrandTotal,
            ApprovalStatus = e.ApprovalStatus,
            CustomerNotes = e.CustomerNotes,
            CustomerRespondedAt = e.CustomerRespondedAt,
            ClarificationQuestion = e.ClarificationQuestion,
            AdvisorReply = e.AdvisorReply,
            JobCardStatus = e.JobCard?.Status,
            AppointmentStatus = e.JobCard?.Appointment?.Status,
            Items = e.Items?.Select(i => new EstimateItemDto
            {
                Id = i.Id,
                RepairEstimateId = i.RepairEstimateId,
                Description = i.Description,
                ItemType = i.ItemType,
                InventoryPartId = i.InventoryPartId,
                PartNumber = i.InventoryPart?.PartNumber,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                LaborCharges = i.LaborCharges,
                TaxPercent = i.TaxPercent,
                LineTotal = i.LineTotal,
                IsApprovedByCustomer = i.IsApprovedByCustomer,
                IsOptional = i.IsOptional,
                Reason = i.Reason
            }).ToList() ?? new()
        };
    }
}