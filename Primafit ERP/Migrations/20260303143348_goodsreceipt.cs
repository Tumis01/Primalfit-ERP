using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class goodsreceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptLine_GoodsReceipts_GoodsReceiptId",
                table: "GoodsReceiptLine");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GoodsReceiptLine",
                table: "GoodsReceiptLine");

            migrationBuilder.RenameTable(
                name: "GoodsReceiptLine",
                newName: "GoodsReceiptLines");

            migrationBuilder.RenameIndex(
                name: "IX_GoodsReceiptLine_GoodsReceiptId",
                table: "GoodsReceiptLines",
                newName: "IX_GoodsReceiptLines_GoodsReceiptId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GoodsReceiptLines",
                table: "GoodsReceiptLines",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptLines_GoodsReceipts_GoodsReceiptId",
                table: "GoodsReceiptLines",
                column: "GoodsReceiptId",
                principalTable: "GoodsReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptLines_GoodsReceipts_GoodsReceiptId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GoodsReceiptLines",
                table: "GoodsReceiptLines");

            migrationBuilder.RenameTable(
                name: "GoodsReceiptLines",
                newName: "GoodsReceiptLine");

            migrationBuilder.RenameIndex(
                name: "IX_GoodsReceiptLines_GoodsReceiptId",
                table: "GoodsReceiptLine",
                newName: "IX_GoodsReceiptLine_GoodsReceiptId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GoodsReceiptLine",
                table: "GoodsReceiptLine",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptLine_GoodsReceipts_GoodsReceiptId",
                table: "GoodsReceiptLine",
                column: "GoodsReceiptId",
                principalTable: "GoodsReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
