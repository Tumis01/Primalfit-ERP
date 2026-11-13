using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class StockEntryAndReconciliationCustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "BankStatementLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFieldsVersion",
                table: "BankReconciliations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_BankStatementLines_ReconciliationId_Id",
                table: "BankStatementLines",
                columns: new[] { "ReconciliationId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_BankReconciliations_CompanyId_Id",
                table: "BankReconciliations",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "InventoryEntryDocument",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GLBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CommittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CustomFieldsVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryEntryDocument", x => x.Id);
                    table.UniqueConstraint("AK_InventoryEntryDocument_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_InventoryEntryDocument_GLBatches_CompanyId_GLBatchId",
                        columns: x => new { x.CompanyId, x.GLBatchId },
                        principalTable: "GLBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfBankReconciliationValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_UdfBankReconciliationValues", x => x.Id);
                    table.CheckConstraint("CK_UdfBankReconciliationValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfBankReconciliationValues_BankReconciliations_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "BankReconciliations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfBankReconciliationValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfBankReconciliationValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UdfBankStatementLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_UdfBankStatementLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfBankStatementLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfBankStatementLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfBankStatementLineValues_BankReconciliations_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "BankReconciliations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfBankStatementLineValues_BankStatementLines_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "BankStatementLines",
                        principalColumns: new[] { "ReconciliationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UdfBankStatementLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfBankStatementLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryEntryRecordLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityChange = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    ValueChange = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CustomFieldsVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryEntryRecordLine", x => x.Id);
                    table.UniqueConstraint("AK_InventoryEntryRecordLine_HeaderId_Id", x => new { x.HeaderId, x.Id });
                    table.ForeignKey(
                        name: "FK_InventoryEntryRecordLine_InventoryEntryDocument_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "InventoryEntryDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfInventoryEntryDocumentValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_UdfInventoryEntryDocumentValues", x => x.Id);
                    table.CheckConstraint("CK_UdfInventoryEntryDocumentValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfInventoryEntryDocumentValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfInventoryEntryDocumentValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfInventoryEntryDocumentValues_InventoryEntryDocument_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalTable: "InventoryEntryDocument",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UdfInventoryEntryRecordLineValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_UdfInventoryEntryRecordLineValues", x => x.Id);
                    table.CheckConstraint("CK_UdfInventoryEntryRecordLineValues_Parent", "[ParentId] IS NOT NULL");
                    table.CheckConstraint("CK_UdfInventoryEntryRecordLineValues_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_UdfInventoryEntryRecordLineValues_CustomFieldOption_CompanyId_DefinitionId_OptionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.OptionId },
                        principalTable: "CustomFieldOption",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfInventoryEntryRecordLineValues_CustomFieldRevision_CompanyId_DefinitionId_RevisionId",
                        columns: x => new { x.CompanyId, x.DefinitionId, x.RevisionId },
                        principalTable: "CustomFieldRevision",
                        principalColumns: new[] { "CompanyId", "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfInventoryEntryRecordLineValues_InventoryEntryDocument_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "InventoryEntryDocument",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UdfInventoryEntryRecordLineValues_InventoryEntryRecordLine_ParentId_RecordId",
                        columns: x => new { x.ParentId, x.RecordId },
                        principalTable: "InventoryEntryRecordLine",
                        principalColumns: new[] { "HeaderId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryEntryDocument_CompanyId_GLBatchId",
                table: "InventoryEntryDocument",
                columns: new[] { "CompanyId", "GLBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfBankReconciliationValues_CompanyId_DefinitionId_OptionId",
                table: "UdfBankReconciliationValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfBankReconciliationValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfBankReconciliationValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfBankReconciliationValues_CompanyId_RecordId_DefinitionId",
                table: "UdfBankReconciliationValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfBankStatementLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfBankStatementLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfBankStatementLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfBankStatementLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfBankStatementLineValues_CompanyId_ParentId",
                table: "UdfBankStatementLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfBankStatementLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfBankStatementLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfBankStatementLineValues_ParentId_RecordId",
                table: "UdfBankStatementLineValues",
                columns: new[] { "ParentId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryEntryDocumentValues_CompanyId_DefinitionId_OptionId",
                table: "UdfInventoryEntryDocumentValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryEntryDocumentValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfInventoryEntryDocumentValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryEntryDocumentValues_CompanyId_RecordId_DefinitionId",
                table: "UdfInventoryEntryDocumentValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryEntryRecordLineValues_CompanyId_DefinitionId_OptionId",
                table: "UdfInventoryEntryRecordLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryEntryRecordLineValues_CompanyId_DefinitionId_RevisionId",
                table: "UdfInventoryEntryRecordLineValues",
                columns: new[] { "CompanyId", "DefinitionId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryEntryRecordLineValues_CompanyId_ParentId",
                table: "UdfInventoryEntryRecordLineValues",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryEntryRecordLineValues_CompanyId_RecordId_DefinitionId",
                table: "UdfInventoryEntryRecordLineValues",
                columns: new[] { "CompanyId", "RecordId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UdfInventoryEntryRecordLineValues_ParentId_RecordId",
                table: "UdfInventoryEntryRecordLineValues",
                columns: new[] { "ParentId", "RecordId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UdfBankReconciliationValues");

            migrationBuilder.DropTable(
                name: "UdfBankStatementLineValues");

            migrationBuilder.DropTable(
                name: "UdfInventoryEntryDocumentValues");

            migrationBuilder.DropTable(
                name: "UdfInventoryEntryRecordLineValues");

            migrationBuilder.DropTable(
                name: "InventoryEntryRecordLine");

            migrationBuilder.DropTable(
                name: "InventoryEntryDocument");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BankStatementLines_ReconciliationId_Id",
                table: "BankStatementLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BankReconciliations_CompanyId_Id",
                table: "BankReconciliations");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "CustomFieldsVersion",
                table: "BankReconciliations");
        }
    }
}
