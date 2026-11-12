using InvoiceHub.Infrastructure.Persistence;
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

    public ApiFactory()
    {
        var database = $"InvoiceHub_Api_Test_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable("ConnectionStrings__InvoiceHub",
            $"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;TrustServerCertificate=True");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", SigningKey);
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
