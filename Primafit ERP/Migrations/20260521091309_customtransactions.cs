using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class customtransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomTransactionTypeId",
                table: "TransactionGlMappings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomTransactionTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomTransactionTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionGlMappings_CustomTransactionTypeId",
                table: "TransactionGlMappings",
                column: "CustomTransactionTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_TransactionGlMappings_CustomTransactionTypes_CustomTransactionTypeId",
                table: "TransactionGlMappings",
                column: "CustomTransactionTypeId",
                principalTable: "CustomTransactionTypes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransactionGlMappings_CustomTransactionTypes_CustomTransactionTypeId",
                table: "TransactionGlMappings");

            migrationBuilder.DropTable(
                name: "CustomTransactionTypes");

            migrationBuilder.DropIndex(
                name: "IX_TransactionGlMappings_CustomTransactionTypeId",
                table: "TransactionGlMappings");

            migrationBuilder.DropColumn(
                name: "CustomTransactionTypeId",
                table: "TransactionGlMappings");
        }
    }
}
