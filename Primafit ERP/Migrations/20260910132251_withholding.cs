using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class withholding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[WithholdingTaxes]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WithholdingTaxes](
        [Id] uniqueidentifier NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [PercentageValue] decimal(18,4) NOT NULL,
        [WithholdingGlAccountId] uniqueidentifier NOT NULL,
        [IsSystemDefault] bit NOT NULL,
        [DefaultKey] nvarchar(40) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WithholdingTaxes] PRIMARY KEY ([Id])
    );
END");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.PurchaseOrders', N'WithholdingTaxId') IS NULL
    ALTER TABLE [dbo].[PurchaseOrders] ADD [WithholdingTaxId] uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'WithholdingTaxName') IS NULL
    ALTER TABLE [dbo].[PurchaseOrders] ADD [WithholdingTaxName] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'WithholdingPercentage') IS NULL
    ALTER TABLE [dbo].[PurchaseOrders] ADD [WithholdingPercentage] decimal(18,4) NOT NULL CONSTRAINT [DF_PurchaseOrders_WithholdingPercentage] DEFAULT (0);
IF COL_LENGTH(N'dbo.PurchaseOrders', N'WithholdingAmountForeign') IS NULL
    ALTER TABLE [dbo].[PurchaseOrders] ADD [WithholdingAmountForeign] decimal(18,4) NOT NULL CONSTRAINT [DF_PurchaseOrders_WithholdingAmountForeign] DEFAULT (0);
IF COL_LENGTH(N'dbo.PurchaseOrders', N'WithholdingAmount') IS NULL
    ALTER TABLE [dbo].[PurchaseOrders] ADD [WithholdingAmount] decimal(18,4) NOT NULL CONSTRAINT [DF_PurchaseOrders_WithholdingAmount] DEFAULT (0);
IF COL_LENGTH(N'dbo.PurchaseOrders', N'WithholdingGlAccountId') IS NULL
    ALTER TABLE [dbo].[PurchaseOrders] ADD [WithholdingGlAccountId] uniqueidentifier NULL;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.VendorBills', N'WithholdingTaxId') IS NULL
    ALTER TABLE [dbo].[VendorBills] ADD [WithholdingTaxId] uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.VendorBills', N'WithholdingTaxName') IS NULL
    ALTER TABLE [dbo].[VendorBills] ADD [WithholdingTaxName] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.VendorBills', N'WithholdingPercentage') IS NULL
    ALTER TABLE [dbo].[VendorBills] ADD [WithholdingPercentage] decimal(18,4) NOT NULL CONSTRAINT [DF_VendorBills_WithholdingPercentage] DEFAULT (0);
IF COL_LENGTH(N'dbo.VendorBills', N'WithholdingAmountForeign') IS NULL
    ALTER TABLE [dbo].[VendorBills] ADD [WithholdingAmountForeign] decimal(18,4) NOT NULL CONSTRAINT [DF_VendorBills_WithholdingAmountForeign] DEFAULT (0);
IF COL_LENGTH(N'dbo.VendorBills', N'WithholdingAmount') IS NULL
    ALTER TABLE [dbo].[VendorBills] ADD [WithholdingAmount] decimal(18,4) NOT NULL CONSTRAINT [DF_VendorBills_WithholdingAmount] DEFAULT (0);
IF COL_LENGTH(N'dbo.VendorBills', N'WithholdingGlAccountId') IS NULL
    ALTER TABLE [dbo].[VendorBills] ADD [WithholdingGlAccountId] uniqueidentifier NULL;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.VendorPayments', N'WithholdingAmountForeign') IS NULL
    ALTER TABLE [dbo].[VendorPayments] ADD [WithholdingAmountForeign] decimal(18,4) NOT NULL CONSTRAINT [DF_VendorPayments_WithholdingAmountForeign] DEFAULT (0);
IF COL_LENGTH(N'dbo.VendorPayments', N'WithholdingAmount') IS NULL
    ALTER TABLE [dbo].[VendorPayments] ADD [WithholdingAmount] decimal(18,4) NOT NULL CONSTRAINT [DF_VendorPayments_WithholdingAmount] DEFAULT (0);
IF COL_LENGTH(N'dbo.VendorPayments', N'WithholdingTaxId') IS NULL
    ALTER TABLE [dbo].[VendorPayments] ADD [WithholdingTaxId] uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.VendorPayments', N'WithholdingTaxName') IS NULL
    ALTER TABLE [dbo].[VendorPayments] ADD [WithholdingTaxName] nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.VendorPayments', N'WithholdingPercentage') IS NULL
    ALTER TABLE [dbo].[VendorPayments] ADD [WithholdingPercentage] decimal(18,4) NOT NULL CONSTRAINT [DF_VendorPayments_WithholdingPercentage] DEFAULT (0);
IF COL_LENGTH(N'dbo.VendorPayments', N'WithholdingGlAccountId') IS NULL
    ALTER TABLE [dbo].[VendorPayments] ADD [WithholdingGlAccountId] uniqueidentifier NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This migration is intentionally idempotent because the earlier
            // WithholdingTaxModule migration may already own these columns in
            // an existing database. Do not drop live accounting history here.
        }
    }
}
