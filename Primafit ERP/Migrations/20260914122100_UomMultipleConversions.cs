using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class UomMultipleConversions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UomConversionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromUomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToUomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversionFactor = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UomConversionRules", x => x.Id);
                    table.ForeignKey("FK_UomConversionRules_UnitOfMeasures_FromUomId", x => x.FromUomId, "UnitOfMeasures", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_UomConversionRules_UnitOfMeasures_ToUomId", x => x.ToUomId, "UnitOfMeasures", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("IX_UomConversionRules_CompanyId_FromUomId_ToUomId", "UomConversionRules", new[] { "CompanyId", "FromUomId", "ToUomId" }, unique: true);
            migrationBuilder.CreateIndex("IX_UomConversionRules_FromUomId", "UomConversionRules", "FromUomId");
            migrationBuilder.CreateIndex("IX_UomConversionRules_ToUomId", "UomConversionRules", "ToUomId");

            // Preserve the original one-line UOM setup when upgrading existing databases.
            migrationBuilder.Sql(@"
                INSERT INTO UomConversionRules (Id, CompanyId, FromUomId, ToUomId, ConversionFactor, IsActive)
                SELECT NEWID(), u.CompanyId, u.Id, u.ConversionUomId, CASE WHEN u.ConversionFactorValue > 0 THEN u.ConversionFactorValue ELSE 1 END, 1
                FROM UnitOfMeasures u
                WHERE u.ConversionUomId IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM UomConversionRules r WHERE r.CompanyId = u.CompanyId AND r.FromUomId = u.Id AND r.ToUomId = u.ConversionUomId);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UomConversionRules");
        }
    }
}
