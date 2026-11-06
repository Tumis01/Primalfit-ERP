using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseInvoiceReceiptSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptDraftLines_GoodsReceiptDraftId_PurchaseOrderLineId",
                table: "GoodsReceiptDraftLines");

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseInvoiceLineId",
                table: "GoodsReceiptLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseOrderLineId",
                table: "GoodsReceiptDraftLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseInvoiceLineId",
                table: "GoodsReceiptDraftLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLines_GoodsReceiptId_PurchaseInvoiceLineId",
                table: "GoodsReceiptLines",
                columns: new[] { "GoodsReceiptId", "PurchaseInvoiceLineId" },
                unique: true,
                filter: "[PurchaseInvoiceLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLines_PurchaseInvoiceLineId",
                table: "GoodsReceiptLines",
                column: "PurchaseInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptDraftLines_GoodsReceiptDraftId_PurchaseInvoiceLineId",
                table: "GoodsReceiptDraftLines",
                columns: new[] { "GoodsReceiptDraftId", "PurchaseInvoiceLineId" },
                unique: true,
                filter: "[PurchaseInvoiceLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptDraftLines_GoodsReceiptDraftId_PurchaseOrderLineId",
                table: "GoodsReceiptDraftLines",
                columns: new[] { "GoodsReceiptDraftId", "PurchaseOrderLineId" },
                unique: true,
                filter: "[PurchaseOrderLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptDraftLines_PurchaseInvoiceLineId",
                table: "GoodsReceiptDraftLines",
                column: "PurchaseInvoiceLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptDraftLines_PurchaseInvoiceLines_PurchaseInvoiceLineId",
                table: "GoodsReceiptDraftLines",
                column: "PurchaseInvoiceLineId",
                principalTable: "PurchaseInvoiceLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptLines_PurchaseInvoiceLines_PurchaseInvoiceLineId",
                table: "GoodsReceiptLines",
                column: "PurchaseInvoiceLineId",
                principalTable: "PurchaseInvoiceLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [GoodsReceiptLines] WHERE [PurchaseInvoiceLineId] IS NOT NULL) OR EXISTS (SELECT 1 FROM [GoodsReceiptDraftLines] WHERE [PurchaseInvoiceLineId] IS NOT NULL) THROW 51000, 'Cannot downgrade while invoice-sourced receipt lines exist.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptDraftLines_PurchaseInvoiceLines_PurchaseInvoiceLineId",
                table: "GoodsReceiptDraftLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptLines_PurchaseInvoiceLines_PurchaseInvoiceLineId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptLines_GoodsReceiptId_PurchaseInvoiceLineId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptLines_PurchaseInvoiceLineId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptDraftLines_GoodsReceiptDraftId_PurchaseInvoiceLineId",
                table: "GoodsReceiptDraftLines");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptDraftLines_GoodsReceiptDraftId_PurchaseOrderLineId",
                table: "GoodsReceiptDraftLines");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptDraftLines_PurchaseInvoiceLineId",
                table: "GoodsReceiptDraftLines");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceLineId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceLineId",
                table: "GoodsReceiptDraftLines");

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseOrderLineId",
                table: "GoodsReceiptDraftLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptDraftLines_GoodsReceiptDraftId_PurchaseOrderLineId",
                table: "GoodsReceiptDraftLines",
                columns: new[] { "GoodsReceiptDraftId", "PurchaseOrderLineId" },
                unique: true);
        }
    }
}
