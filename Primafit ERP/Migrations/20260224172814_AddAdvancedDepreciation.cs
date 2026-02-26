using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvancedDepreciation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssetCategoryId",
                table: "FixedAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DecliningFactor",
                table: "FixedAssets",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DecliningRate",
                table: "FixedAssets",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepreciationMethod",
                table: "FixedAssets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedTotalUnits",
                table: "FixedAssets",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetailsJson",
                table: "AssetDepreciationHistories",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MethodUsed",
                table: "AssetDepreciationHistories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitsUsed",
                table: "AssetDepreciationHistories",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultDepreciationMethod = table.Column<int>(type: "int", nullable: false),
                    FixedAssetAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccumulatedDepreciationAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepreciationExpenseAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecliningRate = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    DecliningFactor = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    EstimatedTotalUnits = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    UnitOfMeasure = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetUsageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FixedAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UnitsUsed = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetUsageLogs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetCategories");

            migrationBuilder.DropTable(
                name: "AssetUsageLogs");

            migrationBuilder.DropColumn(
                name: "AssetCategoryId",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "DecliningFactor",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "DecliningRate",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "DepreciationMethod",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "EstimatedTotalUnits",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "DetailsJson",
                table: "AssetDepreciationHistories");

            migrationBuilder.DropColumn(
                name: "MethodUsed",
                table: "AssetDepreciationHistories");

            migrationBuilder.DropColumn(
                name: "UnitsUsed",
                table: "AssetDepreciationHistories");
        }
    }
}
