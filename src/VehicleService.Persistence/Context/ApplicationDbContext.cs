using System.Text.Json;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Common;
using VehicleService.Domain.Entities;

namespace VehicleService.Persistence.Context;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    private readonly ICurrentUserService? _currentUserService;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService? currentUserService = null) : base(options)
    {
        _currentUserService = currentUserService;
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
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<ChatMessageHistory> ChatMessages => Set<ChatMessageHistory>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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

        // Appointment Unique AppointmentNumber & Concurrency
        builder.Entity<ServiceAppointment>(b =>
        {
            b.HasIndex(a => a.AppointmentNumber).IsUnique();
            b.HasIndex(a => new { a.ServiceCenterId, a.ServiceBayId, a.AppointmentDate, a.TimeSlot });
            b.Property(a => a.RowVersion).IsRowVersion();
            b.HasOne(a => a.Customer).WithMany(u => u.AppointmentsAsCustomer).HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(a => a.Vehicle).WithMany(v => v.ServiceAppointments).HasForeignKey(a => a.VehicleId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(a => a.ServiceCenter).WithMany(c => c.Appointments).HasForeignKey(a => a.ServiceCenterId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(a => a.ServiceBay).WithMany(sb => sb.Appointments).HasForeignKey(a => a.ServiceBayId).OnDelete(DeleteBehavior.SetNull);
        });

        // JobCard
        builder.Entity<ServiceJobCard>(b =>
        {
            b.HasIndex(j => j.JobCardNumber).IsUnique();
            b.Property(j => j.RowVersion).IsRowVersion();
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
            b.Property(i => i.RowVersion).IsRowVersion();
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

        // Knowledge Documents & Chunks
        builder.Entity<KnowledgeDocument>(b =>
        {
            b.HasIndex(d => d.Category);
            b.HasMany(d => d.Chunks).WithOne(c => c.KnowledgeDocument).HasForeignKey(c => c.KnowledgeDocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DocumentChunk>(b =>
        {
            b.HasIndex(c => c.KnowledgeDocumentId);
        });

        builder.Entity<ChatMessageHistory>(b =>
        {
            b.HasIndex(m => m.SessionId);
            b.HasIndex(m => m.Timestamp);
        });

        // RefreshTokens
        builder.Entity<RefreshToken>(b =>
        {
            b.HasIndex(r => r.Token).IsUnique();
            b.HasIndex(r => r.JwtId);
            b.HasOne(r => r.User).WithMany(u => u.RefreshTokens).HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var auditEntries = OnBeforeSaveChanges();
        var result = await base.SaveChangesAsync(cancellationToken);
        if (auditEntries.Count > 0)
        {
            await OnAfterSaveChanges(auditEntries, cancellationToken);
        }
        return result;
    }

    public override int SaveChanges()
    {
        return SaveChangesAsync().GetAwaiter().GetResult();
    }

    private class AuditEntry
    {
        public EntityEntry Entry { get; set; } = null!;
        public string EntityName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? EntityId { get; set; }
        public Dictionary<string, object?> OldValues { get; } = new();
        public Dictionary<string, object?> NewValues { get; } = new();
        public List<PropertyEntry> TemporaryProperties { get; } = new();
    }

    private List<AuditEntry> OnBeforeSaveChanges()
    {
        ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                continue;

            if (entry.Entity is BaseEntity baseEntity)
            {
                if (entry.State == EntityState.Added)
                {
                    baseEntity.CreatedAt = DateTime.UtcNow;
                }
                else if (entry.State == EntityState.Modified)
                {
                    baseEntity.UpdatedAt = DateTime.UtcNow;
                }
            }

            if ((entry.State == EntityState.Added || entry.State == EntityState.Modified) &&
                Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer")
            {
                var rowVersionProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "RowVersion");
                if (rowVersionProp != null)
                {
                    rowVersionProp.CurrentValue = Guid.NewGuid().ToByteArray();
                }
            }

            var auditEntry = new AuditEntry
            {
                Entry = entry,
                EntityName = entry.Entity.GetType().Name
            };

            switch (entry.State)
            {
                case EntityState.Added:
                    auditEntry.Action = "Created";
                    foreach (var property in entry.Properties)
                    {
                        if (property.IsTemporary)
                        {
                            auditEntry.TemporaryProperties.Add(property);
                            continue;
                        }
                        if (property.Metadata.IsPrimaryKey())
                        {
                            auditEntry.EntityId = property.CurrentValue?.ToString();
                        }
                        auditEntry.NewValues[property.Metadata.Name] = property.CurrentValue;
                    }
                    auditEntries.Add(auditEntry);
                    break;

                case EntityState.Deleted:
                    auditEntry.Action = "Deleted";
                    foreach (var property in entry.Properties)
                    {
                        if (property.Metadata.IsPrimaryKey())
                        {
                            auditEntry.EntityId = property.OriginalValue?.ToString();
                        }
                        auditEntry.OldValues[property.Metadata.Name] = property.OriginalValue;
                    }
                    auditEntries.Add(auditEntry);
                    break;

                case EntityState.Modified:
                    var isDeletedProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == nameof(BaseEntity.IsDeleted));
                    if (isDeletedProp != null && isDeletedProp.IsModified && true.Equals(isDeletedProp.CurrentValue))
                    {
                        auditEntry.Action = "SoftDeleted";
                    }
                    else
                    {
                        auditEntry.Action = "Updated";
                    }

                    foreach (var property in entry.Properties)
                    {
                        if (property.Metadata.IsPrimaryKey())
                        {
                            auditEntry.EntityId = property.CurrentValue?.ToString();
                            continue;
                        }

                        if (property.IsModified)
                        {
                            auditEntry.OldValues[property.Metadata.Name] = property.OriginalValue;
                            auditEntry.NewValues[property.Metadata.Name] = property.CurrentValue;
                        }
                    }
                    auditEntries.Add(auditEntry);
                    break;
            }
        }

        return auditEntries;
    }

    private async Task OnAfterSaveChanges(List<AuditEntry> auditEntries, CancellationToken cancellationToken)
    {
        var logs = new List<AuditLog>();
        var userId = _currentUserService?.UserId;
        var userName = _currentUserService?.UserName;
        var ipAddress = _currentUserService?.IpAddress;

        foreach (var auditEntry in auditEntries)
        {
            foreach (var prop in auditEntry.TemporaryProperties)
            {
                if (prop.Metadata.IsPrimaryKey())
                {
                    auditEntry.EntityId = prop.CurrentValue?.ToString();
                }
                auditEntry.NewValues[prop.Metadata.Name] = prop.CurrentValue;
            }

            if (string.IsNullOrEmpty(auditEntry.EntityId))
            {
                var key = auditEntry.Entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString();
                auditEntry.EntityId = key;
            }

            var log = new AuditLog
            {
                UserId = userId,
                UserName = userName,
                Action = auditEntry.Action,
                EntityName = auditEntry.EntityName,
                EntityId = auditEntry.EntityId,
                OldValues = auditEntry.OldValues.Count > 0 ? JsonSerializer.Serialize(auditEntry.OldValues) : null,
                NewValues = auditEntry.NewValues.Count > 0 ? JsonSerializer.Serialize(auditEntry.NewValues) : null,
                IpAddress = ipAddress,
                Timestamp = DateTime.UtcNow
            };

            logs.Add(log);
        }

        if (logs.Count > 0)
        {
            AuditLogs.AddRange(logs);
            await base.SaveChangesAsync(cancellationToken);
        }
    }
}
