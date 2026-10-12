using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using VehicleService.API.Middleware;
using VehicleService.Application;
using VehicleService.Domain.Constants;
using VehicleService.Domain.Entities;
using VehicleService.Infrastructure;
using VehicleService.Persistence;
using VehicleService.Persistence.Context;
using VehicleService.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

// 1. Data Access Layer (Persistence)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=VehicleService_Api.db";
builder.Services.AddPersistenceServices(builder.Configuration, connectionString);

// 2. Identity with Robust Security & Lockout Settings
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

// 3. Business Logic Layer (Application)
builder.Services.AddApplicationServices();

// 4. Infrastructure Layer
builder.Services.AddInfrastructureServices(builder.Configuration);

// 5. REST API Authentication (JWT)
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "VehicleServiceFleetManagementSecureKey2026!#$LongEnoughForHmacSha256Signature";
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "VehicleServiceAPI",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "VehicleServiceClient",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

// 6. Role-Based Authorization Policies
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

// 7. Rate Limiter for Authentication Endpoints (Brute-force protection)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthRateLimit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 15,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 8. Swagger with JWT Bearer Authentication definition
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Vehicle Service & Fleet Management API",
        Version = "v1",
        Description = "Enterprise Vehicle Service & Fleet Management API secured with JWT Bearer Authentication & RBAC"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. \r\n\r\n Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer eyJhbGciOi...\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

// CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// Seed Database on startup
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    await DbInitializer.SeedAsync(context, userManager, roleManager);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Vehicle Service API v1");
        c.RoutePrefix = "swagger";
    });
}

// Security Middleware Pipeline
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();