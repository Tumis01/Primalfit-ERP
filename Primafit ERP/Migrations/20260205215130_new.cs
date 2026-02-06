using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class @new : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "Projects");

            migrationBuilder.RenameColumn(
                name: "ProjectBudget",
                table: "Projects",
                newName: "BudgetedRevenue");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Projects",
                type: "int",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Projects",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<decimal>(
                name: "BudgetedCost",
                table: "Projects",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Projects",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndDate",
                table: "Projects",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectCode",
                table: "Projects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDate",
                table: "Projects",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "GLTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "GLJournalLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "CashbookEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetDepreciationHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FixedAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    GlBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetDepreciationHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BudgetHeaders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetHeaders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FixedAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssetTag = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PurchaseCost = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    SalvageValue = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    UsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    CurrentBookValue = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    PurchaseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DepreciationStartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastDepreciationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FixedAssetAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccumulatedDepreciationAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepreciationExpenseAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemCostHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateChanged = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OldQty = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    OldWacc = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    NewQtyIn = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    NewCostIn = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    ResultingWacc = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCostHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemCostHistories_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WaccHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateChanged = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldQty = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    OldWacc = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    IncomingQty = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    IncomingCost = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    NewWacc = table.Column<decimal>(type: "decimal(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaccHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BudgetLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetHeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LimitAmount = table.Column<decimal>(type: "decimal(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetLines_BudgetHeaders_BudgetHeaderId",
                        column: x => x.BudgetHeaderId,
                        principalTable: "BudgetHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GLTransactions_ProjectId",
                table: "GLTransactions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_GLJournalLines_ProjectId",
                table: "GLJournalLines",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_BudgetHeaderId",
                table: "BudgetLines",
                column: "BudgetHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostHistories_ItemId",
                table: "ItemCostHistories",
                column: "ItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_GLJournalLines_Projects_ProjectId",
                table: "GLJournalLines",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GLTransactions_Projects_ProjectId",
                table: "GLTransactions",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GLJournalLines_Projects_ProjectId",
                table: "GLJournalLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GLTransactions_Projects_ProjectId",
                table: "GLTransactions");

            migrationBuilder.DropTable(
                name: "AssetDepreciationHistories");

            migrationBuilder.DropTable(
                name: "BudgetLines");

            migrationBuilder.DropTable(
                name: "FixedAssets");

            migrationBuilder.DropTable(
                name: "ItemCostHistories");

            migrationBuilder.DropTable(
                name: "WaccHistories");

            migrationBuilder.DropTable(
                name: "BudgetHeaders");

            migrationBuilder.DropIndex(
                name: "IX_GLTransactions_ProjectId",
                table: "GLTransactions");

            migrationBuilder.DropIndex(
                name: "IX_GLJournalLines_ProjectId",
                table: "GLJournalLines");

            migrationBuilder.DropColumn(
                name: "BudgetedCost",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProjectCode",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "GLTransactions");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "GLJournalLines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "CashbookEntries");

            migrationBuilder.RenameColumn(
                name: "BudgetedRevenue",
                table: "Projects",
                newName: "ProjectBudget");

            migrationBuilder.AlterColumn<bool>(
                name: "Status",
                table: "Projects",
                type: "bit",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Projects",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Projects",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }
    }
}
