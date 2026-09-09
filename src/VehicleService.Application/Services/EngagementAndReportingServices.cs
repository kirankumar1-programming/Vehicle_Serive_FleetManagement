using Microsoft.EntityFrameworkCore;
using System.Text;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Application.Services;

public class ReviewAndComplaintService : IReviewAndComplaintService
{
    private readonly IUnitOfWork _unitOfWork;

    public ReviewAndComplaintService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CustomerReviewDto> SubmitReviewAsync(int jobCardId, string customerId, int rating, int qualityScore, int staffScore, int cleanScore, string comments)
    {
        var review = new CustomerReview
        {
            JobCardId = jobCardId,
            CustomerId = customerId,
            OverallRating = Math.Clamp(rating, 1, 5),
            ServiceQualityScore = Math.Clamp(qualityScore, 1, 5),
            StaffRating = Math.Clamp(staffScore, 1, 5),
            CleanlinessRating = Math.Clamp(cleanScore, 1, 5),
            Comments = comments,
            Status = ReviewStatus.Approved // Auto-approved or moderated
        };

        await _unitOfWork.Repository<CustomerReview>().AddAsync(review);
        await _unitOfWork.SaveChangesAsync();

        return new CustomerReviewDto
        {
            Id = review.Id,
            JobCardId = review.JobCardId,
            CustomerId = review.CustomerId,
            OverallRating = review.OverallRating,
            ServiceQualityScore = review.ServiceQualityScore,
            StaffRating = review.StaffRating,
            CleanlinessRating = review.CleanlinessRating,
            Comments = review.Comments,
            Status = review.Status,
            CreatedAt = review.CreatedAt
        };
    }

