using System.Data;
using Dapper;
using InvoiceHub.Application.Reports;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InvoiceHub.Infrastructure.Reports;

/// <summary>
/// Calls the usp_GstSummaryReport stored procedure with Dapper. Dapper does not apply the EF Core
/// tenant query filters, so the tenant id is always passed to the procedure explicitly.
/// </summary>
public sealed class DapperGstReportReader(InvoiceHubDbContext db) : IGstReportReader
{
    public async Task<IReadOnlyList<GstSummaryRow>> GetSummaryAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        // Reuse the context's connection (and its transaction, if any); EF opens and closes it as needed.
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;
        if (wasClosed)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var command = new CommandDefinition(
                "dbo.usp_GstSummaryReport",
                new { TenantId = tenantId, FromDate = from.ToDateTime(TimeOnly.MinValue), ToDate = to.ToDateTime(TimeOnly.MinValue) },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                commandTimeout: db.Database.GetCommandTimeout(), // honour the context's timeout; null means the default
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken);

            var rows = await connection.QueryAsync<GstSummaryRow>(command);
            return rows.ToList();
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }
    }
}
