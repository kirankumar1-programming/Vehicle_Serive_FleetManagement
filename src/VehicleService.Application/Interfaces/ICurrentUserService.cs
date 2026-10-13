namespace VehicleService.Application.Interfaces;

public interface ICurrentUserService
{
    string? UserId { get; }
    string? UserName { get; }
    IReadOnlyList<string> Roles { get; }
    string? IpAddress { get; }
    bool IsAuthenticated { get; }
}
