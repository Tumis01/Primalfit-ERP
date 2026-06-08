using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class pocorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConvertedFromPONumber",
                table: "PurchaseOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDirectInvoice",
                table: "PurchaseOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityBilled",
                table: "PurchaseOrderLines",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityReceived",
                table: "PurchaseOrderLines",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConvertedFromPONumber",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "IsDirectInvoice",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "QuantityBilled",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "QuantityReceived",
                table: "PurchaseOrderLines");
        }
    }
}
