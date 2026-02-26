using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class new1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AccountId",
                table: "GLTransactions",
                newName: "SegCoaId");

            migrationBuilder.RenameColumn(
                name: "AccountId",
                table: "GLJournalLines",
                newName: "SegCoaId");

            migrationBuilder.RenameColumn(
                name: "OffsetAccountId",
                table: "CashbookEntries",
                newName: "OffsetSegCoaId");

            migrationBuilder.RenameColumn(
                name: "BankAccountId",
                table: "CashbookBatches",
                newName: "BankSegCoaId");

            migrationBuilder.CreateIndex(
                name: "IX_GLTransactions_SegCoaId",
                table: "GLTransactions",
                column: "SegCoaId");

            migrationBuilder.CreateIndex(
                name: "IX_GLJournalLines_SegCoaId",
                table: "GLJournalLines",
                column: "SegCoaId");

            migrationBuilder.AddForeignKey(
                name: "FK_GLJournalLines_SegChartOfAccounts_SegCoaId",
                table: "GLJournalLines",
                column: "SegCoaId",
                principalTable: "SegChartOfAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GLTransactions_SegChartOfAccounts_SegCoaId",
                table: "GLTransactions",
                column: "SegCoaId",
                principalTable: "SegChartOfAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GLJournalLines_SegChartOfAccounts_SegCoaId",
                table: "GLJournalLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GLTransactions_SegChartOfAccounts_SegCoaId",
                table: "GLTransactions");

            migrationBuilder.DropIndex(
                name: "IX_GLTransactions_SegCoaId",
                table: "GLTransactions");

            migrationBuilder.DropIndex(
                name: "IX_GLJournalLines_SegCoaId",
                table: "GLJournalLines");

            migrationBuilder.RenameColumn(
                name: "SegCoaId",
                table: "GLTransactions",
                newName: "AccountId");

            migrationBuilder.RenameColumn(
                name: "SegCoaId",
                table: "GLJournalLines",
                newName: "AccountId");

            migrationBuilder.RenameColumn(
                name: "OffsetSegCoaId",
                table: "CashbookEntries",
                newName: "OffsetAccountId");

            migrationBuilder.RenameColumn(
                name: "BankSegCoaId",
                table: "CashbookBatches",
                newName: "BankAccountId");
        }
    }
}
