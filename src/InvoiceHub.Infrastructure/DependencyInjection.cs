using InvoiceHub.Application.Auth;
using InvoiceHub.Infrastructure.Identity;
using InvoiceHub.Infrastructure.Persistence;
using InvoiceHub.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("InvoiceHub")
            ?? throw new InvalidOperationException("Connection string 'InvoiceHub' is not configured.");

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<TenantWriteGuardInterceptor>();

        services.AddDbContext<InvoiceHubDbContext>((sp, options) =>
            options
                .UseSqlServer(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(InvoiceHubDbContext).Assembly.FullName))
                .AddInterceptors(
                    sp.GetRequiredService<TenantWriteGuardInterceptor>(),
                    sp.GetRequiredService<AuditInterceptor>()));

        services.AddSingleton(JwtOptions.Load(configuration));
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<InvoiceHubDbContext>();

        return services;
    }
}
