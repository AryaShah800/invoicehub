using FluentValidation;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InvoiceHub.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessClock(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        // Bind the legacy key first so existing deployments keep their offset. The new section wins.
        services.AddOptions<BusinessCalendarOptions>()
            .Bind(configuration.GetSection("BackgroundJobs"))
            .Bind(configuration.GetSection(BusinessCalendarOptions.Section));
        services.TryAddSingleton<IBusinessClock, BusinessClock>();
        return services;
    }

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
