using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PrimafitERP.Data;

#nullable disable

namespace Primafit_ERP.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261008120000_AddItemCodeSequenceToCategory")]
    public partial class AddItemCodeSequenceToCategory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ItemCodeSequenceDefinitionId",
                table: "ItemCategories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategories_ItemCodeSequenceDefinitionId",
                table: "ItemCategories",
                column: "ItemCodeSequenceDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemCategories_SequenceDefinitions_ItemCodeSequenceDefinitionId",
                table: "ItemCategories",
                column: "ItemCodeSequenceDefinitionId",
                principalTable: "SequenceDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemCategories_SequenceDefinitions_ItemCodeSequenceDefinitionId",
                table: "ItemCategories");

            migrationBuilder.DropIndex(
                name: "IX_ItemCategories_ItemCodeSequenceDefinitionId",
                table: "ItemCategories");

            migrationBuilder.DropColumn(
                name: "ItemCodeSequenceDefinitionId",
                table: "ItemCategories");
        }
    }
}
