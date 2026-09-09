using VehicleService.Application.DTOs;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;

namespace VehicleService.Application.Interfaces;

public interface IVehicleService
{
    Task<IReadOnlyList<VehicleDto>> GetCustomerVehiclesAsync(string customerId);
    Task<IReadOnlyList<VehicleDto>> GetFleetVehiclesAsync(int fleetId);
    Task<IReadOnlyList<VehicleDto>> GetAllVehiclesAsync();
    Task<VehicleDto?> GetVehicleByIdAsync(int id);
    Task<VehicleDto?> GetVehicleByRegistrationAsync(string regNumber);
    Task<VehicleDto> RegisterVehicleAsync(CreateVehicleDto dto);
    Task UpdateVehicleAsync(int id, CreateVehicleDto dto);
    Task UpdateMileageAsync(int vehicleId, int newMileage);
    Task AssignDriverAsync(DriverAssignmentDto dto);
}

public interface IAppointmentService
{
    Task<IReadOnlyList<AppointmentDto>> GetCustomerAppointmentsAsync(string customerId);
    Task<IReadOnlyList<AppointmentDto>> GetCenterAppointmentsAsync(int centerId, DateTime? date = null);
    Task<IReadOnlyList<AppointmentDto>> GetAllAppointmentsAsync();
    Task<AppointmentDto?> GetAppointmentByIdAsync(int id);
    Task<IReadOnlyList<BayAvailabilityDto>> GetAvailableBaysAsync(int centerId, DateTime date, string timeSlot);
    // Scenario 1: Double Booking Prevention
    Task<AppointmentDto> BookAppointmentAsync(BookAppointmentDto dto);
    // Scenario 5: Service Cancellation (Releases slot and reserved parts)
    Task<bool> CancelAppointmentAsync(int appointmentId, string reason, string cancelledByUserId);
}

public interface IJobCardService
{
    Task<IReadOnlyList<JobCardDto>> GetJobCardsAsync(AppointmentStatus? status = null, int? centerId = null);
    Task<IReadOnlyList<JobCardDto>> GetMechanicJobCardsAsync(string mechanicId);
    Task<JobCardDto?> GetJobCardByIdAsync(int id);
    Task<JobCardDto> CreateJobCardFromAppointmentAsync(int appointmentId, string advisorId, int? bayId = null);
    // Scenario 6: Mechanic Availability Validation
    Task AssignMechanicAsync(int jobCardId, string mechanicId);
    Task UpdateJobCardStatusAsync(int jobCardId, AppointmentStatus newStatus, string? notes = null);
    Task AddWorkLogAsync(int jobCardId, string mechanicId, string taskDesc, decimal hours, string? observations);
    Task RecordReplacedPartAsync(int jobCardId, int partId, int quantity, string loggedByUserId);
}

public interface IInspectionService
{
    Task<VehicleInspection?> GetInspectionByJobCardIdAsync(int jobCardId);
    Task<VehicleInspection> SaveInspectionAsync(VehicleInspection inspection);
    Task AddInspectionPhotoAsync(int inspectionId, string photoUrl, string title, string category);
}

public interface IEstimateService
{
    Task<RepairEstimateDto?> GetEstimateByIdAsync(int id);
    Task<RepairEstimateDto?> GetEstimateByJobCardIdAsync(int jobCardId);
    Task<RepairEstimateDto> CreateEstimateAsync(int jobCardId, string advisorId, List<EstimateItemDto> items);
    // Scenario 4: Customer Approval / Rejection Handling
    Task<RepairEstimateDto> ProcessCustomerEstimateResponseAsync(CustomerEstimateResponseDto response);
    Task ClarifyEstimateAsync(int estimateId, string clarificationQuestion);
    Task ReplyEstimateClarificationAsync(int estimateId, string advisorReply);
}

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryPartDto>> GetAllPartsAsync(int? centerId = null, string? search = null, string? category = null);
    Task<IReadOnlyList<InventoryPartDto>> GetLowStockPartsAsync(int? centerId = null);
    Task<InventoryPartDto?> GetPartByIdAsync(int id);
    Task<InventoryPartDto> CreatePartAsync(InventoryPartDto dto);
    Task UpdatePartAsync(int id, InventoryPartDto dto);
    // Scenario 2: Concurrency-Safe Last Spare Part Reservation & Consumption
    Task<bool> ReservePartAsync(int partId, int quantity, int appointmentId);
    Task<bool> ReleaseReservationAsync(int reservationId);
    Task<bool> ConsumePartAsync(int partId, int quantity, int jobCardId, string performedByUserId);
    Task<bool> AdjustStockAsync(AdjustStockDto dto);
    Task<IReadOnlyList<InventoryTransaction>> GetTransactionsAsync(int? partId = null);
}

