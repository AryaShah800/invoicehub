using InvoiceHub.Application;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Reports;
using InvoiceHub.Infrastructure.Identity;
using InvoiceHub.Infrastructure.Persistence;
using InvoiceHub.Infrastructure.Persistence.Interceptors;
using InvoiceHub.Infrastructure.Reports;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = DatabaseProviderConfiguration.Read(configuration);
        var connectionString = DatabaseProviderConfiguration.ReadConnectionString(configuration, provider);

        services.AddBusinessClock(configuration);
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<TenantWriteGuardInterceptor>();

        services.AddDbContext<InvoiceHubDbContext>((sp, options) =>
            DatabaseProviderConfiguration.Configure(
                options, provider, connectionString,
                sp.GetRequiredService<TenantWriteGuardInterceptor>(),
                sp.GetRequiredService<AuditInterceptor>()));

        services.AddSingleton<InvoiceHub.Infrastructure.Demo.DemoDataSeeder>();

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<InvoiceHubDbContext>());

        services.AddScoped<IGstReportReader, DapperGstReportReader>();
        services.AddSingleton<InvoiceHub.Application.Invoices.Pdf.IInvoicePdfRenderer, QuestPdfInvoiceRenderer>();
        services.AddScoped<EfGstReportReader>();

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
