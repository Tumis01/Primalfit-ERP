using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class Segcoacre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment5s",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Segment5s",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment4s",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Segment4s",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment3s",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Segment3s",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment2s",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Segment2s",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment1s",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Segment1s",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment0s",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Segment0s",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "SegChartOfAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Segment0Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Segment1Id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Segment2Id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Segment3Id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Segment4Id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Segment5Id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SegAccountTypeId = table.Column<int>(type: "int", nullable: false),
                    AllowJournal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegChartOfAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SegChartOfAccounts_CompanyDetails_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "CompanyDetails",
                        principalColumn: "CompanyDetailsId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SegCoaConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Segment0Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Segment1Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Segment2Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Segment3Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Segment4Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Segment5Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Segment1Active = table.Column<bool>(type: "bit", nullable: false),
                    Segment2Active = table.Column<bool>(type: "bit", nullable: false),
                    Segment3Active = table.Column<bool>(type: "bit", nullable: false),
                    Segment4Active = table.Column<bool>(type: "bit", nullable: false),
                    Segment5Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegCoaConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SegCoaConfigs_CompanyDetails_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "CompanyDetails",
                        principalColumn: "CompanyDetailsId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Segment5s_CompanyId_Code",
                table: "Segment5s",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Segment4s_CompanyId_Code",
                table: "Segment4s",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Segment3s_CompanyId_Code",
                table: "Segment3s",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Segment2s_CompanyId_Code",
                table: "Segment2s",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Segment1s_CompanyId_Code",
                table: "Segment1s",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Segment0s_CompanyId_Code",
                table: "Segment0s",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SegChartOfAccounts_CompanyId_AccountCode",
                table: "SegChartOfAccounts",
                columns: new[] { "CompanyId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SegCoaConfigs_CompanyId",
                table: "SegCoaConfigs",
                column: "CompanyId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Segment0s_CompanyDetails_CompanyId",
                table: "Segment0s",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Segment1s_CompanyDetails_CompanyId",
                table: "Segment1s",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Segment2s_CompanyDetails_CompanyId",
                table: "Segment2s",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Segment3s_CompanyDetails_CompanyId",
                table: "Segment3s",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Segment4s_CompanyDetails_CompanyId",
                table: "Segment4s",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Segment5s_CompanyDetails_CompanyId",
                table: "Segment5s",
                column: "CompanyId",
                principalTable: "CompanyDetails",
                principalColumn: "CompanyDetailsId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Segment0s_CompanyDetails_CompanyId",
                table: "Segment0s");

            migrationBuilder.DropForeignKey(
                name: "FK_Segment1s_CompanyDetails_CompanyId",
                table: "Segment1s");

            migrationBuilder.DropForeignKey(
                name: "FK_Segment2s_CompanyDetails_CompanyId",
                table: "Segment2s");

            migrationBuilder.DropForeignKey(
                name: "FK_Segment3s_CompanyDetails_CompanyId",
                table: "Segment3s");

            migrationBuilder.DropForeignKey(
                name: "FK_Segment4s_CompanyDetails_CompanyId",
                table: "Segment4s");

            migrationBuilder.DropForeignKey(
                name: "FK_Segment5s_CompanyDetails_CompanyId",
                table: "Segment5s");

            migrationBuilder.DropTable(
                name: "SegChartOfAccounts");

            migrationBuilder.DropTable(
                name: "SegCoaConfigs");

            migrationBuilder.DropIndex(
                name: "IX_Segment5s_CompanyId_Code",
                table: "Segment5s");

            migrationBuilder.DropIndex(
                name: "IX_Segment4s_CompanyId_Code",
                table: "Segment4s");

            migrationBuilder.DropIndex(
                name: "IX_Segment3s_CompanyId_Code",
                table: "Segment3s");

            migrationBuilder.DropIndex(
                name: "IX_Segment2s_CompanyId_Code",
                table: "Segment2s");

            migrationBuilder.DropIndex(
                name: "IX_Segment1s_CompanyId_Code",
                table: "Segment1s");

            migrationBuilder.DropIndex(
                name: "IX_Segment0s_CompanyId_Code",
                table: "Segment0s");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Segment5s");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Segment4s");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Segment3s");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Segment2s");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Segment1s");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Segment0s");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment5s",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment4s",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment3s",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment2s",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment1s",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Segment0s",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
