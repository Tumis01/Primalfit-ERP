using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class cashbook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ForeignCredit",
                table: "CashbookEntries",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ForeignDebit",
                table: "CashbookEntries",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                table: "CashbookBatches",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "CashbookBatches",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsForeignCurrency",
                table: "CashbookBatches",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ForeignCredit",
                table: "CashbookEntries");

            migrationBuilder.DropColumn(
                name: "ForeignDebit",
                table: "CashbookEntries");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "CashbookBatches");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "CashbookBatches");

            migrationBuilder.DropColumn(
                name: "IsForeignCurrency",
                table: "CashbookBatches");
        }
    }
}
