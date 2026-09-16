using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class SyncUomModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repair/synchronize databases that may already contain some of the UOM
            // objects. Every operation is guarded so this migration is safe to run
            // against both partially upgraded and fully upgraded databases.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.StockTransfers', N'QuantityInUom') IS NULL
    ALTER TABLE [dbo].[StockTransfers] ADD [QuantityInUom] decimal(18,4) NOT NULL CONSTRAINT [DF_StockTransfers_QuantityInUom] DEFAULT (0) WITH VALUES;
IF COL_LENGTH(N'dbo.StockTransfers', N'UomConversionFactor') IS NULL
    ALTER TABLE [dbo].[StockTransfers] ADD [UomConversionFactor] decimal(18,4) NOT NULL CONSTRAINT [DF_StockTransfers_UomConversionFactor] DEFAULT (1) WITH VALUES;
IF COL_LENGTH(N'dbo.StockTransfers', N'UomId') IS NULL
    ALTER TABLE [dbo].[StockTransfers] ADD [UomId] uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.StockTransfers', N'UomName') IS NULL
    ALTER TABLE [dbo].[StockTransfers] ADD [UomName] nvarchar(max) NOT NULL CONSTRAINT [DF_StockTransfers_UomName] DEFAULT (N'') WITH VALUES;

IF OBJECT_ID(N'dbo.ItemUomConversionLines', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItemUomConversionLines]
    (
        [Id] uniqueidentifier NOT NULL,
        [ItemId] uniqueidentifier NOT NULL,
        [UomId] uniqueidentifier NOT NULL,
        [ConversionFactorToBase] decimal(18,4) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_ItemUomConversionLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ItemUomConversionLines_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ItemUomConversionLines_UnitOfMeasures_UomId] FOREIGN KEY ([UomId]) REFERENCES [dbo].[UnitOfMeasures] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_ItemUomConversionLines_ItemId_UomId] ON [dbo].[ItemUomConversionLines] ([ItemId], [UomId]);
    CREATE INDEX [IX_ItemUomConversionLines_UomId] ON [dbo].[ItemUomConversionLines] ([UomId]);
END;

IF OBJECT_ID(N'dbo.UomConversionRules', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UomConversionRules]
    (
        [Id] uniqueidentifier NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        [FromUomId] uniqueidentifier NOT NULL,
        [ToUomId] uniqueidentifier NOT NULL,
        [ConversionFactor] decimal(18,4) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_UomConversionRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UomConversionRules_UnitOfMeasures_FromUomId] FOREIGN KEY ([FromUomId]) REFERENCES [dbo].[UnitOfMeasures] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UomConversionRules_UnitOfMeasures_ToUomId] FOREIGN KEY ([ToUomId]) REFERENCES [dbo].[UnitOfMeasures] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_UomConversionRules_CompanyId_FromUomId_ToUomId] ON [dbo].[UomConversionRules] ([CompanyId], [FromUomId], [ToUomId]);
    CREATE INDEX [IX_UomConversionRules_FromUomId] ON [dbo].[UomConversionRules] ([FromUomId]);
    CREATE INDEX [IX_UomConversionRules_ToUomId] ON [dbo].[UomConversionRules] ([ToUomId]);
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No schema changes are reversed by this snapshot-only migration.
        }
    }
}
