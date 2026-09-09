using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VehicleService.Domain.Entities;

namespace VehicleService.Persistence.Context;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<ServiceCenter> ServiceCenters => Set<ServiceCenter>();
    public DbSet<ServiceBay> ServiceBays => Set<ServiceBay>();
    public DbSet<CompanyFleet> CompanyFleets => Set<CompanyFleet>();
    public DbSet<DriverAssignment> DriverAssignments => Set<DriverAssignment>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<ServicePackage> ServicePackages => Set<ServicePackage>();
    public DbSet<ServicePackageItem> ServicePackageItems => Set<ServicePackageItem>();
    public DbSet<DiscountRule> DiscountRules => Set<DiscountRule>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<ServiceAppointment> ServiceAppointments => Set<ServiceAppointment>();
    public DbSet<ServiceJobCard> ServiceJobCards => Set<ServiceJobCard>();
    public DbSet<VehicleInspection> VehicleInspections => Set<VehicleInspection>();
    public DbSet<InspectionPhoto> InspectionPhotos => Set<InspectionPhoto>();
    public DbSet<RepairEstimate> RepairEstimates => Set<RepairEstimate>();
    public DbSet<EstimateItem> EstimateItems => Set<EstimateItem>();
    public DbSet<WorkLog> WorkLogs => Set<WorkLog>();
    public DbSet<PartConsumption> PartConsumptions => Set<PartConsumption>();
    public DbSet<InventoryPart> InventoryParts => Set<InventoryPart>();
    public DbSet<PartReservation> PartReservations => Set<PartReservation>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<Warranty> Warranties => Set<Warranty>();
    public DbSet<WarrantyClaim> WarrantyClaims => Set<WarrantyClaim>();
    public DbSet<RoadsideAssistanceRequest> RoadsideAssistanceRequests => Set<RoadsideAssistanceRequest>();
    public DbSet<RoadsideLog> RoadsideLogs => Set<RoadsideLog>();
    public DbSet<CustomerReview> CustomerReviews => Set<CustomerReview>();
    public DbSet<CustomerComplaint> CustomerComplaints => Set<CustomerComplaint>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Precision configurations for decimal fields
        foreach (var property in builder.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(2);
        }

        // Vehicle indexes
        builder.Entity<Vehicle>(b =>
        {
            b.HasIndex(v => v.RegistrationNumber).IsUnique();
            b.HasIndex(v => v.VIN).IsUnique();
            b.HasOne(v => v.Customer).WithMany(u => u.OwnedVehicles).HasForeignKey(v => v.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(v => v.CompanyFleet).WithMany(f => f.Vehicles).HasForeignKey(v => v.CompanyFleetId).OnDelete(DeleteBehavior.SetNull);
        });

        // InventoryPart Unique PartNumber
        builder.Entity<InventoryPart>(b =>
        {
            b.HasIndex(p => p.PartNumber).IsUnique();
            b.Property(p => p.RowVersion).IsRowVersion();
        });

        // Appointment Unique AppointmentNumber
        builder.Entity<ServiceAppointment>(b =>
        {
            b.HasIndex(a => a.AppointmentNumber).IsUnique();
            b.HasOne(a => a.Customer).WithMany(u => u.AppointmentsAsCustomer).HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(a => a.Vehicle).WithMany(v => v.ServiceAppointments).HasForeignKey(a => a.VehicleId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(a => a.ServiceCenter).WithMany(c => c.Appointments).HasForeignKey(a => a.ServiceCenterId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(a => a.ServiceBay).WithMany(sb => sb.Appointments).HasForeignKey(a => a.ServiceBayId).OnDelete(DeleteBehavior.SetNull);
        });

        // JobCard
        builder.Entity<ServiceJobCard>(b =>
        {
            b.HasIndex(j => j.JobCardNumber).IsUnique();
            b.HasOne(j => j.Appointment).WithOne(a => a.JobCard).HasForeignKey<ServiceJobCard>(j => j.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(j => j.Vehicle).WithMany(v => v.JobCards).HasForeignKey(j => j.VehicleId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(j => j.ServiceAdvisor).WithMany(u => u.JobCardsAsAdvisor).HasForeignKey(j => j.ServiceAdvisorId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(j => j.Mechanic).WithMany(u => u.JobCardsAsMechanic).HasForeignKey(j => j.MechanicId).OnDelete(DeleteBehavior.SetNull);
        });

        // PaymentTransaction Idempotency Index
        builder.Entity<PaymentTransaction>(b =>
        {
            b.HasIndex(p => p.TransactionReference).IsUnique();
            b.HasOne(p => p.Invoice).WithMany(i => i.Payments).HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        // Invoices
        builder.Entity<Invoice>(b =>
        {
            b.HasIndex(i => i.InvoiceNumber).IsUnique();
            b.HasOne(i => i.JobCard).WithMany(j => j.Invoices).HasForeignKey(i => i.JobCardId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(i => i.Customer).WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        // Roadside Unique RequestNumber
        builder.Entity<RoadsideAssistanceRequest>(b =>
        {
            b.HasIndex(r => r.RequestNumber).IsUnique();
            b.HasOne(r => r.Customer).WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(r => r.Vehicle).WithMany(v => v.RoadsideRequests).HasForeignKey(r => r.VehicleId).OnDelete(DeleteBehavior.Restrict);
        });

        // Warranty Unique WarrantyNumber
        builder.Entity<Warranty>(b =>
        {
            b.HasIndex(w => w.WarrantyNumber).IsUnique();
            b.HasOne(w => w.Vehicle).WithMany(v => v.Warranties).HasForeignKey(w => w.VehicleId).OnDelete(DeleteBehavior.Cascade);
        });

        // Warranty Claims
        builder.Entity<WarrantyClaim>(b =>
        {
            b.HasIndex(c => c.ClaimNumber).IsUnique();
            b.HasOne(c => c.Warranty).WithMany(w => w.Claims).HasForeignKey(c => c.WarrantyId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
