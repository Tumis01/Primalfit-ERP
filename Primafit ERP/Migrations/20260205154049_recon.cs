using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class recon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "AccountingPeriodId",
                table: "BankReconciliations");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "BankReconciliations");

            migrationBuilder.RenameColumn(
                name: "IsMatched",
                table: "BankStatementLines",
                newName: "IsCleared");

            migrationBuilder.RenameColumn(
                name: "Date",
                table: "BankStatementLines",
                newName: "TransactionDate");

            migrationBuilder.AddColumn<decimal>(
                name: "BookBalanceAtDate",
                table: "BankReconciliations",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookBalanceAtDate",
                table: "BankReconciliations");

            migrationBuilder.RenameColumn(
                name: "TransactionDate",
                table: "BankStatementLines",
                newName: "Date");

            migrationBuilder.RenameColumn(
                name: "IsCleared",
                table: "BankStatementLines",
                newName: "IsMatched");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "BankStatementLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "AccountingPeriodId",
                table: "BankReconciliations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "BankReconciliations",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