public interface IPaymentService
{
    // Scenario 3: Idempotent Payment Callback & Processing
    Task<PaymentResultDto> ProcessPaymentAsync(PaymentRequestDto request);
    Task<PaymentResultDto> ProcessCallbackAsync(string transactionReference, string gatewayTxnId, PaymentStatus status, decimal amount);
    Task<PaymentResultDto> ProcessRefundAsync(int invoiceId, decimal amount, string reason, string processedByUserId);
}

public interface IInvoiceService
{
    Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(PaymentStatus? status = null, string? customerId = null);
    Task<InvoiceDto?> GetInvoiceByIdAsync(int id);
    Task<InvoiceDto?> GetInvoiceByJobCardIdAsync(int jobCardId);
    Task<InvoiceDto> GenerateInvoiceFromJobCardAsync(int jobCardId);
}

public interface IWarrantyService
{
    Task<IReadOnlyList<WarrantyDto>> GetVehicleWarrantiesAsync(int vehicleId);
    Task<WarrantyDto> RegisterWarrantyAsync(Warranty warranty);
    // Scenario 7: Warranty Coverage Eligibility Calculation
    Task<WarrantyEligibilityResultDto> CheckWarrantyEligibilityAsync(int vehicleId, string componentOrServiceName, int currentMileage);
    Task<WarrantyClaimDto> SubmitClaimAsync(int warrantyId, int? jobCardId, string customerId, string issueDesc, string component, decimal amount);
    Task<WarrantyClaimDto> ReviewClaimAsync(int claimId, WarrantyClaimStatus status, decimal approvedAmount, string reviewNotes, string reviewerUserId);
}

public interface IFleetService
{
    Task<FleetDashboardDto> GetFleetDashboardAsync(int fleetId);
    Task<IReadOnlyList<CompanyFleet>> GetAllFleetsAsync();
    Task<CompanyFleet?> GetFleetByIdAsync(int fleetId);
    Task<CompanyFleet> RegisterFleetAsync(CompanyFleet fleet);
}

public interface IRoadsideService
{
    Task<IReadOnlyList<RoadsideRequestDto>> GetAllRequestsAsync(RoadsideStatus? status = null);
    Task<IReadOnlyList<RoadsideRequestDto>> GetCustomerRequestsAsync(string customerId);
    Task<RoadsideRequestDto?> GetRequestByIdAsync(int id);
    Task<RoadsideRequestDto> CreateRequestAsync(CreateRoadsideRequestDto dto);
    Task<RoadsideRequestDto> UpdateRequestStatusAsync(int requestId, RoadsideStatus status, string? notes = null, string? techName = null, string? techPhone = null);
}

public interface IPreventiveMaintenanceService
{
    Task<IReadOnlyList<VehicleDto>> GetVehiclesDueForMaintenanceAsync(int? fleetId = null);
    Task CheckAndScheduleMaintenanceRemindersAsync();
}

public interface IReviewAndComplaintService
{
    Task<CustomerReviewDto> SubmitReviewAsync(int jobCardId, string customerId, int rating, int qualityScore, int staffScore, int cleanScore, string comments);
    Task<IReadOnlyList<CustomerReviewDto>> GetApprovedReviewsAsync();
    Task<IReadOnlyList<CustomerReviewDto>> GetAllReviewsForModerationAsync();
    Task ModerateReviewAsync(int reviewId, ReviewStatus status, string? notes);

    Task<CustomerComplaintDto> RaiseComplaintAsync(string customerId, string subject, string description, int? vehicleId = null, int? jobCardId = null);
    Task<IReadOnlyList<CustomerComplaintDto>> GetComplaintsAsync(ComplaintStatus? status = null, string? customerId = null);
    Task ResolveComplaintAsync(int complaintId, string resolutionNotes, string resolvedByUserId);
}

public interface IReportService
{
    Task<RevenueReportDto> GetRevenueReportAsync(DateTime? startDate = null, DateTime? endDate = null);
    Task<ServiceMetricsDto> GetServiceMetricsAsync(DateTime? startDate = null, DateTime? endDate = null);
    Task<DashboardMetricsDto> GetAdminDashboardMetricsAsync();
    Task<byte[]> ExportRevenueReportCsvAsync(DateTime? startDate = null, DateTime? endDate = null);
    Task<byte[]> ExportInventoryCsvAsync();
}

public interface IAuditService
{
    Task LogAsync(string? userId, string? userName, string action, string entityName, string? entityId, string? oldValues, string? newValues);
    Task<IReadOnlyList<AuditLogDto>> GetLogsAsync(int take = 100);
}

public interface INotificationService
{
    Task SendNotificationAsync(string userId, string title, string message, NotificationType type, string? actionUrl = null);
    Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(string userId, bool unreadOnly = false);
    Task MarkAsReadAsync(int notificationId);
    Task MarkAllAsReadAsync(string userId);
}

public interface IBlobStorageService
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType);
    Task DeleteFileAsync(string fileUrl);
}

public interface IJwtTokenService
{
    string GenerateToken(ApplicationUser user, IList<string> roles);
}
