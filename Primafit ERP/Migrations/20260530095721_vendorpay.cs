using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class vendorpay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VendorPayment_VendorBills_VendorBillId",
                table: "VendorPayment");

            migrationBuilder.DropPrimaryKey(
                name: "PK_VendorPayment",
                table: "VendorPayment");

            migrationBuilder.RenameTable(
                name: "VendorPayment",
                newName: "VendorPayments");

            migrationBuilder.RenameIndex(
                name: "IX_VendorPayment_VendorBillId",
                table: "VendorPayments",
                newName: "IX_VendorPayments_VendorBillId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_VendorPayments",
                table: "VendorPayments",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorPayments_VendorBills_VendorBillId",
                table: "VendorPayments",
                column: "VendorBillId",
                principalTable: "VendorBills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VendorPayments_VendorBills_VendorBillId",
                table: "VendorPayments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_VendorPayments",
                table: "VendorPayments");

            migrationBuilder.RenameTable(
                name: "VendorPayments",
                newName: "VendorPayment");

            migrationBuilder.RenameIndex(
                name: "IX_VendorPayments_VendorBillId",
                table: "VendorPayment",
                newName: "IX_VendorPayment_VendorBillId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_VendorPayment",
                table: "VendorPayment",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorPayment_VendorBills_VendorBillId",
                table: "VendorPayment",
                column: "VendorBillId",
                principalTable: "VendorBills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
