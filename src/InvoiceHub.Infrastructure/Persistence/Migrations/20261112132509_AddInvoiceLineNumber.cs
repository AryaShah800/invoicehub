using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLineNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LineNumber",
                table: "InvoiceLines",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LineNumber",
                table: "InvoiceLines");
        }
    }
}
