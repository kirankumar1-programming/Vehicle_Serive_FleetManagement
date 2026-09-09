namespace VehicleService.Domain.Enums;

public enum UserRoleType
{
    Customer,
    ServiceAdvisor,
    Mechanic,
    InventoryManager,
    FinanceManager,
    FleetManager,
    Administrator
}

public enum VehicleFuelType
{
    Petrol,
    Diesel,
    Electric,
    Hybrid,
    CNG
}

public enum TransmissionType
{
    Manual,
    Automatic,
    CVT,
    DualClutch
}

public enum AppointmentStatus
{
    Pending,
    Confirmed,
    VehicleReceived,
    UnderInspection,
    EstimateCreated,
    EstimateApproved,
    EstimateRejected,
    InProgress,
    QualityCheck,
    ReadyForDelivery,
    Completed,
    Cancelled
}

public enum BayStatus
{
    Available,
    Occupied,
    Maintenance
}

public enum EstimateApprovalStatus
{
    Pending,
    Approved,
    PartiallyApproved,
    Rejected,
    ClarificationRequested
}

public enum EstimateItemType
{
    Part,
    Labor,
    Diagnostic,
    Consumable
}

public enum InventoryTransactionType
{
    Purchase,
    Consumption,
    Return,
    Damage,
    Adjustment,
    Transfer
}

public enum PaymentStatus
{
    Pending,
    Authorized,
    Successful,
    Failed,
    Refunded,
    PartiallyRefunded
}

public enum PaymentMethod
{
    CreditCard,
    DebitCard,
    UPI,
    NetBanking,
    CorporateAccount,
    Cash
}

public enum WarrantyType
{
    Vehicle,
    Service,
    SparePart,
    Repair
}

public enum WarrantyStatus
{
    Active,
    Expired,
    Voided
}

public enum WarrantyClaimStatus
{
    Submitted,
    UnderReview,
    Approved,
    Rejected,
    Settled
}

public enum RoadsideRequestType
{
    Breakdown,
    FlatTyre,
    BatteryFailure,
    Accident,
    FuelIssue,
    Towing
}

public enum RoadsideStatus
{
    Requested,
    AgentAssigned,
    TechnicianDispatched,
    TechnicianArrived,
    IssueResolved,
    Cancelled
}

public enum ComplaintStatus
{
    Open,
    InReview,
    Resolved,
    Closed
}

public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public enum NotificationType
{
    Appointment,
    Estimate,
    JobProgress,
    Invoice,
    Payment,
    Warranty,
    MaintenanceDue,
    Roadside,
    Inventory,
    System
}

public enum DiscountType
{
    Percentage,
    FixedAmount,
    CustomerTier,
    FleetDiscount,
    Seasonal,
    Coupon
}

public enum PriorityLevel
{
    Low,
    Medium,
    High,
    Emergency
}
