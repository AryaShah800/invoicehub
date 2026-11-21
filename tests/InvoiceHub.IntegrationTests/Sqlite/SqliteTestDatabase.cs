using InvoiceHub.Application.Abstractions;
using InvoiceHub.Infrastructure.Persistence;
using InvoiceHub.Infrastructure.Persistence.Interceptors;
using InvoiceHub.IntegrationTests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.IntegrationTests.Sqlite;

/// <summary>
/// A throwaway SQLite file built with EnsureCreated, using the same provider wiring as the app.
/// A file (not :memory:) so several contexts see the same data, which the concurrency tests need.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"invoicehub-test-{Guid.NewGuid():N}.db");

    public SqliteTestDatabase()
    {
        using var context = CreateContext(new FakeTenantProvider());
        context.Database.EnsureCreated();
    }

    public string ConnectionString => $"Data Source={_path};Pooling=False";

    public InvoiceHubDbContext CreateContext(ITenantProvider tenant)
    {
        var builder = new DbContextOptionsBuilder<InvoiceHubDbContext>();
        DatabaseProviderConfiguration.Configure(
            builder, DatabaseProvider.Sqlite, ConnectionString,
            new TenantWriteGuardInterceptor(tenant), new AuditInterceptor(TimeProvider.System));
        return new InvoiceHubDbContext(builder.Options, tenant);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*"))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}
