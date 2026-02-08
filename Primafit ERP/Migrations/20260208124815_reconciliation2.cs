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
IF OBJECT_ID(N'dbo.BankStatementLines', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BankStatementLines](
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
    );
END
");

            // Now apply your intended schema changes safely:
            // Add BankReconciliationId if not exists
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.BankStatementLines', 'BankReconciliationId') IS NULL
BEGIN
    ALTER TABLE dbo.BankStatementLines ADD BankReconciliationId UNIQUEIDENTIFIER NULL;
END
");

            // Create index if missing
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BankStatementLines_BankReconciliationId' AND object_id = OBJECT_ID('dbo.BankStatementLines'))
BEGIN
    CREATE INDEX [IX_BankStatementLines_BankReconciliationId] ON [dbo].[BankStatementLines]([BankReconciliationId]);
END
");

            // Add FK if missing
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_BankStatementLines_BankReconciliations_BankReconciliationId')
BEGIN
    ALTER TABLE [dbo].[BankStatementLines]
    ADD CONSTRAINT [FK_BankStatementLines_BankReconciliations_BankReconciliationId]
    FOREIGN KEY ([BankReconciliationId]) REFERENCES [dbo].[BankReconciliations]([Id]);
END
");

            // Add Type column to BankReconciliations if missing
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.BankReconciliations', 'Type') IS NULL
BEGIN
    ALTER TABLE dbo.BankReconciliations ADD [Type] int NOT NULL CONSTRAINT DF_BankReconciliations_Type DEFAULT(0);
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
