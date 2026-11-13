using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class UserDefinedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLines_HeaderId",
                table: "SalesOrderLines");

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "Vendors",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmissionState",
                table: "SalesOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "Items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Vendors_CompanyId_Id",
                table: "Vendors",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SalesOrders_CompanyId_Id",
                table: "SalesOrders",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SalesOrderLines_HeaderId_Id",
                table: "SalesOrderLines",
                columns: new[] { "HeaderId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Items_CompanyId_Id",
                table: "Items",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Customers_CompanyId_Id",
                table: "Customers",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "CustomFieldDefinition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    DataType = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CurrentRevision = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldDefinition", x => x.Id);
                    table.UniqueConstraint("AK_CustomFieldDefinition_CompanyId_Id", x => new { x.CompanyId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "CustomFieldOption",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldOption", x => x.Id);
                    table.UniqueConstraint("AK_CustomFieldOption_CompanyId_DefinitionId_Id", x => new { x.CompanyId, x.DefinitionId, x.Id });
                    table.ForeignKey(
                        name: "FK_CustomFieldOption_CustomFieldDefinition_CompanyId_DefinitionId",
                        columns: x => new { x.CompanyId, x.DefinitionId },
                        principalTable: "CustomFieldDefinition",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomFieldRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HelpText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Section = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    MaxLength = table.Column<int>(type: "int", nullable: false),
                    Minimum = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Maximum = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: false),
                    IncludeOnPrint = table.Column<bool>(type: "bit", nullable: false),
                    IncludeInExports = table.Column<bool>(type: "bit", nullable: false),
                    IsReportable = table.Column<bool>(type: "bit", nullable: false),
                    IsFilterable = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldRevision", x => x.Id);
                    table.UniqueConstraint("AK_CustomFieldRevision_CompanyId_DefinitionId_Id", x => new { x.CompanyId, x.DefinitionId, x.Id });
                    table.ForeignKey(
                        name: "FK_CustomFieldRevision_CustomFieldDefinition_CompanyId_DefinitionId",
                        columns: x => new { x.CompanyId, x.DefinitionId },
                        principalTable: "CustomFieldDefinition",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerCustomFieldValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCustomFieldValues", x => x.Id);
                    table.CheckConstraint("CK_CustomerCustomFieldValues_TypedValue", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_CustomerCustomFieldValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCustomFieldValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCustomFieldValues_Customers_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemCustomFieldValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCustomFieldValues", x => x.Id);
                    table.CheckConstraint("CK_ItemCustomFieldValues_TypedValue", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_ItemCustomFieldValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCustomFieldValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCustomFieldValues_Items_CompanyId_ItemId",
                        columns: x => new { x.CompanyId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderCustomFieldValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderCustomFieldValues", x => x.Id);
                    table.CheckConstraint("CK_SalesOrderCustomFieldValues_TypedValue", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_SalesOrderCustomFieldValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderCustomFieldValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderCustomFieldValues_SalesOrders_CompanyId_SalesOrderId",
                        columns: x => new { x.CompanyId, x.SalesOrderId },
                        principalTable: "SalesOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLineCustomFieldValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLineCustomFieldValues", x => x.Id);
                    table.CheckConstraint("CK_SalesOrderLineCustomFieldValues_TypedValue", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_SalesOrderLineCustomFieldValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLineCustomFieldValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLineCustomFieldValues_SalesOrderLines_SalesOrderId_SalesOrderLineId",
                        columns: x => new { x.SalesOrderId, x.SalesOrderLineId },
                        principalTable: "SalesOrderLines",
                        principalColumns: new[] { "HeaderId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesOrderLineCustomFieldValues_SalesOrders_CompanyId_SalesOrderId",
                        columns: x => new { x.CompanyId, x.SalesOrderId },
                        principalTable: "SalesOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorCustomFieldValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionLabelSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorCustomFieldValues", x => x.Id);
                    table.CheckConstraint("CK_VendorCustomFieldValues_TypedValue", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_VendorCustomFieldValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorCustomFieldValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorCustomFieldValues_Vendors_CompanyId_VendorId",
                        columns: x => new { x.CompanyId, x.VendorId },
                        principalTable: "Vendors",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCustomFieldValues_CompanyId_CustomerId_DefinitionId",
                table: "CustomerCustomFieldValues",
                columns: new[] { "CompanyId", "CustomerId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCustomFieldValues_CompanyId_DefinitionId_OptionId",
                table: "CustomerCustomFieldValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCustomFieldValues_CompanyId_DefinitionId_RevisionId",
                table: "CustomerCustomFieldValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldDefinition_CompanyId_Scope_Code",
                table: "CustomFieldDefinition",
                columns: new[] { "CompanyId", "Scope", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldRevision_CompanyId_DefinitionId_RevisionNumber",
                table: "CustomFieldRevision",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemCustomFieldValues_CompanyId_DefinitionId_OptionId",
                table: "ItemCustomFieldValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemCustomFieldValues_CompanyId_DefinitionId_RevisionId",
                table: "ItemCustomFieldValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemCustomFieldValues_CompanyId_ItemId_DefinitionId",
                table: "ItemCustomFieldValues",
                columns: new[] { "CompanyId", "ItemId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderCustomFieldValues_CompanyId_DefinitionId_OptionId",
                table: "SalesOrderCustomFieldValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderCustomFieldValues_CompanyId_DefinitionId_RevisionId",
                table: "SalesOrderCustomFieldValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderCustomFieldValues_CompanyId_SalesOrderId_DefinitionId",
                table: "SalesOrderCustomFieldValues",
                columns: new[] { "CompanyId", "SalesOrderId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLineCustomFieldValues_CompanyId_DefinitionId_OptionId",
                table: "SalesOrderLineCustomFieldValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLineCustomFieldValues_CompanyId_DefinitionId_RevisionId",
                table: "SalesOrderLineCustomFieldValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLineCustomFieldValues_CompanyId_SalesOrderId",
                table: "SalesOrderLineCustomFieldValues",
                columns: new[] { "CompanyId", "SalesOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLineCustomFieldValues_CompanyId_SalesOrderLineId_DefinitionId",
                table: "SalesOrderLineCustomFieldValues",
                columns: new[] { "CompanyId", "SalesOrderLineId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLineCustomFieldValues_SalesOrderId_SalesOrderLineId",
                table: "SalesOrderLineCustomFieldValues",
                columns: new[] { "SalesOrderId", "SalesOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorCustomFieldValues_CompanyId_DefinitionId_OptionId",
                table: "VendorCustomFieldValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorCustomFieldValues_CompanyId_DefinitionId_RevisionId",
                table: "VendorCustomFieldValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorCustomFieldValues_CompanyId_VendorId_DefinitionId",
                table: "VendorCustomFieldValues",
                columns: new[] { "CompanyId", "VendorId", "DefinitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerCustomFieldValues");

            migrationBuilder.DropTable(
                name: "ItemCustomFieldValues");

            migrationBuilder.DropTable(
                name: "SalesOrderCustomFieldValues");

            migrationBuilder.DropTable(
                name: "SalesOrderLineCustomFieldValues");

            migrationBuilder.DropTable(
                name: "VendorCustomFieldValues");

            migrationBuilder.DropTable(
                name: "CustomFieldOption");

            migrationBuilder.DropTable(
                name: "CustomFieldRevision");

            migrationBuilder.DropTable(
                name: "CustomFieldDefinition");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Vendors_CompanyId_Id",
                table: "Vendors");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SalesOrders_CompanyId_Id",
                table: "SalesOrders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SalesOrderLines_HeaderId_Id",
                table: "SalesOrderLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Items_CompanyId_Id",
                table: "Items");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Customers_CompanyId_Id",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "SubmissionState",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_HeaderId",
                table: "SalesOrderLines",
                column: "HeaderId");
        }
    }
}
