using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurableDocumentNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_CustomerGroups_CustomerGroupId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendors_VendorGroups_VendorGroupId",
                table: "Vendors");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Vendors",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SequenceDefinitionId",
                table: "VendorGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "ItemCategories",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SequenceDefinitionId",
                table: "ItemCategories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SequenceDefinitionId",
                table: "CustomerGroups",
                type: "uniqueidentifier",
                nullable: true);

            // Keep existing customers and vendors attached to a real company group
            // before their nullable foreign keys are tightened to NOT NULL.
            migrationBuilder.Sql(@"
INSERT INTO [CustomerGroups] ([Id], [CompanyId], [Name], [ReceivablesAccountId])
SELECT NEWID(), c.[CompanyDetailsId], N'Default Customer Group', NULL
FROM [CompanyDetails] c
WHERE NOT EXISTS (SELECT 1 FROM [CustomerGroups] g WHERE g.[CompanyId] = c.[CompanyDetailsId]);

INSERT INTO [CustomerGroups] ([Id], [CompanyId], [Name], [ReceivablesAccountId])
SELECT DISTINCT NEWID(), c.[CompanyId], N'Default Customer Group', NULL
FROM [Customers] c
WHERE NOT EXISTS (SELECT 1 FROM [CustomerGroups] g WHERE g.[CompanyId] = c.[CompanyId]);

INSERT INTO [VendorGroups] ([Id], [CompanyId], [Name], [PayablesAccountId])
SELECT NEWID(), c.[CompanyDetailsId], N'Default Vendor Group', NULL
FROM [CompanyDetails] c
WHERE NOT EXISTS (SELECT 1 FROM [VendorGroups] g WHERE g.[CompanyId] = c.[CompanyDetailsId]);

INSERT INTO [VendorGroups] ([Id], [CompanyId], [Name], [PayablesAccountId])
SELECT DISTINCT NEWID(), v.[CompanyId], N'Default Vendor Group', NULL
FROM [Vendors] v
WHERE NOT EXISTS (SELECT 1 FROM [VendorGroups] g WHERE g.[CompanyId] = v.[CompanyId]);

UPDATE c SET [CustomerGroupId] = g.[Id]
FROM [Customers] c
CROSS APPLY (SELECT TOP (1) [Id] FROM [CustomerGroups] WHERE [CompanyId] = c.[CompanyId] ORDER BY [Name], [Id]) g
WHERE c.[CustomerGroupId] IS NULL;

UPDATE v SET [VendorGroupId] = g.[Id]
FROM [Vendors] v
CROSS APPLY (SELECT TOP (1) [Id] FROM [VendorGroups] WHERE [CompanyId] = v.[CompanyId] ORDER BY [Name], [Id]) g
WHERE v.[VendorGroupId] IS NULL;

;WITH numbered AS
(
    SELECT [Id], [CompanyId], ROW_NUMBER() OVER (PARTITION BY [CompanyId] ORDER BY [Id]) AS n
    FROM [Customers] WHERE [Code] IS NULL OR LTRIM(RTRIM([Code])) = N''
)
UPDATE c SET [Code] = N'CUST-' + CASE WHEN n < 10000 THEN RIGHT(N'0000' + CONVERT(nvarchar(20), n), 4) ELSE CONVERT(nvarchar(20), n) END
FROM [Customers] c JOIN numbered n ON n.[Id] = c.[Id];

;WITH numbered AS
(
    SELECT [Id], [CompanyId], ROW_NUMBER() OVER (PARTITION BY [CompanyId] ORDER BY [Id]) AS n
    FROM [Vendors] WHERE [Code] IS NULL OR LTRIM(RTRIM([Code])) = N''
)
UPDATE v SET [Code] = N'VEND-' + CASE WHEN n < 10000 THEN RIGHT(N'0000' + CONVERT(nvarchar(20), n), 4) ELSE CONVERT(nvarchar(20), n) END
FROM [Vendors] v JOIN numbered n ON n.[Id] = v.[Id];

;WITH numbered AS
(
    SELECT [Id], [CompanyId], ROW_NUMBER() OVER (PARTITION BY [CompanyId] ORDER BY [Id]) AS n
    FROM [ItemCategories] WHERE [Code] IS NULL OR LTRIM(RTRIM([Code])) = N''
)
UPDATE c SET [Code] = N'CAT-' + CASE WHEN n < 10000 THEN RIGHT(N'0000' + CONVERT(nvarchar(20), n), 4) ELSE CONVERT(nvarchar(20), n) END
FROM [ItemCategories] c JOIN numbered n ON n.[Id] = c.[Id];
");

            migrationBuilder.AlterColumn<Guid>(
                name: "VendorGroupId",
                table: "Vendors",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerGroupId",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "SequenceDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TargetType = table.Column<int>(type: "int", nullable: false),
                    ResetInterval = table.Column<int>(type: "int", nullable: false),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SequenceDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SequenceDefinitions_CompanyDetails_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "CompanyDetails",
                        principalColumn: "CompanyDetailsId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SequenceComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    ComponentType = table.Column<int>(type: "int", nullable: false),
                    Length = table.Column<int>(type: "int", nullable: false),
                    Formula = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SequenceComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SequenceComponents_SequenceDefinitions_SequenceDefinitionId",
                        column: x => x.SequenceDefinitionId,
                        principalTable: "SequenceDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SequenceTrackers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopePeriodKey = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    LastNumber = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SequenceTrackers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SequenceTrackers_SequenceDefinitions_SequenceDefinitionId",
                        column: x => x.SequenceDefinitionId,
                        principalTable: "SequenceDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_CompanyId_Code",
                table: "Vendors",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorGroups_SequenceDefinitionId",
                table: "VendorGroups",
                column: "SequenceDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategories_CompanyId_Code",
                table: "ItemCategories",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategories_SequenceDefinitionId",
                table: "ItemCategories",
                column: "SequenceDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_Code",
                table: "Customers",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerGroups_SequenceDefinitionId",
                table: "CustomerGroups",
                column: "SequenceDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_SequenceComponents_SequenceDefinitionId_OrderIndex",
                table: "SequenceComponents",
                columns: new[] { "SequenceDefinitionId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SequenceDefinitions_CompanyId_TargetType",
                table: "SequenceDefinitions",
                columns: new[] { "CompanyId", "TargetType" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SequenceDefinitions_CompanyId_TargetType_Name",
                table: "SequenceDefinitions",
                columns: new[] { "CompanyId", "TargetType", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SequenceTrackers_SequenceDefinitionId_ScopePeriodKey",
                table: "SequenceTrackers",
                columns: new[] { "SequenceDefinitionId", "ScopePeriodKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerGroups_SequenceDefinitions_SequenceDefinitionId",
                table: "CustomerGroups",
                column: "SequenceDefinitionId",
                principalTable: "SequenceDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_CustomerGroups_CustomerGroupId",
                table: "Customers",
                column: "CustomerGroupId",
                principalTable: "CustomerGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ItemCategories_SequenceDefinitions_SequenceDefinitionId",
                table: "ItemCategories",
                column: "SequenceDefinitionId",
                principalTable: "SequenceDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorGroups_SequenceDefinitions_SequenceDefinitionId",
                table: "VendorGroups",
                column: "SequenceDefinitionId",
                principalTable: "SequenceDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Vendors_VendorGroups_VendorGroupId",
                table: "Vendors",
                column: "VendorGroupId",
                principalTable: "VendorGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerGroups_SequenceDefinitions_SequenceDefinitionId",
                table: "CustomerGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_CustomerGroups_CustomerGroupId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_ItemCategories_SequenceDefinitions_SequenceDefinitionId",
                table: "ItemCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorGroups_SequenceDefinitions_SequenceDefinitionId",
                table: "VendorGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendors_VendorGroups_VendorGroupId",
                table: "Vendors");

            migrationBuilder.DropTable(
                name: "SequenceComponents");

            migrationBuilder.DropTable(
                name: "SequenceTrackers");

            migrationBuilder.DropTable(
                name: "SequenceDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_Vendors_CompanyId_Code",
                table: "Vendors");

            migrationBuilder.DropIndex(
                name: "IX_VendorGroups_SequenceDefinitionId",
                table: "VendorGroups");

            migrationBuilder.DropIndex(
                name: "IX_ItemCategories_CompanyId_Code",
                table: "ItemCategories");

            migrationBuilder.DropIndex(
                name: "IX_ItemCategories_SequenceDefinitionId",
                table: "ItemCategories");

            migrationBuilder.DropIndex(
                name: "IX_Customers_CompanyId_Code",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_CustomerGroups_SequenceDefinitionId",
                table: "CustomerGroups");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "SequenceDefinitionId",
                table: "VendorGroups");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "ItemCategories");

            migrationBuilder.DropColumn(
                name: "SequenceDefinitionId",
                table: "ItemCategories");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SequenceDefinitionId",
                table: "CustomerGroups");

            migrationBuilder.AlterColumn<Guid>(
                name: "VendorGroupId",
                table: "Vendors",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerGroupId",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_CustomerGroups_CustomerGroupId",
                table: "Customers",
                column: "CustomerGroupId",
                principalTable: "CustomerGroups",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Vendors_VendorGroups_VendorGroupId",
                table: "Vendors",
                column: "VendorGroupId",
                principalTable: "VendorGroups",
                principalColumn: "Id");
        }
    }
}
