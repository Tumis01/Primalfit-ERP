using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PrimafitERP.Data;

#nullable disable

namespace Primafit_ERP.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261008140000_RemoveItemCategoryCodeSequence")]
    public partial class RemoveItemCategoryCodeSequence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemCategories_SequenceDefinitions_SequenceDefinitionId",
                table: "ItemCategories");

            migrationBuilder.DropIndex(
                name: "IX_ItemCategories_SequenceDefinitionId",
                table: "ItemCategories");

            migrationBuilder.DropColumn(
                name: "SequenceDefinitionId",
                table: "ItemCategories");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SequenceDefinitionId",
                table: "ItemCategories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategories_SequenceDefinitionId",
                table: "ItemCategories",
                column: "SequenceDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemCategories_SequenceDefinitions_SequenceDefinitionId",
                table: "ItemCategories",
                column: "SequenceDefinitionId",
                principalTable: "SequenceDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
