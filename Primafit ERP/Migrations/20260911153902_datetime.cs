using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class datetime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The timestamp migration is intentionally idempotent because the
            // preceding TransactionDateTime migration may already have been
            // applied in an existing database.
            foreach (var table in new[] { "SalesOrders", "CreditNotes", "DebitNotes", "ReceiptRefunds", "VendorReturns" })
            {
                migrationBuilder.Sql($@"
IF COL_LENGTH(N'dbo.{table}', N'TransactionDateTime') IS NULL
BEGIN
    ALTER TABLE [dbo].[{table}]
        ADD [TransactionDateTime] datetime2 NOT NULL
            CONSTRAINT [DF_{table}_TransactionDateTime] DEFAULT (GETDATE());
END");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "SalesOrders", "CreditNotes", "DebitNotes", "ReceiptRefunds", "VendorReturns" })
            {
                migrationBuilder.Sql($@"
IF COL_LENGTH(N'dbo.{table}', N'TransactionDateTime') IS NOT NULL
BEGIN
    DECLARE @constraintName sysname;
    SELECT @constraintName = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    INNER JOIN sys.tables t ON t.object_id = c.object_id
    WHERE t.name = N'{table}' AND c.name = N'TransactionDateTime';

    IF @constraintName IS NOT NULL
        EXEC(N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT [' + @constraintName + N']');

    ALTER TABLE [dbo].[{table}] DROP COLUMN [TransactionDateTime];
END");
            }
        }
    }
}
