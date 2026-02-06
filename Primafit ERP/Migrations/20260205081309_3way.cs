using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class _3way : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BusinessPartners_GLChartOfAccounts_PayablesAccountId",
                table: "BusinessPartners");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessPartners_GLChartOfAccounts_ReceivablesAccountId",
                table: "BusinessPartners");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerPayments_BusinessPartners_CustomerId",
                table: "CustomerPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerPayments_GLChartOfAccounts_BankGlAccountId",
                table: "CustomerPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentApplications_CustomerPayments_CustomerPaymentId",
                table: "PaymentApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_BusinessPartners_CustomerId",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBillLines_GLChartOfAccounts_ExpenseGlAccountId",
                table: "VendorBillLines");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBills_BusinessPartners_VendorId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_VendorId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_VendorBillLines_ExpenseGlAccountId",
                table: "VendorBillLines");

            migrationBuilder.DropIndex(
                name: "IX_PaymentApplications_CustomerPaymentId",
                table: "PaymentApplications");

            migrationBuilder.DropIndex(
                name: "IX_CustomerPayments_BankGlAccountId",
                table: "CustomerPayments");

            migrationBuilder.DropIndex(
                name: "IX_CustomerPayments_CustomerId",
                table: "CustomerPayments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BusinessPartners",
                table: "BusinessPartners");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "VendorBillLines");

            migrationBuilder.DropColumn(
                name: "PostedAt",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "PostedByUserId",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CustomerPayments");

            migrationBuilder.RenameTable(
                name: "BusinessPartners",
                newName: "BusinessPartner");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "VendorBills",
                newName: "MatchStatus");

            migrationBuilder.RenameColumn(
                name: "CurrencyId",
                table: "VendorBills",
                newName: "CompanyId");

            migrationBuilder.RenameColumn(
                name: "CompanyDetailsId",
                table: "VendorBills",
                newName: "AccountsPayableGlId");

            migrationBuilder.RenameColumn(
                name: "UnitCost",
                table: "VendorBillLines",
                newName: "UnitCostBilled");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "VendorBillLines",
                newName: "QuantityBilled");

            migrationBuilder.RenameColumn(
                name: "TotalAmountReceived",
                table: "CustomerPayments",
                newName: "AmountReceived");

            migrationBuilder.RenameColumn(
                name: "PaymentDate",
                table: "CustomerPayments",
                newName: "Date");

            migrationBuilder.RenameColumn(
                name: "CurrencyId",
                table: "CustomerPayments",
                newName: "DepositToGlAccountId");

            migrationBuilder.RenameColumn(
                name: "CompanyDetailsId",
                table: "CustomerPayments",
                newName: "CreditGlAccountId");

            migrationBuilder.RenameColumn(
                name: "BankGlAccountId",
                table: "CustomerPayments",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_BusinessPartners_ReceivablesAccountId",
                table: "BusinessPartner",
                newName: "IX_BusinessPartner_ReceivablesAccountId");

            migrationBuilder.RenameIndex(
                name: "IX_BusinessPartners_PayablesAccountId",
                table: "BusinessPartner",
                newName: "IX_BusinessPartner_PayablesAccountId");

            migrationBuilder.AddColumn<string>(
                name: "MatchVarianceReason",
                table: "VendorBills",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseOrderId",
                table: "VendorBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ItemId",
                table: "VendorBillLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_BusinessPartner",
                table: "BusinessPartner",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivablesAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrnNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DateReceived = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InventoryGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LinkedSalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivablesGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayablesAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vendors_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "decimal(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptLine_GoodsReceipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "GoodsReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityOrdered = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevenueGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLines_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CurrencyId",
                table: "Customers",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLine_GoodsReceiptId",
                table: "GoodsReceiptLine",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId",
                table: "PurchaseOrderLines",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_SalesInvoiceId",
                table: "SalesInvoiceLines",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_CurrencyId",
                table: "Vendors",
                column: "CurrencyId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessPartner_GLChartOfAccounts_PayablesAccountId",
                table: "BusinessPartner",
                column: "PayablesAccountId",
                principalTable: "GLChartOfAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessPartner_GLChartOfAccounts_ReceivablesAccountId",
                table: "BusinessPartner",
                column: "ReceivablesAccountId",
                principalTable: "GLChartOfAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_BusinessPartner_CustomerId",
                table: "SalesOrders",
                column: "CustomerId",
                principalTable: "BusinessPartner",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BusinessPartner_GLChartOfAccounts_PayablesAccountId",
                table: "BusinessPartner");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessPartner_GLChartOfAccounts_ReceivablesAccountId",
                table: "BusinessPartner");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_BusinessPartner_CustomerId",
                table: "SalesOrders");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "GoodsReceiptLine");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "SalesInvoiceLines");

            migrationBuilder.DropTable(
                name: "Vendors");

            migrationBuilder.DropTable(
                name: "GoodsReceipts");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "SalesInvoices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BusinessPartner",
                table: "BusinessPartner");

            migrationBuilder.DropColumn(
                name: "MatchVarianceReason",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "VendorBillLines");

            migrationBuilder.RenameTable(
                name: "BusinessPartner",
                newName: "BusinessPartners");

            migrationBuilder.RenameColumn(
                name: "MatchStatus",
                table: "VendorBills",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "VendorBills",
                newName: "CurrencyId");

            migrationBuilder.RenameColumn(
                name: "AccountsPayableGlId",
                table: "VendorBills",
                newName: "CompanyDetailsId");

            migrationBuilder.RenameColumn(
                name: "UnitCostBilled",
                table: "VendorBillLines",
                newName: "UnitCost");

            migrationBuilder.RenameColumn(
                name: "QuantityBilled",
                table: "VendorBillLines",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "DepositToGlAccountId",
                table: "CustomerPayments",
                newName: "CurrencyId");

            migrationBuilder.RenameColumn(
                name: "Date",
                table: "CustomerPayments",
                newName: "PaymentDate");

            migrationBuilder.RenameColumn(
                name: "CreditGlAccountId",
                table: "CustomerPayments",
                newName: "CompanyDetailsId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "CustomerPayments",
                newName: "BankGlAccountId");

            migrationBuilder.RenameColumn(
                name: "AmountReceived",
                table: "CustomerPayments",
                newName: "TotalAmountReceived");

            migrationBuilder.RenameIndex(
                name: "IX_BusinessPartner_ReceivablesAccountId",
                table: "BusinessPartners",
                newName: "IX_BusinessPartners_ReceivablesAccountId");

            migrationBuilder.RenameIndex(
                name: "IX_BusinessPartner_PayablesAccountId",
                table: "BusinessPartners",
                newName: "IX_BusinessPartners_PayablesAccountId");

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "VendorBills",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "VendorBills",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "VendorBillLines",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedAt",
                table: "CustomerPayments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostedByUserId",
                table: "CustomerPayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CustomerPayments",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "CustomerPayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_BusinessPartners",
                table: "BusinessPartners",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_VendorId",
                table: "VendorBills",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_ExpenseGlAccountId",
                table: "VendorBillLines",
                column: "ExpenseGlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentApplications_CustomerPaymentId",
                table: "PaymentApplications",
                column: "CustomerPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_BankGlAccountId",
                table: "CustomerPayments",
                column: "BankGlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_CustomerId",
                table: "CustomerPayments",
                column: "CustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessPartners_GLChartOfAccounts_PayablesAccountId",
                table: "BusinessPartners",
                column: "PayablesAccountId",
                principalTable: "GLChartOfAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessPartners_GLChartOfAccounts_ReceivablesAccountId",
                table: "BusinessPartners",
                column: "ReceivablesAccountId",
                principalTable: "GLChartOfAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerPayments_BusinessPartners_CustomerId",
                table: "CustomerPayments",
                column: "CustomerId",
                principalTable: "BusinessPartners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerPayments_GLChartOfAccounts_BankGlAccountId",
                table: "CustomerPayments",
                column: "BankGlAccountId",
                principalTable: "GLChartOfAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentApplications_CustomerPayments_CustomerPaymentId",
                table: "PaymentApplications",
                column: "CustomerPaymentId",
                principalTable: "CustomerPayments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_BusinessPartners_CustomerId",
                table: "SalesOrders",
                column: "CustomerId",
                principalTable: "BusinessPartners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBillLines_GLChartOfAccounts_ExpenseGlAccountId",
                table: "VendorBillLines",
                column: "ExpenseGlAccountId",
                principalTable: "GLChartOfAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_BusinessPartners_VendorId",
                table: "VendorBills",
                column: "VendorId",
                principalTable: "BusinessPartners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
