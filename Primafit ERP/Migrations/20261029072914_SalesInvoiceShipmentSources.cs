using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class SalesInvoiceShipmentSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesShipmentLines_ShipmentId_SalesOrderLineId",
                table: "SalesShipmentLines");

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderLineId",
                table: "SalesShipmentLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceLineId",
                table: "SalesShipmentLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesShipmentLines_SalesInvoiceLineId",
                table: "SalesShipmentLines",
                column: "SalesInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesShipmentLines_ShipmentId_SalesInvoiceLineId",
                table: "SalesShipmentLines",
                columns: new[] { "ShipmentId", "SalesInvoiceLineId" },
                unique: true,
                filter: "[SalesInvoiceLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesShipmentLines_ShipmentId_SalesOrderLineId",
                table: "SalesShipmentLines",
                columns: new[] { "ShipmentId", "SalesOrderLineId" },
                unique: true,
                filter: "[SalesOrderLineId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesShipmentLines_SalesInvoiceLines_SalesInvoiceLineId",
                table: "SalesShipmentLines",
                column: "SalesInvoiceLineId",
                principalTable: "SalesInvoiceLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [SalesShipmentLines] WHERE [SalesInvoiceLineId] IS NOT NULL) THROW 51000, 'Cannot downgrade while invoice-sourced shipment lines exist.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_SalesShipmentLines_SalesInvoiceLines_SalesInvoiceLineId",
                table: "SalesShipmentLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesShipmentLines_SalesInvoiceLineId",
                table: "SalesShipmentLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesShipmentLines_ShipmentId_SalesInvoiceLineId",
                table: "SalesShipmentLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesShipmentLines_ShipmentId_SalesOrderLineId",
                table: "SalesShipmentLines");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceLineId",
                table: "SalesShipmentLines");

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesOrderLineId",
                table: "SalesShipmentLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesShipmentLines_ShipmentId_SalesOrderLineId",
                table: "SalesShipmentLines",
                columns: new[] { "ShipmentId", "SalesOrderLineId" },
                unique: true);
        }
    }
}
