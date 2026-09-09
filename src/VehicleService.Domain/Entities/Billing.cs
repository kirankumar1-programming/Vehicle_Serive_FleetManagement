using VehicleService.Domain.Common;
using VehicleService.Domain.Enums;

namespace VehicleService.Domain.Entities;

public class Invoice : BaseEntity
{
    public string InvoiceNumber { get; set; } = string.Empty; // e.g. "INV-2026-0098"

    public int JobCardId { get; set; }
    public virtual ServiceJobCard? JobCard { get; set; }

    public string CustomerId { get; set; } = string.Empty;
    public virtual ApplicationUser? Customer { get; set; }

    public int VehicleId { get; set; }
    public virtual Vehicle? Vehicle { get; set; }

    public DateTime IssueDate { get; set; } = DateTime.UtcNow;
    public DateTime DueDate { get; set; } = DateTime.UtcNow.AddDays(7);

    public decimal ServicesTotal { get; set; }
    public decimal PartsTotal { get; set; }
    public decimal LaborTotal { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? Notes { get; set; }

    public virtual ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
    public virtual ICollection<PaymentTransaction> Payments { get; set; } = new List<PaymentTransaction>();
    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}

public class InvoiceItem : BaseEntity
{
    public int InvoiceId { get; set; }
    public virtual Invoice? Invoice { get; set; }

    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Service"; // Service, Part, Labor, Surcharge
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; } = 18.0m;
    public decimal LineTotal { get; set; }
}

public class PaymentTransaction : BaseEntity
{
    public string TransactionReference { get; set; } = string.Empty; // Unique reference for idempotency
    public string? GatewayTransactionId { get; set; }

    public int InvoiceId { get; set; }
    public virtual Invoice? Invoice { get; set; }

    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.CreditCard;
    public PaymentStatus Status { get; set; } = PaymentStatus.Successful;

    public string? PayerName { get; set; }
    public string? PayerEmail { get; set; }
    public string? PayerPhone { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public class Refund : BaseEntity
{
    public string RefundReference { get; set; } = string.Empty;
    public int InvoiceId { get; set; }
    public virtual Invoice? Invoice { get; set; }

    public int? PaymentTransactionId { get; set; }
    public virtual PaymentTransaction? PaymentTransaction { get; set; }

    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ProcessedByUserId { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Completed";
}