    public async Task<IReadOnlyList<CustomerReviewDto>> GetApprovedReviewsAsync()
    {
        var list = await _unitOfWork.Repository<CustomerReview>().Query()
            .Include(r => r.Customer)
            .Include(r => r.JobCard)
            .Where(r => r.Status == ReviewStatus.Approved)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return list.Select(r => new CustomerReviewDto
        {
            Id = r.Id,
            JobCardId = r.JobCardId,
            JobCardNumber = r.JobCard?.JobCardNumber ?? string.Empty,
            CustomerId = r.CustomerId,
            CustomerName = r.Customer?.FullName ?? "Verified Customer",
            OverallRating = r.OverallRating,
            ServiceQualityScore = r.ServiceQualityScore,
            StaffRating = r.StaffRating,
            CleanlinessRating = r.CleanlinessRating,
            Comments = r.Comments,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList();
    }

    public async Task<IReadOnlyList<CustomerReviewDto>> GetAllReviewsForModerationAsync()
    {
        var list = await _unitOfWork.Repository<CustomerReview>().Query()
            .Include(r => r.Customer)
            .Include(r => r.JobCard)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return list.Select(r => new CustomerReviewDto
        {
            Id = r.Id,
            JobCardId = r.JobCardId,
            JobCardNumber = r.JobCard?.JobCardNumber ?? string.Empty,
            CustomerId = r.CustomerId,
            CustomerName = r.Customer?.FullName ?? "Customer",
            OverallRating = r.OverallRating,
            ServiceQualityScore = r.ServiceQualityScore,
            StaffRating = r.StaffRating,
            CleanlinessRating = r.CleanlinessRating,
            Comments = r.Comments,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList();
    }

    public async Task ModerateReviewAsync(int reviewId, ReviewStatus status, string? notes)
    {
        var r = await _unitOfWork.Repository<CustomerReview>().GetByIdAsync(reviewId);
        if (r == null) throw new DomainException("Review not found.");

        r.Status = status;
        r.ModerationNotes = notes;
        await _unitOfWork.Repository<CustomerReview>().UpdateAsync(r);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<CustomerComplaintDto> RaiseComplaintAsync(string customerId, string subject, string description, int? vehicleId = null, int? jobCardId = null)
    {
        var ticket = $"CMP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}";
        var complaint = new CustomerComplaint
        {
            TicketNumber = ticket,
            CustomerId = customerId,
            Subject = subject,
            Description = description,
            VehicleId = vehicleId,
            JobCardId = jobCardId,
            Priority = PriorityLevel.Medium,
            Status = ComplaintStatus.Open
        };

        await _unitOfWork.Repository<CustomerComplaint>().AddAsync(complaint);
        await _unitOfWork.SaveChangesAsync();

        return new CustomerComplaintDto
        {
            Id = complaint.Id,
            TicketNumber = complaint.TicketNumber,
            CustomerId = complaint.CustomerId,
            Subject = complaint.Subject,
            Description = complaint.Description,
            VehicleId = complaint.VehicleId,
            JobCardId = complaint.JobCardId,
            Priority = complaint.Priority,
            Status = complaint.Status,
            CreatedAt = complaint.CreatedAt
        };
    }

    public async Task<IReadOnlyList<CustomerComplaintDto>> GetComplaintsAsync(ComplaintStatus? status = null, string? customerId = null)
    {
        var query = _unitOfWork.Repository<CustomerComplaint>().Query()
            .Include(c => c.Customer)
            .Include(c => c.Vehicle)
            .AsQueryable();

        if (status.HasValue) query = query.Where(c => c.Status == status.Value);
        if (!string.IsNullOrEmpty(customerId)) query = query.Where(c => c.CustomerId == customerId);

        var list = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();
        return list.Select(c => new CustomerComplaintDto
        {
            Id = c.Id,
            TicketNumber = c.TicketNumber,
            CustomerId = c.CustomerId,
            CustomerName = c.Customer?.FullName ?? "Customer",
            VehicleId = c.VehicleId,
            VehicleInfo = c.Vehicle != null ? $"{c.Vehicle.Make} {c.Vehicle.Model}" : null,
            JobCardId = c.JobCardId,
            Subject = c.Subject,
            Description = c.Description,
            Priority = c.Priority,
            Status = c.Status,
            ResolutionNotes = c.ResolutionNotes,
            CreatedAt = c.CreatedAt
        }).ToList();
    }

    public async Task ResolveComplaintAsync(int complaintId, string resolutionNotes, string resolvedByUserId)
    {
        var c = await _unitOfWork.Repository<CustomerComplaint>().GetByIdAsync(complaintId);
        if (c == null) throw new DomainException("Complaint not found.");

        c.Status = ComplaintStatus.Resolved;
        c.ResolutionNotes = resolutionNotes;
        c.ResolvedAt = DateTime.UtcNow;
        c.AssignedToUserId = resolvedByUserId;

        await _unitOfWork.Repository<CustomerComplaint>().UpdateAsync(c);
        await _unitOfWork.SaveChangesAsync();
    }
}

public class ReportService : IReportService
{
    private readonly IUnitOfWork _unitOfWork;

    public ReportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<RevenueReportDto> GetRevenueReportAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _unitOfWork.Repository<Invoice>().Query().AsQueryable();
        if (startDate.HasValue) query = query.Where(i => i.IssueDate >= startDate.Value);
        if (endDate.HasValue) query = query.Where(i => i.IssueDate <= endDate.Value);

        var invoices = await query.ToListAsync();

        var paid = invoices.Where(i => i.Status == PaymentStatus.Successful).ToList();
        var pending = invoices.Where(i => i.Status == PaymentStatus.Pending).ToList();

        decimal totalRev = paid.Sum(i => i.PaidAmount);
        decimal partsRev = paid.Sum(i => i.PartsTotal);
        decimal laborRev = paid.Sum(i => i.LaborTotal);
        decimal tax = paid.Sum(i => i.TaxAmount);
        decimal discount = paid.Sum(i => i.DiscountAmount);
        decimal outstanding = pending.Sum(i => i.GrandTotal);

        var monthly = paid
            .GroupBy(i => new { i.IssueDate.Year, i.IssueDate.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new MonthlyRevenueItemDto
            {
                MonthName = $"{g.Key.Year}-{g.Key.Month:D2}",
                Revenue = g.Sum(x => x.PaidAmount),
                ServicesCompleted = g.Count()
            }).ToList();

        return new RevenueReportDto
        {
            TotalRevenue = totalRev,
            PartsRevenue = partsRev,
            LaborRevenue = laborRev,
            TaxCollected = tax,
            TotalDiscounts = discount,
            PaidInvoicesCount = paid.Count,
            PendingInvoicesCount = pending.Count,
            OutstandingAmount = outstanding,
            MonthlyBreakdown = monthly
        };
    }

    public async Task<ServiceMetricsDto> GetServiceMetricsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var jobCards = await _unitOfWork.Repository<ServiceJobCard>().Query()
            .Include(j => j.Appointment).ThenInclude(a => a!.ServiceType)
            .Include(j => j.Mechanic)
            .Include(j => j.Invoices)
            .ToListAsync();

        int total = jobCards.Count;
        int completed = jobCards.Count(j => j.Status == AppointmentStatus.Completed);
        int active = jobCards.Count(j => j.Status != AppointmentStatus.Completed && j.Status != AppointmentStatus.Cancelled);
        decimal avgDuration = jobCards.Where(j => j.TotalLaborHours > 0).Select(j => j.TotalLaborHours).DefaultIfEmpty(0).Average();

        var popular = jobCards
            .Where(j => j.Appointment?.ServiceType != null)
            .GroupBy(j => j.Appointment!.ServiceType!.Name)
            .Select(g => new PopularServiceDto
            {
                ServiceName = g.Key,
                RequestCount = g.Count(),
                TotalRevenue = g.SelectMany(j => j.Invoices).Sum(i => i.GrandTotal)
            })
            .OrderByDescending(p => p.RequestCount)
            .Take(5)
            .ToList();

        var mechanics = jobCards
            .Where(j => j.Mechanic != null)
            .GroupBy(j => j.Mechanic!.FullName)
            .Select(g => new MechanicProductivityDto
            {
                MechanicName = g.Key,
                CompletedJobs = g.Count(j => j.Status == AppointmentStatus.Completed),
                TotalHoursLogged = g.Sum(j => j.TotalLaborHours)
            })
            .OrderByDescending(m => m.CompletedJobs)
            .ToList();

        return new ServiceMetricsDto
        {
            TotalServicesBooked = total,
            TotalCompleted = completed,
            ActiveJobsCount = active,
            AverageServiceDurationHours = avgDuration,
            DelayedJobsCount = 0,
            PopularServices = popular,
            MechanicProductivity = mechanics
        };
    }

    public async Task<DashboardMetricsDto> GetAdminDashboardMetricsAsync()
    {
        int customers = await _unitOfWork.Repository<Vehicle>().Query().Select(v => v.CustomerId).Where(c => c != null).Distinct().CountAsync();
        int vehicles = await _unitOfWork.Repository<Vehicle>().Query().CountAsync();
        int todayAppts = await _unitOfWork.Repository<ServiceAppointment>().Query().CountAsync(a => a.AppointmentDate.Date == DateTime.UtcNow.Date);
        int activeJobs = await _unitOfWork.Repository<ServiceJobCard>().Query().CountAsync(j => j.Status != AppointmentStatus.Completed && j.Status != AppointmentStatus.Cancelled);

        var invoices = await _unitOfWork.Repository<Invoice>().Query().ToListAsync();
        decimal totalRev = invoices.Where(i => i.Status == PaymentStatus.Successful).Sum(i => i.PaidAmount);
        decimal pendingPayments = invoices.Where(i => i.Status == PaymentStatus.Pending).Sum(i => i.GrandTotal);

        int lowStock = await _unitOfWork.Repository<InventoryPart>().Query().CountAsync(p => p.AvailableQuantity <= p.ReorderLevel);
        int complaints = await _unitOfWork.Repository<CustomerComplaint>().Query().CountAsync(c => c.Status != ComplaintStatus.Resolved && c.Status != ComplaintStatus.Closed);
        int availableBays = await _unitOfWork.Repository<ServiceBay>().Query().CountAsync(b => b.Status == BayStatus.Available);
        int activeRoadside = await _unitOfWork.Repository<RoadsideAssistanceRequest>().Query().CountAsync(r => r.Status != RoadsideStatus.IssueResolved && r.Status != RoadsideStatus.Cancelled);

        return new DashboardMetricsDto
        {
            TotalCustomers = customers,
            TotalVehicles = vehicles,
            TodayAppointments = todayAppts,
            ActiveJobs = activeJobs,
            TotalRevenue = totalRev,
            PendingPayments = pendingPayments,
            LowStockPartsCount = lowStock,
            OpenComplaintsCount = complaints,
            AvailableBaysCount = availableBays,
            ActiveRoadsideRequests = activeRoadside
        };
    }

    public async Task<byte[]> ExportRevenueReportCsvAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var invoices = await _unitOfWork.Repository<Invoice>().Query()
            .Include(i => i.Customer)
            .Include(i => i.Vehicle)
            .OrderByDescending(i => i.IssueDate)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Invoice Number,Issue Date,Customer Name,Vehicle,Subtotal,Tax,Discount,Grand Total,Status");

        foreach (var i in invoices)
        {
            sb.AppendLine($"\"{i.InvoiceNumber}\",\"{i.IssueDate:yyyy-MM-dd}\",\"{i.Customer?.FullName}\",\"{i.Vehicle?.Make} {i.Vehicle?.Model} ({i.Vehicle?.RegistrationNumber})\",{i.SubTotal:F2},{i.TaxAmount:F2},{i.DiscountAmount:F2},{i.GrandTotal:F2},\"{i.Status}\"");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> ExportInventoryCsvAsync()
    {
        var parts = await _unitOfWork.Repository<InventoryPart>().Query().OrderBy(p => p.Name).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("Part Number,Part Name,Category,Manufacturer,Cost Price,Selling Price,Available Qty,Reserved Qty,Total Qty,Reorder Level,Warehouse Location");

        foreach (var p in parts)
        {
            sb.AppendLine($"\"{p.PartNumber}\",\"{p.Name}\",\"{p.Category}\",\"{p.Manufacturer}\",{p.CostPrice:F2},{p.SellingPrice:F2},{p.AvailableQuantity},{p.ReservedQuantity},{p.TotalStock},{p.ReorderLevel},\"{p.WarehouseLocation}\"");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}

public class AuditService : IAuditService
{
    private readonly IUnitOfWork _unitOfWork;

    public AuditService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task LogAsync(string? userId, string? userName, string action, string entityName, string? entityId, string? oldValues, string? newValues)
    {
        var log = new AuditLog
        {
            UserId = userId,
            UserName = userName,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            Timestamp = DateTime.UtcNow
        };

        await _unitOfWork.Repository<AuditLog>().AddAsync(log);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<AuditLogDto>> GetLogsAsync(int take = 100)
    {
        var list = await _unitOfWork.Repository<AuditLog>().Query()
            .OrderByDescending(l => l.Timestamp)
            .Take(take)
            .ToListAsync();

        return list.Select(l => new AuditLogDto
        {
            Id = l.Id,
            UserId = l.UserId,
            UserName = l.UserName,
            Action = l.Action,
            EntityName = l.EntityName,
            EntityId = l.EntityId,
            OldValues = l.OldValues,
            NewValues = l.NewValues,
            Timestamp = l.Timestamp
        }).ToList();
    }
}

public class NotificationService : INotificationService
{
    private readonly IUnitOfWork _unitOfWork;

    public NotificationService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task SendNotificationAsync(string userId, string title, string message, NotificationType type, string? actionUrl = null)
    {
        var n = new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            ActionUrl = actionUrl,
            IsRead = false
        };

        await _unitOfWork.Repository<Notification>().AddAsync(n);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(string userId, bool unreadOnly = false)
    {
        var query = _unitOfWork.Repository<Notification>().Query().Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);

        var list = await query.OrderByDescending(n => n.CreatedAt).Take(20).ToListAsync();
        return list.Select(n => new NotificationDto
        {
            Id = n.Id,
            Title = n.Title,
            Message = n.Message,
            Type = n.Type,
            ActionUrl = n.ActionUrl,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt
        }).ToList();
    }

    public async Task MarkAsReadAsync(int notificationId)
    {
        var n = await _unitOfWork.Repository<Notification>().GetByIdAsync(notificationId);
        if (n != null)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
            await _unitOfWork.Repository<Notification>().UpdateAsync(n);
            await _unitOfWork.SaveChangesAsync();
        }
    }

    public async Task MarkAllAsReadAsync(string userId)
    {
        var unread = await _unitOfWork.Repository<Notification>().FindAsync(n => n.UserId == userId && !n.IsRead);
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
            await _unitOfWork.Repository<Notification>().UpdateAsync(n);
        }
        await _unitOfWork.SaveChangesAsync();
    }
}