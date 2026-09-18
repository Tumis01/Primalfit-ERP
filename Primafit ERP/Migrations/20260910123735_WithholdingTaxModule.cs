using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class WithholdingTaxModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WithholdingTaxes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    PercentageValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithholdingGlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    DefaultKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WithholdingTaxes", x => x.Id));

            migrationBuilder.AddColumn<Guid?>("WithholdingTaxId", "PurchaseOrders", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>("WithholdingTaxName", "PurchaseOrders", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<decimal>("WithholdingPercentage", "PurchaseOrders", type: "decimal(18,4)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>("WithholdingAmountForeign", "PurchaseOrders", type: "decimal(18,4)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>("WithholdingAmount", "PurchaseOrders", type: "decimal(18,4)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<Guid?>("WithholdingGlAccountId", "PurchaseOrders", type: "uniqueidentifier", nullable: true);

            migrationBuilder.AddColumn<Guid?>("WithholdingTaxId", "VendorBills", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>("WithholdingTaxName", "VendorBills", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<decimal>("WithholdingPercentage", "VendorBills", type: "decimal(18,4)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>("WithholdingAmountForeign", "VendorBills", type: "decimal(18,4)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>("WithholdingAmount", "VendorBills", type: "decimal(18,4)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<Guid?>("WithholdingGlAccountId", "VendorBills", type: "uniqueidentifier", nullable: true);

            migrationBuilder.AddColumn<decimal>("WithholdingAmountForeign", "VendorPayments", type: "decimal(18,4)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>("WithholdingAmount", "VendorPayments", type: "decimal(18,4)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<Guid?>("WithholdingTaxId", "VendorPayments", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>("WithholdingTaxName", "VendorPayments", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<decimal>("WithholdingPercentage", "VendorPayments", type: "decimal(18,4)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<Guid?>("WithholdingGlAccountId", "VendorPayments", type: "uniqueidentifier", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WithholdingTaxes");
            migrationBuilder.DropColumn("WithholdingTaxId", "PurchaseOrders");
            migrationBuilder.DropColumn("WithholdingTaxName", "PurchaseOrders");
            migrationBuilder.DropColumn("WithholdingPercentage", "PurchaseOrders");
            migrationBuilder.DropColumn("WithholdingAmountForeign", "PurchaseOrders");
            migrationBuilder.DropColumn("WithholdingAmount", "PurchaseOrders");
            migrationBuilder.DropColumn("WithholdingGlAccountId", "PurchaseOrders");
            migrationBuilder.DropColumn("WithholdingTaxId", "VendorBills");
            migrationBuilder.DropColumn("WithholdingTaxName", "VendorBills");
            migrationBuilder.DropColumn("WithholdingPercentage", "VendorBills");
            migrationBuilder.DropColumn("WithholdingAmountForeign", "VendorBills");
            migrationBuilder.DropColumn("WithholdingAmount", "VendorBills");
            migrationBuilder.DropColumn("WithholdingGlAccountId", "VendorBills");
            migrationBuilder.DropColumn("WithholdingAmountForeign", "VendorPayments");
            migrationBuilder.DropColumn("WithholdingAmount", "VendorPayments");
            migrationBuilder.DropColumn("WithholdingTaxId", "VendorPayments");
            migrationBuilder.DropColumn("WithholdingTaxName", "VendorPayments");
            migrationBuilder.DropColumn("WithholdingPercentage", "VendorPayments");
            migrationBuilder.DropColumn("WithholdingGlAccountId", "VendorPayments");
        }
    }
}
