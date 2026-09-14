using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    [Migration("20260909214421_UomAndTransactionUnits")]
    public partial class UomAndTransactionUnits : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddLineUomColumns(migrationBuilder, "VendorReturnLines");
            AddLineUomColumns(migrationBuilder, "VendorBillLines");
            migrationBuilder.AddColumn<decimal>("ConversionFactorValue", "UnitOfMeasures", type: "decimal(18,4)", nullable: false, defaultValue: 1m);
            migrationBuilder.AddColumn<Guid>("ConversionUomId", "UnitOfMeasures", type: "uniqueidentifier", nullable: true);
            AddLineUomColumns(migrationBuilder, "SalesShipmentLines");
            AddLineUomColumns(migrationBuilder, "SalesOrderLines");
            AddLineUomColumns(migrationBuilder, "ReceiptRefundLines");
            AddLineUomColumns(migrationBuilder, "PurchaseOrderLines");
            migrationBuilder.AddColumn<decimal>("AlternateUomConversionFactor", "Items", type: "decimal(18,4)", nullable: false, defaultValue: 1m);
            migrationBuilder.AddColumn<Guid>("AlternateUomId", "Items", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<Guid>("UomId", "Items", type: "uniqueidentifier", nullable: true);
            AddLineUomColumns(migrationBuilder, "GoodsReceiptLines");
            AddLineUomColumns(migrationBuilder, "DebitNoteLines");
            AddLineUomColumns(migrationBuilder, "CreditNoteLines");

            migrationBuilder.CreateIndex("IX_UnitOfMeasures_ConversionUomId", "UnitOfMeasures", "ConversionUomId");
            migrationBuilder.CreateIndex("IX_Items_AlternateUomId", "Items", "AlternateUomId");
            migrationBuilder.CreateIndex("IX_Items_UomId", "Items", "UomId");
            migrationBuilder.AddForeignKey("FK_Items_UnitOfMeasures_AlternateUomId", "Items", "AlternateUomId", "UnitOfMeasures", "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey("FK_Items_UnitOfMeasures_UomId", "Items", "UomId", "UnitOfMeasures", "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey("FK_UnitOfMeasures_UnitOfMeasures_ConversionUomId", "UnitOfMeasures", "ConversionUomId", "UnitOfMeasures", "Id", onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey("FK_Items_UnitOfMeasures_AlternateUomId", "Items");
            migrationBuilder.DropForeignKey("FK_Items_UnitOfMeasures_UomId", "Items");
            migrationBuilder.DropForeignKey("FK_UnitOfMeasures_UnitOfMeasures_ConversionUomId", "UnitOfMeasures");
            migrationBuilder.DropIndex("IX_UnitOfMeasures_ConversionUomId", "UnitOfMeasures");
            migrationBuilder.DropIndex("IX_Items_AlternateUomId", "Items");
            migrationBuilder.DropIndex("IX_Items_UomId", "Items");

            DropLineUomColumns(migrationBuilder, "CreditNoteLines");
            DropLineUomColumns(migrationBuilder, "DebitNoteLines");
            DropLineUomColumns(migrationBuilder, "GoodsReceiptLines");
            migrationBuilder.DropColumn("UomId", "Items");
            migrationBuilder.DropColumn("AlternateUomId", "Items");
            migrationBuilder.DropColumn("AlternateUomConversionFactor", "Items");
            DropLineUomColumns(migrationBuilder, "PurchaseOrderLines");
            DropLineUomColumns(migrationBuilder, "ReceiptRefundLines");
            DropLineUomColumns(migrationBuilder, "SalesOrderLines");
            DropLineUomColumns(migrationBuilder, "SalesShipmentLines");
            migrationBuilder.DropColumn("ConversionUomId", "UnitOfMeasures");
            migrationBuilder.DropColumn("ConversionFactorValue", "UnitOfMeasures");
            DropLineUomColumns(migrationBuilder, "VendorBillLines");
            DropLineUomColumns(migrationBuilder, "VendorReturnLines");
        }

        private static void AddLineUomColumns(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.AddColumn<decimal>("UomConversionFactor", table, type: "decimal(18,4)", nullable: false, defaultValue: 1m);
            migrationBuilder.AddColumn<Guid>("UomId", table, type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>("UomName", table, type: "nvarchar(max)", nullable: false, defaultValue: "");
        }

        private static void DropLineUomColumns(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.DropColumn("UomConversionFactor", table);
            migrationBuilder.DropColumn("UomId", table);
            migrationBuilder.DropColumn("UomName", table);
        }
    }
}
