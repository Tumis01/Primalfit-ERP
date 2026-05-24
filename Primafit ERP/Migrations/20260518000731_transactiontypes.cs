using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class transactiontypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransactionGlMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionType = table.Column<int>(type: "int", nullable: false),
                    OverrideDebitGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverrideCreditGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionGlMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionGlMappings_SegChartOfAccounts_OverrideCreditGlAccountId",
                        column: x => x.OverrideCreditGlAccountId,
                        principalTable: "SegChartOfAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TransactionGlMappings_SegChartOfAccounts_OverrideDebitGlAccountId",
                        column: x => x.OverrideDebitGlAccountId,
                        principalTable: "SegChartOfAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionGlMappings_OverrideCreditGlAccountId",
                table: "TransactionGlMappings",
                column: "OverrideCreditGlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionGlMappings_OverrideDebitGlAccountId",
                table: "TransactionGlMappings",
                column: "OverrideDebitGlAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionGlMappings");
        }
    }
}
