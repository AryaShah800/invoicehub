using InvoiceHub.Application.Abstractions;
using InvoiceHub.Infrastructure.Persistence;
using InvoiceHub.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Benchmarks;

/// <summary>A tenant provider with a fixed tenant, for tools that run outside an HTTP request.</summary>
internal sealed class FixedTenantProvider(Guid? tenantId) : ITenantProvider
{
    public Guid? TenantId { get; } = tenantId;
}

internal static class BenchDatabase
{
    /// <summary>
    /// LocalDB by default; set INVOICEHUB_BENCH_CONNECTION to point somewhere else.
    /// This is a separate database from development and tests.
    /// </summary>
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("INVOICEHUB_BENCH_CONNECTION")
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=InvoiceHub_Bench;Trusted_Connection=True;TrustServerCertificate=True";

    public static InvoiceHubDbContext CreateContext(Guid? tenantId)
    {
        var tenant = new FixedTenantProvider(tenantId);
        var options = new DbContextOptionsBuilder<InvoiceHubDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new TenantWriteGuardInterceptor(tenant), new AuditInterceptor(TimeProvider.System))
            .Options;
        return new InvoiceHubDbContext(options, tenant);
    }
}
