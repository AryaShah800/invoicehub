using System.Data;
using System.Globalization;
using Dapper;
using InvoiceHub.Application.Reports;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InvoiceHub.Infrastructure.Reports;

/// <summary>
/// Calls the usp_GstSummaryReport stored procedure with Dapper. Dapper does not apply the EF Core
/// tenant query filters, so the tenant id is always passed to the procedure explicitly.
/// On SQLite (the hosted demo) there are no stored procedures, so the same report is a plain query whose rows
/// are summed in C#: SQLite keeps decimals as text, and SQL SUM over text would turn the money into floating point.
/// </summary>
public sealed class DapperGstReportReader(InvoiceHubDbContext db) : IGstReportReader
{
    public async Task<IReadOnlyList<GstSummaryRow>> GetSummaryAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (db.Database.IsSqlite())
            return await GetSqliteSummaryAsync(tenantId, from, to, cancellationToken);

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

    private sealed class SqliteLineRow
    {
        public string InvoiceId { get; set; } = "";
        public string GstRate { get; set; } = "";
        public string TaxableValue { get; set; } = "";
        public string CgstAmount { get; set; } = "";
        public string SgstAmount { get; set; } = "";
        public string IgstAmount { get; set; } = "";
    }

    private async Task<IReadOnlyList<GstSummaryRow>> GetSqliteSummaryAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT i.Id AS InvoiceId, l.GstRate, l.TaxableValue, l.CgstAmount, l.SgstAmount, l.IgstAmount
            FROM Invoices i
            JOIN InvoiceLines l ON l.InvoiceId = i.Id
            WHERE i.TenantId = @TenantId
              AND l.TenantId = @TenantId
              AND i.IssueDate >= @FromDate
              AND i.IssueDate <= @ToDate
              AND i.Status IN ('Issued', 'PartiallyPaid', 'Paid')
            """;

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;
        if (wasClosed)
            await connection.OpenAsync(cancellationToken);

        try
        {
            // EF stores a Guid as upper-case text and a DateOnly as yyyy-MM-dd; compare in exactly that form.
            var command = new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId.ToString().ToUpperInvariant(),
                    FromDate = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ToDate = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken: cancellationToken);

            var lines = await connection.QueryAsync<SqliteLineRow>(command);
            return lines
                .Select(l => (l.InvoiceId, Rate: Parse(l.GstRate), Taxable: Parse(l.TaxableValue), Cgst: Parse(l.CgstAmount), Sgst: Parse(l.SgstAmount), Igst: Parse(l.IgstAmount)))
                .GroupBy(l => l.Rate)
                .OrderBy(g => g.Key)
                .Select(g => new GstSummaryRow(
                    g.Key,
                    g.Select(l => l.InvoiceId).Distinct().Count(),
                    g.Sum(l => l.Taxable), g.Sum(l => l.Cgst), g.Sum(l => l.Sgst), g.Sum(l => l.Igst)))
                .ToList();
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }
    }

    private static decimal Parse(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
