namespace VehicleService.Domain.Constants;

public static class UserRoles
{
    public const string Administrator = "Administrator";
    public const string ServiceAdvisor = "ServiceAdvisor";
    public const string Mechanic = "Mechanic";
    public const string InventoryManager = "InventoryManager";
    public const string FinanceManager = "FinanceManager";
    public const string FleetManager = "FleetManager";
    public const string Customer = "Customer";

    // Composite role groupings for RBAC authorization policies
    public const string Staff = $"{Administrator},{ServiceAdvisor},{Mechanic},{InventoryManager},{FinanceManager},{FleetManager}";
    public const string AdvisorOrAdmin = $"{Administrator},{ServiceAdvisor}";
    public const string MechanicOrAdvisorOrAdmin = $"{Administrator},{ServiceAdvisor},{Mechanic}";
    public const string InventoryOrAdmin = $"{Administrator},{InventoryManager}";
    public const string FinanceOrAdmin = $"{Administrator},{FinanceManager}";
    public const string FleetOrAdmin = $"{Administrator},{FleetManager}";
    public const string CustomerOrAdmin = $"{Administrator},{Customer}";
}
