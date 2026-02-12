using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    public partial class AddCreditNotes_Fresh : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. SAFETY: Drop tables if they exist (Clean Slate)
            migrationBuilder.Sql("IF OBJECT_ID('dbo.CreditNoteLines', 'U') IS NOT NULL DROP TABLE dbo.CreditNoteLines");
            migrationBuilder.Sql("IF OBJECT_ID('dbo.CreditNotes', 'U') IS NOT NULL DROP TABLE dbo.CreditNotes");

            // 2. Create Header Table
            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReturnToStock = table.Column<bool>(type: "bit", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GlBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);

                    // Foreign Keys with NO ACTION to prevent cycles
                    table.ForeignKey(
                        name: "FK_CreditNotes_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);

                    table.ForeignKey(
                        name: "FK_CreditNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction); // <--- FIXED

                    table.ForeignKey(
                        name: "FK_CreditNotes_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction); // <--- FIXED

                    table.ForeignKey(
                        name: "FK_CreditNotes_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction); // <--- FIXED
                });

            // 3. Create Lines Table
            migrationBuilder.CreateTable(
                name: "CreditNoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNoteLines", x => x.Id);

                    // Cascade delete lines if header is deleted
                    table.ForeignKey(
                        name: "FK_CreditNoteLines_CreditNotes_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);

                    table.ForeignKey(
                        name: "FK_CreditNoteLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // 4. Create Indexes
            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_HeaderId",
                table: "CreditNoteLines",
                column: "HeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_ItemId",
                table: "CreditNoteLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CompanyId_CreditNoteNumber",
                table: "CreditNotes",
                columns: new[] { "CompanyId", "CreditNoteNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CurrencyId",
                table: "CreditNotes",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CustomerId",
                table: "CreditNotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_SalesInvoiceId",
                table: "CreditNotes",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_WarehouseId",
                table: "CreditNotes",
                column: "WarehouseId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CreditNoteLines");
            migrationBuilder.DropTable(name: "CreditNotes");
        }
    }
}