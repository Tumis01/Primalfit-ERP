using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PrimafitERP.Data;

namespace Primafit_ERP.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261102120000_TransactionTemplateModules")]
public class TransactionTemplateModules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<int>(
        name: "Module", table: "CustomTransactionTypes", type: "int", nullable: false, defaultValue: 0);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "Module", table: "CustomTransactionTypes");
}
