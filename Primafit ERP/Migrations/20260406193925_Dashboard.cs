using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class Dashboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialDashboardSnapshots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialDashboardSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashOnHand = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentRatio = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    DaysSalesOutstanding = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    GrossProfitMargin = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    InventoryTurnover = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    LastRefresh = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LowStockItemsCount = table.Column<int>(type: "int", nullable: false),
                    NetProfitMTD = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    NetProfitMargin = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    OverdueInvoicesCount = table.Column<int>(type: "int", nullable: false),
                    QuickRatio = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    RevenueMTD = table.Column<decimal>(type: "decimal(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialDashboardSnapshots", x => x.Id);
                });
        }
    }
}
