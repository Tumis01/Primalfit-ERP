using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class first : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_BusinessPartner_CustomerId",
                table: "SalesOrders");

            migrationBuilder.DropTable(
                name: "BusinessPartner");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_Customers_CustomerId",
                table: "SalesOrders",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Customers_CustomerId",
                table: "SalesOrders");

            migrationBuilder.CreateTable(
                name: "BusinessPartner",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayablesAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivablesAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsCustomer = table.Column<bool>(type: "bit", nullable: false),
                    IsVendor = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TaxId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessPartner", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessPartner_GLChartOfAccounts_PayablesAccountId",
                        column: x => x.PayablesAccountId,
                        principalTable: "GLChartOfAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BusinessPartner_GLChartOfAccounts_ReceivablesAccountId",
                        column: x => x.ReceivablesAccountId,
                        principalTable: "GLChartOfAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartner_PayablesAccountId",
                table: "BusinessPartner",
                column: "PayablesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartner_ReceivablesAccountId",
                table: "BusinessPartner",
                column: "ReceivablesAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_BusinessPartner_CustomerId",
                table: "SalesOrders",
                column: "CustomerId",
                principalTable: "BusinessPartner",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
