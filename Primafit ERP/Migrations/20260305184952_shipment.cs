using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class shipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "QtyInvoiced",
                table: "SalesOrderLines",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QtyShipped",
                table: "SalesOrderLines",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SalesShipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShippedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ShipmentBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShipmentNumber = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesShipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesShipments_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesShipmentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyOrdered = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    QtyShipped = table.Column<decimal>(type: "decimal(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesShipmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesShipmentLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesShipmentLines_SalesShipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "SalesShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesShipmentLines_ItemId",
                table: "SalesShipmentLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesShipmentLines_ShipmentId",
                table: "SalesShipmentLines",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesShipments_SalesOrderId",
                table: "SalesShipments",
                column: "SalesOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesShipmentLines");

            migrationBuilder.DropTable(
                name: "SalesShipments");

            migrationBuilder.DropColumn(
                name: "QtyInvoiced",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "QtyShipped",
                table: "SalesOrderLines");
        }
    }
}
