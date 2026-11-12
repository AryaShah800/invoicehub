using InvoiceHub.Application.Abstractions;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.IntegrationTests.Persistence;

/// <summary>Tenant provider whose tenant the test sets directly.</summary>
public sealed class FakeTenantProvider : ITenantProvider
{
    public Guid? TenantId { get; set; }
}

/// <summary>
/// A throwaway LocalDB database created by applying the real migrations,
/// and dropped when the fixture is disposed.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _connectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database=InvoiceHub_Test_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True";

    public TestDatabase()
    {
        using var context = CreateContext(new FakeTenantProvider());
        context.Database.Migrate();
    }

    public InvoiceHubDbContext CreateContext(ITenantProvider tenant)
    {
        var options = new DbContextOptionsBuilder<InvoiceHubDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        return new InvoiceHubDbContext(options, tenant);
    }

    public void Dispose()
    {
        using var context = CreateContext(new FakeTenantProvider());
        context.Database.EnsureDeleted();
    }
}
