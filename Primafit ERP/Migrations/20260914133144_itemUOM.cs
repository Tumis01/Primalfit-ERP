using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    public partial class itemUOM : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemUomConversionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversionFactorToBase = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemUomConversionLines", x => x.Id);
                    table.ForeignKey("FK_ItemUomConversionLines_Items_ItemId", x => x.ItemId, "Items", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_ItemUomConversionLines_UnitOfMeasures_UomId", x => x.UomId, "UnitOfMeasures", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("IX_ItemUomConversionLines_ItemId_UomId", "ItemUomConversionLines", new[] { "ItemId", "UomId" }, unique: true);
            migrationBuilder.CreateIndex("IX_ItemUomConversionLines_UomId", "ItemUomConversionLines", "UomId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ItemUomConversionLines");
        }
    }
}
