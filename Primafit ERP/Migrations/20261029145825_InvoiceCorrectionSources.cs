using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceCorrectionSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseInvoiceId",
                table: "VendorReturns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseOrderLineId",
                table: "VendorReturnLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseInvoiceLineId",
                table: "VendorReturnLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderId",
                table: "ReceiptRefunds",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceId",
                table: "ReceiptRefunds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderLineId",
                table: "ReceiptRefundLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceLineId",
                table: "ReceiptRefundLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseInvoiceId",
                table: "DebitNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseOrderLineId",
                table: "DebitNoteLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "ItemId",
                table: "DebitNoteLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseInvoiceLineId",
                table: "DebitNoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnfulfilledQuantity",
                table: "DebitNoteLines",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderLineId",
                table: "CreditNoteLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "ItemId",
                table: "CreditNoteLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceLineId",
                table: "CreditNoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnfulfilledQuantity",
                table: "CreditNoteLines",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_VendorReturns_PurchaseInvoiceId",
                table: "VendorReturns",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorReturnLines_PurchaseInvoiceLineId",
                table: "VendorReturnLines",
                column: "PurchaseInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRefunds_SalesInvoiceId",
                table: "ReceiptRefunds",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRefundLines_SalesInvoiceLineId",
                table: "ReceiptRefundLines",
                column: "SalesInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_PurchaseInvoiceId",
                table: "DebitNotes",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNoteLines_PurchaseInvoiceLineId",
                table: "DebitNoteLines",
                column: "PurchaseInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_SalesInvoiceId",
                table: "CreditNotes",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_SalesInvoiceLineId",
                table: "CreditNoteLines",
                column: "SalesInvoiceLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNoteLines_SalesInvoiceLines_SalesInvoiceLineId",
                table: "CreditNoteLines",
                column: "SalesInvoiceLineId",
                principalTable: "SalesInvoiceLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_SalesInvoices_SalesInvoiceId",
                table: "CreditNotes",
                column: "SalesInvoiceId",
                principalTable: "SalesInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DebitNoteLines_PurchaseInvoiceLines_PurchaseInvoiceLineId",
                table: "DebitNoteLines",
                column: "PurchaseInvoiceLineId",
                principalTable: "PurchaseInvoiceLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DebitNotes_PurchaseInvoices_PurchaseInvoiceId",
                table: "DebitNotes",
                column: "PurchaseInvoiceId",
                principalTable: "PurchaseInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiptRefundLines_SalesInvoiceLines_SalesInvoiceLineId",
                table: "ReceiptRefundLines",
                column: "SalesInvoiceLineId",
                principalTable: "SalesInvoiceLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiptRefunds_SalesInvoices_SalesInvoiceId",
                table: "ReceiptRefunds",
                column: "SalesInvoiceId",
                principalTable: "SalesInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorReturnLines_PurchaseInvoiceLines_PurchaseInvoiceLineId",
                table: "VendorReturnLines",
                column: "PurchaseInvoiceLineId",
                principalTable: "PurchaseInvoiceLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorReturns_PurchaseInvoices_PurchaseInvoiceId",
                table: "VendorReturns",
                column: "PurchaseInvoiceId",
                principalTable: "PurchaseInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [CreditNotes] WHERE [SalesInvoiceId] IS NOT NULL) OR EXISTS (SELECT 1 FROM [DebitNotes] WHERE [PurchaseInvoiceId] IS NOT NULL) OR EXISTS (SELECT 1 FROM [ReceiptRefunds] WHERE [SalesInvoiceId] IS NOT NULL) OR EXISTS (SELECT 1 FROM [VendorReturns] WHERE [PurchaseInvoiceId] IS NOT NULL) THROW 51000, 'Cannot downgrade while invoice-sourced corrections exist.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNoteLines_SalesInvoiceLines_SalesInvoiceLineId",
                table: "CreditNoteLines");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_SalesInvoices_SalesInvoiceId",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_DebitNoteLines_PurchaseInvoiceLines_PurchaseInvoiceLineId",
                table: "DebitNoteLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DebitNotes_PurchaseInvoices_PurchaseInvoiceId",
                table: "DebitNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceiptRefundLines_SalesInvoiceLines_SalesInvoiceLineId",
                table: "ReceiptRefundLines");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceiptRefunds_SalesInvoices_SalesInvoiceId",
                table: "ReceiptRefunds");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorReturnLines_PurchaseInvoiceLines_PurchaseInvoiceLineId",
                table: "VendorReturnLines");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorReturns_PurchaseInvoices_PurchaseInvoiceId",
                table: "VendorReturns");

            migrationBuilder.DropIndex(
                name: "IX_VendorReturns_PurchaseInvoiceId",
                table: "VendorReturns");

            migrationBuilder.DropIndex(
                name: "IX_VendorReturnLines_PurchaseInvoiceLineId",
                table: "VendorReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptRefunds_SalesInvoiceId",
                table: "ReceiptRefunds");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptRefundLines_SalesInvoiceLineId",
                table: "ReceiptRefundLines");

            migrationBuilder.DropIndex(
                name: "IX_DebitNotes_PurchaseInvoiceId",
                table: "DebitNotes");

            migrationBuilder.DropIndex(
                name: "IX_DebitNoteLines_PurchaseInvoiceLineId",
                table: "DebitNoteLines");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_SalesInvoiceId",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNoteLines_SalesInvoiceLineId",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceId",
                table: "VendorReturns");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceLineId",
                table: "VendorReturnLines");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceId",
                table: "ReceiptRefunds");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceLineId",
                table: "ReceiptRefundLines");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceId",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceLineId",
                table: "DebitNoteLines");

            migrationBuilder.DropColumn(
                name: "UnfulfilledQuantity",
                table: "DebitNoteLines");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceLineId",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "UnfulfilledQuantity",
                table: "CreditNoteLines");

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseOrderLineId",
                table: "VendorReturnLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderId",
                table: "ReceiptRefunds",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderLineId",
                table: "ReceiptRefundLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseOrderLineId",
                table: "DebitNoteLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ItemId",
                table: "DebitNoteLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderLineId",
                table: "CreditNoteLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ItemId",
                table: "CreditNoteLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
