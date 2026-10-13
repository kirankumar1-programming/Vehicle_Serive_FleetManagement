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
    private readonly VehicleService.Application.Common.IConcurrencyCoordinator _concurrencyCoordinator;

    public MockPaymentService(
        IUnitOfWork unitOfWork,
        INotificationService notificationService,
        VehicleService.Application.Common.IConcurrencyCoordinator? concurrencyCoordinator = null)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
        _concurrencyCoordinator = concurrencyCoordinator ?? new VehicleService.Application.Common.KeyedLockCoordinator();
    }

    // SCENARIO 3: Idempotent Payment Processing & Callback Protection
    public async Task<PaymentResultDto> ProcessPaymentAsync(PaymentRequestDto request)
    {
        var lockKey = $"payment:invoice:{request.InvoiceId}";
        using (await _concurrencyCoordinator.LockAsync(lockKey))
        {
            // Check for duplicate transaction reference first (Idempotency)
            if (!string.IsNullOrEmpty(request.TransactionReference))
            {
                var existingTxn = await _unitOfWork.Repository<PaymentTransaction>().Query()
                    .FirstOrDefaultAsync(p => p.TransactionReference == request.TransactionReference);

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
            }

            var invoice = await _unitOfWork.Repository<Invoice>().Query()
                .Include(i => i.JobCard).ThenInclude(j => j!.Appointment)
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

            decimal paymentAmount = request.Amount > 0 ? request.Amount : (invoice.GrandTotal - invoice.PaidAmount);

            if (invoice.PaidAmount + paymentAmount > invoice.GrandTotal)
            {
                throw new DuplicatePaymentException($"Payment amount of ₹{paymentAmount:N2} would exceed remaining invoice balance of ₹{(invoice.GrandTotal - invoice.PaidAmount):N2}. Duplicate or excess payment rejected.");
            }

            string txnRef = !string.IsNullOrEmpty(request.TransactionReference)
                ? request.TransactionReference
                : $"TXN-MOCK-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

            var payment = new PaymentTransaction
            {
                InvoiceId = request.InvoiceId,
                TransactionReference = txnRef,
                GatewayTransactionId = $"gtw_mock_{Guid.NewGuid():N}",
                Amount = paymentAmount,
                Method = request.Method,
                Status = PaymentStatus.Successful,
                PayerName = request.CardHolderName ?? invoice.Customer?.FullName ?? "Customer",
                PayerEmail = invoice.Customer?.Email,
                ProcessedAt = DateTime.UtcNow
            };

            await _unitOfWork.Repository<PaymentTransaction>().AddAsync(payment);

            invoice.PaidAmount += payment.Amount;
            invoice.Status = invoice.PaidAmount >= invoice.GrandTotal ? PaymentStatus.Successful : PaymentStatus.Authorized;
            await _unitOfWork.Repository<Invoice>().UpdateAsync(invoice);

            // Synchronize JobCard and Appointment status to Completed upon successful invoice payment
            if (invoice.Status == PaymentStatus.Successful && invoice.JobCard != null)
            {
                invoice.JobCard.Status = AppointmentStatus.Completed;
                if (!invoice.JobCard.WorkCompletedAt.HasValue)
                {
                    invoice.JobCard.WorkCompletedAt = DateTime.UtcNow;
                }
                invoice.JobCard.ServiceBayId = null; // Release service bay if assigned
                await _unitOfWork.Repository<ServiceJobCard>().UpdateAsync(invoice.JobCard);

                if (invoice.JobCard.Appointment != null)
                {
                    invoice.JobCard.Appointment.Status = AppointmentStatus.Completed;
                    invoice.JobCard.Appointment.ServiceBayId = null;
                    await _unitOfWork.Repository<ServiceAppointment>().UpdateAsync(invoice.JobCard.Appointment);
                }
            }

            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Concurrency race: transaction reference already inserted by another concurrent call
                var existing = await _unitOfWork.Repository<PaymentTransaction>().Query()
                    .FirstOrDefaultAsync(p => p.TransactionReference == txnRef);
                if (existing != null)
                {
                    return new PaymentResultDto
                    {
                        Success = existing.Status == PaymentStatus.Successful,
                        TransactionReference = existing.TransactionReference,
                        GatewayTransactionId = existing.GatewayTransactionId,
                        Amount = existing.Amount,
                        Status = existing.Status,
                        Message = "Existing transaction processed (Idempotent response)."
                    };
                }
                throw;
            }

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
        var lockKey = $"payment:callback:{transactionReference}";
        using (await _concurrencyCoordinator.LockAsync(lockKey))
        {
            // Idempotency: Check if transaction reference is already recorded
            var existingTxn = await _unitOfWork.Repository<PaymentTransaction>().Query()
                .FirstOrDefaultAsync(p => p.TransactionReference == transactionReference);

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