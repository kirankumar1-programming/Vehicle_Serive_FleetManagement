using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Application.Services;

public class InventoryService : IInventoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private static readonly object _stockLock = new();

    public InventoryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<InventoryPartDto>> GetAllPartsAsync(int? centerId = null, string? search = null, string? category = null)
    {
        var query = _unitOfWork.Repository<InventoryPart>().Query()
            .Include(p => p.ServiceCenter)
            .AsQueryable();

        if (centerId.HasValue)
        {
            query = query.Where(p => p.ServiceCenterId == centerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p => p.PartNumber.ToLower().Contains(s) || p.Name.ToLower().Contains(s) || p.Manufacturer.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(p => p.Category.ToLower() == category.Trim().ToLower());
        }

        var list = await query.OrderBy(p => p.Name).ToListAsync();
        return list.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<InventoryPartDto>> GetLowStockPartsAsync(int? centerId = null)
    {
        var query = _unitOfWork.Repository<InventoryPart>().Query()
            .Include(p => p.ServiceCenter)
            .Where(p => p.AvailableQuantity <= p.ReorderLevel);

        if (centerId.HasValue)
        {
            query = query.Where(p => p.ServiceCenterId == centerId.Value);
        }

        var list = await query.OrderBy(p => p.AvailableQuantity).ToListAsync();
        return list.Select(MapToDto).ToList();
    }

    public async Task<InventoryPartDto?> GetPartByIdAsync(int id)
    {
        var p = await _unitOfWork.Repository<InventoryPart>().Query()
            .Include(p => p.ServiceCenter)
            .FirstOrDefaultAsync(p => p.Id == id);

        return p == null ? null : MapToDto(p);
    }

    public async Task<InventoryPartDto> CreatePartAsync(InventoryPartDto dto)
    {
        var existing = await _unitOfWork.Repository<InventoryPart>()
            .FirstOrDefaultAsync(p => p.PartNumber.ToUpper() == dto.PartNumber.ToUpper());

        if (existing != null) throw new DomainException($"Part number '{dto.PartNumber}' already exists.");

        var part = new InventoryPart
        {
            PartNumber = dto.PartNumber.ToUpper().Trim(),
            Name = dto.Name.Trim(),
            Category = dto.Category.Trim(),
            Manufacturer = dto.Manufacturer.Trim(),
            Supplier = dto.Supplier.Trim(),
            CompatibleVehicles = dto.CompatibleVehicles.Trim(),
            CostPrice = dto.CostPrice,
            SellingPrice = dto.SellingPrice,
            AvailableQuantity = dto.AvailableQuantity,
            ReservedQuantity = 0,
            ReorderLevel = dto.ReorderLevel,
            WarehouseLocation = dto.WarehouseLocation,
            ServiceCenterId = dto.ServiceCenterId,
            WarrantyPeriodDays = dto.WarrantyPeriodDays,
            IsActive = true
        };

        await _unitOfWork.Repository<InventoryPart>().AddAsync(part);
        await _unitOfWork.SaveChangesAsync();

        // Record Initial Purchase / Inward Transaction
        if (dto.AvailableQuantity > 0)
        {
            await _unitOfWork.Repository<InventoryTransaction>().AddAsync(new InventoryTransaction
            {
                InventoryPartId = part.Id,
                TransactionType = InventoryTransactionType.Purchase,
                Quantity = dto.AvailableQuantity,
                QuantityBefore = 0,
                QuantityAfter = dto.AvailableQuantity,
                UnitCost = dto.CostPrice,
                TotalAmount = dto.CostPrice * dto.AvailableQuantity,
                Notes = "Initial Stock Inward upon part registration",
                ReferenceNumber = "INIT-STOCK"
            });
            await _unitOfWork.SaveChangesAsync();
        }

        return (await GetPartByIdAsync(part.Id))!;
    }

    public async Task UpdatePartAsync(int id, InventoryPartDto dto)
    {
        var part = await _unitOfWork.Repository<InventoryPart>().GetByIdAsync(id);
        if (part == null) throw new DomainException("Part not found.");

        part.Name = dto.Name;
        part.Category = dto.Category;
        part.Manufacturer = dto.Manufacturer;
        part.Supplier = dto.Supplier;
        part.CompatibleVehicles = dto.CompatibleVehicles;
        part.CostPrice = dto.CostPrice;
        part.SellingPrice = dto.SellingPrice;
        part.ReorderLevel = dto.ReorderLevel;
        part.WarehouseLocation = dto.WarehouseLocation;
        part.WarrantyPeriodDays = dto.WarrantyPeriodDays;

        await _unitOfWork.Repository<InventoryPart>().UpdateAsync(part);
        await _unitOfWork.SaveChangesAsync();
    }

    // SCENARIO 2: Last Spare Part Concurrency & Negative Stock Prevention
    public async Task<bool> ReservePartAsync(int partId, int quantity, int appointmentId)
    {
        if (quantity <= 0) return false;

        lock (_stockLock)
        {
            var part = _unitOfWork.Repository<InventoryPart>().Query().FirstOrDefault(p => p.Id == partId);
            if (part == null) throw new DomainException("Inventory part not found.");

            if (part.AvailableQuantity < quantity)
            {
                throw new InsufficientStockException($"Cannot reserve part '{part.Name}' ({part.PartNumber}). Requested: {quantity}, Available: {part.AvailableQuantity}. Stock cannot be negative.");
            }

            part.AvailableQuantity -= quantity;
            part.ReservedQuantity += quantity;
            _unitOfWork.Repository<InventoryPart>().UpdateAsync(part).GetAwaiter().GetResult();

            var reservation = new PartReservation
            {
                InventoryPartId = partId,
                AppointmentId = appointmentId,
                Quantity = quantity,
                IsReleased = false
            };

            _unitOfWork.Repository<PartReservation>().AddAsync(reservation).GetAwaiter().GetResult();
            _unitOfWork.SaveChangesAsync().GetAwaiter().GetResult();

            return true;
        }
    }

    public async Task<bool> ReleaseReservationAsync(int reservationId)
    {
        lock (_stockLock)
        {
            var reservation = _unitOfWork.Repository<PartReservation>().Query().FirstOrDefault(r => r.Id == reservationId);
            if (reservation == null || reservation.IsReleased || reservation.IsConsumed) return false;

            var part = _unitOfWork.Repository<InventoryPart>().Query().FirstOrDefault(p => p.Id == reservation.InventoryPartId);
            if (part != null)
            {
                part.AvailableQuantity += reservation.Quantity;
                part.ReservedQuantity = Math.Max(0, part.ReservedQuantity - reservation.Quantity);
                _unitOfWork.Repository<InventoryPart>().UpdateAsync(part).GetAwaiter().GetResult();
            }

            reservation.IsReleased = true;
            reservation.ReleasedAt = DateTime.UtcNow;
            _unitOfWork.Repository<PartReservation>().UpdateAsync(reservation).GetAwaiter().GetResult();
            _unitOfWork.SaveChangesAsync().GetAwaiter().GetResult();

            return true;
        }
    }

    // SCENARIO 2: Concurrency-Safe Part Consumption
    public async Task<bool> ConsumePartAsync(int partId, int quantity, int jobCardId, string performedByUserId)
    {
        if (quantity <= 0) return false;

        lock (_stockLock)
        {
            var part = _unitOfWork.Repository<InventoryPart>().Query().FirstOrDefault(p => p.Id == partId);
            if (part == null) throw new DomainException("Part not found.");

            // Check if there was an active reservation for this appointment
            var jobCard = _unitOfWork.Repository<ServiceJobCard>().Query().FirstOrDefault(j => j.Id == jobCardId);
            PartReservation? activeReservation = null;

            if (jobCard != null)
            {
                activeReservation = _unitOfWork.Repository<PartReservation>().Query()
                    .FirstOrDefault(r => r.AppointmentId == jobCard.AppointmentId && r.InventoryPartId == partId && !r.IsReleased && !r.IsConsumed);
            }

            int beforeQty = part.AvailableQuantity + part.ReservedQuantity;

            if (activeReservation != null && activeReservation.Quantity >= quantity)
            {
                // Consume from reserved quantity
                part.ReservedQuantity = Math.Max(0, part.ReservedQuantity - quantity);
                activeReservation.IsConsumed = true;
                activeReservation.ConsumedAt = DateTime.UtcNow;
                _unitOfWork.Repository<PartReservation>().UpdateAsync(activeReservation).GetAwaiter().GetResult();
            }
            else
            {
                // Consume from available quantity - MUST NOT BE NEGATIVE
                if (part.AvailableQuantity < quantity)
                {
                    throw new InsufficientStockException($"Cannot consume part '{part.Name}'. Requested: {quantity}, Available in stock: {part.AvailableQuantity}. Inventory must not become negative.");
                }
                part.AvailableQuantity -= quantity;
            }

            _unitOfWork.Repository<InventoryPart>().UpdateAsync(part).GetAwaiter().GetResult();

            // Record consumption in JobCard
            var consumption = new PartConsumption
            {
                JobCardId = jobCardId,
                InventoryPartId = partId,
                Quantity = quantity,
                UnitPrice = part.SellingPrice,
                LineTotal = part.SellingPrice * quantity,
                LoggedByUserId = performedByUserId,
                ConsumedAt = DateTime.UtcNow
            };
            _unitOfWork.Repository<PartConsumption>().AddAsync(consumption).GetAwaiter().GetResult();

            // Record inventory movement transaction
            int afterQty = part.AvailableQuantity + part.ReservedQuantity;
            var transaction = new InventoryTransaction
            {
                InventoryPartId = partId,
                TransactionType = InventoryTransactionType.Consumption,
                Quantity = quantity,
                QuantityBefore = beforeQty,
                QuantityAfter = afterQty,
                UnitCost = part.CostPrice,
                TotalAmount = part.CostPrice * quantity,
                ReferenceNumber = jobCard?.JobCardNumber ?? $"JC-{jobCardId}",
                PerformedByUserId = performedByUserId,
                Notes = $"Consumed for repair Job Card {jobCard?.JobCardNumber}"
            };
            _unitOfWork.Repository<InventoryTransaction>().AddAsync(transaction).GetAwaiter().GetResult();

            _unitOfWork.SaveChangesAsync().GetAwaiter().GetResult();
            return true;
        }
    }

    public async Task<bool> AdjustStockAsync(AdjustStockDto dto)
    {
        lock (_stockLock)
        {
            var part = _unitOfWork.Repository<InventoryPart>().Query().FirstOrDefault(p => p.Id == dto.PartId);
            if (part == null) throw new DomainException("Part not found.");

            int before = part.AvailableQuantity;
            int after = before;

            switch (dto.TransactionType)
            {
                case InventoryTransactionType.Purchase:
                case InventoryTransactionType.Return:
                    part.AvailableQuantity += dto.Quantity;
                    after = part.AvailableQuantity;
                    break;

                case InventoryTransactionType.Damage:
                case InventoryTransactionType.Consumption:
                case InventoryTransactionType.Transfer:
                    if (part.AvailableQuantity < dto.Quantity)
                    {
                        throw new InsufficientStockException($"Cannot deduct {dto.Quantity} units. Available: {part.AvailableQuantity}. Stock cannot become negative.");
                    }
                    part.AvailableQuantity -= dto.Quantity;
                    after = part.AvailableQuantity;
                    break;

                case InventoryTransactionType.Adjustment:
                    // Direct set
                    part.AvailableQuantity = Math.Max(0, dto.Quantity);
                    after = part.AvailableQuantity;
                    break;
            }

            _unitOfWork.Repository<InventoryPart>().UpdateAsync(part).GetAwaiter().GetResult();

            var txn = new InventoryTransaction
            {
                InventoryPartId = dto.PartId,
                TransactionType = dto.TransactionType,
                Quantity = dto.Quantity,
                QuantityBefore = before,
                QuantityAfter = after,
                UnitCost = dto.UnitCost > 0 ? dto.UnitCost : part.CostPrice,
                TotalAmount = (dto.UnitCost > 0 ? dto.UnitCost : part.CostPrice) * dto.Quantity,
                ReferenceNumber = dto.ReferenceNumber ?? "MANUAL-ADJ",
                PerformedByUserId = dto.PerformedByUserId,
                Notes = dto.Notes
            };

            _unitOfWork.Repository<InventoryTransaction>().AddAsync(txn).GetAwaiter().GetResult();
            _unitOfWork.SaveChangesAsync().GetAwaiter().GetResult();
            return true;
        }
    }

    public async Task<IReadOnlyList<InventoryTransaction>> GetTransactionsAsync(int? partId = null)
    {
        var query = _unitOfWork.Repository<InventoryTransaction>().Query()
            .Include(t => t.InventoryPart)
            .AsQueryable();

        if (partId.HasValue)
        {
            query = query.Where(t => t.InventoryPartId == partId.Value);
        }

        return await query.OrderByDescending(t => t.CreatedAt).Take(100).ToListAsync();
    }

    private static InventoryPartDto MapToDto(InventoryPart p)
    {
        return new InventoryPartDto
        {
            Id = p.Id,
            PartNumber = p.PartNumber,
            Name = p.Name,
            Category = p.Category,
            Manufacturer = p.Manufacturer,
            Supplier = p.Supplier,
            CompatibleVehicles = p.CompatibleVehicles,
            CostPrice = p.CostPrice,
            SellingPrice = p.SellingPrice,
            AvailableQuantity = p.AvailableQuantity,
            ReservedQuantity = p.ReservedQuantity,
            ReorderLevel = p.ReorderLevel,
            WarehouseLocation = p.WarehouseLocation,
            ServiceCenterId = p.ServiceCenterId,
            ServiceCenterName = p.ServiceCenter?.Name,
            WarrantyPeriodDays = p.WarrantyPeriodDays,
            IsActive = p.IsActive
        };
    }
}

public class VehicleInspectionService : IInspectionService
{
    private readonly IUnitOfWork _unitOfWork;

    public VehicleInspectionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<VehicleInspection?> GetInspectionByJobCardIdAsync(int jobCardId)
    {
        return await _unitOfWork.Repository<VehicleInspection>().Query()
            .Include(i => i.InspectionPhotos)
            .Include(i => i.InspectedByUser)
            .FirstOrDefaultAsync(i => i.JobCardId == jobCardId);
    }

    public async Task<VehicleInspection> SaveInspectionAsync(VehicleInspection inspection)
    {
        var existing = await _unitOfWork.Repository<VehicleInspection>()
            .FirstOrDefaultAsync(i => i.JobCardId == inspection.JobCardId);

        if (existing == null)
        {
            await _unitOfWork.Repository<VehicleInspection>().AddAsync(inspection);
        }
        else
        {
            existing.Mileage = inspection.Mileage;
            existing.FuelLevel = inspection.FuelLevel;
            existing.EngineCondition = inspection.EngineCondition;
            existing.BrakeCondition = inspection.BrakeCondition;
            existing.TyreCondition = inspection.TyreCondition;
            existing.BatteryCondition = inspection.BatteryCondition;
            existing.FluidLevels = inspection.FluidLevels;
            existing.ACPerformance = inspection.ACPerformance;
            existing.SuspensionCondition = inspection.SuspensionCondition;
            existing.ElectricalsCondition = inspection.ElectricalsCondition;
            existing.ExistingDamages = inspection.ExistingDamages;
            existing.InspectionSummary = inspection.InspectionSummary;
            await _unitOfWork.Repository<VehicleInspection>().UpdateAsync(existing);
            inspection = existing;
        }

        // Advance JobCard status to UnderInspection / EstimateCreated if needed
        var jobCard = await _unitOfWork.Repository<ServiceJobCard>().GetByIdAsync(inspection.JobCardId);
        if (jobCard != null && jobCard.Status == AppointmentStatus.VehicleReceived)
        {
            jobCard.Status = AppointmentStatus.UnderInspection;
            await _unitOfWork.Repository<ServiceJobCard>().UpdateAsync(jobCard);
        }

        await _unitOfWork.SaveChangesAsync();
        return inspection;
    }

    public async Task AddInspectionPhotoAsync(int inspectionId, string photoUrl, string title, string category)
    {
        var photo = new InspectionPhoto
        {
            InspectionId = inspectionId,
            PhotoUrl = photoUrl,
            Title = title,
            PhotoCategory = category
        };

        await _unitOfWork.Repository<InspectionPhoto>().AddAsync(photo);
        await _unitOfWork.SaveChangesAsync();
    }
}