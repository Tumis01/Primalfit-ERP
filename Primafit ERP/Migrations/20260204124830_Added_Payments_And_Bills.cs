using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class Added_Payments_And_Bills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccountingPeriods_CompanyDetails_CompanyId",
                table: "AccountingPeriods");

            migrationBuilder.DropForeignKey(
                name: "FK_GLAccountTypes_CompanyDetails_CompanyId",
                table: "GLAccountTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_GLBatches_AccountingPeriods_AccountingPeriodId",
                table: "GLBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_GLBatches_CompanyDetails_CompanyId",
                table: "GLBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_GLChartOfAccounts_CompanyDetails_CompanyId",
                table: "GLChartOfAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_GLChartOfAccounts_GLMainAccounts_MainAccountId",
                table: "GLChartOfAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_GLMainAccounts_CompanyDetails_CompanyId",
                table: "GLMainAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_GLMainAccounts_GLAccountTypes_AccountTypeId",
                table: "GLMainAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_Items_ItemId",
                table: "SalesOrderLines");

            migrationBuilder.AlterColumn<decimal>(
                name: "Per",
                table: "Taxes",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ValueAtShipment",
                table: "StockTransfers",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "StockTransfers",
                type: "decimal(18,6)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "StockTransfers",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityChanged",
                table: "StockLedgers",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "CostAtTime",
                table: "StockLedgers",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<string>(
                name: "OrderNumber",
                table: "SalesOrders",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "SalesOrderLines",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "SalesOrderLines",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ProjectBudget",
                table: "Projects",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "WeightedAverageCost",
                table: "Items",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "SellingPrice",
                table: "Items",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<string>(
                name: "SKU",
                table: "Items",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ReorderLevel",
                table: "Items",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Debit",
                table: "GLTransactions",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Credit",
                table: "GLTransactions",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Debit",
                table: "GLJournalLines",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Credit",
                table: "GLJournalLines",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "BankStatementLines",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "StatementEndingBalance",
                table: "BankReconciliations",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.CreateTable(
                name: "CustomerPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyDetailsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    TotalAmountReceived = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerPayments_BusinessPartners_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "BusinessPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendorBills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyDetailsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BillDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorBills_BusinessPartners_VendorId",
                        column: x => x.VendorId,
                        principalTable: "BusinessPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    CashDiscountTaken = table.Column<decimal>(type: "decimal(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentApplications_CustomerPayments_CustomerPaymentId",
                        column: x => x.CustomerPaymentId,
                        principalTable: "CustomerPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendorBillLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorBillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpenseGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorBillLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorBillLines_VendorBills_VendorBillId",
                        column: x => x.VendorBillId,
                        principalTable: "VendorBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CompanyId_OrderNumber",
                table: "SalesOrders",
                columns: new[] { "CompanyId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_SKU",
                table: "Items",
                columns: new[] { "CompanyId", "SKU" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_CustomerId",
                table: "CustomerPayments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentApplications_CustomerPaymentId",
                table: "PaymentApplications",
                column: "CustomerPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_VendorBillId",
                table: "VendorBillLines",
                column: "VendorBillId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_VendorId",
                table: "VendorBills",
                column: "VendorId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountingPeriods_CompanyDetails_CompanyId",
                table: "AccountingPeriods",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GLAccountTypes_CompanyDetails_CompanyId",
                table: "GLAccountTypes",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GLBatches_AccountingPeriods_AccountingPeriodId",
                table: "GLBatches",
                column: "AccountingPeriodId",
                principalTable: "AccountingPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GLBatches_CompanyDetails_CompanyId",
                table: "GLBatches",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GLChartOfAccounts_CompanyDetails_CompanyId",
                table: "GLChartOfAccounts",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GLChartOfAccounts_GLMainAccounts_MainAccountId",
                table: "GLChartOfAccounts",
                column: "MainAccountId",
                principalTable: "GLMainAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GLMainAccounts_CompanyDetails_CompanyId",
                table: "GLMainAccounts",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GLMainAccounts_GLAccountTypes_AccountTypeId",
                table: "GLMainAccounts",
                column: "AccountTypeId",
                principalTable: "GLAccountTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_Items_ItemId",
                table: "SalesOrderLines",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccountingPeriods_CompanyDetails_CompanyId",
                table: "AccountingPeriods");

            migrationBuilder.DropForeignKey(
                name: "FK_GLAccountTypes_CompanyDetails_CompanyId",
                table: "GLAccountTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_GLBatches_AccountingPeriods_AccountingPeriodId",
                table: "GLBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_GLBatches_CompanyDetails_CompanyId",
                table: "GLBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_GLChartOfAccounts_CompanyDetails_CompanyId",
                table: "GLChartOfAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_GLChartOfAccounts_GLMainAccounts_MainAccountId",
                table: "GLChartOfAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_GLMainAccounts_CompanyDetails_CompanyId",
                table: "GLMainAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_GLMainAccounts_GLAccountTypes_AccountTypeId",
                table: "GLMainAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_Items_ItemId",
                table: "SalesOrderLines");

            migrationBuilder.DropTable(
                name: "PaymentApplications");

            migrationBuilder.DropTable(
                name: "VendorBillLines");

            migrationBuilder.DropTable(
                name: "CustomerPayments");

            migrationBuilder.DropTable(
                name: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_CompanyId_OrderNumber",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_Items_CompanyId_SKU",
                table: "Items");

            migrationBuilder.AlterColumn<decimal>(
                name: "Per",
                table: "Taxes",
                type: "decimal(9,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ValueAtShipment",
                table: "StockTransfers",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "StockTransfers",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "StockTransfers",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityChanged",
                table: "StockLedgers",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "CostAtTime",
                table: "StockLedgers",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<string>(
                name: "OrderNumber",
                table: "SalesOrders",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "SalesOrderLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "SalesOrderLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ProjectBudget",
                table: "Projects",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "WeightedAverageCost",
                table: "Items",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "SellingPrice",
                table: "Items",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<string>(
                name: "SKU",
                table: "Items",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ReorderLevel",
                table: "Items",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Debit",
                table: "GLTransactions",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Credit",
                table: "GLTransactions",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Debit",
                table: "GLJournalLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Credit",
                table: "GLJournalLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "BankStatementLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "StatementEndingBalance",
                table: "BankReconciliations",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountingPeriods_CompanyDetails_CompanyId",
                table: "AccountingPeriods",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId");

            migrationBuilder.AddForeignKey(
                name: "FK_GLAccountTypes_CompanyDetails_CompanyId",
                table: "GLAccountTypes",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId");

            migrationBuilder.AddForeignKey(
                name: "FK_GLBatches_AccountingPeriods_AccountingPeriodId",
                table: "GLBatches",
                column: "AccountingPeriodId",
                principalTable: "AccountingPeriods",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GLBatches_CompanyDetails_CompanyId",
                table: "GLBatches",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId");

            migrationBuilder.AddForeignKey(
                name: "FK_GLChartOfAccounts_CompanyDetails_CompanyId",
                table: "GLChartOfAccounts",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId");

            migrationBuilder.AddForeignKey(
                name: "FK_GLChartOfAccounts_GLMainAccounts_MainAccountId",
                table: "GLChartOfAccounts",
                column: "MainAccountId",
                principalTable: "GLMainAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GLMainAccounts_CompanyDetails_CompanyId",
                table: "GLMainAccounts",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId");

            migrationBuilder.AddForeignKey(
                name: "FK_GLMainAccounts_GLAccountTypes_AccountTypeId",
                table: "GLMainAccounts",
                column: "AccountTypeId",
                principalTable: "GLAccountTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_Items_ItemId",
                table: "SalesOrderLines",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
