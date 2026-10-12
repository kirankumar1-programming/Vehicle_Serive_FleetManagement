using Microsoft.AspNetCore.Identity;
using VehicleService.Application;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Entities;
using VehicleService.Infrastructure;
using VehicleService.Persistence;
using VehicleService.Persistence.Context;
using VehicleService.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

// 1. Data Access Layer (Persistence)
builder.Services.AddPersistenceServices(builder.Configuration);

// 2. Identity & Hardened Cookie Authentication
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;

    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
});

// 3. Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdministrator", policy => policy.RequireRole(UserRoles.Administrator));
    options.AddPolicy("RequireAdvisorOrAdmin", policy => policy.RequireRole(UserRoles.Administrator, UserRoles.ServiceAdvisor));
    options.AddPolicy("RequireMechanic", policy => policy.RequireRole(UserRoles.Administrator, UserRoles.ServiceAdvisor, UserRoles.Mechanic));
    options.AddPolicy("RequireInventory", policy => policy.RequireRole(UserRoles.Administrator, UserRoles.InventoryManager));
    options.AddPolicy("RequireFinance", policy => policy.RequireRole(UserRoles.Administrator, UserRoles.FinanceManager));
    options.AddPolicy("RequireFleet", policy => policy.RequireRole(UserRoles.Administrator, UserRoles.FleetManager));
    options.AddPolicy("RequireCustomerOrAdmin", policy => policy.RequireRole(UserRoles.Administrator, UserRoles.Customer));
});

// 4. Business Logic Layer (Application)
builder.Services.AddApplicationServices();

// 5. Infrastructure Layer
builder.Services.AddInfrastructureServices(builder.Configuration);

// 6. Presentation Layer (Web MVC)
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Seed Database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    await DbInitializer.SeedAsync(context, userManager, roleManager);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Security Headers Middleware
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        if (!headers.ContainsKey("X-Content-Type-Options"))
            headers.Append("X-Content-Type-Options", "nosniff");
        if (!headers.ContainsKey("X-Frame-Options"))
            headers.Append("X-Frame-Options", "SAMEORIGIN");
        if (!headers.ContainsKey("X-XSS-Protection"))
            headers.Append("X-XSS-Protection", "1; mode=block");
        if (!headers.ContainsKey("Referrer-Policy"))
            headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        return Task.CompletedTask;
    });
    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();