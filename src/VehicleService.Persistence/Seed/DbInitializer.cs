using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Persistence.Context;

namespace VehicleService.Persistence.Seed;

public static class DbInitializer
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager)
    {
        await context.Database.EnsureCreatedAsync();

        // Ensure IsActive column exists in Vehicles table for existing databases
        if (context.Database.IsSqlite())
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Vehicles ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;");
            }
            catch { /* Column may already exist */ }
        }
        else if (context.Database.IsSqlServer())
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (
                        SELECT * FROM sys.columns 
                        WHERE object_id = OBJECT_ID('Vehicles') AND name = 'IsActive'
                    )
                    BEGIN
                        ALTER TABLE Vehicles ADD IsActive bit NOT NULL CONSTRAINT DF_Vehicles_IsActive DEFAULT 1;
                    END");
            }
            catch { /* Column may already exist */ }
        }

        // 1. Seed Roles
        var roles = Enum.GetValues<UserRoleType>().Select(r => r.ToString()).ToList();
        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = roleName,
                    Description = $"Role for {roleName} users"
                });
            }
        }

        // 2. Seed Service Centers
        if (!await context.ServiceCenters.AnyAsync())
        {
            var center1 = new ServiceCenter
            {
                Name = "AutoPro Central Hub & Service Center",
                Code = "SC-DOWNTOWN-01",
                Address = "104 Gateway Boulevard, Tech City",
                City = "Bangalore",
                State = "Karnataka",
                Phone = "+91 80 4123 4567",
                Email = "hub.central@autoproservice.com",
                OperatingHours = "08:00 AM - 07:00 PM",
                MaxDailyCapacity = 25,
                IsActive = true
            };

            var center2 = new ServiceCenter
            {
                Name = "AutoPro Fleet & Express Service Bay",
                Code = "SC-WEST-02",
                Address = "45 Industrial Expressway, Sector 5",
                City = "Bangalore",
                State = "Karnataka",
                Phone = "+91 80 4987 6543",
                Email = "hub.west@autoproservice.com",
                OperatingHours = "07:00 AM - 08:00 PM",
                MaxDailyCapacity = 30,
                IsActive = true
            };

            await context.ServiceCenters.AddRangeAsync(center1, center2);
            await context.SaveChangesAsync();

            var bayTypes = new[] { "General", "Express", "Wheel Alignment", "Heavy Repair", "Painting & Denting" };
            for (int i = 1; i <= 5; i++)
            {
                context.ServiceBays.Add(new ServiceBay
                {
                    ServiceCenterId = center1.Id,
                    BayNumber = $"BAY-10{i}",
                    BayName = $"Service Bay #{i} ({bayTypes[i - 1]})",
                    BayType = bayTypes[i - 1],
                    Status = BayStatus.Available,
                    IsActive = true
                });
            }

            for (int i = 1; i <= 4; i++)
            {
                context.ServiceBays.Add(new ServiceBay
                {
                    ServiceCenterId = center2.Id,
                    BayNumber = $"BAY-20{i}",
                    BayName = $"Fleet Bay #{i}",
                    BayType = "Fleet & Express",
                    Status = BayStatus.Available,
                    IsActive = true
                });
            }
            await context.SaveChangesAsync();
        }

        var defaultCenter = await context.ServiceCenters.FirstAsync();

        // 3. Seed Corporate Fleets
        if (!await context.CompanyFleets.AnyAsync())
        {
            var fleet1 = new CompanyFleet
            {
                CompanyName = "Apex FastLogistics Solutions Pvt Ltd",
                RegistrationNumber = "CIN-U72200KA2021PTC123456",
                TaxId = "29AAACA1234A1Z5",
                ContactPerson = "Rajesh Sharma",
                ContactEmail = "fleet.ops@apexlogistics.com",
                ContactPhone = "+91 98801 23456",
                Address = "Building 4, Electronic City Phase 1, Bangalore",
                CorporateDiscountRate = 12.5m,
                MonthlyBudgetLimit = 250000m,
                IsActive = true
            };

            var fleet2 = new CompanyFleet
            {
                CompanyName = "SwiftRide Express Cabs",
                RegistrationNumber = "CIN-U74999KA2020PTC654321",
                TaxId = "29BBBCB5678B1Z9",
                ContactPerson = "Ananya Desai",
                ContactEmail = "fleet@swiftride.in",
                ContactPhone = "+91 97702 98765",
                Address = "Indiranagar 100ft Road, Bangalore",
                CorporateDiscountRate = 15.0m,
                MonthlyBudgetLimit = 180000m,
                IsActive = true
            };

            await context.CompanyFleets.AddRangeAsync(fleet1, fleet2);
            await context.SaveChangesAsync();
        }

        var defaultFleet = await context.CompanyFleets.FirstAsync();

        // 4. Seed Users
        async Task CreateUserIfNotExist(string email, string name, string role, string phone, int? centerId = null, int? fleetId = null)
        {
            var existing = await userManager.FindByEmailAsync(email);
            if (existing == null)
            {
                var user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = name,
                    PhoneNumber = phone,
                    EmailConfirmed = true,
                    RoleType = Enum.Parse<UserRoleType>(role),
                    ServiceCenterId = centerId,
                    CompanyFleetId = fleetId,
                    Address = "123 MG Road",
                    City = "Bangalore",
                    State = "Karnataka",
                    PostalCode = "560001",
                    IsActive = true
                };

                var result = await userManager.CreateAsync(user, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, role);
                }
            }
        }

        await CreateUserIfNotExist("admin@vehicleservice.com", "Alexander Wright (Administrator)", "Administrator", "+91 99001 00001");
        await CreateUserIfNotExist("advisor@vehicleservice.com", "Vikram Malhotra (Service Advisor)", "ServiceAdvisor", "+91 99001 00002", defaultCenter.Id);
        await CreateUserIfNotExist("mechanic@vehicleservice.com", "John Doe (Lead Master Mechanic)", "Mechanic", "+91 99001 00003", defaultCenter.Id);
        await CreateUserIfNotExist("mechanic2@vehicleservice.com", "David Miller (Diagnostic Specialist)", "Mechanic", "+91 99001 00004", defaultCenter.Id);
        await CreateUserIfNotExist("inventory@vehicleservice.com", "Priya Nair (Inventory Manager)", "InventoryManager", "+91 99001 00005", defaultCenter.Id);
        await CreateUserIfNotExist("finance@vehicleservice.com", "Rahul Sengupta (Finance Manager)", "FinanceManager", "+91 99001 00006");
        await CreateUserIfNotExist("fleet@vehicleservice.com", "Amit Patel (Apex Fleet Operations Manager)", "FleetManager", "+91 99001 00007", null, defaultFleet.Id);
        await CreateUserIfNotExist("customer@vehicleservice.com", "Rohan Mehta (Individual Customer)", "Customer", "+91 99001 00008");
        await CreateUserIfNotExist("sarah.customer@vehicleservice.com", "Sarah Jenkins (Premium Customer)", "Customer", "+91 99001 00009");

        var customerUser = await userManager.FindByEmailAsync("customer@vehicleservice.com");
        var sarahUser = await userManager.FindByEmailAsync("sarah.customer@vehicleservice.com");
        var advisorUser = await userManager.FindByEmailAsync("advisor@vehicleservice.com");
        var mechanicUser = await userManager.FindByEmailAsync("mechanic@vehicleservice.com");

        // 5. Seed Service Types
        if (!await context.ServiceTypes.AnyAsync())
        {
            var services = new List<ServiceType>
            {
                new ServiceType
                {
                    Name = "General Periodic Service",
                    Code = "SRV-GEN-01",
                    Description = "Comprehensive 40-point vehicle health check, engine oil & filter replacement, brake fluid top-up, spark plug & battery inspection.",
                    EstimatedDurationHours = 2.5m,
                    BasePrice = 1999m,
                    LaborCharges = 1200m,
                    ApplicableTaxPercent = 18m,
                    WarrantyPeriodDays = 90,
                    Category = "Periodic Maintenance",
                    RecommendedPartsSummary = "Synthetic Engine Oil (3.5L), Oil Filter, Air Filter",
                    IsActive = true
                },
                new ServiceType
                {
                    Name = "Full Synthetic Engine Oil & Filter Change",
                    Code = "SRV-OIL-02",
                    Description = "Drain old engine oil, flush engine chamber, replace OEM oil filter, refill 5W-30 fully synthetic engine oil.",
                    EstimatedDurationHours = 1.0m,
                    BasePrice = 999m,
                    LaborCharges = 450m,
                    ApplicableTaxPercent = 18m,
                    WarrantyPeriodDays = 60,
                    Category = "Fluid & Filter",
                    RecommendedPartsSummary = "Fully Synthetic Engine Oil (4L), Premium Oil Filter",
                    IsActive = true
                },
                new ServiceType
                {
                    Name = "Comprehensive Brake System Service & Pad Replacement",
                    Code = "SRV-BRK-03",
                    Description = "Front and rear brake disc inspection, caliper greasing, brake pad replacement, bleeding brake fluid lines.",
                    EstimatedDurationHours = 2.0m,
                    BasePrice = 1499m,
                    LaborCharges = 950m,
                    ApplicableTaxPercent = 18m,
                    WarrantyPeriodDays = 180,
                    Category = "Brake System",
                    RecommendedPartsSummary = "Ceramic Front Brake Pads, DOT-4 Brake Fluid 500ml",
                    IsActive = true
                },
                new ServiceType
                {
                    Name = "Air Conditioning HVAC Deep Cleaning & Gas Refill",
                    Code = "SRV-AC-04",
                    Description = "AC compressor test, evaporator coil antimicrobial cleaning, cabin pollen filter replacement, R134a refrigerant recharge.",
                    EstimatedDurationHours = 1.5m,
                    BasePrice = 1799m,
                    LaborCharges = 800m,
                    ApplicableTaxPercent = 18m,
                    WarrantyPeriodDays = 90,
                    Category = "Climate Control",
                    RecommendedPartsSummary = "Activated Carbon Cabin Filter, R134a Refrigerant Canister",
                    IsActive = true
                },
                new ServiceType
                {
                    Name = "3D Computerized Wheel Alignment & Dynamic Balancing",
                    Code = "SRV-WHL-05",
                    Description = "Laser computerized 4-wheel alignment check, camber/caster adjustment, wheel balancing with lead weights.",
                    EstimatedDurationHours = 1.0m,
                    BasePrice = 899m,
                    LaborCharges = 500m,
                    ApplicableTaxPercent = 18m,
                    WarrantyPeriodDays = 30,
                    Category = "Tyres & Suspension",
                    RecommendedPartsSummary = "Balancing Weights Assortment",
                    IsActive = true
                },
                new ServiceType
                {
                    Name = "Full Vehicle OBD-II Computer Diagnostics",
                    Code = "SRV-DIAG-06",
                    Description = "Complete electronic scan of Engine ECU, ABS, Airbag, Transmission TCM, sensor error code clearing.",
                    EstimatedDurationHours = 1.0m,
                    BasePrice = 1200m,
                    LaborCharges = 800m,
                    ApplicableTaxPercent = 18m,
                    WarrantyPeriodDays = 30,
                    Category = "Diagnostics",
                    RecommendedPartsSummary = "None",
                    IsActive = true
                }
            };

            await context.ServiceTypes.AddRangeAsync(services);
            await context.SaveChangesAsync();
        }

        // 6. Seed Service Packages
        if (!await context.ServicePackages.AnyAsync())
        {
            var genService = await context.ServiceTypes.FirstAsync(s => s.Code == "SRV-GEN-01");
            var acService = await context.ServiceTypes.FirstAsync(s => s.Code == "SRV-AC-04");
            var wheelService = await context.ServiceTypes.FirstAsync(s => s.Code == "SRV-WHL-05");

            var pkg1 = new ServicePackage
            {
                Name = "Executive Annual Care Package",
                Description = "All-inclusive yearly package: General Periodic Service + AC Deep Cleaning + 3D Wheel Alignment & Balancing.",
                PackagePrice = 4199m,
                OriginalTotalPrice = 5496m,
                SavingsPercent = 23.6m,
                ValidityDays = 365,
                IsActive = true
            };

            await context.ServicePackages.AddAsync(pkg1);
            await context.SaveChangesAsync();

            context.ServicePackageItems.AddRange(
                new ServicePackageItem { ServicePackageId = pkg1.Id, ServiceTypeId = genService.Id },
                new ServicePackageItem { ServicePackageId = pkg1.Id, ServiceTypeId = acService.Id },
                new ServicePackageItem { ServicePackageId = pkg1.Id, ServiceTypeId = wheelService.Id }
            );
            await context.SaveChangesAsync();
        }

        // 7. Seed Coupons
        if (!await context.Coupons.AnyAsync())
        {
            context.Coupons.AddRange(
                new Coupon { Code = "WELCOME500", Title = "First Service Welcome Discount", Type = DiscountType.FixedAmount, Value = 500m, MinimumBillAmount = 2000m, MaxDiscountLimit = 500m, ExpiryDate = DateTime.UtcNow.AddYears(1), IsActive = true },
                new Coupon { Code = "FLEET15", Title = "Corporate Fleet Flat 15% Off", Type = DiscountType.Percentage, Value = 15.0m, MinimumBillAmount = 5000m, MaxDiscountLimit = 3000m, ExpiryDate = DateTime.UtcNow.AddYears(1), IsActive = true }
            );
            await context.SaveChangesAsync();
        }

        // 8. Seed Spare Parts Inventory
        if (!await context.InventoryParts.AnyAsync())
        {
            var parts = new List<InventoryPart>
            {
                new InventoryPart
                {
                    PartNumber = "BP-HY-101",
                    Name = "Front Ceramic Brake Pad Set",
                    Category = "Brakes",
                    Manufacturer = "Bosch Automotive",
                    Supplier = "AutoParts Direct India",
                    CompatibleVehicles = "Hyundai Creta, Kia Seltos, Hyundai Verna",
                    CostPrice = 2800m,
                    SellingPrice = 4500m,
                    AvailableQuantity = 14,
                    ReservedQuantity = 2,
                    ReorderLevel = 5,
                    WarehouseLocation = "Bay A - Rack 03",
                    ServiceCenterId = defaultCenter.Id,
                    WarrantyPeriodDays = 180,
                    IsActive = true
                },
                new InventoryPart
                {
                    PartNumber = "OIL-SYN-5W30",
                    Name = "Mobil 1 Fully Synthetic 5W-30 (4 Litres)",
                    Category = "Fluids",
                    Manufacturer = "Mobil / ExxonMobil",
                    Supplier = "Apex Petrochem Ltd",
                    CompatibleVehicles = "Universal Petrol & Diesel Cars",
                    CostPrice = 1850m,
                    SellingPrice = 2950m,
                    AvailableQuantity = 28,
                    ReservedQuantity = 4,
                    ReorderLevel = 8,
                    WarehouseLocation = "Liquid Storage Tank #2",
                    ServiceCenterId = defaultCenter.Id,
                    WarrantyPeriodDays = 90,
                    IsActive = true
                },
                new InventoryPart
                {
                    PartNumber = "FIL-OIL-008",
                    Name = "High Performance Engine Oil Filter",
                    Category = "Filters",
                    Manufacturer = "Mann-Filter",
                    Supplier = "Global Spares Warehouse",
                    CompatibleVehicles = "Hyundai, Kia, Maruti Suzuki, Toyota",
                    CostPrice = 220m,
                    SellingPrice = 480m,
                    AvailableQuantity = 35,
                    ReservedQuantity = 5,
                    ReorderLevel = 10,
                    WarehouseLocation = "Bay B - Shelf 01",
                    ServiceCenterId = defaultCenter.Id,
                    WarrantyPeriodDays = 90,
                    IsActive = true
                },
                new InventoryPart
                {
                    PartNumber = "BAT-AGM-65AH",
                    Name = "Exide AGM Maintenance-Free 65Ah Battery",
                    Category = "Electrical",
                    Manufacturer = "Exide Industries",
                    Supplier = "PowerSource Distribution",
                    CompatibleVehicles = "SUVs, Sedans, Commercial Light Vans",
                    CostPrice = 5200m,
                    SellingPrice = 7800m,
                    AvailableQuantity = 3, // Low stock demo!
                    ReservedQuantity = 1,
                    ReorderLevel = 5,
                    WarehouseLocation = "Battery Safe Rack 01",
                    ServiceCenterId = defaultCenter.Id,
                    WarrantyPeriodDays = 730,
                    IsActive = true
                }
            };

            await context.InventoryParts.AddRangeAsync(parts);
            await context.SaveChangesAsync();
        }

        // 9. Seed Vehicles
        if (!await context.Vehicles.AnyAsync())
        {
            var veh1 = new Vehicle
            {
                RegistrationNumber = "KA01MJ4589",
                VIN = "MALC3411KM1234567",
                Make = "Hyundai",
                Model = "Creta",
                Variant = "SX (O) 1.5 Turbo Petrol",
                ManufacturingYear = 2023,
                Color = "Titan Grey",
                FuelType = VehicleFuelType.Petrol,
                Transmission = TransmissionType.DualClutch,
                CurrentMileage = 21500,
                InsuranceProvider = "HDFC ERGO General Insurance",
                InsurancePolicyNumber = "POL-2023-887410",
                InsuranceExpiryDate = DateTime.UtcNow.AddMonths(7),
                CustomerId = customerUser?.Id,
                LastServiceMileage = 10000,
                LastServiceDate = DateTime.UtcNow.AddMonths(-6),
                NextServiceDueMileage = 20000,
                NextServiceDueDate = DateTime.UtcNow.AddDays(-15) // Overdue!
            };

            var veh2 = new Vehicle
            {
                RegistrationNumber = "KA03NB9821",
                VIN = "MB1F6866LN9876543",
                Make = "Toyota",
                Model = "Innova Hycross",
                Variant = "ZX Hybrid",
                ManufacturingYear = 2024,
                Color = "Attitude Black Mica",
                FuelType = VehicleFuelType.Hybrid,
                Transmission = TransmissionType.CVT,
                CurrentMileage = 14200,
                InsuranceProvider = "ICICI Lombard",
                InsurancePolicyNumber = "POL-2024-112233",
                InsuranceExpiryDate = DateTime.UtcNow.AddMonths(10),
                CustomerId = sarahUser?.Id,
                LastServiceMileage = 5000,
                LastServiceDate = DateTime.UtcNow.AddMonths(-4),
                NextServiceDueMileage = 15000,
                NextServiceDueDate = DateTime.UtcNow.AddMonths(2)
            };

            var fleetVeh1 = new Vehicle
            {
                RegistrationNumber = "KA02FL1001",
                VIN = "MA3A54321FL110001",
                Make = "Maruti Suzuki",
                Model = "Super Carry / Eeco Cargo",
                Variant = "CNG Commercial Van",
                ManufacturingYear = 2023,
                Color = "White",
                FuelType = VehicleFuelType.CNG,
                Transmission = TransmissionType.Manual,
                CurrentMileage = 48500,
                InsuranceProvider = "Bajaj Allianz Corporate Fleet",
                InsurancePolicyNumber = "CORP-FL-9001",
                InsuranceExpiryDate = DateTime.UtcNow.AddMonths(8),
                CompanyFleetId = defaultFleet.Id,
                LastServiceMileage = 40000,
                LastServiceDate = DateTime.UtcNow.AddMonths(-3),
                NextServiceDueMileage = 50000,
                NextServiceDueDate = DateTime.UtcNow.AddMonths(1)
            };

            await context.Vehicles.AddRangeAsync(veh1, veh2, fleetVeh1);
            await context.SaveChangesAsync();
        }

        var customerVeh = await context.Vehicles.FirstAsync(v => v.RegistrationNumber == "KA01MJ4589");
        var sarahVeh = await context.Vehicles.FirstAsync(v => v.RegistrationNumber == "KA03NB9821");
        var genSrv = await context.ServiceTypes.FirstAsync(s => s.Code == "SRV-GEN-01");
        var brakePadPart = await context.InventoryParts.FirstAsync(p => p.PartNumber == "BP-HY-101");
        var engineOilPart = await context.InventoryParts.FirstAsync(p => p.PartNumber == "OIL-SYN-5W30");
        var oilFilterPart = await context.InventoryParts.FirstAsync(p => p.PartNumber == "FIL-OIL-008");
        var firstBay = await context.ServiceBays.FirstAsync();

        // 10. Seed Warranties
        if (!await context.Warranties.AnyAsync())
        {
            var war1 = new Warranty
            {
                WarrantyNumber = "WAR-HY-2023-9901",
                Type = WarrantyType.Vehicle,
                VehicleId = customerVeh.Id,
                Provider = "Hyundai Extended Shield",
                CoverageTerms = "5-Year / 100,000 KM Bumper-to-Bumper Warranty covering Powertrain, Engine ECU, ABS, and Electricals.",
                CoveredItemsSummary = "Engine, Transmission, Suspension, Electricals, ECU, Brake Caliper",
                StartDate = DateTime.UtcNow.AddYears(-1),
                EndDate = DateTime.UtcNow.AddYears(4),
                MaxMileageLimit = 100000,
                Status = WarrantyStatus.Active
            };
            await context.Warranties.AddAsync(war1);
            await context.SaveChangesAsync();
        }

        // 11. Seed Active Service Appointments & Job Cards
        if (!await context.ServiceAppointments.AnyAsync())
        {
            var apt1 = new ServiceAppointment
            {
                AppointmentNumber = "APT-2026-00101",
                VehicleId = customerVeh.Id,
                CustomerId = customerUser!.Id,
                ServiceCenterId = defaultCenter.Id,
                ServiceBayId = firstBay.Id,
                ServiceTypeId = genSrv.Id,
                AppointmentDate = DateTime.UtcNow.Date,
                TimeSlot = "09:00 AM - 11:00 AM",
                Status = AppointmentStatus.InProgress,
                Priority = PriorityLevel.High,
                CustomerNotes = "Slight squeaking noise from front wheels when braking. Please inspect thoroughly.",
                RequiresPickupDrop = true,
                PickupAddress = "Flat 402, Skyline Residency, Indiranagar, Bangalore"
            };

            await context.ServiceAppointments.AddAsync(apt1);
            await context.SaveChangesAsync();

            context.PartReservations.AddRange(
                new PartReservation { InventoryPartId = engineOilPart.Id, AppointmentId = apt1.Id, Quantity = 1 },
                new PartReservation { InventoryPartId = oilFilterPart.Id, AppointmentId = apt1.Id, Quantity = 1 }
            );

            var jc1 = new ServiceJobCard
            {
                JobCardNumber = "JC-2026-00125",
                AppointmentId = apt1.Id,
                VehicleId = customerVeh.Id,
                ServiceAdvisorId = advisorUser!.Id,
                MechanicId = mechanicUser!.Id,
                ServiceBayId = firstBay.Id,
                Status = AppointmentStatus.InProgress,
                OdometerIn = 21500,
                WorkStartedAt = DateTime.UtcNow.AddHours(-2),
                TotalLaborHours = 2.5m,
                AdvisorObservations = "Vehicle inspection confirmed front brake pad wear at 85%. Additional front brake pad replacement required.",
                MechanicNotes = "Drained engine oil, replaced oil filter, cleaned air filter chamber. Commencing front brake pad overhaul."
            };
            await context.ServiceJobCards.AddAsync(jc1);
            await context.SaveChangesAsync();

            var insp1 = new VehicleInspection
            {
                JobCardId = jc1.Id,
                VehicleId = customerVeh.Id,
                InspectedByUserId = advisorUser.Id,
                Mileage = 21500,
                FuelLevel = "65%",
                EngineCondition = "Good - Smooth idle",
                BrakeCondition = "NeedsAttention - Front pads worn down to 2.5mm",
                TyreCondition = "Good - 6mm tread depth",
                BatteryCondition = "Good - 12.6V healthy",
                FluidLevels = "Engine oil dark, brake fluid clear",
                ACPerformance = "Cooling at 7°C vent output",
                ExistingDamages = "Minor 2cm scratch on rear left bumper corner.",
                InspectionSummary = "Routine periodic service approved. Recommend immediate front brake pad replacement."
            };
            await context.VehicleInspections.AddAsync(insp1);
            await context.SaveChangesAsync();

            var est1 = new RepairEstimate
            {
                EstimateNumber = "EST-2026-0034",
                JobCardId = jc1.Id,
                PreparedByUserId = advisorUser.Id,
                TotalPartsCost = 4500m + 2950m + 480m,
                TotalLaborCost = 1200m + 950m,
                TaxAmount = 1814.40m,
                DiscountAmount = 500m,
                GrandTotal = 11394.40m,
                ApprovalStatus = EstimateApprovalStatus.Approved,
                CustomerNotes = "Approved front brake pad replacement. Please proceed with genuine Bosch pads.",
                CustomerRespondedAt = DateTime.UtcNow.AddHours(-1)
            };
            await context.RepairEstimates.AddAsync(est1);
            await context.SaveChangesAsync();

            context.EstimateItems.AddRange(
                new EstimateItem { RepairEstimateId = est1.Id, Description = "General Periodic Service Package Labor", ItemType = EstimateItemType.Labor, LaborCharges = 1200m, LineTotal = 1416m, IsApprovedByCustomer = true },
                new EstimateItem { RepairEstimateId = est1.Id, Description = "Mobil 1 Fully Synthetic 5W-30 Oil (4L)", ItemType = EstimateItemType.Part, InventoryPartId = engineOilPart.Id, Quantity = 1, UnitPrice = 2950m, LineTotal = 3481m, IsApprovedByCustomer = true },
                new EstimateItem { RepairEstimateId = est1.Id, Description = "OEM Engine Oil Filter", ItemType = EstimateItemType.Part, InventoryPartId = oilFilterPart.Id, Quantity = 1, UnitPrice = 480m, LineTotal = 566.40m, IsApprovedByCustomer = true },
                new EstimateItem { RepairEstimateId = est1.Id, Description = "Front Ceramic Brake Pad Set Replacement", ItemType = EstimateItemType.Part, InventoryPartId = brakePadPart.Id, Quantity = 1, UnitPrice = 4500m, LaborCharges = 950m, LineTotal = 6431m, IsApprovedByCustomer = true }
            );

            context.WorkLogs.Add(new WorkLog { JobCardId = jc1.Id, MechanicId = mechanicUser.Id, TaskDescription = "Initial multi-point diagnostic and drain engine oil", HoursSpent = 1.0m, LoggedAt = DateTime.UtcNow.AddHours(-2) });
            context.PartConsumptions.Add(new PartConsumption { JobCardId = jc1.Id, InventoryPartId = engineOilPart.Id, Quantity = 1, UnitPrice = 2950m, LineTotal = 2950m, LoggedByUserId = mechanicUser.Id });

            // Seed Roadside Request
            var rsa1 = new RoadsideAssistanceRequest
            {
                RequestNumber = "RSA-2026-0044",
                CustomerId = customerUser.Id,
                VehicleId = customerVeh.Id,
                RequestType = RoadsideRequestType.FlatTyre,
                Status = RoadsideStatus.TechnicianDispatched,
                LocationAddress = "Near Silk Board Flyover Junction, Outer Ring Road, Bangalore",
                Latitude = 12.9176,
                Longitude = 77.6238,
                ContactPhone = "+91 99001 00008",
                IssueDescription = "Rear right tyre flat on highway. Mobile repair assistance needed.",
                AssignedTechnicianName = "Patrol Unit #4 - Rajesh",
                AssignedTechnicianPhone = "+91 98450 11223",
                TowTruckPlate = "KA05TR8899",
                EstimatedArrivalTime = DateTime.UtcNow.AddMinutes(20)
            };
            await context.RoadsideAssistanceRequests.AddAsync(rsa1);

            context.Notifications.AddRange(
                new Notification { UserId = customerUser.Id, Title = "Repair Estimate Approved", Message = "Your estimate EST-2026-0034 for Hyundai Creta (KA01MJ4589) is approved. Repairs in progress.", Type = NotificationType.Estimate, ActionUrl = "/Customer/TrackJob/1", IsRead = false },
                new Notification { UserId = customerUser.Id, Title = "Preventive Maintenance Due", Message = "Your Hyundai Creta (KA01MJ4589) is due for its 20,000 KM periodic service check.", Type = NotificationType.MaintenanceDue, ActionUrl = "/Customer/BookService?vehicleId=" + customerVeh.Id, IsRead = false }
            );

            await context.SaveChangesAsync();
        }
    }
}