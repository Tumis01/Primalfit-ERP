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
            migrationBuilder.CreateTable(
                name: "CashbookBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    TotalDebits = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    TotalCredits = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PostedGLBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashbookBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashbookEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashbookBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    OffsetAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashbookEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashbookEntries_CashbookBatches_CashbookBatchId",
                        column: x => x.CashbookBatchId,
                        principalTable: "CashbookBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashbookEntries_CashbookBatchId",
                table: "CashbookEntries",
                column: "CashbookBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashbookEntries");

            migrationBuilder.DropTable(
                name: "CashbookBatches");
        }
    }
}
