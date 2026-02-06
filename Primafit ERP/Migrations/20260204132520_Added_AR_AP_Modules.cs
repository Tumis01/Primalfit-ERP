using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class Added_AR_AP_Modules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "BankAccountId",
                table: "CustomerPayments",
                newName: "BankGlAccountId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ItemId",
                table: "VendorBillLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "ExpenseGlAccountId",
                table: "VendorBillLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedAt",
                table: "CustomerPayments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostedByUserId",
                table: "CustomerPayments",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PostedAt",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "PostedByUserId",
                table: "CustomerPayments");

            migrationBuilder.RenameColumn(
                name: "BankGlAccountId",
                table: "CustomerPayments",
                newName: "BankAccountId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ItemId",
                table: "VendorBillLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ExpenseGlAccountId",
                table: "VendorBillLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");
        }
    }
}
