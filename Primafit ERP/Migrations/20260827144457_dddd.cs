using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class dddd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "CustomerPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideReceivablesGlAccountId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideRevenueGlAccountId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_CustomTransactionTypeId",
                table: "CustomerPayments",
                column: "CustomTransactionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CustomTransactionTypeId",
                table: "CreditNotes",
                column: "CustomTransactionTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_CustomTransactionTypes_CustomTransactionTypeId",
                table: "CreditNotes",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerPayments_CustomTransactionTypes_CustomTransactionTypeId",
                table: "CustomerPayments",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_CustomTransactionTypes_CustomTransactionTypeId",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerPayments_CustomTransactionTypes_CustomTransactionTypeId",
                table: "CustomerPayments");

            migrationBuilder.DropIndex(
                name: "IX_CustomerPayments_CustomTransactionTypeId",
                table: "CustomerPayments");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_CustomTransactionTypeId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "OverrideReceivablesGlAccountId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "OverrideRevenueGlAccountId",
                table: "CreditNotes");
        }
    }
}
