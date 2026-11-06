using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class GoodsReceiptDraftReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoodsReceiptDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrnNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DateReceived = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InventoryGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomTransactionTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverrideInventoryAssetGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverrideGrIrClearingGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptDraftLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoodsReceiptDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UomName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UomConversionFactor = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptDraftLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptDraftLines_GoodsReceiptDrafts_GoodsReceiptDraftId",
                        column: x => x.GoodsReceiptDraftId,
                        principalTable: "GoodsReceiptDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptDraftLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptDraftLines_GoodsReceiptDraftId_PurchaseOrderLineId",
                table: "GoodsReceiptDraftLines",
                columns: new[] { "GoodsReceiptDraftId", "PurchaseOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptDraftLines_PurchaseOrderLineId",
                table: "GoodsReceiptDraftLines",
                column: "PurchaseOrderLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoodsReceiptDraftLines");

            migrationBuilder.DropTable(
                name: "GoodsReceiptDrafts");
        }
    }
}
