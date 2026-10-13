using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using VehicleService.API.Middleware;
using VehicleService.Domain.Exceptions;
using Xunit;

namespace VehicleService.Tests;

public class GlobalExceptionHandlingMiddlewareTests
{
    private readonly Mock<ILogger<GlobalExceptionHandlingMiddleware>> _mockLogger;
    private readonly Mock<IHostEnvironment> _mockEnvironment;

    public GlobalExceptionHandlingMiddlewareTests()
    {
        _mockLogger = new Mock<ILogger<GlobalExceptionHandlingMiddleware>>();
        _mockEnvironment = new Mock<IHostEnvironment>();
        _mockEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
    }

    private static (HttpContext Context, MemoryStream BodyStream) CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;
        context.TraceIdentifier = "test-correlation-id-12345";
        return (context, bodyStream);
    }

    private static async Task<ProblemDetails?> ReadProblemDetailsAsync(MemoryStream bodyStream)
    {
        bodyStream.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<ProblemDetails>(bodyStream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    [Fact]
    public async Task InvokeAsync_WhenEntityNotFoundException_Returns404ProblemDetails()
    {
        // Arrange
        var (context, stream) = CreateHttpContext();
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new EntityNotFoundException("Appointment with id 99 was not found"),
            _mockLogger.Object,
            _mockEnvironment.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.ContentType.Should().Contain("application/problem+json");

        var problem = await ReadProblemDetailsAsync(stream);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(404);
        problem.Title.Should().Be("Resource Not Found");
        problem.Detail.Should().Be("Appointment with id 99 was not found");
        problem.Extensions.Should().ContainKey("correlationId");
        problem.Extensions["correlationId"]?.ToString().Should().Be("test-correlation-id-12345");
    }

    [Fact]
    public async Task InvokeAsync_WhenDoubleBookingException_Returns409Conflict()
    {
        // Arrange
        var (context, stream) = CreateHttpContext();
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new DoubleBookingException("Service Bay 1 is already reserved for this timeslot"),
            _mockLogger.Object,
            _mockEnvironment.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = await ReadProblemDetailsAsync(stream);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(409);
        problem.Title.Should().Be("Double Booking Conflict");
        problem.Detail.Should().Contain("already reserved");
    }

    [Fact]
    public async Task InvokeAsync_WhenInsufficientStockException_Returns422UnprocessableEntity()
    {
        // Arrange
        var (context, stream) = CreateHttpContext();
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new InsufficientStockException("Insufficient inventory for Oil Filter (available: 0, requested: 2)"),
            _mockLogger.Object,
            _mockEnvironment.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        var problem = await ReadProblemDetailsAsync(stream);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(422);
        problem.Title.Should().Be("Insufficient Inventory Stock");
    }

    [Fact]
    public async Task InvokeAsync_WhenInvalidStateTransitionException_Returns422UnprocessableEntity()
    {
        // Arrange
        var (context, stream) = CreateHttpContext();
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new InvalidStateTransitionException("Cannot move Job Card from Created to Completed directly"),
            _mockLogger.Object,
            _mockEnvironment.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        var problem = await ReadProblemDetailsAsync(stream);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(422);
        problem.Title.Should().Be("Invalid State Transition");
    }

    [Fact]
    public async Task InvokeAsync_WhenArgumentException_Returns400BadRequest()
    {
        // Arrange
        var (context, stream) = CreateHttpContext();
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new ArgumentException("Invalid vehicle VIN format"),
            _mockLogger.Object,
            _mockEnvironment.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = await ReadProblemDetailsAsync(stream);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(400);
        problem.Title.Should().Be("Bad Request");
    }

    [Fact]
    public async Task InvokeAsync_WhenUnauthorizedAccessException_Returns403Forbidden()
    {
        // Arrange
        var (context, stream) = CreateHttpContext();
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new UnauthorizedAccessException("Customer cannot access advisor records"),
            _mockLogger.Object,
            _mockEnvironment.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var problem = await ReadProblemDetailsAsync(stream);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(403);
        problem.Title.Should().Be("Forbidden Access");
    }

    [Fact]
    public async Task InvokeAsync_WhenUnhandledExceptionInProduction_Returns500AndSanitizesMessage()
    {
        // Arrange
        var (context, stream) = CreateHttpContext();
        _mockEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Fatal database disk corruption"),
            _mockLogger.Object,
            _mockEnvironment.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = await ReadProblemDetailsAsync(stream);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(500);
        problem.Title.Should().Be("Internal Server Error");
        // Must NOT leak internal database details
        problem.Detail.Should().NotContain("Fatal database disk corruption");
        problem.Detail.Should().Contain("unexpected error occurred");
    }

    [Fact]
    public async Task InvokeAsync_WhenUnhandledExceptionInDevelopment_Returns500WithExceptionDetails()
    {
        // Arrange
        var (context, stream) = CreateHttpContext();
        _mockEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Fatal database disk corruption"),
            _mockLogger.Object,
            _mockEnvironment.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = await ReadProblemDetailsAsync(stream);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(500);
        problem.Detail.Should().Be("Fatal database disk corruption");
    }
}
