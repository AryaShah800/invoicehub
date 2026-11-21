using InvoiceHub.Infrastructure.Persistence;
using InvoiceHub.Infrastructure.Persistence.Interceptors;
using InvoiceHub.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.IntegrationTests.Sqlite;

/// <summary>
/// Guards the SQL Server path: the model EF builds for SQL Server (with the provider switch in place) must still
/// match the migration snapshot exactly, so adding SQLite support cannot silently require a new migration.
/// </summary>
public sealed class SqlServerModelUnchangedTests
{
    [Fact]
    public void Sql_server_model_has_no_pending_changes_against_the_migration_snapshot()
    {
        var tenant = new FakeTenantProvider();
        var builder = new DbContextOptionsBuilder<InvoiceHubDbContext>();
        DatabaseProviderConfiguration.Configure(
            builder, DatabaseProvider.SqlServer, "Server=unused;Database=unused",
            new TenantWriteGuardInterceptor(tenant), new AuditInterceptor(TimeProvider.System));
        using var context = new InvoiceHubDbContext(builder.Options, tenant);

        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        snapshot = context.GetService<IModelRuntimeInitializer>().Initialize(snapshot);
        var current = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(snapshot.GetRelationalModel(), current);
        Assert.Empty(differences);
    }
}
