using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data.Seed;
using System.Reflection.Emit;

namespace PrimafitERP.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // --- MASTER DATA ---
        public DbSet<CompanyDetails> CompanyDetails { get; set; }
        public DbSet<Currency> Currencies { get; set; }
        public DbSet<CurrencyManagement> CurrencyManagements { get; set; }
        public DbSet<Tax> Taxes { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<Warehouse> Warehouses { get; set; }

        // --- FINANCE (GL) ---
        public DbSet<AccountingPeriod> AccountingPeriods { get; set; }
        public DbSet<GLAccountType> GLAccountTypes { get; set; }
        public DbSet<GLMainAccount> GLMainAccounts { get; set; }
        public DbSet<GLChartOfAccount> GLChartOfAccounts { get; set; }
        public DbSet<GLBatch> GLBatches { get; set; }
        public DbSet<GLJournalHeader> GLJournalHeaders { get; set; }
        public DbSet<GLJournalLine> GLJournalLines { get; set; }
        public DbSet<GLTransaction> GLTransactions { get; set; }
        public DbSet<BankReconciliation> BankReconciliations { get; set; }
        public DbSet<BankStatementLine> BankStatementLines { get; set; }

        // --- SUPPLY CHAIN ---
        public DbSet<Item> Items { get; set; }
        public DbSet<UnitOfMeasure> UnitOfMeasures { get; set; }
        public DbSet<StockLedger> StockLedgers { get; set; }
        public DbSet<StockTransfer> StockTransfers { get; set; }
        public DbSet<SalesOrder> SalesOrders { get; set; }
        public DbSet<SalesOrderLine> SalesOrderLines { get; set; }
        public DbSet<CustomerPayment> CustomerPayments { get; set; }
        public DbSet<PaymentApplication> PaymentApplications { get; set; }
        public DbSet<VendorBill> VendorBills { get; set; }
        public DbSet<VendorBillLine> VendorBillLines { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Vendor> Vendors { get; set; }
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
        public DbSet<GoodsReceipt> GoodsReceipts { get; set; }
        public DbSet<GoodsReceiptLine> GoodsReceiptLines { get; set; }
        public DbSet<SalesInvoice> SalesInvoices { get; set; }
        public DbSet<SalesInvoiceLine> SalesInvoiceLines { get; set; }
        public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; }
        public DbSet<CashbookBatch> CashbookBatches { get; set; }
        public DbSet<CashbookEntry> CashbookEntries { get; set; }
        public DbSet<FinancialDashboardSnapshot> FinancialDashboardSnapshots { get; set; }
        public DbSet<ItemCostHistory> ItemCostHistories { get; set; }
        public DbSet<WaccHistory> WaccHistories { get; set; }
        public DbSet<FixedAsset> FixedAssets { get; set; }
        public DbSet<AssetDepreciationHistory> AssetDepreciationHistories { get; set; }
        public DbSet<BudgetHeader> BudgetHeaders { get; set; }
        public DbSet<BudgetLine> BudgetLines { get; set; }
        public DbSet<ParsedStatementRow> ParsedStatementRows { get; set; }
        public DbSet<Segment0> Segment0s { get; set; }
        public DbSet<Segment1> Segment1s { get; set; }
        public DbSet<Segment2> Segment2s { get; set; }
        public DbSet<Segment3> Segment3s { get; set; }
        public DbSet<Segment4> Segment4s { get; set; }
        public DbSet<Segment5> Segment5s { get; set; }
        public DbSet<PurchaseReturn> PurchaseReturns { get; set; }
        public DbSet<PurchaseReturnLine> PurchaseReturnLines { get; set; }
        public DbSet<CreditNote> CreditNotes { get; set; }
        public DbSet<CreditNoteLine> CreditNoteLines { get; set; }

        public DbSet<SegCoaConfig> SegCoaConfigs => Set<SegCoaConfig>();
        public DbSet<SegChartOfAccount> SegChartOfAccounts => Set<SegChartOfAccount>();
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<CustomerGroup> CustomerGroups { get; set; }
        public DbSet<VendorGroup> VendorGroups { get; set; }
        public DbSet<ItemCategory> ItemCategories { get; set; }
        public DbSet<GrnLandedCost> GrnLandedCosts { get; set; }
        public DbSet<AssetCategory> AssetCategories { get; set; }
        public DbSet<AssetUsageLog> AssetUsageLogs { get; set; }
        public DbSet<SalesShipment> SalesShipments { get; set; }
        public DbSet<SalesShipmentLine> SalesShipmentLines { get; set; }
        // HR & Payroll
        public DbSet<Branch> Branches { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<JobRole> JobRoles { get; set; }
        public DbSet<EmployeeSalaryStructure> SalaryStructures { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<PayrollRun> PayrollRuns { get; set; }
        public DbSet<PayrollItem> PayrollItems { get; set; }
        public DbSet<PayrollSetting> PayrollSettings { get; set; }
        public DbSet<PayrollEarning> PayrollEarnings { get; set; }
        public DbSet<PayrollDeduction> PayrollDeductions { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            foreach (var property in builder.Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                property.SetColumnType("decimal(18, 6)");
            }

            foreach (var relationship in builder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            {
                if (relationship.PrincipalEntityType.ClrType == typeof(CompanyDetails))
                {
                    relationship.DeleteBehavior = DeleteBehavior.Restrict;
                }
            }

            // ... inside OnModelCreating ...

            // --- CREDIT NOTE SAFE CONFIGURATION ---
            // We explicitly disable cascading deletes here to prevent SQL Server Error 1785

            builder.Entity<CreditNote>(entity =>
            {
                
                entity.HasOne(c => c.Customer)
                      .WithMany()
                      .HasForeignKey(c => c.CustomerId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(c => c.SalesOrder)
                      .WithMany()
                      .HasForeignKey(c => c.SalesOrderId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(c => c.Warehouse)
                      .WithMany()
                      .HasForeignKey(c => c.WarehouseId)
                      .OnDelete(DeleteBehavior.NoAction);

                // 4. Ensure Unique Number per Company
                entity.HasIndex(c => new { c.CompanyId, c.CreditNoteNumber })
                      .IsUnique();
            });

            // Credit Note Lines can cascade (if header dies, lines die)
            builder.Entity<CreditNoteLine>()
                   .HasOne(l => l.Header)
                   .WithMany(h => h.Lines)
                   .HasForeignKey(l => l.HeaderId)
                   .OnDelete(DeleteBehavior.Cascade);

            // ... rest of your code ...

            builder.Entity<PurchaseReturnLine>()
                .HasOne(l => l.VendorBillLine)
                .WithMany()
                .HasForeignKey(l => l.VendorBillLineId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<PurchaseReturnLine>()
                .HasOne(l => l.PurchaseReturn)
                .WithMany(r => r.Lines)
                .HasForeignKey(l => l.PurchaseReturnId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<GLMainAccount>()
                .HasOne(m => m.AccountType)
                .WithMany()
                .HasForeignKey(m => m.AccountTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<GLChartOfAccount>()
                .HasOne(c => c.MainAccount)
                .WithMany()
                .HasForeignKey(c => c.MainAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<GLJournalHeader>()
                .HasOne(h => h.Batch)
                .WithMany(b => b.Journals)
                .HasForeignKey(h => h.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<GLJournalLine>()
                .HasOne(l => l.Header)
                .WithMany(h => h.Lines)
                .HasForeignKey(l => l.HeaderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<StockTransfer>()
                .HasOne(t => t.FromWarehouse)
                .WithMany()
                .HasForeignKey(t => t.FromWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<StockTransfer>()
                .HasOne(t => t.ToWarehouse)
                .WithMany()
                .HasForeignKey(t => t.ToWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SalesOrder>()
                .HasOne(s => s.Customer)
                .WithMany()
                .HasForeignKey(s => s.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SalesOrderLine>()
                .HasOne(l => l.Item)
                .WithMany()
                .HasForeignKey(l => l.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SalesOrder>()
                .HasIndex(s => new { s.CompanyId, s.OrderNumber })
                .IsUnique();

            builder.ApplyConfiguration(new SegAccountTypeSeed());
            builder.Entity<SegChartOfAccount>()
                .HasIndex(x => new { x.CompanyId, x.AccountCode })
                .IsUnique();

            builder.Entity<SegCoaConfig>()
                .HasIndex(x => x.CompanyId)
                .IsUnique();

            builder.Entity<Segment0>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<Segment1>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<Segment2>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<Segment3>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<Segment4>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<Segment5>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.Entity is not AuditLog && (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted));

            foreach (var entry in entries)
            {
                // Auditing logic placeholder
            }

            return await base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            BlockSegAccountTypeChanges();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            BlockSegAccountTypeChanges();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void BlockSegAccountTypeChanges()
        {
            var blocked = ChangeTracker.Entries<SegAccountType>()
                .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted || e.State == EntityState.Added)
                .ToList();

            if (blocked.Any())
                throw new InvalidOperationException("SegAccountTypes is fixed lookup data and cannot be added/edited/deleted.");
        }
    }
}