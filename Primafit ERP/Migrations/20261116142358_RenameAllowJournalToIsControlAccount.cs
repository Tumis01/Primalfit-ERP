using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class RenameAllowJournalToIsControlAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AllowJournal",
                table: "SegChartOfAccounts",
                newName: "IsControlAccount");

            // Previously AllowJournal = false meant control account.
            migrationBuilder.Sql("UPDATE [SegChartOfAccounts] SET [IsControlAccount] = CASE WHEN [IsControlAccount] = 1 THEN 0 ELSE 1 END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [SegChartOfAccounts] SET [IsControlAccount] = CASE WHEN [IsControlAccount] = 1 THEN 0 ELSE 1 END;");

            migrationBuilder.RenameColumn(
                name: "IsControlAccount",
                table: "SegChartOfAccounts",
                newName: "AllowJournal");
        }
    }
}
