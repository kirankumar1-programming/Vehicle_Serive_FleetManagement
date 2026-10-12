using VehicleService.Domain.Entities;

namespace VehicleService.Application.Interfaces;

public interface ICouponService
{
    Task<IReadOnlyList<Coupon>> GetAllCouponsAsync();
    Task<IReadOnlyList<Coupon>> GetActiveCouponsAsync();
    Task<Coupon?> GetCouponByIdAsync(int id);
    Task<Coupon?> GetCouponByCodeAsync(string code);
    Task<(bool IsValid, decimal DiscountAmount, string Message)> ValidateCouponAsync(string code, decimal totalAmount);
    Task<Coupon> CreateCouponAsync(Coupon coupon);
    Task UpdateCouponAsync(Coupon coupon);
    Task ToggleCouponStatusAsync(int id);
}
