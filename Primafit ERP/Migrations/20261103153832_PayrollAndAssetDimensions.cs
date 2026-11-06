using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class PayrollAndAssetDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "PayrollItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DimensionSetId",
                table: "FixedAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollItems_DimensionSetId",
                table: "PayrollItems",
                column: "DimensionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_DimensionSetId",
                table: "FixedAssets",
                column: "DimensionSetId");

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_DimensionSets_DimensionSetId",
                table: "FixedAssets",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollItems_DimensionSets_DimensionSetId",
                table: "PayrollItems",
                column: "DimensionSetId",
                principalTable: "DimensionSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_DimensionSets_DimensionSetId",
                table: "FixedAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollItems_DimensionSets_DimensionSetId",
                table: "PayrollItems");

            migrationBuilder.DropIndex(
                name: "IX_PayrollItems_DimensionSetId",
                table: "PayrollItems");

            migrationBuilder.DropIndex(
                name: "IX_FixedAssets_DimensionSetId",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "DimensionSetId",
                table: "FixedAssets");
        }
    }
}
