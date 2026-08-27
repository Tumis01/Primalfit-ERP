using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class configuringsalesmodule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivablesGlAccountId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CustomTransactionTypeId",
                table: "SalesOrders",
                column: "CustomTransactionTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_CustomTransactionTypes_CustomTransactionTypeId",
                table: "SalesOrders",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_CustomTransactionTypes_CustomTransactionTypeId",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_CustomTransactionTypeId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "ReceivablesGlAccountId",
                table: "SalesOrders");
        }
    }
}
