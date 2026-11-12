using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGstAmounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SellerStateCode",
                table: "Invoices",
                type: "char(2)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "CgstAmount",
                table: "InvoiceLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IgstAmount",
                table: "InvoiceLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SgstAmount",
                table: "InvoiceLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Rows created before this migration: take the seller state from the tenant, then work out the
            // tax with the same rules as GstCalculator (SQL ROUND on decimals rounds half away from zero).
            migrationBuilder.Sql(
                "UPDATE i SET i.SellerStateCode = t.StateCode FROM Invoices i JOIN Tenants t ON t.Id = i.TenantId");
            migrationBuilder.Sql(@"
UPDATE l SET
    l.CgstAmount = CASE WHEN i.SellerStateCode = i.PlaceOfSupplyStateCode THEN ROUND(l.TaxableValue * l.GstRate / 200, 2) ELSE 0 END,
    l.SgstAmount = CASE WHEN i.SellerStateCode = i.PlaceOfSupplyStateCode THEN ROUND(l.TaxableValue * l.GstRate / 200, 2) ELSE 0 END,
    l.IgstAmount = CASE WHEN i.SellerStateCode = i.PlaceOfSupplyStateCode THEN 0 ELSE ROUND(l.TaxableValue * l.GstRate / 100, 2) END
FROM InvoiceLines l JOIN Invoices i ON i.Id = l.InvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SellerStateCode",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CgstAmount",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "IgstAmount",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "SgstAmount",
                table: "InvoiceLines");
        }
    }
}
