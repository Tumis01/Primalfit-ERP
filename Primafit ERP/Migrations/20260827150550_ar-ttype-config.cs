using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class arttypeconfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "SalesShipments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideCogsGlAccountId",
                table: "SalesShipments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideInventoryAssetGlAccountId",
                table: "SalesShipments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "ReceiptRefunds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideCogsGlAccountId",
                table: "ReceiptRefunds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideInventoryAssetGlAccountId",
                table: "ReceiptRefunds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideReceivablesGlAccountId",
                table: "ReceiptRefunds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesShipments_CustomTransactionTypeId",
                table: "SalesShipments",
                column: "CustomTransactionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRefunds_CustomTransactionTypeId",
                table: "ReceiptRefunds",
                column: "CustomTransactionTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiptRefunds_CustomTransactionTypes_CustomTransactionTypeId",
                table: "ReceiptRefunds",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesShipments_CustomTransactionTypes_CustomTransactionTypeId",
                table: "SalesShipments",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReceiptRefunds_CustomTransactionTypes_CustomTransactionTypeId",
                table: "ReceiptRefunds");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesShipments_CustomTransactionTypes_CustomTransactionTypeId",
                table: "SalesShipments");

            migrationBuilder.DropIndex(
                name: "IX_SalesShipments_CustomTransactionTypeId",
                table: "SalesShipments");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptRefunds_CustomTransactionTypeId",
                table: "ReceiptRefunds");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "SalesShipments");

            migrationBuilder.DropColumn(
                name: "OverrideCogsGlAccountId",
                table: "SalesShipments");

            migrationBuilder.DropColumn(
                name: "OverrideInventoryAssetGlAccountId",
                table: "SalesShipments");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "ReceiptRefunds");

            migrationBuilder.DropColumn(
                name: "OverrideCogsGlAccountId",
                table: "ReceiptRefunds");

            migrationBuilder.DropColumn(
                name: "OverrideInventoryAssetGlAccountId",
                table: "ReceiptRefunds");

            migrationBuilder.DropColumn(
                name: "OverrideReceivablesGlAccountId",
                table: "ReceiptRefunds");
        }
    }
}
