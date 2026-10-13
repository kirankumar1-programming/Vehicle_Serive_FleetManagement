using System.Diagnostics;
using System.Security.Claims;

namespace VehicleService.API.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isHealthCheck = path.StartsWith("/health", StringComparison.OrdinalIgnoreCase);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            var method = context.Request.Method;
            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Anonymous";

            if (isHealthCheck && statusCode < 400)
            {
                // Health checks that pass can be logged at Debug/Trace level to avoid noise
                _logger.LogDebug("Health Check {Path} responded {StatusCode} in {ElapsedMs}ms", path, statusCode, elapsedMs);
            }
            else if (statusCode >= 500)
            {
                _logger.LogError("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms [User: {UserId}, IP: {IpAddress}]",
                    method, path, statusCode, elapsedMs, userId, ipAddress);
            }
            else if (statusCode >= 400)
            {
                _logger.LogWarning("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms [User: {UserId}, IP: {IpAddress}]",
                    method, path, statusCode, elapsedMs, userId, ipAddress);
            }
            else
            {
                _logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms [User: {UserId}, IP: {IpAddress}]",
                    method, path, statusCode, elapsedMs, userId, ipAddress);
            }
        }
    }
}
