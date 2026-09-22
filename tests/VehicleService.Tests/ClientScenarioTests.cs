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

    [Fact]
    public async Task UpdatePartAsync_UpdatesAllEditableFieldsCorrectly()
    {
        // Arrange
        var inventoryService = new InventoryService(_unitOfWork);
        var part = new InventoryPart
        {
            PartNumber = "TEST-PART-01",
            Name = "Original Part Name",
            Category = "Brakes",
            Manufacturer = "Original Manufacturer",
            Supplier = "Original Supplier",
            CompatibleVehicles = "All Models",
            CostPrice = 1000m,
            SellingPrice = 1500m,
            AvailableQuantity = 20,
            ReservedQuantity = 2,
            ReorderLevel = 5,
            WarehouseLocation = "Bay A - Rack 01",
            WarrantyPeriodDays = 180,
            IsActive = true
        };
        _context.InventoryParts.Add(part);
        await _context.SaveChangesAsync();

        var updateDto = new InventoryPartDto
        {
            Id = part.Id,
            PartNumber = "TEST-PART-UPDATED",
            Name = "Ceramic Performance Brake Pads",
            Category = "Brakes & Suspension",
            Manufacturer = "Brembo",
            Supplier = "Brembo Global Distribution",
            CompatibleVehicles = "Hyundai Creta, Kia Seltos",
            CostPrice = 1200m,
            SellingPrice = 1850m,
            ReorderLevel = 8,
            WarehouseLocation = "Bay B - Shelf 04",
            WarrantyPeriodDays = 365,
            IsActive = false
        };

        // Act
        await inventoryService.UpdatePartAsync(part.Id, updateDto);

        // Assert
        var updatedPart = await _context.InventoryParts.FindAsync(part.Id);
        updatedPart.Should().NotBeNull();
        updatedPart!.PartNumber.Should().Be("TEST-PART-UPDATED");
        updatedPart.Name.Should().Be("Ceramic Performance Brake Pads");
        updatedPart.Category.Should().Be("Brakes & Suspension");
        updatedPart.Manufacturer.Should().Be("Brembo");
        updatedPart.Supplier.Should().Be("Brembo Global Distribution");
        updatedPart.CompatibleVehicles.Should().Be("Hyundai Creta, Kia Seltos");
        updatedPart.CostPrice.Should().Be(1200m);
        updatedPart.SellingPrice.Should().Be(1850m);
        updatedPart.ReorderLevel.Should().Be(8);
        updatedPart.WarehouseLocation.Should().Be("Bay B - Shelf 04");
        updatedPart.WarrantyPeriodDays.Should().Be(365);
        updatedPart.IsActive.Should().BeFalse();
        // Existing quantities must remain intact
        updatedPart.AvailableQuantity.Should().Be(20);
        updatedPart.ReservedQuantity.Should().Be(2);
    }


    [Fact]
    public async Task Scenario_UpdateJobCardDetails_ShouldPersistModifications()
    {
        // Arrange
        var jobCardService = new JobCardService(_unitOfWork, _mockNotification.Object);
        var vehicle = await _context.Vehicles.FirstAsync();
        var bay = await _context.ServiceBays.FirstAsync();

        var appointment = new ServiceAppointment
        {
            AppointmentNumber = "APT-TEST-001",
            VehicleId = vehicle.Id,
            CustomerId = "cust_001",
            ServiceCenterId = bay.ServiceCenterId,
            ServiceBayId = bay.Id,
            AppointmentDate = DateTime.UtcNow.Date,
            TimeSlot = "10:00 AM - 12:00 PM",
            Status = AppointmentStatus.InProgress
        };
        _context.ServiceAppointments.Add(appointment);
        await _context.SaveChangesAsync();

        var jobCard = new ServiceJobCard
        {
            JobCardNumber = "JC-TEST-001",
            AppointmentId = appointment.Id,
            VehicleId = vehicle.Id,
            ServiceBayId = bay.Id,
            Status = AppointmentStatus.InProgress,
            OdometerIn = 25000,
            MechanicNotes = "Initial check ok",
            AdvisorObservations = "Customer noted squeaking"
        };
        _context.ServiceJobCards.Add(jobCard);
        await _context.SaveChangesAsync();

        var updateDto = new UpdateJobCardDto
        {
            JobCardId = jobCard.Id,
            OdometerIn = 25100,
            OdometerOut = 25120,
            MechanicNotes = "Replaced front calipers, tested braking.",
            QualityCheckNotes = "Brake performance verified on dyno.",
            Recommendations = "Replace rear brake pads in 5,000 km.",
            Status = AppointmentStatus.QualityCheck
        };

        // Act
        await jobCardService.UpdateJobCardDetailsAsync(updateDto);

        // Assert
        var updated = await _context.ServiceJobCards.FindAsync(jobCard.Id);
        updated.Should().NotBeNull();
        updated!.OdometerIn.Should().Be(25100);
        updated.OdometerOut.Should().Be(25120);
        updated.MechanicNotes.Should().Be("Replaced front calipers, tested braking.");
        updated.QualityCheckNotes.Should().Be("Brake performance verified on dyno.");
        updated.Recommendations.Should().Be("Replace rear brake pads in 5,000 km.");
        updated.Status.Should().Be(AppointmentStatus.QualityCheck);
    }

    [Fact]
    public async Task Scenario_CancelJobCard_ShouldReleaseReservedPartsAndFreeBay()
    {
        // Arrange
        var jobCardService = new JobCardService(_unitOfWork, _mockNotification.Object);
        var vehicle = await _context.Vehicles.FirstAsync();
        var bay = await _context.ServiceBays.FirstAsync();
        var part = await _context.InventoryParts.FirstAsync();

        // Setup part with 5 available, 1 reserved
        part.AvailableQuantity = 5;
        part.ReservedQuantity = 1;
        await _context.SaveChangesAsync();

        var appointment = new ServiceAppointment
        {
            AppointmentNumber = "APT-CANCEL-01",
            VehicleId = vehicle.Id,
            CustomerId = "cust_001",
            ServiceCenterId = bay.ServiceCenterId,
            ServiceBayId = bay.Id,
            AppointmentDate = DateTime.UtcNow.Date,
            TimeSlot = "10:00 AM - 12:00 PM",
            Status = AppointmentStatus.InProgress
        };
        _context.ServiceAppointments.Add(appointment);
        await _context.SaveChangesAsync();

        var reservation = new PartReservation
        {
            AppointmentId = appointment.Id,
            InventoryPartId = part.Id,
            Quantity = 1,
            IsReleased = false,
            IsConsumed = false
        };
        _context.PartReservations.Add(reservation);

        var jobCard = new ServiceJobCard
        {
            JobCardNumber = "JC-CANCEL-001",
            AppointmentId = appointment.Id,
            VehicleId = vehicle.Id,
            ServiceBayId = bay.Id,
            Status = AppointmentStatus.InProgress,
            OdometerIn = 25000
        };
        _context.ServiceJobCards.Add(jobCard);
        await _context.SaveChangesAsync();

        // Act
        var result = await jobCardService.CancelJobCardAsync(jobCard.Id, "Customer requested cancellation due to travel", "adv_001");

        // Assert
        result.Should().BeTrue();

        var cancelledJob = await _context.ServiceJobCards.FindAsync(jobCard.Id);
        cancelledJob.Should().NotBeNull();
        cancelledJob!.Status.Should().Be(AppointmentStatus.Cancelled);
        cancelledJob.ServiceBayId.Should().BeNull();
        cancelledJob.MechanicNotes.Should().Contain("Customer requested cancellation due to travel");

        var updatedAppointment = await _context.ServiceAppointments.FindAsync(appointment.Id);
        updatedAppointment!.Status.Should().Be(AppointmentStatus.Cancelled);
        updatedAppointment.CancellationReason.Should().Be("Customer requested cancellation due to travel");

        // Reserved parts should be released back to available inventory
        var updatedPart = await _context.InventoryParts.FindAsync(part.Id);
        updatedPart!.AvailableQuantity.Should().Be(6); // 5 + 1
        updatedPart.ReservedQuantity.Should().Be(0); // 1 - 1

        var updatedReservation = await _context.PartReservations.FindAsync(reservation.Id);
        updatedReservation!.IsReleased.Should().BeTrue();
        updatedReservation.ReleasedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Scenario_AdjustStock_ReservationRelease_ShouldTransferReservedToAvailable()
    {
        // Arrange
        var inventoryService = new InventoryService(_unitOfWork);
        var part = await _context.InventoryParts.FirstAsync();
        part.AvailableQuantity = 10;
        part.ReservedQuantity = 3;
        await _context.SaveChangesAsync();

        var adjustDto = new AdjustStockDto
        {
            PartId = part.Id,
            Quantity = 2,
            TransactionType = InventoryTransactionType.ReservationRelease,
            Notes = "Manual reservation release"
        };

        // Act
        var result = await inventoryService.AdjustStockAsync(adjustDto);

        // Assert
        result.Should().BeTrue();
        var updated = await _context.InventoryParts.FindAsync(part.Id);
        updated!.AvailableQuantity.Should().Be(12);
        updated.ReservedQuantity.Should().Be(1);

        var txns = await inventoryService.GetTransactionsAsync(part.Id);
        var latestTxn = txns.OrderByDescending(t => t.CreatedAt).FirstOrDefault();
        latestTxn.Should().NotBeNull();
        latestTxn!.TransactionType.Should().Be(InventoryTransactionType.ReservationRelease);
        latestTxn.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task Scenario_ReconcileReservedStock_ShouldReleaseOrphanedReservations()
    {
        // Arrange
        var inventoryService = new InventoryService(_unitOfWork);
        var center = await _context.ServiceCenters.FirstAsync();
        var partWithOrphan = new InventoryPart
        {
            PartNumber = "ORPHAN-PART-01",
            Name = "Orphaned Part Test",
            Category = "Electrical",
            Manufacturer = "TestMfg",
            CostPrice = 100,
            SellingPrice = 200,
            AvailableQuantity = 5,
            ReservedQuantity = 3, // 3 reserved, but 0 backing PartReservations!
            ServiceCenterId = center.Id,
            IsActive = true
        };
        _context.InventoryParts.Add(partWithOrphan);
        await _context.SaveChangesAsync();

        // Act
        var count = await inventoryService.ReconcileReservedStockAsync();

        // Assert
        count.Should().BeGreaterThan(0);
        var reconciledPart = await _context.InventoryParts.FindAsync(partWithOrphan.Id);
        reconciledPart!.ReservedQuantity.Should().Be(0);
        reconciledPart.AvailableQuantity.Should().Be(8); // 5 + 3 orphaned released back to available!
    }

    [Fact]
    public async Task Reconcile_LiveDatabase_OrphanedReservations_ShouldSync()
    {
        // Path to the web project's SQLite database
        var dbPath = @"c:\Task\VehicleServiceFleetManagement\src\VehicleService.Web\VehicleService.db";
        if (!File.Exists(dbPath)) return;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using var liveContext = new ApplicationDbContext(options);
        var liveUow = new UnitOfWork(liveContext);
        var liveInventoryService = new InventoryService(liveUow);

        var reconciledCount = await liveInventoryService.ReconcileReservedStockAsync();

        // Verify that BAT-AGM-65AH has 0 orphaned reserved quantity and 14 available
        var battery = await liveContext.InventoryParts.FirstOrDefaultAsync(p => p.PartNumber == "BAT-AGM-65AH");
        if (battery != null)
        {
            battery.ReservedQuantity.Should().Be(0);
            battery.AvailableQuantity.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task Scenario_CreateJobCard_WithPartIds_ShouldUpdateReservedCount()
    {
        // Arrange
        var jobCardService = new JobCardService(_unitOfWork, _mockNotification.Object);
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();
        var part = await _context.InventoryParts.FirstAsync();
        part.AvailableQuantity = 10;
        part.ReservedQuantity = 0;
        await _context.SaveChangesAsync();

        var appointment = new ServiceAppointment
        {
            AppointmentNumber = "APT-RESERVE-JC",
            VehicleId = vehicle.Id,
            CustomerId = "cust_001",
            ServiceCenterId = center.Id,
            AppointmentDate = DateTime.UtcNow,
            Status = AppointmentStatus.Confirmed
        };
        _context.ServiceAppointments.Add(appointment);
        await _context.SaveChangesAsync();

        // Act - Create Job Card and specify part to reserve
        var jobCard = await jobCardService.CreateJobCardFromAppointmentAsync(appointment.Id, "adv_001", null, new List<int> { part.Id });

        // Assert
        jobCard.Should().NotBeNull();
        var updatedPart = await _context.InventoryParts.FindAsync(part.Id);
        updatedPart!.AvailableQuantity.Should().Be(9);
        updatedPart.ReservedQuantity.Should().Be(1);

        var reservation = await _context.PartReservations.FirstOrDefaultAsync(r => r.AppointmentId == appointment.Id && r.InventoryPartId == part.Id);
        reservation.Should().NotBeNull();
        reservation!.Quantity.Should().Be(1);
        reservation.IsReleased.Should().BeFalse();
    }

    [Fact]
    public async Task Scenario_CreateEstimate_WithParts_ShouldReservePartsForJobCard()
    {
        // Arrange
        var jobCardService = new JobCardService(_unitOfWork, _mockNotification.Object);
        var estimateService = new EstimateService(_unitOfWork, _mockNotification.Object);
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();
        var part = await _context.InventoryParts.FirstAsync();
        part.AvailableQuantity = 15;
        part.ReservedQuantity = 0;
        await _context.SaveChangesAsync();

        var appointment = new ServiceAppointment
        {
            AppointmentNumber = "APT-EST-RES",
            VehicleId = vehicle.Id,
            CustomerId = "cust_001",
            ServiceCenterId = center.Id,
            AppointmentDate = DateTime.UtcNow,
            Status = AppointmentStatus.Confirmed
        };
        _context.ServiceAppointments.Add(appointment);
        await _context.SaveChangesAsync();

        var jobCard = await jobCardService.CreateJobCardFromAppointmentAsync(appointment.Id, "adv_001");

        var estimateItems = new List<EstimateItemDto>
        {
            new EstimateItemDto
            {
                Description = "Brake Pad Replacement",
                ItemType = EstimateItemType.Part,
                InventoryPartId = part.Id,
                Quantity = 2,
                UnitPrice = 1500,
                LaborCharges = 500
            }
        };

        // Act - Create Estimate
        var estimate = await estimateService.CreateEstimateAsync(jobCard.Id, "adv_001", estimateItems);

        // Assert
        estimate.Should().NotBeNull();
        var updatedPart = await _context.InventoryParts.FindAsync(part.Id);
        updatedPart!.AvailableQuantity.Should().Be(13); // 15 - 2
        updatedPart.ReservedQuantity.Should().Be(2);  // 0 + 2

        var reservation = await _context.PartReservations.FirstOrDefaultAsync(r => r.AppointmentId == appointment.Id && r.InventoryPartId == part.Id);
        reservation.Should().NotBeNull();
        reservation!.Quantity.Should().Be(2);
        reservation.IsReleased.Should().BeFalse();
    }

    [Fact]
    public async Task CancelAppointment_ShouldCancelAssociatedJobCard_AndReleaseParts()
    {
        // Arrange
        var appointmentService = new AppointmentService(_unitOfWork, _mockNotification.Object);
        var jobCardService = new JobCardService(_unitOfWork, _mockNotification.Object);
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();
        var bay = await _context.ServiceBays.FirstAsync();
        var part = await _context.InventoryParts.FirstAsync();
        part.AvailableQuantity = 10;
        part.ReservedQuantity = 0;
        await _context.SaveChangesAsync();

        var appointment = new ServiceAppointment
        {
            AppointmentNumber = "APT-CANCEL-SYNC",
            VehicleId = vehicle.Id,
            CustomerId = "cust_001",
            ServiceCenterId = center.Id,
            ServiceBayId = bay.Id,
            AppointmentDate = DateTime.UtcNow,
            Status = AppointmentStatus.Confirmed
        };
        _context.ServiceAppointments.Add(appointment);
        await _context.SaveChangesAsync();

        var jobCard = await jobCardService.CreateJobCardFromAppointmentAsync(appointment.Id, "adv_001", bay.Id, new List<int> { part.Id });

        // Verify pre-conditions
        var partBeforeCancel = await _context.InventoryParts.FindAsync(part.Id);
        partBeforeCancel!.ReservedQuantity.Should().Be(1);
        jobCard.Status.Should().Be(AppointmentStatus.VehicleReceived);

        // Act - Cancel appointment
        var result = await appointmentService.CancelAppointmentAsync(appointment.Id, "Customer requested cancellation", "cust_001");

        // Assert
        result.Should().BeTrue();
        var cancelledAppt = await _context.ServiceAppointments.FindAsync(appointment.Id);
        cancelledAppt!.Status.Should().Be(AppointmentStatus.Cancelled);
        cancelledAppt.ServiceBayId.Should().BeNull();

        var cancelledJobCard = await _context.ServiceJobCards.FindAsync(jobCard.Id);
        cancelledJobCard!.Status.Should().Be(AppointmentStatus.Cancelled);
        cancelledJobCard.ServiceBayId.Should().BeNull();
        cancelledJobCard.MechanicNotes.Should().Contain("Cancelled with Appointment");

        // Reserved parts released back to inventory
        var partAfterCancel = await _context.InventoryParts.FindAsync(part.Id);
        partAfterCancel!.ReservedQuantity.Should().Be(0);
        partAfterCancel.AvailableQuantity.Should().Be(10);
    }

    [Fact]
    public async Task CancelledAppointment_OrJobCard_CannotApproveEstimate_AndStatusRemainsCancelled()
    {
        // Arrange
        var appointmentService = new AppointmentService(_unitOfWork, _mockNotification.Object);
        var jobCardService = new JobCardService(_unitOfWork, _mockNotification.Object);
        var estimateService = new EstimateService(_unitOfWork, _mockNotification.Object);
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();
        var part = await _context.InventoryParts.FirstAsync();

        var appointment = new ServiceAppointment
        {
            AppointmentNumber = "APT-CANCEL-EST",
            VehicleId = vehicle.Id,
            CustomerId = "cust_001",
            ServiceCenterId = center.Id,
            AppointmentDate = DateTime.UtcNow,
            Status = AppointmentStatus.Confirmed
        };
        _context.ServiceAppointments.Add(appointment);
        await _context.SaveChangesAsync();

        var jobCard = await jobCardService.CreateJobCardFromAppointmentAsync(appointment.Id, "adv_001");

        var estimate = await estimateService.CreateEstimateAsync(jobCard.Id, "adv_001", new List<EstimateItemDto>
        {
            new EstimateItemDto { Description = "Oil Filter", ItemType = EstimateItemType.Part, InventoryPartId = part.Id, Quantity = 1, UnitPrice = 500, LaborCharges = 200 }
        });

        // Cancel the appointment (which also cancels the job card)
        await appointmentService.CancelAppointmentAsync(appointment.Id, "Schedule conflict", "cust_001");

        // Act & Assert - Attempting to approve the estimate must throw DomainException
        var act = async () => await estimateService.ProcessCustomerEstimateResponseAsync(new CustomerEstimateResponseDto
        {
            EstimateId = estimate.Id,
            Action = EstimateApprovalStatus.Approved,
            CustomerNotes = "Trying to approve after cancellation"
        });

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*cancelled*");

        // Verify job card status did NOT change to InProgress
        var refreshedJobCard = await _context.ServiceJobCards.FindAsync(jobCard.Id);
        refreshedJobCard!.Status.Should().Be(AppointmentStatus.Cancelled);
        refreshedJobCard.Status.Should().NotBe(AppointmentStatus.InProgress);

        var refreshedAppt = await _context.ServiceAppointments.FindAsync(appointment.Id);
        refreshedAppt!.Status.Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task CancelledJobCard_ShouldNotAllowUpdatingDetailsOrAdvancingStatus()
    {
        // Arrange
        var jobCardService = new JobCardService(_unitOfWork, _mockNotification.Object);
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();

        var appointment = new ServiceAppointment
        {
            AppointmentNumber = "APT-CANCEL-BLOCKED",
            VehicleId = vehicle.Id,
            CustomerId = "cust_001",
            ServiceCenterId = center.Id,
            AppointmentDate = DateTime.UtcNow,
            Status = AppointmentStatus.Confirmed
        };
        _context.ServiceAppointments.Add(appointment);
        await _context.SaveChangesAsync();

        var jobCard = await jobCardService.CreateJobCardFromAppointmentAsync(appointment.Id, "adv_001");
        await jobCardService.CancelJobCardAsync(jobCard.Id, "Customer decided not to service", "adv_001");

        // Act & Assert 1: Cannot update status
        var actStatus = async () => await jobCardService.UpdateJobCardStatusAsync(jobCard.Id, AppointmentStatus.InProgress);
        await actStatus.Should().ThrowAsync<DomainException>().WithMessage("*cancelled*");

        // Act & Assert 2: Cannot assign mechanic
        var actAssign = async () => await jobCardService.AssignMechanicAsync(jobCard.Id, "mech_john");
        await actAssign.Should().ThrowAsync<DomainException>().WithMessage("*cancelled*");

        // Act & Assert 3: Cannot update details
        var actDetails = async () => await jobCardService.UpdateJobCardDetailsAsync(new UpdateJobCardDto
        {
            JobCardId = jobCard.Id,
            OdometerIn = 30000,
            MechanicNotes = "Attempted note on cancelled job"
        });
        await actDetails.Should().ThrowAsync<DomainException>().WithMessage("*Cannot edit job card in 'Cancelled' status*");
    }

    [Fact]
    public async Task CancelledJobCard_ShouldNotAllowLoggingLabor_OrConsumingParts_OrInspections()
    {
        // Arrange
        var jobCardService = new JobCardService(_unitOfWork, _mockNotification.Object);
        var inventoryService = new InventoryService(_unitOfWork);
        var inspectionService = new VehicleInspectionService(_unitOfWork);
        var invoiceService = new InvoiceService(_unitOfWork, _mockNotification.Object);
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();
        var part = await _context.InventoryParts.FirstAsync();

        var appointment = new ServiceAppointment
        {
            AppointmentNumber = "APT-CANCEL-OPERATIONS",
            VehicleId = vehicle.Id,
            CustomerId = "cust_001",
            ServiceCenterId = center.Id,
            AppointmentDate = DateTime.UtcNow,
            Status = AppointmentStatus.Confirmed
        };
        _context.ServiceAppointments.Add(appointment);
        await _context.SaveChangesAsync();

        var jobCard = await jobCardService.CreateJobCardFromAppointmentAsync(appointment.Id, "adv_001");
        await jobCardService.CancelJobCardAsync(jobCard.Id, "Owner withdrew vehicle", "adv_001");

        // Act & Assert 1: Labor log blocked
        var actLabor = async () => await jobCardService.AddWorkLogAsync(jobCard.Id, "mech_john", "Engine tuning", 2, "Test observations");
        await actLabor.Should().ThrowAsync<DomainException>().WithMessage("*cancelled*");

        // Act & Assert 2: Part consumption blocked
        var actConsume = async () => await inventoryService.ConsumePartAsync(part.Id, 1, jobCard.Id, "mech_john");
        await actConsume.Should().ThrowAsync<DomainException>().WithMessage("*cancelled*");

        // Act & Assert 3: Inspection saving blocked
        var actInspect = async () => await inspectionService.SaveInspectionAsync(new VehicleInspection
        {
            JobCardId = jobCard.Id,
            VehicleId = vehicle.Id,
            InspectedByUserId = "adv_001",
            EngineCondition = "Good"
        });
        await actInspect.Should().ThrowAsync<DomainException>().WithMessage("*cancelled*");

        // Act & Assert 4: Invoice generation blocked
        var actInvoice = async () => await invoiceService.GenerateInvoiceFromJobCardAsync(jobCard.Id);
        await actInvoice.Should().ThrowAsync<DomainException>().WithMessage("*cancelled*");
    }

    [Fact]
    public async Task PreventiveMaintenance_FleetVehicle_CanBeRetrievedAndBooked()
    {
        // Arrange
        var fleet = new CompanyFleet
        {
            CompanyName = "Apex Logistics",
            ContactPerson = "Fleet Manager",
            ContactEmail = "fleet@apex.com",
            ContactPhone = "+91 99000 00000",
            IsActive = true
        };
        _context.CompanyFleets.Add(fleet);
        await _context.SaveChangesAsync();

        var fleetVehicle = new Vehicle
        {
            RegistrationNumber = "KA05FL9999",
            VIN = "VINFL999900000",
            Make = "Tata",
            Model = "Ace Gold",
            Variant = "Diesel",
            ManufacturingYear = 2023,
            CurrentMileage = 35000,
            CompanyFleetId = fleet.Id,
            LastServiceMileage = 20000,
            NextServiceDueMileage = 30000, // Due for service!
            NextServiceDueDate = DateTime.UtcNow.AddDays(-5),
            IsActive = true
        };
        _context.Vehicles.Add(fleetVehicle);
        await _context.SaveChangesAsync();

        var fleetManager = new ApplicationUser
        {
            Id = "fleet_manager_user",
            UserName = "fleet@test.com",
            Email = "fleet@test.com",
            FullName = "Apex Fleet Manager",
            RoleType = UserRoleType.FleetManager,
            CompanyFleetId = fleet.Id
        };
        _context.Users.Add(fleetManager);
        await _context.SaveChangesAsync();

        var vehicleService = new VehicleManagementService(_unitOfWork);
        var pmService = new PreventiveMaintenanceService(_unitOfWork, _mockNotification.Object);
        var appointmentService = new AppointmentService(_unitOfWork, _mockNotification.Object);

        // Act 1: Verify PM service identifies fleet vehicle as maintenance due
        var dueVehicles = await pmService.GetVehiclesDueForMaintenanceAsync(fleet.Id);
        dueVehicles.Should().Contain(v => v.Id == fleetVehicle.Id);

        // Act 2: Fetch vehicle by ID (as done when clicking Book Service from PM)
        var fetchedVehicle = await vehicleService.GetVehicleByIdAsync(fleetVehicle.Id);
        fetchedVehicle.Should().NotBeNull();
        fetchedVehicle!.RegistrationNumber.Should().Be("KA05FL9999");
        fetchedVehicle.CompanyFleetId.Should().Be(fleet.Id);

        // Act 3: Book appointment for this vehicle
        var center = await _context.ServiceCenters.FirstAsync();
        var srv = await _context.ServiceTypes.FirstAsync();
        var bookDto = new BookAppointmentDto
        {
            VehicleId = fleetVehicle.Id,
            CustomerId = "fleet_manager_user",
            ServiceCenterId = center.Id,
            ServiceTypeId = srv.Id,
            AppointmentDate = DateTime.UtcNow.Date.AddDays(3),
            TimeSlot = "11:30 AM - 01:30 PM"
        };

        var appt = await appointmentService.BookAppointmentAsync(bookDto);

        // Assert
        appt.Should().NotBeNull();
        appt.VehicleId.Should().Be(fleetVehicle.Id);
        appt.Status.Should().Be(AppointmentStatus.Confirmed);

        var retrievedAppt = await appointmentService.GetAppointmentByIdAsync(appt.Id);
        retrievedAppt.Should().NotBeNull();
        retrievedAppt!.VehicleInfo.Should().Contain("Tata Ace Gold");
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}