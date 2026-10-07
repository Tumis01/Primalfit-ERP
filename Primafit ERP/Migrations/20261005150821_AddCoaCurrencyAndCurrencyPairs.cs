using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddCoaCurrencyAndCurrencyPairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CurrencyManagements_Currencies_CurrencyId",
                table: "CurrencyManagements");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                table: "SegChartOfAccounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeCurrencyId",
                table: "CurrencyManagements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBaseCurrency",
                table: "Currencies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Keep deployed tenant currency data and transaction histories intact while
            // identifying each company's configured base currency. Existing rate rows
            // are linked to their prior target currency where it can be resolved by code.
            migrationBuilder.Sql("""
                ;WITH RankedBaseCurrencies AS
                (
                    SELECT c.Id,
                           c.CompanyId,
                           ROW_NUMBER() OVER (PARTITION BY c.CompanyId ORDER BY c.Id) AS RowNumber
                    FROM Currencies c
                    INNER JOIN CompanyDetails company ON company.CompanyDetailsId = c.CompanyId
                    WHERE UPPER(LTRIM(RTRIM(c.CurrencyCode))) = UPPER(LTRIM(RTRIM(company.FunctionalCurrency)))
                      AND NULLIF(LTRIM(RTRIM(company.FunctionalCurrency)), N'') IS NOT NULL
                )
                UPDATE c
                   SET c.IsBaseCurrency = CASE WHEN ranked.RowNumber = 1 THEN 1 ELSE 0 END,
                       c.CurrencyName = CASE WHEN ranked.RowNumber = 1 AND NULLIF(LTRIM(RTRIM(company.BaseCurrency)), N'') IS NOT NULL
                                             THEN company.BaseCurrency ELSE c.CurrencyName END
                FROM Currencies c
                INNER JOIN RankedBaseCurrencies ranked ON ranked.Id = c.Id
                INNER JOIN CompanyDetails company ON company.CompanyDetailsId = c.CompanyId;

                INSERT INTO Currencies (Id, CompanyId, CurrencyName, CurrencyCode, IsBaseCurrency)
                SELECT NEWID(), company.CompanyDetailsId, LTRIM(RTRIM(company.BaseCurrency)),
                       UPPER(LTRIM(RTRIM(company.FunctionalCurrency))), 1
                FROM CompanyDetails company
                WHERE NULLIF(LTRIM(RTRIM(company.BaseCurrency)), N'') IS NOT NULL
                  AND NULLIF(LTRIM(RTRIM(company.FunctionalCurrency)), N'') IS NOT NULL
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM Currencies c
                      WHERE c.CompanyId = company.CompanyDetailsId
                        AND UPPER(LTRIM(RTRIM(c.CurrencyCode))) = UPPER(LTRIM(RTRIM(company.FunctionalCurrency)))
                  );

                UPDATE rate
                   SET rate.ExchangeCurrencyId = targetCurrency.Id
                FROM CurrencyManagements rate
                INNER JOIN Currencies targetCurrency
                    ON targetCurrency.CompanyId = rate.CompanyId
                   AND UPPER(LTRIM(RTRIM(targetCurrency.CurrencyCode))) = UPPER(LTRIM(RTRIM(rate.ExchangeCurrency)))
                WHERE rate.ExchangeCurrencyId IS NULL;

                INSERT INTO CurrencyManagements (Id, CompanyId, CurrencyId, ExchangeCurrency, ExchangeCurrencyId, Date, Rate)
                SELECT NEWID(), currency.CompanyId, currency.Id, currency.CurrencyCode, currency.Id,
                       CAST(COALESCE(NULLIF(company.CreatedDate, '0001-01-01T00:00:00'), SYSUTCDATETIME()) AS date), 1.0000
                FROM Currencies currency
                INNER JOIN CompanyDetails company ON company.CompanyDetailsId = currency.CompanyId
                WHERE currency.IsBaseCurrency = 1
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM CurrencyManagements rate
                      WHERE rate.CompanyId = currency.CompanyId
                        AND rate.CurrencyId = currency.Id
                        AND rate.ExchangeCurrencyId = currency.Id
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SegChartOfAccounts_CurrencyId",
                table: "SegChartOfAccounts",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyManagements_ExchangeCurrencyId",
                table: "CurrencyManagements",
                column: "ExchangeCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_CompanyId",
                table: "Currencies",
                column: "CompanyId",
                unique: true,
                filter: "[IsBaseCurrency] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_CurrencyManagements_Currencies_CurrencyId",
                table: "CurrencyManagements",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CurrencyManagements_Currencies_ExchangeCurrencyId",
                table: "CurrencyManagements",
                column: "ExchangeCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SegChartOfAccounts_Currencies_CurrencyId",
                table: "SegChartOfAccounts",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CurrencyManagements_Currencies_CurrencyId",
                table: "CurrencyManagements");

            migrationBuilder.DropForeignKey(
                name: "FK_CurrencyManagements_Currencies_ExchangeCurrencyId",
                table: "CurrencyManagements");

            migrationBuilder.DropForeignKey(
                name: "FK_SegChartOfAccounts_Currencies_CurrencyId",
                table: "SegChartOfAccounts");

            migrationBuilder.DropIndex(
                name: "IX_SegChartOfAccounts_CurrencyId",
                table: "SegChartOfAccounts");

            migrationBuilder.DropIndex(
                name: "IX_CurrencyManagements_ExchangeCurrencyId",
                table: "CurrencyManagements");

            migrationBuilder.DropIndex(
                name: "IX_Currencies_CompanyId",
                table: "Currencies");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "SegChartOfAccounts");

            migrationBuilder.DropColumn(
                name: "ExchangeCurrencyId",
                table: "CurrencyManagements");

            migrationBuilder.DropColumn(
                name: "IsBaseCurrency",
                table: "Currencies");

            migrationBuilder.AddForeignKey(
                name: "FK_CurrencyManagements_Currencies_CurrencyId",
                table: "CurrencyManagements",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
