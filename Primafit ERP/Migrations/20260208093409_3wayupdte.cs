using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class _3wayupdte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasReceipt",
                table: "PurchaseOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvoicePosted",
                table: "PurchaseOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasReceipt",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "IsInvoicePosted",
                table: "PurchaseOrders");
        }
    }
}
