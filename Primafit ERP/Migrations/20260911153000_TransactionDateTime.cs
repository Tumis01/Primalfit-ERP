using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    public partial class TransactionDateTime : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TransactionDateTime",
                table: "SalesOrders",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "TransactionDateTime",
                table: "CreditNotes",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "TransactionDateTime",
                table: "DebitNotes",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "TransactionDateTime",
                table: "ReceiptRefunds",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "TransactionDateTime",
                table: "VendorReturns",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TransactionDateTime", table: "SalesOrders");
            migrationBuilder.DropColumn(name: "TransactionDateTime", table: "CreditNotes");
            migrationBuilder.DropColumn(name: "TransactionDateTime", table: "DebitNotes");
            migrationBuilder.DropColumn(name: "TransactionDateTime", table: "ReceiptRefunds");
            migrationBuilder.DropColumn(name: "TransactionDateTime", table: "VendorReturns");
        }
    }
}
