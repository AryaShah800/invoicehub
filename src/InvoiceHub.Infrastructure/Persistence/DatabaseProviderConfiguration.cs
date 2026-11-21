using InvoiceHub.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace InvoiceHub.Infrastructure.Persistence;

public enum DatabaseProvider
{
    /// <summary>The default: SQL Server with the EF Core migrations and the stored procedure.</summary>
    SqlServer,

    /// <summary>
    /// Used by the hosted demo, where no SQL Server is available. The schema is created with EnsureCreated
    /// because the migrations contain SQL Server specific DDL.
    /// </summary>
    Sqlite,
}

/// <summary>Reads the provider setting and applies the provider, and its interceptors, to a context. One place, so the app and the tests wire it the same way.</summary>
public static class DatabaseProviderConfiguration
{
    public const string ProviderKey = "Database:Provider";
    public const string DefaultSqliteConnectionString = "Data Source=invoicehub-demo.db";

    public static DatabaseProvider Read(IConfiguration configuration)
    {
        var value = configuration[ProviderKey];
        if (string.IsNullOrWhiteSpace(value))
            return DatabaseProvider.SqlServer;

        return Enum.TryParse<DatabaseProvider>(value.Trim(), ignoreCase: true, out var provider)
            ? provider
            : throw new InvalidOperationException($"{ProviderKey} must be 'SqlServer' or 'Sqlite', not '{value}'.");
    }

    /// <summary>SQL Server uses ConnectionStrings:InvoiceHub. SQLite has its own key so the LocalDB default in appsettings is never used by mistake.</summary>
    public static string ReadConnectionString(IConfiguration configuration, DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Sqlite => configuration.GetConnectionString("InvoiceHubSqlite") ?? DefaultSqliteConnectionString,
        _ => configuration.GetConnectionString("InvoiceHub")
            ?? throw new InvalidOperationException("Connection string 'InvoiceHub' is not configured."),
    };

    public static void Configure(
        DbContextOptionsBuilder options,
        DatabaseProvider provider,
        string connectionString,
        TenantWriteGuardInterceptor tenantWriteGuard,
        AuditInterceptor audit)
    {
        switch (provider)
        {
            case DatabaseProvider.Sqlite:
                options.UseSqlite(connectionString);
                // The model differs per provider (see InvoiceHubDbContext), so the cached model must be keyed by provider.
                options.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, ProviderModelCacheKeyFactory>();
                // SQLite cannot generate a rowversion, so the concurrency token is refreshed here on every save.
                options.AddInterceptors(tenantWriteGuard, audit, new SqliteConcurrencyTokenInterceptor());
                break;
            default:
                options.UseSqlServer(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(InvoiceHubDbContext).Assembly.FullName));
                options.AddInterceptors(tenantWriteGuard, audit);
                break;
        }
    }
}

/// <summary>Gives each provider its own cached EF model, so a process that uses both (the test run) never mixes them.</summary>
internal sealed class ProviderModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), "sqlite", designTime);
}
