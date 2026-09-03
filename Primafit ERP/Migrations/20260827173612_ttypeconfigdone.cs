using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class ttypeconfigdone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "VendorReturns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideAccountsPayableGlAccountId",
                table: "VendorReturns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideGrIrClearingGlAccountId",
                table: "VendorReturns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideInventoryAssetGlAccountId",
                table: "VendorReturns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "VendorPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideCreditBankGlAccountId",
                table: "VendorPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideDebitApGlAccountId",
                table: "VendorPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "VendorBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideAccountsPayableGlAccountId",
                table: "VendorBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideExpenseGlAccountId",
                table: "VendorBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AccountsPayableGlAccountId",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GoodsReceiptClearingGlAccountId",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "GoodsReceipts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideGrIrClearingGlAccountId",
                table: "GoodsReceipts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideInventoryAssetGlAccountId",
                table: "GoodsReceipts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "DebitNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideAccountsPayableGlAccountId",
                table: "DebitNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideGrIrClearingGlAccountId",
                table: "DebitNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorReturns_CustomTransactionTypeId",
                table: "VendorReturns",
                column: "CustomTransactionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorPayments_CustomTransactionTypeId",
                table: "VendorPayments",
                column: "CustomTransactionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_CurrencyId",
                table: "VendorBills",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_CustomTransactionTypeId",
                table: "VendorBills",
                column: "CustomTransactionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_PurchaseOrderId",
                table: "VendorBills",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_VendorId",
                table: "VendorBills",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_ItemId",
                table: "VendorBillLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CustomTransactionTypeId",
                table: "PurchaseOrders",
                column: "CustomTransactionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_CustomTransactionTypeId",
                table: "GoodsReceipts",
                column: "CustomTransactionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_CustomTransactionTypeId",
                table: "DebitNotes",
                column: "CustomTransactionTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_DebitNotes_CustomTransactionTypes_CustomTransactionTypeId",
                table: "DebitNotes",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceipts_CustomTransactionTypes_CustomTransactionTypeId",
                table: "GoodsReceipts",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_CustomTransactionTypes_CustomTransactionTypeId",
                table: "PurchaseOrders",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBillLines_Items_ItemId",
                table: "VendorBillLines",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_Currencies_CurrencyId",
                table: "VendorBills",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_CustomTransactionTypes_CustomTransactionTypeId",
                table: "VendorBills",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_PurchaseOrders_PurchaseOrderId",
                table: "VendorBills",
                column: "PurchaseOrderId",
                principalTable: "PurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_Vendors_VendorId",
                table: "VendorBills",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorPayments_CustomTransactionTypes_CustomTransactionTypeId",
                table: "VendorPayments",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorReturns_CustomTransactionTypes_CustomTransactionTypeId",
                table: "VendorReturns",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DebitNotes_CustomTransactionTypes_CustomTransactionTypeId",
                table: "DebitNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceipts_CustomTransactionTypes_CustomTransactionTypeId",
                table: "GoodsReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_CustomTransactionTypes_CustomTransactionTypeId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBillLines_Items_ItemId",
                table: "VendorBillLines");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBills_Currencies_CurrencyId",
                table: "VendorBills");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBills_CustomTransactionTypes_CustomTransactionTypeId",
                table: "VendorBills");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBills_PurchaseOrders_PurchaseOrderId",
                table: "VendorBills");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBills_Vendors_VendorId",
                table: "VendorBills");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorPayments_CustomTransactionTypes_CustomTransactionTypeId",
                table: "VendorPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorReturns_CustomTransactionTypes_CustomTransactionTypeId",
                table: "VendorReturns");

            migrationBuilder.DropIndex(
                name: "IX_VendorReturns_CustomTransactionTypeId",
                table: "VendorReturns");

            migrationBuilder.DropIndex(
                name: "IX_VendorPayments_CustomTransactionTypeId",
                table: "VendorPayments");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_CurrencyId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_CustomTransactionTypeId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_PurchaseOrderId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_VendorId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_VendorBillLines_ItemId",
                table: "VendorBillLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_CustomTransactionTypeId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceipts_CustomTransactionTypeId",
                table: "GoodsReceipts");

            migrationBuilder.DropIndex(
                name: "IX_DebitNotes_CustomTransactionTypeId",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "VendorReturns");

            migrationBuilder.DropColumn(
                name: "OverrideAccountsPayableGlAccountId",
                table: "VendorReturns");

            migrationBuilder.DropColumn(
                name: "OverrideGrIrClearingGlAccountId",
                table: "VendorReturns");

            migrationBuilder.DropColumn(
                name: "OverrideInventoryAssetGlAccountId",
                table: "VendorReturns");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "VendorPayments");

            migrationBuilder.DropColumn(
                name: "OverrideCreditBankGlAccountId",
                table: "VendorPayments");

            migrationBuilder.DropColumn(
                name: "OverrideDebitApGlAccountId",
                table: "VendorPayments");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "OverrideAccountsPayableGlAccountId",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "OverrideExpenseGlAccountId",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "AccountsPayableGlAccountId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "GoodsReceiptClearingGlAccountId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "GoodsReceipts");

            migrationBuilder.DropColumn(
                name: "OverrideGrIrClearingGlAccountId",
                table: "GoodsReceipts");

            migrationBuilder.DropColumn(
                name: "OverrideInventoryAssetGlAccountId",
                table: "GoodsReceipts");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "OverrideAccountsPayableGlAccountId",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "OverrideGrIrClearingGlAccountId",
                table: "DebitNotes");
        }
    }
}
