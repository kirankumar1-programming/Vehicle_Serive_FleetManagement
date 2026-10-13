using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Domain.Exceptions;

namespace VehicleService.API.Middleware;

public class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title) = MapException(exception);

        var isServerError = statusCode >= 500;
        var correlationId = context.TraceIdentifier;

        if (isServerError)
        {
            _logger.LogError(exception, "Unhandled Exception ({StatusCode}): {Message} | CorrelationId: {CorrelationId} | Path: {Path}",
                statusCode, exception.Message, correlationId, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("Handled Domain/Client Exception ({StatusCode}): {Message} | CorrelationId: {CorrelationId} | Path: {Path}",
                statusCode, exception.Message, correlationId, context.Request.Path);
        }

        var detail = (!isServerError || _environment.IsDevelopment())
            ? exception.Message
            : "An unexpected error occurred while processing your request. Please contact support with the correlation ID.";

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Type = $"https://httpstatuses.io/{statusCode}"
        };

        problemDetails.Extensions["correlationId"] = correlationId;
        problemDetails.Extensions["traceId"] = correlationId;
        problemDetails.Extensions["timestamp"] = DateTime.UtcNow;

        if (_environment.IsDevelopment() && exception.StackTrace != null)
        {
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, options));
    }

    private static (int StatusCode, string Title) MapException(Exception exception) => exception switch
    {
        EntityNotFoundException => (StatusCodes.Status404NotFound, "Resource Not Found"),
        KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource Not Found"),

        DoubleBookingException => (StatusCodes.Status409Conflict, "Double Booking Conflict"),
        DuplicatePaymentException => (StatusCodes.Status409Conflict, "Duplicate Payment Conflict"),
        MechanicUnavailableException => (StatusCodes.Status409Conflict, "Mechanic Unavailable"),

        InsufficientStockException => (StatusCodes.Status422UnprocessableEntity, "Insufficient Inventory Stock"),
        InvalidStateTransitionException => (StatusCodes.Status422UnprocessableEntity, "Invalid State Transition"),

        WarrantyEligibilityException => (StatusCodes.Status400BadRequest, "Warranty Ineligible"),
        ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request"),
        DomainException => (StatusCodes.Status400BadRequest, "Domain Business Rule Violation"),

        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden Access"),

        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
    };
}
