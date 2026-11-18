using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyCustomFieldSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Merge the former long-text type into Text without changing definitions,
            // revision links, configured lengths or stored text values.
            migrationBuilder.Sql("UPDATE [CustomFieldDefinition] SET [DataType] = 0 WHERE [DataType] = 1;");

            migrationBuilder.DropColumn(
                name: "HelpText",
                table: "CustomFieldRevision");

            migrationBuilder.DropColumn(
                name: "Section",
                table: "CustomFieldRevision");

            migrationBuilder.RenameColumn(
                name: "Label",
                table: "CustomFieldRevision",
                newName: "FieldName");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomFieldRevision_FieldName",
                table: "CustomFieldRevision",
                sql: "LEN(LTRIM(RTRIM([FieldName]))) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomFieldRevision_MaxLength",
                table: "CustomFieldRevision",
                sql: "[MaxLength] BETWEEN 1 AND 4000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomFieldDefinition_DataType",
                table: "CustomFieldDefinition",
                sql: "[DataType] IN (0, 2, 3, 4, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomFieldRevision_FieldName",
                table: "CustomFieldRevision");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomFieldRevision_MaxLength",
                table: "CustomFieldRevision");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomFieldDefinition_DataType",
                table: "CustomFieldDefinition");

            migrationBuilder.RenameColumn(
                name: "FieldName",
                table: "CustomFieldRevision",
                newName: "Label");

            migrationBuilder.AddColumn<string>(
                name: "HelpText",
                table: "CustomFieldRevision",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "CustomFieldRevision",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");
        }
    }
}
