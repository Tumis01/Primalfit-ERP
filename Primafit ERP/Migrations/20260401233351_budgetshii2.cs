using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class budgetshii2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BudgetTransferLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetHeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LimitAmount = table.Column<decimal>(type: "decimal(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetTransferLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetTransferLine_BudgetHeaders_BudgetHeaderId",
                        column: x => x.BudgetHeaderId,
                        principalTable: "BudgetHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BudgetTransferPeriodAllocation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetTransferLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetTransferPeriodAllocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetTransferPeriodAllocation_BudgetTransferLine_BudgetTransferLineId",
                        column: x => x.BudgetTransferLineId,
                        principalTable: "BudgetTransferLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransferLine_BudgetHeaderId",
                table: "BudgetTransferLine",
                column: "BudgetHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransferPeriodAllocation_BudgetTransferLineId",
                table: "BudgetTransferPeriodAllocation",
                column: "BudgetTransferLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetTransferPeriodAllocation");

            migrationBuilder.DropTable(
                name: "BudgetTransferLine");
        }
    }
}
