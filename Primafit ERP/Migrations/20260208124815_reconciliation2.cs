using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class reconciliation2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DECLARE @schema sysname;
SELECT TOP (1) @schema = s.name
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name = N'BankReconciliations';

SET @schema = COALESCE(@schema, N'dbo');

IF OBJECT_ID(QUOTENAME(@schema) + N'.BankStatementLines', N'U') IS NULL
BEGIN
    DECLARE @createSql nvarchar(max) = N'CREATE TABLE ' + QUOTENAME(@schema) + N'.[BankStatementLines](
        [Id] UNIQUEIDENTIFIER NOT NULL,
        [ReconciliationId] UNIQUEIDENTIFIER NOT NULL,
        [Date] date NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [Reference] nvarchar(max) NULL,
        [Amount] decimal(18,2) NOT NULL,
        [IsCleared] bit NOT NULL,
        [IsMatched] bit NOT NULL,
        [MatchedGLTransactionId] UNIQUEIDENTIFIER NULL,
        CONSTRAINT [PK_BankStatementLines] PRIMARY KEY ([Id])
    );';
    EXEC sp_executesql @createSql;
END
");

            // Now apply your intended schema changes safely:
            // Add BankReconciliationId if not exists
            migrationBuilder.Sql(@"
DECLARE @schema sysname;
SELECT TOP (1) @schema = s.name
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name = N'BankReconciliations';

SET @schema = COALESCE(@schema, N'dbo');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns c
    JOIN sys.tables t ON t.object_id = c.object_id
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = @schema
      AND t.name = N'BankStatementLines'
      AND c.name = N'BankReconciliationId'
)
BEGIN
    DECLARE @alterSql nvarchar(max) = N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[BankStatementLines] ADD [BankReconciliationId] UNIQUEIDENTIFIER NULL;';
    EXEC sp_executesql @alterSql;
END
");

            // Create index if missing
            migrationBuilder.Sql(@"
DECLARE @schema sysname;
SELECT TOP (1) @schema = s.name
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name = N'BankReconciliations';

SET @schema = COALESCE(@schema, N'dbo');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes i
    JOIN sys.tables t ON t.object_id = i.object_id
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE i.name = N'IX_BankStatementLines_BankReconciliationId'
      AND s.name = @schema
      AND t.name = N'BankStatementLines'
)
BEGIN
    DECLARE @indexSql nvarchar(max) = N'CREATE INDEX [IX_BankStatementLines_BankReconciliationId] ON ' + QUOTENAME(@schema) + N'.[BankStatementLines]([BankReconciliationId]);';
    EXEC sp_executesql @indexSql;
END
");

            // Add FK if missing
            migrationBuilder.Sql(@"
DECLARE @schema sysname;
SELECT TOP (1) @schema = s.name
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name = N'BankReconciliations';

SET @schema = COALESCE(@schema, N'dbo');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys fk
    JOIN sys.tables t ON t.object_id = fk.parent_object_id
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE fk.name = N'FK_BankStatementLines_BankReconciliations_BankReconciliationId'
      AND s.name = @schema
      AND t.name = N'BankStatementLines'
)
BEGIN
    DECLARE @fkSql nvarchar(max) = N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[BankStatementLines]
    ADD CONSTRAINT [FK_BankStatementLines_BankReconciliations_BankReconciliationId]
    FOREIGN KEY ([BankReconciliationId]) REFERENCES ' + QUOTENAME(@schema) + N'.[BankReconciliations]([Id]);';
    EXEC sp_executesql @fkSql;
END
");

            // Add Type column to BankReconciliations if missing
            migrationBuilder.Sql(@"
DECLARE @schema sysname;
SELECT TOP (1) @schema = s.name
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name = N'BankReconciliations';

SET @schema = COALESCE(@schema, N'dbo');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns c
    JOIN sys.tables t ON t.object_id = c.object_id
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = @schema
      AND t.name = N'BankReconciliations'
      AND c.name = N'Type'
)
BEGIN
    DECLARE @typeSql nvarchar(max) = N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[BankReconciliations] ADD [Type] int NOT NULL CONSTRAINT [DF_BankReconciliations_Type] DEFAULT(0);';
    EXEC sp_executesql @typeSql;
END
");
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankStatementLines_BankReconciliations_BankReconciliationId",
                table: "BankStatementLines");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementLines_BankReconciliationId",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "BankReconciliationId",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "BankReconciliations");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "BankStatementLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "MatchedAt",
                table: "BankStatementLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchedByUserId",
                table: "BankStatementLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AccountingPeriodId",
                table: "BankReconciliations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                table: "BankReconciliations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_ReconciliationId",
                table: "BankStatementLines",
                column: "ReconciliationId");

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatementLines_BankReconciliations_ReconciliationId",
                table: "BankStatementLines",
                column: "ReconciliationId",
                principalTable: "BankReconciliations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
