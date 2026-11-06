using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class SeparatePurchaseAndSalesDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Validate required legacy references before reshaping the schema.
            // Optional warehouse/project links are normalized to NULL below;
            // missing vendors, customers, currencies, or required request items
            // need data repair and must not be silently discarded.
            migrationBuilder.Sql("""
                IF EXISTS
                (
                    SELECT 1
                    FROM [VendorBills] bill
                    LEFT JOIN [Vendors] vendor ON vendor.[Id] = bill.[VendorId] AND vendor.[CompanyId] = bill.[CompanyId]
                    LEFT JOIN [Currencies] currency ON currency.[Id] = bill.[CurrencyId] AND currency.[CompanyId] = bill.[CompanyId]
                    WHERE vendor.[Id] IS NULL OR currency.[Id] IS NULL
                )
                    THROW 51001, N'Cannot migrate purchase invoices: a legacy vendor bill references a missing vendor or currency. Repair the source record and retry.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [PurchaseOrders] po
                    LEFT JOIN [Vendors] vendor ON vendor.[Id] = po.[VendorId] AND vendor.[CompanyId] = po.[CompanyId]
                    LEFT JOIN [Currencies] currency ON currency.[Id] = po.[CurrencyId] AND currency.[CompanyId] = po.[CompanyId]
                    WHERE po.[Status] = 1 AND (vendor.[Id] IS NULL OR currency.[Id] IS NULL)
                )
                    THROW 51002, N'Cannot migrate purchase requests: an approved legacy purchase order references a missing vendor or currency. Repair the source record and retry.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [PurchaseOrderLines] line
                    INNER JOIN [PurchaseOrders] po ON po.[Id] = line.[PurchaseOrderId]
                    LEFT JOIN [Items] item ON item.[Id] = line.[ItemId] AND item.[CompanyId] = po.[CompanyId]
                    WHERE po.[Status] = 1 AND item.[Id] IS NULL
                )
                    THROW 51003, N'Cannot migrate purchase requests: an approved purchase order line references a missing item. Repair the source line and retry.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [SalesOrders] so
                    LEFT JOIN [Customers] customer ON customer.[Id] = so.[CustomerId] AND customer.[CompanyId] = so.[CompanyId]
                    LEFT JOIN [Currencies] currency ON currency.[Id] = so.[CurrencyId] AND currency.[CompanyId] = so.[CompanyId]
                    WHERE so.[Status] = 3 AND (customer.[Id] IS NULL OR currency.[Id] IS NULL)
                )
                    THROW 51004, N'Cannot migrate sales quotes: a legacy quote references a missing customer or currency. Repair the source record and retry.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [SalesOrders] so
                    LEFT JOIN [Customers] customer ON customer.[Id] = so.[CustomerId] AND customer.[CompanyId] = so.[CompanyId]
                    LEFT JOIN [Currencies] currency ON currency.[Id] = so.[CurrencyId] AND currency.[CompanyId] = so.[CompanyId]
                    WHERE (so.[IsDirectInvoice] = 1 OR so.[InvoiceBatchId] IS NOT NULL
                           OR (so.[ConvertedFromQuoteNumber] IS NOT NULL AND so.[Status] = 0)
                           OR so.[OrderNumber] LIKE N'INV%')
                      AND (customer.[Id] IS NULL OR currency.[Id] IS NULL)
                )
                    THROW 51005, N'Cannot migrate sales invoices: a legacy invoice order references a missing customer or currency. Repair the source record and retry.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [SalesInvoices] si
                    LEFT JOIN [SalesOrders] so ON so.[Id] = si.[SalesOrderId]
                    WHERE
                        (
                            NOT EXISTS (SELECT 1 FROM [Customers] c WHERE c.[Id] = so.[CustomerId] AND c.[CompanyId] = si.[CompanyId])
                            AND NOT EXISTS (SELECT 1 FROM [Customers] c WHERE c.[Id] = si.[CustomerId] AND c.[CompanyId] = si.[CompanyId])
                        )
                        OR
                        (
                            NOT EXISTS (SELECT 1 FROM [Currencies] c WHERE c.[Id] = so.[CurrencyId] AND c.[CompanyId] = si.[CompanyId])
                            AND NOT EXISTS (SELECT 1 FROM [Currencies] c WHERE c.[CompanyId] = si.[CompanyId])
                        )
                )
                    THROW 51006, N'Cannot migrate sales invoices: an existing invoice has no valid customer or currency, and no company currency is available as a fallback. Repair the invoice/master data and retry.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [SalesOrders] so
                    JOIN [SalesInvoices] existing ON existing.[Id] = so.[Id] AND existing.[SalesOrderId] <> so.[Id]
                    WHERE (so.[IsDirectInvoice] = 1 OR so.[InvoiceBatchId] IS NOT NULL
                           OR (so.[ConvertedFromQuoteNumber] IS NOT NULL AND so.[Status] = 0)
                           OR so.[OrderNumber] LIKE N'INV%')
                      AND NOT EXISTS (SELECT 1 FROM [SalesInvoices] linked WHERE linked.[SalesOrderId] = so.[Id])
                )
                    THROW 51009, N'Cannot migrate sales invoices: a legacy invoice identifier is already used by a different sales invoice. Reconcile the conflicting records before retrying.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [SalesShipmentLines] shipmentLine
                    LEFT JOIN [SalesOrderLines] orderLine ON orderLine.[Id] = shipmentLine.[SalesOrderLineId]
                    WHERE orderLine.[Id] IS NULL
                )
                    THROW 51008, N'Cannot migrate shipments: a shipment line references a missing sales order line. Repair the source record and retry.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_SalesInvoices_SalesInvoiceId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesShipments_SalesOrders_SalesOrderId",
                table: "SalesShipments");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorPayments_VendorBills_VendorBillId",
                table: "VendorPayments");

            // Preserve the existing AP invoice register and its lines while the
            // CLR/entity and physical table names change from VendorBill to
            // PurchaseInvoice.
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[__LegacyPurchaseInvoices]', N'U') IS NOT NULL DROP TABLE [__LegacyPurchaseInvoices];
                IF OBJECT_ID(N'[__LegacyPurchaseInvoiceLines]', N'U') IS NOT NULL DROP TABLE [__LegacyPurchaseInvoiceLines];
                SELECT * INTO [__LegacyPurchaseInvoices] FROM [VendorBills];
                SELECT * INTO [__LegacyPurchaseInvoiceLines] FROM [VendorBillLines];
                """);

            migrationBuilder.DropTable(
                name: "VendorBillLines");

            migrationBuilder.DropTable(
                name: "VendorBills");

            migrationBuilder.RenameColumn(
                name: "VendorBillId",
                table: "VendorPayments",
                newName: "PurchaseInvoiceId");

            migrationBuilder.RenameIndex(
                name: "IX_VendorPayments_VendorBillId",
                table: "VendorPayments",
                newName: "IX_VendorPayments_PurchaseInvoiceId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderId",
                table: "SalesShipments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "SalesQuoteLineId",
                table: "SalesOrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReceivablesGlAccountId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Date",
                table: "SalesInvoices",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<Guid>(
                name: "DirectIncomeGlAccountId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "SalesInvoices",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "DiscountGlAccountId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercentage",
                table: "SalesInvoices",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "SalesInvoices",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "GLBatchId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvoiceBatchId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "SalesInvoices",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDirectInvoice",
                table: "SalesInvoices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "SalesInvoices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedDate",
                table: "SalesInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "SalesInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxGLAccountId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAmount",
                table: "SalesInvoices",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAmountForeign",
                table: "SalesInvoices",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "TransactionDateTime",
                table: "SalesInvoices",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingPercentage",
                table: "SalesInvoices",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "WithholdingSalesGlAccountId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WithholdingTaxId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithholdingTaxName",
                table: "SalesInvoices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RevenueGlAccountId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "ItemId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "SalesInvoiceLines",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "SalesInvoiceLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "SalesInvoiceLines",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityInvoiced",
                table: "SalesInvoiceLines",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesOrderLineId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentLineId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxGLAccountId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "SalesInvoiceLines",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UomConversionFactor",
                table: "SalesInvoiceLines",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "UomId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UomName",
                table: "SalesInvoiceLines",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseRequestLineId",
                table: "PurchaseOrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseOrderId",
                table: "GoodsReceipts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "GoodsReceipts",
                type: "uniqueidentifier",
                nullable: true);

            // Preserve orphaned legacy references while enforcing a real FK for
            // all receipt lines whose purchase order line still exists.
            migrationBuilder.AddColumn<Guid>(
                name: "LegacyPurchaseOrderLineId",
                table: "GoodsReceiptLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseOrderLineId",
                table: "GoodsReceiptLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.Sql("""
                UPDATE receiptLine
                SET [LegacyPurchaseOrderLineId] = receiptLine.[PurchaseOrderLineId],
                    [PurchaseOrderLineId] = NULL
                FROM [GoodsReceiptLines] receiptLine
                LEFT JOIN [PurchaseOrderLines] orderLine ON orderLine.[Id] = receiptLine.[PurchaseOrderLineId]
                WHERE receiptLine.[PurchaseOrderLineId] IS NOT NULL AND orderLine.[Id] IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "PurchaseInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountsPayableGlId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDirectBill = table.Column<bool>(type: "bit", nullable: false),
                    PurchaseInvoiceNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SupplierInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExternalInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BillDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalAmountForeign = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    MatchStatus = table.Column<int>(type: "int", nullable: false),
                    MatchVarianceReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TaxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxGLAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GoodsReceiptClearingGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiscountGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiscountPercentage = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingTaxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithholdingTaxName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WithholdingPercentage = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingAmountForeign = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomTransactionTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverrideExpenseGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverrideAccountsPayableGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsPosted = table.Column<bool>(type: "bit", nullable: false),
                    PostedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GLBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_CustomTransactionTypes_CustomTransactionTypeId",
                        column: x => x.CustomTransactionTypeId,
                        principalTable: "CustomTransactionTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RequestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountPercentage = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithholdingTaxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithholdingTaxName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WithholdingPercentage = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingAmountForeign = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountPercentage = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithholdingTaxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithholdingTaxName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WithholdingPercentage = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingSalesGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesQuotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesQuotes_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesQuotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesQuotes_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesQuotes_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UomName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UomConversionFactor = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ExpenseGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QuantityBilled = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitCostBilled = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    GoodsReceiptLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_GoodsReceiptLines_GoodsReceiptLineId",
                        column: x => x.GoodsReceiptLineId,
                        principalTable: "GoodsReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequestLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UomName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UomConversionFactor = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    QuantityRequested = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequestLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequestLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequestLines_PurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "PurchaseRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesQuoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesQuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UomName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UomConversionFactor = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesQuoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesQuoteLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesQuoteLines_SalesQuotes_SalesQuoteId",
                        column: x => x.SalesQuoteId,
                        principalTable: "SalesQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Keep legacy invoice IDs, lines, payment references and posting
            // metadata intact while adding the new document fields.
            migrationBuilder.Sql("""
                INSERT INTO [PurchaseInvoices]
                    ([Id],[CompanyId],[VendorId],[PurchaseOrderId],[ProjectId],[AccountsPayableGlId],[IsDirectBill],
                     [PurchaseInvoiceNumber],[SupplierInvoiceNumber],[ExternalInvoiceNumber],[BillDate],[InvoiceDate],[WarehouseId],
                     [CurrencyId],[ExchangeRate],[TotalAmountForeign],[TotalAmount],[MatchStatus],[MatchVarianceReason],[Description],
                     [TaxId],[TaxGLAccountId],[GoodsReceiptClearingGlAccountId],[DiscountGlAccountId],[DiscountPercentage],[DiscountAmount],
                     [WithholdingTaxId],[WithholdingTaxName],[WithholdingPercentage],[WithholdingAmountForeign],[WithholdingAmount],
                     [WithholdingGlAccountId],[CustomTransactionTypeId],[OverrideExpenseGlAccountId],[OverrideAccountsPayableGlAccountId],
                     [IsPosted],[PostedDate],[GLBatchId])
                SELECT old.[Id],old.[CompanyId],old.[VendorId],po.[Id],project.[Id],old.[AccountsPayableGlId],old.[IsDirectBill],
                       N'PINV-LEGACY-' + CONVERT(nvarchar(36),old.[Id]),old.[ExternalInvoiceNumber],old.[ExternalInvoiceNumber],old.[BillDate],old.[BillDate],warehouse.[Id],
                       old.[CurrencyId],CASE WHEN old.[ExchangeRate] > 0 THEN old.[ExchangeRate] ELSE 1 END,old.[TotalAmountForeign],old.[TotalAmount],
                       old.[MatchStatus],old.[MatchVarianceReason],old.[Description],old.[TaxId],old.[TaxGLAccountId],NULL,NULL,0,0,
                       old.[WithholdingTaxId],old.[WithholdingTaxName],old.[WithholdingPercentage],old.[WithholdingAmountForeign],old.[WithholdingAmount],
                       old.[WithholdingGlAccountId],transactionType.[Id],old.[OverrideExpenseGlAccountId],old.[OverrideAccountsPayableGlAccountId],
                       old.[IsPosted],old.[PostedDate],old.[GLBatchId]
                FROM [__LegacyPurchaseInvoices] old
                LEFT JOIN [PurchaseOrders] po ON po.[Id] = old.[PurchaseOrderId] AND po.[CompanyId] = old.[CompanyId]
                LEFT JOIN [Warehouses] warehouse ON warehouse.[Id] = po.[WarehouseId] AND warehouse.[CompanyId] = old.[CompanyId]
                LEFT JOIN [Projects] project ON project.[Id] = old.[ProjectId] AND project.[CompanyId] = old.[CompanyId]
                LEFT JOIN [CustomTransactionTypes] transactionType ON transactionType.[Id] = old.[CustomTransactionTypeId];

                INSERT INTO [PurchaseInvoiceLines]
                    ([Id],[PurchaseInvoiceId],[ItemId],[UomId],[UomName],[UomConversionFactor],[ExpenseGlAccountId],[Description],
                     [QuantityBilled],[UnitCostBilled],[GoodsReceiptLineId],[PurchaseOrderLineId])
                SELECT old.[Id],old.[VendorBillId],item.[Id],old.[UomId],old.[UomName],old.[UomConversionFactor],old.[ExpenseGlAccountId],
                       old.[Description],old.[QuantityBilled],old.[UnitCostBilled],NULL,NULL
                FROM [__LegacyPurchaseInvoiceLines] old
                JOIN [__LegacyPurchaseInvoices] bill ON bill.[Id] = old.[VendorBillId]
                LEFT JOIN [Items] item ON item.[Id] = old.[ItemId] AND item.[CompanyId] = bill.[CompanyId];

                DROP TABLE [__LegacyPurchaseInvoiceLines];
                DROP TABLE [__LegacyPurchaseInvoices];

                UPDATE si
                SET [InvoiceNumber] = N'AR-LEGACY-' + CONVERT(nvarchar(36),si.[Id]),
                    [Date] = COALESCE(so.[Date],CONVERT(date,GETDATE())),
                    [TransactionDateTime] = COALESCE(so.[TransactionDateTime],CONVERT(datetime2,GETDATE())),
                    [CustomerId] = COALESCE(orderCustomer.[Id],invoiceCustomer.[Id]),
                    [CurrencyId] = COALESCE(orderCurrency.[Id],currency.[Id],invoiceCurrency.[Id]),
                    [ExchangeRate] = CASE WHEN COALESCE(so.[ExchangeRate],0) > 0 THEN so.[ExchangeRate] ELSE 1 END,
                    [WarehouseId] = COALESCE(orderWarehouse.[Id],invoiceWarehouse.[Id]),
                    [ProjectId] = so.[ProjectId],
                    [TaxId] = so.[TaxId],
                    [TaxGLAccountId] = so.[TaxGLAccountId],
                    [DiscountPercentage] = COALESCE(so.[DiscountPercentage],0),
                    [DiscountAmount] = COALESCE(so.[DiscountAmount],0),
                    [DiscountGlAccountId] = so.[DiscountGlAccountId],
                    [WithholdingTaxId] = so.[WithholdingTaxId],
                    [WithholdingTaxName] = so.[WithholdingTaxName],
                    [WithholdingPercentage] = COALESCE(so.[WithholdingPercentage],0),
                    [WithholdingSalesGlAccountId] = so.[WithholdingSalesGlAccountId],
                    [DirectIncomeGlAccountId] = so.[DirectIncomeGlAccountId],
                    [CustomTransactionTypeId] = so.[CustomTransactionTypeId],
                    [IsDirectInvoice] = COALESCE(so.[IsDirectInvoice],0),
                    [InvoiceBatchId] = so.[InvoiceBatchId],
                    [GLBatchId] = so.[InvoiceBatchId],
                    [Status] = CASE WHEN so.[InvoiceBatchId] IS NULL THEN 0
                                    WHEN EXISTS (SELECT 1 FROM [GLBatches] b WHERE b.[Id] = so.[InvoiceBatchId] AND b.[Status] = 2) THEN 2
                                    ELSE 1 END
                FROM [SalesInvoices] si
                LEFT JOIN [SalesOrders] so ON so.[Id] = si.[SalesOrderId]
                LEFT JOIN [Customers] orderCustomer ON orderCustomer.[Id] = so.[CustomerId] AND orderCustomer.[CompanyId] = si.[CompanyId]
                LEFT JOIN [Customers] invoiceCustomer ON invoiceCustomer.[Id] = si.[CustomerId] AND invoiceCustomer.[CompanyId] = si.[CompanyId]
                LEFT JOIN [Currencies] orderCurrency ON orderCurrency.[Id] = so.[CurrencyId] AND orderCurrency.[CompanyId] = si.[CompanyId]
                LEFT JOIN [Currencies] invoiceCurrency ON invoiceCurrency.[Id] = si.[CurrencyId] AND invoiceCurrency.[CompanyId] = si.[CompanyId]
                LEFT JOIN [Warehouses] orderWarehouse ON orderWarehouse.[Id] = so.[WarehouseId] AND orderWarehouse.[CompanyId] = si.[CompanyId]
                LEFT JOIN [Warehouses] invoiceWarehouse ON invoiceWarehouse.[Id] = si.[WarehouseId] AND invoiceWarehouse.[CompanyId] = si.[CompanyId]
                OUTER APPLY (SELECT TOP (1) c.[Id] FROM [Currencies] c WHERE c.[CompanyId] = si.[CompanyId] ORDER BY c.[IsBaseCurrency] DESC,c.[CurrencyCode]) currency;

                UPDATE invoiceLine
                SET [ItemId] = item.[Id],
                    [SalesOrderLineId] = salesOrderLine.[Id],
                    [ShipmentLineId] = shipmentLine.[Id],
                    [QuantityInvoiced] = CASE WHEN COALESCE(invoiceLine.[Amount],0) <> 0 THEN 1 ELSE 0 END,
                    [UnitPrice] = COALESCE(invoiceLine.[Amount],0),
                    [UomConversionFactor] = 1
                FROM [SalesInvoiceLines] invoiceLine
                LEFT JOIN [SalesInvoices] invoice ON invoice.[Id] = invoiceLine.[SalesInvoiceId]
                LEFT JOIN [Items] item ON item.[Id] = invoiceLine.[ItemId] AND item.[CompanyId] = invoice.[CompanyId]
                LEFT JOIN [SalesOrderLines] salesOrderLine ON salesOrderLine.[Id] = invoiceLine.[SalesOrderLineId]
                LEFT JOIN [SalesShipmentLines] shipmentLine ON shipmentLine.[Id] = invoiceLine.[ShipmentLineId];

                UPDATE shipment
                SET [SalesOrderId] = NULL
                FROM [SalesShipments] shipment
                LEFT JOIN [SalesOrders] salesOrder ON salesOrder.[Id] = shipment.[SalesOrderId]
                WHERE shipment.[SalesOrderId] IS NOT NULL AND salesOrder.[Id] IS NULL;

                INSERT INTO [SalesInvoices]
                    ([Id],[CompanyId],[CustomerId],[SalesOrderId],[WarehouseId],[ProjectId],[InvoiceNumber],[Date],[TransactionDateTime],
                     [Status],[IsDirectInvoice],[CurrencyId],[ExchangeRate],[TaxId],[TaxGLAccountId],[DiscountPercentage],[DiscountAmount],
                     [DiscountGlAccountId],[WithholdingTaxId],[WithholdingTaxName],[WithholdingPercentage],[WithholdingSalesGlAccountId],
                     [ReceivablesGlAccountId],[DirectIncomeGlAccountId],[InvoiceBatchId],[GLBatchId],[IsPaid],[TotalAmountForeign],[TotalAmount])
                SELECT s.[Id],s.[CompanyId],s.[CustomerId],s.[Id],warehouse.[Id],
                       s.[ProjectId],N'AR-LEGACY-' + CONVERT(nvarchar(36),s.[Id]),s.[Date],s.[TransactionDateTime],
                       CASE WHEN s.[InvoiceBatchId] IS NULL THEN 0
                            WHEN EXISTS (SELECT 1 FROM [GLBatches] b WHERE b.[Id] = s.[InvoiceBatchId] AND b.[Status] = 2) THEN 2 ELSE 1 END,
                       s.[IsDirectInvoice],s.[CurrencyId],CASE WHEN s.[ExchangeRate] > 0 THEN s.[ExchangeRate] ELSE 1 END,
                       s.[TaxId],s.[TaxGLAccountId],s.[DiscountPercentage],s.[DiscountAmount],s.[DiscountGlAccountId],
                       s.[WithholdingTaxId],s.[WithholdingTaxName],s.[WithholdingPercentage],s.[WithholdingSalesGlAccountId],
                       s.[ReceivablesGlAccountId],s.[DirectIncomeGlAccountId],s.[InvoiceBatchId],s.[InvoiceBatchId],0,0,0
                FROM [SalesOrders] s
                LEFT JOIN [Warehouses] warehouse ON warehouse.[Id] = s.[WarehouseId] AND warehouse.[CompanyId] = s.[CompanyId]
                WHERE (s.[IsDirectInvoice] = 1 OR s.[InvoiceBatchId] IS NOT NULL
                       OR (s.[ConvertedFromQuoteNumber] IS NOT NULL AND s.[Status] = 0)
                       OR s.[OrderNumber] LIKE N'INV%')
                  AND NOT EXISTS (SELECT 1 FROM [SalesInvoices] existing WHERE existing.[SalesOrderId] = s.[Id]);

                INSERT INTO [SalesInvoiceLines]
                    ([Id],[SalesInvoiceId],[SalesOrderLineId],[ItemId],[Amount],[RevenueGlAccountId],[TaxId],[TaxGLAccountId],
                     [DiscountAmount],[Description],[UomId],[UomName],[UomConversionFactor],[QuantityInvoiced],[UnitPrice])
                SELECT NEWID(),invoice.[Id],orderLine.[Id],item.[Id],
                       CASE WHEN orderLine.[QtyInvoiced] > 0 THEN orderLine.[QtyInvoiced] * orderLine.[UnitPrice]
                            ELSE orderLine.[Quantity] * orderLine.[UnitPrice] END,
                       item.[SalesIncomeAccountId],salesOrder.[TaxId],salesOrder.[TaxGLAccountId],0,orderLine.[Description],
                       orderLine.[UomId],orderLine.[UomName],CASE WHEN orderLine.[UomConversionFactor] > 0 THEN orderLine.[UomConversionFactor] ELSE 1 END,
                       CASE WHEN orderLine.[QtyInvoiced] > 0 THEN orderLine.[QtyInvoiced] ELSE orderLine.[Quantity] END,orderLine.[UnitPrice]
                FROM [SalesInvoices] invoice
                JOIN [SalesOrders] salesOrder ON salesOrder.[Id] = invoice.[SalesOrderId]
                JOIN [SalesOrderLines] orderLine ON orderLine.[HeaderId] = salesOrder.[Id]
                LEFT JOIN [Items] item ON item.[Id] = orderLine.[ItemId] AND item.[CompanyId] = salesOrder.[CompanyId]
                WHERE (salesOrder.[IsDirectInvoice] = 1 OR salesOrder.[InvoiceBatchId] IS NOT NULL
                       OR (salesOrder.[ConvertedFromQuoteNumber] IS NOT NULL AND salesOrder.[Status] = 0)
                       OR salesOrder.[OrderNumber] LIKE N'INV%')
                  AND NOT EXISTS (SELECT 1 FROM [SalesInvoiceLines] oldLine WHERE oldLine.[SalesInvoiceId] = invoice.[Id]);

                UPDATE invoice
                SET [TotalAmountForeign] = totals.[TotalForeign],
                    [TotalAmount] = ROUND(totals.[TotalForeign] * invoice.[ExchangeRate],2)
                FROM [SalesInvoices] invoice
                OUTER APPLY
                (
                    SELECT COALESCE(SUM(ROUND(line.[QuantityInvoiced] * line.[UnitPrice],2)),0) AS [Subtotal]
                    FROM [SalesInvoiceLines] line
                    WHERE line.[SalesInvoiceId] = invoice.[Id]
                ) lines
                LEFT JOIN [Taxes] tax ON tax.[Id] = invoice.[TaxId] AND tax.[CompanyId] = invoice.[CompanyId]
                CROSS APPLY
                (
                    SELECT CASE WHEN invoice.[DiscountPercentage] > 0 THEN lines.[Subtotal] * invoice.[DiscountPercentage] / 100
                                WHEN invoice.[DiscountAmount] > lines.[Subtotal] THEN lines.[Subtotal]
                                ELSE invoice.[DiscountAmount] END AS [Discount]
                ) discount
                CROSS APPLY
                (
                    SELECT (lines.[Subtotal] - discount.[Discount]) * (1 + COALESCE(tax.[Per],0) / 100) AS [TotalForeign]
                ) totals;

                INSERT INTO [PurchaseRequests]
                    ([Id],[CompanyId],[VendorId],[WarehouseId],[CurrencyId],[RequestNumber],[RequestDate],[Status],[ProjectId],
                     [ExchangeRate],[DiscountPercentage],[DiscountAmount],[DiscountGlAccountId],[TaxId],[TaxGlAccountId],
                     [WithholdingTaxId],[WithholdingTaxName],[WithholdingPercentage],[WithholdingAmountForeign],[WithholdingAmount],[WithholdingGlAccountId])
                SELECT p.[Id],p.[CompanyId],p.[VendorId],warehouse.[Id],p.[CurrencyId],p.[OrderNumber],p.[OrderDate],1,project.[Id],
                       p.[ExchangeRate],p.[DiscountPercentage],p.[DiscountAmount],p.[DiscountGlAccountId],p.[TaxId],p.[TaxGLAccountId],
                       p.[WithholdingTaxId],p.[WithholdingTaxName],p.[WithholdingPercentage],p.[WithholdingAmountForeign],p.[WithholdingAmount],p.[WithholdingGlAccountId]
                FROM [PurchaseOrders] p
                LEFT JOIN [Warehouses] warehouse ON warehouse.[Id] = p.[WarehouseId] AND warehouse.[CompanyId] = p.[CompanyId]
                LEFT JOIN [Projects] project ON project.[Id] = p.[ProjectId] AND project.[CompanyId] = p.[CompanyId]
                WHERE p.[Status] = 1;

                INSERT INTO [PurchaseRequestLines]
                    ([Id],[PurchaseRequestId],[ItemId],[UomId],[UomName],[UomConversionFactor],[QuantityRequested],[UnitCost],[Description])
                SELECT l.[Id],l.[PurchaseOrderId],item.[Id],l.[UomId],l.[UomName],l.[UomConversionFactor],l.[QuantityOrdered],l.[UnitCost],NULL
                FROM [PurchaseOrderLines] l
                JOIN [PurchaseOrders] p ON p.[Id] = l.[PurchaseOrderId]
                JOIN [PurchaseRequests] request ON request.[Id] = p.[Id]
                JOIN [Items] item ON item.[Id] = l.[ItemId] AND item.[CompanyId] = p.[CompanyId]
                WHERE p.[Status] = 1;

                INSERT INTO [SalesQuotes]
                    ([Id],[CompanyId],[CustomerId],[WarehouseId],[CurrencyId],[QuoteNumber],[Date],[TransactionDateTime],[Status],[ValidUntil],[ProjectId],
                     [ExchangeRate],[DiscountPercentage],[DiscountAmount],[DiscountGlAccountId],[TaxId],[TaxGlAccountId],
                     [WithholdingTaxId],[WithholdingTaxName],[WithholdingPercentage],[WithholdingSalesGlAccountId])
                SELECT s.[Id],s.[CompanyId],s.[CustomerId],warehouse.[Id],s.[CurrencyId],s.[OrderNumber],s.[Date],s.[TransactionDateTime],1,NULL,project.[Id],
                       s.[ExchangeRate],s.[DiscountPercentage],s.[DiscountAmount],s.[DiscountGlAccountId],s.[TaxId],s.[TaxGLAccountId],
                       s.[WithholdingTaxId],s.[WithholdingTaxName],s.[WithholdingPercentage],s.[WithholdingSalesGlAccountId]
                FROM [SalesOrders] s
                LEFT JOIN [Warehouses] warehouse ON warehouse.[Id] = s.[WarehouseId] AND warehouse.[CompanyId] = s.[CompanyId]
                LEFT JOIN [Projects] project ON project.[Id] = s.[ProjectId] AND project.[CompanyId] = s.[CompanyId]
                WHERE s.[Status] = 3;

                INSERT INTO [SalesQuoteLines]
                    ([Id],[SalesQuoteId],[ItemId],[Description],[UomId],[UomName],[UomConversionFactor],[Quantity],[UnitPrice])
                SELECT l.[Id],q.[Id],item.[Id],l.[Description],l.[UomId],l.[UomName],l.[UomConversionFactor],l.[Quantity],l.[UnitPrice]
                FROM [SalesOrderLines] l
                JOIN [SalesOrders] s ON s.[Id] = l.[HeaderId]
                JOIN [SalesQuotes] q ON q.[Id] = s.[Id]
                LEFT JOIN [Items] item ON item.[Id] = l.[ItemId] AND item.[CompanyId] = s.[CompanyId]
                WHERE s.[Status] = 3;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SalesShipmentLines_SalesOrderLineId",
                table: "SalesShipmentLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesShipmentLines_ShipmentId_SalesOrderLineId",
                table: "SalesShipmentLines",
                columns: new[] { "ShipmentId", "SalesOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_SalesQuoteLineId",
                table: "SalesOrderLines",
                column: "SalesQuoteLineId",
                unique: true,
                filter: "[SalesQuoteLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CompanyId_InvoiceNumber",
                table: "SalesInvoices",
                columns: new[] { "CompanyId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CurrencyId",
                table: "SalesInvoices",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CustomerId",
                table: "SalesInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_WarehouseId",
                table: "SalesInvoices",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_ItemId",
                table: "SalesInvoiceLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_SalesOrderLineId",
                table: "SalesInvoiceLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_ShipmentLineId",
                table: "SalesInvoiceLines",
                column: "ShipmentLineId",
                unique: true,
                filter: "[ShipmentLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_WarehouseId",
                table: "PurchaseOrders",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseRequestLineId",
                table: "PurchaseOrderLines",
                column: "PurchaseRequestLineId",
                unique: true,
                filter: "[PurchaseRequestLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_WarehouseId",
                table: "GoodsReceipts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLines_PurchaseOrderLineId",
                table: "GoodsReceiptLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLines_GoodsReceiptId_PurchaseOrderLineId",
                table: "GoodsReceiptLines",
                columns: new[] { "GoodsReceiptId", "PurchaseOrderLineId" },
                unique: true,
                filter: "[PurchaseOrderLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_GoodsReceiptLineId",
                table: "PurchaseInvoiceLines",
                column: "GoodsReceiptLineId",
                unique: true,
                filter: "[GoodsReceiptLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_ItemId",
                table: "PurchaseInvoiceLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_PurchaseInvoiceId",
                table: "PurchaseInvoiceLines",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_PurchaseOrderLineId",
                table: "PurchaseInvoiceLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_CompanyId_PurchaseInvoiceNumber",
                table: "PurchaseInvoices",
                columns: new[] { "CompanyId", "PurchaseInvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_CurrencyId",
                table: "PurchaseInvoices",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_CustomTransactionTypeId",
                table: "PurchaseInvoices",
                column: "CustomTransactionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_ProjectId",
                table: "PurchaseInvoices",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_PurchaseOrderId",
                table: "PurchaseInvoices",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_VendorId",
                table: "PurchaseInvoices",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_WarehouseId",
                table: "PurchaseInvoices",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestLines_ItemId",
                table: "PurchaseRequestLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestLines_PurchaseRequestId",
                table: "PurchaseRequestLines",
                column: "PurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_CompanyId_RequestNumber",
                table: "PurchaseRequests",
                columns: new[] { "CompanyId", "RequestNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_CurrencyId",
                table: "PurchaseRequests",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_ProjectId",
                table: "PurchaseRequests",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_VendorId",
                table: "PurchaseRequests",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_WarehouseId",
                table: "PurchaseRequests",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_ItemId",
                table: "SalesQuoteLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_SalesQuoteId",
                table: "SalesQuoteLines",
                column: "SalesQuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_CompanyId_QuoteNumber",
                table: "SalesQuotes",
                columns: new[] { "CompanyId", "QuoteNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_CurrencyId",
                table: "SalesQuotes",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_CustomerId",
                table: "SalesQuotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_ProjectId",
                table: "SalesQuotes",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_WarehouseId",
                table: "SalesQuotes",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptLines_PurchaseOrderLines_PurchaseOrderLineId",
                table: "GoodsReceiptLines",
                column: "PurchaseOrderLineId",
                principalTable: "PurchaseOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceipts_Warehouses_WarehouseId",
                table: "GoodsReceipts",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_PurchaseRequestLines_PurchaseRequestLineId",
                table: "PurchaseOrderLines",
                column: "PurchaseRequestLineId",
                principalTable: "PurchaseRequestLines",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Warehouses_WarehouseId",
                table: "PurchaseOrders",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_Items_ItemId",
                table: "SalesInvoiceLines",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_SalesInvoices_SalesInvoiceId",
                table: "SalesInvoiceLines",
                column: "SalesInvoiceId",
                principalTable: "SalesInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_SalesOrderLines_SalesOrderLineId",
                table: "SalesInvoiceLines",
                column: "SalesOrderLineId",
                principalTable: "SalesOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_SalesShipmentLines_ShipmentLineId",
                table: "SalesInvoiceLines",
                column: "ShipmentLineId",
                principalTable: "SalesShipmentLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_Currencies_CurrencyId",
                table: "SalesInvoices",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_Customers_CustomerId",
                table: "SalesInvoices",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_Warehouses_WarehouseId",
                table: "SalesInvoices",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_SalesQuoteLines_SalesQuoteLineId",
                table: "SalesOrderLines",
                column: "SalesQuoteLineId",
                principalTable: "SalesQuoteLines",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesShipmentLines_SalesOrderLines_SalesOrderLineId",
                table: "SalesShipmentLines",
                column: "SalesOrderLineId",
                principalTable: "SalesOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesShipments_SalesOrders_SalesOrderId",
                table: "SalesShipments",
                column: "SalesOrderId",
                principalTable: "SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorPayments_PurchaseInvoices_PurchaseInvoiceId",
                table: "VendorPayments",
                column: "PurchaseInvoiceId",
                principalTable: "PurchaseInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [GoodsReceiptLines] WHERE [LegacyPurchaseOrderLineId] IS NOT NULL OR [PurchaseOrderLineId] IS NULL) THROW 51010, 'Cannot roll back: legacy goods receipt links must be repaired first.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptLines_PurchaseOrderLines_PurchaseOrderLineId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceipts_Warehouses_WarehouseId",
                table: "GoodsReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_PurchaseRequestLines_PurchaseRequestLineId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Warehouses_WarehouseId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_Items_ItemId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_SalesInvoices_SalesInvoiceId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_SalesOrderLines_SalesOrderLineId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_SalesShipmentLines_ShipmentLineId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_Currencies_CurrencyId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_Customers_CustomerId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_Warehouses_WarehouseId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_SalesQuoteLines_SalesQuoteLineId",
                table: "SalesOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesShipmentLines_SalesOrderLines_SalesOrderLineId",
                table: "SalesShipmentLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesShipments_SalesOrders_SalesOrderId",
                table: "SalesShipments");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorPayments_PurchaseInvoices_PurchaseInvoiceId",
                table: "VendorPayments");

            // Keep documents created after this migration when rolling back to
            // the legacy shared order tables. Existing migrated rows keep their
            // original IDs, so only genuinely new request/quote headers are added.
            migrationBuilder.Sql("""
                INSERT INTO [PurchaseOrders]
                    ([Id],[CompanyId],[VendorId],[WarehouseId],[ProjectId],[OrderNumber],[OrderDate],[CurrencyId],[ExchangeRate],[Status],
                     [IsDirectInvoice],[HasReceipt],[IsFullyReceived],[IsInvoicePosted],[IsFullyPaid],
                     [TaxId],[TaxGLAccountId],[WithholdingTaxId],[WithholdingTaxName],[WithholdingPercentage],[WithholdingAmountForeign],
                     [WithholdingAmount],[WithholdingGlAccountId],[DiscountPercentage],[DiscountAmount],[DiscountGlAccountId])
                SELECT r.[Id],r.[CompanyId],r.[VendorId],r.[WarehouseId],r.[ProjectId],r.[RequestNumber],r.[RequestDate],r.[CurrencyId],
                       CASE WHEN r.[ExchangeRate] > 0 THEN r.[ExchangeRate] ELSE 1 END,
                       CASE WHEN r.[Status] = 4 THEN 6 ELSE 1 END,0,0,0,0,0,
                       r.[TaxId],r.[TaxGlAccountId],r.[WithholdingTaxId],r.[WithholdingTaxName],r.[WithholdingPercentage],
                       r.[WithholdingAmountForeign],r.[WithholdingAmount],r.[WithholdingGlAccountId],r.[DiscountPercentage],r.[DiscountAmount],r.[DiscountGlAccountId]
                FROM [PurchaseRequests] r
                WHERE NOT EXISTS (SELECT 1 FROM [PurchaseOrders] p WHERE p.[Id] = r.[Id]);

                INSERT INTO [PurchaseOrderLines]
                    ([Id],[PurchaseOrderId],[ItemId],[UomId],[UomName],[UomConversionFactor],[QuantityOrdered],[UnitCost],[QuantityReceived],[QuantityBilled])
                SELECT l.[Id],l.[PurchaseRequestId],l.[ItemId],l.[UomId],l.[UomName],l.[UomConversionFactor],l.[QuantityRequested],l.[UnitCost],0,0
                FROM [PurchaseRequestLines] l
                WHERE NOT EXISTS (SELECT 1 FROM [PurchaseOrderLines] oldLine WHERE oldLine.[Id] = l.[Id]);

                INSERT INTO [SalesOrders]
                    ([Id],[CompanyId],[CustomerId],[ProjectId],[WarehouseId],[OrderNumber],[Date],[TransactionDateTime],[CurrencyId],[ExchangeRate],
                     [Status],[IsDirectInvoice],[TaxId],[TaxGLAccountId],[WithholdingTaxId],[WithholdingTaxName],[WithholdingPercentage],
                     [WithholdingSalesGlAccountId],[DiscountPercentage],[DiscountAmount],[DiscountGlAccountId])
                SELECT q.[Id],q.[CompanyId],q.[CustomerId],q.[ProjectId],q.[WarehouseId],
                       q.[QuoteNumber],q.[Date],q.[TransactionDateTime],q.[CurrencyId],CASE WHEN q.[ExchangeRate] > 0 THEN q.[ExchangeRate] ELSE 1 END,
                       CASE WHEN q.[Status] = 4 OR q.[Status] = 5 THEN 8 ELSE 3 END,0,q.[TaxId],q.[TaxGlAccountId],q.[WithholdingTaxId],
                       q.[WithholdingTaxName],q.[WithholdingPercentage],q.[WithholdingSalesGlAccountId],q.[DiscountPercentage],q.[DiscountAmount],q.[DiscountGlAccountId]
                FROM [SalesQuotes] q
                WHERE NOT EXISTS (SELECT 1 FROM [SalesOrders] s WHERE s.[Id] = q.[Id]);

                INSERT INTO [SalesOrderLines]
                    ([Id],[HeaderId],[ItemId],[Description],[UomId],[UomName],[UomConversionFactor],[Quantity],[UnitPrice],[QtyShipped],[QtyInvoiced])
                SELECT l.[Id],l.[SalesQuoteId],l.[ItemId],l.[Description],l.[UomId],l.[UomName],l.[UomConversionFactor],l.[Quantity],l.[UnitPrice],0,0
                FROM [SalesQuoteLines] l
                WHERE NOT EXISTS (SELECT 1 FROM [SalesOrderLines] oldLine WHERE oldLine.[Id] = l.[Id]);
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[__RollbackPurchaseInvoices]', N'U') IS NOT NULL DROP TABLE [__RollbackPurchaseInvoices];
                IF OBJECT_ID(N'[__RollbackPurchaseInvoiceLines]', N'U') IS NOT NULL DROP TABLE [__RollbackPurchaseInvoiceLines];
                SELECT * INTO [__RollbackPurchaseInvoices] FROM [PurchaseInvoices];
                SELECT * INTO [__RollbackPurchaseInvoiceLines] FROM [PurchaseInvoiceLines];
                """);

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceLines");

            migrationBuilder.DropTable(
                name: "PurchaseRequestLines");

            migrationBuilder.DropTable(
                name: "SalesQuoteLines");

            migrationBuilder.DropTable(
                name: "PurchaseInvoices");

            migrationBuilder.DropTable(
                name: "PurchaseRequests");

            migrationBuilder.DropTable(
                name: "SalesQuotes");

            migrationBuilder.DropIndex(
                name: "IX_SalesShipmentLines_SalesOrderLineId",
                table: "SalesShipmentLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesShipmentLines_ShipmentId_SalesOrderLineId",
                table: "SalesShipmentLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLines_SalesQuoteLineId",
                table: "SalesOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_CompanyId_InvoiceNumber",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_CurrencyId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_CustomerId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_WarehouseId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_ItemId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_SalesOrderLineId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_ShipmentLineId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_WarehouseId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLines_PurchaseRequestLineId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceipts_WarehouseId",
                table: "GoodsReceipts");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptLines_PurchaseOrderLineId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptLines_GoodsReceiptId_PurchaseOrderLineId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropColumn(name: "LegacyPurchaseOrderLineId", table: "GoodsReceiptLines");
            migrationBuilder.AlterColumn<Guid>(name: "PurchaseOrderLineId", table: "GoodsReceiptLines", type: "uniqueidentifier", nullable: false, oldClrType: typeof(Guid), oldType: "uniqueidentifier", oldNullable: true);

            migrationBuilder.DropColumn(
                name: "SalesQuoteLineId",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "DirectIncomeGlAccountId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "DiscountGlAccountId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "DiscountPercentage",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "GLBatchId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "InvoiceBatchId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "IsDirectInvoice",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "PostedDate",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TaxGLAccountId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TaxId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TotalAmount",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TotalAmountForeign",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TransactionDateTime",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "WithholdingPercentage",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "WithholdingSalesGlAccountId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "WithholdingTaxId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "WithholdingTaxName",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "QuantityInvoiced",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "SalesOrderLineId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ShipmentLineId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TaxGLAccountId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TaxId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "UomConversionFactor",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "UomId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "UomName",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "PurchaseRequestLineId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "GoodsReceipts");

            migrationBuilder.RenameColumn(
                name: "PurchaseInvoiceId",
                table: "VendorPayments",
                newName: "VendorBillId");

            migrationBuilder.RenameIndex(
                name: "IX_VendorPayments_PurchaseInvoiceId",
                table: "VendorPayments",
                newName: "IX_VendorPayments_VendorBillId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderId",
                table: "SalesShipments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ReceivablesGlAccountId",
                table: "SalesInvoices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RevenueGlAccountId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ItemId",
                table: "SalesInvoiceLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "SalesInvoiceLines",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseOrderId",
                table: "GoodsReceipts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "VendorBills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomTransactionTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VendorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountsPayableGlId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ExternalInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GLBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDirectBill = table.Column<bool>(type: "bit", nullable: false),
                    IsPosted = table.Column<bool>(type: "bit", nullable: false),
                    MatchStatus = table.Column<int>(type: "int", nullable: false),
                    MatchVarianceReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OverrideAccountsPayableGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverrideExpenseGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TaxGLAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalAmountForeign = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingAmountForeign = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithholdingPercentage = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingTaxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithholdingTaxName = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorBills_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VendorBills_CustomTransactionTypes_CustomTransactionTypeId",
                        column: x => x.CustomTransactionTypeId,
                        principalTable: "CustomTransactionTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VendorBills_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VendorBills_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorBills_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorBillLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VendorBillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpenseGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityBilled = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitCostBilled = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UomConversionFactor = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UomName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorBillLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorBillLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VendorBillLines_VendorBills_VendorBillId",
                        column: x => x.VendorBillId,
                        principalTable: "VendorBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO [VendorBills]
                    ([Id],[CurrencyId],[CustomTransactionTypeId],[ProjectId],[PurchaseOrderId],[VendorId],[AccountsPayableGlId],[BillDate],
                     [CompanyId],[Description],[ExchangeRate],[ExternalInvoiceNumber],[GLBatchId],[IsDirectBill],[IsPosted],[MatchStatus],
                     [MatchVarianceReason],[OverrideAccountsPayableGlAccountId],[OverrideExpenseGlAccountId],[PostedDate],[TaxGLAccountId],[TaxId],
                     [TotalAmount],[TotalAmountForeign],[WithholdingAmount],[WithholdingAmountForeign],[WithholdingGlAccountId],[WithholdingPercentage],
                     [WithholdingTaxId],[WithholdingTaxName])
                SELECT [Id],[CurrencyId],[CustomTransactionTypeId],[ProjectId],[PurchaseOrderId],[VendorId],[AccountsPayableGlId],[BillDate],
                       [CompanyId],[Description],CASE WHEN [ExchangeRate] > 0 THEN [ExchangeRate] ELSE 1 END,
                       COALESCE(NULLIF([SupplierInvoiceNumber],N''),NULLIF([ExternalInvoiceNumber],N''),[PurchaseInvoiceNumber]),[GLBatchId],
                       [IsDirectBill],[IsPosted],[MatchStatus],[MatchVarianceReason],[OverrideAccountsPayableGlAccountId],[OverrideExpenseGlAccountId],
                       [PostedDate],[TaxGLAccountId],[TaxId],[TotalAmount],[TotalAmountForeign],[WithholdingAmount],[WithholdingAmountForeign],
                       [WithholdingGlAccountId],[WithholdingPercentage],[WithholdingTaxId],[WithholdingTaxName]
                FROM [__RollbackPurchaseInvoices];

                INSERT INTO [VendorBillLines]
                    ([Id],[ItemId],[VendorBillId],[Description],[ExpenseGlAccountId],[QuantityBilled],[UnitCostBilled],[UomConversionFactor],[UomId],[UomName])
                SELECT [Id],[ItemId],[PurchaseInvoiceId],[Description],[ExpenseGlAccountId],[QuantityBilled],[UnitCostBilled],[UomConversionFactor],[UomId],[UomName]
                FROM [__RollbackPurchaseInvoiceLines];

                DROP TABLE [__RollbackPurchaseInvoiceLines];
                DROP TABLE [__RollbackPurchaseInvoices];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_ItemId",
                table: "VendorBillLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_VendorBillId",
                table: "VendorBillLines",
                column: "VendorBillId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_CurrencyId",
                table: "VendorBills",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_CustomTransactionTypeId",
                table: "VendorBills",
                column: "CustomTransactionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_ProjectId",
                table: "VendorBills",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_PurchaseOrderId",
                table: "VendorBills",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_VendorId",
                table: "VendorBills",
                column: "VendorId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_SalesInvoices_SalesInvoiceId",
                table: "SalesInvoiceLines",
                column: "SalesInvoiceId",
                principalTable: "SalesInvoices",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesShipments_SalesOrders_SalesOrderId",
                table: "SalesShipments",
                column: "SalesOrderId",
                principalTable: "SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorPayments_VendorBills_VendorBillId",
                table: "VendorPayments",
                column: "VendorBillId",
                principalTable: "VendorBills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
