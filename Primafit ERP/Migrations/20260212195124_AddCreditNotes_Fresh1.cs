using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNotes_Fresh1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_SalesInvoices_SalesInvoiceId",
                table: "CreditNotes");

            migrationBuilder.RenameColumn(
                name: "SalesInvoiceId",
                table: "CreditNotes",
                newName: "SalesOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_CreditNotes_SalesInvoiceId",
                table: "CreditNotes",
                newName: "IX_CreditNotes_SalesOrderId");

            migrationBuilder.RenameColumn(
                name: "SalesInvoiceLineId",
                table: "CreditNoteLines",
                newName: "SalesOrderLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_SalesOrders_SalesOrderId",
                table: "CreditNotes",
                column: "SalesOrderId",
                principalTable: "SalesOrders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_SalesOrders_SalesOrderId",
                table: "CreditNotes");

            migrationBuilder.RenameColumn(
                name: "SalesOrderId",
                table: "CreditNotes",
                newName: "SalesInvoiceId");

            migrationBuilder.RenameIndex(
                name: "IX_CreditNotes_SalesOrderId",
                table: "CreditNotes",
                newName: "IX_CreditNotes_SalesInvoiceId");

            migrationBuilder.RenameColumn(
                name: "SalesOrderLineId",
                table: "CreditNoteLines",
                newName: "SalesInvoiceLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_SalesInvoices_SalesInvoiceId",
                table: "CreditNotes",
                column: "SalesInvoiceId",
                principalTable: "SalesInvoices",
                principalColumn: "Id");
        }
    }
}
