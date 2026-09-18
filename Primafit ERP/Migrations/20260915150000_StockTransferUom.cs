using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    public partial class StockTransferUom : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UomId",
                table: "StockTransfers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UomName",
                table: "StockTransfers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "UomConversionFactor",
                table: "StockTransfers",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityInUom",
                table: "StockTransfers",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "UomId", table: "StockTransfers");
            migrationBuilder.DropColumn(name: "UomName", table: "StockTransfers");
            migrationBuilder.DropColumn(name: "UomConversionFactor", table: "StockTransfers");
            migrationBuilder.DropColumn(name: "QuantityInUom", table: "StockTransfers");
        }
    }
}
