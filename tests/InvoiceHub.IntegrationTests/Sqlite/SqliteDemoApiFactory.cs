using InvoiceHub.Infrastructure.Demo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.IntegrationTests.Sqlite;

/// <summary>
/// Hosts the real API the way the hosted demo runs it: SQLite file, demo mode on. Settings are process-wide
/// environment variables (Program.cs reads them before the host is built), so this collection never runs in
/// parallel with the SQL Server one.
/// </summary>
public sealed class SqliteDemoApiFactory : WebApplicationFactory<Program>
{
    private static readonly Dictionary<string, string> Settings = new()
    {
        ["Database__Provider"] = "Sqlite",
        ["Demo__Enabled"] = "true",
        ["Jwt__SigningKey"] = "integration-test-signing-key-0123456789-abcdef",
        ["PaymentWebhook__Secret"] = "integration-test-webhook-secret-0123456789",
        ["BackgroundJobs__Enabled"] = "false",
    };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"invoicehub-demo-test-{Guid.NewGuid():N}.db");

    public SqliteDemoApiFactory()
    {
        foreach (var (key, value) in Settings)
            Environment.SetEnvironmentVariable(key, value);
        Environment.SetEnvironmentVariable("ConnectionStrings__InvoiceHubSqlite", $"Data Source={_path}");
    }

    public DemoOptions Demo => Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DemoOptions>>().Value;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services => services.Configure<InvoiceHub.Api.RateLimiting.RateLimitSettings>(settings =>
        {
            settings.AuthPermitsPerMinute = 100_000;
            settings.WebhookPermitsPerMinute = 100_000;
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        foreach (var key in Settings.Keys.Append("ConnectionStrings__InvoiceHubSqlite"))
            Environment.SetEnvironmentVariable(key, null);
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*"))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqliteDemoCollection : ICollectionFixture<SqliteDemoApiFactory>
{
    public const string Name = "SqliteDemo";
}
