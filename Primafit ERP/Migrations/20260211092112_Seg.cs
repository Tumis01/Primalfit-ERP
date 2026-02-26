using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class Seg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            

            migrationBuilder.CreateTable(
                name: "SegAccountTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsBalanceSheet = table.Column<bool>(type: "bit", nullable: false),
                    IsDebit = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegAccountTypes", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SegAccountTypes",
                columns: new[] { "Id", "Description", "IsBalanceSheet", "IsDebit" },
                values: new object[,]
                {
                    { 1, "Cash and Cash Equivalents", true, true },
                    { 2, "Other Expense", false, true },
                    { 3, "Other Current Asset", true, true },
                    { 4, "Other Income", false, false },
                    { 5, "Other Current Liability", true, false },
                    { 6, "Non Current Liability", true, false },
                    { 7, "Share Capital", true, false },
                    { 8, "Property, Plant and Equipment", true, true },
                    { 9, "Revenue", false, false },
                    { 10, "Cost of Sales", false, true },
                    { 11, "Retained Earnings", true, false },
                    { 12, "Tax Expense", false, true },
                    { 13, "Unallocated IS", false, true },
                    { 14, "Dividends Paid", false, true },
                    { 15, "Dividends Received", false, false },
                    { 16, "Profit/Loss on Sale of Non-Current Asset", false, false },
                    { 17, "Finance Cost", false, true },
                    { 18, "Profit/Loss On Exchange", false, false },
                    { 19, "Unallocated BS", true, true },
                    { 20, "Shareholders Loan", true, false },
                    { 21, "Other Non Current Liability", true, false },
                    { 22, "Investments", true, true },
                    { 23, "Other Fixed Assets", true, true },
                    { 24, "Inventories", true, true },
                    { 25, "Trade Receivables", true, true },
                    { 26, "Trade Payables", true, false },
                    { 27, "Taxation Liability", true, false },
                    { 28, "Deferred Tax", true, false },
                    { 29, "Non Distributable Reserves", true, false },
                    { 30, "Other Non Current Asset", true, true },
                    { 31, "Intangible Asset", true, true },
                    { 32, "Other Comprehensive Income", false, false },
                    { 33, "Investment Property", true, true },
                    { 34, "Financial Asset", true, true },
                    { 35, "Distribution Cost", false, true },
                    { 36, "Administration Expense", false, true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            

            migrationBuilder.CreateTable(
                name: "AccountTypes1",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    IsBalanceSheet = table.Column<bool>(type: "bit", nullable: false),
                    IsDebit = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountTypes1", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SegmentDefinitions",
                columns: table => new
                {
                    SegmentNumber = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Length = table.Column<int>(type: "int", nullable: false),
                    SegmentName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentDefinitions", x => x.SegmentNumber);
                });

            migrationBuilder.CreateTable(
                name: "SegmentValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SegmentNumber = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MainAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountType1Id = table.Column<int>(type: "int", nullable: false),
                    AccountCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MainAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MainAccounts_AccountTypes1_AccountType1Id",
                        column: x => x.AccountType1Id,
                        principalTable: "AccountTypes1",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SegmentedAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MainAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Segment2ValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Segment3ValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Segment4ValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Segment5ValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Segment6ValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountCodeString = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentedAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SegmentedAccounts_MainAccounts_MainAccountId",
                        column: x => x.MainAccountId,
                        principalTable: "MainAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SegmentedAccounts_SegmentValues_Segment2ValueId",
                        column: x => x.Segment2ValueId,
                        principalTable: "SegmentValues",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SegmentedAccounts_SegmentValues_Segment3ValueId",
                        column: x => x.Segment3ValueId,
                        principalTable: "SegmentValues",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SegmentedAccounts_SegmentValues_Segment4ValueId",
                        column: x => x.Segment4ValueId,
                        principalTable: "SegmentValues",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SegmentedAccounts_SegmentValues_Segment5ValueId",
                        column: x => x.Segment5ValueId,
                        principalTable: "SegmentValues",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SegmentedAccounts_SegmentValues_Segment6ValueId",
                        column: x => x.Segment6ValueId,
                        principalTable: "SegmentValues",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_SKU",
                table: "Items",
                columns: new[] { "CompanyId", "SKU" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MainAccounts_AccountType1Id",
                table: "MainAccounts",
                column: "AccountType1Id");

            migrationBuilder.CreateIndex(
                name: "IX_MainAccounts_CompanyId_AccountCode",
                table: "MainAccounts",
                columns: new[] { "CompanyId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SegmentedAccounts_CompanyId_AccountCodeString",
                table: "SegmentedAccounts",
                columns: new[] { "CompanyId", "AccountCodeString" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SegmentedAccounts_MainAccountId",
                table: "SegmentedAccounts",
                column: "MainAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SegmentedAccounts_Segment2ValueId",
                table: "SegmentedAccounts",
                column: "Segment2ValueId");

            migrationBuilder.CreateIndex(
                name: "IX_SegmentedAccounts_Segment3ValueId",
                table: "SegmentedAccounts",
                column: "Segment3ValueId");

            migrationBuilder.CreateIndex(
                name: "IX_SegmentedAccounts_Segment4ValueId",
                table: "SegmentedAccounts",
                column: "Segment4ValueId");

            migrationBuilder.CreateIndex(
                name: "IX_SegmentedAccounts_Segment5ValueId",
                table: "SegmentedAccounts",
                column: "Segment5ValueId");

            migrationBuilder.CreateIndex(
                name: "IX_SegmentedAccounts_Segment6ValueId",
                table: "SegmentedAccounts",
                column: "Segment6ValueId");

            migrationBuilder.CreateIndex(
                name: "IX_SegmentValues_CompanyId_SegmentNumber_Value",
                table: "SegmentValues",
                columns: new[] { "CompanyId", "SegmentNumber", "Value" },
                unique: true);
        }
    }
}
