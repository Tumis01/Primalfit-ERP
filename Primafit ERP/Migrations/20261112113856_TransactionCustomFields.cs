using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class TransactionCustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VendorReturnLines_HeaderId",
                table: "VendorReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_VendorPayments_PurchaseInvoiceId",
                table: "VendorPayments");

            migrationBuilder.DropIndex(
                name: "IX_StockTransferLines_StockTransferId",
                table: "StockTransferLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesQuoteLines_SalesQuoteId",
                table: "SalesQuoteLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_SalesInvoiceId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptRefundLines_HeaderId",
                table: "ReceiptRefundLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseRequestLines_PurchaseRequestId",
                table: "PurchaseRequestLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceLines_PurchaseInvoiceId",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_GLJournalLines_HeaderId",
                table: "GLJournalLines");

            migrationBuilder.DropIndex(
                name: "IX_GLBatches_CompanyId",
                table: "GLBatches");

            migrationBuilder.DropIndex(
                name: "IX_DebitNoteLines_HeaderId",
                table: "DebitNoteLines");

            migrationBuilder.DropIndex(
                name: "IX_CreditNoteLines_HeaderId",
                table: "CreditNoteLines");

            migrationBuilder.DropIndex(
                name: "IX_CashbookEntries_CashbookBatchId",
                table: "CashbookEntries");

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "VendorReturns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "VendorReturnLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "VendorPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "StockTransfers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "StockTransferLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "SalesShipments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "SalesShipmentLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "SalesQuotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "SalesQuoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [SalesInvoiceLines] WHERE [SalesInvoiceId] IS NULL) THROW 51000, 'Resolve sales invoice lines without an invoice before enabling transaction custom fields. No placeholder IDs will be assigned.', 1;");
            migrationBuilder.AlterColumn<Guid>(
                name: "SalesInvoiceId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "ReceiptRefunds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "ReceiptRefundLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "PurchaseRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "PurchaseRequestLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "PurchaseOrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "PurchaseInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "PurchaseInvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "GoodsReceipts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "GoodsReceiptLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "GoodsReceiptDrafts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "GoodsReceiptDraftLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "GLJournalLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "GLJournalHeaders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasManualCustomFields",
                table: "GLJournalHeaders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CustomFieldContext",
                table: "GLBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "GLBatches",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "DebitNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "DebitNoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "CustomerPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "CreditNoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "CashbookEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "CashbookBatches",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_VendorReturns_CompanyId_Id",
                table: "VendorReturns",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_VendorReturnLines_HeaderId_Id",
                table: "VendorReturnLines",
                columns: new[] { "HeaderId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_VendorPayments_PurchaseInvoiceId_Id",
                table: "VendorPayments",
                columns: new[] { "PurchaseInvoiceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_StockTransfers_CompanyId_Id",
                table: "StockTransfers",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_StockTransferLines_StockTransferId_Id",
                table: "StockTransferLines",
                columns: new[] { "StockTransferId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SalesShipments_CompanyId_Id",
                table: "SalesShipments",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SalesShipmentLines_ShipmentId_Id",
                table: "SalesShipmentLines",
                columns: new[] { "ShipmentId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SalesQuotes_CompanyId_Id",
                table: "SalesQuotes",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SalesQuoteLines_SalesQuoteId_Id",
                table: "SalesQuoteLines",
                columns: new[] { "SalesQuoteId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SalesInvoices_CompanyId_Id",
                table: "SalesInvoices",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SalesInvoiceLines_SalesInvoiceId_Id",
                table: "SalesInvoiceLines",
                columns: new[] { "SalesInvoiceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ReceiptRefunds_CompanyId_Id",
                table: "ReceiptRefunds",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ReceiptRefundLines_HeaderId_Id",
                table: "ReceiptRefundLines",
                columns: new[] { "HeaderId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PurchaseRequests_CompanyId_Id",
                table: "PurchaseRequests",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PurchaseRequestLines_PurchaseRequestId_Id",
                table: "PurchaseRequestLines",
                columns: new[] { "PurchaseRequestId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PurchaseOrders_CompanyId_Id",
                table: "PurchaseOrders",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PurchaseOrderLines_PurchaseOrderId_Id",
                table: "PurchaseOrderLines",
                columns: new[] { "PurchaseOrderId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PurchaseInvoices_CompanyId_Id",
                table: "PurchaseInvoices",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PurchaseInvoiceLines_PurchaseInvoiceId_Id",
                table: "PurchaseInvoiceLines",
                columns: new[] { "PurchaseInvoiceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GoodsReceipts_CompanyId_Id",
                table: "GoodsReceipts",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GoodsReceiptLines_GoodsReceiptId_Id",
                table: "GoodsReceiptLines",
                columns: new[] { "GoodsReceiptId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GoodsReceiptDrafts_CompanyId_Id",
                table: "GoodsReceiptDrafts",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GoodsReceiptDraftLines_GoodsReceiptDraftId_Id",
                table: "GoodsReceiptDraftLines",
                columns: new[] { "GoodsReceiptDraftId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GLJournalLines_HeaderId_Id",
                table: "GLJournalLines",
                columns: new[] { "HeaderId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GLJournalHeaders_CompanyId_Id",
                table: "GLJournalHeaders",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GLBatches_CompanyId_Id",
                table: "GLBatches",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DebitNotes_CompanyId_Id",
                table: "DebitNotes",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DebitNoteLines_HeaderId_Id",
                table: "DebitNoteLines",
                columns: new[] { "HeaderId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CustomerPayments_CompanyId_Id",
                table: "CustomerPayments",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CreditNotes_CompanyId_Id",
                table: "CreditNotes",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CreditNoteLines_HeaderId_Id",
                table: "CreditNoteLines",
                columns: new[] { "HeaderId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CashbookEntries_CashbookBatchId_Id",
                table: "CashbookEntries",
                columns: new[] { "CashbookBatchId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CashbookBatches_CompanyId_Id",
                table: "CashbookBatches",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "InventoryAdjustmentDocument",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GLBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CommittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CustomFieldsVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAdjustmentDocument", x => x.Id);
                    table.UniqueConstraint("AK_InventoryAdjustmentDocument_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_InventoryAdjustmentDocument_GLBatches_CompanyId_GLBatchId",
                        columns: x => new { x.CompanyId, x.GLBatchId },
                        principalTable: "GLBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfCashbookBatchValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfCashbookBatchValues", x => x.Id);
                    table.CheckConstraint("CK_UdfCashbookBatchValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfCashbookBatchValues_CashbookBatches_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "CashbookBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfCashbookBatchValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfCashbookBatchValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfCashbookEntryValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfCashbookEntryValues", x => x.Id);
                    table.CheckConstraint("CK_UdfCashbookEntryValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfCashbookEntryValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfCashbookEntryValues_CashbookBatches_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "CashbookBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfCashbookEntryValues_CashbookEntries_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "CashbookEntries",
                        principalColumns: new[] { "CashbookBatchId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfCashbookEntryValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfCashbookEntryValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfCreditNoteLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfCreditNoteLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfCreditNoteLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfCreditNoteLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfCreditNoteLineValues_CreditNoteLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "CreditNoteLines",
                        principalColumns: new[] { "HeaderId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfCreditNoteLineValues_CreditNotes_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "CreditNotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfCreditNoteLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfCreditNoteLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfCreditNoteValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfCreditNoteValues", x => x.Id);
                    table.CheckConstraint("CK_UdfCreditNoteValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfCreditNoteValues_CreditNotes_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "CreditNotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfCreditNoteValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfCreditNoteValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfCustomerPaymentValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfCustomerPaymentValues", x => x.Id);
                    table.CheckConstraint("CK_UdfCustomerPaymentValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfCustomerPaymentValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfCustomerPaymentValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfCustomerPaymentValues_CustomerPayments_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "CustomerPayments",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfDebitNoteLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfDebitNoteLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfDebitNoteLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfDebitNoteLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfDebitNoteLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfDebitNoteLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfDebitNoteLineValues_DebitNoteLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "DebitNoteLines",
                        principalColumns: new[] { "HeaderId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfDebitNoteLineValues_DebitNotes_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "DebitNotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfDebitNoteValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfDebitNoteValues", x => x.Id);
                    table.CheckConstraint("CK_UdfDebitNoteValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfDebitNoteValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfDebitNoteValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfDebitNoteValues_DebitNotes_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "DebitNotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfGLBatchValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfGLBatchValues", x => x.Id);
                    table.CheckConstraint("CK_UdfGLBatchValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfGLBatchValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGLBatchValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGLBatchValues_GLBatches_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "GLBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfGLJournalLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfGLJournalLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfGLJournalLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfGLJournalLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfGLJournalLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGLJournalLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGLJournalLineValues_GLJournalHeaders_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "GLJournalHeaders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGLJournalLineValues_GLJournalLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "GLJournalLines",
                        principalColumns: new[] { "HeaderId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfGoodsReceiptDraftLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfGoodsReceiptDraftLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfGoodsReceiptDraftLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfGoodsReceiptDraftLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptDraftLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptDraftLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptDraftLineValues_GoodsReceiptDraftLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "GoodsReceiptDraftLines",
                        principalColumns: new[] { "GoodsReceiptDraftId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptDraftLineValues_GoodsReceiptDrafts_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "GoodsReceiptDrafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfGoodsReceiptDraftValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfGoodsReceiptDraftValues", x => x.Id);
                    table.CheckConstraint("CK_UdfGoodsReceiptDraftValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptDraftValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptDraftValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptDraftValues_GoodsReceiptDrafts_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "GoodsReceiptDrafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfGoodsReceiptLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfGoodsReceiptLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfGoodsReceiptLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfGoodsReceiptLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptLineValues_GoodsReceiptLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "GoodsReceiptLines",
                        principalColumns: new[] { "GoodsReceiptId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptLineValues_GoodsReceipts_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "GoodsReceipts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfGoodsReceiptValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfGoodsReceiptValues", x => x.Id);
                    table.CheckConstraint("CK_UdfGoodsReceiptValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfGoodsReceiptValues_GoodsReceipts_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "GoodsReceipts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfPurchaseInvoiceLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfPurchaseInvoiceLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfPurchaseInvoiceLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfPurchaseInvoiceLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfPurchaseInvoiceLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseInvoiceLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseInvoiceLineValues_PurchaseInvoiceLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "PurchaseInvoiceLines",
                        principalColumns: new[] { "PurchaseInvoiceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseInvoiceLineValues_PurchaseInvoices_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "PurchaseInvoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfPurchaseInvoiceValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfPurchaseInvoiceValues", x => x.Id);
                    table.CheckConstraint("CK_UdfPurchaseInvoiceValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfPurchaseInvoiceValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseInvoiceValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseInvoiceValues_PurchaseInvoices_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "PurchaseInvoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfPurchaseOrderLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfPurchaseOrderLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfPurchaseOrderLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfPurchaseOrderLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfPurchaseOrderLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseOrderLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseOrderLineValues_PurchaseOrderLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "PurchaseOrderLines",
                        principalColumns: new[] { "PurchaseOrderId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseOrderLineValues_PurchaseOrders_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfPurchaseOrderValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfPurchaseOrderValues", x => x.Id);
                    table.CheckConstraint("CK_UdfPurchaseOrderValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfPurchaseOrderValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseOrderValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseOrderValues_PurchaseOrders_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfPurchaseRequestLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfPurchaseRequestLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfPurchaseRequestLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfPurchaseRequestLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfPurchaseRequestLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseRequestLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseRequestLineValues_PurchaseRequestLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "PurchaseRequestLines",
                        principalColumns: new[] { "PurchaseRequestId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseRequestLineValues_PurchaseRequests_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "PurchaseRequests",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfPurchaseRequestValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfPurchaseRequestValues", x => x.Id);
                    table.CheckConstraint("CK_UdfPurchaseRequestValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfPurchaseRequestValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseRequestValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfPurchaseRequestValues_PurchaseRequests_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "PurchaseRequests",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfReceiptRefundLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfReceiptRefundLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfReceiptRefundLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfReceiptRefundLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfReceiptRefundLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfReceiptRefundLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfReceiptRefundLineValues_ReceiptRefundLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "ReceiptRefundLines",
                        principalColumns: new[] { "HeaderId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfReceiptRefundLineValues_ReceiptRefunds_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "ReceiptRefunds",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfReceiptRefundValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfReceiptRefundValues", x => x.Id);
                    table.CheckConstraint("CK_UdfReceiptRefundValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfReceiptRefundValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfReceiptRefundValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfReceiptRefundValues_ReceiptRefunds_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "ReceiptRefunds",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfSalesInvoiceLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfSalesInvoiceLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfSalesInvoiceLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfSalesInvoiceLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfSalesInvoiceLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesInvoiceLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesInvoiceLineValues_SalesInvoiceLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "SalesInvoiceLines",
                        principalColumns: new[] { "SalesInvoiceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfSalesInvoiceLineValues_SalesInvoices_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfSalesInvoiceValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfSalesInvoiceValues", x => x.Id);
                    table.CheckConstraint("CK_UdfSalesInvoiceValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfSalesInvoiceValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesInvoiceValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesInvoiceValues_SalesInvoices_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfSalesOrderLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfSalesOrderLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfSalesOrderLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfSalesOrderLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfSalesOrderLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesOrderLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesOrderLineValues_SalesOrderLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "SalesOrderLines",
                        principalColumns: new[] { "HeaderId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfSalesOrderLineValues_SalesOrders_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "SalesOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfSalesOrderValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfSalesOrderValues", x => x.Id);
                    table.CheckConstraint("CK_UdfSalesOrderValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfSalesOrderValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesOrderValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesOrderValues_SalesOrders_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "SalesOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfSalesQuoteLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfSalesQuoteLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfSalesQuoteLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfSalesQuoteLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfSalesQuoteLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesQuoteLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesQuoteLineValues_SalesQuoteLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "SalesQuoteLines",
                        principalColumns: new[] { "SalesQuoteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfSalesQuoteLineValues_SalesQuotes_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "SalesQuotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfSalesQuoteValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfSalesQuoteValues", x => x.Id);
                    table.CheckConstraint("CK_UdfSalesQuoteValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfSalesQuoteValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesQuoteValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesQuoteValues_SalesQuotes_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "SalesQuotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfSalesShipmentLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfSalesShipmentLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfSalesShipmentLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfSalesShipmentLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfSalesShipmentLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesShipmentLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesShipmentLineValues_SalesShipmentLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "SalesShipmentLines",
                        principalColumns: new[] { "ShipmentId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfSalesShipmentLineValues_SalesShipments_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "SalesShipments",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfSalesShipmentValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfSalesShipmentValues", x => x.Id);
                    table.CheckConstraint("CK_UdfSalesShipmentValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfSalesShipmentValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesShipmentValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfSalesShipmentValues_SalesShipments_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "SalesShipments",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfStockTransferLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfStockTransferLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfStockTransferLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfStockTransferLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfStockTransferLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfStockTransferLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfStockTransferLineValues_StockTransferLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "StockTransferLines",
                        principalColumns: new[] { "StockTransferId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfStockTransferLineValues_StockTransfers_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "StockTransfers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfStockTransferValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfStockTransferValues", x => x.Id);
                    table.CheckConstraint("CK_UdfStockTransferValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfStockTransferValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfStockTransferValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfStockTransferValues_StockTransfers_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "StockTransfers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfVendorPaymentValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfVendorPaymentValues", x => x.Id);
                    table.CheckConstraint("CK_UdfVendorPaymentValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfVendorPaymentValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfVendorPaymentValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfVendorPaymentValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfVendorPaymentValues_PurchaseInvoices_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "PurchaseInvoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfVendorPaymentValues_VendorPayments_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "VendorPayments",
                        principalColumns: new[] { "PurchaseInvoiceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfVendorReturnLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfVendorReturnLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfVendorReturnLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfVendorReturnLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfVendorReturnLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfVendorReturnLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfVendorReturnLineValues_VendorReturnLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "VendorReturnLines",
                        principalColumns: new[] { "HeaderId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfVendorReturnLineValues_VendorReturns_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "VendorReturns",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfVendorReturnValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfVendorReturnValues", x => x.Id);
                    table.CheckConstraint("CK_UdfVendorReturnValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfVendorReturnValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfVendorReturnValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfVendorReturnValues_VendorReturns_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "VendorReturns",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryAdjustmentRecordLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityChange = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    ValueChange = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CustomFieldsVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAdjustmentRecordLine", x => x.Id);
                    table.UniqueConstraint("AK_InventoryAdjustmentRecordLine_HeaderId_Id", x => new { x.HeaderId, x.Id });
                    table.ForeignKey(
                        name: "FK_InventoryAdjustmentRecordLine_InventoryAdjustmentDocument_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "InventoryAdjustmentDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfInventoryAdjustmentDocumentValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfInventoryAdjustmentDocumentValues", x => x.Id);
                    table.CheckConstraint("CK_UdfInventoryAdjustmentDocumentValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfInventoryAdjustmentDocumentValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfInventoryAdjustmentDocumentValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfInventoryAdjustmentDocumentValues_InventoryAdjustmentDocument_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "InventoryAdjustmentDocument",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfInventoryAdjustmentRecordLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UdfInventoryAdjustmentRecordLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfInventoryAdjustmentRecordLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfInventoryAdjustmentRecordLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfInventoryAdjustmentRecordLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfInventoryAdjustmentRecordLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfInventoryAdjustmentRecordLineValues_InventoryAdjustmentDocument_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "InventoryAdjustmentDocument",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfInventoryAdjustmentRecordLineValues_InventoryAdjustmentRecordLine_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "InventoryAdjustmentRecordLine",
                        principalColumns: new[] { "HeaderId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentDocument_CompanyId_GLBatchId",
                table: "InventoryAdjustmentDocument",
                columns: new[] { "CompanyId", "GLBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCashbookBatchValues_CompanyId_DefinitionId_OptionId",
                table: "UdfCashbookBatchValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCashbookBatchValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfCashbookBatchValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCashbookBatchValues_CompanyId_RecordId_DefinitionId",
                table: "UdfCashbookBatchValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfCashbookEntryValues_CompanyId_DefinitionId_OptionId",
                table: "UdfCashbookEntryValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCashbookEntryValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfCashbookEntryValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCashbookEntryValues_CompanyId_ParentId",
                table: "UdfCashbookEntryValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCashbookEntryValues_CompanyId_RecordId_DefinitionId",
                table: "UdfCashbookEntryValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfCashbookEntryValues_ParentId_RecordId",
                table: "UdfCashbookEntryValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCreditNoteLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfCreditNoteLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCreditNoteLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfCreditNoteLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCreditNoteLineValues_CompanyId_ParentId",
                table: "UdfCreditNoteLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCreditNoteLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfCreditNoteLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfCreditNoteLineValues_ParentId_RecordId",
                table: "UdfCreditNoteLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCreditNoteValues_CompanyId_DefinitionId_OptionId",
                table: "UdfCreditNoteValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCreditNoteValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfCreditNoteValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCreditNoteValues_CompanyId_RecordId_DefinitionId",
                table: "UdfCreditNoteValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfCustomerPaymentValues_CompanyId_DefinitionId_OptionId",
                table: "UdfCustomerPaymentValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCustomerPaymentValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfCustomerPaymentValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfCustomerPaymentValues_CompanyId_RecordId_DefinitionId",
                table: "UdfCustomerPaymentValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfDebitNoteLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfDebitNoteLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfDebitNoteLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfDebitNoteLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfDebitNoteLineValues_CompanyId_ParentId",
                table: "UdfDebitNoteLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfDebitNoteLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfDebitNoteLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfDebitNoteLineValues_ParentId_RecordId",
                table: "UdfDebitNoteLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfDebitNoteValues_CompanyId_DefinitionId_OptionId",
                table: "UdfDebitNoteValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfDebitNoteValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfDebitNoteValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfDebitNoteValues_CompanyId_RecordId_DefinitionId",
                table: "UdfDebitNoteValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfGLBatchValues_CompanyId_DefinitionId_OptionId",
                table: "UdfGLBatchValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGLBatchValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfGLBatchValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGLBatchValues_CompanyId_RecordId_DefinitionId",
                table: "UdfGLBatchValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfGLJournalLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfGLJournalLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGLJournalLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfGLJournalLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGLJournalLineValues_CompanyId_ParentId",
                table: "UdfGLJournalLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGLJournalLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfGLJournalLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfGLJournalLineValues_ParentId_RecordId",
                table: "UdfGLJournalLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptDraftLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfGoodsReceiptDraftLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptDraftLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfGoodsReceiptDraftLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptDraftLineValues_CompanyId_ParentId",
                table: "UdfGoodsReceiptDraftLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptDraftLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfGoodsReceiptDraftLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptDraftLineValues_ParentId_RecordId",
                table: "UdfGoodsReceiptDraftLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptDraftValues_CompanyId_DefinitionId_OptionId",
                table: "UdfGoodsReceiptDraftValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptDraftValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfGoodsReceiptDraftValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptDraftValues_CompanyId_RecordId_DefinitionId",
                table: "UdfGoodsReceiptDraftValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfGoodsReceiptLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfGoodsReceiptLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptLineValues_CompanyId_ParentId",
                table: "UdfGoodsReceiptLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfGoodsReceiptLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptLineValues_ParentId_RecordId",
                table: "UdfGoodsReceiptLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptValues_CompanyId_DefinitionId_OptionId",
                table: "UdfGoodsReceiptValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfGoodsReceiptValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfGoodsReceiptValues_CompanyId_RecordId_DefinitionId",
                table: "UdfGoodsReceiptValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryAdjustmentDocumentValues_CompanyId_DefinitionId_OptionId",
                table: "UdfInventoryAdjustmentDocumentValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryAdjustmentDocumentValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfInventoryAdjustmentDocumentValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryAdjustmentDocumentValues_CompanyId_RecordId_DefinitionId",
                table: "UdfInventoryAdjustmentDocumentValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryAdjustmentRecordLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfInventoryAdjustmentRecordLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryAdjustmentRecordLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfInventoryAdjustmentRecordLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryAdjustmentRecordLineValues_CompanyId_ParentId",
                table: "UdfInventoryAdjustmentRecordLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryAdjustmentRecordLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfInventoryAdjustmentRecordLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryAdjustmentRecordLineValues_ParentId_RecordId",
                table: "UdfInventoryAdjustmentRecordLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseInvoiceLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfPurchaseInvoiceLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseInvoiceLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfPurchaseInvoiceLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseInvoiceLineValues_CompanyId_ParentId",
                table: "UdfPurchaseInvoiceLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseInvoiceLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfPurchaseInvoiceLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseInvoiceLineValues_ParentId_RecordId",
                table: "UdfPurchaseInvoiceLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseInvoiceValues_CompanyId_DefinitionId_OptionId",
                table: "UdfPurchaseInvoiceValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseInvoiceValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfPurchaseInvoiceValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseInvoiceValues_CompanyId_RecordId_DefinitionId",
                table: "UdfPurchaseInvoiceValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseOrderLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfPurchaseOrderLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseOrderLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfPurchaseOrderLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseOrderLineValues_CompanyId_ParentId",
                table: "UdfPurchaseOrderLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseOrderLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfPurchaseOrderLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseOrderLineValues_ParentId_RecordId",
                table: "UdfPurchaseOrderLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseOrderValues_CompanyId_DefinitionId_OptionId",
                table: "UdfPurchaseOrderValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseOrderValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfPurchaseOrderValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseOrderValues_CompanyId_RecordId_DefinitionId",
                table: "UdfPurchaseOrderValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseRequestLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfPurchaseRequestLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseRequestLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfPurchaseRequestLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseRequestLineValues_CompanyId_ParentId",
                table: "UdfPurchaseRequestLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseRequestLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfPurchaseRequestLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseRequestLineValues_ParentId_RecordId",
                table: "UdfPurchaseRequestLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseRequestValues_CompanyId_DefinitionId_OptionId",
                table: "UdfPurchaseRequestValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseRequestValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfPurchaseRequestValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfPurchaseRequestValues_CompanyId_RecordId_DefinitionId",
                table: "UdfPurchaseRequestValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfReceiptRefundLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfReceiptRefundLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfReceiptRefundLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfReceiptRefundLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfReceiptRefundLineValues_CompanyId_ParentId",
                table: "UdfReceiptRefundLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfReceiptRefundLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfReceiptRefundLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfReceiptRefundLineValues_ParentId_RecordId",
                table: "UdfReceiptRefundLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfReceiptRefundValues_CompanyId_DefinitionId_OptionId",
                table: "UdfReceiptRefundValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfReceiptRefundValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfReceiptRefundValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfReceiptRefundValues_CompanyId_RecordId_DefinitionId",
                table: "UdfReceiptRefundValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesInvoiceLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfSalesInvoiceLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesInvoiceLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfSalesInvoiceLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesInvoiceLineValues_CompanyId_ParentId",
                table: "UdfSalesInvoiceLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesInvoiceLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfSalesInvoiceLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesInvoiceLineValues_ParentId_RecordId",
                table: "UdfSalesInvoiceLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesInvoiceValues_CompanyId_DefinitionId_OptionId",
                table: "UdfSalesInvoiceValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesInvoiceValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfSalesInvoiceValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesInvoiceValues_CompanyId_RecordId_DefinitionId",
                table: "UdfSalesInvoiceValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesOrderLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfSalesOrderLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesOrderLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfSalesOrderLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesOrderLineValues_CompanyId_ParentId",
                table: "UdfSalesOrderLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesOrderLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfSalesOrderLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesOrderLineValues_ParentId_RecordId",
                table: "UdfSalesOrderLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesOrderValues_CompanyId_DefinitionId_OptionId",
                table: "UdfSalesOrderValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesOrderValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfSalesOrderValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesOrderValues_CompanyId_RecordId_DefinitionId",
                table: "UdfSalesOrderValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesQuoteLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfSalesQuoteLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesQuoteLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfSalesQuoteLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesQuoteLineValues_CompanyId_ParentId",
                table: "UdfSalesQuoteLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesQuoteLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfSalesQuoteLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesQuoteLineValues_ParentId_RecordId",
                table: "UdfSalesQuoteLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesQuoteValues_CompanyId_DefinitionId_OptionId",
                table: "UdfSalesQuoteValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesQuoteValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfSalesQuoteValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesQuoteValues_CompanyId_RecordId_DefinitionId",
                table: "UdfSalesQuoteValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesShipmentLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfSalesShipmentLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesShipmentLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfSalesShipmentLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesShipmentLineValues_CompanyId_ParentId",
                table: "UdfSalesShipmentLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesShipmentLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfSalesShipmentLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesShipmentLineValues_ParentId_RecordId",
                table: "UdfSalesShipmentLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesShipmentValues_CompanyId_DefinitionId_OptionId",
                table: "UdfSalesShipmentValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesShipmentValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfSalesShipmentValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfSalesShipmentValues_CompanyId_RecordId_DefinitionId",
                table: "UdfSalesShipmentValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfStockTransferLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfStockTransferLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfStockTransferLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfStockTransferLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfStockTransferLineValues_CompanyId_ParentId",
                table: "UdfStockTransferLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfStockTransferLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfStockTransferLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfStockTransferLineValues_ParentId_RecordId",
                table: "UdfStockTransferLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfStockTransferValues_CompanyId_DefinitionId_OptionId",
                table: "UdfStockTransferValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfStockTransferValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfStockTransferValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfStockTransferValues_CompanyId_RecordId_DefinitionId",
                table: "UdfStockTransferValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorPaymentValues_CompanyId_DefinitionId_OptionId",
                table: "UdfVendorPaymentValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorPaymentValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfVendorPaymentValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorPaymentValues_CompanyId_ParentId",
                table: "UdfVendorPaymentValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorPaymentValues_CompanyId_RecordId_DefinitionId",
                table: "UdfVendorPaymentValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorPaymentValues_ParentId_RecordId",
                table: "UdfVendorPaymentValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorReturnLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfVendorReturnLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorReturnLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfVendorReturnLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorReturnLineValues_CompanyId_ParentId",
                table: "UdfVendorReturnLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorReturnLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfVendorReturnLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorReturnLineValues_ParentId_RecordId",
                table: "UdfVendorReturnLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorReturnValues_CompanyId_DefinitionId_OptionId",
                table: "UdfVendorReturnValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorReturnValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfVendorReturnValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfVendorReturnValues_CompanyId_RecordId_DefinitionId",
                table: "UdfVendorReturnValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UdfCashbookBatchValues");

            migrationBuilder.DropTable(
                name: "UdfCashbookEntryValues");

            migrationBuilder.DropTable(
                name: "UdfCreditNoteLineValues");

            migrationBuilder.DropTable(
                name: "UdfCreditNoteValues");

            migrationBuilder.DropTable(
                name: "UdfCustomerPaymentValues");

            migrationBuilder.DropTable(
                name: "UdfDebitNoteLineValues");

            migrationBuilder.DropTable(
                name: "UdfDebitNoteValues");

            migrationBuilder.DropTable(
                name: "UdfGLBatchValues");

            migrationBuilder.DropTable(
                name: "UdfGLJournalLineValues");

            migrationBuilder.DropTable(
                name: "UdfGoodsReceiptDraftLineValues");

            migrationBuilder.DropTable(
                name: "UdfGoodsReceiptDraftValues");

            migrationBuilder.DropTable(
                name: "UdfGoodsReceiptLineValues");

            migrationBuilder.DropTable(
                name: "UdfGoodsReceiptValues");

            migrationBuilder.DropTable(
                name: "UdfInventoryAdjustmentDocumentValues");

            migrationBuilder.DropTable(
                name: "UdfInventoryAdjustmentRecordLineValues");

            migrationBuilder.DropTable(
                name: "UdfPurchaseInvoiceLineValues");

            migrationBuilder.DropTable(
                name: "UdfPurchaseInvoiceValues");

            migrationBuilder.DropTable(
                name: "UdfPurchaseOrderLineValues");

            migrationBuilder.DropTable(
                name: "UdfPurchaseOrderValues");

            migrationBuilder.DropTable(
                name: "UdfPurchaseRequestLineValues");

            migrationBuilder.DropTable(
                name: "UdfPurchaseRequestValues");

            migrationBuilder.DropTable(
                name: "UdfReceiptRefundLineValues");

            migrationBuilder.DropTable(
                name: "UdfReceiptRefundValues");

            migrationBuilder.DropTable(
                name: "UdfSalesInvoiceLineValues");

            migrationBuilder.DropTable(
                name: "UdfSalesInvoiceValues");

            migrationBuilder.DropTable(
                name: "UdfSalesOrderLineValues");

            migrationBuilder.DropTable(
                name: "UdfSalesOrderValues");

            migrationBuilder.DropTable(
                name: "UdfSalesQuoteLineValues");

            migrationBuilder.DropTable(
                name: "UdfSalesQuoteValues");

            migrationBuilder.DropTable(
                name: "UdfSalesShipmentLineValues");

            migrationBuilder.DropTable(
                name: "UdfSalesShipmentValues");

            migrationBuilder.DropTable(
                name: "UdfStockTransferLineValues");

            migrationBuilder.DropTable(
                name: "UdfStockTransferValues");

            migrationBuilder.DropTable(
                name: "UdfVendorPaymentValues");

            migrationBuilder.DropTable(
                name: "UdfVendorReturnLineValues");

            migrationBuilder.DropTable(
                name: "UdfVendorReturnValues");

            migrationBuilder.DropTable(
                name: "InventoryAdjustmentRecordLine");

            migrationBuilder.DropTable(
                name: "InventoryAdjustmentDocument");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_VendorReturns_CompanyId_Id",
                table: "VendorReturns");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_VendorReturnLines_HeaderId_Id",
                table: "VendorReturnLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_VendorPayments_PurchaseInvoiceId_Id",
                table: "VendorPayments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_StockTransfers_CompanyId_Id",
                table: "StockTransfers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_StockTransferLines_StockTransferId_Id",
                table: "StockTransferLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SalesShipments_CompanyId_Id",
                table: "SalesShipments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SalesShipmentLines_ShipmentId_Id",
                table: "SalesShipmentLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SalesQuotes_CompanyId_Id",
                table: "SalesQuotes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SalesQuoteLines_SalesQuoteId_Id",
                table: "SalesQuoteLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SalesInvoices_CompanyId_Id",
                table: "SalesInvoices");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SalesInvoiceLines_SalesInvoiceId_Id",
                table: "SalesInvoiceLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ReceiptRefunds_CompanyId_Id",
                table: "ReceiptRefunds");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ReceiptRefundLines_HeaderId_Id",
                table: "ReceiptRefundLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PurchaseRequests_CompanyId_Id",
                table: "PurchaseRequests");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PurchaseRequestLines_PurchaseRequestId_Id",
                table: "PurchaseRequestLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PurchaseOrders_CompanyId_Id",
                table: "PurchaseOrders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PurchaseOrderLines_PurchaseOrderId_Id",
                table: "PurchaseOrderLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PurchaseInvoices_CompanyId_Id",
                table: "PurchaseInvoices");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PurchaseInvoiceLines_PurchaseInvoiceId_Id",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GoodsReceipts_CompanyId_Id",
                table: "GoodsReceipts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GoodsReceiptLines_GoodsReceiptId_Id",
                table: "GoodsReceiptLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GoodsReceiptDrafts_CompanyId_Id",
                table: "GoodsReceiptDrafts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GoodsReceiptDraftLines_GoodsReceiptDraftId_Id",
                table: "GoodsReceiptDraftLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GLJournalLines_HeaderId_Id",
                table: "GLJournalLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GLJournalHeaders_CompanyId_Id",
                table: "GLJournalHeaders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GLBatches_CompanyId_Id",
                table: "GLBatches");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DebitNotes_CompanyId_Id",
                table: "DebitNotes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DebitNoteLines_HeaderId_Id",
                table: "DebitNoteLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CustomerPayments_CompanyId_Id",
                table: "CustomerPayments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CreditNotes_CompanyId_Id",
                table: "CreditNotes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CreditNoteLines_HeaderId_Id",
                table: "CreditNoteLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CashbookEntries_CashbookBatchId_Id",
                table: "CashbookEntries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CashbookBatches_CompanyId_Id",
                table: "CashbookBatches");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "VendorReturns");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "VendorReturnLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "VendorPayments");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "StockTransferLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "SalesShipments");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "SalesShipmentLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "SalesQuotes");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "SalesQuoteLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "ReceiptRefunds");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "ReceiptRefundLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "PurchaseRequestLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "GoodsReceipts");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "GoodsReceiptLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "GoodsReceiptDrafts");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "GoodsReceiptDraftLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "GLJournalLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "GLJournalHeaders");

            migrationBuilder.DropColumn(
                name: "HasManualCustomFields",
                table: "GLJournalHeaders");

            migrationBuilder.DropColumn(
                name: "CustomFieldContext",
                table: "GLBatches");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "GLBatches");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "DebitNoteLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "CashbookEntries");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "CashbookBatches");

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesInvoiceId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_VendorReturnLines_HeaderId",
                table: "VendorReturnLines",
                column: "HeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorPayments_PurchaseInvoiceId",
                table: "VendorPayments",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_StockTransferId",
                table: "StockTransferLines",
                column: "StockTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_SalesQuoteId",
                table: "SalesQuoteLines",
                column: "SalesQuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_SalesInvoiceId",
                table: "SalesInvoiceLines",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRefundLines_HeaderId",
                table: "ReceiptRefundLines",
                column: "HeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestLines_PurchaseRequestId",
                table: "PurchaseRequestLines",
                column: "PurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId",
                table: "PurchaseOrderLines",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_PurchaseInvoiceId",
                table: "PurchaseInvoiceLines",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_GLJournalLines_HeaderId",
                table: "GLJournalLines",
                column: "HeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_GLBatches_CompanyId",
                table: "GLBatches",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNoteLines_HeaderId",
                table: "DebitNoteLines",
                column: "HeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_HeaderId",
                table: "CreditNoteLines",
                column: "HeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CashbookEntries_CashbookBatchId",
                table: "CashbookEntries",
                column: "CashbookBatchId");
        }
    }
}
