using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using VehicleService.API.Middleware;
using Xunit;

namespace VehicleService.Tests;

public class CorrelationIdMiddlewareTests
{
    private readonly Mock<ILogger<CorrelationIdMiddleware>> _mockLogger;

    public CorrelationIdMiddlewareTests()
    {
        _mockLogger = new Mock<ILogger<CorrelationIdMiddleware>>();
    }

    [Fact]
    public async Task InvokeAsync_WhenHeaderPresent_UsesExistingCorrelationId()
    {
        // Arrange
        var context = new DefaultHttpContext();
        const string customCorrelationId = "client-trace-abc-12345";
        context.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader] = customCorrelationId;

        string? capturedTraceId = null;
        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            capturedTraceId = ctx.TraceIdentifier;
            return Task.CompletedTask;
        }, _mockLogger.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedTraceId.Should().Be(customCorrelationId);
        context.TraceIdentifier.Should().Be(customCorrelationId);
    }

    [Fact]
    public async Task InvokeAsync_WhenHeaderMissing_GeneratesNewCorrelationId()
    {
        // Arrange
        var context = new DefaultHttpContext();

        string? capturedTraceId = null;
        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            capturedTraceId = ctx.TraceIdentifier;
            return Task.CompletedTask;
        }, _mockLogger.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedTraceId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(capturedTraceId, out _).Should().BeTrue("Generated correlation ID must be a valid GUID");
    }

    [Fact]
    public async Task InvokeAsync_AppendsCorrelationIdHeaderToResponse()
    {
        // Arrange
        var context = new DefaultHttpContext();
        const string customCorrelationId = "my-service-correlation-id-99";
        context.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader] = customCorrelationId;

        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            // Simulate response start callback
            return Task.CompletedTask;
        }, _mockLogger.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Fire response starting callbacks
        context.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeader] = context.TraceIdentifier;

        // Assert
        context.Response.Headers.Should().ContainKey(CorrelationIdMiddleware.CorrelationIdHeader);
        context.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString().Should().Be(customCorrelationId);
    }
}
