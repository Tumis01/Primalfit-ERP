using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class receiptrefund : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create the Header Table: ReceiptRefunds
            migrationBuilder.CreateTable(
                name: "ReceiptRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefundNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RefundType = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DestinationWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GlBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptRefunds", x => x.Id);

                    // FIXED: Cascade actions explicitly set to Restrict to bypass multiple cascade path rules
                    table.ForeignKey(
                        name: "FK_ReceiptRefunds_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptRefunds_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // 2. Create the Child Lines Table: ReceiptRefundLines
            migrationBuilder.CreateTable(
                name: "ReceiptRefundLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptRefundLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptRefundLines_ReceiptRefunds_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "ReceiptRefunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 3. Generate Search Performance Indexes
            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRefundLines_HeaderId",
                table: "ReceiptRefundLines",
                column: "HeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRefundDrafts_CustomerId",
                table: "ReceiptRefunds",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRefundDrafts_SalesOrderId",
                table: "ReceiptRefunds",
                column: "SalesOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop in reverse sequence to preserve table dependencies
            migrationBuilder.DropTable(
                name: "ReceiptRefundLines");

            migrationBuilder.DropTable(
                name: "ReceiptRefunds");
        }
    }
}