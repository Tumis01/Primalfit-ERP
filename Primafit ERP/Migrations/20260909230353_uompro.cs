using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class uompro : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>("QuantityInUom", "StockLedgers", type: "decimal(18,4)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>("UomConversionFactor", "StockLedgers", type: "decimal(18,4)", nullable: false, defaultValue: 1m);
            migrationBuilder.AddColumn<Guid>("UomId", "StockLedgers", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>("UomName", "StockLedgers", type: "nvarchar(max)", nullable: false, defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("QuantityInUom", "StockLedgers");
            migrationBuilder.DropColumn("UomConversionFactor", "StockLedgers");
            migrationBuilder.DropColumn("UomId", "StockLedgers");
            migrationBuilder.DropColumn("UomName", "StockLedgers");
        }
    }
}
