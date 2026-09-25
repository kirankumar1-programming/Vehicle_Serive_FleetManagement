using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Services;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Persistence.Context;
using VehicleService.Persistence.Repositories;
using VehicleService.Persistence.Seed;
using Xunit;

namespace VehicleService.Tests;

public class ChatbotRagTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly DocumentKnowledgeService _docService;
    private readonly DatabaseRagService _dbRagService;
    private readonly RagOrchestratorService _ragOrchestrator;

    public ChatbotRagTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"RagTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _unitOfWork = new UnitOfWork(_context);
        _docService = new DocumentKnowledgeService(_unitOfWork);
        _dbRagService = new DatabaseRagService(_unitOfWork);
        _ragOrchestrator = new RagOrchestratorService(_docService, _dbRagService, _unitOfWork);

        SeedTestData().GetAwaiter().GetResult();
    }

    private async Task SeedTestData()
    {
        // 1. Seed RAG Document Knowledge Base
        await DocumentKnowledgeSeeder.SeedAsync(_context);

        // 2. Seed Customer and Vehicle
        var customer = new ApplicationUser
        {
            Id = "cust_rag_01",
            UserName = "rag_customer@autopro.com",
            Email = "rag_customer@autopro.com",
            FullName = "Alex Morgan",
            RoleType = UserRoleType.Customer
        };
        _context.Users.Add(customer);

        var vehicle = new Vehicle
        {
            Id = 101,
            CustomerId = customer.Id,
            RegistrationNumber = "KA05MK9900",
            VIN = "1HGCR2F83HA00101",
            Make = "Honda",
            Model = "Civic",
            ManufacturingYear = 2022,
            FuelType = VehicleFuelType.Petrol,
            CurrentMileage = 28500,
            IsActive = true
        };
        _context.Vehicles.Add(vehicle);

        // 3. Seed Warranty & Claim
        var warranty = new Warranty
        {
            Id = 201,
            VehicleId = vehicle.Id,
            WarrantyNumber = "WAR-2026-8888",
            Type = WarrantyType.Vehicle,
            Provider = "Hyundai Extended Shield",
            StartDate = DateTime.UtcNow.AddMonths(-6),
            EndDate = DateTime.UtcNow.AddMonths(18),
            MaxMileageLimit = 50000,
            CoveredItemsSummary = "Engine, Transmission, ECU, Electricals",
            Status = WarrantyStatus.Active
        };
        _context.Warranties.Add(warranty);

        var claim = new WarrantyClaim
        {
            Id = 801,
            ClaimNumber = "CLM-20260924-8DA77",
            WarrantyId = warranty.Id,
            ClaimedByCustomerId = customer.Id,
            ComponentName = "ECU",
            AmountClaimed = 4500m,
            AmountApproved = 0m,
            Status = WarrantyClaimStatus.Submitted,
            IssueDescription = "Engine ECU warning light active"
        };
        _context.WarrantyClaims.Add(claim);

        // 4. Seed Low-Stock Inventory Part
        var part = new InventoryPart
        {
            Id = 301,
            PartNumber = "OIL-FLT-SYN",
            Name = "Synthetic Oil Filter Pro",
            Category = "Fluids",
            AvailableQuantity = 2,
            ReorderLevel = 10,
            SellingPrice = 18.50m,
            IsActive = true
        };
        _context.InventoryParts.Add(part);

        // 5. Seed Corporate Fleet Vehicle due for Maintenance
        var fleet = new CompanyFleet
        {
            Id = 401,
            CompanyName = "Express Logistics Corp",
            RegistrationNumber = "CORP-FLT-01",
            ContactPerson = "David Miller",
            IsActive = true
        };
        _context.CompanyFleets.Add(fleet);

        var fleetVehicle = new Vehicle
        {
            Id = 102,
            CompanyFleetId = fleet.Id,
            RegistrationNumber = "KA01FL8822",
            VIN = "1HGCR2F83HA00102",
            Make = "Tata",
            Model = "Winger Commercial",
            ManufacturingYear = 2023,
            FuelType = VehicleFuelType.Diesel,
            CurrentMileage = 39200, // Due for PM because 39200 % 10000 >= 8000
            IsActive = true
        };
        _context.Vehicles.Add(fleetVehicle);

        // 6. Seed Mechanic, Bay, Appointment, and Job Card
        var mechanic = new ApplicationUser
        {
            Id = "mech_rag_01",
            UserName = "mechanic_rag@autopro.com",
            Email = "mechanic_rag@autopro.com",
            FullName = "John Doe",
            RoleType = UserRoleType.Mechanic
        };
        _context.Users.Add(mechanic);

        var bay = new ServiceBay
        {
            Id = 501,
            BayNumber = "Bay #3",
            BayName = "Hydraulic Lift Bay 3",
            BayType = "Mechanical Repair",
            Status = BayStatus.Occupied,
            IsActive = true
        };
        _context.ServiceBays.Add(bay);

        var apt = new ServiceAppointment
        {
            Id = 601,
            AppointmentNumber = "APT-RAG-01",
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            AppointmentDate = DateTime.UtcNow.Date,
            TimeSlot = "10:00 AM - 12:00 PM",
            Status = AppointmentStatus.InProgress
        };
        _context.ServiceAppointments.Add(apt);

        var jobCard = new ServiceJobCard
        {
            Id = 701,
            JobCardNumber = "JC-2026-9901",
            AppointmentId = apt.Id,
            VehicleId = vehicle.Id,
            MechanicId = mechanic.Id,
            ServiceBayId = bay.Id,
            Status = AppointmentStatus.InProgress,
            TotalLaborHours = 2.0m,
            AdvisorObservations = "Routine 30k service check"
        };
        _context.ServiceJobCards.Add(jobCard);

        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task DocumentKnowledgeService_SearchBrakeMaintenance_ReturnsRelevantManualChunks()
    {
        // Act
        var results = await _docService.SearchDocumentChunksAsync("minimum brake pad thickness replacement", topK: 3);

        // Assert
        results.Should().NotBeEmpty();
        var topResult = results.First();
        topResult.DocumentTitle.Should().Contain("Maintenance");
        topResult.ChunkText.Should().Contain("Brake Pad Thickness");
        topResult.Score.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task DocumentKnowledgeService_SearchWarrantyPolicy_ReturnsWarrantyCoverageDetails()
    {
        // Act
        var results = await _docService.SearchDocumentChunksAsync("standard labor warranty period genuine parts", topK: 3);

        // Assert
        results.Should().NotBeEmpty();
        var topResult = results.First();
        topResult.DocumentTitle.Should().Contain("Warranty");
        topResult.ChunkText.Should().Contain("6 months or 10,000 kilometers");
    }

    [Fact]
    public async Task DocumentKnowledgeService_SearchRoadsideSOS_ReturnsTowingRadiusAndAssistance()
    {
        // Act
        var results = await _docService.SearchDocumentChunksAsync("roadside assistance hotline emergency towing limit", topK: 3);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.ChunkText.Contains("1800-AUTOPRO"));
        results.Should().Contain(r => r.ChunkText.Contains("50 kilometers"));
    }

    [Fact]
    public async Task DocumentKnowledgeService_IngestCustomDocument_IndexesAndRetrievesContent()
    {
        // Arrange
        var newDocDto = new DocumentUploadDto
        {
            Title = "Electric Vehicle High Voltage Safety Protocol 2026",
            Category = "Maintenance",
            Description = "Safety guidelines for servicing EV traction batteries and orange high-voltage cables.",
            FileName = "EV_Safety_Protocol.md",
            Content = @"# Electric Vehicle High Voltage Safety Protocol
## High Voltage Disconnect Procedure
Always remove the manual service disconnect (MSD) plug before servicing EV components.
Wear Class 0 insulated gloves rated for 1,000 Volts AC.
Verify zero potential using a calibrated Category IV 1000V multimeter."
        };

        // Act
        int docId = await _docService.IngestDocumentAsync(newDocDto);
        var searchResults = await _docService.SearchDocumentChunksAsync("manual service disconnect high voltage EV battery", topK: 2);

        // Assert
        docId.Should().BeGreaterThan(0);
        searchResults.Should().NotBeEmpty();
        searchResults.First().DocumentTitle.Should().Be("Electric Vehicle High Voltage Safety Protocol 2026");
        searchResults.First().ChunkText.Should().Contain("manual service disconnect");
    }

    [Fact]
    public async Task DatabaseRagService_CustomerQuery_ReturnsRegisteredVehicleAndEntityCards()
    {
        // Act
        var result = await _dbRagService.QueryDatabaseContextAsync("what vehicles do I have in my garage?", "cust_rag_01", "Customer");

        // Assert
        result.HasDirectDbMatches.Should().BeTrue();
        result.ContextSummaries.Should().Contain(s => s.Contains("KA05MK9900") && s.Contains("Honda Civic"));
        result.EntityCards.Should().Contain(c => c.Type == "Vehicle" && c.Subtitle.Contains("KA05MK9900"));
    }

    [Fact]
    public async Task DatabaseRagService_StaffQuery_ReturnsLowStockInventoryParts()
    {
        // Act
        var result = await _dbRagService.QueryDatabaseContextAsync("which spare parts are low in stock?", "staff_user", "InventoryManager");

        // Assert
        result.HasDirectDbMatches.Should().BeTrue();
        result.ContextSummaries.Should().Contain(s => s.Contains("OIL-FLT-SYN") && s.Contains("Synthetic Oil Filter Pro"));
        result.EntityCards.Should().Contain(c => c.Type == "Part" && c.BadgeText.Contains("Stock: 2"));
    }

    [Fact]
    public async Task DatabaseRagService_FleetQuery_IdentifiesVehiclesDueForPreventiveMaintenance()
    {
        // Act
        var result = await _dbRagService.QueryDatabaseContextAsync("check fleet vehicles maintenance due", "fleet_manager_user", "FleetManager");

        // Assert
        result.HasDirectDbMatches.Should().BeTrue();
        result.ContextSummaries.Should().Contain(s => s.Contains("KA01FL8822") && s.Contains("due for Preventive Maintenance"));
        result.EntityCards.Should().Contain(c => c.Type == "Vehicle" && c.BadgeText == "Maintenance Due");
    }

    [Fact]
    public async Task RagOrchestrator_HybridQuery_FusesDocumentsAndLiveDatabaseRecords()
    {
        // Act: Customer asks about warranty coverage for their vehicle
        var response = await _ragOrchestrator.ProcessQueryAsync(
            "Can I claim warranty for my registered vehicle KA05MK9900?", 
            "cust_rag_01", 
            "Customer", 
            sessionId: "test_session_1", 
            contextUrl: "/Customer/Warranties");

        // Assert
        response.Intent.Should().Be("HybridQuery");
        response.Reply.Should().Contain("### 📋 Current Database Records:");
        response.Reply.Should().Contain("### 📖 From");
        response.Sources.Should().Contain(s => s.SourceType == "Document");
        response.Sources.Should().Contain(s => s.SourceType == "Database");
    }

    [Fact]
    public async Task RagOrchestrator_GreetingQuery_ReturnsWelcomeMessageAndRoleSpecificPrompts()
    {
        // Act
        var response = await _ragOrchestrator.ProcessQueryAsync(
            "Hello, who are you?", 
            "cust_rag_01", 
            "Customer", 
            sessionId: "test_session_2", 
            contextUrl: "/Home/Index");

        // Assert
        response.Intent.Should().Be("Greeting");
        response.Reply.Should().Contain("AutoBot");
        response.SuggestedActions.Should().NotBeEmpty();
        response.SuggestedActions.Should().Contain(a => a.Label.Contains("Registered Vehicles") || a.Label.Contains("Roadside"));
    }

    [Fact]
    public async Task DatabaseRagService_CustomerQueryActiveBookings_WhenOnlyCancelledExists_ReturnsNoActiveBookings()
    {
        // Act: User specifically asks for "Active Service Booking Details"
        var result = await _dbRagService.QueryDatabaseContextAsync(
            "Active Service Booking Details",
            "cust_rag_01",
            "Customer");

        // Assert
        result.HasDirectDbMatches.Should().BeTrue();
        // In the test setup, the appointment has Status = Cancelled, so active booking check should find 0 active
        result.ContextSummaries.Should().Contain(s => s.Contains("no active service bookings"));
        // Should NOT dump vehicle cards when user asked about bookings
        result.EntityCards.Should().NotContain(c => c.Type == "Vehicle");
        // Should offer action to book new service
        result.Actions.Should().Contain(a => a.Label == "Book New Service");
    }

    [Fact]
    public async Task RagOrchestrator_ActiveServiceBookingDetails_SuppressesUnrelatedManualDocs()
    {
        // Act
        var response = await _ragOrchestrator.ProcessQueryAsync(
            "Active Service Booking Details",
            "cust_rag_01",
            "Customer",
            sessionId: "test_session_active_booking",
            contextUrl: "/Customer/Appointments");

        // Assert
        response.Sources.Should().Contain(s => s.SourceType == "Database");
        // Should NOT include unrelated manuals like brake fluid flush or warranty docs
        response.Sources.Should().NotContain(s => s.SourceType == "Document");
        response.Reply.Should().NotContain("Brake System Fluid Flush");
        response.EntityCards.Should().NotContain(c => c.Type == "Vehicle");
    }

    [Fact]
    public async Task DatabaseRagService_MechanicQuery_MyAssignedJobs_ReturnsJobCardsAndWorkbenchActions()
    {
        // Act: Mechanic asks for "My Assigned Jobs"
        var result = await _dbRagService.QueryDatabaseContextAsync(
            "My Assigned Jobs",
            "mech_rag_01",
            "Mechanic");

        // Assert
        result.HasDirectDbMatches.Should().BeTrue();
        result.ContextSummaries.Should().Contain(s => s.Contains("JC-2026-9901") && s.Contains("Honda Civic"));
        result.EntityCards.Should().Contain(c => c.Type == "JobCard" && c.Title == "Job Card JC-2026-9901");
        result.EntityCards.First(c => c.Title == "Job Card JC-2026-9901").ActionUrl.Should().Contain("/Mechanic/JobDetails/701");
        result.Actions.Should().Contain(a => a.Label == "Open Workstation");
    }

    [Fact]
    public async Task RagOrchestrator_MechanicAssignedJobs_ReturnsDatabaseRecordsAndSuppressesDriverSop()
    {
        // Act: Mechanic sends "My Assigned Jobs" in Chatbot
        var response = await _ragOrchestrator.ProcessQueryAsync(
            "My Assigned Jobs",
            "mech_rag_01",
            "Mechanic",
            sessionId: "test_mech_session",
            contextUrl: "/Mechanic");

        // Assert
        response.Intent.Should().Be("DatabaseQuery");
        response.Sources.Should().Contain(s => s.SourceType == "Database" && s.Title.Contains("Mechanic Workstation"));
        // Must NOT return Driver Assignment & Fleet Compliance Protocol document chunk
        response.Sources.Should().NotContain(s => s.SourceType == "Document");
        response.Reply.Should().NotContain("Corporate Fleet Preventive Maintenance & Driver SOP");
        response.Reply.Should().NotContain("Fleet Registry before dispatch");
        response.Reply.Should().Contain("JC-2026-9901");
        response.EntityCards.Should().Contain(c => c.Type == "JobCard");
    }

    [Fact]
    public async Task DatabaseRagService_MechanicQuery_WhenAllJobsCancelled_ReturnsCancelledJobsListAndNoFleetDriverSop()
    {
        // Arrange: Mark the mechanic's job card as Cancelled (matching user's screenshot)
        var job = await _context.ServiceJobCards.FindAsync(701);
        job!.Status = AppointmentStatus.Cancelled;
        await _context.SaveChangesAsync();

        // Act: Mechanic sends "My Assigned Jobs"
        var response = await _ragOrchestrator.ProcessQueryAsync(
            "My Assigned Jobs",
            "mech_rag_01",
            "Mechanic",
            sessionId: "test_mech_cancelled_session",
            contextUrl: "/Mechanic");

        // Assert
        response.Sources.Should().Contain(s => s.SourceType == "Database");
        response.Sources.Should().NotContain(s => s.SourceType == "Document");
        response.Reply.Should().NotContain("Fleet Registry before dispatch");
        response.Reply.Should().Contain("JC-2026-9901");
        response.Reply.Should().Contain("Cancelled");
        response.EntityCards.Should().Contain(c => c.Type == "JobCard" && c.BadgeText == "Cancelled");
        response.EntityCards.First(c => c.Type == "JobCard").ActionText.Should().Be("View Job Details");
    }

    [Fact]
    public async Task DatabaseRagService_CustomerQuery_WarrantyHistory_ReturnsWarrantyAndSubmittedClaimCards()
    {
        // Act: Customer queries "Warranty history"
        var result = await _dbRagService.QueryDatabaseContextAsync(
            "Warranty history",
            "cust_rag_01",
            "Customer");

        // Assert
        result.HasDirectDbMatches.Should().BeTrue();
        result.ContextSummaries.Should().Contain(s => s.Contains("WAR-2026-8888") && s.Contains("Hyundai Extended Shield"));
        result.ContextSummaries.Should().Contain(s => s.Contains("CLM-20260924-8DA77") && s.Contains("ECU"));
        result.EntityCards.Should().Contain(c => c.Type == "Warranty" && c.Title.Contains("WAR-2026-8888"));
        result.EntityCards.Should().Contain(c => c.Type == "WarrantyClaim" && c.Title.Contains("CLM-20260924-8DA77"));
        result.EntityCards.First(c => c.Type == "WarrantyClaim").KeyValues["Amount Claimed"].Should().Be("₹4,500");
        result.Actions.Should().Contain(a => a.Label == "Submit Warranty Claim");
    }

    [Fact]
    public async Task DatabaseRagService_CustomerQuery_SpecificClaimNumber_ReturnsExactClaimDetails()
    {
        // Act: User enters specific claim number "CLM-20260924-8DA77"
        var result = await _dbRagService.QueryDatabaseContextAsync(
            "Check status of CLM-20260924-8DA77",
            "cust_rag_01",
            "Customer");

        // Assert
        result.HasDirectDbMatches.Should().BeTrue();
        result.EntityCards.Should().Contain(c => c.Type == "WarrantyClaim" && c.Title == "Claim CLM-20260924-8DA77");
        result.EntityCards.First(c => c.Type == "WarrantyClaim").BadgeText.Should().Be("Submitted");
        result.ContextSummaries.Should().Contain(s => s.Contains("CLM-20260924-8DA77") && s.Contains("₹4,500"));
    }

    [Fact]
    public async Task RagOrchestrator_WarrantyHistory_ReturnsLiveWarrantyAndClaimRecordsWithoutUnrelatedDocChunks()
    {
        // Act: Customer asks for "Warranty history"
        var response = await _ragOrchestrator.ProcessQueryAsync(
            "Warranty history",
            "cust_rag_01",
            "Customer",
            sessionId: "test_warranty_session",
            contextUrl: "/Customer/Warranties");

        // Assert
        response.Intent.Should().Be("DatabaseQuery");
        response.Sources.Should().Contain(s => s.SourceType == "Database" && s.Title.Contains("Warranty"));
        // Pure account query for warranty history should not be polluted with generic policy chapters
        response.Sources.Should().NotContain(s => s.SourceType == "Document");
        response.Reply.Should().Contain("WAR-2026-8888");
        response.Reply.Should().Contain("CLM-20260924-8DA77");
        response.EntityCards.Should().Contain(c => c.Type == "WarrantyClaim");
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
