using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Collections.Concurrent;
using VehicleService.Application.Common;
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

public class ConcurrencyScenarioTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly Mock<INotificationService> _mockNotification;
    private readonly IConcurrencyCoordinator _concurrencyCoordinator;

    public ConcurrencyScenarioTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ConcurrencyTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _unitOfWork = new UnitOfWork(_context);
        _mockNotification = new Mock<INotificationService>();
        _concurrencyCoordinator = new KeyedLockCoordinator();

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
            Code = "SC-CONCURRENT-01",
            MaxDailyCapacity = 20,
            IsActive = true
        };
        _context.ServiceCenters.Add(center);
        _context.SaveChanges();

        // Seed 2 active bays for center
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

        // Seed 20 test vehicles for concurrent tests
        for (int i = 1; i <= 20; i++)
        {
            _context.Vehicles.Add(new Vehicle
            {
                RegistrationNumber = $"KA01CC{i:D4}",
                VIN = $"VINCONCURRENT{i:D6}",
                Make = "Hyundai",
                Model = "Creta",
                ManufacturingYear = 2023,
                CurrentMileage = 25000,
                CustomerId = i % 2 == 0 ? "cust_001" : "cust_002"
            });
        }

        // Seed Inventory Parts
        _context.InventoryParts.Add(new InventoryPart
        {
            PartNumber = "BP-CONC-01",
            Name = "Ceramic Brake Pad",
            Category = "Brakes",
            Manufacturer = "Brembo",
            CostPrice = 1500,
            SellingPrice = 2500,
            AvailableQuantity = 1, // Only 1 part available for concurrency conflict tests
            ReservedQuantity = 0,
            ReorderLevel = 2,
            IsActive = true
        });

        _context.InventoryParts.Add(new InventoryPart
        {
            PartNumber = "OIL-CONC-05",
            Name = "Synthetic Engine Oil",
            Category = "Fluids",
            Manufacturer = "Mobil",
            CostPrice = 800,
            SellingPrice = 1400,
            AvailableQuantity = 5,
            ReservedQuantity = 0,
            ReorderLevel = 2,
            IsActive = true
        });

        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // =========================================================================
    // SCENARIO 1: Double Booking Concurrency
    // =========================================================================

    [Fact]
    public async Task DoubleBooking_ParallelRequestsForSameBayAndSlot_OnlyOneSucceedsAndOthersThrow()
    {
        // Arrange
        var appointmentService = new AppointmentService(_unitOfWork, _mockNotification.Object, _concurrencyCoordinator);
        var center = await _context.ServiceCenters.FirstAsync();
        var bay = await _context.ServiceBays.FirstAsync();
        var srv = await _context.ServiceTypes.FirstAsync();
        var vehicles = await _context.Vehicles.Take(10).ToListAsync();

        var bookingDate = DateTime.UtcNow.Date.AddDays(5);
        var timeSlot = "10:00 AM - 12:00 PM";

        var successfulBookings = new ConcurrentBag<AppointmentDto>();
        var failedExceptions = new ConcurrentBag<Exception>();

        // Act - 10 concurrent requests targeting the exact same bay, date, and slot
        var tasks = vehicles.Select(async v =>
        {
            var dto = new BookAppointmentDto
            {
                VehicleId = v.Id,
                CustomerId = v.CustomerId,
                ServiceCenterId = center.Id,
                ServiceBayId = bay.Id, // Exact same bay
                ServiceTypeId = srv.Id,
                AppointmentDate = bookingDate,
                TimeSlot = timeSlot
            };

            try
            {
                var result = await appointmentService.BookAppointmentAsync(dto);
                successfulBookings.Add(result);
            }
            catch (Exception ex)
            {
                failedExceptions.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Assert
        successfulBookings.Should().HaveCount(1, "exactly one booking must succeed for the specific bay and slot");
        failedExceptions.Should().HaveCount(9, "all other 9 concurrent attempts must fail");
        failedExceptions.Should().AllBeOfType<DoubleBookingException>();

        // Verify database state: only 1 appointment exists for this bay and slot
        var savedAppointments = await _context.ServiceAppointments
            .Where(a => a.ServiceCenterId == center.Id &&
                        a.ServiceBayId == bay.Id &&
                        a.AppointmentDate.Date == bookingDate.Date &&
                        a.TimeSlot == timeSlot &&
                        a.Status != AppointmentStatus.Cancelled)
            .ToListAsync();

        savedAppointments.Should().HaveCount(1);
    }

    [Fact]
    public async Task DoubleBooking_ParallelRequestsForAutoAssignBay_DoesNotExceedCenterCapacity()
    {
        // Arrange: Center has 2 active bays total
        var appointmentService = new AppointmentService(_unitOfWork, _mockNotification.Object, _concurrencyCoordinator);
        var center = await _context.ServiceCenters.FirstAsync();
        var srv = await _context.ServiceTypes.FirstAsync();
        var vehicles = await _context.Vehicles.Take(10).ToListAsync();

        var bookingDate = DateTime.UtcNow.Date.AddDays(7);
        var timeSlot = "02:00 PM - 04:00 PM";

        var successfulBookings = new ConcurrentBag<AppointmentDto>();
        var failedExceptions = new ConcurrentBag<Exception>();

        // Act - 10 concurrent requests with null ServiceBayId (auto-assignment)
        var tasks = vehicles.Select(async v =>
        {
            var dto = new BookAppointmentDto
            {
                VehicleId = v.Id,
                CustomerId = v.CustomerId,
                ServiceCenterId = center.Id,
                ServiceBayId = null, // Auto-assignment requested
                ServiceTypeId = srv.Id,
                AppointmentDate = bookingDate,
                TimeSlot = timeSlot
            };

            try
            {
                var result = await appointmentService.BookAppointmentAsync(dto);
                successfulBookings.Add(result);
            }
            catch (Exception ex)
            {
                failedExceptions.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Assert: Center capacity is 2 bays, so exactly 2 succeed and 8 fail with DoubleBookingException
        successfulBookings.Should().HaveCount(2, "only 2 bays exist at the center for this time slot");
        failedExceptions.Should().HaveCount(8, "excess bookings must be rejected due to capacity limit");
        failedExceptions.Should().AllBeOfType<DoubleBookingException>();

        // Ensure both successful bookings were assigned to distinct bays (no duplicate bay assignment)
        var assignedBayIds = successfulBookings.Select(b => b.ServiceBayId).ToList();
        assignedBayIds.Should().OnlyHaveUniqueItems();
    }

    // =========================================================================
    // SCENARIO 2: Stock Conflicts Concurrency
    // =========================================================================

    [Fact]
    public async Task StockConflict_ParallelConsumeLastPart_OnlyOneSucceedsAndStockNeverNegative()
    {
        // Arrange: Part BP-CONC-01 has AvailableQuantity = 1
        var inventoryService = new InventoryService(_unitOfWork, _concurrencyCoordinator);
        var part = await _context.InventoryParts.FirstAsync(p => p.PartNumber == "BP-CONC-01");
        int partId = part.Id;

        var successfulConsumptions = new ConcurrentBag<bool>();
        var failedExceptions = new ConcurrentBag<Exception>();

        // Act - 10 concurrent tasks attempt to consume the single available unit
        var tasks = Enumerable.Range(1, 10).Select(async i =>
        {
            try
            {
                bool success = await inventoryService.ConsumePartAsync(partId, 1, jobCardId: 100 + i, performedByUserId: $"mech_{i}");
                if (success) successfulConsumptions.Add(true);
            }
            catch (Exception ex)
            {
                failedExceptions.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Assert: Exactly 1 succeeds, 9 fail
        successfulConsumptions.Should().HaveCount(1);
        failedExceptions.Should().HaveCount(9);
        failedExceptions.Should().AllBeOfType<InsufficientStockException>();

        // Verify remaining stock is 0 and NEVER negative
        var refreshedPart = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(partId);
        refreshedPart.Should().NotBeNull();
        refreshedPart!.AvailableQuantity.Should().Be(0);
        refreshedPart.AvailableQuantity.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task StockConflict_ParallelReserveLastPart_OnlyOneSucceedsAndStockNeverNegative()
    {
        // Arrange
        var inventoryService = new InventoryService(_unitOfWork, _concurrencyCoordinator);
        var part = await _context.InventoryParts.FirstAsync(p => p.PartNumber == "BP-CONC-01");
        int partId = part.Id;

        // Reset part stock to 1
        part.AvailableQuantity = 1;
        part.ReservedQuantity = 0;
        await _unitOfWork.SaveChangesAsync();

        var successfulReservations = new ConcurrentBag<bool>();
        var failedExceptions = new ConcurrentBag<Exception>();

        // Act - 10 concurrent tasks attempt to reserve the 1 available unit
        var tasks = Enumerable.Range(1, 10).Select(async i =>
        {
            try
            {
                bool success = await inventoryService.ReservePartAsync(partId, 1, appointmentId: 200 + i);
                if (success) successfulReservations.Add(true);
            }
            catch (Exception ex)
            {
                failedExceptions.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Assert
        successfulReservations.Should().HaveCount(1);
        failedExceptions.Should().HaveCount(9);
        failedExceptions.Should().AllBeOfType<InsufficientStockException>();

        var refreshedPart = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(partId);
        refreshedPart!.AvailableQuantity.Should().Be(0);
        refreshedPart.ReservedQuantity.Should().Be(1);
    }

    [Fact]
    public async Task StockConflict_ConcurrentReserveAndConsume_MaintainsStockIntegrity()
    {
        // Arrange: Part OIL-CONC-05 has AvailableQuantity = 5
        var inventoryService = new InventoryService(_unitOfWork, _concurrencyCoordinator);
        var part = await _context.InventoryParts.FirstAsync(p => p.PartNumber == "OIL-CONC-05");
        int partId = part.Id;

        var successfulOps = new ConcurrentBag<string>();
        var failedExceptions = new ConcurrentBag<Exception>();

        // 5 reservation tasks + 5 consumption tasks = total demand 10 units, but only 5 available
        var reserveTasks = Enumerable.Range(1, 5).Select(async i =>
        {
            try
            {
                bool success = await inventoryService.ReservePartAsync(partId, 1, appointmentId: 300 + i);
                if (success) successfulOps.Add($"Reserve_{i}");
            }
            catch (Exception ex)
            {
                failedExceptions.Add(ex);
            }
        });

        var consumeTasks = Enumerable.Range(1, 5).Select(async i =>
        {
            try
            {
                bool success = await inventoryService.ConsumePartAsync(partId, 1, jobCardId: 400 + i, performedByUserId: $"mech_{i}");
                if (success) successfulOps.Add($"Consume_{i}");
            }
            catch (Exception ex)
            {
                failedExceptions.Add(ex);
            }
        });

        await Task.WhenAll(reserveTasks.Concat(consumeTasks));

        // Assert: Exactly 5 total operations must succeed, remaining 5 must fail with InsufficientStockException
        successfulOps.Should().HaveCount(5);
        failedExceptions.Should().HaveCount(5);
        failedExceptions.Should().AllBeOfType<InsufficientStockException>();

        var refreshedPart = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(partId);
        refreshedPart!.AvailableQuantity.Should().Be(0);
        refreshedPart.AvailableQuantity.Should().BeGreaterThanOrEqualTo(0);
    }

    // =========================================================================
    // SCENARIO 3: Duplicate Payments Concurrency
    // =========================================================================

    [Fact]
    public async Task DuplicatePayment_ParallelRequestsWithSameTransactionRef_OnlyOneRecordedAndIdempotent()
    {
        // Arrange
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();

        var apt = new ServiceAppointment { AppointmentNumber = "APT-PAY-CONC", VehicleId = vehicle.Id, CustomerId = "cust_001", ServiceCenterId = center.Id, AppointmentDate = DateTime.UtcNow };
        _context.ServiceAppointments.Add(apt);
        await _context.SaveChangesAsync();

        var jc = new ServiceJobCard { JobCardNumber = "JC-PAY-CONC", AppointmentId = apt.Id, VehicleId = vehicle.Id, Status = AppointmentStatus.InProgress };
        _context.ServiceJobCards.Add(jc);
        await _context.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-PAY-CONC-01",
            JobCardId = jc.Id,
            CustomerId = "cust_001",
            VehicleId = vehicle.Id,
            GrandTotal = 5000,
            PaidAmount = 0,
            Status = PaymentStatus.Pending
        };
        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync();

        var paymentService = new MockPaymentService(_unitOfWork, _mockNotification.Object, _concurrencyCoordinator);
        var txnRef = "TXN-CONCURRENT-IDEMPOTENT-001";

        var results = new ConcurrentBag<PaymentResultDto>();
        var exceptions = new ConcurrentBag<Exception>();

        // Act - 10 parallel requests with identical TransactionReference
        var tasks = Enumerable.Range(1, 10).Select(async _ =>
        {
            try
            {
                var res = await paymentService.ProcessPaymentAsync(new PaymentRequestDto
                {
                    InvoiceId = invoice.Id,
                    Amount = 5000,
                    Method = PaymentMethod.CreditCard,
                    TransactionReference = txnRef
                });
                results.Add(res);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Assert: All 10 requests complete successfully without crashing
        exceptions.Should().BeEmpty();
        results.Should().HaveCount(10);
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());

        // Exactly ONE transaction record exists in DB
        var recordedTxns = await _context.PaymentTransactions
            .Where(p => p.TransactionReference == txnRef)
            .ToListAsync();

        recordedTxns.Should().HaveCount(1);

        // Invoice was credited exactly once
        var updatedInvoice = await _context.Invoices.FirstAsync(i => i.Id == invoice.Id);
        updatedInvoice.PaidAmount.Should().Be(5000);
        updatedInvoice.Status.Should().Be(PaymentStatus.Successful);
    }

    [Fact]
    public async Task DuplicatePayment_ParallelRequestsToAlreadyPaidInvoice_RejectsDuplicatePayment()
    {
        // Arrange: Invoice is already paid in full
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();

        var apt = new ServiceAppointment { AppointmentNumber = "APT-PAID-ALREADY", VehicleId = vehicle.Id, CustomerId = "cust_001", ServiceCenterId = center.Id, AppointmentDate = DateTime.UtcNow };
        _context.ServiceAppointments.Add(apt);
        await _context.SaveChangesAsync();

        var jc = new ServiceJobCard { JobCardNumber = "JC-PAID-ALREADY", AppointmentId = apt.Id, VehicleId = vehicle.Id, Status = AppointmentStatus.Completed };
        _context.ServiceJobCards.Add(jc);
        await _context.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-PAID-ALREADY-01",
            JobCardId = jc.Id,
            CustomerId = "cust_001",
            VehicleId = vehicle.Id,
            GrandTotal = 3000,
            PaidAmount = 3000,
            Status = PaymentStatus.Successful
        };
        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync();

        var paymentService = new MockPaymentService(_unitOfWork, _mockNotification.Object, _concurrencyCoordinator);

        // Act - Attempting to pay an already settled invoice returns paid-in-full response
        var res = await paymentService.ProcessPaymentAsync(new PaymentRequestDto
        {
            InvoiceId = invoice.Id,
            Amount = 3000,
            Method = PaymentMethod.CreditCard
        });

        res.Success.Should().BeTrue();
        res.Message.Should().Contain("already paid in full");

        // Verify invoice was not double-credited
        var refreshedInvoice = await _context.Invoices.FirstAsync(i => i.Id == invoice.Id);
        refreshedInvoice.PaidAmount.Should().Be(3000);
    }

    [Fact]
    public async Task DuplicatePayment_OverpaymentAttempt_ThrowsDuplicatePaymentException()
    {
        // Arrange
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();

        var apt = new ServiceAppointment { AppointmentNumber = "APT-OVERPAY", VehicleId = vehicle.Id, CustomerId = "cust_001", ServiceCenterId = center.Id, AppointmentDate = DateTime.UtcNow };
        _context.ServiceAppointments.Add(apt);
        await _context.SaveChangesAsync();

        var jc = new ServiceJobCard { JobCardNumber = "JC-OVERPAY", AppointmentId = apt.Id, VehicleId = vehicle.Id, Status = AppointmentStatus.InProgress };
        _context.ServiceJobCards.Add(jc);
        await _context.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-OVERPAY-01",
            JobCardId = jc.Id,
            CustomerId = "cust_001",
            VehicleId = vehicle.Id,
            GrandTotal = 2000,
            PaidAmount = 1500, // Remaining due is only 500
            Status = PaymentStatus.Authorized
        };
        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync();

        var paymentService = new MockPaymentService(_unitOfWork, _mockNotification.Object, _concurrencyCoordinator);

        // Act & Assert - Attempting to pay 1000 when only 500 is due must throw DuplicatePaymentException
        Func<Task> overpay = async () => await paymentService.ProcessPaymentAsync(new PaymentRequestDto
        {
            InvoiceId = invoice.Id,
            Amount = 1000, // Exceeds remaining balance of 500
            Method = PaymentMethod.CreditCard
        });

        await overpay.Should().ThrowAsync<DuplicatePaymentException>();
    }

    // =========================================================================
    // SCENARIO 4: Mechanic Assignments Concurrency
    // =========================================================================

    [Fact]
    public async Task MechanicAssignment_ParallelAssignmentsToBusyMechanic_NeverExceedsMaxCapacity()
    {
        // Arrange: Mechanic "mech_john" already has 1 active job (InProgress)
        // Workshop policy allows at most 2 active jobs (InProgress or UnderInspection)
        var vehicle = await _context.Vehicles.FirstAsync();
        var center = await _context.ServiceCenters.FirstAsync();

        var aptExisting = new ServiceAppointment { AppointmentNumber = "APT-MECH-EXIST", VehicleId = vehicle.Id, CustomerId = "cust_001", ServiceCenterId = center.Id, AppointmentDate = DateTime.UtcNow };
        _context.ServiceAppointments.Add(aptExisting);
        await _context.SaveChangesAsync();

        var jcExisting = new ServiceJobCard
        {
            JobCardNumber = "JC-MECH-ACTIVE-1",
            AppointmentId = aptExisting.Id,
            VehicleId = vehicle.Id,
            MechanicId = "mech_john",
            Status = AppointmentStatus.InProgress
        };
        _context.ServiceJobCards.Add(jcExisting);
        await _context.SaveChangesAsync();

        // Create 10 distinct job cards awaiting assignment
        var unassignedJobCards = new List<ServiceJobCard>();
        for (int i = 1; i <= 10; i++)
        {
            var apt = new ServiceAppointment { AppointmentNumber = $"APT-MECH-Q-{i}", VehicleId = vehicle.Id, CustomerId = "cust_001", ServiceCenterId = center.Id, AppointmentDate = DateTime.UtcNow };
            _context.ServiceAppointments.Add(apt);
            await _context.SaveChangesAsync();

            var jc = new ServiceJobCard
            {
                JobCardNumber = $"JC-MECH-PENDING-{i}",
                AppointmentId = apt.Id,
                VehicleId = vehicle.Id,
                MechanicId = null,
                Status = AppointmentStatus.InProgress // Needs mechanic assigned to work
            };
            _context.ServiceJobCards.Add(jc);
            unassignedJobCards.Add(jc);
        }
        await _context.SaveChangesAsync();

        var jobCardService = new JobCardService(_unitOfWork, _mockNotification.Object, _concurrencyCoordinator);

        var successfulAssignments = new ConcurrentBag<int>();
        var failedExceptions = new ConcurrentBag<Exception>();

        // Act - 10 concurrent advisors attempt to assign the 10 job cards to "mech_john"
        var tasks = unassignedJobCards.Select(async card =>
        {
            try
            {
                await jobCardService.AssignMechanicAsync(card.Id, "mech_john");
                successfulAssignments.Add(card.Id);
            }
            catch (Exception ex)
            {
                failedExceptions.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Assert: Since mech_john already had 1 active job and max capacity is 2,
        // EXACTLY 1 concurrent assignment can succeed. The remaining 9 MUST fail.
        successfulAssignments.Should().HaveCount(1, "mechanic can only take 1 more active job before hitting max capacity of 2");
        failedExceptions.Should().HaveCount(9, "all other concurrent assignments must be rejected with MechanicUnavailableException");
        failedExceptions.Should().AllBeOfType<MechanicUnavailableException>();

        // Verify database: Mechanic John has EXACTLY 2 active jobs, NEVER more
        var johnActiveJobs = await _context.ServiceJobCards
            .Where(j => j.MechanicId == "mech_john" &&
                        (j.Status == AppointmentStatus.InProgress || j.Status == AppointmentStatus.UnderInspection))
            .ToListAsync();

        johnActiveJobs.Should().HaveCount(2, "mechanic must never be overbooked beyond max allowed concurrent jobs");
    }
}
