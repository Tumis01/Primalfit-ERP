using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class LineItemDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "VendorReturns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "VendorReturnLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "VendorPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "StockTransferLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "StockLedgers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "SalesShipmentLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "SalesQuotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "SalesQuoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "SalesOrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "ReceiptRefunds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "ReceiptRefundLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "PurchaseRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "PurchaseRequestLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "PurchaseOrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "PurchaseInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "PurchaseInvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "PaymentApplications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "GoodsReceipts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "GoodsReceiptLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "GoodsReceiptDrafts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "GoodsReceiptDraftLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "GLTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "GLJournalLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "DebitNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "DebitNoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "CreditNoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "CashbookEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DimensionSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DimensionSets", x => x.Id);
                    table.UniqueConstraint("AK_DimensionSets_Id_CompanyId", x => new { x.Id, x.CompanyId });
                    table.ForeignKey(
                        name: "FK_DimensionSets_CompanyDetails_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "CompanyDetails",
                        principalColumn: "CompanyDetailsId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DimensionTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DimensionTypes", x => x.Id);
                    table.UniqueConstraint("AK_DimensionTypes_Id_CompanyId", x => new { x.Id, x.CompanyId });
                    table.ForeignKey(
                        name: "FK_DimensionTypes_CompanyDetails_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "CompanyDetails",
                        principalColumn: "CompanyDetailsId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DimensionValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DimensionTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LegacyProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DimensionValues", x => x.Id);
                    table.UniqueConstraint("AK_DimensionValues_Id_DimensionTypeId_CompanyId", x => new { x.Id, x.DimensionTypeId, x.CompanyId });
                    table.ForeignKey(
                        name: "FK_DimensionValues_CompanyDetails_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "CompanyDetails",
                        principalColumn: "CompanyDetailsId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DimensionValues_DimensionTypes_DimensionTypeId_CompanyId",
                        columns: x => new { x.DimensionTypeId, x.CompanyId },
                        principalTable: "DimensionTypes",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DimensionSetValues",
                columns: table => new
                {
                    DimensionSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DimensionTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DimensionValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DimensionSetValues", x => new { x.DimensionSetId, x.DimensionTypeId });
                    table.ForeignKey(
                        name: "FK_DimensionSetValues_DimensionSets_DimensionSetId_CompanyId",
                        columns: x => new { x.DimensionSetId, x.CompanyId },
                        principalTable: "DimensionSets",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DimensionSetValues_DimensionValues_DimensionValueId_DimensionTypeId_CompanyId",
                        columns: x => new { x.DimensionValueId, x.DimensionTypeId, x.CompanyId },
                        principalTable: "DimensionValues",
                        principalColumns: new[] { "Id", "DimensionTypeId", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendorReturns_DimensionSetId",
                table: "VendorReturns",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorReturnLines_DimensionSetId",
                table: "VendorReturnLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorPayments_DimensionSetId",
                table: "VendorPayments",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_DimensionSetId",
                table: "StockTransferLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgers_DimensionSetId",
                table: "StockLedgers",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesShipmentLines_DimensionSetId",
                table: "SalesShipmentLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_DimensionSetId",
                table: "SalesQuotes",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_DimensionSetId",
                table: "SalesQuoteLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_DimensionSetId",
                table: "SalesOrders",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_DimensionSetId",
                table: "SalesOrderLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_DimensionSetId",
                table: "SalesInvoices",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_DimensionSetId",
                table: "SalesInvoiceLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRefunds_DimensionSetId",
                table: "ReceiptRefunds",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRefundLines_DimensionSetId",
                table: "ReceiptRefundLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_DimensionSetId",
                table: "PurchaseRequests",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestLines_DimensionSetId",
                table: "PurchaseRequestLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_DimensionSetId",
                table: "PurchaseOrders",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_DimensionSetId",
                table: "PurchaseOrderLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_DimensionSetId",
                table: "PurchaseInvoices",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_DimensionSetId",
                table: "PurchaseInvoiceLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentApplications_DimensionSetId",
                table: "PaymentApplications",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_DimensionSetId",
                table: "GoodsReceipts",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLines_DimensionSetId",
                table: "GoodsReceiptLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptDrafts_DimensionSetId",
                table: "GoodsReceiptDrafts",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptDraftLines_DimensionSetId",
                table: "GoodsReceiptDraftLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_GLTransactions_DimensionSetId",
                table: "GLTransactions",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_GLJournalLines_DimensionSetId",
                table: "GLJournalLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_DimensionSetId",
                table: "DebitNotes",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNoteLines_DimensionSetId",
                table: "DebitNoteLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_DimensionSetId",
                table: "CreditNotes",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_DimensionSetId",
                table: "CreditNoteLines",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_CashbookEntries_DimensionSetId",
                table: "CashbookEntries",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_DimensionSets_CompanyId",
                table: "DimensionSets",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DimensionSetValues_DimensionSetId_CompanyId",
                table: "DimensionSetValues",
                columns: new[] { "DimensionSetId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_DimensionSetValues_DimensionValueId_DimensionTypeId_CompanyId",
                table: "DimensionSetValues",
                columns: new[] { "DimensionValueId", "DimensionTypeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_DimensionTypes_CompanyId_Code",
                table: "DimensionTypes",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DimensionValues_CompanyId",
                table: "DimensionValues",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DimensionValues_DimensionTypeId_Code",
                table: "DimensionValues",
                columns: new[] { "DimensionTypeId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DimensionValues_DimensionTypeId_CompanyId",
                table: "DimensionValues",
                columns: new[] { "DimensionTypeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_DimensionValues_LegacyProjectId",
                table: "DimensionValues",
                column: "LegacyProjectId",
                unique: true,
                filter: "[LegacyProjectId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CashbookEntries_DimensionSets_DimensionSetId",
                table: "CashbookEntries",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNoteLines_DimensionSets_DimensionSetId",
                table: "CreditNoteLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_DimensionSets_DimensionSetId",
                table: "CreditNotes",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DebitNoteLines_DimensionSets_DimensionSetId",
                table: "DebitNoteLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DebitNotes_DimensionSets_DimensionSetId",
                table: "DebitNotes",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GLJournalLines_DimensionSets_DimensionSetId",
                table: "GLJournalLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GLTransactions_DimensionSets_DimensionSetId",
                table: "GLTransactions",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptDraftLines_DimensionSets_DimensionSetId",
                table: "GoodsReceiptDraftLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptDrafts_DimensionSets_DimensionSetId",
                table: "GoodsReceiptDrafts",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptLines_DimensionSets_DimensionSetId",
                table: "GoodsReceiptLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceipts_DimensionSets_DimensionSetId",
                table: "GoodsReceipts",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentApplications_DimensionSets_DimensionSetId",
                table: "PaymentApplications",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceLines_DimensionSets_DimensionSetId",
                table: "PurchaseInvoiceLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoices_DimensionSets_DimensionSetId",
                table: "PurchaseInvoices",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_DimensionSets_DimensionSetId",
                table: "PurchaseOrderLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_DimensionSets_DimensionSetId",
                table: "PurchaseOrders",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequestLines_DimensionSets_DimensionSetId",
                table: "PurchaseRequestLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequests_DimensionSets_DimensionSetId",
                table: "PurchaseRequests",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiptRefundLines_DimensionSets_DimensionSetId",
                table: "ReceiptRefundLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiptRefunds_DimensionSets_DimensionSetId",
                table: "ReceiptRefunds",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_DimensionSets_DimensionSetId",
                table: "SalesInvoiceLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_DimensionSets_DimensionSetId",
                table: "SalesInvoices",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_DimensionSets_DimensionSetId",
                table: "SalesOrderLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_DimensionSets_DimensionSetId",
                table: "SalesOrders",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesQuoteLines_DimensionSets_DimensionSetId",
                table: "SalesQuoteLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesQuotes_DimensionSets_DimensionSetId",
                table: "SalesQuotes",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesShipmentLines_DimensionSets_DimensionSetId",
                table: "SalesShipmentLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockLedgers_DimensionSets_DimensionSetId",
                table: "StockLedgers",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransferLines_DimensionSets_DimensionSetId",
                table: "StockTransferLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorPayments_DimensionSets_DimensionSetId",
                table: "VendorPayments",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorReturnLines_DimensionSets_DimensionSetId",
                table: "VendorReturnLines",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorReturns_DimensionSets_DimensionSetId",
                table: "VendorReturns",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""

-- Preserve the project catalog, including legacy values referenced by old transactions.
INSERT INTO [DimensionTypes] ([Id], [CompanyId], [Code], [Name], [IsRequired], [IsActive], [DisplayOrder])
SELECT NEWID(), [CompanyDetailsId], N'PRJ', N'Project', 0, 1, 0 FROM [CompanyDetails];
WITH ProjectCodes AS (
 SELECT p.*, ROW_NUMBER() OVER (PARTITION BY p.[CompanyId], p.[ProjectCode] ORDER BY p.[IsAccountingProject] DESC, p.[Id]) AS rn
 FROM [Projects] p
)
INSERT INTO [DimensionValues] ([Id], [CompanyId], [DimensionTypeId], [Code], [Name], [Description], [IsActive], [LegacyProjectId])
SELECT p.[Id], p.[CompanyId], t.[Id],
 CASE WHEN p.rn = 1 AND LEN(LTRIM(RTRIM(p.[ProjectCode]))) > 0 THEN p.[ProjectCode]
 ELSE LEFT(COALESCE(NULLIF(p.[ProjectCode], N''), N'PROJECT'), 17) + N'-' + REPLACE(CONVERT(nvarchar(36), p.[Id]), N'-', N'') END,
 p.[Name], LEFT(p.[Description], 2000), p.[IsActive], p.[Id]
FROM ProjectCodes p JOIN [DimensionTypes] t ON t.[CompanyId] = p.[CompanyId] AND t.[Code] = N'PRJ';
INSERT INTO [DimensionSets] ([Id], [CompanyId], [CreatedAtUtc])
SELECT [Id], [CompanyId], SYSUTCDATETIME() FROM [DimensionValues];
INSERT INTO [DimensionSetValues] ([DimensionSetId], [CompanyId], [DimensionTypeId], [DimensionValueId])
SELECT [Id], [CompanyId], [DimensionTypeId], [Id] FROM [DimensionValues];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [VendorReturns] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [VendorPayments] t JOIN [PurchaseInvoices] h ON h.[Id] = t.[PurchaseInvoiceId] JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = h.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [StockLedgers] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [SalesQuotes] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [SalesOrders] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [SalesInvoices] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [ReceiptRefunds] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [PurchaseRequests] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [PurchaseOrders] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [PurchaseInvoices] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [GoodsReceipts] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [GoodsReceiptDrafts] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [GLTransactions] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [DebitNotes] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [CreditNotes] t JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = t.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [StockTransferLines] t JOIN [StockTransfers] h ON h.[Id] = t.[StockTransferId] JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = h.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [PaymentApplications] t JOIN [CustomerPayments] h ON h.[Id] = t.[CustomerPaymentId] JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = h.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [GLJournalLines] t JOIN [GLJournalHeaders] h ON h.[Id] = t.[HeaderId] JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = h.[CompanyId];
UPDATE t SET [DimensionSetId] = s.[Id] FROM [CashbookEntries] t JOIN [CashbookBatches] h ON h.[Id] = t.[CashbookBatchId] JOIN [DimensionSets] s ON s.[Id] = t.[ProjectId] AND s.[CompanyId] = h.[CompanyId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [PurchaseRequestLines] t JOIN [PurchaseRequests] h ON h.[Id] = t.[PurchaseRequestId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [SalesQuoteLines] t JOIN [SalesQuotes] h ON h.[Id] = t.[SalesQuoteId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [PurchaseOrderLines] t JOIN [PurchaseOrders] h ON h.[Id] = t.[PurchaseOrderId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [SalesOrderLines] t JOIN [SalesOrders] h ON h.[Id] = t.[HeaderId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [PurchaseInvoiceLines] t JOIN [PurchaseInvoices] h ON h.[Id] = t.[PurchaseInvoiceId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [SalesInvoiceLines] t JOIN [SalesInvoices] h ON h.[Id] = t.[SalesInvoiceId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [GoodsReceiptLines] t JOIN [GoodsReceipts] h ON h.[Id] = t.[GoodsReceiptId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [GoodsReceiptDraftLines] t JOIN [GoodsReceiptDrafts] h ON h.[Id] = t.[GoodsReceiptDraftId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [CreditNoteLines] t JOIN [CreditNotes] h ON h.[Id] = t.[HeaderId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [DebitNoteLines] t JOIN [DebitNotes] h ON h.[Id] = t.[HeaderId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [VendorReturnLines] t JOIN [VendorReturns] h ON h.[Id] = t.[HeaderId];
UPDATE t SET [DimensionSetId] = h.[DimensionSetId] FROM [ReceiptRefundLines] t JOIN [ReceiptRefunds] h ON h.[Id] = t.[HeaderId];

UPDATE l SET [DimensionSetId] = COALESCE(i.[DimensionSetId], o.[DimensionSetId]) FROM [SalesShipmentLines] l
LEFT JOIN [SalesInvoiceLines] i ON i.[Id] = l.[SalesInvoiceLineId]
LEFT JOIN [SalesOrderLines] o ON o.[Id] = l.[SalesOrderLineId];
UPDATE l SET [DimensionSetId] = COALESCE(i.[DimensionSetId], o.[DimensionSetId]) FROM [GoodsReceiptLines] l
LEFT JOIN [PurchaseInvoiceLines] i ON i.[Id] = l.[PurchaseInvoiceLineId]
LEFT JOIN [PurchaseOrderLines] o ON o.[Id] = l.[PurchaseOrderLineId]
WHERE l.[DimensionSetId] IS NULL;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashbookEntries_DimensionSets_DimensionSetId",
                table: "CashbookEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNoteLines_DimensionSets_DimensionSetId",
                table: "CreditNoteLines");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_DimensionSets_DimensionSetId",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_DebitNoteLines_DimensionSets_DimensionSetId",
                table: "DebitNoteLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DebitNotes_DimensionSets_DimensionSetId",
                table: "DebitNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_GLJournalLines_DimensionSets_DimensionSetId",
                table: "GLJournalLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GLTransactions_DimensionSets_DimensionSetId",
                table: "GLTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptDraftLines_DimensionSets_DimensionSetId",
                table: "GoodsReceiptDraftLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptDrafts_DimensionSets_DimensionSetId",
                table: "GoodsReceiptDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptLines_DimensionSets_DimensionSetId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceipts_DimensionSets_DimensionSetId",
                table: "GoodsReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentApplications_DimensionSets_DimensionSetId",
                table: "PaymentApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceLines_DimensionSets_DimensionSetId",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoices_DimensionSets_DimensionSetId",
                table: "PurchaseInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_DimensionSets_DimensionSetId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_DimensionSets_DimensionSetId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseRequestLines_DimensionSets_DimensionSetId",
                table: "PurchaseRequestLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseRequests_DimensionSets_DimensionSetId",
                table: "PurchaseRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceiptRefundLines_DimensionSets_DimensionSetId",
                table: "ReceiptRefundLines");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceiptRefunds_DimensionSets_DimensionSetId",
                table: "ReceiptRefunds");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_DimensionSets_DimensionSetId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_DimensionSets_DimensionSetId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_DimensionSets_DimensionSetId",
                table: "SalesOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_DimensionSets_DimensionSetId",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesQuoteLines_DimensionSets_DimensionSetId",
                table: "SalesQuoteLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesQuotes_DimensionSets_DimensionSetId",
                table: "SalesQuotes");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesShipmentLines_DimensionSets_DimensionSetId",
                table: "SalesShipmentLines");

            migrationBuilder.DropForeignKey(
                name: "FK_StockLedgers_DimensionSets_DimensionSetId",
                table: "StockLedgers");

            migrationBuilder.DropForeignKey(
                name: "FK_StockTransferLines_DimensionSets_DimensionSetId",
                table: "StockTransferLines");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorPayments_DimensionSets_DimensionSetId",
                table: "VendorPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorReturnLines_DimensionSets_DimensionSetId",
                table: "VendorReturnLines");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorReturns_DimensionSets_DimensionSetId",
                table: "VendorReturns");

            migrationBuilder.DropTable(
                name: "DimensionSetValues");

            migrationBuilder.DropTable(
                name: "DimensionSets");

            migrationBuilder.DropTable(
                name: "DimensionValues");

            migrationBuilder.DropTable(
                name: "DimensionTypes");

            migrationBuilder.DropIndex(
                name: "IX_VendorReturns_DimensionSetId",
                table: "VendorReturns");

            migrationBuilder.DropIndex(
                name: "IX_VendorReturnLines_DimensionSetId",
                table: "VendorReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_VendorPayments_DimensionSetId",
                table: "VendorPayments");

            migrationBuilder.DropIndex(
                name: "IX_StockTransferLines_DimensionSetId",
                table: "StockTransferLines");

            migrationBuilder.DropIndex(
                name: "IX_StockLedgers_DimensionSetId",
                table: "StockLedgers");

            migrationBuilder.DropIndex(
                name: "IX_SalesShipmentLines_DimensionSetId",
                table: "SalesShipmentLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesQuotes_DimensionSetId",
                table: "SalesQuotes");

            migrationBuilder.DropIndex(
                name: "IX_SalesQuoteLines_DimensionSetId",
                table: "SalesQuoteLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_DimensionSetId",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLines_DimensionSetId",
                table: "SalesOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_DimensionSetId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_DimensionSetId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptRefunds_DimensionSetId",
                table: "ReceiptRefunds");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptRefundLines_DimensionSetId",
                table: "ReceiptRefundLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseRequests_DimensionSetId",
                table: "PurchaseRequests");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseRequestLines_DimensionSetId",
                table: "PurchaseRequestLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_DimensionSetId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLines_DimensionSetId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_DimensionSetId",
                table: "PurchaseInvoices");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceLines_DimensionSetId",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_PaymentApplications_DimensionSetId",
                table: "PaymentApplications");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceipts_DimensionSetId",
                table: "GoodsReceipts");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptLines_DimensionSetId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptDrafts_DimensionSetId",
                table: "GoodsReceiptDrafts");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptDraftLines_DimensionSetId",
                table: "GoodsReceiptDraftLines");

            migrationBuilder.DropIndex(
                name: "IX_GLTransactions_DimensionSetId",
                table: "GLTransactions");

            migrationBuilder.DropIndex(
                name: "IX_GLJournalLines_DimensionSetId",
                table: "GLJournalLines");

            migrationBuilder.DropIndex(
                name: "IX_DebitNotes_DimensionSetId",
                table: "DebitNotes");

            migrationBuilder.DropIndex(
                name: "IX_DebitNoteLines_DimensionSetId",
                table: "DebitNoteLines");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_DimensionSetId",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNoteLines_DimensionSetId",
                table: "CreditNoteLines");

            migrationBuilder.DropIndex(
                name: "IX_CashbookEntries_DimensionSetId",
                table: "CashbookEntries");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "VendorReturns");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "VendorReturnLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "VendorPayments");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "StockTransferLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "StockLedgers");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "SalesShipmentLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "SalesQuotes");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "SalesQuoteLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "ReceiptRefunds");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "ReceiptRefundLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "PurchaseRequestLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "PaymentApplications");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "GoodsReceipts");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "GoodsReceiptDrafts");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "GoodsReceiptDraftLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "GLTransactions");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "GLJournalLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "DebitNoteLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "CashbookEntries");
        }
    }
}
