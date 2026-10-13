using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Enums;

namespace VehicleService.API.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsApiController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentsApiController> _logger;

    public PaymentsApiController(IPaymentService paymentService, ILogger<PaymentsApiController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<PaymentResultDto>> ProcessPayment([FromBody] PaymentRequestDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        _logger.LogInformation("Processing payment for Invoice {InvoiceId}, Amount {Amount}, Method {Method}",
            dto.InvoiceId, dto.Amount, dto.Method);

        var result = await _paymentService.ProcessPaymentAsync(dto);

        _logger.LogInformation("Payment for Invoice {InvoiceId} finished with Status: {Status}, Ref: {Ref}",
            dto.InvoiceId, result.Status, result.TransactionReference);

        return Ok(result);
    }

    [HttpPost("callback")]
    [AllowAnonymous]
    public async Task<ActionResult<PaymentResultDto>> ProcessCallback(
        [FromQuery] string transactionReference,
        [FromQuery] string gatewayTxnId,
        [FromQuery] PaymentStatus status,
        [FromQuery] decimal amount)
    {
        if (string.IsNullOrWhiteSpace(transactionReference) || string.IsNullOrWhiteSpace(gatewayTxnId))
            return BadRequest("Transaction reference and gateway transaction ID are required.");

        if (amount < 0)
            return BadRequest("Amount cannot be negative.");

        var result = await _paymentService.ProcessCallbackAsync(transactionReference, gatewayTxnId, status, amount);
        return Ok(result);
    }

    [HttpPost("refund")]
    [Authorize(Roles = UserRoles.FinanceOrAdmin)]
    public async Task<ActionResult<PaymentResultDto>> ProcessRefund(
        [FromQuery] int invoiceId,
        [FromQuery] decimal amount,
        [FromQuery] string reason)
    {
        if (invoiceId <= 0)
            return BadRequest("Valid Invoice ID is required.");

        if (amount <= 0)
            return BadRequest("Refund amount must be greater than zero.");

        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest("Refund reason is required.");

        var processedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "FinanceManager";
        var result = await _paymentService.ProcessRefundAsync(invoiceId, amount, reason, processedBy);
        return Ok(result);
    }
}
