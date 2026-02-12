using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class payment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                table: "CustomerPayments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "DiscountGlAccountId",
                table: "CustomerPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FxGainLossGlAccountId",
                table: "CustomerPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "CustomerPayments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "CustomerPayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "UnappliedCashGlAccountId",
                table: "CustomerPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentApplications_CustomerPaymentId",
                table: "PaymentApplications",
                column: "CustomerPaymentId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentApplications_CustomerPayments_CustomerPaymentId",
                table: "PaymentApplications",
                column: "CustomerPaymentId",
                principalTable: "CustomerPayments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentApplications_CustomerPayments_CustomerPaymentId",
                table: "PaymentApplications");

            migrationBuilder.DropIndex(
                name: "IX_PaymentApplications_CustomerPaymentId",
                table: "PaymentApplications");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "DiscountGlAccountId",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "FxGainLossGlAccountId",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "UnappliedCashGlAccountId",
                table: "CustomerPayments");
        }
    }
}
