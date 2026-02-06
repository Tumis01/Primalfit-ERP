using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class tax_updat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxId",
                table: "SalesOrderLines");

            migrationBuilder.AddColumn<Guid>(
                name: "TaxId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxId",
                table: "SalesOrders");

            migrationBuilder.AddColumn<Guid>(
                name: "TaxId",
                table: "SalesOrderLines",
                type: "uniqueidentifier",
                nullable: true);
        }
    }
}
