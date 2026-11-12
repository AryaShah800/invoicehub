using InvoiceHub.Infrastructure.Persistence;
using InvoiceHub.Infrastructure.Persistence.Interceptors;
using InvoiceHub.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.IntegrationTests.Api;

/// <summary>
/// Hosts the real API against a throwaway LocalDB database built from the real migrations.
/// Settings go in through environment variables so they are in place before Program.cs reads them.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-test-signing-key-0123456789-abcdef";

    private static readonly string[] EnvironmentKeys = ["ConnectionStrings__InvoiceHub", "Jwt__SigningKey"];

    private readonly string _connectionString;

    public ApiFactory()
    {
        var database = $"InvoiceHub_Api_Test_{Guid.NewGuid():N}";
        _connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
        Environment.SetEnvironmentVariable("ConnectionStrings__InvoiceHub", _connectionString);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", SigningKey);
    }

    /// <summary>
    /// A context scoped to one tenant, for seeding data that has no endpoint yet. It enforces the same
    /// tenant rules as the API, so a test cannot accidentally write across tenants.
    /// </summary>
    public InvoiceHubDbContext CreateDbContext(Guid tenantId)
    {
        var tenant = new FakeTenantProvider { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<InvoiceHubDbContext>()
            .UseSqlServer(_connectionString)
            .AddInterceptors(new TenantWriteGuardInterceptor(tenant), new AuditInterceptor(TimeProvider.System))
            .Options;
        return new InvoiceHubDbContext(options, tenant);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoiceHubDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InvoiceHubDbContext>();
            await db.Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
        foreach (var key in EnvironmentKeys)
            Environment.SetEnvironmentVariable(key, null);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
