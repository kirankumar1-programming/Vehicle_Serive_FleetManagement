using VehicleService.Domain.Enums;

namespace VehicleService.Application.DTOs;

public class InvoiceDto
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int JobCardId { get; set; }
    public string JobCardNumber { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int VehicleId { get; set; }
    public string VehicleInfo { get; set; } = string.Empty;
    public string RegistrationNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal ServicesTotal { get; set; }
    public decimal PartsTotal { get; set; }
    public decimal LaborTotal { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceDue => GrandTotal - PaidAmount;
    public PaymentStatus Status { get; set; }
    public string? Notes { get; set; }
    public List<InvoiceItemDto> Items { get; set; } = new();
    public List<PaymentTransactionDto> Payments { get; set; } = new();
}

public class InvoiceItemDto
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public decimal LineTotal { get; set; }
}

public class PaymentRequestDto
{
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.CreditCard;
    public string? CardNumber { get; set; }
    public string? CardHolderName { get; set; }
    public string? ExpiryDate { get; set; }
    public string? CVV { get; set; }
    public string? UpiId { get; set; }
    public string? TransactionReference { get; set; } // Optional client idempotency key
}

public class PaymentResultDto
{
    public bool Success { get; set; }
    public string? TransactionReference { get; set; }
    public string? GatewayTransactionId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class PaymentTransactionDto
{
    public int Id { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public string? GatewayTransactionId { get; set; }
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime ProcessedAt { get; set; }
}

public class WarrantyDto
{
    public int Id { get; set; }
    public string WarrantyNumber { get; set; } = string.Empty;
    public WarrantyType Type { get; set; }
    public int VehicleId { get; set; }
    public string VehicleInfo { get; set; } = string.Empty;
    public string RegistrationNumber { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string CoverageTerms { get; set; } = string.Empty;
    public string CoveredItemsSummary { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int MaxMileageLimit { get; set; }
    public WarrantyStatus Status { get; set; }
    public bool IsCurrentlyValid { get; set; }
    public List<WarrantyClaimDto> Claims { get; set; } = new();
}

public class WarrantyClaimDto
{
    public int Id { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public int WarrantyId { get; set; }
    public string WarrantyNumber { get; set; } = string.Empty;
    public int? JobCardId { get; set; }
    public string ClaimedByCustomerId { get; set; } = string.Empty;
    public string IssueDescription { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public decimal AmountClaimed { get; set; }
    public decimal AmountApproved { get; set; }
    public WarrantyClaimStatus Status { get; set; }
    public string? ReviewNotes { get; set; }
}

public class WarrantyEligibilityResultDto
{
    public bool IsEligible { get; set; }
    public string? Reason { get; set; }
    public int? WarrantyId { get; set; }
    public string? WarrantyNumber { get; set; }
    public decimal MaxCoverageAmount { get; set; }
}
