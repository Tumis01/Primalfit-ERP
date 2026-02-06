using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class Added_AR_AP_Modules1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "VendorBillLines");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_ExpenseGlAccountId",
                table: "VendorBillLines",
                column: "ExpenseGlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_BankGlAccountId",
                table: "CustomerPayments",
                column: "BankGlAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerPayments_GLChartOfAccounts_BankGlAccountId",
                table: "CustomerPayments",
                column: "BankGlAccountId",
                principalTable: "GLChartOfAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBillLines_GLChartOfAccounts_ExpenseGlAccountId",
                table: "VendorBillLines",
                column: "ExpenseGlAccountId",
                principalTable: "GLChartOfAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerPayments_GLChartOfAccounts_BankGlAccountId",
                table: "CustomerPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBillLines_GLChartOfAccounts_ExpenseGlAccountId",
                table: "VendorBillLines");

            migrationBuilder.DropIndex(
                name: "IX_VendorBillLines_ExpenseGlAccountId",
                table: "VendorBillLines");

            migrationBuilder.DropIndex(
                name: "IX_CustomerPayments_BankGlAccountId",
                table: "CustomerPayments");

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseOrderId",
                table: "VendorBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ItemId",
                table: "VendorBillLines",
                type: "uniqueidentifier",
                nullable: true);
        }
    }
}
