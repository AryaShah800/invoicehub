using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOverdueMarkAndRecurringInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "OverdueMarkedOn",
                table: "Invoices",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IntervalMonths = table.Column<int>(type: "int", nullable: false),
                    FirstRunDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NextRunDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueInDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringInvoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecurringInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringInvoiceLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringInvoiceLines_RecurringInvoices_RecurringInvoiceId",
                        column: x => x.RecurringInvoiceId,
                        principalTable: "RecurringInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoiceLines_ProductId",
                table: "RecurringInvoiceLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoiceLines_RecurringInvoiceId",
                table: "RecurringInvoiceLines",
                column: "RecurringInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoices_CustomerId",
                table: "RecurringInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoices_TenantId_IsActive_NextRunDate",
                table: "RecurringInvoices",
                columns: new[] { "TenantId", "IsActive", "NextRunDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringInvoiceLines");

            migrationBuilder.DropTable(
                name: "RecurringInvoices");

            migrationBuilder.DropColumn(
                name: "OverdueMarkedOn",
                table: "Invoices");
        }
    }
}
