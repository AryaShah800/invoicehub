using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGstSummaryReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_IssueDate",
                table: "Invoices",
                columns: new[] { "TenantId", "IssueDate" })
                .Annotation("SqlServer:Include", new[] { "Status" });

            // GST collected per slab for invoices issued in a date range. Draft and cancelled invoices are
            // excluded. Called from Dapper, which skips the EF tenant filters, so @TenantId is mandatory.
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_GstSummaryReport
    @TenantId uniqueidentifier,
    @FromDate date,
    @ToDate   date
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        l.GstRate                    AS GstRate,
        COUNT(DISTINCT i.Id)         AS InvoiceCount,
        SUM(l.TaxableValue)          AS TaxableValue,
        SUM(l.CgstAmount)            AS CgstAmount,
        SUM(l.SgstAmount)            AS SgstAmount,
        SUM(l.IgstAmount)            AS IgstAmount
    FROM dbo.Invoices i
    JOIN dbo.InvoiceLines l ON l.InvoiceId = i.Id
    WHERE i.TenantId = @TenantId
      AND l.TenantId = @TenantId
      AND i.IssueDate >= @FromDate
      AND i.IssueDate <= @ToDate
      AND i.Status IN ('Issued', 'PartiallyPaid', 'Paid')
    GROUP BY l.GstRate
    ORDER BY l.GstRate;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_GstSummaryReport");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_TenantId_IssueDate",
                table: "Invoices");
        }
    }
}
