using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Application.Services;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;
using VehicleService.Infrastructure.Payment;
using VehicleService.Persistence.Context;
using VehicleService.Persistence.Repositories;
using Xunit;

namespace VehicleService.Tests;

public class ClientScenarioTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly Mock<INotificationService> _mockNotification;

    public ClientScenarioTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"VehicleServiceTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _unitOfWork = new UnitOfWork(_context);
        _mockNotification = new Mock<INotificationService>();

        SeedTestData();
    }

    private void SeedTestData()
    {
        var customer1 = new ApplicationUser { Id = "cust_001", UserName = "cust1@test.com", Email = "cust1@test.com", FullName = "Customer One", RoleType = UserRoleType.Customer };
        var customer2 = new ApplicationUser { Id = "cust_002", UserName = "cust2@test.com", Email = "cust2@test.com", FullName = "Customer Two", RoleType = UserRoleType.Customer };
        var mechanic = new ApplicationUser { Id = "mech_john", UserName = "mech@test.com", Email = "mech@test.com", FullName = "John Mechanic", RoleType = UserRoleType.Mechanic };
        var advisor = new ApplicationUser { Id = "adv_001", UserName = "adv@test.com", Email = "adv@test.com", FullName = "Advisor Vikram", RoleType = UserRoleType.ServiceAdvisor };
        _context.Users.AddRange(customer1, customer2, mechanic, advisor);

        var center = new ServiceCenter
        {
            Name = "Downtown Center",
            Code = "SC-01",
            MaxDailyCapacity = 10,
            IsActive = true
        };
        _context.ServiceCenters.Add(center);
        _context.SaveChanges();

        var bay1 = new ServiceBay { ServiceCenterId = center.Id, BayNumber = "BAY-01", BayName = "Bay 1", Status = BayStatus.Available, IsActive = true };
        var bay2 = new ServiceBay { ServiceCenterId = center.Id, BayNumber = "BAY-02", BayName = "Bay 2", Status = BayStatus.Available, IsActive = true };
        _context.ServiceBays.AddRange(bay1, bay2);

        var serviceType = new ServiceType
        {
            Name = "General Service",
            Code = "SRV-GEN",
            BasePrice = 2000,
            LaborCharges = 1000,
            IsActive = true
        };
        _context.ServiceTypes.Add(serviceType);

        var vehicle1 = new Vehicle
        {
            RegistrationNumber = "KA01AB1234",
            VIN = "VIN11223344556677",
            Make = "Hyundai",
            Model = "Creta",
            ManufacturingYear = 2023,
            CurrentMileage = 25000,
            CustomerId = "cust_001",
            NextServiceDueMileage = 30000,
            NextServiceDueDate = DateTime.UtcNow.AddMonths(3)
        };
        var vehicle2 = new Vehicle
        {
            RegistrationNumber = "KA02CD5678",
            VIN = "VIN99887766554433",
            Make = "Toyota",
            Model = "Innova",
            ManufacturingYear = 2024,
            CurrentMileage = 15000,
            CustomerId = "cust_002",
            NextServiceDueMileage = 20000,
            NextServiceDueDate = DateTime.UtcNow.AddMonths(4)
        };
        _context.Vehicles.AddRange(vehicle1, vehicle2);

        var part = new InventoryPart
        {
            PartNumber = "BP-100",
            Name = "Brake Pad",
            Category = "Brakes",
            Manufacturer = "Bosch",
            CostPrice = 2000,
            SellingPrice = 3500,
            AvailableQuantity = 1, // Only 1 part available for concurrency test!
            ReservedQuantity = 0,
            ReorderLevel = 2,
            IsActive = true
        };
        _context.InventoryParts.Add(part);

        _context.SaveChanges();
    }

    // SCENARIO 1: Double Booking Prevention
    [Fact]
    public async Task Scenario1_DoubleBooking_PreventsDuplicateBaySlotBooking()
    {
        // Arrange
        var appointmentService = new AppointmentService(_unitOfWork, _mockNotification.Object);
        var bookingDate = DateTime.UtcNow.Date.AddDays(2);
        var timeSlot = "09:00 AM - 11:00 AM";

        var center = await _context.ServiceCenters.FirstAsync();
        var bay1 = await _context.ServiceBays.FirstAsync();
        var v1 = await _context.Vehicles.FirstAsync(v => v.RegistrationNumber == "KA01AB1234");
        var v2 = await _context.Vehicles.FirstAsync(v => v.RegistrationNumber == "KA02CD5678");
        var srv = await _context.ServiceTypes.FirstAsync();

        var bookDto1 = new BookAppointmentDto
        {
            VehicleId = v1.Id,
            CustomerId = "cust_001",
            ServiceCenterId = center.Id,
            ServiceBayId = bay1.Id,
            ServiceTypeId = srv.Id,
            AppointmentDate = bookingDate,
            TimeSlot = timeSlot
        };

        var bookDto2 = new BookAppointmentDto
        {
            VehicleId = v2.Id,
            CustomerId = "cust_002",
            ServiceCenterId = center.Id,
            ServiceBayId = bay1.Id, // Same bay & time slot!
            ServiceTypeId = srv.Id,
            AppointmentDate = bookingDate,
            TimeSlot = timeSlot
        };

        // Act
        var result1 = await appointmentService.BookAppointmentAsync(bookDto1);

        // Assert
        result1.Should().NotBeNull();
        result1.Status.Should().Be(AppointmentStatus.Confirmed);

        // Second booking for the same slot and bay must fail
        Func<Task> secondBooking = async () => await appointmentService.BookAppointmentAsync(bookDto2);
        await secondBooking.Should().ThrowAsync<DoubleBookingException>();
    }

    // SCENARIO 2: Last Spare Part Concurrency & Negative Stock Prevention
    [Fact]
    public async Task Scenario2_LastSparePart_PreventsNegativeStock()
    {
        // Arrange
        var inventoryService = new InventoryService(_unitOfWork);
        var part = await _context.InventoryParts.FirstAsync();
        int partId = part.Id;

        // Act - First consumption should succeed
        bool firstConsumed = await inventoryService.ConsumePartAsync(partId, 1, 101, "mech_1");
        firstConsumed.Should().BeTrue();

        var partAfterFirst = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(partId);
        partAfterFirst!.AvailableQuantity.Should().Be(0);

        // Second attempt to consume when stock is 0 must throw InsufficientStockException and NOT become negative
        Func<Task> secondConsumption = async () => await inventoryService.ConsumePartAsync(partId, 1, 102, "mech_2");
        await secondConsumption.Should().ThrowAsync<InsufficientStockException>();

        var partAfterSecond = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(partId);
        partAfterSecond!.AvailableQuantity.Should().Be(0); // Remained non-negative!
    }

    // SCENARIO 3: Idempotent Payment Callback Processing
    [Fact]
    public async Task Scenario3_PaymentCallbackTwice_DoesNotDuplicateTransaction()
    {
        // Arrange
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();

        var apt = new ServiceAppointment
        {
            AppointmentNumber = "APT-PAY-TEST",
            VehicleId = vehicle.Id,
            CustomerId = "cust_001",
            ServiceCenterId = center.Id,
            AppointmentDate = DateTime.UtcNow
        };
        _context.ServiceAppointments.Add(apt);
        await _context.SaveChangesAsync();

        var jc = new ServiceJobCard
        {
            JobCardNumber = "JC-PAY-TEST",
            AppointmentId = apt.Id,
            VehicleId = vehicle.Id,
            Status = AppointmentStatus.Completed
        };
        _context.ServiceJobCards.Add(jc);
        await _context.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-TEST-001",
            JobCardId = jc.Id,
            CustomerId = "cust_001",
            VehicleId = vehicle.Id,
            GrandTotal = 5000,
            PaidAmount = 0,
            Status = PaymentStatus.Pending
        };
        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync();

        var paymentService = new MockPaymentService(_unitOfWork, _mockNotification.Object);
        var txnRef = "TXN-IDEMPOTENT-12345";

        var paymentRequest = new PaymentRequestDto
        {
            InvoiceId = invoice.Id,
            Amount = 5000,
            Method = PaymentMethod.CreditCard,
            TransactionReference = txnRef
        };

        // Act - First payment attempt
        var result1 = await paymentService.ProcessPaymentAsync(paymentRequest);
        result1.Success.Should().BeTrue();

        // Duplicate payment callback with the same transaction reference
        var result2 = await paymentService.ProcessCallbackAsync(txnRef, "gtw_mock_123", PaymentStatus.Successful, 5000);
        result2.Success.Should().BeTrue();
        result2.Message.Should().Contain("Duplicate callback acknowledged");

        // Verify only ONE PaymentTransaction was persisted
        var recordedTransactions = await _unitOfWork.Repository<PaymentTransaction>().Query()
            .Where(p => p.TransactionReference == txnRef)
            .ToListAsync();

        recordedTransactions.Should().HaveCount(1);
    }

    // SCENARIO 4: Estimate Rejection Handling
    [Fact]
    public async Task Scenario4_EstimateRejected_ProceedsOnlyWithApprovedServices()
    {
        // Arrange
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();

        var apt = new ServiceAppointment
        {
            AppointmentNumber = "APT-EST-TEST",
            VehicleId = vehicle.Id,
            CustomerId = "cust_001",
            ServiceCenterId = center.Id,
            AppointmentDate = DateTime.UtcNow
        };
        _context.ServiceAppointments.Add(apt);
        await _context.SaveChangesAsync();

        var jobCard = new ServiceJobCard
        {
            JobCardNumber = "JC-TEST-50",
            AppointmentId = apt.Id,
            VehicleId = vehicle.Id,
            Status = AppointmentStatus.EstimateCreated
        };
        _context.ServiceJobCards.Add(jobCard);
        await _context.SaveChangesAsync();

        var estimate = new RepairEstimate
        {
            JobCardId = jobCard.Id,
            EstimateNumber = "EST-TEST-20",
            GrandTotal = 8000,
            ApprovalStatus = EstimateApprovalStatus.Pending
        };
        _context.RepairEstimates.Add(estimate);
        await _context.SaveChangesAsync();

        var item1 = new EstimateItem { RepairEstimateId = estimate.Id, Description = "Additional Brake Pad", UnitPrice = 3000, IsApprovedByCustomer = true };
        var item2 = new EstimateItem { RepairEstimateId = estimate.Id, Description = "Additional AC Gas", UnitPrice = 5000, IsApprovedByCustomer = true };
        _context.EstimateItems.AddRange(item1, item2);
        await _context.SaveChangesAsync();

        var estimateService = new EstimateService(_unitOfWork, _mockNotification.Object);

        // Act - Customer rejects additional estimate
        var response = new CustomerEstimateResponseDto
        {
            EstimateId = estimate.Id,
            Action = EstimateApprovalStatus.Rejected,
            CustomerNotes = "Not required at this time"
        };

        var updatedEstimate = await estimateService.ProcessCustomerEstimateResponseAsync(response);

        // Assert
        updatedEstimate.ApprovalStatus.Should().Be(EstimateApprovalStatus.Rejected);
        updatedEstimate.Items.All(i => !i.IsApprovedByCustomer).Should().BeTrue();

        var updatedJobCard = await _unitOfWork.Repository<ServiceJobCard>().GetByIdAsync(jobCard.Id);
        updatedJobCard!.Status.Should().Be(AppointmentStatus.InProgress); // Job continues with base service
        updatedJobCard.MechanicNotes.Should().Contain("Additional repairs rejected");
    }

    // SCENARIO 5: Service Cancellation & Resource Release
    [Fact]
    public async Task Scenario5_ServiceCancellation_ReleasesBayAndReservedParts()
    {
        // Arrange
        var appointmentService = new AppointmentService(_unitOfWork, _mockNotification.Object);
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();
        var bay1 = await _context.ServiceBays.FirstAsync();

        var apt = new ServiceAppointment
        {
            AppointmentNumber = "APT-CANCEL-TEST",
            VehicleId = vehicle.Id,
            CustomerId = "cust_001",
            ServiceCenterId = center.Id,
            ServiceBayId = bay1.Id,
            AppointmentDate = DateTime.UtcNow.AddDays(1),
            TimeSlot = "10:00 AM - 12:00 PM",
            Status = AppointmentStatus.Confirmed
        };
        _context.ServiceAppointments.Add(apt);
        await _context.SaveChangesAsync();

        var part = await _context.InventoryParts.FirstAsync();
        part.AvailableQuantity = 10;
        part.ReservedQuantity = 2;

        var reservation = new PartReservation
        {
            InventoryPartId = part.Id,
            AppointmentId = apt.Id,
            Quantity = 2,
            IsReleased = false
        };
        _context.PartReservations.Add(reservation);
        await _context.SaveChangesAsync();

        // Act - Cancel Appointment
        bool isCancelled = await appointmentService.CancelAppointmentAsync(apt.Id, "Customer requested cancellation", "cust_001");

        // Assert
        isCancelled.Should().BeTrue();

        var cancelledApt = await _unitOfWork.Repository<ServiceAppointment>().GetByIdAsync(apt.Id);
        cancelledApt!.Status.Should().Be(AppointmentStatus.Cancelled);
        cancelledApt.ServiceBayId.Should().BeNull(); // Bay released!

        var updatedReservation = await _unitOfWork.Repository<PartReservation>().GetByIdAsync(reservation.Id);
        updatedReservation!.IsReleased.Should().BeTrue(); // Reservation released!

        var updatedPart = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(part.Id);
        updatedPart!.AvailableQuantity.Should().Be(12); // 10 + 2 restored to available stock
        updatedPart.ReservedQuantity.Should().Be(0);
    }

    // SCENARIO 6: Mechanic Availability Overlap Prevention
    [Fact]
    public async Task Scenario6_MechanicAvailability_PreventsOverlappingAssignments()
    {
        // Arrange
        var jobCardService = new JobCardService(_unitOfWork, _mockNotification.Object);
        var v1 = await _context.Vehicles.FirstAsync(v => v.RegistrationNumber == "KA01AB1234");
        var v2 = await _context.Vehicles.FirstAsync(v => v.RegistrationNumber == "KA02CD5678");
        var center = await _context.ServiceCenters.FirstAsync();
        string mechanicId = "mech_john";

        var apt1 = new ServiceAppointment { AppointmentNumber = "A1", VehicleId = v1.Id, CustomerId = "cust_001", ServiceCenterId = center.Id, AppointmentDate = DateTime.UtcNow };
        var apt2 = new ServiceAppointment { AppointmentNumber = "A2", VehicleId = v2.Id, CustomerId = "cust_002", ServiceCenterId = center.Id, AppointmentDate = DateTime.UtcNow };
        var apt3 = new ServiceAppointment { AppointmentNumber = "A3", VehicleId = v1.Id, CustomerId = "cust_001", ServiceCenterId = center.Id, AppointmentDate = DateTime.UtcNow };
        _context.ServiceAppointments.AddRange(apt1, apt2, apt3);
        await _context.SaveChangesAsync();

        var jc1 = new ServiceJobCard { JobCardNumber = "JC-61", AppointmentId = apt1.Id, MechanicId = mechanicId, Status = AppointmentStatus.InProgress, VehicleId = v1.Id };
        var jc2 = new ServiceJobCard { JobCardNumber = "JC-62", AppointmentId = apt2.Id, MechanicId = mechanicId, Status = AppointmentStatus.UnderInspection, VehicleId = v2.Id };
        var jc3 = new ServiceJobCard { JobCardNumber = "JC-63", AppointmentId = apt3.Id, MechanicId = null, Status = AppointmentStatus.VehicleReceived, VehicleId = v1.Id };

        _context.ServiceJobCards.AddRange(jc1, jc2, jc3);
        await _context.SaveChangesAsync();

        // Act & Assert - Attempting to assign 3rd simultaneous active job must throw MechanicUnavailableException
        Func<Task> assignThirdJob = async () => await jobCardService.AssignMechanicAsync(jc3.Id, mechanicId);
        await assignThirdJob.Should().ThrowAsync<MechanicUnavailableException>();
    }

    // SCENARIO 7: Warranty Coverage Eligibility Calculation
    [Fact]
    public async Task Scenario7_WarrantyClaim_ValidatesDateMileageAndCoveredTerms()
    {
        // Arrange
        var vehicle = await _context.Vehicles.FirstAsync();
        var warranty = new Warranty
        {
            WarrantyNumber = "WAR-TEST-001",
            VehicleId = vehicle.Id,
            Provider = "OEM Comprehensive Warranty",
            CoverageTerms = "5-Year Powertrain & Brake System Warranty",
            CoveredItemsSummary = "Engine, Transmission, Brake Caliper, ABS Module",
            StartDate = DateTime.UtcNow.AddYears(-1),
            EndDate = DateTime.UtcNow.AddYears(3), // Valid date
            MaxMileageLimit = 50000,
            Status = WarrantyStatus.Active
        };
        _context.Warranties.Add(warranty);
        await _context.SaveChangesAsync();

        var warrantyService = new WarrantyService(_unitOfWork, _mockNotification.Object);

        // Case A: Covered component & valid mileage
        var resultEligible = await warrantyService.CheckWarrantyEligibilityAsync(vehicle.Id, "Brake Caliper", 25000);
        resultEligible.IsEligible.Should().BeTrue();
        resultEligible.WarrantyNumber.Should().Be("WAR-TEST-001");

        // Case B: Non-covered component (e.g., Wiper Blade)
        var resultNotCovered = await warrantyService.CheckWarrantyEligibilityAsync(vehicle.Id, "Windshield Wiper Blade", 25000);
        resultNotCovered.IsEligible.Should().BeFalse();
        resultNotCovered.Reason.Should().Contain("not included in the covered warranty terms");

        // Case C: Exceeded mileage limit (e.g., 60,000 KM > 50,000 KM)
        var resultOverMileage = await warrantyService.CheckWarrantyEligibilityAsync(vehicle.Id, "Brake Caliper", 60000);
        resultOverMileage.IsEligible.Should().BeFalse();
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}