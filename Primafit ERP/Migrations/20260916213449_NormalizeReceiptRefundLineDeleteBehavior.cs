using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeReceiptRefundLineDeleteBehavior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Older databases do not contain this foreign key at all, while
            // a database created from an intermediate model may contain it
            // with CASCADE. Make the repair safe in both cases.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ReceiptRefundLines', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SalesOrderLines', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.foreign_keys
       WHERE name = N'FK_ReceiptRefundLines_SalesOrderLines_SalesOrderLineId'
   )
   AND NOT EXISTS
   (
       SELECT 1
       FROM dbo.ReceiptRefundLines rrl
       LEFT JOIN dbo.SalesOrderLines sol ON sol.Id = rrl.SalesOrderLineId
       WHERE sol.Id IS NULL
   )
BEGIN
    ALTER TABLE dbo.ReceiptRefundLines
        ADD CONSTRAINT FK_ReceiptRefundLines_SalesOrderLines_SalesOrderLineId
        FOREIGN KEY (SalesOrderLineId) REFERENCES dbo.SalesOrderLines(Id)
        ON DELETE NO ACTION;
END
IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_ReceiptRefundLines_SalesOrderLines_SalesOrderLineId'
      AND delete_referential_action_desc <> N'NO_ACTION'
)
BEGIN
    ALTER TABLE dbo.ReceiptRefundLines
        DROP CONSTRAINT FK_ReceiptRefundLines_SalesOrderLines_SalesOrderLineId;

    ALTER TABLE dbo.ReceiptRefundLines
        ADD CONSTRAINT FK_ReceiptRefundLines_SalesOrderLines_SalesOrderLineId
        FOREIGN KEY (SalesOrderLineId) REFERENCES dbo.SalesOrderLines(Id)
        ON DELETE NO ACTION;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ReceiptRefundLines', N'U') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM sys.foreign_keys
       WHERE name = N'FK_ReceiptRefundLines_SalesOrderLines_SalesOrderLineId'
   )
BEGIN
    ALTER TABLE dbo.ReceiptRefundLines
        DROP CONSTRAINT FK_ReceiptRefundLines_SalesOrderLines_SalesOrderLineId;
END");
        }
    }
}
