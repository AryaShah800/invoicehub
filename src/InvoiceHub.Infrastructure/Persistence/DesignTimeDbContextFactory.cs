using InvoiceHub.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InvoiceHub.Infrastructure.Persistence;

/// <summary>
/// Lets the EF Core tools (migrations add, database update, migrations script) build the context without
/// starting the whole API, so they need no JWT key or webhook secret. A CI pipeline can then generate the
/// migration script without any secrets. The connection string comes from the environment variable
/// ConnectionStrings__InvoiceHub and falls back to LocalDB for local work.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<InvoiceHubDbContext>
{
    private const string LocalDb =
        "Server=(localdb)\\MSSQLLocalDB;Database=InvoiceHub;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public InvoiceHubDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__InvoiceHub") ?? LocalDb;

        var options = new DbContextOptionsBuilder<InvoiceHubDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(InvoiceHubDbContext).Assembly.FullName))
            .Options;

        return new InvoiceHubDbContext(options, new NoTenant());
    }

    /// <summary>Schema work has no tenant. Nothing here reads or writes tenant data.</summary>
    private sealed class NoTenant : ITenantProvider
    {
        public Guid? TenantId => null;
    }
}
