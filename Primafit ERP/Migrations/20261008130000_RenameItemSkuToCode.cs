using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PrimafitERP.Data;

#nullable disable

namespace Primafit_ERP.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261008130000_RenameItemSkuToCode")]
    public partial class RenameItemSkuToCode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SKU",
                table: "Items",
                newName: "Code");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Items",
                newName: "SKU");
        }
    }
}
