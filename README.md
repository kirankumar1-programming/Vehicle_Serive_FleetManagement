# Enterprise Vehicle Service & Fleet Management System

A production-grade, enterprise-ready **Vehicle Service & Fleet Management System** built with **ASP.NET Core (net10.0)**, adhering strictly to **Clean Architecture** and Domain-Driven Design (DDD) principles.

The platform provides dedicated operational portals for **7 distinct enterprise roles**, comprehensive REST APIs with JWT authentication, real-time background processing services, concurrency-safe inventory and booking controls, and complete DevOps CI/CD automation for Azure and Docker environments.

---

## Table of Contents
1. [Architecture Overview](#architecture-overview)
2. [Role Portals & Capabilities](#role-portals--capabilities)
3. [7 Advanced Client Scenarios & Implementation](#7-advanced-client-scenarios--implementation)
4. [Pre-Seeded Demonstration Accounts & 1-Click Switcher](#pre-seeded-demonstration-accounts--1-click-switcher)
5. [REST API & JWT Authentication](#rest-api--jwt-authentication)
6. [Background Hosted Services](#background-hosted-services)
7. [Cloud, Azure & Container Deployment](#cloud-azure--container-deployment)
8. [Quick Start Guide](#quick-start-guide)
9. [Running Tests](#running-tests)

---

## Architecture Overview

The solution is divided into 7 decoupled layers following Clean Architecture:

```
VehicleServiceFleetManagement/
+-- src/
¦   +-- VehicleService.Domain/          # Enterprise domain entities, enums, exceptions, business rules
¦   +-- VehicleService.Application/     # Application services, DTOs, repository interfaces, business workflows
¦   +-- VehicleService.Persistence/     # EF Core ApplicationDbContext, Fluent API mappings, Seeder, UnitOfWork
¦   +-- VehicleService.Infrastructure/  # Background services, Payment gateway mock, Blob storage, JWT token generator
¦   +-- VehicleService.API/             # RESTful API controllers, JWT authentication, Swagger OpenAPI documentation
¦   +-- VehicleService.Web/             # ASP.NET Core MVC, Razor Views, Bootstrap 5 UI, 1-Click Role Switcher
+-- tests/
¦   +-- VehicleService.Tests/           # Comprehensive xUnit & FluentAssertions test suite for all 7 scenarios
+-- .github/workflows/deploy.yml        # GitHub Actions CI/CD pipeline
+-- azure-pipelines.yml                 # Azure DevOps CI/CD pipeline
+-- Dockerfile                          # Multi-stage production container build
+-- docker-compose.yml                  # Container orchestration specification
+-- VehicleService.slnx                 # .NET Solution file
```

### Key Technical Highlights
- **Framework**: .NET 10.0 / ASP.NET Core MVC & REST APIs
- **ORM & Data**: Entity Framework Core with Fluent Configurations, Indexing, and SQLite / Azure SQL toggle (`UseSqlite: true/false`)
- **Security & RBAC**: ASP.NET Core Identity with Claims-based authorization, Dual Auth (Cookie for Portals, JWT Bearer for REST APIs)
- **Concurrency & Transaction Safety**: `IUnitOfWork` transactional boundaries with row locking & atomic reservation mechanisms.

---

## Role Portals & Capabilities

The system provides 7 dedicated operational portals:

| Role | Portal Capabilities |
| :--- | :--- |
| **?? Customer** | • **Digital Garage**: Register multiple private vehicles (Make, Model, VIN, Plate, Fuel Type, Mileage)<br>• **Booking Wizard**: Choose service center, bay, service packages, and preferred time slot<br>• **Live Servicing Tracker**: Real-time progress bar from intake to delivery<br>• **Estimate Approval**: Review itemized parts & labor costs, 1-click Approve or Reject<br>• **Invoicing & Payments**: View breakdowns with GST, pay via Mock Payment Gateway, download PDF invoices<br>• **Warranties & Roadside Assistance**: View active warranties, request emergency 24/7 SOS rescue with GPS location<br>• **Feedback**: Submit ratings, reviews, and service complaints |
| **????? Service Advisor** | • **Intake Desk**: Manage incoming vehicle appointments and create new walk-in requests<br>• **40-Point Digital Inspection**: Conduct inspection checklists with status flags (Pass/Attention/Fail)<br>• **Job Card Generation**: Convert appointments to active workshop job cards and assign mechanics<br>• **Line-Item Estimate Builder**: Prepare additional repair estimates with parts and labor quotes |
| **?? Mechanic** | • **Workshop Workbench**: View assigned jobs and vehicle service checklists<br>• **Work Progress Logging**: Update status (In Inspection, In Progress, In Quality Check, Completed)<br>• **Parts & Labor Consumption**: Record actual parts used from inventory and log precise labor hours |
| **?? Inventory Manager** | • **Spare Parts Catalog**: Manage SKUs, categories, unit prices, minimum thresholds, and bins<br>• **Stock Movement Ledger**: Track GRNs (Goods Received Notes), issues, and returns<br>• **Concurrency Stock Control**: Prevent overselling and enforce non-negative inventory thresholds<br>• **Low-Stock Alerts**: Real-time dashboard warnings when stock dips below reorder points |
| **?? Finance Manager** | • **Billing & Invoicing**: Generate automated tax invoices with itemized parts & labor charges<br>• **Mock Payment Processing**: Handle online payments with duplicate callback idempotency<br>• **Refund Management**: Process authorized partial or full refunds<br>• **Revenue Analytics**: Daily/monthly revenue, pending collections, and tax summaries |
| **?? Fleet Manager** | • **Corporate Fleet Registry**: Manage commercial trucks, vans, and passenger vehicles<br>• **Driver Allocation**: Assign drivers to vehicles with license verification<br>• **Preventive Maintenance**: Configure mileage and time-interval based servicing schedules<br>• **Fleet Cost Analytics**: Track vehicle maintenance expenditure, downtime hours, and cost-per-km |
| **?? Administrator** | • **Master KPIs**: Workshop revenue, active jobs, customer retention, bay utilization<br>• **Multi-Center & Bay Management**: Configure physical service centers, operating bays, and capacities<br>• **Catalog & Coupon Management**: Define standard service packages, pricing, and discount promo codes<br>• **User RBAC & Audit Trails**: Manage user roles and inspect security/system audit logs<br>• **Review Moderation**: Moderate customer reviews and complaints |

---

## 7 Advanced Client Scenarios & Implementation

All 7 required edge-case scenarios are explicitly handled and validated via automated unit/integration tests in `tests/VehicleService.Tests/ClientScenarioTests.cs`:

### 1. Bay & Time-Slot Double Booking Prevention
- **Challenge**: Two customers or advisors attempt to book the same service bay for overlapping time windows.
- **Solution**: `AppointmentService.BookAppointmentAsync` performs an atomic overlapping interval query `(ExistingStart < NewEnd && ExistingEnd > NewStart)` scoped to the specific Bay and Active statuses (excluding Cancelled/Completed), throwing `InvalidBookingException` on conflict.
- **Verification**: `Scenario1_DoubleBooking_PreventsDuplicateBaySlotBooking` ?

### 2. Last Spare Part Concurrency & Non-Negative Stock Constraint
- **Challenge**: Multiple mechanics attempt to claim the final stock unit of a critical spare part concurrently.
- **Solution**: `InventoryService.ReservePartsAsync` and `ConsumePartAsync` execute within a Unit of Work transaction, checking `AvailableQuantity = (QuantityOnHand - ReservedQuantity) >= requestedQuantity`. If insufficient, throws `InsufficientStockException` and prevents negative stock values.
- **Verification**: `Scenario2_LastSparePart_PreventsNegativeStock` ?

### 3. Payment Gateway Duplicate Webhook / Callback Idempotency
- **Challenge**: Payment gateway retries webhooks or customer clicks "Pay" twice, causing double ledger credits.
- **Solution**: `InvoiceService.ProcessPaymentAsync` checks if the transaction is already `Paid` or if the `TransactionReference` already exists in payment records. Duplicate callbacks return success immediately without duplicating financial transactions or invoice status updates.
- **Verification**: `Scenario3_PaymentCallbackTwice_DoesNotDuplicateTransaction` ?

### 4. Estimate Rejection Handling with Base Service Continuation
- **Challenge**: Customer rejects an additional repair estimate (e.g. brake pad replacement). The system must not abort the original scheduled service (e.g. oil change).
- **Solution**: `EstimateService.RejectEstimateAsync` marks the estimate as `Rejected` with customer notes, releases any soft-reserved additional parts back to inventory, and keeps the parent `JobCard` active so mechanics can complete standard services.
- **Verification**: `Scenario4_EstimateRejected_ProceedsOnlyWithApprovedServices` ?

### 5. Service Cancellation Atomic Bay & Inventory Release
- **Challenge**: A customer cancels an appointment with pre-allocated bays and reserved spare parts.
- **Solution**: `AppointmentService.CancelAppointmentAsync` executes an atomic workflow: updates appointment status to `Cancelled`, frees the Bay schedule, and rolls back all reserved parts via `InventoryService.ReleaseReservedPartsAsync`.
- **Verification**: `Scenario5_ServiceCancellation_ReleasesBayAndReservedParts` ?

### 6. Mechanic Availability & Active Job Overlap Prevention
- **Challenge**: An advisor inadvertently assigns a mechanic who is already actively servicing another vehicle.
- **Solution**: `JobCardService.AssignMechanicAsync` inspects existing job cards assigned to the mechanic with status `InProgress` or `InInspection`. If another active job exists, it rejects assignment with `MechanicUnavailableException`.
- **Verification**: `Scenario6_MechanicAvailability_PreventsOverlappingAssignments` ?

### 7. Warranty Coverage Verification (Date, Mileage & Covered Parts)
- **Challenge**: Customer requests warranty coverage for a repair, but warranty is expired by age, exceeded maximum mileage, or the component is explicitly excluded.
- **Solution**: `WarrantyService.ValidateWarrantyCoverageAsync` performs 3-tier validation:
  1. `DateTime.UtcNow <= EndDate`
  2. `CurrentVehicleMileage <= MaxMileage`
  3. `CoveredItems` list contains the target part category/name.
  If valid, automatically applies warranty discount to the invoice.
- **Verification**: `Scenario7_WarrantyClaim_ValidatesDateMileageAndCoveredTerms` ?

---

## Pre-Seeded Demonstration Accounts & 1-Click Switcher

For instant testing with **zero setup**, the application includes a **1-Click Demo Role Switcher bar** at the top of every page. You can also sign in manually with the following pre-seeded credentials:

| Role | Username / Email | Default Password | Initial Data Seeded |
| :--- | :--- | :--- | :--- |
| **Administrator** | `admin@vehicleservice.com` | `Admin@123` | System overview, centers, bays, package catalog, coupons |
| **Customer** | `customer@vehicleservice.com` | `Admin@123` | Registered private vehicles, live tracking job, invoices, warranty |
| **Service Advisor** | `advisor@vehicleservice.com` | `Admin@123` | Pending appointments, intake desk, estimate builder |
| **Mechanic** | `mechanic@vehicleservice.com` | `Admin@123` | Active assigned job cards, inspection checklist, labor logging |
| **Inventory Manager** | `inventory@vehicleservice.com` | `Admin@123` | Parts catalog, low-stock items, stock movement logs |
| **Finance Manager** | `finance@vehicleservice.com` | `Admin@123` | Pending invoices, payment history, revenue reporting |
| **Fleet Manager** | `fleet@vehicleservice.com` | `Admin@123` | Corporate fleet vehicles, assigned drivers, preventive maintenance schedules |

---

## REST API & JWT Authentication

The application provides a comprehensive set of REST APIs for mobile applications and external integrations.

### API Endpoints Overview
- `POST /api/auth/login` - Authenticate and receive JWT Bearer token
- `GET /api/vehicles` - List user vehicles
- `POST /api/vehicles` - Register a new vehicle
- `GET /api/appointments` - Query scheduled service appointments
- `POST /api/appointments` - Book appointment with bay allocation
- `GET /api/jobcards/{id}` - Fetch real-time job card status and inspection checklist
- `GET /api/inventory/parts` - Browse parts catalog and stock levels
- `POST /api/payments/process` - Idempotent payment processing endpoint
- `POST /api/assistance/sos` - Trigger 24/7 Roadside Assistance request

### Swagger OpenAPI
When running locally in Development mode, navigate to:
```
http://localhost:5000/swagger
```

---

## Background Hosted Services

The system runs three autonomous background services (`IHostedService` / `BackgroundService`):

1. **`MaintenanceReminderBackgroundService`**: Scans vehicle maintenance schedules every 6 hours and sends automated service due alerts based on time intervals and mileage milestones.
2. **`WarrantyExpiryBackgroundService`**: Monitors active warranty policies daily and dispatches notifications 30 days prior to policy expiration.
3. **`LowStockAlertBackgroundService`**: Scans inventory stock levels hourly and generates priority notifications to Inventory Managers for parts below their minimum reorder threshold.

---

## Cloud, Azure & Container Deployment

### Azure Services Ready
- **Azure App Service**: Deployable as a Linux or Windows container web app.
- **Azure SQL Database**: Set `"UseSqlite": false` and configure `"ConnectionStrings:DefaultConnection"` with your Azure SQL connection string.
- **Azure Blob Storage**: Switch from `LocalBlobStorageService` to Azure Blob Storage SDK by providing `"AzureBlobStorage:ConnectionString"`.
- **Azure Key Vault**: Ready for Secret Management and environment variable injection in production.
- **Application Insights**: Pre-configured telemetry for request monitoring, exception tracking, and performance logging.

### DevOps CI/CD
- **Azure Pipelines**: Configured in [`azure-pipelines.yml`](azure-pipelines.yml) (Build, test, package, and publish drop artifact).
- **GitHub Actions**: Configured in [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml) (Automatic validation on PRs and main branch).
- **Docker**: [`Dockerfile`](Dockerfile) multi-stage container build + [`docker-compose.yml`](docker-compose.yml) for 1-command startup.

---

## Quick Start Guide

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) (or .NET 9.0+)

### Running Locally (Zero Setup)
1. Clone the repository and navigate to the project root:
   ```bash
   cd c:/Task/VehicleServiceFleetManagement
   ```
2. Run the application:
   ```bash
   dotnet run --project src/VehicleService.Web/VehicleService.Web.csproj
   ```
3. Open your browser and navigate to:
   ```
   http://localhost:5000
   ```
4. Click any role button in the **Quick Role Switcher** bar at the top (e.g. *Customer*, *Service Advisor*, *Mechanic*, *Inventory*, *Finance*, *Fleet*, *Admin*) to test the portals instantly!

### Running with Docker
```bash
docker-compose up --build
```
Access the application at `http://localhost:5000`.

---

## Running Tests

To execute the test suite validating all 7 client edge-case scenarios:

```bash
dotnet test VehicleService.slnx --logger "console;verbosity=detailed"
```

### Test Results Summary
```
Passed VehicleService.Tests.ClientScenarioTests.Scenario1_DoubleBooking_PreventsDuplicateBaySlotBooking
Passed VehicleService.Tests.ClientScenarioTests.Scenario2_LastSparePart_PreventsNegativeStock
Passed VehicleService.Tests.ClientScenarioTests.Scenario3_PaymentCallbackTwice_DoesNotDuplicateTransaction
Passed VehicleService.Tests.ClientScenarioTests.Scenario4_EstimateRejected_ProceedsOnlyWithApprovedServices
Passed VehicleService.Tests.ClientScenarioTests.Scenario5_ServiceCancellation_ReleasesBayAndReservedParts
Passed VehicleService.Tests.ClientScenarioTests.Scenario6_MechanicAvailability_PreventsOverlappingAssignments
Passed VehicleService.Tests.ClientScenarioTests.Scenario7_WarrantyClaim_ValidatesDateMileageAndCoveredTerms

Total tests: 7 | Passed: 7 | Failed: 0 | Duration: < 4s
```
