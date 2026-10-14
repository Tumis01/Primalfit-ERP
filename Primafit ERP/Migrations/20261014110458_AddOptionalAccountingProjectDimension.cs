using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddOptionalAccountingProjectDimension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "VendorReturns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "VendorPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "VendorBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasLineDetails",
                table: "StockTransfers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "StockLedgers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "ReceiptRefunds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAccountingProject",
                table: "Projects",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Projects",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "PaymentApplications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "GoodsReceipts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "GLJournalLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "DebitNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockTransferLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UomName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UomConversionFactor = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    QuantityInUom = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    ValueAtShipment = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockTransferLines_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockTransferLines_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendorReturns_ProjectId",
                table: "VendorReturns",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorPayments_ProjectId",
                table: "VendorPayments",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_ProjectId",
                table: "VendorBills",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgers_ProjectId",
                table: "StockLedgers",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_ProjectId",
                table: "SalesOrders",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRefunds_ProjectId",
                table: "ReceiptRefunds",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_ProjectId",
                table: "PurchaseOrders",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CompanyId_ProjectCode",
                table: "Projects",
                columns: new[] { "CompanyId", "ProjectCode" },
                unique: true,
                filter: "[IsAccountingProject] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentApplications_ProjectId",
                table: "PaymentApplications",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_ProjectId",
                table: "GoodsReceipts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_GLJournalLines_ProjectId",
                table: "GLJournalLines",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_ProjectId",
                table: "DebitNotes",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_ProjectId",
                table: "CreditNotes",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CashbookEntries_ProjectId",
                table: "CashbookEntries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_ItemId",
                table: "StockTransferLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_ProjectId",
                table: "StockTransferLines",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_StockTransferId",
                table: "StockTransferLines",
                column: "StockTransferId");

            migrationBuilder.AddForeignKey(
                name: "FK_CashbookEntries_Projects_ProjectId",
                table: "CashbookEntries",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_Projects_ProjectId",
                table: "CreditNotes",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DebitNotes_Projects_ProjectId",
                table: "DebitNotes",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GLJournalLines_Projects_ProjectId",
                table: "GLJournalLines",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceipts_Projects_ProjectId",
                table: "GoodsReceipts",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentApplications_Projects_ProjectId",
                table: "PaymentApplications",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Projects_ProjectId",
                table: "PurchaseOrders",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiptRefunds_Projects_ProjectId",
                table: "ReceiptRefunds",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_Projects_ProjectId",
                table: "SalesOrders",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StockLedgers_Projects_ProjectId",
                table: "StockLedgers",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_Projects_ProjectId",
                table: "VendorBills",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorPayments_Projects_ProjectId",
                table: "VendorPayments",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorReturns_Projects_ProjectId",
                table: "VendorReturns",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashbookEntries_Projects_ProjectId",
                table: "CashbookEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_Projects_ProjectId",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_DebitNotes_Projects_ProjectId",
                table: "DebitNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_GLJournalLines_Projects_ProjectId",
                table: "GLJournalLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceipts_Projects_ProjectId",
                table: "GoodsReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentApplications_Projects_ProjectId",
                table: "PaymentApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Projects_ProjectId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceiptRefunds_Projects_ProjectId",
                table: "ReceiptRefunds");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Projects_ProjectId",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_StockLedgers_Projects_ProjectId",
                table: "StockLedgers");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBills_Projects_ProjectId",
                table: "VendorBills");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorPayments_Projects_ProjectId",
                table: "VendorPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorReturns_Projects_ProjectId",
                table: "VendorReturns");

            migrationBuilder.DropTable(
                name: "StockTransferLines");

            migrationBuilder.DropIndex(
                name: "IX_VendorReturns_ProjectId",
                table: "VendorReturns");

            migrationBuilder.DropIndex(
                name: "IX_VendorPayments_ProjectId",
                table: "VendorPayments");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_ProjectId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_StockLedgers_ProjectId",
                table: "StockLedgers");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_ProjectId",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptRefunds_ProjectId",
                table: "ReceiptRefunds");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_ProjectId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_Projects_CompanyId_ProjectCode",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_PaymentApplications_ProjectId",
                table: "PaymentApplications");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceipts_ProjectId",
                table: "GoodsReceipts");

            migrationBuilder.DropIndex(
                name: "IX_GLJournalLines_ProjectId",
                table: "GLJournalLines");

            migrationBuilder.DropIndex(
                name: "IX_DebitNotes_ProjectId",
                table: "DebitNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_ProjectId",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CashbookEntries_ProjectId",
                table: "CashbookEntries");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "VendorReturns");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "VendorPayments");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "HasLineDetails",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "StockLedgers");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "ReceiptRefunds");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "IsAccountingProject",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "PaymentApplications");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "GoodsReceipts");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "GLJournalLines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "CreditNotes");
        }
    }
}
