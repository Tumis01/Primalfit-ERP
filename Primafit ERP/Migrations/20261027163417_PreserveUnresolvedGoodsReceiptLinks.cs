using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    public partial class PreserveUnresolvedGoodsReceiptLinks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The earlier document migration now creates this column for new upgrades.
            // Databases that already ran its original version still need it here.
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'dbo.GoodsReceiptLines', N'LegacyPurchaseOrderLineId') IS NULL
                    ALTER TABLE [dbo].[GoodsReceiptLines] ADD [LegacyPurchaseOrderLineId] uniqueidentifier NULL;
                """);

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.GoodsReceiptLines')
                           AND name = N'PurchaseOrderLineId' AND is_nullable = 0)
                BEGIN
                    ALTER TABLE [dbo].[GoodsReceiptLines] DROP CONSTRAINT [FK_GoodsReceiptLines_PurchaseOrderLines_PurchaseOrderLineId];
                    DROP INDEX [IX_GoodsReceiptLines_GoodsReceiptId_PurchaseOrderLineId] ON [dbo].[GoodsReceiptLines];
                    DROP INDEX [IX_GoodsReceiptLines_PurchaseOrderLineId] ON [dbo].[GoodsReceiptLines];
                    ALTER TABLE [dbo].[GoodsReceiptLines] ALTER COLUMN [PurchaseOrderLineId] uniqueidentifier NULL;
                    CREATE INDEX [IX_GoodsReceiptLines_PurchaseOrderLineId] ON [dbo].[GoodsReceiptLines] ([PurchaseOrderLineId]);
                    CREATE UNIQUE INDEX [IX_GoodsReceiptLines_GoodsReceiptId_PurchaseOrderLineId]
                        ON [dbo].[GoodsReceiptLines] ([GoodsReceiptId], [PurchaseOrderLineId])
                        WHERE [PurchaseOrderLineId] IS NOT NULL;
                    ALTER TABLE [dbo].[GoodsReceiptLines] WITH CHECK ADD CONSTRAINT [FK_GoodsReceiptLines_PurchaseOrderLines_PurchaseOrderLineId]
                        FOREIGN KEY ([PurchaseOrderLineId]) REFERENCES [dbo].[PurchaseOrderLines] ([Id]) ON DELETE NO ACTION;
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The preceding document migration owns this schema on new upgrades.
            // Its Down method checks unresolved links before removing the column.
        }
    }
}
