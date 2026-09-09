using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Infrastructure.Payment;

public class MockPaymentService : IPaymentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private static readonly object _paymentLock = new();

    public MockPaymentService(IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    // SCENARIO 3: Idempotent Payment Processing & Callback Protection
    public async Task<PaymentResultDto> ProcessPaymentAsync(PaymentRequestDto request)
    {
        var invoice = await _unitOfWork.Repository<Invoice>().Query()
            .Include(i => i.JobCard)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId);

        if (invoice == null) throw new DomainException("Invoice not found.");

        if (invoice.Status == PaymentStatus.Successful && invoice.PaidAmount >= invoice.GrandTotal)
        {
            return new PaymentResultDto
            {
                Success = true,
                Amount = invoice.PaidAmount,
                Status = PaymentStatus.Successful,
                Message = "Invoice is already paid in full."
            };
        }

        string txnRef = !string.IsNullOrEmpty(request.TransactionReference)
            ? request.TransactionReference
            : $"TXN-MOCK-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        lock (_paymentLock)
        {
            // Check for duplicate transaction reference (Idempotency)
            var existingTxn = _unitOfWork.Repository<PaymentTransaction>().Query()
                .FirstOrDefault(p => p.TransactionReference == txnRef);

            if (existingTxn != null)
            {
                return new PaymentResultDto
                {
                    Success = existingTxn.Status == PaymentStatus.Successful,
                    TransactionReference = existingTxn.TransactionReference,
                    GatewayTransactionId = existingTxn.GatewayTransactionId,
                    Amount = existingTxn.Amount,
                    Status = existingTxn.Status,
                    Message = "Existing transaction processed (Idempotent response)."
                };
            }

            var payment = new PaymentTransaction
            {
                InvoiceId = request.InvoiceId,
                TransactionReference = txnRef,
                GatewayTransactionId = $"gtw_mock_{Guid.NewGuid():N}",
                Amount = request.Amount > 0 ? request.Amount : invoice.GrandTotal,
                Method = request.Method,
                Status = PaymentStatus.Successful,
                PayerName = request.CardHolderName ?? invoice.Customer?.FullName ?? "Customer",
                PayerEmail = invoice.Customer?.Email,
                ProcessedAt = DateTime.UtcNow
            };

            _unitOfWork.Repository<PaymentTransaction>().AddAsync(payment).GetAwaiter().GetResult();

            invoice.PaidAmount += payment.Amount;
            invoice.Status = invoice.PaidAmount >= invoice.GrandTotal ? PaymentStatus.Successful : PaymentStatus.Authorized;
            _unitOfWork.Repository<Invoice>().UpdateAsync(invoice).GetAwaiter().GetResult();

            // Advance JobCard to ReadyForDelivery / Completed if applicable
            if (invoice.JobCard != null && invoice.JobCard.Status == AppointmentStatus.ReadyForDelivery)
            {
                invoice.JobCard.Status = AppointmentStatus.Completed;
                _unitOfWork.Repository<ServiceJobCard>().UpdateAsync(invoice.JobCard).GetAwaiter().GetResult();
            }

            _unitOfWork.SaveChangesAsync().GetAwaiter().GetResult();

            return new PaymentResultDto
            {
                Success = true,
                TransactionReference = payment.TransactionReference,
                GatewayTransactionId = payment.GatewayTransactionId,
                Amount = payment.Amount,
                Status = payment.Status,
                Message = "Payment processed successfully."
            };
        }
    }

    // SCENARIO 3: Webhook / Provider Double Callback Handler
    public async Task<PaymentResultDto> ProcessCallbackAsync(string transactionReference, string gatewayTxnId, PaymentStatus status, decimal amount)
    {
        lock (_paymentLock)
        {
            // Idempotency: Check if transaction reference is already recorded
            var existingTxn = _unitOfWork.Repository<PaymentTransaction>().Query()
                .FirstOrDefault(p => p.TransactionReference == transactionReference);

            if (existingTxn != null)
            {
                // Webhook sent twice! Do NOT create a duplicate transaction.
                return new PaymentResultDto
                {
                    Success = existingTxn.Status == PaymentStatus.Successful,
                    TransactionReference = existingTxn.TransactionReference,
                    GatewayTransactionId = existingTxn.GatewayTransactionId,
                    Amount = existingTxn.Amount,
                    Status = existingTxn.Status,
                    Message = "Duplicate callback acknowledged. No new transaction created."
                };
            }

            throw new DomainException($"Payment reference '{transactionReference}' not found for callback processing.");
        }
    }

    public async Task<PaymentResultDto> ProcessRefundAsync(int invoiceId, decimal amount, string reason, string processedByUserId)
    {
        var invoice = await _unitOfWork.Repository<Invoice>().Query()
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) throw new DomainException("Invoice not found.");
        if (invoice.PaidAmount < amount) throw new DomainException("Refund amount exceeds paid amount.");

        var refundRef = $"REF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        var lastPayment = invoice.Payments.LastOrDefault(p => p.Status == PaymentStatus.Successful);

        var refund = new Refund
        {
            RefundReference = refundRef,
            InvoiceId = invoiceId,
            PaymentTransactionId = lastPayment?.Id,
            Amount = amount,
            Reason = reason,
            ProcessedByUserId = processedByUserId,
            ProcessedAt = DateTime.UtcNow,
            Status = "Completed"
        };

        await _unitOfWork.Repository<Refund>().AddAsync(refund);

        invoice.PaidAmount -= amount;
        invoice.Status = invoice.PaidAmount == 0 ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        await _unitOfWork.Repository<Invoice>().UpdateAsync(invoice);

        await _unitOfWork.SaveChangesAsync();

        return new PaymentResultDto
        {
            Success = true,
            TransactionReference = refundRef,
            Amount = amount,
            Status = invoice.Status,
            Message = $"Refund of ₹{amount:N2} processed successfully."
        };
    }
}