using Microsoft.EntityFrameworkCore;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;
using VehicleService.Domain.Enums;
using VehicleService.Domain.Exceptions;

namespace VehicleService.Application.Services;

public class CouponService : ICouponService
{
    private readonly IUnitOfWork _unitOfWork;

    public CouponService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<Coupon>> GetAllCouponsAsync()
    {
        return await _unitOfWork.Repository<Coupon>().Query()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Coupon>> GetActiveCouponsAsync()
    {
        var now = DateTime.UtcNow;
        return await _unitOfWork.Repository<Coupon>().Query()
            .Where(c => c.IsActive && c.ExpiryDate >= now && c.TimesUsed < c.UsageLimit)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<Coupon?> GetCouponByIdAsync(int id)
    {
        return await _unitOfWork.Repository<Coupon>().GetByIdAsync(id);
    }

    public async Task<Coupon?> GetCouponByCodeAsync(string code)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();
        return await _unitOfWork.Repository<Coupon>().Query()
            .FirstOrDefaultAsync(c => c.Code.ToUpper() == normalizedCode);
    }

    public async Task<(bool IsValid, decimal DiscountAmount, string Message)> ValidateCouponAsync(string code, decimal totalAmount)
    {
        if (string.IsNullOrWhiteSpace(code))
            return (false, 0, "Coupon code is required.");

        var coupon = await GetCouponByCodeAsync(code);
        if (coupon == null)
            return (false, 0, "Invalid coupon code.");

        if (!coupon.IsActive)
            return (false, 0, "This coupon is no longer active.");

        if (coupon.ExpiryDate < DateTime.UtcNow)
            return (false, 0, "This coupon has expired.");

        if (coupon.TimesUsed >= coupon.UsageLimit)
            return (false, 0, "This coupon has reached its maximum usage limit.");

        if (totalAmount < coupon.MinimumBillAmount)
            return (false, 0, $"Minimum bill amount of ₹{coupon.MinimumBillAmount:N2} required to apply this coupon.");

        decimal discount = 0;
        if (coupon.Type == DiscountType.Percentage)
        {
            discount = (totalAmount * coupon.Value) / 100m;
            if (coupon.MaxDiscountLimit > 0 && discount > coupon.MaxDiscountLimit)
            {
                discount = coupon.MaxDiscountLimit;
            }
        }
        else // Fixed
        {
            discount = Math.Min(coupon.Value, totalAmount);
            if (coupon.MaxDiscountLimit > 0 && discount > coupon.MaxDiscountLimit)
            {
                discount = coupon.MaxDiscountLimit;
            }
        }

        return (true, discount, $"Coupon '{coupon.Code}' applied successfully! Savings: ₹{discount:N2}");
    }

    public async Task<Coupon> CreateCouponAsync(Coupon coupon)
    {
        coupon.Code = coupon.Code.Trim().ToUpperInvariant();
        await _unitOfWork.Repository<Coupon>().AddAsync(coupon);
        await _unitOfWork.SaveChangesAsync();
        return coupon;
    }

    public async Task UpdateCouponAsync(Coupon coupon)
    {
        coupon.Code = coupon.Code.Trim().ToUpperInvariant();
        coupon.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Repository<Coupon>().UpdateAsync(coupon);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ToggleCouponStatusAsync(int id)
    {
        var coupon = await _unitOfWork.Repository<Coupon>().GetByIdAsync(id);
        if (coupon == null)
            throw new EntityNotFoundException($"Coupon with ID {id} was not found.");

        coupon.IsActive = !coupon.IsActive;
        coupon.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Repository<Coupon>().UpdateAsync(coupon);
        await _unitOfWork.SaveChangesAsync();
    }
}
