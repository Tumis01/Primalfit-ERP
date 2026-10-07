using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesWithholdingAndWarehouseTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SalesGlAccountId",
                table: "WithholdingTaxes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Warehouses",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingPercentage",
                table: "SalesOrders",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "WithholdingSalesGlAccountId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WithholdingTaxId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithholdingTaxName",
                table: "SalesOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingAmountForeign",
                table: "PaymentApplications",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SalesGlAccountId",
                table: "WithholdingTaxes");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "WithholdingPercentage",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "WithholdingSalesGlAccountId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "WithholdingTaxId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "WithholdingTaxName",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "WithholdingAmountForeign",
                table: "PaymentApplications");
        }
    }
}
